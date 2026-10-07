using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.Services;

public sealed class RegistrationNotificationService
{
	private sealed record SmtpCredentials(string Host, int Port, bool EnableSsl, string UserName, string Password, string FromAddress);

	public const string FromDisplayName = "PharmaBill Team";

	public const string FromAddress = "pharma.bill26@gmail.com";

	public const string Subject = "Welcome to PharmaBill – Your User Manual & Quick Start Guide";

	private readonly IServiceScopeFactory _scopeFactory;

	private readonly DocumentOutputSettingsStore _documentOutputSettings;

	private readonly UserManualEmailQueueStore _queue;

	private readonly ILogger<RegistrationNotificationService> _logger;

	private readonly object _gate = new object();

	private bool _networkHooked;

	public RegistrationNotificationService(IServiceScopeFactory scopeFactory, DocumentOutputSettingsStore documentOutputSettings, UserManualEmailQueueStore queue, ILogger<RegistrationNotificationService> logger)
	{
		_scopeFactory = scopeFactory;
		_documentOutputSettings = documentOutputSettings;
		_queue = queue;
		_logger = logger;
	}

	public void ScheduleWelcomeManualEmail(string? recipientEmail, string pharmacyName, string ownerName, bool forceResend = false)
	{
		EnsureNetworkHook();
		Task.Run(async () =>
		{
			try
			{
				await SendWelcomeManualAsync(recipientEmail, pharmacyName, ownerName, forceResend);
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Background welcome-manual email failed.");
			}
		});
	}

	public async Task<string> SendWelcomeManualAsync(string? recipientEmail, string pharmacyName, string ownerName, bool forceResend = false, CancellationToken cancellationToken = default(CancellationToken))
	{
		string email = (recipientEmail ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
		{
			return "Enter a pharmacy email address before sending the user manual.";
		}
		using IServiceScope scope = _scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		PharmacyProfile profile = await context.PharmacyProfiles.OrderBy((PharmacyProfile item) => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
		if (profile == null)
		{
			return "Pharmacy profile is not available yet.";
		}
		if (!forceResend && profile.UserManualEmailSent)
		{
			DateTime? userManualSentAtUtc = profile.UserManualSentAtUtc;
			string result;
			if (userManualSentAtUtc.HasValue)
			{
				DateTime valueOrDefault = userManualSentAtUtc.GetValueOrDefault();
				result = $"User manual email was already sent on {valueOrDefault.ToLocalTime():dd-MMM-yyyy HH:mm}.";
			}
			else
			{
				result = "User manual email was already sent.";
			}
			return result;
		}
		if (!NetworkInterface.GetIsNetworkAvailable())
		{
			_queue.Enqueue(new PendingUserManualEmail(email, pharmacyName, ownerName, DateTime.UtcNow, forceResend));
			return "No internet connection. The welcome email is queued and will send automatically when you are online.";
		}
		try
		{
			string pdfPath = UserManualPdfBuilder.EnsurePdfPath();
			SmtpCredentials credentials = ResolveSmtpCredentials();
			await SendSmtpAsync(credentials, email, pharmacyName, ownerName, pdfPath, cancellationToken);
			profile.UserManualEmailSent = true;
			profile.UserManualSentAtUtc = DateTime.UtcNow;
			profile.UpdatedAtUtc = DateTime.UtcNow;
			await context.SaveChangesAsync(cancellationToken);
			_queue.Clear();
			_logger.LogInformation("Welcome user-manual email sent to {Email}.", email);
			return "User manual emailed to " + email + ".";
		}
		catch (Exception ex) when ((ex is SmtpException || ex is IOException || ex is InvalidOperationException) ? true : false)
		{
			_queue.Enqueue(new PendingUserManualEmail(email, pharmacyName, ownerName, DateTime.UtcNow, forceResend));
			_logger.LogWarning(ex, "Welcome email failed; queued for retry.");
			return "Could not send email yet (" + ex.Message + "). It is queued for automatic retry when online.";
		}
	}

	public async Task FlushPendingAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		PendingUserManualEmail pendingUserManualEmail = _queue.Peek();
		if ((object)pendingUserManualEmail != null && NetworkInterface.GetIsNetworkAvailable())
		{
			await SendWelcomeManualAsync(pendingUserManualEmail.RecipientEmail, pendingUserManualEmail.PharmacyName, pendingUserManualEmail.OwnerName, pendingUserManualEmail.ForceResend, cancellationToken);
		}
	}

	public void EnsureNetworkHook()
	{
		lock (_gate)
		{
			if (_networkHooked)
			{
				return;
			}
			NetworkChange.NetworkAvailabilityChanged += (object? _, NetworkAvailabilityEventArgs args) =>
			{
				if (args.IsAvailable)
				{
					_ = Task.Run(async () =>
					{
						try
						{
							await FlushPendingAsync();
						}
						catch (Exception exception)
						{
							_logger.LogDebug(exception, "Pending welcome-email flush failed.");
						}
					});
				}
			};
			_networkHooked = true;
		}
	}

	private async Task SendSmtpAsync(SmtpCredentials credentials, string recipientEmail, string pharmacyName, string ownerName, string pdfPath, CancellationToken cancellationToken)
	{
		using MailMessage message = new MailMessage
		{
			From = new MailAddress(credentials.FromAddress, "PharmaBill Team"),
			Subject = "Welcome to PharmaBill – Your User Manual & Quick Start Guide",
			Body = BuildPlainBody(pharmacyName, ownerName),
			IsBodyHtml = false
		};
		message.To.Add(new MailAddress(recipientEmail, ownerName));
		message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(BuildHtmlBody(pharmacyName, ownerName), null, "text/html"));
		message.Attachments.Add(new Attachment(pdfPath));
		using SmtpClient client = new SmtpClient(credentials.Host, credentials.Port)
		{
			EnableSsl = credentials.EnableSsl,
			DeliveryMethod = SmtpDeliveryMethod.Network,
			UseDefaultCredentials = false,
			Credentials = new NetworkCredential(credentials.UserName, credentials.Password)
		};
		await client.SendMailAsync(message, cancellationToken);
	}

	private SmtpCredentials ResolveSmtpCredentials()
	{
		string text = Environment.GetEnvironmentVariable("PHARMABILL_WELCOME_SMTP_USER") ?? Environment.GetEnvironmentVariable("PHARMABILL_SMTP_USER");
		string text2 = Environment.GetEnvironmentVariable("PHARMABILL_WELCOME_SMTP_PASSWORD") ?? Environment.GetEnvironmentVariable("PHARMABILL_SMTP_PASSWORD");
		if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2))
		{
			return new SmtpCredentials("smtp.gmail.com", 587, EnableSsl: true, text.Trim(), text2, "pharma.bill26@gmail.com");
		}
		DocumentOutputSettings documentOutputSettings = _documentOutputSettings.Load();
		string text3 = _documentOutputSettings.ReadSmtpPassword();
		if (!string.IsNullOrWhiteSpace(documentOutputSettings.SmtpHost) && !string.IsNullOrWhiteSpace(documentOutputSettings.SmtpUserName) && !string.IsNullOrWhiteSpace(text3))
		{
			string fromAddress = (string.IsNullOrWhiteSpace(documentOutputSettings.SmtpFromAddress) ? "pharma.bill26@gmail.com" : documentOutputSettings.SmtpFromAddress.Trim());
			return new SmtpCredentials(documentOutputSettings.SmtpHost.Trim(), (documentOutputSettings.SmtpPort <= 0) ? 587 : documentOutputSettings.SmtpPort, documentOutputSettings.SmtpEnableSsl, documentOutputSettings.SmtpUserName.Trim(), text3, fromAddress);
		}
		throw new InvalidOperationException("Configure Gmail SMTP to send the user manual: set PHARMABILL_WELCOME_SMTP_USER and PHARMABILL_WELCOME_SMTP_PASSWORD (App Password), or save SMTP settings under Settings → Print & invoice.");
	}

	private static string BuildPlainBody(string pharmacyName, string ownerName)
	{
		string value = (string.IsNullOrWhiteSpace(ownerName) ? "there" : ownerName.Trim());
		string value2 = (string.IsNullOrWhiteSpace(pharmacyName) ? "your pharmacy" : pharmacyName.Trim());
		return $"Dear {value},\n\nWelcome to PharmaBill — thank you for registering {value2}.\n\n" + "Your complete User Manual & Quick Start Guide is attached as PharmaBill_User_Manual.pdf.\n\nQuick start:\n1) Local Wi-Fi Sync (Port 5055): Connect Android and Desktop on the same Wi-Fi, open Sync Station, scan the QR (or wait for discovery). If blocked, allow firewall port 5055; Drive cloud fallback still works.\n2) Google Drive backups: Configure Keys → Link Google Account → enable Sync on Exit / Bill Save.\n3) Catalog, FEFO batches & GST: Import medicines, sell FEFO batches, use retail cash memo or wholesale tax invoices (licensed buyers only; rate ≤ MRP).\n\nDesigned and developed by S.A.E.R. to serve pharma professionals. For any query: email at pharma.bill26@gmail.com\n\n— PharmaBill Team\n";
	}

	private static string BuildHtmlBody(string pharmacyName, string ownerName)
	{
		string value = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(ownerName) ? "there" : ownerName.Trim());
		string value2 = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(pharmacyName) ? "your pharmacy" : pharmacyName.Trim());
		return $"<html><body style=\"font-family:Segoe UI,Arial,sans-serif;color:#0F172A;line-height:1.5\"><p>Dear {value},</p><p>Welcome to <strong>PharmaBill</strong> — thank you for registering <strong>{value2}</strong>.</p>" + "<p>Your complete <strong>User Manual &amp; Quick Start Guide</strong> is attached (<code>PharmaBill_User_Manual.pdf</code>).</p><h3 style=\"color:#1D4ED8\">Quick start</h3><ol><li><strong>Local Wi-Fi Sync (Port 5055)</strong> — Same Wi-Fi for PC and Android; open Sync Station; scan QR or wait for discovery. Allow firewall port 5055 if needed; Google Drive fallback covers offline LAN.</li><li><strong>Google Drive backups</strong> — Configure Keys → Link Account → Sync on Exit / Bill Save.</li><li><strong>Catalog, FEFO &amp; GST</strong> — Import medicines, FEFO batches, retail or wholesale GST invoicing (licensed buyers; rate ≤ MRP).</li></ol><p style=\"color:#475569\">Designed and developed by S.A.E.R. to serve pharma professionals. For any query: email at <a href=\"mailto:pharma.bill26@gmail.com\">pharma.bill26@gmail.com</a>.</p><p>— PharmaBill Team</p></body></html>";
	}
}

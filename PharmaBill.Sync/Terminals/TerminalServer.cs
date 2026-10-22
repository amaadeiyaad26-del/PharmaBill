using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PharmaBill.Core;
using PharmaBill.Core.Compliance;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.Sync.Terminals;

/// <summary>Print-settings content the server records on terminal bills (the server's settings, not the terminal's).</summary>
public interface ITerminalPrintBrandingProvider
{
	RetailPrintBranding? Current();
}

// The billing counter, served to paired terminal PCs over the shop's LAN. One database, on this PC: terminals
// hold none. Every bill is saved by the same RetailBillingService the desktop uses, so stock and invoice numbers
// are decided here, inside its write transaction, and the shop's licence on this PC governs every call.
public sealed class TerminalServer(IServiceScopeFactory scopeFactory, TerminalDeviceStore devices, TerminalSessions sessions, TerminalServerCertificate certificate, TerminalPairing pairing, ILogger<TerminalServer> logger) : IAsyncDisposable
{
	private const string DeviceItem = "terminal.device";

	private const string SessionItem = "terminal.session";

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	private readonly object _gate = new object();

	private WebApplication? _app;

	private IReadOnlyList<IPAddress> _boundAddresses = Array.Empty<IPAddress>();

	private IReadOnlyList<IPAddress>? _fixedAddresses;

	private int _rebinding;

	public int Port { get; private set; } = TerminalProtocol.DefaultPort;

	public bool IsRunning
	{
		get
		{
			lock (_gate)
			{
				return _app != null;
			}
		}
	}

	public string Fingerprint => certificate.Fingerprint;

	public IReadOnlyList<IPAddress> BoundAddresses => _boundAddresses;

	/// <summary>Starts serving terminals. <paramref name="addresses"/> pins the binding (tests); by default the
	/// shop-network address plus loopback, as for the phone listeners.</summary>
	public async Task StartAsync(int port = TerminalProtocol.DefaultPort, IReadOnlyList<IPAddress>? addresses = null, CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			if (_app != null)
			{
				return;
			}
		}
		_fixedAddresses = addresses;
		IReadOnlyList<IPAddress> bind = addresses ?? LanListener.Addresses();
		var tls = certificate.Get();
		WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.ConfigureKestrel((KestrelServerOptions options) =>
		{
			options.AddServerHeader = false;
			options.Limits.MaxRequestBodySize = TerminalProtocol.MaxPrescriptionBytes + 2L * 1024 * 1024;
			options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
			foreach (IPAddress address in bind)
			{
				options.Listen(address, port, listen => listen.UseHttps(tls));
			}
		});
		WebApplication app = builder.Build();
		app.Use((HttpContext context, Func<Task> next) => LanListener.RefuseBrowsersAsync(context, next));
		app.Use(HandleErrorsAsync);
		MapEndpoints(app);
		try
		{
			await app.StartAsync(cancellationToken);
		}
		catch
		{
			await app.DisposeAsync();
			throw;
		}
		lock (_gate)
		{
			_app = app;
			_boundAddresses = bind;
			Port = app.Urls.Select(url => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Port : 0).FirstOrDefault(p => p > 0) is int bound and > 0 ? bound : port;
		}
		if (addresses == null)
		{
			NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
			NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
			_advertiser.Start(Port, certificate.Fingerprint);
		}
		logger.LogInformation("Terminal server listening on {Addresses}, port {Port}.", string.Join(", ", bind), Port);
	}

	public async Task StopAsync()
	{
		NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
		_advertiser.Stop();
		WebApplication? app;
		lock (_gate)
		{
			app = _app;
			_app = null;
		}
		if (app != null)
		{
			try
			{
				await app.StopAsync(TimeSpan.FromSeconds(3));
			}
			catch (Exception exception)
			{
				logger.LogDebug(exception, "Terminal server stop failed.");
			}
			await app.DisposeAsync();
		}
	}

	/// <summary>Revokes a terminal's key and signs out anyone using it. Its next call is refused.</summary>
	public bool Revoke(Guid deviceId)
	{
		sessions.EndAllFor(deviceId);
		return devices.Revoke(deviceId);
	}

	public ValueTask DisposeAsync() => new ValueTask(StopAsync());

	private void OnNetworkAddressChanged(object? sender, EventArgs e)
	{
		if (_fixedAddresses != null || !IsRunning || LanListener.SameAddresses(_boundAddresses, LanListener.Addresses()) || Interlocked.Exchange(ref _rebinding, 1) == 1)
		{
			return;
		}
		_ = Task.Run(async () =>
		{
			try
			{
				int port = Port;
				await StopAsync();
				await StartAsync(port);
			}
			catch (Exception exception)
			{
				logger.LogWarning(exception, "Terminal server could not follow a network change.");
			}
			finally
			{
				Interlocked.Exchange(ref _rebinding, 0);
			}
		});
	}

	private async Task HandleErrorsAsync(HttpContext http, Func<Task> next)
	{
		try
		{
			await next();
		}
		catch (UnauthorizedAccessException exception)
		{
			await WriteErrorAsync(http, StatusCodes.Status403Forbidden, exception.Message);
		}
		catch (RecordLockedException exception)
		{
			// Older than the record-lock period: the terminal asks for an admin unlock and sends it with the retry.
			await WriteErrorAsync(http, StatusCodes.Status423Locked, exception.Message);
		}
		catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or KeyNotFoundException or JsonException or Microsoft.AspNetCore.Http.BadHttpRequestException)
		{
			// Billing rules speak to the cashier in plain words (stock, MRP, prescription): pass them through.
			await WriteErrorAsync(http, StatusCodes.Status422UnprocessableEntity, exception.Message);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			logger.LogError(exception, "Terminal request {Path} failed.", http.Request.Path);
			await WriteErrorAsync(http, StatusCodes.Status500InternalServerError,
				"The server PC could not finish this request. Try again: a bill is never saved twice for the same request.");
		}
	}

	private static Task WriteErrorAsync(HttpContext http, int status, string message)
	{
		if (http.Response.HasStarted)
		{
			return Task.CompletedTask;
		}
		http.Response.Clear();
		http.Response.StatusCode = status;
		return http.Response.WriteAsJsonAsync(new TerminalError(message), Json);
	}

	private readonly TerminalAdvertiser _advertiser = new TerminalAdvertiser(logger);

	private void MapEndpoints(WebApplication app)
	{
		// Pairing is the one thing a terminal does before it has a key. Both calls are gated by the code open on
		// this server's screen; see TerminalPairing.
		app.MapGet(TerminalProtocol.BasePath + "/pair", async (CancellationToken token) =>
		{
			string? nonce = pairing.ServerNonce;
			return Results.Json(new TerminalPairingChallenge(TerminalProtocol.Version, nonce != null, nonce, TerminalPairingProof.Iterations, Environment.MachineName, await ShopNameAsync(token)), Json);
		});
		app.MapPost(TerminalProtocol.BasePath + "/pair", async (TerminalPairRequest request, CancellationToken token) =>
		{
			TerminalPairingOutcome outcome = pairing.TryPair(request.TerminalName, request.TerminalNonce, request.Proof);
			if (outcome.Device == null)
			{
				return Results.Json(new TerminalError(outcome.Error ?? "Pairing was refused."), Json, statusCode: StatusCodes.Status403Forbidden);
			}
			logger.LogInformation("Paired billing terminal {Name} ({Id}).", outcome.Device.Name, outcome.Device.Id);
			return Results.Json(new TerminalPairResponse(outcome.Device.Id, outcome.Secret!, outcome.ServerProof!, Environment.MachineName, await ShopNameAsync(token)), Json);
		});

		RouteGroupBuilder device = app.MapGroup(TerminalProtocol.BasePath);
		device.AddEndpointFilter(async (EndpointFilterInvocationContext invocation, EndpointFilterDelegate next) =>
		{
			HttpContext http = invocation.HttpContext;
			string authorization = http.Request.Headers.Authorization.ToString();
			string? secret = authorization.StartsWith(TerminalProtocol.AuthorizationScheme + " ", StringComparison.Ordinal)
				? authorization[(TerminalProtocol.AuthorizationScheme.Length + 1)..].Trim()
				: null;
			TerminalDevice? terminal = Guid.TryParse(http.Request.Headers[TerminalProtocol.DeviceHeader].ToString(), out Guid deviceId)
				? devices.Authenticate(deviceId, secret)
				: null;
			if (terminal == null)
			{
				return Results.Json(new TerminalError("This PC is not paired with the server, or its pairing was removed. Pair it again from the server's Devices panel."), Json, statusCode: StatusCodes.Status401Unauthorized);
			}
			http.Items[DeviceItem] = terminal;
			// Everything this request saves is recorded as this terminal: device ID on every row, its name on audit
			// rows and register entries.
			using (RequestDevice.Use(terminal.Id, terminal.Name))
			{
				return await next(invocation);
			}
		});
		device.MapGet("/hello", HelloAsync);
		device.MapPost("/session", SignInAsync);

		RouteGroupBuilder user = device.MapGroup(string.Empty);
		user.AddEndpointFilter(async (EndpointFilterInvocationContext invocation, EndpointFilterDelegate next) =>
		{
			HttpContext http = invocation.HttpContext;
			TerminalSession? session = sessions.Find(http.Request.Headers[TerminalProtocol.SessionHeader].ToString(), Device(http).Id);
			if (session == null)
			{
				return Results.Json(new TerminalError("Signed out. Sign in again."), Json, statusCode: StatusCodes.Status401Unauthorized);
			}
			// The server's user record decides, on every call: a deactivated user is out, a changed role applies now.
			await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
			{
				AppUser? user = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().AppUsers.AsNoTracking()
					.FirstOrDefaultAsync(item => item.Id == session.UserId, http.RequestAborted);
				if (user == null || !user.IsActive)
				{
					sessions.End(session.TokenHash);
					return Results.Json(new TerminalError("This user can no longer sign in. Ask the owner."), Json, statusCode: StatusCodes.Status401Unauthorized);
				}
				session = sessions.Refresh(session, user.Role, string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName);
			}
			http.Items[SessionItem] = session;
			return await next(invocation);
		});
		user.MapDelete("/session", (HttpContext http) =>
		{
			sessions.End(Session(http).TokenHash);
			return Results.NoContent();
		});
		user.MapGet("/shop", ShopAsync);
		user.MapGet("/invoice/next", (HttpContext http, CancellationToken token) => WithScope(http, AppPermission.View, (scope, _) => scope.GetRequiredService<RetailBillingService>().PreviewNextInvoiceNoAsync(token)));
		user.MapGet("/stock", (HttpContext http, string? q, CancellationToken token) => WithScope(http, AppPermission.Search, (scope, _) => scope.GetRequiredService<RetailBillingService>().SearchStockAsync(q ?? string.Empty, token)));
		user.MapGet("/stock/barcode/{code}", (HttpContext http, string code, CancellationToken token) => WithScope(http, AppPermission.Search, (scope, _) => scope.GetRequiredService<RetailBillingService>().GetByBarcodeAsync(code, token)));
		user.MapPost("/stock/fefo", (HttpContext http, TerminalFefoRequest request, CancellationToken token) => WithScope(http, AppPermission.Search, (scope, _) => scope.GetRequiredService<RetailBillingService>().GetFefoChoicesAsync(request.DrugIds.ToArray(), token)));
		user.MapGet("/catalog", (HttpContext http, string? q, CancellationToken token) => WithScope(http, AppPermission.Search, async (scope, _) =>
		{
			MedicineSearchResults results = await scope.GetRequiredService<CatalogSearchService>().SearchAsync(q ?? string.Empty, token);
			return new { results.InStock, results.FromCatalog };
		}));
		user.MapGet("/patients", (HttpContext http, string? phone, CancellationToken token) => WithScope(http, AppPermission.Search, (scope, _) => scope.GetRequiredService<RetailBillingService>().SearchPatientsByPhoneAsync(phone ?? string.Empty, token)));
		user.MapGet("/patients/by-phone", (HttpContext http, string? phone, CancellationToken token) => WithScope(http, AppPermission.Search, (scope, _) => scope.GetRequiredService<RetailBillingService>().FindPatientIdByPhoneAsync(phone ?? string.Empty, token)));
		user.MapGet("/patients/loyalty", (HttpContext http, string? phone, CancellationToken token) => WithScope(http, AppPermission.Search, (scope, _) => scope.GetRequiredService<RetailBillingService>().GetLoyaltyPointsAsync(phone ?? string.Empty, token)));
		user.MapGet("/patients/{id:guid}/history", (HttpContext http, Guid id, CancellationToken token) => WithScope(http, AppPermission.Search, (scope, _) => scope.GetRequiredService<RetailBillingService>().GetPatientHistoryAsync(id, token)));
		user.MapPost("/sales", SaveSaleAsync);
		user.MapGet("/sales", (HttpContext http, string? q, CancellationToken token) => WithScope(http, AppPermission.Search, (scope, _) => scope.GetRequiredService<RetailBillingService>().SearchRecentBillsAsync(q, token)));
		user.MapGet("/sales/{id:guid}/returnable", (HttpContext http, Guid id, CancellationToken token) => WithScope(http, AppPermission.Search, (scope, _) => scope.GetRequiredService<RetailBillingService>().GetSaleReturnLinesAsync(id, token)));
		user.MapPost("/sales/{id:guid}/return", ReturnAsync);
		user.MapPost("/sales/{id:guid}/void", VoidAsync);
		user.MapGet("/sales/{id:guid}/print", (HttpContext http, Guid id, CancellationToken token) => WithScope(http, AppPermission.Print, (scope, _) => scope.GetRequiredService<RetailBillingService>().GetPrintableBillAsync(id, token)));
		user.MapPut("/products/{drugId:guid}/tax", (HttpContext http, Guid drugId, TerminalProductTaxRequest request, CancellationToken token) => WithScope(http, AppPermission.CreateBill, (scope, session) =>
			scope.GetRequiredService<ProductTaxService>().SetAsync(new ProductTaxUpdate(drugId, request.HsnCode, request.IsNilRated ? null : request.GstRate, request.IsNilRated), session.UserId, "Terminal bill (" + Device(http).Name + ")", token)));
		user.MapPut("/batches/{batchId:guid}/mrp", (HttpContext http, Guid batchId, TerminalBatchMrpRequest request, CancellationToken token) => WithScope(http, AppPermission.EnterStock, async (scope, session) =>
		{
			await scope.GetRequiredService<InventoryService>().UpdateBatchMrpAsync(batchId, request.Mrp, session.Role, token);
			return new { batchId, request.Mrp };
		}));
	}

	private async Task<string> ShopNameAsync(CancellationToken token)
	{
		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		return await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().PharmacyProfiles.AsNoTracking().Select(p => p.Name).FirstOrDefaultAsync(token) ?? string.Empty;
	}

	private async Task<IResult> HelloAsync(HttpContext http, CancellationToken token)
	{
		TerminalDevice terminal = Device(http);
		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		string shop = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().PharmacyProfiles.AsNoTracking().Select(p => p.Name).FirstOrDefaultAsync(token) ?? string.Empty;
		bool allowed = await scope.ServiceProvider.GetRequiredService<IEntitlementService>().CanPerformAsync(ProtectedOperation.CreateBill, token);
		return Results.Json(new TerminalHello(TerminalProtocol.Version, Environment.MachineName, shop, terminal.Id, terminal.Name, BusinessClock.UtcNow, allowed,
			allowed ? null : "Billing is unavailable: the server PC's licence is read-only or expired."), Json);
	}

	private async Task<IResult> SignInAsync(HttpContext http, TerminalSignInRequest request, CancellationToken token)
	{
		TerminalDevice terminal = Device(http);
		if (sessions.IsSignInLocked(terminal.Id))
		{
			return Results.Json(new TerminalError("Too many wrong sign-ins from this PC. Wait five minutes."), Json, statusCode: StatusCodes.Status429TooManyRequests);
		}
		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		AppUser? user = await scope.ServiceProvider.GetRequiredService<AuthenticationService>().AuthenticateAsync(request.UserName ?? string.Empty, request.Secret ?? string.Empty, token);
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		if (user == null)
		{
			sessions.RecordFailedSignIn(terminal.Id);
			return Results.Json(new TerminalError("Wrong user name or PIN."), Json, statusCode: StatusCodes.Status401Unauthorized);
		}
		sessions.ClearFailedSignIns(terminal.Id);
		(TerminalSession session, string sessionToken) = sessions.Start(terminal.Id, user);
		context.AuditLogs.Add(new AuditLog { UserId = user.Id, Action = "TerminalSignIn", EntityName = "Terminal", EntityId = terminal.Id, Details = $"{user.UserName} signed in at terminal {terminal.Name}" });
		await context.SaveChangesAsync(token);
		return Results.Json(new TerminalSignInResponse(sessionToken, user.Id, session.DisplayName, user.Role.ToString(), session.ExpiresAtUtc), Json);
	}

	private Task<IResult> ShopAsync(HttpContext http, CancellationToken token) => WithScope(http, AppPermission.View, async (scope, _) =>
	{
		PharmacyProfile profile = await scope.GetRequiredService<PharmaBillDbContext>().PharmacyProfiles.AsNoTracking().FirstAsync(token);
		string next = await scope.GetRequiredService<RetailBillingService>().PreviewNextInvoiceNoAsync(token);
		return new TerminalShop(profile.Name, profile.LegalName, profile.Address, profile.Phone, profile.Gstin, profile.State, profile.BusinessMode.ToString(), next);
	});

	private async Task<IResult> SaveSaleAsync(HttpContext http, CancellationToken token)
	{
		TerminalDevice terminal = Device(http);
		TerminalSession session = Session(http);
		if (!Guid.TryParse(http.Request.Headers[TerminalProtocol.RequestIdHeader].ToString(), out Guid requestId) || requestId == Guid.Empty)
		{
			return Results.Json(new TerminalError($"Missing {TerminalProtocol.RequestIdHeader}: every save needs its own request ID."), Json, statusCode: StatusCodes.Status400BadRequest);
		}
		TerminalSaleRequest? request;
		string? uploadPath = null;
		try
		{
			if (http.Request.HasFormContentType)
			{
				IFormCollection form = await http.Request.ReadFormAsync(token);
				request = JsonSerializer.Deserialize<TerminalSaleRequest>(form["sale"].ToString(), Json);
				IFormFile? file = form.Files["prescription"];
				if (file != null)
				{
					uploadPath = await StageUploadAsync(file, token);
				}
			}
			else
			{
				request = await http.Request.ReadFromJsonAsync<TerminalSaleRequest>(Json, token);
			}
			if (request == null || request.Items == null || request.Payments == null)
			{
				throw new InvalidOperationException("The bill was empty or unreadable.");
			}

			await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
			IServiceProvider services = scope.ServiceProvider;
			bool prescriptionOverride = false;
			if (request.PrescriptionOverride != null)
			{
				prescriptionOverride = await GrantPrescriptionOverrideAsync(services.GetRequiredService<PharmaBillDbContext>(), request.PrescriptionOverride, terminal, token);
			}
			var input = new SaveRetailSaleInput(
				request.PatientName,
				request.PatientPhone,
				request.PatientAddress,
				request.PrescriberName,
				request.PrescriberRegistrationNumber,
				uploadPath,
				request.Items.Select(line => new RetailSaleLineInput(line.DrugId, line.BatchId, line.Quantity, line.DiscountAmount, line.UnitPrice,
					line.HsnCode == null && line.GstRate == null && !line.IsNilRated ? null : ProductTax.Require("Bill line", line.HsnCode, line.IsNilRated ? null : line.GstRate, line.IsNilRated))).ToArray(),
				request.Payments.Select(payment => new RetailPaymentInput(payment.Method, payment.AppliedAmount, payment.TenderedAmount)).ToArray(),
				request.UpiPaymentReceived,
				request.Notes,
				request.MrdNumber,
				prescriptionOverride,
				request.AddToCustomers,
				services.GetService<ITerminalPrintBrandingProvider>()?.Current(),
				requestId);
			RetailBillingService billing = services.GetRequiredService<RetailBillingService>();
			RetailSaleResult result = await billing.SaveSaleAsync(input, session.UserId, session.Role, token);
			string next = await billing.PreviewNextInvoiceNoAsync(token);
			return Results.Json(new TerminalSaleResponse(result.Sale.Id, result.InvoiceNo, result.Subtotal, result.TaxAmount, result.DiscountAmount, result.TotalAmount, result.PaidAmount, result.ChangeDue, next), Json);
		}
		finally
		{
			if (uploadPath != null)
			{
				try { File.Delete(uploadPath); } catch (IOException) { }
			}
		}
	}

	private static async Task<bool> GrantPrescriptionOverrideAsync(PharmaBillDbContext context, TerminalAdminOverride grant, TerminalDevice terminal, CancellationToken token)
	{
		await VerifyAdminAsync(context, grant, "save a controlled bill without a prescription attachment", "Admin override was not granted. Attach the prescription to continue.", terminal, token);
		return true;
	}

	// Same rule as the desktop's Admin Override: an active admin who may edit printed bills, verified by PIN.
	// Every grant is audited with what it was for and at which terminal.
	private static async Task<AppUser> VerifyAdminAsync(PharmaBillDbContext context, TerminalAdminOverride? grant, string purpose, string refusal, TerminalDevice terminal, CancellationToken token)
	{
		string name = (grant?.UserName ?? string.Empty).Trim().ToLowerInvariant();
		AppUser? admin = await context.AppUsers.AsNoTracking().FirstOrDefaultAsync(user => user.IsActive && user.UserName.ToLower() == name, token);
		if (admin == null || !PermissionMatrix.Allows(admin.Role, AppPermission.EditPrintedBills) || string.IsNullOrEmpty(grant?.Secret) || !PasswordHasher.Verify(grant.Secret, admin.PasswordHash))
		{
			throw new UnauthorizedAccessException(refusal);
		}
		context.AuditLogs.Add(new AuditLog { UserId = admin.Id, Action = "AdminOverrideGranted", EntityName = "Terminal", EntityId = terminal.Id,
			Details = $"{purpose} by {admin.UserName} ({PermissionMatrix.ToOperatorLabel(admin.Role)}) at terminal {terminal.Name}" });
		await context.SaveChangesAsync(token);
		return admin;
	}

	private static RecordUnlockGrant UnlockFor(AppUser admin, Guid saleId, string reason) => new RecordUnlockGrant
	{
		AdminUserId = admin.Id,
		AdminUserName = admin.UserName,
		AdminRole = admin.Role,
		Reason = reason.Trim(),
		EntityType = "RetailBill",
		EntityId = saleId,
		Action = "MODIFIED_AFTER_LOCK"
	};

	private async Task<IResult> ReturnAsync(HttpContext http, Guid id, TerminalReturnRequest request, CancellationToken token)
	{
		TerminalDevice terminal = Device(http);
		TerminalSession session = Session(http);
		if (!Guid.TryParse(http.Request.Headers[TerminalProtocol.RequestIdHeader].ToString(), out Guid requestId) || requestId == Guid.Empty)
		{
			return Results.Json(new TerminalError($"Missing {TerminalProtocol.RequestIdHeader}: every return needs its own request ID."), Json, statusCode: StatusCodes.Status400BadRequest);
		}
		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		IServiceProvider services = scope.ServiceProvider;
		try
		{
			if (request.Unlock != null)
			{
				string invoice = await InvoiceNoAsync(services, id, token);
				AppUser admin = await VerifyAdminAsync(services.GetRequiredService<PharmaBillDbContext>(), request.Unlock, $"unlock bill {invoice} for a return", "Admin unlock was not granted.", terminal, token);
				RecordUnlockContext.Arm(UnlockFor(admin, id, request.Reason ?? string.Empty));
			}
			RetailSaleReturnResult result = await services.GetRequiredService<RetailBillingService>().IssueSalesReturnAsync(id,
				(request.Lines ?? []).Select(line => new RetailSaleReturnLineInput(line.SaleItemId, line.Quantity)).ToArray(),
				request.Reason ?? string.Empty, request.Restock, session.UserId, session.Role, requestId, token);
			return Results.Json(new TerminalReturnResponse(result.ReturnNo, result.CreditAmount, result.RestockedQuantity), Json);
		}
		finally
		{
			RecordUnlockContext.Clear();
		}
	}

	private async Task<IResult> VoidAsync(HttpContext http, Guid id, TerminalVoidRequest request, CancellationToken token)
	{
		TerminalDevice terminal = Device(http);
		TerminalSession session = Session(http);
		if (!PermissionMatrix.Allows(session.Role, AppPermission.CreateBill))
		{
			return Results.Json(new TerminalError($"{session.DisplayName} is not allowed to do this (void)."), Json, statusCode: StatusCodes.Status403Forbidden);
		}
		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		IServiceProvider services = scope.ServiceProvider;
		PharmaBillDbContext context = services.GetRequiredService<PharmaBillDbContext>();
		Sale sale = await context.Sales.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, token)
			?? throw new KeyNotFoundException("That bill was not found on the server.");
		if (sale.IsDeleted)
		{
			// Already voided - for example the reply to an earlier void was lost. Nothing more to do.
			return Results.Json(new TerminalVoidResponse(sale.Id, sale.InvoiceNo, AlreadyVoided: true), Json);
		}
		AppUser admin = await VerifyAdminAsync(context, request.Admin, $"void bill {sale.InvoiceNo}", "Admin override was not granted. Only an admin can void a bill.", terminal, token);
		try
		{
			// The admin who voids also covers the record lock on a bill older than the lock period, as on the desktop.
			RecordUnlockContext.Arm(UnlockFor(admin, id, request.Reason ?? string.Empty));
			await services.GetRequiredService<RetailBillingService>().VoidSaleAsync(id, admin.Id, admin.Role, request.Reason ?? string.Empty, token);
		}
		finally
		{
			RecordUnlockContext.Clear();
		}
		return Results.Json(new TerminalVoidResponse(sale.Id, sale.InvoiceNo, AlreadyVoided: false), Json);
	}

	private static async Task<string> InvoiceNoAsync(IServiceProvider services, Guid saleId, CancellationToken token) =>
		await services.GetRequiredService<PharmaBillDbContext>().Sales.AsNoTracking().Where(sale => sale.Id == saleId).Select(sale => sale.InvoiceNo).FirstOrDefaultAsync(token)
		?? throw new KeyNotFoundException("That bill was not found on the server.");

	private static async Task<string> StageUploadAsync(IFormFile file, CancellationToken token)
	{
		string extension = Path.GetExtension(file.FileName).ToLowerInvariant();
		if (!TerminalProtocol.PrescriptionExtensions.Contains(extension))
		{
			throw new InvalidOperationException("Attach the prescription as a JPG, PNG or PDF.");
		}
		if (file.Length == 0 || file.Length > TerminalProtocol.MaxPrescriptionBytes)
		{
			throw new InvalidOperationException("The prescription file must be under 10 MB.");
		}
		string directory = Path.Combine(Path.GetTempPath(), "PharmaRemit-terminal-uploads");
		Directory.CreateDirectory(directory);
		string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);
		await using FileStream output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		await file.CopyToAsync(output, token);
		return path;
	}

	private async Task<IResult> WithScope<T>(HttpContext http, AppPermission permission, Func<IServiceProvider, TerminalSession, Task<T>> action)
	{
		TerminalSession session = Session(http);
		if (!PermissionMatrix.Allows(session.Role, permission))
		{
			return Results.Json(new TerminalError($"{session.DisplayName} is not allowed to do this ({permission})."), Json, statusCode: StatusCodes.Status403Forbidden);
		}
		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		return Results.Json(await action(scope.ServiceProvider, session), Json);
	}

	private static TerminalDevice Device(HttpContext http) => (TerminalDevice)http.Items[DeviceItem]!;

	private static TerminalSession Session(HttpContext http) => (TerminalSession)http.Items[SessionItem]!;
}

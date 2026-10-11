using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PharmaBill.Sync;

/// <summary>Local Wi-Fi listener that receives distributor bills captured on a paired Android phone.</summary>
public sealed class PurchasePhoneBridge : IAsyncDisposable
{
	public const int Port = 5184;

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	private readonly object _gate = new object();

	private WebApplication? _app;

	private string _sessionToken = Guid.NewGuid().ToString("D");

	public string SessionToken => _sessionToken;

	public string? LocalIp { get; private set; }

	public bool IsListening { get; private set; }

	public string StatusText { get; private set; } = "Phone scan is stopped.";

	public string ReachableUrl => "http://" + (string.IsNullOrWhiteSpace(LocalIp) ? "127.0.0.1" : LocalIp) + ":" + Port + "/api/purchases/ping";

	public event Action<PhonePurchaseUpload>? BillReceived;

	public event Action<string>? StatusChanged;

	public string BuildQrPayload()
	{
		RefreshLocalIp();
		string host = string.IsNullOrWhiteSpace(LocalIp) ? "127.0.0.1" : LocalIp;
		return JsonSerializer.Serialize(new
		{
			ip = host,
			host,
			port = Port,
			sessionToken = _sessionToken,
			token = _sessionToken,
			station = "Purchases",
			ping = "http://" + host + ":" + Port + "/api/purchases/ping",
			upload = "http://" + host + ":" + Port + "/api/inward/upload-image"
		});
	}

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		RefreshLocalIp();
		lock (_gate)
		{
			if (_app != null)
			{
				IsListening = true;
				SetStatus(ListeningStatus());
				return;
			}
		}

		WebApplication? app = null;
		try
		{
			WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
			builder.Logging.ClearProviders();
			builder.WebHost.ConfigureKestrel((KestrelServerOptions options) =>
			{
				options.AddServerHeader = false;
				options.Limits.MaxRequestBodySize = 32L * 1024L * 1024L;
				options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(20);
				// Bind every IPv4 interface (not loopback-only) so the phone can reach this PC on LAN.
				options.Listen(IPAddress.Any, Port);
			});
			app = builder.Build();
			app.UseWebSockets();
			app.Use(async (HttpContext context, Func<Task> next) =>
			{
				context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
				context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
				context.Response.Headers.Append("Access-Control-Allow-Headers", "*");
				if (HttpMethods.IsOptions(context.Request.Method))
				{
					context.Response.StatusCode = StatusCodes.Status204NoContent;
					return;
				}

				await next();
			});

			app.MapGet("/api/purchases/ping", HandlePingAsync);
			app.MapGet("/api/sync/ping", HandlePingAsync);
			app.MapGet("/api/purchases/stream", HandleStreamAsync);
			app.MapPost("/api/purchases/stream", HandleUploadAsync);
			app.MapPost("/api/purchases/bills", HandleUploadAsync);
			app.MapPost("/api/inward/upload-image", HandleUploadAsync);
			app.MapPost("/api/sync/bill", HandleUploadAsync);

			await app.StartAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			if (app != null)
			{
				await app.DisposeAsync();
			}

			IsListening = false;
			SetStatus("Cannot listen on port " + Port + " (all adapters). " + ex.Message);
			return;
		}

		lock (_gate)
		{
			_app = app;
		}

		IsListening = true;
		string firewallNote = string.Empty;
		try
		{
			WindowsFirewallPortOpener.Result firewall = WindowsFirewallPortOpener.EnsureAllowRule(
				Port,
				"PharmaBill Purchases Phone Scan (Port 5184)",
				includePublicProfile: true);
			if (!firewall.RulePresent)
			{
				firewallNote = " Firewall: " + firewall.Message;
			}
		}
		catch (Exception ex)
		{
			firewallNote = " Firewall: " + ex.Message;
		}

		SetStatus(ListeningStatus() + firewallNote);
	}

	public async Task StopAsync()
	{
		WebApplication? app;
		lock (_gate)
		{
			app = _app;
			_app = null;
		}

		IsListening = false;
		if (app != null)
		{
			try
			{
				await app.StopAsync(TimeSpan.FromSeconds(2));
			}
			catch
			{
				// The process is shutting the listener down.
			}

			await app.DisposeAsync();
		}

		SetStatus("Phone scan is stopped.");
	}

	public ValueTask DisposeAsync()
	{
		return new ValueTask(StopAsync());
	}

	private void RefreshLocalIp()
	{
		LocalIp = LocalNetworkInfo.GetLocalIPv4() ?? "127.0.0.1";
	}

	private string ListeningStatus()
	{
		return "Listening at " + ReachableUrl + " — connect the phone to the same Wi-Fi, then scan the QR.";
	}

	private Task HandlePingAsync(HttpContext context)
	{
		context.Response.ContentType = "application/json";
		return context.Response.WriteAsync(JsonSerializer.Serialize(new
		{
			status = "ok",
			listening = IsListening,
			station = "Purchases",
			port = Port,
			ip = LocalIp,
			message = StatusText
		}, Json), context.RequestAborted);
	}

	private async Task HandleStreamAsync(HttpContext context)
	{
		if (!context.WebSockets.IsWebSocketRequest)
		{
			await HandlePingAsync(context);
			return;
		}

		if (!TokenMatches(context, null))
		{
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			return;
		}

		using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
		byte[] hello = Encoding.UTF8.GetBytes("{\"status\":\"listening\"}");
		await socket.SendAsync(hello, WebSocketMessageType.Text, true, context.RequestAborted);
		byte[] buffer = new byte[64 * 1024];
		while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
		{
			using MemoryStream message = new MemoryStream();
			WebSocketReceiveResult result;
			do
			{
				result = await socket.ReceiveAsync(buffer, context.RequestAborted);
				if (result.MessageType == WebSocketMessageType.Close)
				{
					await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
					return;
				}

				message.Write(buffer, 0, result.Count);
			}
			while (!result.EndOfMessage);

			if (result.MessageType != WebSocketMessageType.Text || message.Length == 0)
			{
				continue;
			}

			string json = Encoding.UTF8.GetString(message.ToArray());
			if (await TryAcceptJsonAsync(json, context))
			{
				byte[] ack = Encoding.UTF8.GetBytes("{\"status\":\"received\"}");
				await socket.SendAsync(ack, WebSocketMessageType.Text, true, context.RequestAborted);
			}
		}
	}

	private async Task HandleUploadAsync(HttpContext context)
	{
		if (context.Request.HasFormContentType)
		{
			if (!await TryAcceptMultipartAsync(context))
			{
				if (context.Response.StatusCode == StatusCodes.Status200OK)
				{
					context.Response.StatusCode = StatusCodes.Status400BadRequest;
				}

				return;
			}

			context.Response.ContentType = "application/json";
			await context.Response.WriteAsync("{\"status\":\"received\"}", context.RequestAborted);
			return;
		}

		context.Request.EnableBuffering();
		using StreamReader reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
		string json = await reader.ReadToEndAsync(context.RequestAborted);
		context.Request.Body.Position = 0;
		if (!await TryAcceptJsonAsync(json, context))
		{
			if (context.Response.StatusCode == StatusCodes.Status200OK)
			{
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
			}

			return;
		}

		context.Response.ContentType = "application/json";
		await context.Response.WriteAsync("{\"status\":\"received\"}", context.RequestAborted);
	}

	private async Task<bool> TryAcceptMultipartAsync(HttpContext context)
	{
		IFormCollection form;
		try
		{
			form = await context.Request.ReadFormAsync(context.RequestAborted);
		}
		catch
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return false;
		}

		string? bodyToken = First(
			form["sessionToken"].FirstOrDefault(),
			form["token"].FirstOrDefault(),
			form["SessionToken"].FirstOrDefault());
		if (!TokenMatches(context, bodyToken))
		{
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			return false;
		}

		IFormFile? file = form.Files.GetFile("file")
			?? form.Files.GetFile("image")
			?? form.Files.GetFile("bill")
			?? form.Files.GetFile("photo")
			?? form.Files.FirstOrDefault();

		string? saved = file == null ? null : await SaveFormFileAsync(file);
		List<PhonePurchaseLineUpload> items = ParseItemsField(form["items"].FirstOrDefault() ?? form["lines"].FirstOrDefault());

		if (items.Count == 0 && string.IsNullOrWhiteSpace(saved) && string.IsNullOrWhiteSpace(form["imageBase64"].FirstOrDefault()))
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return false;
		}

		if (string.IsNullOrWhiteSpace(saved))
		{
			string? b64 = First(form["imageBase64"].FirstOrDefault(), form["fileBase64"].FirstOrDefault(), form["pdfBase64"].FirstOrDefault());
			if (!string.IsNullOrWhiteSpace(b64))
			{
				saved = await SaveBase64Async(b64, form["fileName"].FirstOrDefault() ?? "bill.jpg");
			}
		}

		RaiseBillReceived(new PhonePurchaseUpload
		{
			Supplier = First(form["supplier"].FirstOrDefault(), form["supplierName"].FirstOrDefault()),
			InvoiceNo = First(form["invoiceNo"].FirstOrDefault(), form["billNo"].FirstOrDefault()),
			InvoiceDate = form["invoiceDate"].FirstOrDefault(),
			FileName = file?.FileName ?? form["fileName"].FirstOrDefault(),
			SavedFilePath = saved,
			Items = items
		});
		return true;
	}

	private async Task<bool> TryAcceptJsonAsync(string json, HttpContext context)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return false;
		}

		PhoneBillWire? wire;
		try
		{
			wire = JsonSerializer.Deserialize<PhoneBillWire>(json, Json);
		}
		catch (JsonException)
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return false;
		}

		if (wire == null || !TokenMatches(context, wire.SessionToken))
		{
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			return false;
		}

		string? saved = await SaveAttachmentAsync(wire);
		List<PhonePurchaseLineUpload> items = new List<PhonePurchaseLineUpload>();
		if (wire.Items != null)
		{
			foreach (PhoneBillLineWire line in wire.Items)
			{
				string? name = First(line.Name, line.ItemName, line.Medicine);
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}

				items.Add(new PhonePurchaseLineUpload
				{
					Name = name.Trim(),
					Batch = First(line.Batch, line.BatchNo) ?? string.Empty,
					Expiry = line.Expiry ?? string.Empty,
					Quantity = line.Quantity > 0m ? line.Quantity : line.Qty,
					Free = line.Free,
					Mrp = line.Mrp,
					Rate = line.Rate,
					Gst = line.Gst
				});
			}
		}

		RaiseBillReceived(new PhonePurchaseUpload
		{
			Supplier = First(wire.Supplier, wire.SupplierName),
			InvoiceNo = First(wire.InvoiceNo, wire.BillNo),
			InvoiceDate = wire.InvoiceDate,
			FileName = wire.FileName,
			SavedFilePath = saved,
			Items = items
		});
		return true;
	}

	private void RaiseBillReceived(PhonePurchaseUpload upload)
	{
		SetStatus("Received bill from mobile — extracting items on this PC...");
		BillReceived?.Invoke(upload);
	}

	/// <summary>Updates the listen banner after PC-side OCR finishes (or fails).</summary>
	public void ReportExtractionStatus(string message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			if (IsListening)
			{
				SetStatus(ListeningStatus());
			}

			return;
		}

		SetStatus(message.Trim());
	}

	private bool TokenMatches(HttpContext context, string? bodyToken)
	{
		string? presented = bodyToken;
		if (string.IsNullOrWhiteSpace(presented))
		{
			presented = context.Request.Headers["X-Session-Token"].ToString();
		}

		if (string.IsNullOrWhiteSpace(presented))
		{
			presented = context.Request.Headers["X-Sync-Token"].ToString();
		}

		if (string.IsNullOrWhiteSpace(presented))
		{
			presented = context.Request.Query["sessionToken"].ToString();
		}

		if (string.IsNullOrWhiteSpace(presented))
		{
			presented = context.Request.Query["token"].ToString();
		}

		return string.Equals(presented?.Trim(), _sessionToken, StringComparison.OrdinalIgnoreCase);
	}

	private static List<PhonePurchaseLineUpload> ParseItemsField(string? json)
	{
		List<PhonePurchaseLineUpload> items = new List<PhonePurchaseLineUpload>();
		if (string.IsNullOrWhiteSpace(json))
		{
			return items;
		}

		try
		{
			List<PhoneBillLineWire>? lines = JsonSerializer.Deserialize<List<PhoneBillLineWire>>(json, Json);
			if (lines == null)
			{
				return items;
			}

			foreach (PhoneBillLineWire line in lines)
			{
				string? name = First(line.Name, line.ItemName, line.Medicine);
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}

				items.Add(new PhonePurchaseLineUpload
				{
					Name = name.Trim(),
					Batch = First(line.Batch, line.BatchNo) ?? string.Empty,
					Expiry = line.Expiry ?? string.Empty,
					Quantity = line.Quantity > 0m ? line.Quantity : line.Qty,
					Free = line.Free,
					Mrp = line.Mrp,
					Rate = line.Rate,
					Gst = line.Gst
				});
			}
		}
		catch (JsonException)
		{
			// Optional field — image-only uploads are still accepted.
		}

		return items;
	}

	private static async Task<string?> SaveAttachmentAsync(PhoneBillWire wire)
	{
		string? payload = First(wire.FileBase64, wire.ImageBase64, wire.PdfBase64);
		if (string.IsNullOrWhiteSpace(payload))
		{
			return null;
		}

		return await SaveBase64Async(payload, wire.FileName ?? "bill.jpg");
	}

	private static async Task<string?> SaveFormFileAsync(IFormFile file)
	{
		string folder = InwardScansFolder();
		Directory.CreateDirectory(folder);
		string name = string.IsNullOrWhiteSpace(file.FileName) ? "bill.jpg" : Path.GetFileName(file.FileName);
		string safe = SanitizeFileName(name);
		string path = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + safe);
		await using FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		await file.CopyToAsync(stream);
		return path;
	}

	private static async Task<string?> SaveBase64Async(string payload, string fileName)
	{
		int comma = payload.IndexOf(',');
		if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
		{
			payload = payload[(comma + 1)..];
		}

		byte[] bytes;
		try
		{
			bytes = Convert.FromBase64String(payload);
		}
		catch (FormatException)
		{
			return null;
		}

		string folder = InwardScansFolder();
		Directory.CreateDirectory(folder);
		string safe = SanitizeFileName(string.IsNullOrWhiteSpace(fileName) ? "bill.jpg" : Path.GetFileName(fileName));
		string path = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + safe);
		await File.WriteAllBytesAsync(path, bytes);
		return path;
	}

	private static string InwardScansFolder() =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "InwardScans");

	private static string SanitizeFileName(string name)
	{
		string safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
		return string.IsNullOrWhiteSpace(safe) ? "bill.jpg" : safe;
	}

	private static string? First(params string?[] values)
	{
		foreach (string? value in values)
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				return value.Trim();
			}
		}

		return null;
	}

	private void SetStatus(string text)
	{
		StatusText = text;
		StatusChanged?.Invoke(text);
	}

	private sealed class PhoneBillWire
	{
		public string? SessionToken { get; set; }

		public string? Supplier { get; set; }

		public string? SupplierName { get; set; }

		public string? InvoiceNo { get; set; }

		public string? BillNo { get; set; }

		public string? InvoiceDate { get; set; }

		public string? FileName { get; set; }

		public string? FileBase64 { get; set; }

		public string? ImageBase64 { get; set; }

		public string? PdfBase64 { get; set; }

		public List<PhoneBillLineWire>? Items { get; set; }
	}

	private sealed class PhoneBillLineWire
	{
		public string? Name { get; set; }

		public string? ItemName { get; set; }

		public string? Medicine { get; set; }

		public string? Batch { get; set; }

		public string? BatchNo { get; set; }

		public string? Expiry { get; set; }

		public decimal Qty { get; set; }

		public decimal Quantity { get; set; }

		public decimal Free { get; set; }

		public decimal Mrp { get; set; }

		public decimal Rate { get; set; }

		public decimal Gst { get; set; }
	}
}

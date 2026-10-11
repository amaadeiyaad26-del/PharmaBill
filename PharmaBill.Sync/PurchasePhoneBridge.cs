using System;
using System.Collections.Generic;
using System.IO;
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

	public event Action<PhonePurchaseUpload>? BillReceived;

	public event Action<string>? StatusChanged;

	public string BuildQrPayload()
	{
		return JsonSerializer.Serialize(new
		{
			ip = string.IsNullOrWhiteSpace(LocalIp) ? "127.0.0.1" : LocalIp,
			port = Port,
			sessionToken = _sessionToken,
			station = "Purchases"
		});
	}

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			if (_app != null)
			{
				return;
			}
		}

		LocalIp = LocalNetworkInfo.GetLocalIPv4() ?? "127.0.0.1";
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
			app.MapGet("/api/purchases/stream", HandleStreamAsync);
			app.MapPost("/api/purchases/stream", HandlePostAsync);
			app.MapPost("/api/purchases/bills", HandlePostAsync);
			await app.StartAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			if (app != null)
			{
				await app.DisposeAsync();
			}

			SetStatus("Cannot listen on port " + Port + ". " + ex.Message);
			return;
		}

		lock (_gate)
		{
			_app = app;
		}

		IsListening = true;
		try
		{
			WindowsFirewallPortOpener.EnsureAllowRule(Port, "PharmaBill Purchases Phone Scan (Port 5184)");
		}
		catch
		{
			// Pairing still works on networks that already allow the port.
		}

		SetStatus("Listening for incoming bills from phone... (Connect phone to same Wi-Fi network)");
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

	private async Task HandleStreamAsync(HttpContext context)
	{
		if (!context.WebSockets.IsWebSocketRequest)
		{
			context.Response.ContentType = "application/json";
			await context.Response.WriteAsync(JsonSerializer.Serialize(new
			{
				listening = IsListening,
				station = "Purchases",
				message = StatusText
			}, Json));
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

	private async Task HandlePostAsync(HttpContext context)
	{
		context.Request.EnableBuffering();
		using StreamReader reader = new StreamReader(context.Request.Body, Encoding.UTF8);
		string json = await reader.ReadToEndAsync(context.RequestAborted);
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

	private async Task<bool> TryAcceptJsonAsync(string json, HttpContext context)
	{
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

		BillReceived?.Invoke(new PhonePurchaseUpload
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

	private bool TokenMatches(HttpContext context, string? bodyToken)
	{
		string? presented = bodyToken;
		if (string.IsNullOrWhiteSpace(presented))
		{
			presented = context.Request.Headers["X-Session-Token"].ToString();
		}

		if (string.IsNullOrWhiteSpace(presented))
		{
			presented = context.Request.Query["sessionToken"].ToString();
		}

		return string.Equals(presented?.Trim(), _sessionToken, StringComparison.OrdinalIgnoreCase);
	}

	private static async Task<string?> SaveAttachmentAsync(PhoneBillWire wire)
	{
		string? payload = First(wire.FileBase64, wire.ImageBase64, wire.PdfBase64);
		if (string.IsNullOrWhiteSpace(payload))
		{
			return null;
		}

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

		string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "PhoneInbox");
		Directory.CreateDirectory(folder);
		string name = string.IsNullOrWhiteSpace(wire.FileName) ? "bill.jpg" : Path.GetFileName(wire.FileName);
		string safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
		if (string.IsNullOrWhiteSpace(safe))
		{
			safe = "bill.jpg";
		}

		string path = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + safe);
		await File.WriteAllBytesAsync(path, bytes);
		return path;
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

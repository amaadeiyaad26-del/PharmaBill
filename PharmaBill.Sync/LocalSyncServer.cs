using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
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
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class LocalSyncServer(IServiceScopeFactory scopeFactory, HybridLogicalClock clock, SyncPayloadSerializer serializer, DatabaseDeviceId deviceId, ILogger<LocalSyncServer> logger) : IAsyncDisposable
{
	private sealed record PendingPage(IReadOnlyList<SyncChangeEnvelope> Changes, IReadOnlyList<Guid> ChangeIds, bool HasMore, int PendingRemaining, string? NextAfter);

	private sealed record BillSnapshot(IReadOnlyList<Sale> Sales, IReadOnlyList<SaleItem> SaleItems, IReadOnlyList<Patient> Patients, IReadOnlyList<WholesaleInvoice> WholesaleInvoices, IReadOnlyList<WholesaleInvoiceItem> WholesaleInvoiceItems, DateTime NextSince, bool HasMore);

	public const int DefaultPort = 5055;

	public const int MaxBodyBytes = 5242880;

	public const int MaxPushChanges = 500;

	public const int MaxPullLimit = 1000;

	public const string TokenHeader = "X-Sync-Token";

	public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(45L);

	private static readonly HashSet<string> PushableEntities = new HashSet<string>(StringComparer.Ordinal) { "Sale", "SaleItem", "Patient", "Prescription", "StockMovement", "ScheduleRegisterEntry", "Receipt", "ReturnNote", "SaleReturnItem", "AuditLog" };

	private static readonly string[] PullableEntities = new string[6] { "Drug", "Batch", "DrugPackLevel", "CatalogInfo", "StorageLocation", "StockLocationBalance" };

	private static readonly string[] PullableBillEntities = new string[6] { "Sale", "SaleItem", "Patient", "Prescription", "WholesaleInvoice", "WholesaleInvoiceItem" };

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	private readonly object _gate = new object();

	private WebApplication? _app;

	private Timer? _connectionTimer;

	private long _lastDeviceSeenTicks;

	private string _token = NewToken();

	private LocalSyncStatus _status = new LocalSyncStatus(LocalSyncState.Stopped, "Sync station is stopped");

	public int Port { get; private set; } = 5055;

	public LocalSyncStatus Status => _status;

	public string Token => _token;

	public event Action<LocalSyncStatus>? StatusChanged;

	public string BuildPairingPayload(string host)
	{
		return JsonSerializer.Serialize(new
		{
			host = host,
			port = ((Port <= 0) ? 5055 : Port),
			token = _token
		});
	}

	public string RegenerateToken()
	{
		_token = NewToken();
		return _token;
	}

	public async Task StartAsync(int port = 5055, CancellationToken cancellationToken = default(CancellationToken))
	{
		lock (_gate)
		{
			if (_app != null)
			{
				return;
			}
		}
		Port = port;
		WebApplication app = null;
		try
		{
			WebApplicationBuilder webApplicationBuilder = WebApplication.CreateSlimBuilder();
			webApplicationBuilder.Logging.ClearProviders();
			webApplicationBuilder.WebHost.ConfigureKestrel((KestrelServerOptions options) =>
			{
				options.AddServerHeader = false;
				options.Limits.MaxRequestBodySize = 5242880L;
				options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10L);
				options.Listen(IPAddress.Any, port);
			});
			app = webApplicationBuilder.Build();
			app.Use(async (HttpContext context, Func<Task> next) =>
			{
				context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
				context.Response.Headers.Append("Access-Control-Allow-Methods", "*");
				context.Response.Headers.Append("Access-Control-Allow-Headers", "*");
				if (HttpMethods.IsOptions(context.Request.Method))
				{
					context.Response.StatusCode = 204;
				}
				else
				{
					await next();
				}
			});
			MapEndpoints(app);
			await app.StartAsync(cancellationToken);
			CaptureBoundPort(app);
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			logger.LogWarning(ex, "Local sync listener could not start on port {Port}.", port);
			if (app != null)
			{
				await app.DisposeAsync();
			}
			SetStatus(new LocalSyncStatus(LocalSyncState.Unavailable, $"Cannot listen on port {port}", Describe(ex, port)));
			return;
		}
		lock (_gate)
		{
			_app = app;
		}
		_connectionTimer = new Timer((object? _) =>
		{
			RefreshConnectionState();
		}, null, TimeSpan.FromSeconds(5L), TimeSpan.FromSeconds(5L));
		SetStatus(ListeningStatus());
	}

	public async Task StopAsync()
	{
		WebApplication app;
		lock (_gate)
		{
			app = _app;
			_app = null;
		}
		_connectionTimer?.Dispose();
		_connectionTimer = null;
		if (app != null)
		{
			try
			{
				await app.StopAsync(TimeSpan.FromSeconds(3L));
			}
			catch (Exception exception)
			{
				logger.LogDebug(exception, "Local sync listener stop failed.");
			}
			await app.DisposeAsync();
		}
		SetStatus(new LocalSyncStatus(LocalSyncState.Stopped, "Sync station is stopped"));
	}

	public ValueTask DisposeAsync()
	{
		return new ValueTask(StopAsync());
	}

	private static string Describe(Exception exception, int port)
	{
		if (exception is SocketException socket)
		{
			return DescribeSocket(socket, port);
		}
		if (exception.InnerException is SocketException socket2)
		{
			return DescribeSocket(socket2, port);
		}
		if (exception is UnauthorizedAccessException)
		{
			return $"Windows denied access to port {port}. Run PharmaBill once as administrator or ask IT to allow it.";
		}
		return "Listener failed: " + exception.Message;
	}

	private static string DescribeSocket(SocketException socket, int port)
	{
		return socket.SocketErrorCode switch
		{
			SocketError.AddressAlreadyInUse => $"Port {port} is already used by another program. Close it and restart PharmaBill.", 
			SocketError.AccessDenied => $"Windows denied access to port {port}. Run PharmaBill once as administrator or ask IT to allow it.", 
			_ => $"Network error {(int)socket.SocketErrorCode} while opening port {port}.", 
		};
	}

	private LocalSyncStatus ListeningStatus()
	{
		return new LocalSyncStatus(LocalSyncState.Listening, $"Listening on port {Port} (all adapters)", $"Phone URL: http://<this-pc-ip>:{Port}/api/sync/ping — if that fails, use Check / Allow Firewall Rule for TCP {Port} on private networks.");
	}

	private void RefreshConnectionState()
	{
		LocalSyncState state = _status.State;
		if ((uint)(state - 1) <= 1u)
		{
			long num = Interlocked.Read(in _lastDeviceSeenTicks);
			bool flag = num != 0L && (double)(Environment.TickCount64 - num) < ConnectionTimeout.TotalMilliseconds;
			if (flag && _status.State == LocalSyncState.Listening)
			{
				SetStatus(new LocalSyncStatus(LocalSyncState.DeviceConnected, "Mobile Device Connected"));
			}
			else if (!flag && _status.State == LocalSyncState.DeviceConnected)
			{
				SetStatus(ListeningStatus());
			}
		}
	}

	private void MarkDeviceSeen()
	{
		Interlocked.Exchange(ref _lastDeviceSeenTicks, Math.Max(1L, Environment.TickCount64));
		if (_status.State == LocalSyncState.Listening)
		{
			SetStatus(new LocalSyncStatus(LocalSyncState.DeviceConnected, "Mobile Device Connected"));
		}
	}

	private void SetStatus(LocalSyncStatus status)
	{
		_status = status;
		StatusChanged?.Invoke(status);
	}

	private static string NewToken()
	{
		return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
	}

	private bool IsAuthorized(HttpContext context)
	{
		string text = context.Request.Headers["X-Sync-Token"].ToString();
		if (string.IsNullOrEmpty(text))
		{
			string text2 = context.Request.Headers.Authorization.ToString();
			text = (text2.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? text2.Substring(7).Trim() : string.Empty);
		}
		byte[] array = SHA256.HashData(Encoding.UTF8.GetBytes(text));
		return CryptographicOperations.FixedTimeEquals(right: SHA256.HashData(Encoding.UTF8.GetBytes(_token)), left: array);
	}

	private void CaptureBoundPort(WebApplication app)
	{
		foreach (string url in app.Urls)
		{
			if (Uri.TryCreate(url, UriKind.Absolute, out Uri result) && result.Port > 0)
			{
				Port = result.Port;
				break;
			}
		}
	}

	private void MapEndpoints(WebApplication app)
	{
		app.MapGet("/health", (Func<IResult>)(() => Results.Json(new
		{
			status = "ok",
			deviceId = deviceId.Value
		}, Json)));
		app.MapGet("/api/sync/ping", (Func<IResult>)(() => Results.Json(new
		{
			status = "ok",
			deviceId = deviceId.Value
		}, Json)));
		RouteGroupBuilder routeGroupBuilder = app.MapGroup("/api/sync");
		routeGroupBuilder.AddEndpointFilter(async (EndpointFilterInvocationContext invocation, EndpointFilterDelegate next) =>
		{
			if (!IsAuthorized(invocation.HttpContext))
			{
				return Results.Json(new
				{
					error = "Invalid or missing sync token."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)401);
			}
			MarkDeviceSeen();
			return await next(invocation);
		});
		routeGroupBuilder.MapGet("/handshake", new Func<CancellationToken, Task<IResult>>(HandshakeAsync));
		routeGroupBuilder.MapGet("/pull", new Func<HttpContext, CancellationToken, Task<IResult>>(PullPendingAsync));
		routeGroupBuilder.MapGet("/pull-catalog", new Func<HttpContext, CancellationToken, Task<IResult>>(PullCatalogAsync));
		routeGroupBuilder.MapGet("/pull-bills", new Func<HttpContext, CancellationToken, Task<IResult>>(PullBillsAsync));
		routeGroupBuilder.MapPost("/ack", new Func<HttpContext, CancellationToken, Task<IResult>>(AckPendingAsync));
		routeGroupBuilder.MapPost("/exchange", new Func<HttpContext, CancellationToken, Task<IResult>>(ExchangeAsync));
		routeGroupBuilder.MapPost("/push-bills", new Func<HttpContext, CancellationToken, Task<IResult>>(PushBillsAsync));
	}

	private async Task<IResult> HandshakeAsync(CancellationToken cancellationToken)
	{
		IResult result;
		await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
		{
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			PharmacyProfile profile = await context.PharmacyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
			if (profile == null)
			{
				result = Results.Json(new
				{
					error = "Pharmacy is not set up on this PC."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)409);
			}
			else
			{
				int pendingChanges = await context.ChangeLogs.AsNoTracking().CountAsync((ChangeLog change) => (int)change.SyncState == 0, cancellationToken);
				result = Results.Json(new
				{
					protocolVersion = 1,
					storeId = profile.Id,
					deviceId = deviceId.Value,
					pharmacy = PharmacyPayload(profile),
					pendingChanges = pendingChanges,
					hlc = clock.Now(),
					serverTimeUtc = DateTime.UtcNow
				}, Json);
			}
		}
		return result;
	}

	private async Task<IResult> PullPendingAsync(HttpContext http, CancellationToken cancellationToken)
	{
		int limit = ParsePullLimit(http.Request.Query["limit"]);
		DateTime? afterUtc = ParseAfterCursor(http.Request.Query["after"].ToString());
		IResult result;
		await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
		{
			PharmaBillDbContext requiredService = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			result = Results.Json(ToPendingPullResponse(await LoadPendingPageAsync(requiredService, afterUtc, limit, cancellationToken), 0), Json);
		}
		return result;
	}

	private async Task<IResult> AckPendingAsync(HttpContext http, CancellationToken cancellationToken)
	{
		List<Guid> changeIds;
		try
		{
			using JsonDocument jsonDocument = await JsonDocument.ParseAsync(http.Request.Body, default, cancellationToken);
			changeIds = ReadAcknowledgeIds(jsonDocument.RootElement);
		}
		catch (Exception ex) when ((ex is JsonException || ex is Microsoft.AspNetCore.Http.BadHttpRequestException) ? true : false)
		{
			return Results.Json(new
			{
				error = "Body must be { \"changeIds\": [\"...\"] } or { \"acknowledgeChangeIds\": [\"...\"] }."
			}, (JsonSerializerOptions?)null, (string?)null, (int?)400);
		}
		IResult result;
		await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
		{
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			result = Results.Json(new
			{
				acknowledged = await AcknowledgePendingAsync(context, changeIds, cancellationToken),
				pendingRemaining = await context.ChangeLogs.AsNoTracking().CountAsync((ChangeLog change) => (int)change.SyncState == 0, cancellationToken),
				hlc = clock.Now()
			}, Json);
		}
		return result;
	}

	private async Task<IResult> ExchangeAsync(HttpContext http, CancellationToken cancellationToken)
	{
		List<Guid> acknowledgeIds = new List<Guid>();
		int limit = 1000;
		try
		{
			using JsonDocument jsonDocument = await JsonDocument.ParseAsync(http.Request.Body, default, cancellationToken);
			JsonElement rootElement = jsonDocument.RootElement;
			acknowledgeIds = ReadAcknowledgeIds(rootElement);
			if (rootElement.ValueKind == JsonValueKind.Object && rootElement.TryGetProperty("limit", out var value) && value.TryGetInt32(out var value2))
			{
				limit = Math.Clamp(value2, 1, 1000);
			}
		}
		catch (JsonException)
		{
		}
		catch (Microsoft.AspNetCore.Http.BadHttpRequestException ex2)
		{
			return Results.Json(new
			{
				error = ex2.Message
			}, (JsonSerializerOptions?)null, (string?)null, (int?)400);
		}
		IResult result;
		await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
		{
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			result = Results.Json(ToPendingPullResponse(acknowledged: await AcknowledgePendingAsync(context, acknowledgeIds, cancellationToken), page: await LoadPendingPageAsync(context, null, limit, cancellationToken)), Json);
		}
		return result;
	}

	private object ToPendingPullResponse(PendingPage page, int acknowledged)
	{
		return new
		{
			changes = page.Changes,
			changeIds = page.ChangeIds,
			hasMore = page.HasMore,
			pendingRemaining = page.PendingRemaining,
			delivered = page.ChangeIds.Count,
			acknowledged = acknowledged,
			nextAfter = page.NextAfter,
			hlc = clock.Now(),
			serverTimeUtc = DateTime.UtcNow
		};
	}

	private static int ParsePullLimit(string? text)
	{
		if (int.TryParse(text, out var result))
		{
			return Math.Clamp(result, 1, 1000);
		}
		return 1000;
	}

	private static DateTime? ParseAfterCursor(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		if (!DateTime.TryParse(text.Split('|', 2, StringSplitOptions.TrimEntries)[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var result))
		{
			return null;
		}
		return DateTime.SpecifyKind(result, DateTimeKind.Utc);
	}

	private static List<Guid> ReadAcknowledgeIds(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Object)
		{
			return new List<Guid>();
		}
		JsonElement value = default;
		if (root.TryGetProperty("acknowledgeChangeIds", out value) || root.TryGetProperty("changeIds", out value) || root.TryGetProperty("acknowledgedChangeIds", out value))
		{
			if (value.ValueKind != JsonValueKind.Array)
			{
				return new List<Guid>();
			}
			return (from item in value.EnumerateArray()
				select (item.ValueKind != JsonValueKind.String || !Guid.TryParse(item.GetString(), out var result)) ? Guid.Empty : result into id
				where id != Guid.Empty
				select id).Distinct().ToList();
		}
		return new List<Guid>();
	}

	private async Task<int> AcknowledgePendingAsync(PharmaBillDbContext context, IReadOnlyCollection<Guid> changeIds, CancellationToken cancellationToken)
	{
		if (changeIds.Count == 0)
		{
			return 0;
		}
		HashSet<Guid> idSet = changeIds.ToHashSet();
		List<ChangeLog> rows = await context.ChangeLogs.Where((ChangeLog change) => idSet.Contains(change.Id) && (int)change.SyncState == 0).ToListAsync(cancellationToken);
		foreach (ChangeLog item in rows)
		{
			item.SyncState = SyncState.Synced;
			item.UpdatedAtUtc = DateTime.UtcNow;
		}
		if (rows.Count > 0)
		{
			await context.SaveChangesAsync(cancellationToken);
		}
		return rows.Count;
	}

	private async Task<PendingPage> LoadPendingPageAsync(PharmaBillDbContext context, DateTime? afterUtc, int limit, CancellationToken cancellationToken)
	{
		IQueryable<ChangeLog> source = from change in context.ChangeLogs.IgnoreQueryFilters().AsNoTracking()
			where (int)change.SyncState == 0
			select change;
		if (afterUtc.HasValue)
		{
			DateTime cursor = afterUtc.Value;
			source = source.Where((ChangeLog change) => change.ChangedAtUtc > cursor);
		}
		List<ChangeLog> page = await (from change in source
			orderby change.ChangedAtUtc, change.Id
			select change).Take(limit + 1).ToListAsync(cancellationToken);
		bool hasMore = page.Count > limit;
		if (hasMore)
		{
			page.RemoveAt(page.Count - 1);
		}
		if (hasMore && page.Count > 0)
		{
			DateTime boundary = page[page.Count - 1].ChangedAtUtc;
			HashSet<Guid> known = page.Select((ChangeLog change) => change.Id).ToHashSet();
			foreach (ChangeLog item in (await (from change in context.ChangeLogs.IgnoreQueryFilters().AsNoTracking()
				where (int)change.SyncState == 0 && change.ChangedAtUtc == boundary
				select change).ToListAsync(cancellationToken)).Where((ChangeLog change) => known.Add(change.Id)))
			{
				page.Add(item);
			}
			hasMore = await context.ChangeLogs.IgnoreQueryFilters().AsNoTracking().AnyAsync((ChangeLog change) => (int)change.SyncState == 0 && change.ChangedAtUtc > boundary, cancellationToken);
		}
		int pendingRemaining = await context.ChangeLogs.AsNoTracking().CountAsync((ChangeLog change) => (int)change.SyncState == 0, cancellationToken);
		List<Guid> changeIds = page.Select((ChangeLog change) => change.Id).ToList();
		List<SyncChangeEnvelope> changes = page.Select(serializer.FromChangeLog).ToList();
		string nextAfter = null;
		if (page.Count > 0)
		{
			ChangeLog changeLog = page[page.Count - 1];
			nextAfter = $"{changeLog.ChangedAtUtc.ToUniversalTime():O}|{changeLog.Id:D}";
		}
		return new PendingPage(changes, changeIds, hasMore, pendingRemaining, nextAfter);
	}

	private static object PharmacyPayload(PharmacyProfile profile)
	{
		return new
		{
			storeId = profile.Id,
			name = profile.Name,
			address = profile.Address,
			phone = profile.Phone,
			gstin = profile.Gstin,
			businessMode = profile.BusinessMode.ToString(),
			invoicePrefix = profile.InvoicePrefix
		};
	}

	private async Task<IResult> PullCatalogAsync(HttpContext http, CancellationToken cancellationToken)
	{
		IQueryCollection query = http.Request.Query;
		DateTime since = DateTime.MinValue;
		string text = query["last_sync_timestamp"].ToString();
		if (!string.IsNullOrWhiteSpace(text) && !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out since))
		{
			return Results.Json(new
			{
				error = "last_sync_timestamp must be an ISO-8601 UTC timestamp."
			}, (JsonSerializerOptions?)null, (string?)null, (int?)400);
		}
		int limit = (int.TryParse(query["limit"], out var result) ? Math.Clamp(result, 1, 1000) : 500);
		since = DateTime.SpecifyKind(since, DateTimeKind.Utc);
		IResult result2;
		await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
		{
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			IQueryable<ChangeLog> baseQuery = from change in context.ChangeLogs.IgnoreQueryFilters().AsNoTracking()
				where PullableEntities.Contains(change.EntityName)
				select change;
			List<ChangeLog> page = await (from change in baseQuery
				where change.ChangedAtUtc > since
				orderby change.ChangedAtUtc, change.Id
				select change).Take(limit + 1).ToListAsync(cancellationToken);
			bool hasMore = page.Count > limit;
			if (hasMore)
			{
				page.RemoveAt(page.Count - 1);
			}
			if (hasMore && page.Count > 0)
			{
				DateTime boundary = page[page.Count - 1].ChangedAtUtc;
				HashSet<Guid> known = page.Select((ChangeLog change) => change.Id).ToHashSet();
				page.AddRange((await baseQuery.Where((ChangeLog change) => change.ChangedAtUtc == boundary).ToListAsync(cancellationToken)).Where((ChangeLog change) => known.Add(change.Id)));
				hasMore = await baseQuery.AnyAsync((ChangeLog change) => change.ChangedAtUtc > boundary, cancellationToken);
			}
			List<SyncChangeEnvelope> changes = page.Select(serializer.FromChangeLog).ToList();
			DateTime dateTime;
			if (page.Count <= 0)
			{
				dateTime = since;
			}
			else
			{
				dateTime = page[page.Count - 1].ChangedAtUtc;
			}
			DateTime next = dateTime;
			DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
			List<Drug> products = await (from drug in context.Drugs.AsNoTracking()
				where drug.UpdatedAtUtc > since
				orderby drug.Name
				select drug).Take(1000).ToListAsync(cancellationToken);
			List<Batch> batches = await (from batch in context.Batches.AsNoTracking()
				where batch.UpdatedAtUtc > since && batch.Quantity > 0m && (batch.ExpiryDate == null || batch.ExpiryDate >= today)
				orderby batch.ExpiryDate ?? DateOnly.MaxValue, batch.Id
				select batch).Take(1000).ToListAsync(cancellationToken);
			var tax = (from drug in products
				where drug.GstRate.HasValue || !string.IsNullOrWhiteSpace(drug.HsnCode)
				select new
				{
					drugId = drug.Id,
					hsnCode = drug.HsnCode,
					gstRatePercent = drug.GstRate
				}).ToList();
			PharmacyProfile profile = await context.PharmacyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
			BillSnapshot billSnapshot = await LoadBillSnapshotAsync(context, since, limit, cancellationToken);
			List<StorageLocation> storageLocations = await (from location in context.StorageLocations.AsNoTracking()
				where location.IsActive
				orderby location.IsDefaultRetailLocation descending, location.Name
				select location).ToListAsync(cancellationToken);
			List<StockLocationBalance> locationBalances = await (from balance in context.StockLocationBalances.AsNoTracking()
				where balance.Quantity > 0m
				orderby balance.LocationId, balance.BatchId
				select balance).Take(1000).ToListAsync(cancellationToken);
			Guid[] balanceBatchIds = locationBalances.Select((StockLocationBalance balance) => balance.BatchId).Distinct().ToArray();
			Dictionary<Guid, Batch> balanceBatches = await (from batch in context.Batches.AsNoTracking()
				where balanceBatchIds.Contains(batch.Id)
				select batch).ToDictionaryAsync((Batch batch) => batch.Id, cancellationToken);
			Guid[] balanceDrugIds = locationBalances.Select((StockLocationBalance balance) => balance.DrugId).Distinct().ToArray();
			Dictionary<Guid, Drug> balanceDrugs = await (from drug in context.Drugs.AsNoTracking()
				where balanceDrugIds.Contains(drug.Id)
				select drug).ToDictionaryAsync((Drug drug) => drug.Id, cancellationToken);
			Dictionary<Guid, StorageLocation> locationById = storageLocations.ToDictionary((StorageLocation location) => location.Id);
			result2 = Results.Json(new
			{
				changes = changes,
				pharmacy = ((profile == null) ? null : PharmacyPayload(profile)),
				products = products.Select(ToWire).ToList(),
				batches = batches.Select(ToWire).ToList(),
				tax = tax,
				storageLocations = storageLocations.Select((StorageLocation location) => new
				{
					id = location.Id,
					name = location.Name,
					code = location.Code,
					isDefault = location.IsDefaultRetailLocation,
					isActive = location.IsActive
				}).ToList(),
				stockLocationBalances = locationBalances.Select((StockLocationBalance balance) =>
				{
					balanceBatches.TryGetValue(balance.BatchId, out var value);
					balanceDrugs.TryGetValue(balance.DrugId, out var value2);
					locationById.TryGetValue(balance.LocationId, out var value3);
					return new
					{
						locationId = balance.LocationId,
						locationCode = value3?.Code,
						batchId = balance.BatchId,
						batchNo = value?.BatchNo,
						drugId = balance.DrugId,
						drugName = value2?.Name,
						expiryDate = value?.ExpiryDate?.ToString("MM/yy", CultureInfo.InvariantCulture),
						quantity = balance.Quantity
					};
				}).ToList(),
				sales = billSnapshot.Sales.Select(ToWire).ToList(),
				saleItems = billSnapshot.SaleItems.Select(ToWire).ToList(),
				patients = billSnapshot.Patients.Select(ToWire).ToList(),
				nextSince = DateTime.SpecifyKind(next, DateTimeKind.Utc),
				hasMore = hasMore,
				hlc = clock.Now()
			}, Json);
		}
		return result2;
	}

	private async Task<IResult> PullBillsAsync(HttpContext http, CancellationToken cancellationToken)
	{
		IQueryCollection query = http.Request.Query;
		DateTime since = DateTime.MinValue;
		string text = query["last_sync_timestamp"].ToString();
		if (!string.IsNullOrWhiteSpace(text) && !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out since))
		{
			return Results.Json(new
			{
				error = "last_sync_timestamp must be an ISO-8601 UTC timestamp."
			}, (JsonSerializerOptions?)null, (string?)null, (int?)400);
		}
		int limit = (int.TryParse(query["limit"], out var result) ? Math.Clamp(result, 1, 1000) : 500);
		since = DateTime.SpecifyKind(since, DateTimeKind.Utc);
		IResult result2;
		await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
		{
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			PharmacyProfile profile = await context.PharmacyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
			if (profile == null)
			{
				result2 = Results.Json(new
				{
					error = "Pharmacy is not set up on this PC."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)409);
			}
			else
			{
				BillSnapshot billSnapshot = await LoadBillSnapshotAsync(context, since, limit, cancellationToken);
				IQueryable<ChangeLog> baseQuery = from change in context.ChangeLogs.IgnoreQueryFilters().AsNoTracking()
					where PullableBillEntities.Contains(change.EntityName)
					select change;
				List<ChangeLog> page = await (from change in baseQuery
					where change.ChangedAtUtc > since
					orderby change.ChangedAtUtc, change.Id
					select change).Take(limit + 1).ToListAsync(cancellationToken);
				bool flag = page.Count > limit;
				if (flag)
				{
					page.RemoveAt(page.Count - 1);
				}
				if (flag && page.Count > 0)
				{
					DateTime boundary = page[page.Count - 1].ChangedAtUtc;
					HashSet<Guid> known = page.Select((ChangeLog change) => change.Id).ToHashSet();
					page.AddRange((await baseQuery.Where((ChangeLog change) => change.ChangedAtUtc == boundary).ToListAsync(cancellationToken)).Where((ChangeLog change) => known.Add(change.Id)));
					flag = await baseQuery.AnyAsync((ChangeLog change) => change.ChangedAtUtc > boundary, cancellationToken) || billSnapshot.HasMore;
				}
				else
				{
					flag = flag || billSnapshot.HasMore;
				}
				DateTime[] array = new DateTime[2];
				DateTime dateTime;
				if (page.Count <= 0)
				{
					dateTime = since;
				}
				else
				{
					dateTime = page[page.Count - 1].ChangedAtUtc;
				}
				array[0] = dateTime;
				array[1] = billSnapshot.NextSince;
				DateTime value = array.Max();
				result2 = Results.Json(new
				{
					pharmacy = PharmacyPayload(profile),
					storeId = profile.Id,
					deviceId = deviceId.Value,
					changes = page.Select(serializer.FromChangeLog).ToList(),
					sales = billSnapshot.Sales.Select(ToWire).ToList(),
					saleItems = billSnapshot.SaleItems.Select(ToWire).ToList(),
					patients = billSnapshot.Patients.Select(ToWire).ToList(),
					wholesaleInvoices = billSnapshot.WholesaleInvoices.Select(ToWire).ToList(),
					wholesaleInvoiceItems = billSnapshot.WholesaleInvoiceItems.Select(ToWire).ToList(),
					nextSince = DateTime.SpecifyKind(value, DateTimeKind.Utc),
					hasMore = flag,
					hlc = clock.Now(),
					serverTimeUtc = DateTime.UtcNow
				}, Json);
			}
		}
		return result2;
	}

	private static async Task<BillSnapshot> LoadBillSnapshotAsync(PharmaBillDbContext context, DateTime since, int limit, CancellationToken cancellationToken)
	{
		List<Sale> salesPage = await (from sale in context.Sales.AsNoTracking()
			where sale.UpdatedAtUtc > since
			orderby sale.SaleAtUtc, sale.Id
			select sale).Take(limit + 1).ToListAsync(cancellationToken);
		bool hasMore = salesPage.Count > limit;
		if (hasMore)
		{
			salesPage.RemoveAt(salesPage.Count - 1);
		}
		HashSet<Guid> saleIds = salesPage.Select((Sale sale) => sale.Id).ToHashSet();
		List<SaleItem> list = ((saleIds.Count != 0) ? (await (from item in context.SaleItems.AsNoTracking()
			where saleIds.Contains(item.SaleId)
			orderby item.CreatedAtUtc, item.Id
			select item).ToListAsync(cancellationToken)) : new List<SaleItem>());
		List<SaleItem> saleItems = list;
		List<Guid> patientIds = (from sale in salesPage
			where sale.PatientId.HasValue
			select sale.PatientId.Value).Distinct().ToList();
		List<Patient> list2 = ((patientIds.Count != 0) ? (await (from patient in context.Patients.AsNoTracking()
			where patientIds.Contains(patient.Id)
			select patient).ToListAsync(cancellationToken)) : new List<Patient>());
		List<Patient> patients = list2;
		List<WholesaleInvoice> wholesalePage = await (from invoice in context.WholesaleInvoices.AsNoTracking()
			where invoice.UpdatedAtUtc > since
			orderby invoice.InvoiceAtUtc, invoice.Id
			select invoice).Take(limit + 1).ToListAsync(cancellationToken);
		bool wholesaleHasMore = wholesalePage.Count > limit;
		if (wholesaleHasMore)
		{
			wholesalePage.RemoveAt(wholesalePage.Count - 1);
		}
		HashSet<Guid> wholesaleIds = wholesalePage.Select((WholesaleInvoice invoice) => invoice.Id).ToHashSet();
		List<WholesaleInvoiceItem> list3 = ((wholesaleIds.Count != 0) ? (await (from item in context.WholesaleInvoiceItems.AsNoTracking()
			where wholesaleIds.Contains(item.WholesaleInvoiceId)
			orderby item.CreatedAtUtc, item.Id
			select item).ToListAsync(cancellationToken)) : new List<WholesaleInvoiceItem>());
		List<WholesaleInvoiceItem> wholesaleInvoiceItems = list3;
		DateTime[] array = new DateTime[2];
		DateTime dateTime;
		if (salesPage.Count <= 0)
		{
			dateTime = since;
		}
		else
		{
			dateTime = salesPage[salesPage.Count - 1].UpdatedAtUtc;
		}
		array[0] = dateTime;
		DateTime dateTime2;
		if (wholesalePage.Count <= 0)
		{
			dateTime2 = since;
		}
		else
		{
			dateTime2 = wholesalePage[wholesalePage.Count - 1].UpdatedAtUtc;
		}
		array[1] = dateTime2;
		DateTime nextSince = array.Max();
		return new BillSnapshot(salesPage, saleItems, patients, wholesalePage, wholesaleInvoiceItems, nextSince, hasMore | wholesaleHasMore);
	}

	private JsonElement ToWire(EntityBase entity)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(serializer.SerializeEntity(entity));
		return jsonDocument.RootElement.Clone();
	}

	private async Task<IResult> PushBillsAsync(HttpContext http, CancellationToken cancellationToken)
	{
		List<SyncChangeEnvelope> changes;
		try
		{
			using JsonDocument jsonDocument = await JsonDocument.ParseAsync(http.Request.Body, default, cancellationToken);
			JsonElement element = jsonDocument.RootElement;
			if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("changes", out var value))
			{
				element = value;
			}
			if (element.ValueKind != JsonValueKind.Array)
			{
				return Results.Json(new
				{
					error = "Body must be { \"changes\": [...] } or an array of changes."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)400);
			}
			changes = element.Deserialize<List<SyncChangeEnvelope>>(Json) ?? new List<SyncChangeEnvelope>();
		}
		catch (Exception ex) when ((ex is JsonException || ex is Microsoft.AspNetCore.Http.BadHttpRequestException) ? true : false)
		{
			return Results.Json(new
			{
				error = "Request body is not valid sync JSON."
			}, (JsonSerializerOptions?)null, (string?)null, (int?)400);
		}
		if (changes.Count == 0)
		{
			return Results.Json(new
			{
				applied = 0,
				duplicates = 0,
				conflicts = 0,
				stockConflicts = 0
			}, Json);
		}
		if (changes.Count > 500)
		{
			return Results.Json(new
			{
				error = $"At most {500} changes per request."
			}, (JsonSerializerOptions?)null, (string?)null, (int?)413);
		}
		List<string> list = (from change in changes
			select change.Entity into entity
			where !PushableEntities.Contains(entity)
			select entity).Distinct().ToList();
		if (list.Count > 0)
		{
			return Results.Json(new
			{
				error = "Entities not accepted from mobile: " + string.Join(", ", list) + "."
			}, (JsonSerializerOptions?)null, (string?)null, (int?)400);
		}
		if (changes.Any((SyncChangeEnvelope change) => change.OriginDeviceId == deviceId.Value))
		{
			return Results.Json(new
			{
				error = "Changes must originate from the mobile device."
			}, (JsonSerializerOptions?)null, (string?)null, (int?)400);
		}
		IResult result;
		await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
		{
			if (!(await scope.ServiceProvider.GetRequiredService<IEntitlementService>().CanPerformAsync(ProtectedOperation.CreateBill, cancellationToken)))
			{
				result = Results.Json(new
				{
					error = "Billing is read-only on this PC (licence or subscription ended)."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			else
			{
				try
				{
					SyncImportResult syncImportResult = await scope.ServiceProvider.GetRequiredService<ChangeApplier>().ApplyAsync(changes, null, cancellationToken);
					result = Results.Json(new
					{
						applied = syncImportResult.Applied,
						duplicates = syncImportResult.Duplicates,
						conflicts = syncImportResult.Conflicts,
						stockConflicts = syncImportResult.StockConflicts,
						hlc = clock.Now()
					}, Json);
				}
				catch (InvalidDataException ex2)
				{
					logger.LogWarning(ex2, "Rejected mobile push.");
					result = Results.Json(new
					{
						error = ex2.Message
					}, (JsonSerializerOptions?)null, (string?)null, (int?)400);
				}
				catch (Exception ex3) when (!(ex3 is OperationCanceledException))
				{
					logger.LogError(ex3, "Mobile push failed.");
					result = Results.Json(new
					{
						error = "The PC could not apply the bills. Nothing was saved."
					}, (JsonSerializerOptions?)null, (string?)null, (int?)500);
				}
			}
		}
		return result;
	}
}

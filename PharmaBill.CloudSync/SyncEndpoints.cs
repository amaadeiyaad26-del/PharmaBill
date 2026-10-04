using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaBill.CloudSync.Data;
using PharmaBill.Core.Sync;

namespace PharmaBill.CloudSync;

public sealed class CloudSyncOptions
{
    public const int MaxPushBatch = 500;
    public const int MaxPullLimit = 500;
    public string Provider { get; set; } = "Sqlite";
    public string BlobRoot { get; set; } = "blobs";
    public string? AdminKey { get; set; }
    public long MaxFileBytes { get; set; } = 25 * 1024 * 1024;
    public S3Options? S3 { get; set; }
}

public sealed class S3Options
{
    public string Bucket { get; set; } = string.Empty;
    public string? ServiceUrl { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string Region { get; set; } = "us-east-1";
}

public static class SyncEndpoints
{
    private static readonly SemaphoreSlim PushGate = new(1, 1);

    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var sync = app.MapGroup("/api/sync").RequireAuthorization();
        sync.MapPost("/push", PushAsync).WithMetadata(new RequestSizeLimitAttribute(16 * 1024 * 1024));
        sync.MapGet("/pull", PullAsync);
        sync.MapPost("/files", UploadFileAsync).DisableAntiforgery();
        sync.MapGet("/files/{hash}", DownloadFileAsync);

        var admin = app.MapGroup("/api/admin");
        admin.MapPost("/tenants", CreateTenantAsync);
        admin.MapPost("/branches", CreateBranchAsync);
    }

    private static async Task<IResult> PushAsync(
        HttpContext http, PushRequest request, CloudDbContext db, ServerClock clock, CancellationToken ct)
    {
        var caller = CallerContext.From(http.User);
        if (request.DeviceId == Guid.Empty || request.Changes is null)
            return Results.BadRequest(new { error = "deviceId and changes are required." });
        if (request.Changes.Count > CloudSyncOptions.MaxPushBatch)
            return Results.BadRequest(new { error = $"At most {CloudSyncOptions.MaxPushBatch} changes per push." });

        var tenantBranches = await db.Branches.AsNoTracking()
            .Where(b => b.TenantId == caller.TenantId).Select(b => b.Id).ToListAsync(ct);
        foreach (var change in request.Changes)
        {
            var problem = Validate(change, tenantBranches);
            if (problem is not null) return Results.BadRequest(new { error = problem, changeId = change.ChangeId });
        }

        await PushGate.WaitAsync(ct);
        try
        {
            var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == request.DeviceId, ct);
            if (device is not null && (device.BranchId != caller.BranchId || device.TenantId != caller.TenantId))
                return Results.Json(new { error = "Device belongs to another branch." }, statusCode: 403);
            if (device is null)
            {
                device = new Device { Id = request.DeviceId, TenantId = caller.TenantId, BranchId = caller.BranchId };
                db.Devices.Add(device);
            }

            device.LastSeenAtUtc = DateTime.UtcNow;

            var distinct = request.Changes.GroupBy(c => c.ChangeId).Select(g => g.First()).ToList();
            var ids = distinct.Select(c => c.ChangeId).ToList();
            var existing = (await db.ChangeLog.AsNoTracking()
                .Where(c => c.TenantId == caller.TenantId && ids.Contains(c.ChangeId))
                .Select(c => c.ChangeId).ToListAsync(ct)).ToHashSet();

            var accepted = 0;
            foreach (var change in distinct.Where(c => !existing.Contains(c.ChangeId))
                         .OrderBy(c => c.HlcStamp, Comparer<string>.Create(HybridLogicalClockState.Compare)))
            {
                var stamp = clock.Receive(change.HlcStamp);
                var parsed = HybridLogicalClockState.Parse(stamp);
                var shared = change.SharedWithBranchIds?.Where(b => b != caller.BranchId).Distinct().ToList();
                db.ChangeLog.Add(new CloudChange
                {
                    TenantId = caller.TenantId,
                    ChangeId = change.ChangeId,
                    BranchId = caller.BranchId,
                    OriginDeviceId = change.OriginDeviceId,
                    Entity = change.Entity,
                    EntityId = change.EntityId,
                    Operation = change.Operation,
                    SchemaVersion = change.SchemaVersion,
                    HlcStamp = change.HlcStamp,
                    ChangedAtUtc = DateTime.SpecifyKind(change.ChangedAtUtc, DateTimeKind.Utc),
                    PayloadJson = change.Payload.GetRawText(),
                    Scope = ScopeRules.Classify(change.Entity, shared),
                    SharedBranchIds = shared is { Count: > 0 }
                        ? "," + string.Join(',', shared.Select(b => b.ToString("N"))) + ","
                        : string.Empty,
                    ServerHlc = stamp,
                    ServerMs = parsed.PhysicalMilliseconds,
                    ServerCounter = parsed.LogicalCounter
                });
                accepted++;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new PushAck(accepted, distinct.Count - accepted, clock.Now(), ids));
        }
        finally
        {
            PushGate.Release();
        }
    }

    private static string? Validate(CloudChangeDto change, IReadOnlyCollection<Guid> tenantBranches)
    {
        if (change.ChangeId == Guid.Empty || change.EntityId == Guid.Empty) return "Ids must not be empty.";
        if (string.IsNullOrWhiteSpace(change.Entity) || change.Entity.Length > 100 ||
            !change.Entity.All(char.IsLetterOrDigit)) return "Invalid entity name.";
        if (change.Operation is not ("Upsert" or "SoftDeleted")) return "Operation must be Upsert or SoftDeleted.";
        if (change.Payload.ValueKind != JsonValueKind.Object) return "Payload must be a JSON object.";
        try
        {
            HybridLogicalClockState.Parse(change.HlcStamp);
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            return "Invalid HLC stamp.";
        }

        if (change.SharedWithBranchIds?.Any(b => !tenantBranches.Contains(b)) == true)
            return "Shared branches must belong to the same tenant.";
        return null;
    }

    private static async Task<IResult> PullAsync(
        HttpContext http,
        CloudDbContext db,
        ServerClock clock,
        string? since,
        Guid branchId,
        int? limit,
        CancellationToken ct)
    {
        var caller = CallerContext.From(http.User);
        if (branchId != caller.BranchId)
            return Results.Json(new { error = "branchId does not match the token." }, statusCode: 403);

        long ms = 0, counter = 0;
        if (!string.IsNullOrWhiteSpace(since))
        {
            try
            {
                var parsed = HybridLogicalClockState.Parse(since);
                ms = parsed.PhysicalMilliseconds;
                counter = parsed.LogicalCounter;
            }
            catch (FormatException)
            {
                return Results.BadRequest(new { error = "Invalid since stamp." });
            }
        }

        _ = Guid.TryParse(http.Request.Headers["X-Device-Id"], out var deviceId);
        var take = Math.Clamp(limit ?? 200, 1, CloudSyncOptions.MaxPullLimit);
        var marker = "," + caller.BranchId.ToString("N") + ",";

        var rows = await db.ChangeLog.AsNoTracking()
            .Where(c => c.TenantId == caller.TenantId &&
                        (c.ServerMs > ms || (c.ServerMs == ms && c.ServerCounter > counter)) &&
                        c.OriginDeviceId != deviceId &&
                        (c.Scope == ChangeScope.Global ||
                         c.BranchId == caller.BranchId ||
                         (c.Scope == ChangeScope.Shared && c.SharedBranchIds.Contains(marker))))
            .OrderBy(c => c.ServerMs).ThenBy(c => c.ServerCounter)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > take;
        var page = rows.Take(take).ToList();
        var next = page.Count > 0 ? page[^1].ServerHlc : since ?? string.Empty;
        var dtos = page.Select(c => new CloudChangeDto(
            c.ChangeId, c.Entity, c.EntityId, c.Operation, c.SchemaVersion, c.HlcStamp, c.OriginDeviceId,
            DateTime.SpecifyKind(c.ChangedAtUtc, DateTimeKind.Utc),
            JsonDocument.Parse(c.PayloadJson).RootElement.Clone())).ToList();
        return Results.Ok(new PullResponse(dtos, next, hasMore, clock.Now()));
    }

    private static async Task<IResult> UploadFileAsync(
        HttpContext http,
        CloudDbContext db,
        IBlobStore blobs,
        Microsoft.Extensions.Options.IOptions<CloudSyncOptions> options,
        bool? shared,
        CancellationToken ct)
    {
        var caller = CallerContext.From(http.User);
        var declared = http.Request.Headers["X-Content-Sha256"].ToString().ToLowerInvariant();
        if (declared.Length != 64 || !declared.All(Uri.IsHexDigit))
            return Results.BadRequest(new { error = "X-Content-Sha256 header (64 hex chars) is required." });

        var max = options.Value.MaxFileBytes;
        if (http.Request.ContentLength > max) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        http.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize = max;

        var temp = Path.GetTempFileName();
        try
        {
            string actual;
            long length = 0;
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await http.Request.Body.ReadAsync(buffer, ct)) > 0)
                {
                    length += read;
                    if (length > max) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                }

                actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            if (length == 0 || actual != declared)
                return Results.BadRequest(new { error = "Content hash mismatch or empty body." });

            var existing = await db.FileBlobs.FirstOrDefaultAsync(
                f => f.TenantId == caller.TenantId && f.BranchId == caller.BranchId && f.Sha256 == actual, ct);
            if (existing is not null)
            {
                if (shared == true && !existing.IsShared)
                {
                    existing.IsShared = true;
                    await db.SaveChangesAsync(ct);
                }

                return Results.Ok(new FileUploadResult(actual, existing.Length, true));
            }

            var key = $"{caller.TenantId:N}/{caller.BranchId:N}/{actual[..2]}/{actual}";
            await using (var source = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            {
                await blobs.PutAsync(key, source, ct);
            }

            db.FileBlobs.Add(new FileBlob
            {
                TenantId = caller.TenantId,
                BranchId = caller.BranchId,
                Sha256 = actual,
                Length = length,
                StorageKey = key,
                ContentType = http.Request.ContentType ?? "application/octet-stream",
                IsShared = shared == true
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new FileUploadResult(actual, length, false));
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static async Task<IResult> DownloadFileAsync(
        HttpContext http, string hash, CloudDbContext db, IBlobStore blobs, CancellationToken ct)
    {
        var caller = CallerContext.From(http.User);
        hash = hash.ToLowerInvariant();
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) return Results.BadRequest(new { error = "Invalid hash." });

        var blob = await db.FileBlobs.AsNoTracking().FirstOrDefaultAsync(f =>
            f.TenantId == caller.TenantId && f.Sha256 == hash &&
            (f.BranchId == caller.BranchId || f.IsShared), ct);
        if (blob is null) return Results.NotFound();
        var stream = await blobs.OpenReadAsync(blob.StorageKey, ct);
        return stream is null ? Results.NotFound() : Results.Stream(stream, blob.ContentType);
    }

    private static bool IsAdmin(HttpContext http, Microsoft.Extensions.Options.IOptions<CloudSyncOptions> options)
    {
        var configured = options.Value.AdminKey;
        if (string.IsNullOrEmpty(configured)) return false;
        var supplied = http.Request.Headers["X-Admin-Key"].ToString();
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(configured)));
    }

    private static async Task<IResult> CreateTenantAsync(
        HttpContext http, CreateTenantRequest request, CloudDbContext db,
        Microsoft.Extensions.Options.IOptions<CloudSyncOptions> options, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(options.Value.AdminKey)) return Results.NotFound();
        if (!IsAdmin(http, options)) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { error = "Name is required." });
        var tenant = new Tenant { Name = request.Name.Trim() };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { tenantId = tenant.Id });
    }

    private static async Task<IResult> CreateBranchAsync(
        HttpContext http, CreateBranchRequest request, CloudDbContext db,
        Microsoft.Extensions.Options.IOptions<CloudSyncOptions> options, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(options.Value.AdminKey)) return Results.NotFound();
        if (!IsAdmin(http, options)) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name) ||
            !await db.Tenants.AnyAsync(t => t.Id == request.TenantId, ct))
            return Results.BadRequest(new { error = "Valid tenantId and name are required." });
        var branch = new Branch { TenantId = request.TenantId, Name = request.Name.Trim() };
        var (key, hash) = ApiKeys.Generate(branch.Id);
        branch.ApiKeyHash = hash;
        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new CreatedBranch(request.TenantId, branch.Id, key));
    }
}

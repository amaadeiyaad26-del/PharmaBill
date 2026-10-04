using Amazon.S3;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using PharmaBill.CloudSync;
using PharmaBill.CloudSync.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<CloudSyncOptions>(builder.Configuration.GetSection("Cloud"));
builder.Services.AddDbContext<CloudDbContext>((sp, options) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var provider = configuration["Cloud:Provider"] ?? "Sqlite";
    var connection = configuration.GetConnectionString("Cloud") ?? "Data Source=cloudsync.db";
    if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        options.UseNpgsql(connection);
    else
        options.UseSqlite(connection);
});
builder.Services.AddSingleton<ServerClock>();
builder.Services.AddSingleton<IBlobStore>(sp =>
{
    var cloud = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CloudSyncOptions>>().Value;
    if (cloud.S3 is { Bucket.Length: > 0 } s3)
    {
        var config = new AmazonS3Config { ForcePathStyle = true, AuthenticationRegion = s3.Region };
        if (!string.IsNullOrWhiteSpace(s3.ServiceUrl)) config.ServiceURL = s3.ServiceUrl;
        var client = string.IsNullOrWhiteSpace(s3.AccessKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(s3.AccessKey, s3.SecretKey, config);
        return new S3BlobStore(client, s3.Bucket);
    }

    return new LocalDiskBlobStore(cloud.BlobRoot);
});
builder.Services.AddAuthentication(ApiKeys.Scheme)
    .AddScheme<AuthenticationSchemeOptions, BranchApiKeyHandler>(ApiKeys.Scheme, _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();
    await db.Database.EnsureCreatedAsync();
    var clock = app.Services.GetRequiredService<ServerClock>();
    foreach (var stamp in await db.ChangeLog.AsNoTracking()
                 .OrderByDescending(c => c.ServerMs).ThenByDescending(c => c.ServerCounter)
                 .Select(c => c.ServerHlc).Take(1).ToListAsync())
    {
        clock.Observe(stamp);
    }
}

app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapSyncEndpoints();

app.Run();

public partial class Program;

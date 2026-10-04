using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmaBill.CloudSync.Data;

namespace PharmaBill.CloudSync;

public static class ApiKeys
{
    public const string Scheme = "BranchApiKey";
    public const string Prefix = "pbk";

    // Format: pbk_<branchId:N>_<secret>. Only the SHA-256 of the secret is stored.
    public static (string Key, string Hash) Generate(Guid branchId)
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return ($"{Prefix}_{branchId:N}_{secret}", Hash(secret));
    }

    public static string Hash(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static bool TryParse(string? key, out Guid branchId, out string secret)
    {
        branchId = Guid.Empty;
        secret = string.Empty;
        if (string.IsNullOrWhiteSpace(key)) return false;
        var parts = key.Split('_', 3);
        if (parts.Length != 3 || parts[0] != Prefix || parts[2].Length == 0 ||
            !Guid.TryParseExact(parts[1], "N", out branchId))
        {
            return false;
        }

        secret = parts[2];
        return true;
    }

    public static bool Verify(string storedHash, string secret) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(storedHash), Encoding.ASCII.GetBytes(Hash(secret)));
}

public sealed class BranchApiKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    CloudDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string TenantClaim = "tenant";
    public const string BranchClaim = "branch";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        if (!ApiKeys.TryParse(header["Bearer ".Length..].Trim(), out var branchId, out var secret))
        {
            return AuthenticateResult.Fail("Malformed token.");
        }

        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId);
        var valid = branch is not null && ApiKeys.Verify(branch.ApiKeyHash, secret);
        if (!valid || !branch!.IsActive ||
            !await db.Tenants.AnyAsync(t => t.Id == branch.TenantId && t.IsActive))
        {
            return AuthenticateResult.Fail("Invalid token.");
        }

        var identity = new ClaimsIdentity(
            [new Claim(TenantClaim, branch.TenantId.ToString()), new Claim(BranchClaim, branch.Id.ToString())],
            Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}

public sealed record CallerContext(Guid TenantId, Guid BranchId)
{
    public static CallerContext From(ClaimsPrincipal user) => new(
        Guid.Parse(user.FindFirstValue(BranchApiKeyHandler.TenantClaim)!),
        Guid.Parse(user.FindFirstValue(BranchApiKeyHandler.BranchClaim)!));
}

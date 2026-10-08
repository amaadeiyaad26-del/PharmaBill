using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PharmaBill.App.Licensing;

/// <summary>
/// Signs activation payloads with the RSA private key.
/// Signing code and private-key loading are compiled only for <c>MASTER_ADMIN_BUILD</c>.
/// </summary>
public static class AdminKeyGenerator
{
	public const string FeatureFlagsFull = "FULL";

#if MASTER_ADMIN_BUILD
	public static bool CanSign => TryLoadPrivateKey(out _);
#else
	public static bool CanSign => false;
#endif

	public static string GenerateKey(string machineId, LicenseManager.LicenseDuration duration, DateTime? asOfUtc = null)
	{
#if !MASTER_ADMIN_BUILD
		throw new InvalidOperationException(
			"Admin key generation is not available in this client build. Use a MASTER_ADMIN_BUILD or tools/licensing signer.");
#else
		if (!TryLoadPrivateKey(out RSA? rsa) || rsa is null)
		{
			throw new InvalidOperationException(
				"RSA private key not found. Place pharmabill-license-private.pem under tools/licensing or set PHARMABILL_LICENSE_PRIVATE_KEY_PEM.");
		}

		using (rsa)
		{
			DateTime anchor = (asOfUtc ?? DateTime.UtcNow).ToUniversalTime();
			int grantDays = duration switch
			{
				LicenseManager.LicenseDuration.OneMonth => 30,
				LicenseManager.LicenseDuration.Lifetime => 0,
				_ => LicenseManager.AnnualGrantDays
			};

			string expiryToken = duration == LicenseManager.LicenseDuration.Lifetime
				? "LIFETIME"
				: anchor.AddDays(grantDays).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

			string payload = BuildPayload(machineId, expiryToken, FeatureFlagsFull, grantDays);
			byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
			byte[] signature = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			return "PBILL2." + ToBase64Url(payloadBytes) + "." + ToBase64Url(signature);
		}
#endif
	}

	public static string BuildPayload(string machineId, string expiryToken, string featureFlags, int grantDays)
	{
		string mid = (machineId ?? string.Empty).Trim().ToUpperInvariant();
		string flags = string.IsNullOrWhiteSpace(featureFlags) ? FeatureFlagsFull : featureFlags.Trim().ToUpperInvariant();
		return $"{mid}|{expiryToken.Trim().ToUpperInvariant()}|{flags}|{grantDays.ToString(CultureInfo.InvariantCulture)}";
	}

	public static string ToBase64Url(byte[] data)
		=> Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

#if MASTER_ADMIN_BUILD
	private static bool TryLoadPrivateKey(out RSA? rsa)
	{
		rsa = null;
		try
		{
			string? pem = Environment.GetEnvironmentVariable("PHARMABILL_LICENSE_PRIVATE_KEY_PEM");
			if (string.IsNullOrWhiteSpace(pem))
			{
				foreach (string candidate in PrivateKeySearchPaths())
				{
					if (File.Exists(candidate))
					{
						pem = File.ReadAllText(candidate);
						break;
					}
				}
			}

			if (string.IsNullOrWhiteSpace(pem))
			{
				return false;
			}

			rsa = RSA.Create();
			rsa.ImportFromPem(pem);
			return true;
		}
		catch
		{
			rsa?.Dispose();
			rsa = null;
			return false;
		}
	}

	private static IEnumerable<string> PrivateKeySearchPaths()
	{
		string? baseDir = AppContext.BaseDirectory;
		yield return Path.Combine(baseDir, "pharmabill-license-private.pem");
		yield return Path.Combine(baseDir, "licensing", "pharmabill-license-private.pem");

		DirectoryInfo? dir = new DirectoryInfo(baseDir);
		for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
		{
			yield return Path.Combine(dir.FullName, "tools", "licensing", "pharmabill-license-private.pem");
		}

		string? envPath = Environment.GetEnvironmentVariable("PHARMABILL_LICENSE_PRIVATE_KEY_PATH");
		if (!string.IsNullOrWhiteSpace(envPath))
		{
			yield return envPath;
		}
	}
#endif
}
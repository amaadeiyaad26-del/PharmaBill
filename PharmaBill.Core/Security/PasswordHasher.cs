using System.Security.Cryptography;

namespace PharmaBill.Core.Security;

public static class PasswordHasher
{
    private const int SaltLength = 16;
    private const int KeyLength = 32;
    private const int Iterations = 600_000;

    public static string Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, KeyLength);
        return $"PBKDF2-SHA256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string secret, string encodedHash)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2-SHA256" ||
            !int.TryParse(parts[1], out var iterations) || iterations is < 100_000 or > 2_000_000)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != SaltLength || expected.Length != KeyLength)
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

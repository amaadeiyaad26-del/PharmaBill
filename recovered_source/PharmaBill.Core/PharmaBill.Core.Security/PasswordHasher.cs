using System;
using System.Security.Cryptography;

namespace PharmaBill.Core.Security;

public static class PasswordHasher
{
	private const int SaltLength = 16;

	private const int KeyLength = 32;

	private const int Iterations = 600000;

	public static string Hash(string secret)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(secret, "secret");
		byte[] bytes = RandomNumberGenerator.GetBytes(16);
		byte[] inArray = Rfc2898DeriveBytes.Pbkdf2(secret, bytes, 600000, HashAlgorithmName.SHA256, 32);
		return $"PBKDF2-SHA256${600000}${Convert.ToBase64String(bytes)}${Convert.ToBase64String(inArray)}";
	}

	public static bool Verify(string secret, string encodedHash)
	{
		ArgumentNullException.ThrowIfNull(secret, "secret");
		string[] array = encodedHash.Split('$');
		int result = default;
		bool flag = array.Length != 4 || array[0] != "PBKDF2-SHA256" || !int.TryParse(array[1], out result);
		if (!flag)
		{
			bool flag2 = ((result < 100000 || result > 2000000) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			return false;
		}
		try
		{
			byte[] array2 = Convert.FromBase64String(array[2]);
			byte[] array3 = Convert.FromBase64String(array[3]);
			if (array2.Length != 16 || array3.Length != 32)
			{
				return false;
			}
			return CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(secret, array2, result, HashAlgorithmName.SHA256, array3.Length), array3);
		}
		catch (FormatException)
		{
			return false;
		}
	}
}

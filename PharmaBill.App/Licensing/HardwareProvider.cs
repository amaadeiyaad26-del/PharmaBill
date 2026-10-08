using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace PharmaBill.App.Licensing;

/// <summary>
/// Strict hardware fingerprinting (Motherboard + CPU + BIOS UUID) for anti-cloning.
/// </summary>
public static class HardwareProvider
{
	private static string? _cachedMachineId;
	private static string? _cachedFingerprint;

	public static string GetMachineId()
	{
		string? cached = Volatile.Read(ref _cachedMachineId);
		if (cached != null)
		{
			return cached;
		}

		string id = "PB-MID-" + GetHardwareFingerprintHex()[..12];
		Interlocked.CompareExchange(ref _cachedMachineId, id, null);
		return _cachedMachineId!;
	}

	/// <summary>Full SHA-256 hex of the hardware material (used for clone detection).</summary>
	public static string GetHardwareFingerprintHex()
	{
		string? cached = Volatile.Read(ref _cachedFingerprint);
		if (cached != null)
		{
			return cached;
		}

		string material = BuildHardwareMaterial();
		using var sha = SHA256.Create();
		string hex = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(material)));
		Interlocked.CompareExchange(ref _cachedFingerprint, hex, null);
		return _cachedFingerprint!;
	}

	public static bool MatchesBoundFingerprint(string? boundFingerprintHex)
	{
		if (string.IsNullOrWhiteSpace(boundFingerprintHex))
		{
			return false;
		}

		return string.Equals(GetHardwareFingerprintHex(), boundFingerprintHex.Trim(), StringComparison.OrdinalIgnoreCase);
	}

	private static string BuildHardwareMaterial()
	{
		string board = ReadWmiProperty("Win32_BaseBoard", "SerialNumber") ?? string.Empty;
		string cpu = ReadWmiProperty("Win32_Processor", "ProcessorId") ?? string.Empty;
		string biosUuid = ReadWmiProperty("Win32_ComputerSystemProduct", "UUID") ?? string.Empty;

		if (IsPlaceholder(board) && IsPlaceholder(cpu) && IsPlaceholder(biosUuid))
		{
			string? machineGuid = null;
			try
			{
				using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
				machineGuid = key?.GetValue("MachineGuid")?.ToString();
			}
			catch
			{
			}

			string cpuId = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? string.Empty;
			return $"FALLBACK|{machineGuid}|{cpuId}|{Environment.ProcessorCount}".ToUpperInvariant();
		}

		return $"BOARD={Normalize(board)}|CPU={Normalize(cpu)}|UUID={Normalize(biosUuid)}";
	}

	private static string Normalize(string value) => value.Trim().ToUpperInvariant();

	private static bool IsPlaceholder(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return true;
		}

		string v = value.Trim();
		return v.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase)
			|| v.Equals("None", StringComparison.OrdinalIgnoreCase)
			|| v.Equals("Default string", StringComparison.OrdinalIgnoreCase)
			|| v.Equals("0", StringComparison.OrdinalIgnoreCase)
			|| v.Equals("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF", StringComparison.OrdinalIgnoreCase);
	}

	private static string? ReadWmiProperty(string className, string propertyName)
	{
		try
		{
			using var searcher = new System.Management.ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
			foreach (System.Management.ManagementObject obj in searcher.Get())
			{
				string? text = obj[propertyName]?.ToString()?.Trim();
				if (!IsPlaceholder(text))
				{
					return text;
				}
			}
		}
		catch
		{
		}

		return null;
	}
}
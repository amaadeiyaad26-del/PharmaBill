using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace PharmaBill.Data.Services;

public sealed class ProtectedAccessStateStore
{
	private const string RegistryPath = "Software\\PharmaBill\\SystemState";

	private readonly string _filePath;

	public ProtectedAccessStateStore(string? filePath = null)
	{
		_filePath = filePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "access.state");
	}

	[SupportedOSPlatform("windows")]
	public AccessState Read()
	{
		EnsureWindows();
		List<AccessState> list = new List<AccessState>();
		if (File.Exists(_filePath))
		{
			AccessState accessState = JsonSerializer.Deserialize<AccessState>(ProtectedData.Unprotect(File.ReadAllBytes(_filePath), null, DataProtectionScope.CurrentUser));
			if ((object)accessState == null)
			{
				throw new InvalidDataException("The protected access state is invalid.");
			}
			list.Add(accessState);
		}
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\PharmaBill\\SystemState", writable: false);
		if (registryKey?.GetValue("TrialStartedAtUtc") is string s && DateTime.TryParse(s, null, DateTimeStyles.RoundtripKind, out var result))
		{
			DateTime? lastObservedUtc = ((registryKey.GetValue("LastObservedUtc") is string s2 && DateTime.TryParse(s2, null, DateTimeStyles.RoundtripKind, out var result2)) ? new DateTime?(result2) : ((DateTime?)null));
			list.Add(new AccessState(result, lastObservedUtc));
		}
		AccessState result3;
		if (list.Count != 0)
		{
			DateTime dateTime = (from state in list
				where state.TrialStartedAtUtc.HasValue
				select state.TrialStartedAtUtc.Value).DefaultIfEmpty().Min();
			DateTime? trialStartedAtUtc = ((dateTime != default(DateTime)) ? new DateTime?(dateTime) : ((DateTime?)null));
			DateTime dateTime2 = (from state in list
				where state.LastObservedUtc.HasValue
				select state.LastObservedUtc.Value).DefaultIfEmpty().Max();
			result3 = new AccessState(trialStartedAtUtc, (dateTime2 != default(DateTime)) ? new DateTime?(dateTime2) : ((DateTime?)null), list.Any((AccessState state) => state.ClockRollbackDetected));
		}
		else
		{
			result3 = new AccessState(null, null);
		}
		return result3;
	}

	[SupportedOSPlatform("windows")]
	public void Write(AccessState state)
	{
		EnsureWindows();
		Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? throw new InvalidOperationException("The access-state path has no parent directory."));
		byte[] bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(state), null, DataProtectionScope.CurrentUser);
		string text = $"{_filePath}.{Guid.NewGuid():N}.tmp";
		File.WriteAllBytes(text, bytes);
		File.Move(text, _filePath, overwrite: true);
		using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\PharmaBill\\SystemState", writable: true) ?? throw new InvalidOperationException("The Windows registry access-state key could not be opened.");
		if (state.TrialStartedAtUtc.HasValue)
		{
			registryKey.SetValue("TrialStartedAtUtc", state.TrialStartedAtUtc.Value.ToString("O"));
		}
		if (state.LastObservedUtc.HasValue)
		{
			registryKey.SetValue("LastObservedUtc", state.LastObservedUtc.Value.ToString("O"));
		}
		registryKey.Flush();
	}

	private static void EnsureWindows()
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Trial storage uses Windows DPAPI and Registry.");
		}
	}
}

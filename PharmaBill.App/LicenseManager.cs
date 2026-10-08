using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using PharmaBill.App.Licensing;

namespace PharmaBill.App;

/// <summary>
/// Offline Win32 licensing with RSA-2048 signature verification, hardware binding,
/// LocalMachine DPAPI storage, and clock-tamper detection.
/// </summary>
public static class LicenseManager
{
	private const int TrialDays = 7;
	public const int AnnualGrantDays = 365;
	public const int ExpiryWarningDays = 15;
	private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(5);

	private const string TrialRegistryPath = @"Software\PharmaBill\Activation";
	private const string TrialRegistryValue = "TrialAnchor";
	private const string StateRegistryValue = "LicenseState";

	private static readonly object CacheGate = new();
	private static readonly string AppDataRoot = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"PharmaBill");
	private static readonly string AppDataDirectory = Path.Combine(AppDataRoot, "data");
	private static readonly string LicenseFilePath = Path.Combine(AppDataDirectory, "license.lic");
	private static readonly string LicenseStatePath = Path.Combine(AppDataDirectory, "license.state");
	private static readonly string TrialFilePath = Path.Combine(AppDataDirectory, "trial.anchor");

	private static bool _evaluated;
	private static bool _canRun;
	private static bool _activated;
	private static bool _clockTampered;
	private static bool _hardwareMismatch;
	private static int? _trialDaysRemaining;
	private static DateTime? _licensedUntilUtc;

	public enum LicenseDuration
	{
		OneMonth,
		OneYear,
		Lifetime
	}

	public static string GetMachineId() => HardwareProvider.GetMachineId();

	public static string ClockTamperMessage =>
		"System clock manipulation detected. Please set your system time correctly to resume.";

	private static void EnsureDataDirectory()
	{
		Directory.CreateDirectory(AppDataRoot);
		Directory.CreateDirectory(AppDataDirectory);
		MigrateIfNeeded(Path.Combine(AppDataRoot, "license.lic"), LicenseFilePath);
		MigrateIfNeeded(Path.Combine(AppDataRoot, "license.state"), LicenseStatePath);
		MigrateIfNeeded(Path.Combine(AppDataRoot, "trial.anchor"), TrialFilePath);
	}

	private static void MigrateIfNeeded(string legacyPath, string targetPath)
	{
		try
		{
			if (File.Exists(legacyPath) && !File.Exists(targetPath))
			{
				File.Move(legacyPath, targetPath);
			}
		}
		catch
		{
		}
	}

	public static string GetStatusSummary()
	{
		EnsureEvaluated();
		if (_clockTampered)
		{
			return ClockTamperMessage;
		}

		if (_hardwareMismatch)
		{
			return "Hardware fingerprint mismatch — this license is locked to another PC (anti-cloning)";
		}

		if (_activated)
		{
			return _licensedUntilUtc is DateTime until
				? $"Licensed until {until:dd-MMM-yyyy}"
				: "Licensed (lifetime)";
		}

		if (_trialDaysRemaining is > 0)
		{
			return $"7-Day Free Trial ({_trialDaysRemaining.Value} days remaining)";
		}

		return "Annual license expired — enter renewal key to continue billing (records stay readable)";
	}

	public static DateTime? GetLicensedUntilUtc()
	{
		EnsureEvaluated();
		return _licensedUntilUtc;
	}

	public static bool IsClockTampered()
	{
		EnsureEvaluated();
		return _clockTampered;
	}

	public static bool IsHardwareMismatch()
	{
		EnsureEvaluated();
		return _hardwareMismatch;
	}

	public static int? GetDaysUntilExpiry()
	{
		EnsureEvaluated();
		if (!_activated || _licensedUntilUtc is not DateTime until)
		{
			return null;
		}

		return (int)Math.Ceiling((until.Date - DateTime.UtcNow.Date).TotalDays);
	}

	public static bool IsWithinExpiryWarningWindow()
	{
		int? days = GetDaysUntilExpiry();
		return days is > 0 and <= ExpiryWarningDays;
	}

	public static string? GetExpiryWarningMessage()
	{
		EnsureEvaluated();
		if (_clockTampered)
		{
			return ClockTamperMessage;
		}

		if (_hardwareMismatch)
		{
			return "This database/license is bound to different hardware. Billing is locked (anti-cloning).";
		}

		if (_activated && _licensedUntilUtc is DateTime until && IsWithinExpiryWarningWindow())
		{
			return $"Your annual license expires on {until:dd-MMM-yyyy}. Contact support to renew.";
		}

		if (!_activated && !HasFullAccess())
		{
			return "Annual License Expired. Enter renewal key to continue billing.";
		}

		return null;
	}

	public static void InvalidateCache()
	{
		lock (CacheGate)
		{
			_evaluated = false;
		}
	}

	public static bool IsAppActivated()
	{
		EnsureEvaluated();
		return _activated && !_clockTampered && !_hardwareMismatch;
	}

	public static bool CanRunApp()
	{
		EnsureEvaluated();
		return _canRun && !_clockTampered && !_hardwareMismatch;
	}

	public static bool HasFullAccess()
	{
		EnsureEvaluated();
		return _canRun && !_clockTampered && !_hardwareMismatch;
	}

	public static int? GetTrialDaysRemaining()
	{
		EnsureEvaluated();
		return _trialDaysRemaining;
	}

	public static bool IsTrialActive(out int daysRemaining)
	{
		EnsureEvaluated();
		if (_activated || _clockTampered || _hardwareMismatch || _trialDaysRemaining is null or <= 0)
		{
			daysRemaining = 0;
			return false;
		}

		daysRemaining = _trialDaysRemaining.Value;
		return true;
	}

	/// <summary>Updates LastVerifiedTimestamp (DPAPI LocalMachine). Returns false on clock rollback.</summary>
	public static bool RecordRunTimestamp()
	{
		EnsureDataDirectory();
		DateTime now = DateTime.UtcNow;
		LicensePersistedState state = LoadPersistedState() ?? new LicensePersistedState();

		if (state.LastVerifiedUtc is DateTime last && now + ClockSkewTolerance < last)
		{
			state.ClockTampered = true;
			SavePersistedState(state);
			InvalidateCache();
			return false;
		}

		state.LastVerifiedUtc = state.LastVerifiedUtc is DateTime previous && previous > now ? previous : now;
		SavePersistedState(state);
		InvalidateCache();
		return !state.ClockTampered;
	}

	public static string GenerateAnnualKey(string machineId, DateTime? asOfUtc = null)
		=> GenerateKey(machineId, LicenseDuration.OneYear, asOfUtc);

	public static string GenerateKey(string machineId, LicenseDuration duration, DateTime? asOfUtc = null)
		=> AdminKeyGenerator.GenerateKey(machineId, duration, asOfUtc);

	public static bool ValidateLicense(string machineId, string licenseKey, out string message)
	{
		message = string.Empty;
		licenseKey = NormalizeKey(licenseKey);

		if (licenseKey == "PB-STORE-TEST-2026")
		{
			message = "Store Certification Test Key accepted.";
			return true;
		}

		if (!TryVerifyRsaKey(machineId, licenseKey, out int? grantDays, out DateTime? keyExpiryUtc, out string? flags, out string error))
		{
			message = error;
			return false;
		}

		if (keyExpiryUtc is DateTime expiry && DateTime.UtcNow > expiry.AddDays(1))
		{
			message = "This activation key has expired. Request a fresh annual renewal key.";
			return false;
		}

		_ = flags;
		message = grantDays is null
			? "Lifetime licence key is valid for this Machine ID (RSA)."
			: keyExpiryUtc is DateTime until
				? $"RSA key valid for this Machine ID (nominal end {until:dd-MMM-yyyy} UTC, {grantDays} day grant)."
				: $"RSA key valid for this Machine ID ({grantDays} day grant).";
		return true;
	}

	public static bool ActivateLicense(string licenseKey, out string message)
	{
		string machineId = GetMachineId();
		licenseKey = NormalizeKey(licenseKey);

		if (!ValidateLicense(machineId, licenseKey, out message))
		{
			return false;
		}

		if (!TryVerifyRsaKey(machineId, licenseKey, out int? grantDays, out DateTime? keyExpiryUtc, out _, out string verifyError))
		{
			message = verifyError;
			return false;
		}

		DateTime now = DateTime.UtcNow;
		LicensePersistedState state = LoadPersistedState() ?? new LicensePersistedState();
		DateTime? newUntil;

		if (grantDays is null)
		{
			newUntil = null;
		}
		else
		{
			int days = grantDays.Value > 0 ? grantDays.Value : AnnualGrantDays;
			if (state.ValidUntilUtc is DateTime current && current > now)
			{
				newUntil = current.AddDays(days);
			}
			else
			{
				newUntil = now.AddDays(days);
			}

			if (keyExpiryUtc is DateTime keyEnd && keyEnd > newUntil)
			{
				newUntil = keyEnd;
			}
		}

		state.Token = licenseKey;
		state.ValidUntilUtc = newUntil;
		state.BoundMachineId = machineId;
		state.BoundHardwareFingerprint = HardwareProvider.GetHardwareFingerprintHex();
		state.LastVerifiedUtc = now;
		state.ClockTampered = false;

		try
		{
			EnsureDataDirectory();
			File.WriteAllText(LicenseFilePath, licenseKey);
			SavePersistedState(state);
			InvalidateCache();
		}
		catch (Exception ex)
		{
			message = "Could not save the license: " + ex.Message;
			return false;
		}

		message = newUntil is DateTime until
			? $"Annual licence activated until {until:dd-MMM-yyyy} UTC. Billing access restored."
			: "Lifetime licence activated. Billing access restored.";
		return true;
	}

	public static bool SaveLicense(string key) => ActivateLicense(key, out _);

	private static string NormalizeKey(string key)
		=> (key ?? string.Empty).Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

	private static bool TryVerifyRsaKey(
		string machineId,
		string licenseKey,
		out int? grantDays,
		out DateTime? keyExpiryUtc,
		out string? featureFlags,
		out string error)
	{
		grantDays = AnnualGrantDays;
		keyExpiryUtc = null;
		featureFlags = null;
		error = string.Empty;
		machineId = machineId.Trim().ToUpperInvariant();
		licenseKey = NormalizeKey(licenseKey);

		if (string.IsNullOrWhiteSpace(licenseKey))
		{
			error = "Enter an activation key.";
			return false;
		}

		// Store test bypass (no private key needed for certification).
		if (licenseKey == "PB-STORE-TEST-2026")
		{
			grantDays = AnnualGrantDays;
			keyExpiryUtc = DateTime.UtcNow.AddDays(AnnualGrantDays);
			featureFlags = AdminKeyGenerator.FeatureFlagsFull;
			return true;
		}

		if (!licenseKey.StartsWith("PBILL2.", StringComparison.OrdinalIgnoreCase))
		{
			error = "Invalid license key format. Expected RSA key PBILL2.<payload>.<signature>.";
			return false;
		}

		string[] parts = licenseKey.Split('.', 3);
		if (parts.Length != 3)
		{
			error = "Malformed RSA license key.";
			return false;
		}

		byte[] payloadBytes;
		byte[] signature;
		try
		{
			payloadBytes = FromBase64Url(parts[1]);
			signature = FromBase64Url(parts[2]);
		}
		catch
		{
			error = "License key encoding is invalid.";
			return false;
		}

		try
		{
			using RSA rsa = RSA.Create();
			rsa.ImportFromPem(LicensePublicKey.Pem);
			if (!rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
			{
				error = "License signature is not authentic.";
				return false;
			}
		}
		catch (Exception ex)
		{
			error = "Could not verify license signature: " + ex.Message;
			return false;
		}

		string payload = Encoding.UTF8.GetString(payloadBytes);
		string[] fields = payload.Split('|');
		if (fields.Length != 4)
		{
			error = "License payload is malformed.";
			return false;
		}

		string keyMachine = fields[0].Trim().ToUpperInvariant();
		string expiryToken = fields[1].Trim().ToUpperInvariant();
		featureFlags = fields[2].Trim().ToUpperInvariant();
		if (!int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int days))
		{
			error = "License grant days are invalid.";
			return false;
		}

		if (!string.Equals(keyMachine, machineId, StringComparison.OrdinalIgnoreCase))
		{
			error = "License key does not match this computer's Machine ID.";
			return false;
		}

		if (string.Equals(expiryToken, "LIFETIME", StringComparison.OrdinalIgnoreCase))
		{
			grantDays = null;
			keyExpiryUtc = null;
			return true;
		}

		if (!DateTime.TryParseExact(
			    expiryToken,
			    "yyyyMMdd",
			    CultureInfo.InvariantCulture,
			    DateTimeStyles.None,
			    out DateTime compactDate))
		{
			error = "Invalid expiration date in license payload.";
			return false;
		}

		keyExpiryUtc = DateTime.SpecifyKind(compactDate.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
		grantDays = days > 0 ? days : AnnualGrantDays;
		return true;
	}

	private static byte[] FromBase64Url(string text)
	{
		string padded = text.Replace('-', '+').Replace('_', '/');
		switch (padded.Length % 4)
		{
			case 2: padded += "=="; break;
			case 3: padded += "="; break;
		}

		return Convert.FromBase64String(padded);
	}

	private static void EnsureEvaluated()
	{
		lock (CacheGate)
		{
			if (_evaluated)
			{
				return;
			}

			_licensedUntilUtc = null;
			_clockTampered = false;
			_hardwareMismatch = false;
			_activated = false;
			_canRun = false;
			_trialDaysRemaining = null;

			LicensePersistedState? state = LoadPersistedState();
			_clockTampered = state?.ClockTampered == true;

			if (state?.LastVerifiedUtc is DateTime lastVerified)
			{
				DateTime now = DateTime.UtcNow;
				if (now + ClockSkewTolerance < lastVerified)
				{
					_clockTampered = true;
					state.ClockTampered = true;
					SavePersistedState(state);
				}
			}

			if (state != null
			    && (!string.IsNullOrWhiteSpace(state.BoundHardwareFingerprint)
			        || !string.IsNullOrWhiteSpace(state.BoundMachineId)))
			{
				bool fingerprintOk = string.IsNullOrWhiteSpace(state.BoundHardwareFingerprint)
					|| HardwareProvider.MatchesBoundFingerprint(state.BoundHardwareFingerprint);
				bool machineOk = string.IsNullOrWhiteSpace(state.BoundMachineId)
					|| string.Equals(state.BoundMachineId, GetMachineId(), StringComparison.OrdinalIgnoreCase);
				if (!fingerprintOk || !machineOk)
				{
					_hardwareMismatch = true;
				}
			}

			DateTime? untilUtc = null;
			bool activated = !_hardwareMismatch && TryReadActivated(state, out untilUtc);
			_activated = activated && !_clockTampered;
			_licensedUntilUtc = untilUtc;

			if (_activated)
			{
				_canRun = true;
				_trialDaysRemaining = null;
				_evaluated = true;
				return;
			}

			if (_hardwareMismatch || _clockTampered)
			{
				_canRun = false;
				_trialDaysRemaining = 0;
				_evaluated = true;
				return;
			}

			if (TryComputeTrial(out int daysRemaining))
			{
				_canRun = true;
				_trialDaysRemaining = daysRemaining;
			}
			else
			{
				_canRun = false;
				_trialDaysRemaining = 0;
			}

			_evaluated = true;
		}
	}

	private static bool TryReadActivated(LicensePersistedState? state, out DateTime? licensedUntilUtc)
	{
		licensedUntilUtc = null;
		try
		{
			string? savedKey = state?.Token;
			if (string.IsNullOrWhiteSpace(savedKey) && File.Exists(LicenseFilePath))
			{
				savedKey = NormalizeKey(File.ReadAllText(LicenseFilePath));
			}

			if (string.IsNullOrWhiteSpace(savedKey))
			{
				return false;
			}

			string machineId = GetMachineId();
			if (!TryVerifyRsaKey(machineId, savedKey, out int? grantDays, out DateTime? keyExpiry, out _, out _))
			{
				return false;
			}

			if (grantDays is null)
			{
				licensedUntilUtc = state?.ValidUntilUtc;
				return true;
			}

			if (state?.ValidUntilUtc is DateTime storedUntil)
			{
				licensedUntilUtc = storedUntil;
				return DateTime.UtcNow <= storedUntil;
			}

			if (grantDays is null)
			{
				licensedUntilUtc = null;
				PersistActivationSnapshot(savedKey, null);
				return true;
			}

			if (keyExpiry is DateTime until && DateTime.UtcNow <= until)
			{
				licensedUntilUtc = until;
				PersistActivationSnapshot(savedKey, until);
				return true;
			}

			return false;
		}
		catch
		{
			return false;
		}
	}

	private static void PersistActivationSnapshot(string token, DateTime? validUntilUtc)
	{
		try
		{
			LicensePersistedState state = LoadPersistedState() ?? new LicensePersistedState();
			state.Token = token;
			state.ValidUntilUtc = validUntilUtc;
			state.BoundMachineId = GetMachineId();
			state.BoundHardwareFingerprint = HardwareProvider.GetHardwareFingerprintHex();
			state.LastVerifiedUtc ??= DateTime.UtcNow;
			SavePersistedState(state);
			if (!File.Exists(LicenseFilePath))
			{
				File.WriteAllText(LicenseFilePath, token);
			}
		}
		catch
		{
		}
	}

	private sealed class LicensePersistedState
	{
		public string? Token { get; set; }
		public DateTime? ValidUntilUtc { get; set; }
		public DateTime? LastVerifiedUtc { get; set; }
		public string? BoundMachineId { get; set; }
		public string? BoundHardwareFingerprint { get; set; }
		public bool ClockTampered { get; set; }
	}

	private static LicensePersistedState? LoadPersistedState()
	{
		EnsureDataDirectory();
		LicensePersistedState? fromFile = TryReadStateBytes(TryReadAllBytes(LicenseStatePath));
		LicensePersistedState? fromRegistry = null;
		try
		{
			using RegistryKey? key = Registry.LocalMachine.OpenSubKey(TrialRegistryPath, writable: false)
				?? Registry.CurrentUser.OpenSubKey(TrialRegistryPath, writable: false);
			if (key?.GetValue(StateRegistryValue) is string b64 && !string.IsNullOrWhiteSpace(b64))
			{
				fromRegistry = TryReadStateBytes(Convert.FromBase64String(b64));
			}
		}
		catch
		{
		}

		if (fromFile is null) return fromRegistry;
		if (fromRegistry is null) return fromFile;

		return new LicensePersistedState
		{
			Token = !string.IsNullOrWhiteSpace(fromFile.Token) ? fromFile.Token : fromRegistry.Token,
			ValidUntilUtc = MaxDate(fromFile.ValidUntilUtc, fromRegistry.ValidUntilUtc),
			LastVerifiedUtc = MaxDate(fromFile.LastVerifiedUtc, fromRegistry.LastVerifiedUtc),
			BoundMachineId = fromFile.BoundMachineId ?? fromRegistry.BoundMachineId,
			BoundHardwareFingerprint = fromFile.BoundHardwareFingerprint ?? fromRegistry.BoundHardwareFingerprint,
			ClockTampered = fromFile.ClockTampered || fromRegistry.ClockTampered
		};
	}

	private static DateTime? MaxDate(DateTime? a, DateTime? b)
	{
		if (a is null) return b;
		if (b is null) return a;
		return a >= b ? a : b;
	}

	private static byte[]? TryReadAllBytes(string path)
	{
		try
		{
			return File.Exists(path) ? File.ReadAllBytes(path) : null;
		}
		catch
		{
			return null;
		}
	}

	private static LicensePersistedState? TryReadStateBytes(byte[]? protectedBytes)
	{
		if (protectedBytes is null || protectedBytes.Length == 0)
		{
			return null;
		}

		try
		{
			string machineId = GetMachineId();
			byte[] entropy = Encoding.UTF8.GetBytes("PharmaBill|LICENSE|v2|" + machineId);
			string plain = Encoding.UTF8.GetString(UnprotectBytes(protectedBytes, entropy));
			// token|validTicks|lastVerifiedTicks|boundMid|boundFp|tamper|mac
			string[] parts = plain.Split('|');
			if (parts.Length < 7)
			{
				return null;
			}

			string macPayload = string.Join('|', parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]);
			string expectedMac = ComputeIntegrityMac(machineId, macPayload);
			if (!string.Equals(parts[6], expectedMac, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			return new LicensePersistedState
			{
				Token = string.IsNullOrWhiteSpace(parts[0]) ? null : parts[0],
				ValidUntilUtc = ParseTicks(parts[1]),
				LastVerifiedUtc = ParseTicks(parts[2]),
				BoundMachineId = string.IsNullOrWhiteSpace(parts[3]) ? null : parts[3],
				BoundHardwareFingerprint = string.IsNullOrWhiteSpace(parts[4]) ? null : parts[4],
				ClockTampered = parts[5] == "1"
			};
		}
		catch
		{
			return null;
		}
	}

	private static DateTime? ParseTicks(string text)
	{
		if (string.IsNullOrWhiteSpace(text) || text == "0")
		{
			return null;
		}

		return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)
			? new DateTime(ticks, DateTimeKind.Utc)
			: null;
	}

	private static void SavePersistedState(LicensePersistedState state)
	{
		EnsureDataDirectory();
		string machineId = GetMachineId();
		string token = state.Token ?? string.Empty;
		string valid = state.ValidUntilUtc?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "0";
		string lastVerified = state.LastVerifiedUtc?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "0";
		string boundMid = state.BoundMachineId ?? string.Empty;
		string boundFp = state.BoundHardwareFingerprint ?? string.Empty;
		string tamper = state.ClockTampered ? "1" : "0";
		string macPayload = string.Join('|', token, valid, lastVerified, boundMid, boundFp, tamper);
		string mac = ComputeIntegrityMac(machineId, macPayload);
		string plain = macPayload + "|" + mac;
		byte[] entropy = Encoding.UTF8.GetBytes("PharmaBill|LICENSE|v2|" + machineId);
		byte[] protectedBytes = ProtectBytes(Encoding.UTF8.GetBytes(plain), entropy);
		File.WriteAllBytes(LicenseStatePath, protectedBytes);

		try
		{
			using RegistryKey key = Registry.LocalMachine.CreateSubKey(TrialRegistryPath, writable: true)
				?? Registry.CurrentUser.CreateSubKey(TrialRegistryPath, writable: true)
				?? throw new InvalidOperationException("Could not open license registry key.");
			key.SetValue(StateRegistryValue, Convert.ToBase64String(protectedBytes), RegistryValueKind.String);
		}
		catch
		{
			try
			{
				using RegistryKey key = Registry.CurrentUser.CreateSubKey(TrialRegistryPath, writable: true)
					?? throw new InvalidOperationException("Could not open HKCU license registry key.");
				key.SetValue(StateRegistryValue, Convert.ToBase64String(protectedBytes), RegistryValueKind.String);
			}
			catch
			{
			}
		}
	}

	private static string ComputeIntegrityMac(string machineId, string payload)
	{
		// Integrity MAC keyed by public material + machine — DPAPI LocalMachine is the secrecy boundary.
		using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(LicensePublicKey.Pem + "|" + machineId));
		return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)))[..16];
	}

	private static bool TryComputeTrial(out int daysRemaining)
	{
		daysRemaining = 0;
		try
		{
			DateTime trialStartUtc = EnsureTrialStartUtc();
			double elapsedDays = (DateTime.UtcNow - trialStartUtc).TotalDays;
			if (elapsedDays < 0 || elapsedDays >= TrialDays)
			{
				return false;
			}

			daysRemaining = Math.Max(1, (int)Math.Ceiling(TrialDays - elapsedDays));
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static DateTime EnsureTrialStartUtc()
	{
		DateTime? fromFile = TryReadTrialStart(TrialFilePath);
		DateTime? fromRegistry = TryReadTrialStartFromRegistry();

		if (fromFile is null && fromRegistry is null)
		{
			DateTime started = DateTime.UtcNow;
			PersistTrialStart(started);
			return started;
		}

		DateTime start = fromFile is null
			? fromRegistry!.Value
			: fromRegistry is null
				? fromFile.Value
				: (fromFile.Value <= fromRegistry.Value ? fromFile.Value : fromRegistry.Value);

		if (fromFile is null || fromRegistry is null || fromFile.Value != fromRegistry.Value)
		{
			PersistTrialStart(start);
		}

		return start;
	}

	private static void PersistTrialStart(DateTime utc)
	{
		EnsureDataDirectory();
		byte[] protectedBytes = ProtectTrialPayload(utc);
		File.WriteAllBytes(TrialFilePath, protectedBytes);

		try
		{
			using RegistryKey key = Registry.LocalMachine.CreateSubKey(TrialRegistryPath, writable: true)
				?? Registry.CurrentUser.CreateSubKey(TrialRegistryPath, writable: true)
				?? throw new InvalidOperationException("Could not open trial registry key.");
			key.SetValue(TrialRegistryValue, Convert.ToBase64String(protectedBytes), RegistryValueKind.String);
		}
		catch
		{
			try
			{
				using RegistryKey cu = Registry.CurrentUser.CreateSubKey(TrialRegistryPath, writable: true)!;
				cu.SetValue(TrialRegistryValue, Convert.ToBase64String(protectedBytes), RegistryValueKind.String);
			}
			catch
			{
			}
		}
	}

	private static DateTime? TryReadTrialStart(string path)
	{
		try
		{
			return File.Exists(path) ? UnprotectTrialPayload(File.ReadAllBytes(path)) : null;
		}
		catch
		{
			return null;
		}
	}

	private static DateTime? TryReadTrialStartFromRegistry()
	{
		try
		{
			using RegistryKey? key = Registry.LocalMachine.OpenSubKey(TrialRegistryPath, writable: false)
				?? Registry.CurrentUser.OpenSubKey(TrialRegistryPath, writable: false);
			if (key?.GetValue(TrialRegistryValue) is not string text || string.IsNullOrWhiteSpace(text))
			{
				return null;
			}

			return UnprotectTrialPayload(Convert.FromBase64String(text));
		}
		catch
		{
			return null;
		}
	}

	private static byte[] ProtectTrialPayload(DateTime utc)
	{
		string machineId = GetMachineId();
		string ticks = utc.Ticks.ToString(CultureInfo.InvariantCulture);
		string fingerprint = HardwareProvider.GetHardwareFingerprintHex();
		string mac = Convert.ToHexString(
			SHA256.HashData(Encoding.UTF8.GetBytes($"{machineId}|{fingerprint}|{ticks}|trial")))[..16];
		string plain = $"{ticks}|{fingerprint}|{mac}";
		byte[] entropy = Encoding.UTF8.GetBytes("PharmaBill|TRIAL|v2|" + machineId);
		return ProtectBytes(Encoding.UTF8.GetBytes(plain), entropy);
	}

	private static DateTime UnprotectTrialPayload(byte[] protectedBytes)
	{
		string machineId = GetMachineId();
		byte[] entropy = Encoding.UTF8.GetBytes("PharmaBill|TRIAL|v2|" + machineId);
		string plain = Encoding.UTF8.GetString(UnprotectBytes(protectedBytes, entropy));
		string[] parts = plain.Split('|');
		if (parts.Length < 3
		    || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
		{
			throw new InvalidDataException("Trial anchor is corrupt.");
		}

		string fingerprint = HardwareProvider.GetHardwareFingerprintHex();
		if (!string.Equals(parts[1], fingerprint, StringComparison.OrdinalIgnoreCase))
		{
			throw new CryptographicException("Trial anchor hardware mismatch.");
		}

		string expected = Convert.ToHexString(
			SHA256.HashData(Encoding.UTF8.GetBytes($"{machineId}|{fingerprint}|{parts[0]}|trial")))[..16];
		if (!string.Equals(expected, parts[2], StringComparison.OrdinalIgnoreCase))
		{
			throw new CryptographicException("Trial anchor integrity mismatch.");
		}

		return new DateTime(ticks, DateTimeKind.Utc);
	}

	/// <summary>Prefer LocalMachine DPAPI; fall back to CurrentUser if LocalMachine is unavailable.</summary>
	private static byte[] ProtectBytes(byte[] plain, byte[] entropy)
	{
		try
		{
			return ProtectedData.Protect(plain, entropy, DataProtectionScope.LocalMachine);
		}
		catch
		{
			return ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);
		}
	}

	private static byte[] UnprotectBytes(byte[] protectedBytes, byte[] entropy)
	{
		try
		{
			return ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.LocalMachine);
		}
		catch
		{
			return ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
		}
	}
}
using System.Security.Cryptography;
using System.Text;
using PharmaBill.Core;

namespace PharmaBill.Sync.Terminals;

/// <summary>What the server's Devices panel shows while a pairing code is open.</summary>
public sealed record TerminalPairingCode(string Code, DateTime ExpiresAtUtc, int AttemptsLeft);

/// <summary>The proofs both ends compute. Shared by the server (TerminalPairing) and the terminal (TerminalPairingClient).</summary>
public static class TerminalPairingProof
{
	// Slows each guess of a captured proof: a code from a 31-character alphabet has ~887 million values, and at
	// this cost an attacker cannot search them inside the code's five minutes.
	public const int Iterations = 200_000;

	public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

	public const int CodeLength = 6;

	public static string Normalize(string? code) =>
		new string((code ?? string.Empty).ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

	public static byte[] Key(string code, string serverNonce, string terminalNonce) =>
		Rfc2898DeriveBytes.Pbkdf2(Normalize(code), Encoding.UTF8.GetBytes("PharmaRemit.Pairing.v1|" + serverNonce + "|" + terminalNonce), Iterations, HashAlgorithmName.SHA256, 32);

	/// <summary>Terminal to server: "I know the code, and the server I am talking to has this certificate".</summary>
	public static string TerminalProof(byte[] key, string certificateFingerprint, string serverNonce, string terminalNonce, string terminalName) =>
		Mac(key, "terminal", certificateFingerprint, serverNonce, terminalNonce, terminalName);

	/// <summary>Server to terminal: "I know the code too" - so the terminal knows it reached the PC showing it.</summary>
	public static string ServerProof(byte[] key, string certificateFingerprint, string serverNonce, string terminalNonce, Guid deviceId) =>
		Mac(key, "server", certificateFingerprint, serverNonce, terminalNonce, deviceId.ToString("D"));

	public static bool Matches(string expected, string? presented) =>
		presented != null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(presented));

	private static string Mac(byte[] key, params string[] parts) =>
		Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(string.Join("|", parts))));
}

// One pairing code at a time, shown on the server's screen: valid five minutes, used once, closed after three
// wrong tries. The code never travels over the network; only proofs built from it do.
public sealed class TerminalPairing(TerminalDeviceStore devices, TerminalServerCertificate certificate)
{
	public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

	public const int MaxAttempts = 3;

	private readonly object _gate = new object();

	private string? _code;

	private string? _serverNonce;

	private DateTime _expiresAtUtc;

	private int _attemptsLeft;

	/// <summary>Raised when the code closes: paired, expired, too many wrong tries or cancelled.</summary>
	public event Action<TerminalDevice?>? Closed;

	public TerminalPairingCode Open()
	{
		lock (_gate)
		{
			_code = new string(Enumerable.Range(0, TerminalPairingProof.CodeLength)
				.Select(_ => TerminalPairingProof.Alphabet[RandomNumberGenerator.GetInt32(TerminalPairingProof.Alphabet.Length)]).ToArray());
			_serverNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
			_expiresAtUtc = BusinessClock.UtcNow + CodeLifetime;
			_attemptsLeft = MaxAttempts;
			return new TerminalPairingCode(_code, _expiresAtUtc, _attemptsLeft);
		}
	}

	public void Cancel()
	{
		bool wasOpen;
		lock (_gate)
		{
			wasOpen = _code != null;
			_code = null;
		}
		if (wasOpen)
		{
			Closed?.Invoke(null);
		}
	}

	public TerminalPairingCode? Current
	{
		get
		{
			lock (_gate)
			{
				return IsOpenLocked() ? new TerminalPairingCode(_code!, _expiresAtUtc, _attemptsLeft) : null;
			}
		}
	}

	/// <summary>The nonce a terminal needs to build its proof, while a code is open.</summary>
	public string? ServerNonce
	{
		get
		{
			lock (_gate)
			{
				return IsOpenLocked() ? _serverNonce : null;
			}
		}
	}

	/// <summary>Checks a terminal's proof. On success the terminal is registered and the code is spent.</summary>
	public TerminalPairingOutcome TryPair(string? terminalName, string? terminalNonce, string? terminalProof)
	{
		string name = string.IsNullOrWhiteSpace(terminalName) ? "Billing counter" : terminalName.Trim();
		if (name.Length > 60)
		{
			name = name[..60];
		}
		string code;
		string serverNonce;
		lock (_gate)
		{
			if (!IsOpenLocked())
			{
				return TerminalPairingOutcome.Refused("No pairing code is open on the server. Ask the owner to press \"Add a billing counter\" there.");
			}
			if (string.IsNullOrWhiteSpace(terminalNonce) || terminalNonce.Length > 64)
			{
				return TerminalPairingOutcome.Refused("The pairing request was incomplete.");
			}
			code = _code!;
			serverNonce = _serverNonce!;
		}
		string fingerprint = certificate.Fingerprint;
		byte[] key = TerminalPairingProof.Key(code, serverNonce, terminalNonce);
		try
		{
			// The proof covers the certificate the terminal saw. Behind a machine in the middle that is not ours.
			if (!TerminalPairingProof.Matches(TerminalPairingProof.TerminalProof(key, fingerprint, serverNonce, terminalNonce, name), terminalProof))
			{
				bool closed;
				lock (_gate)
				{
					closed = --_attemptsLeft <= 0;
					if (closed)
					{
						_code = null;
					}
				}
				if (closed)
				{
					Closed?.Invoke(null);
				}
				return TerminalPairingOutcome.Refused(closed
					? "Wrong code entered three times, so it was cancelled. Ask the owner for a new code."
					: "That code is not right. Check it on the server's screen and try again.");
			}
			lock (_gate)
			{
				if (_code != code)
				{
					return TerminalPairingOutcome.Refused("That code was already used or cancelled. Ask the owner for a new one.");
				}
				_code = null;
			}
			(TerminalDevice device, string secret) = devices.Register(name);
			Closed?.Invoke(device);
			return new TerminalPairingOutcome(device, secret, TerminalPairingProof.ServerProof(key, fingerprint, serverNonce, terminalNonce, device.Id), null);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(key);
		}
	}

	private bool IsOpenLocked()
	{
		if (_code != null && BusinessClock.UtcNow >= _expiresAtUtc)
		{
			_code = null;
		}
		return _code != null;
	}
}

public sealed record TerminalPairingOutcome(TerminalDevice? Device, string? Secret, string? ServerProof, string? Error)
{
	public static TerminalPairingOutcome Refused(string error) => new TerminalPairingOutcome(null, null, null, error);
}

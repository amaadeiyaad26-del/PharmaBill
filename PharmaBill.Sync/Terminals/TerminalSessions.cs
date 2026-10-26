using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PharmaBill.Core;
using PharmaBill.Core.Entities;

namespace PharmaBill.Sync.Terminals;

/// <summary>A signed-in user at one terminal. Only a SHA-256 of the session token is kept, in memory and on disk.</summary>
public sealed record TerminalSession(string TokenHash, Guid DeviceId, Guid UserId, string DisplayName, UserRole Role, DateTime ExpiresAtUtc, DateTime LastUsedUtc);

// Who is signed in at each terminal. Sessions survive a server restart (a Windows update rebooting the server PC
// must not lock every counter out mid-trade), but still end 12 hours after sign-in and after 2 hours unused -
// time the server was off counts. The role used for permission checks is re-read from the server's users on
// every call (TerminalServer), never taken from the terminal.
public sealed class TerminalSessions
{
	public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

	public static readonly TimeSpan IdleLimit = TimeSpan.FromHours(2);

	private const int MaxFailedSignIns = 5;

	private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(5);

	private static readonly TimeSpan LastUsedWriteInterval = TimeSpan.FromMinutes(5);

	private readonly string? _path;

	private readonly object _fileGate = new object();

	private readonly ConcurrentDictionary<string, TerminalSession> _sessions = new ConcurrentDictionary<string, TerminalSession>(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<Guid, List<DateTime>> _failures = new ConcurrentDictionary<Guid, List<DateTime>>();

	private DateTime _lastSavedUtc = DateTime.MinValue;

	/// <param name="directory">Where sessions are kept across restarts; null keeps them in memory only.</param>
	public TerminalSessions(string? directory = null)
	{
		if (directory == null)
		{
			return;
		}
		_path = Path.Combine(directory, "terminal-sessions.dat");
		foreach (TerminalSession session in LoadSaved().Where(IsLive))
		{
			_sessions[session.TokenHash] = session;
		}
	}

	/// <summary>Signs a user in at a terminal. The returned token goes to the terminal once and is never stored.</summary>
	public (TerminalSession Session, string Token) Start(Guid deviceId, AppUser user)
	{
		DateTime now = BusinessClock.UtcNow;
		string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
		var session = new TerminalSession(Hash(token), deviceId, user.Id, string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName, user.Role, now + Lifetime, now);
		_sessions[session.TokenHash] = session;
		Save(force: true);
		return (session, token);
	}

	/// <summary>The live session for this token on this terminal, or null. A token used from another terminal is refused.</summary>
	public TerminalSession? Find(string? token, Guid deviceId)
	{
		if (string.IsNullOrEmpty(token) || !_sessions.TryGetValue(Hash(token), out TerminalSession? session))
		{
			return null;
		}
		if (session.DeviceId != deviceId)
		{
			return null;
		}
		if (!IsLive(session))
		{
			End(session.TokenHash);
			return null;
		}
		TerminalSession touched = session with { LastUsedUtc = BusinessClock.UtcNow };
		_sessions[session.TokenHash] = touched;
		Save(force: false);
		return touched;
	}

	/// <summary>Keeps the session's role in step with the server's user record.</summary>
	public TerminalSession Refresh(TerminalSession session, UserRole role, string displayName)
	{
		if (session.Role == role && session.DisplayName == displayName)
		{
			return session;
		}
		TerminalSession updated = session with { Role = role, DisplayName = displayName };
		_sessions[session.TokenHash] = updated;
		Save(force: true);
		return updated;
	}

	public void End(string tokenHash)
	{
		if (_sessions.TryRemove(tokenHash, out _))
		{
			Save(force: true);
		}
	}

	public void EndAllFor(Guid deviceId)
	{
		bool removed = false;
		foreach (string hash in _sessions.Where(pair => pair.Value.DeviceId == deviceId).Select(pair => pair.Key).ToArray())
		{
			removed |= _sessions.TryRemove(hash, out _);
		}
		if (removed)
		{
			Save(force: true);
		}
	}

	public bool IsSignInLocked(Guid deviceId)
	{
		if (!_failures.TryGetValue(deviceId, out List<DateTime>? times))
		{
			return false;
		}
		lock (times)
		{
			times.RemoveAll(time => BusinessClock.UtcNow - time > FailureWindow);
			return times.Count >= MaxFailedSignIns;
		}
	}

	public void RecordFailedSignIn(Guid deviceId)
	{
		List<DateTime> times = _failures.GetOrAdd(deviceId, _ => new List<DateTime>());
		lock (times)
		{
			times.Add(BusinessClock.UtcNow);
		}
	}

	public void ClearFailedSignIns(Guid deviceId) => _failures.TryRemove(deviceId, out _);

	public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

	private static bool IsLive(TerminalSession session)
	{
		DateTime now = BusinessClock.UtcNow;
		return now < session.ExpiresAtUtc && now - session.LastUsedUtc <= IdleLimit;
	}

	private IReadOnlyList<TerminalSession> LoadSaved()
	{
		if (_path == null || !File.Exists(_path))
		{
			return Array.Empty<TerminalSession>();
		}
		try
		{
			byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
			return JsonSerializer.Deserialize<List<TerminalSession>>(plain) ?? new List<TerminalSession>();
		}
		catch (Exception exception) when (exception is CryptographicException or JsonException or IOException)
		{
			// Unreadable means everyone signs in again: the safe direction.
			return Array.Empty<TerminalSession>();
		}
	}

	private void Save(bool force)
	{
		if (_path == null)
		{
			return;
		}
		lock (_fileGate)
		{
			DateTime now = BusinessClock.UtcNow;
			if (!force && now - _lastSavedUtc < LastUsedWriteInterval)
			{
				return;
			}
			_lastSavedUtc = now;
			Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
			byte[] protectedBytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(_sessions.Values.Where(IsLive).ToList()), null, DataProtectionScope.CurrentUser);
			string temp = $"{_path}.{Guid.NewGuid():N}.tmp";
			File.WriteAllBytes(temp, protectedBytes);
			File.Move(temp, _path, overwrite: true);
		}
	}
}

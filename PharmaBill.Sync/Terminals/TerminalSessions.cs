using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using PharmaBill.Core;
using PharmaBill.Core.Entities;

namespace PharmaBill.Sync.Terminals;

public sealed record TerminalSession(string Token, Guid DeviceId, Guid UserId, string DisplayName, UserRole Role, DateTime ExpiresAtUtc, DateTime LastUsedUtc);

// Who is signed in at each terminal. Held in memory on the server: a server restart signs terminals out, which is
// the safe direction. The role used for every permission check comes from here, never from the terminal.
public sealed class TerminalSessions
{
	public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

	public static readonly TimeSpan IdleLimit = TimeSpan.FromHours(2);

	private const int MaxFailedSignIns = 5;

	private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(5);

	private readonly ConcurrentDictionary<string, TerminalSession> _sessions = new ConcurrentDictionary<string, TerminalSession>(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<Guid, List<DateTime>> _failures = new ConcurrentDictionary<Guid, List<DateTime>>();

	public TerminalSession Start(Guid deviceId, AppUser user)
	{
		DateTime now = BusinessClock.UtcNow;
		string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
		var session = new TerminalSession(token, deviceId, user.Id, string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName, user.Role, now + Lifetime, now);
		_sessions[token] = session;
		return session;
	}

	/// <summary>The live session for this token on this terminal, or null. A token used from another terminal is refused.</summary>
	public TerminalSession? Find(string? token, Guid deviceId)
	{
		if (string.IsNullOrEmpty(token) || !_sessions.TryGetValue(token, out TerminalSession? session))
		{
			return null;
		}
		DateTime now = BusinessClock.UtcNow;
		if (session.DeviceId != deviceId || now >= session.ExpiresAtUtc || now - session.LastUsedUtc > IdleLimit)
		{
			if (session.DeviceId == deviceId)
			{
				_sessions.TryRemove(token, out _);
			}
			return null;
		}
		TerminalSession touched = session with { LastUsedUtc = now };
		_sessions[token] = touched;
		return touched;
	}

	public void End(string token) => _sessions.TryRemove(token, out _);

	public void EndAllFor(Guid deviceId)
	{
		foreach (string token in _sessions.Where(pair => pair.Value.DeviceId == deviceId).Select(pair => pair.Key).ToArray())
		{
			_sessions.TryRemove(token, out _);
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
}

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace PharmaBill.Sync;

[SupportedOSPlatform("windows")]
public static class WindowsFirewallPortOpener
{
	public sealed record Result(bool RulePresent, bool CreatedNow, string Message);

	public const string DefaultRuleName = "PharmaBill Sync Station (Port 5055)";

	private const int ProfilesPrivateDomain = 3;

	private const int ProtocolTcp = 6;

	private const int DirectionIn = 1;

	private const int ActionAllow = 1;

	public static Result EnsureAllowRule(int port = 5055, string? ruleName = null)
	{
		if (!OperatingSystem.IsWindows())
		{
			return new Result(RulePresent: false, CreatedNow: false, "Firewall rules are only managed on Windows.");
		}
		if (ruleName == null)
		{
			ruleName = "PharmaBill Sync Station (Port 5055)";
		}
		try
		{
			dynamic val = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2") ?? throw new InvalidOperationException("Windows Firewall COM (HNetCfg.FwPolicy2) is unavailable.")) ?? throw new InvalidOperationException("Could not create FwPolicy2.");
			dynamic val2 = val.Rules;
			if (WindowsFirewallPortOpener.TryFindRule(val2, ruleName, port))
			{
				return new Result(RulePresent: true, CreatedNow: false, $"Firewall already allows TCP {port}.");
			}
			dynamic val3 = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule") ?? throw new InvalidOperationException("Windows Firewall COM (HNetCfg.FWRule) is unavailable.")) ?? throw new InvalidOperationException("Could not create FWRule.");
			val3.Name = ruleName;
			val3.Description = "PharmaBill Android LAN sync listener (zero-config)";
			val3.Protocol = 6;
			val3.LocalPorts = port.ToString();
			val3.Direction = 1;
			val3.Action = 1;
			val3.Enabled = true;
			val3.Profiles = 3;
			val3.Grouping = "PharmaBill";
			val2.Add(val3);
			return new Result(RulePresent: true, CreatedNow: true, $"Firewall rule added for TCP {port}.");
		}
		catch (UnauthorizedAccessException)
		{
			return new Result(RulePresent: false, CreatedNow: false, "Administrator permission is required to open the firewall port.");
		}
		catch (COMException ex2) when (((Func<bool>)delegate
		{
			// Could not convert BlockContainer to single expression
			uint errorCode = (uint)ex2.ErrorCode;
			return (errorCode == 2147500037u || errorCode == 2147942405u) ? true : false;
		}).Invoke())
		{
			return new Result(RulePresent: false, CreatedNow: false, "Administrator permission is required to open the firewall port.");
		}
		catch (Exception ex3)
		{
			return new Result(RulePresent: false, CreatedNow: false, ex3.Message);
		}
	}

	public static bool IsRulePresent(int port = 5055, string? ruleName = null)
	{
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}
		if (ruleName == null)
		{
			ruleName = "PharmaBill Sync Station (Port 5055)";
		}
		try
		{
			Type typeFromProgID = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
			if ((object)typeFromProgID == null)
			{
				return false;
			}
			dynamic val = Activator.CreateInstance(typeFromProgID);
			return WindowsFirewallPortOpener.TryFindRule(val.Rules, ruleName, port);
		}
		catch
		{
			return false;
		}
	}

	private static bool TryFindRule(dynamic rules, string ruleName, int port)
	{
		try
		{
			bool flag = default;
			foreach (dynamic rule in rules)
			{
				if (string.Equals(rule.Name as string, ruleName, StringComparison.OrdinalIgnoreCase))
				{
					object obj = rule.Enabled;
					int num;
					if (obj is bool)
					{
						flag = (bool)obj;
						num = 1;
					}
					else
					{
						num = 0;
					}
					bool flag2 = (byte)((uint)num & (flag ? 1u : 0u)) != 0;
					dynamic val = rule.LocalPorts?.ToString() ?? string.Empty;
					if (flag2 && val.Contains(port.ToString(), StringComparison.Ordinal))
					{
						return true;
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}

	public static void LogResult(ILogger logger, Result result)
	{
		logger.LogInformation("LAN firewall: {Message} (present={Present}, created={Created})", result.Message, result.RulePresent, result.CreatedNow);
	}
}

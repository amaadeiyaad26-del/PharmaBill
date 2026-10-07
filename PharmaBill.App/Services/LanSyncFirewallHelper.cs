using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using PharmaBill.Sync;

namespace PharmaBill.App.Services;

public static class LanSyncFirewallHelper
{
	public sealed record FirewallCheckResult(bool RulePresent, string Message, string? Detail = null);

	public const string RuleName = "PharmaBill Sync Station (Port 5055)";

	public static FirewallCheckResult CheckRule(int port = 5055)
	{
		try
		{
			if (OperatingSystem.IsWindows() && WindowsFirewallPortOpener.IsRulePresent(port, "PharmaBill Sync Station (Port 5055)"))
			{
				return new FirewallCheckResult(RulePresent: true, $"Firewall allows TCP {port}", $"Rule \"{"PharmaBill Sync Station (Port 5055)"}\" is present. Phone can discover this PC via mDNS or http://<pc-ip>:{port}/api/sync/ping.");
			}
			(int, string, string) tuple = RunNetsh("advfirewall firewall show rule name=\"PharmaBill Sync Station (Port 5055)\"", elevate: false);
			if (tuple.Item1 == 0 && tuple.Item2.Contains("Enabled:", StringComparison.OrdinalIgnoreCase) && !tuple.Item2.Contains("No rules match", StringComparison.OrdinalIgnoreCase))
			{
				return new FirewallCheckResult(RulePresent: true, $"Firewall allows TCP {port}", "Rule \"PharmaBill Sync Station (Port 5055)\" is present.");
			}
			return new FirewallCheckResult(RulePresent: false, $"Windows Firewall may be blocking TCP {port}", "No inbound allow rule named \"PharmaBill Sync Station (Port 5055)\" was found. Tap Allow to register it (one-time UAC if needed).");
		}
		catch (Exception ex)
		{
			return new FirewallCheckResult(RulePresent: false, "Could not query Windows Firewall", ex.Message);
		}
	}

	public static FirewallCheckResult AllowRuleElevated(int port = 5055)
	{
		if (OperatingSystem.IsWindows())
		{
			WindowsFirewallPortOpener.Result result = WindowsFirewallPortOpener.EnsureAllowRule(port, "PharmaBill Sync Station (Port 5055)");
			if (result.RulePresent)
			{
				return new FirewallCheckResult(RulePresent: true, result.Message, result.CreatedNow ? "Android on the same Wi-Fi can now open /api/sync/ping. Discovery: _pharmabill-sync._tcp." : "Firewall rule already active.");
			}
			if (!result.Message.Contains("Administrator", StringComparison.OrdinalIgnoreCase))
			{
				return new FirewallCheckResult(RulePresent: false, "Could not add firewall rule", result.Message);
			}
		}
		string text = FindHelperScript();
		try
		{
			if (text != null)
			{
				RunElevatedPowerShell($"-NoProfile -ExecutionPolicy Bypass -File \"{text}\" -Port {port}");
			}
			else
			{
				RunNetsh("advfirewall firewall delete rule name=\"PharmaBill Sync Station (Port 5055)\"", elevate: true);
				(int, string, string) tuple = RunNetsh($"advfirewall firewall add rule name=\"{"PharmaBill Sync Station (Port 5055)"}\" dir=in action=allow protocol=TCP localport={port} profile=private,domain description=\"PharmaBill Android LAN sync listener\"", elevate: true);
				if (tuple.Item1 != 0)
				{
					return new FirewallCheckResult(RulePresent: false, "Could not add firewall rule", string.IsNullOrWhiteSpace(tuple.Item3) ? tuple.Item2 : tuple.Item3);
				}
			}
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			return new FirewallCheckResult(RulePresent: false, "Firewall change cancelled", "UAC elevation was declined. LAN sync will fall back to Google Drive / folder sync when available.");
		}
		catch (Exception ex2)
		{
			return new FirewallCheckResult(RulePresent: false, "Firewall change failed", ex2.Message);
		}
		Thread.Sleep(400);
		FirewallCheckResult firewallCheckResult = CheckRule(port);
		if (firewallCheckResult.RulePresent)
		{
			return firewallCheckResult with
			{
				Message = $"Firewall rule added for TCP {port}",
				Detail = "Android can reach /api/sync/ping. Cloud Drive remains the automatic fallback if LAN is blocked."
			};
		}
		return new FirewallCheckResult(RulePresent: false, "Rule may not have been created", "Elevation finished but the rule was not detected. Cloud sync fallback stays available when Google Drive is connected.");
	}

	private static string? FindHelperScript()
	{
		string[] array = new string[3]
		{
			Path.Combine(AppContext.BaseDirectory, "scripts", "allow-firewall-port5055.ps1"),
			null,
			null
		};
		InlineArray7<string> buffer = default;
		buffer[0] = AppContext.BaseDirectory;
		buffer[1] = "..";
		buffer[2] = "..";
		buffer[3] = "..";
		buffer[4] = "..";
		buffer[5] = "scripts";
		buffer[6] = "allow-firewall-port5055.ps1";
		array[1] = Path.Combine(buffer);
		array[2] = Path.Combine(Environment.CurrentDirectory, "scripts", "allow-firewall-port5055.ps1");
		string[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			string fullPath = Path.GetFullPath(array2[i]);
			if (File.Exists(fullPath))
			{
				return fullPath;
			}
		}
		return null;
	}

	private static void RunElevatedPowerShell(string arguments)
	{
		using Process process = Process.Start(new ProcessStartInfo
		{
			FileName = "powershell.exe",
			Arguments = arguments,
			UseShellExecute = true,
			Verb = "runas"
		}) ?? throw new InvalidOperationException("Could not start elevated PowerShell.");
		process.WaitForExit(60000);
	}

	private static (int ExitCode, string Stdout, string Stderr) RunNetsh(string arguments, bool elevate)
	{
		if (elevate)
		{
			using (Process process = Process.Start(new ProcessStartInfo
			{
				FileName = "netsh",
				Arguments = arguments,
				UseShellExecute = true,
				Verb = "runas"
			}) ?? throw new InvalidOperationException("Could not start elevated netsh."))
			{
				process.WaitForExit(60000);
				return (ExitCode: process.ExitCode, Stdout: string.Empty, Stderr: string.Empty);
			}
		}
		using Process process2 = Process.Start(new ProcessStartInfo
		{
			FileName = "netsh",
			Arguments = arguments,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		}) ?? throw new InvalidOperationException("Could not start netsh.");
		StringBuilder stdout = new StringBuilder();
		StringBuilder stderr = new StringBuilder();
		process2.OutputDataReceived += (object _, DataReceivedEventArgs e) =>
		{
			if (e.Data != null)
			{
				stdout.AppendLine(e.Data);
			}
		};
		process2.ErrorDataReceived += (object _, DataReceivedEventArgs e) =>
		{
			if (e.Data != null)
			{
				stderr.AppendLine(e.Data);
			}
		};
		process2.BeginOutputReadLine();
		process2.BeginErrorReadLine();
		process2.WaitForExit(15000);
		return (ExitCode: process2.ExitCode, Stdout: stdout.ToString(), Stderr: stderr.ToString());
	}
}

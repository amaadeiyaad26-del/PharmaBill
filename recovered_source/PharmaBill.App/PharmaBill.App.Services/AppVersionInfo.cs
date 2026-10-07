using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PharmaBill.App.Services;

public sealed class AppVersionInfo
{
	public string Version { get; }

	public string BuildDate { get; }

	public string ReleaseNotesPath { get; }

	public string Display
	{
		get
		{
			IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
			DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(17, 2, invariantCulture);
			handler.AppendLiteral("Version ");
			handler.AppendFormatted(Version);
			handler.AppendLiteral(" (built ");
			handler.AppendFormatted(BuildDate);
			handler.AppendLiteral(")");
			return string.Create(invariantCulture, ref handler);
		}
	}

	public AppVersionInfo(Assembly? assembly = null, string? baseDirectory = null)
	{
		if ((object)assembly == null)
		{
			assembly = typeof(AppVersionInfo).Assembly;
		}
		Version = (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0").Split('+')[0];
		BuildDate = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault((AssemblyMetadataAttribute item) => item.Key == "BuildDateUtc")?.Value ?? "unknown";
		ReleaseNotesPath = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "ReleaseNotes.md");
	}

	public string LoadReleaseNotes()
	{
		if (!File.Exists(ReleaseNotesPath))
		{
			return "Release notes are not available.";
		}
		return File.ReadAllText(ReleaseNotesPath);
	}
}

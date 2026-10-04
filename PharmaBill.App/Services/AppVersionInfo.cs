using System.Globalization;
using System.IO;
using System.Reflection;

namespace PharmaBill.App.Services;

public sealed class AppVersionInfo
{
    public AppVersionInfo(Assembly? assembly = null, string? baseDirectory = null)
    {
        assembly ??= typeof(AppVersionInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Version = (informational ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0").Split('+')[0];
        BuildDate = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(item => item.Key == "BuildDateUtc")?.Value ?? "unknown";
        ReleaseNotesPath = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "ReleaseNotes.md");
    }

    public string Version { get; }

    public string BuildDate { get; }

    public string ReleaseNotesPath { get; }

    public string Display => string.Create(CultureInfo.InvariantCulture, $"Version {Version} (built {BuildDate})");

    public string LoadReleaseNotes() =>
        File.Exists(ReleaseNotesPath) ? File.ReadAllText(ReleaseNotesPath) : "Release notes are not available.";
}


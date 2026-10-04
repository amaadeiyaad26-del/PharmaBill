using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace PharmaBill.Data.Services;

public sealed class ProtectedAccessStateStore
{
    private const string RegistryPath = @"Software\PharmaBill\SystemState";
    private readonly string _filePath;

    public ProtectedAccessStateStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaBill",
            "access.state");
    }

    [SupportedOSPlatform("windows")]
    public AccessState Read()
    {
        EnsureWindows();
        var candidates = new List<AccessState>();
        if (File.Exists(_filePath))
        {
            var protectedBytes = File.ReadAllBytes(_filePath);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var fileState = JsonSerializer.Deserialize<AccessState>(clearBytes);
            if (fileState is null)
            {
                throw new InvalidDataException("The protected access state is invalid.");
            }

            candidates.Add(fileState);
        }

        using var registry = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
        if (registry?.GetValue("TrialStartedAtUtc") is string start &&
            DateTime.TryParse(start, null, System.Globalization.DateTimeStyles.RoundtripKind, out var trialStart))
        {
            var lastObserved = registry.GetValue("LastObservedUtc") is string last &&
                               DateTime.TryParse(last, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedLast)
                ? parsedLast
                : (DateTime?)null;
            candidates.Add(new AccessState(trialStart, lastObserved));
        }

        return candidates.Count == 0
            ? new AccessState(null, null)
            : new AccessState(
                candidates.Where(state => state.TrialStartedAtUtc.HasValue)
                    .Select(state => state.TrialStartedAtUtc!.Value)
                    .DefaultIfEmpty()
                    .Min() is var earliest && earliest != default ? earliest : null,
                candidates.Where(state => state.LastObservedUtc.HasValue)
                    .Select(state => state.LastObservedUtc!.Value)
                    .DefaultIfEmpty()
                    .Max() is var latest && latest != default ? latest : null,
                candidates.Any(state => state.ClockRollbackDetected));
    }

    [SupportedOSPlatform("windows")]
    public void Write(AccessState state)
    {
        EnsureWindows();
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("The access-state path has no parent directory.");
        Directory.CreateDirectory(directory);
        var clearBytes = JsonSerializer.SerializeToUtf8Bytes(state);
        var protectedBytes = ProtectedData.Protect(clearBytes, null, DataProtectionScope.CurrentUser);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temporaryPath, protectedBytes);
        File.Move(temporaryPath, _filePath, overwrite: true);

        using var registry = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new InvalidOperationException("The Windows registry access-state key could not be opened.");
        if (state.TrialStartedAtUtc.HasValue)
        {
            registry.SetValue("TrialStartedAtUtc", state.TrialStartedAtUtc.Value.ToString("O"));
        }

        if (state.LastObservedUtc.HasValue)
        {
            registry.SetValue("LastObservedUtc", state.LastObservedUtc.Value.ToString("O"));
        }

        registry.Flush();
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Trial storage uses Windows DPAPI and Registry.");
        }
    }
}

public sealed record AccessState(
    DateTime? TrialStartedAtUtc,
    DateTime? LastObservedUtc,
    bool ClockRollbackDetected = false);

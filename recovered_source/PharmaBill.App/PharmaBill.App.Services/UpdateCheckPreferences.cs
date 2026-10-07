using System;

namespace PharmaBill.App.Services;

public sealed record UpdateCheckPreferences(DateTimeOffset? LastCheckedUtc, DateTimeOffset? RemindAfterUtc, string? LastKnownLatestVersion);


namespace PharmaBill.App.Services;

/// <param name="LastCheckedUtc">When the update server was last asked (at most once a day unless the user asks).</param>
/// <param name="DismissedVersion">The newer version whose indicator the user closed; it stays hidden until a later one.</param>
/// <param name="LastFeedJson">The last update file read, as it was; checked again every time it is used.</param>
public sealed record UpdateCheckPreferences(DateTimeOffset? LastCheckedUtc, string? DismissedVersion = null, string? LastFeedJson = null);

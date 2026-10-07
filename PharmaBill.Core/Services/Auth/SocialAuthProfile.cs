namespace PharmaBill.Core.Services.Auth;

public sealed record SocialAuthProfile(string Provider, string SubjectId, string? Email, string? Name);

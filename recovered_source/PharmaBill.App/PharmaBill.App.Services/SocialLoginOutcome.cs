using System;
using PharmaBill.Core.Services.Auth;

namespace PharmaBill.App.Services;

public sealed record SocialLoginOutcome(SocialLoginOutcomeKind Kind, SocialAuthProfile? Profile = null, string? Message = null, Guid? LinkableAdminId = null, string? LinkableAdminDisplayName = null);

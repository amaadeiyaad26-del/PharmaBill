using System;
using PharmaBill.Core.Services.Auth;

namespace PharmaBill.App.ViewModels;

public sealed record SocialLinkAdminRequest(SocialAuthProfile Profile, Guid AdminId, string AdminDisplayName);

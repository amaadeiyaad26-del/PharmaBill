using PharmaBill.Core.Entities;

namespace PharmaBill.App.Services;

public sealed class CurrentSession
{
    public AppUser? User { get; private set; }

    public bool IsAuthenticated => User is not null;

    public void SignIn(AppUser user) => User = user;

    public void SignOut() => User = null;
}

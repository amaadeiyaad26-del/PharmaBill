namespace PharmaBill.Data.Services;

public interface ISubscriptionProvider
{
    Task<SubscriptionState> GetStateAsync(CancellationToken cancellationToken = default);
}

public sealed record SubscriptionState(bool IsSubscribed, bool IsOfflineGrace);

public sealed class StubSubscriptionProvider : ISubscriptionProvider
{
    public Task<SubscriptionState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Integrate a subscription provider; subscription prices must never be hard-coded.
        return Task.FromResult(new SubscriptionState(IsSubscribed: false, IsOfflineGrace: false));
    }
}

using PharmaBill.App;
using PharmaBill.App.Services;
using Xunit;

namespace PharmaBill.Tests;

/// <summary>Both editions are exercised here in one run; only AppEdition.Current depends on the build.</summary>
public sealed class AppEditionTests
{
    [Fact]
    public void DefaultBuild_IsLocalMarket()
    {
        // The test suite builds the shared code with the development default (-p:Edition=LocalMarket).
        Assert.Equal(Edition.LocalMarket, AppEdition.Current);
    }

    [Theory]
    [InlineData(Edition.Store, "Pharma Remit", true, false)]
    [InlineData(Edition.LocalMarket, "Pharma Remit Local Market", false, true)]
    public void EditionDifferences(Edition edition, string productName, bool storeBadge, bool releaseFeed)
    {
        Assert.Equal(productName, AppEdition.ProductName(edition));
        Assert.Equal(storeBadge, AppEdition.ShowsStoreBadge(edition));
        Assert.Equal(releaseFeed, AppEdition.UsesReleaseFeed(edition));
    }

    [Fact]
    public void UpdateAdvice_MentionsTheStoreOnlyInTheStoreEdition()
    {
        Assert.Contains("Microsoft Store", AppEdition.UpdateAdvice(Edition.Store));
        Assert.DoesNotContain("Store", AppEdition.UpdateAdvice(Edition.LocalMarket));
    }

    [Fact]
    public void StoreEditionUpdateCheck_PointsToTheStoreAndNeverToTheInstaller()
    {
        AppUpdateAvailability result = AppUpdateService.StoreEditionResult("1.0.4.0");

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(UpdateChannel.MicrosoftStore, result.Channel);
        Assert.Equal(AppEdition.StoreUpdatesUri, result.DownloadOrStoreUrl);
        Assert.DoesNotContain("github", result.DownloadOrStoreUrl!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Microsoft Store", result.StatusMessage);
    }
}

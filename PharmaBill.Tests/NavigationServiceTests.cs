using PharmaBill.App.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class NavigationServiceTests
{
    [Fact]
    public void Navigate_ChangesCurrentSectionAndRaisesEvent()
    {
        var service = new NavigationService();
        var eventRaised = false;
        service.SectionChanged += (_, _) => eventRaised = true;

        service.Navigate("Stock");

        Assert.Equal("Stock", service.CurrentSectionKey);
        Assert.True(eventRaised);
    }

    [Fact]
    public void NavigateToCurrentSection_DoesNotRaiseEvent()
    {
        var service = new NavigationService();
        var eventRaised = false;
        service.SectionChanged += (_, _) => eventRaised = true;

        service.Navigate("Dashboard");

        Assert.False(eventRaised);
    }
}

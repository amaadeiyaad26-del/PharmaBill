using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using Xunit;

namespace PharmaBill.Tests;

public sealed class B2clThresholdNoticeTests
{
    [Fact]
    public void Notice_ShowsTheConfiguredThresholdInIndianFormat()
    {
        Assert.Equal(
            "B2CL threshold is set to ₹1,00,000. Confirm this with your accountant before filing.",
            GstReturnsPageViewModel.FormatB2clThresholdNotice(100000m));
        Assert.StartsWith("B2CL threshold is set to ₹2,50,000.", GstReturnsPageViewModel.FormatB2clThresholdNotice(250000m));
    }

    [Fact]
    public void NoticeStore_RemembersDismissalPerKey()
    {
        string path = Path.Combine(Path.GetTempPath(), "pb-notices-" + Guid.NewGuid().ToString("N"), "dismissed-notices.json");
        try
        {
            var store = new OneTimeNoticeStore(path);
            Assert.False(store.IsDismissed("b2cl-threshold-confirm:Fayaz"));

            store.Dismiss("b2cl-threshold-confirm:Fayaz");

            Assert.True(new OneTimeNoticeStore(path).IsDismissed("b2cl-threshold-confirm:Fayaz"));
            Assert.False(new OneTimeNoticeStore(path).IsDismissed("b2cl-threshold-confirm:Rumessa"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void NoticeStore_UnreadableFileShowsTheNoticeAgainRatherThanFailing()
    {
        string directory = Path.Combine(Path.GetTempPath(), "pb-notices-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "dismissed-notices.json");
        File.WriteAllText(path, "not json");
        try
        {
            Assert.False(new OneTimeNoticeStore(path).IsDismissed("b2cl-threshold-confirm:Fayaz"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

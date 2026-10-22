using System.Security.Cryptography;
using System.Text;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Sync.Terminals;
using Xunit;

namespace PharmaBill.Tests;

public sealed class TerminalSessionsTests
{
    private sealed class MovableClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }

    private static readonly AppUser Cashier = new() { DisplayName = "Asha", UserName = "asha", Role = UserRole.BillingClerk };

    [Fact]
    public void SessionsSurviveARestart_WithoutTheTokenOnDisk()
    {
        string directory = NewDirectory();
        try
        {
            Guid counter = Guid.NewGuid();
            (TerminalSession session, string token) = new TerminalSessions(directory).Start(counter, Cashier);

            // The server PC restarts: a new instance reads the saved sessions.
            var afterRestart = new TerminalSessions(directory);
            Assert.Equal(session.UserId, afterRestart.Find(token, counter)?.UserId);
            Assert.Null(afterRestart.Find(token, Guid.NewGuid()));

            byte[] saved = ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(directory, "terminal-sessions.dat")), null, DataProtectionScope.CurrentUser);
            Assert.DoesNotContain(token, Encoding.UTF8.GetString(saved));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExpiryStillApplies_AcrossARestart()
    {
        string directory = NewDirectory();
        var clock = new MovableClock(new DateTime(2026, 10, 22, 3, 0, 0, DateTimeKind.Utc));
        using IDisposable pinned = BusinessClock.Use(clock);
        try
        {
            Guid counter = Guid.NewGuid();
            var sessions = new TerminalSessions(directory);
            (_, string idleToken) = sessions.Start(counter, Cashier);
            (_, string busyToken) = sessions.Start(counter, Cashier);

            // A 40-minute reboot: both still signed in.
            clock.UtcNow = clock.UtcNow.AddMinutes(40);
            var rebooted = new TerminalSessions(directory);
            Assert.NotNull(rebooted.Find(busyToken, counter));

            // Idle more than 2 hours (server-off time counts): signed out.
            clock.UtcNow = clock.UtcNow.AddHours(2).AddMinutes(1);
            Assert.Null(new TerminalSessions(directory).Find(idleToken, counter));

            // In use all day, but 12 hours after sign-in the shift's session ends anyway.
            var allDay = new TerminalSessions(directory);
            (_, string shiftToken) = allDay.Start(counter, Cashier);
            for (int hour = 0; hour < 11; hour++)
            {
                clock.UtcNow = clock.UtcNow.AddHours(1);
                Assert.NotNull(allDay.Find(shiftToken, counter));
            }
            clock.UtcNow = clock.UtcNow.AddHours(1).AddMinutes(1);
            Assert.Null(allDay.Find(shiftToken, counter));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NewDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"PharmaBill.Sessions.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}

using PharmaBill.Core;
using PharmaBill.Sync.Terminals;
using Xunit;

namespace PharmaBill.Tests;

public sealed class TerminalPairingTests
{
    private sealed class MovableClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }

    [Fact]
    public void ACode_ExpiresAfterFiveMinutes()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"PharmaBill.Pairing.{Guid.NewGuid():N}");
        var clock = new MovableClock(new DateTime(2026, 10, 22, 4, 0, 0, DateTimeKind.Utc));
        using IDisposable pinned = BusinessClock.Use(clock);
        try
        {
            var certificate = new TerminalServerCertificate(directory);
            var pairing = new TerminalPairing(new TerminalDeviceStore(directory), certificate);
            TerminalPairingCode code = pairing.Open();
            string nonce = pairing.ServerNonce!;
            string terminalNonce = "0011223344556677";
            byte[] key = TerminalPairingProof.Key(code.Code, nonce, terminalNonce);
            string proof = TerminalPairingProof.TerminalProof(key, certificate.Fingerprint, nonce, terminalNonce, "Counter");

            // The right proof, a second too late.
            clock.UtcNow = clock.UtcNow.AddMinutes(5).AddSeconds(1);

            Assert.Null(pairing.Current);
            TerminalPairingOutcome late = pairing.TryPair("Counter", terminalNonce, proof);
            Assert.Null(late.Device);
            Assert.Contains("No pairing code is open", late.Error);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Codes_UseAnAlphabetWithoutLookAlikes()
    {
        Assert.DoesNotContain('0', TerminalPairingProof.Alphabet);
        Assert.DoesNotContain('O', TerminalPairingProof.Alphabet);
        Assert.DoesNotContain('1', TerminalPairingProof.Alphabet);
        Assert.DoesNotContain('I', TerminalPairingProof.Alphabet);
        Assert.Equal("AB3XYZ", TerminalPairingProof.Normalize(" ab3-xyz "));
    }
}

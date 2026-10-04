using PharmaBill.Data.Persistence;
using Xunit;

namespace PharmaBill.Tests;

public sealed class EncryptionKeyProviderTests
{
    [Fact]
    public void GetOrCreateKey_PersistsAUserProtectedKey()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PharmaBill.KeyTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var keyPath = Path.Combine(directory, "database.key");
            var first = new DatabaseEncryptionKeyProvider(keyPath).GetOrCreateKey();
            var second = new DatabaseEncryptionKeyProvider(keyPath).GetOrCreateKey();

            Assert.Equal(32, first.Length);
            Assert.Equal(first, second);
            Assert.False(first.SequenceEqual(File.ReadAllBytes(keyPath)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

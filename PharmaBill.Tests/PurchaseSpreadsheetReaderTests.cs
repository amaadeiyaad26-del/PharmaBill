using PharmaBill.App.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class PurchaseSpreadsheetReaderTests
{
    [Fact]
    public async Task CsvReader_PreservesQuotedCommasAndColumns()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pharmabill-purchase-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(
            path,
            "Drug Name,Batch,Expiry,Quantity,Rate,MRP,Amount\r\n\"Foo, tablets\",A1,2027-12-31,2,5,8,10\r\n");

        try
        {
            var result = await new PurchaseSpreadsheetReader().ReadAsync(path);

            Assert.Equal(7, result.Headers.Count);
            Assert.Single(result.Rows);
            Assert.Equal("Foo, tablets", result.Rows[0]["Drug Name"]);
            Assert.Equal("A1", result.Rows[0]["Batch"]);
            Assert.Equal("10", result.Rows[0]["Amount"]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

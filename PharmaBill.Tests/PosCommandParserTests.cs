using PharmaBill.Core.Pos;
using Xunit;

namespace PharmaBill.Tests;

public sealed class PosCommandParserTests
{
    [Theory]
    [InlineData("sold 1 strip of pacimol to mr x", 1, PosQuantityUnit.Pack, "strip", "pacimol", "Mr X")]
    [InlineData("1 strip paracetamol to John", 1, PosQuantityUnit.Pack, "strip", "paracetamol", "John")]
    [InlineData("Sell two strips of Dolo 650 for Mrs Rao.", 2, PosQuantityUnit.Pack, "strip", "Dolo 650", "Mrs Rao")]
    [InlineData("pacimol 3 strips to dr. a. shah", 3, PosQuantityUnit.Pack, "strip", "pacimol", "Dr. A. Shah")]
    [InlineData("dolo 650 x2", 2, PosQuantityUnit.Pack, "pack", "dolo 650", null)]
    [InlineData("dolo 650", 1, PosQuantityUnit.Pack, "pack", "dolo 650", null)]
    [InlineData("a bottle of benadryl", 1, PosQuantityUnit.Pack, "bottle", "benadryl", null)]
    [InlineData("gave 5 tablets crocin to ali", 5, PosQuantityUnit.Piece, "tablet", "crocin", "Ali")]
    [InlineData("half strip azithral", 0.5, PosQuantityUnit.Pack, "strip", "azithral", null)]
    [InlineData("2 combiflam", 2, PosQuantityUnit.Pack, "pack", "combiflam", null)]
    public void Reads_quantity_unit_medicine_and_customer(string text, double quantity, PosQuantityUnit unit, string unitWord, string medicine, string? customer)
    {
        Assert.True(PosCommandParser.TryParse(text, out PosSaleCommand? command, out string error), error);
        Assert.Equal(new PosSaleCommand((decimal)quantity, unit, unitWord, medicine, customer), command);
    }

    [Theory]
    [InlineData("", "Type a sale")]
    [InlineData("sold 2 strips", "Which medicine?")]
    [InlineData("0 strips dolo", "more than 0")]
    public void Says_what_is_missing(string text, string expected)
    {
        Assert.False(PosCommandParser.TryParse(text, out _, out string error));
        Assert.Contains(expected, error);
    }

    [Theory]
    [InlineData("strip of 10 tablets", 10)]
    [InlineData("15 Tablets / Strip", 15)]
    [InlineData("10 Capsules", 10)]
    [InlineData("100ml Bottle", null)]
    [InlineData(null, null)]
    public void Pieces_per_pack_come_from_the_pack_label(string? label, int? pieces)
    {
        Assert.Equal(pieces, PosCommandParser.PiecesPerPack(label));
    }
}

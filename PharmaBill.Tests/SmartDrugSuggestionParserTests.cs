using PharmaBill.Core.Ai;
using Xunit;

namespace PharmaBill.Tests;

public class SmartDrugSuggestionParserTests
{
	[Fact]
	public void Parse_ReadsJsonInsideCodeFence()
	{
		string reply = "```json\n{\"brandName\":\"Foo 500\",\"composition\":\"Paracetamol 500mg\",\"manufacturer\":\"Acme\",\"packLabel\":\"Strip\",\"unitsPerPack\":10,\"hsnCode\":\"3004\",\"gstRate\":12,\"approxMrp\":\"\u20B932.456\"}\n```";
		SmartDrugSuggestion? s = SmartDrugSuggestionParser.Parse(reply);
		Assert.NotNull(s);
		Assert.Equal("Foo 500", s!.BrandName);
		Assert.Equal("Paracetamol 500mg", s.GenericName);
		Assert.Equal("Strip", s.PackSizeLabel);
		Assert.Equal(12m, s.GstRate);
		Assert.Equal(32.46m, s.ApproximateMrp);
	}

	[Fact]
	public void Parse_DropsInvalidValuesAndRejectsGarbage()
	{
		SmartDrugSuggestion? s = SmartDrugSuggestionParser.Parse("{\"brandName\":\"X\",\"hsnCode\":\"abc\",\"gstRate\":500,\"approxMrp\":-4,\"manufacturer\":\"unknown\"}");
		Assert.NotNull(s);
		Assert.Equal("3004", s!.HsnCode);
		Assert.Equal(12m, s.GstRate);
		Assert.Null(s.ApproximateMrp);
		Assert.Null(s.Manufacturer);
		Assert.Null(SmartDrugSuggestionParser.Parse("sorry, I do not know"));
		Assert.Null(SmartDrugSuggestionParser.Parse("{bad json"));
		Assert.Null(SmartDrugSuggestionParser.Parse(null));
	}
}

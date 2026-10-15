using QuestPDF.Fluent;

namespace PharmaBill.App.Services;

/// <summary>
/// A faint "PR" monogram centred behind every page of a generated document. A tax invoice must be legible
/// first, so it sits on the page background layer (under all text) and is kept small and nearly transparent.
/// </summary>
public static class PdfBrandWatermark
{
	public const string Monogram = "PR";

	private const float FontSizePoints = 96f;

	/// <summary>Slate ink at alpha 0x0A (10/255, about 4% opacity).</summary>
	private const string InkArgb = "#0A0F172A";

	public static void Apply(PageDescriptor page)
	{
		page.Background().AlignCenter().AlignMiddle()
			.Text(Monogram)
			.FontSize(FontSizePoints)
			.Bold()
			.FontColor(InkArgb);
	}
}

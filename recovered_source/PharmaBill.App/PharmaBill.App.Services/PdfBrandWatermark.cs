using QuestPDF.Fluent;

namespace PharmaBill.App.Services;

public static class PdfBrandWatermark
{
	private const float SizePoints = 300f;

	public static void Apply(PageDescriptor page)
	{
		byte[] array = BrandAssets.TryGetWatermarkPng();
		if (array != null)
		{
			page.Background().AlignCenter().AlignMiddle()
				.Width(300f)
				.Height(300f)
				.Image(array)
				.FitArea();
		}
	}
}

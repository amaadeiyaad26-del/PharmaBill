using System.Windows;

namespace PharmaBill.App.Services;

public sealed class PromptService : IPromptService
{
	public string? AskText(string title, string message, string label, string? initialText = null)
	{
		TextPromptWindow textPromptWindow = new TextPromptWindow(title, message, label, initialText)
		{
			Owner = Application.Current?.MainWindow
		};
		if (textPromptWindow.ShowDialog() != true)
		{
			return null;
		}
		return textPromptWindow.Value;
	}

	public ProductTaxPromptResult? AskProductTax(string productName, string? currentHsn, decimal? currentRate, bool currentNilRated, bool newProduct = false, ProductTaxHints? hints = null)
	{
		ProductTaxPromptWindow window = new ProductTaxPromptWindow(productName, currentHsn, currentRate, currentNilRated, newProduct, hints)
		{
			Owner = Application.Current?.MainWindow
		};
		if (window.ShowDialog() != true || window.Details == null)
		{
			return null;
		}
		return new ProductTaxPromptResult(window.Details, window.SaveToProduct);
	}
}

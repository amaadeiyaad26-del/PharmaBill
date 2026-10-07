using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PharmaBill.App.Services;

namespace PharmaBill.App;

public sealed class InvoiceFormatPickerWindow : Window
{
	public (InvoiceTemplateType Template, bool Preview)? Result { get; private set; }

	public InvoiceFormatPickerWindow(bool allowPreview)
	{
		Title = "Invoice format";
		Width = 420.0;
		Height = (allowPreview ? 280 : 220);
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		ResizeMode = ResizeMode.NoResize;
		Background = Brushes.White;
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(20.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Choose invoice layout",
			FontSize = 16.0,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "3-inch / 80 mm thermal slip uses ESC/POS. A4/A5 tax invoice uses the Windows print dialog (or silent print when enabled).",
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 0.0, 0.0, 16.0),
			Foreground = Brushes.DimGray
		});
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Vertical
		};
		stackPanel2.Children.Add(CreateButton("3-Inch / 80 mm Thermal Slip", () =>
		{
			Accept(InvoiceTemplateType.Thermal80mm, preview: false);
		}));
		stackPanel2.Children.Add(CreateButton("A4 / A5 Tax Invoice", () =>
		{
			Accept(InvoiceTemplateType.StandardA4, preview: false);
		}));
		if (allowPreview)
		{
			stackPanel2.Children.Add(CreateButton("Preview A4 PDF", () =>
			{
				Accept(InvoiceTemplateType.StandardA4, preview: true);
			}));
			stackPanel2.Children.Add(CreateButton("Preview Thermal Text", () =>
			{
				Accept(InvoiceTemplateType.Thermal80mm, preview: true);
			}));
		}
		Button button = new Button
		{
			Content = "Cancel",
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			HorizontalAlignment = HorizontalAlignment.Right
		};
		button.Click += (object _, RoutedEventArgs _) =>
		{
			DialogResult = false;
			Close();
		};
		stackPanel.Children.Add(stackPanel2);
		stackPanel.Children.Add(button);
		Content = stackPanel;
	}

	private Button CreateButton(string label, Action action)
	{
		Button button = new Button();
		button.Content = label;
		button.Margin = new Thickness(0.0, 0.0, 0.0, 8.0);
		button.Padding = new Thickness(12.0, 10.0, 12.0, 10.0);
		button.HorizontalAlignment = HorizontalAlignment.Stretch;
		button.Click += (object _, RoutedEventArgs _) =>
		{
			action();
		};
		return button;
	}

	private void Accept(InvoiceTemplateType template, bool preview)
	{
		Result = (template, preview);
		DialogResult = true;
		Close();
	}
}

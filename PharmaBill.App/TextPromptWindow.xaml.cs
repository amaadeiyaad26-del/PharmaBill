using System;
using System.Diagnostics;
using System.Windows;

namespace PharmaBill.App;

public partial class TextPromptWindow : Window
{
	public string Value => InputBox.Text.Trim();

	public TextPromptWindow(string title, string message, string label, string? initialText = null)
	{
		InitializeComponent();
		Title = title;
		MessageText.Text = message;
		InputField.Label = label;
		InputBox.Text = initialText ?? string.Empty;
		Loaded += (object _, RoutedEventArgs _) =>
		{
			InputBox.Focus();
			InputBox.SelectAll();
		};
	}

	private void Ok_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(InputBox.Text))
		{
			InputField.ErrorText = InputField.Label + " is required";
			InputBox.Focus();
		}
		else
		{
			DialogResult = true;
		}
	}

}

using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Windows;
using System.Windows.Markup;

namespace PharmaBill.App;

public partial class TextPromptWindow : Window, IComponentConnector
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

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.12.0")]
	internal Delegate _CreateDelegate(Type delegateType, string handler)
	{
		return Delegate.CreateDelegate(delegateType, this, handler);
	}
}

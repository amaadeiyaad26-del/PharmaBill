using System.Windows;
using System.Windows.Controls;

namespace PharmaBill.App.Services;

public sealed class ConfirmationService : IConfirmationService
{
	public bool Confirm(string message, string title)
	{
		return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes;
	}

	public bool? Choose(string message, string title, string acceptLabel, string alternateLabel)
	{
		bool? choice = null;
		Window window = new Window
		{
			Title = title,
			MinWidth = 420,
			MaxWidth = 560,
			SizeToContent = SizeToContent.WidthAndHeight,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			ResizeMode = ResizeMode.NoResize,
		};
		Window? owner = Application.Current?.MainWindow;
		if (owner != null && owner.IsLoaded)
		{
			window.Owner = owner;
		}
		StackPanel panel = new StackPanel { Margin = new Thickness(20) };
		panel.Children.Add(new TextBlock
		{
			Text = message,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0, 0, 0, 16)
		});
		StackPanel buttons = new StackPanel { Orientation = Orientation.Vertical };
		Button accept = new Button { Content = acceptLabel, MinWidth = 280, Margin = new Thickness(0, 0, 0, 8), IsDefault = true, Padding = new Thickness(12, 8, 12, 8), HorizontalContentAlignment = HorizontalAlignment.Center };
		Button alternate = new Button { Content = alternateLabel, MinWidth = 280, Padding = new Thickness(12, 8, 12, 8), HorizontalContentAlignment = HorizontalAlignment.Center };
		accept.Click += (_, _) =>
		{
			choice = true;
			window.Close();
		};
		alternate.Click += (_, _) =>
		{
			choice = false;
			window.Close();
		};
		buttons.Children.Add(accept);
		buttons.Children.Add(alternate);
		panel.Children.Add(buttons);
		window.Content = panel;
		window.ShowDialog();
		return choice;
	}

	public void Notify(string title, string message)
	{
		MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	public void NotifyError(string title, string message)
	{
		MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Hand);
	}
}

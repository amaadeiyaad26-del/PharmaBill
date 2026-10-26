using System.Windows;
using System.Windows.Controls;

namespace PharmaBill.App.Controls;

/// <summary>
/// Lets a PasswordBox's text reach a view-model property, in both directions.
/// </summary>
public static class PasswordBoxBinding
{
	// The default must never equal a value a binding can deliver. With string.Empty as the default, a view-model
	// starting at "" never changed the property, so the typing handler was never attached and nothing typed ever
	// reached the view-model (the Backup page saw an empty password however long the one typed).
	private static readonly string Unbound = new string('\0', 1);

	public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached("Password", typeof(string), typeof(PasswordBoxBinding), new FrameworkPropertyMetadata(Unbound, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

	public static string GetPassword(DependencyObject element)
	{
		string value = (string)element.GetValue(PasswordProperty);
		return ReferenceEquals(value, Unbound) ? string.Empty : value;
	}

	public static void SetPassword(DependencyObject element, string value)
	{
		element.SetValue(PasswordProperty, value);
	}

	private static void OnPasswordChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is PasswordBox passwordBox)
		{
			passwordBox.PasswordChanged -= OnControlPasswordChanged;
			string text = (args.NewValue as string) ?? string.Empty;
			if (!ReferenceEquals(text, Unbound) && !string.Equals(passwordBox.Password, text, StringComparison.Ordinal))
			{
				passwordBox.Password = text;
			}
			passwordBox.PasswordChanged += OnControlPasswordChanged;
		}
	}

	private static void OnControlPasswordChanged(object sender, RoutedEventArgs args)
	{
		if (sender is PasswordBox passwordBox)
		{
			passwordBox.SetCurrentValue(PasswordProperty, passwordBox.Password);
		}
	}
}

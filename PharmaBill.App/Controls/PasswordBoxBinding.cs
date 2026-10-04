using System.Windows;
using System.Windows.Controls;

namespace PharmaBill.App.Controls;

public static class PasswordBoxBinding
{
    public static readonly DependencyProperty PasswordProperty =
        DependencyProperty.RegisterAttached(
            "Password",
            typeof(string),
            typeof(PasswordBoxBinding),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    public static string GetPassword(DependencyObject element) => (string)element.GetValue(PasswordProperty);

    public static void SetPassword(DependencyObject element, string value) => element.SetValue(PasswordProperty, value);

    private static void OnPasswordChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not PasswordBox passwordBox)
        {
            return;
        }

        passwordBox.PasswordChanged -= OnControlPasswordChanged;
        var password = args.NewValue as string ?? string.Empty;
        if (!string.Equals(passwordBox.Password, password, StringComparison.Ordinal))
        {
            passwordBox.Password = password;
        }

        passwordBox.PasswordChanged += OnControlPasswordChanged;
    }

    private static void OnControlPasswordChanged(object sender, RoutedEventArgs args)
    {
        if (sender is PasswordBox passwordBox)
        {
            passwordBox.SetCurrentValue(PasswordProperty, passwordBox.Password);
        }
    }
}

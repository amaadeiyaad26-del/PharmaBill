using System.Windows;

namespace PharmaBill.App;

public partial class PinPromptWindow : Window
{
    public PinPromptWindow(string prompt)
    {
        InitializeComponent();
        Prompt = prompt;
        DataContext = this;
        Loaded += (_, _) => PinBox.Focus();
    }

    public string Prompt { get; }

    public string ErrorMessage { get; private set; } = string.Empty;

    public string Pin => PinBox.Password;

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PinBox.Password))
        {
            ErrorMessage = "Enter your PIN or password.";
            DataContext = null;
            DataContext = this;
            PinBox.Focus();
            return;
        }

        DialogResult = true;
    }
}

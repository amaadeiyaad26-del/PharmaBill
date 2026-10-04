using System.Windows;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Entities;

namespace PharmaBill.App;

public partial class FirstRunWizardWindow : Window
{
    private readonly CurrentSession _currentSession;

    public FirstRunWizardWindow(FirstRunWizardViewModel viewModel, CurrentSession currentSession)
    {
        _currentSession = currentSession;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.SetupCompleted += OnSetupCompleted;
    }

    private void SecretBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is FirstRunWizardViewModel viewModel)
        {
            viewModel.OwnerSecret = SecretBox.Password;
        }
    }

    private void ConfirmSecretBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is FirstRunWizardViewModel viewModel)
        {
            viewModel.OwnerSecretConfirmation = ConfirmSecretBox.Password;
        }
    }

    private void OnSetupCompleted(object? sender, EventArgs e)
    {
        if (DataContext is FirstRunWizardViewModel viewModel)
        {
            new RecoveryCodeWindow(viewModel.RecoveryCode) { Owner = this }.ShowDialog();
        }

        if (sender is AppUser owner)
        {
            _currentSession.SignIn(owner);
        }

        DialogResult = true;
    }
}

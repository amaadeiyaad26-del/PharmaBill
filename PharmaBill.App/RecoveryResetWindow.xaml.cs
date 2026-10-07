using System;
using System.Windows;
using PharmaBill.App.Services;

namespace PharmaBill.App;

public partial class RecoveryResetWindow : Window
{
	private readonly OwnerRecoveryService _recoveryService;

	public RecoveryResetWindow(OwnerRecoveryService recoveryService, string username)
	{
		_recoveryService = recoveryService;
		InitializeComponent();
		UsernameBox.Text = username;
		UsernameBox.IsReadOnly = !string.IsNullOrWhiteSpace(username);
	}

	private async void Reset_Click(object sender, RoutedEventArgs e)
	{
		ErrorText.Text = string.Empty;
		if (NewPasswordBox.Password.Length < 6)
		{
			ErrorText.Text = "PIN or password must contain at least 6 characters.";
			return;
		}
		if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
		{
			ErrorText.Text = "The new PIN or password confirmation does not match.";
			return;
		}
		try
		{
			if (!(await _recoveryService.ResetOwnerPasswordAsync(UsernameBox.Text.Trim(), RecoveryCodeBox.Text, NewPasswordBox.Password)))
			{
				ErrorText.Text = "The owner username or recovery code is incorrect.";
			}
			else
			{
				DialogResult = true;
			}
		}
		catch (Exception ex)
		{
			ErrorText.Text = ex.Message;
		}
	}
}

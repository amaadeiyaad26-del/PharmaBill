using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.IO;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSession _currentSession;
    private readonly DispatcherTimer _idleTimer;
    private DateTime _lastInputUtc = DateTime.UtcNow;
    private bool _showingLockScreen;

    public MainWindow(
        MainWindowViewModel viewModel,
        IServiceScopeFactory scopeFactory,
        CurrentSession currentSession)
    {
        _viewModel = viewModel;
        _scopeFactory = scopeFactory;
        _currentSession = currentSession;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
        InputManager.Current.PreProcessInput += OnPreProcessInput;
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _idleTimer.Tick += OnIdleTimerTick;
        _idleTimer.Start();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        AttachRetailPage(_viewModel.CurrentPage);
        await _viewModel.InitializeAsync();
        AttachRetailPage(_viewModel.CurrentPage);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _idleTimer.Stop();
        InputManager.Current.PreProcessInput -= OnPreProcessInput;
        _viewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
        AttachRetailPage(null);
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
        {
            AttachRetailPage(_viewModel.CurrentPage);
        }
    }

    private RetailBillingViewModel? _attachedRetailPage;

    private void AttachRetailPage(object? page)
    {
        if (_attachedRetailPage is not null)
        {
            _attachedRetailPage.PropertyChanged -= OnRetailPagePropertyChanged;
        }

        _attachedRetailPage = page as RetailBillingViewModel;
        if (_attachedRetailPage is not null)
        {
            _attachedRetailPage.PropertyChanged += OnRetailPagePropertyChanged;
            FocusRetailControl("PatientNameBox");
        }
    }

    private void OnRetailPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RetailBillingViewModel.FocusRequest) ||
            sender is not RetailBillingViewModel page)
        {
            return;
        }

        var targetName = page.FocusRequest switch
        {
            "ItemSearch" => "RetailItemSearchBox",
            "PatientName" => "PatientNameBox",
            "PatientPhone" => "PatientPhoneBox",
            "PatientAddress" => "PatientAddressBox",
            "DoctorName" => "DoctorNameBox",
            "DoctorRegistration" => "DoctorRegistrationBox",
            "PaymentAmount" => "PaymentAmountBox",
            _ => null
        };
        if (targetName is not null)
        {
            FocusRetailControl(targetName);
        }
    }

    private void FocusRetailControl(string name)
    {
        // The page template may not be in the visual tree yet when the request is raised.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var target = FindVisualChildByName(this, name);
            target?.Focus();
            if (target is TextBox textBox)
            {
                textBox.SelectAll();
            }
        });
    }

    private static FrameworkElement? FindVisualChildByName(DependencyObject parent, string name)
    {
        if (parent is not Visual && parent is not System.Windows.Media.Media3D.Visual3D)
        {
            return null;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element && element.Name == name)
            {
                return element;
            }

            var nested = FindVisualChildByName(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void SmtpPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && passwordBox.DataContext is SettingsPageViewModel settings)
        {
            settings.SmtpPassword = passwordBox.Password;
        }
    }

    private void SaveDocumentOutputSettings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SettingsPageViewModel settings })
        {
            settings.SaveDocumentOutputSettingsCommand.Execute(null);
            var passwordBox = FindVisualChildByName(this, "SmtpPasswordBox") as PasswordBox;
            passwordBox?.Clear();
        }
    }

    private void RegisterTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not TabControl { DataContext: StatutoryRegistersPageViewModel page } ||
            sender is not TabControl { SelectedValue: string registerType })
        {
            return;
        }

        page.SelectedRegisterType = registerType;
        page.SearchCommand.Execute(null);
    }

    private void InspectorPinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && passwordBox.DataContext is SettingsPageViewModel settings)
        {
            settings.InspectorPinInput = passwordBox.Password;
        }
    }

    private void InspectorPinConfirmationBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && passwordBox.DataContext is SettingsPageViewModel settings)
        {
            settings.InspectorPinConfirmation = passwordBox.Password;
        }
    }

    private void SaveInspectorPin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SettingsPageViewModel settings })
        {
            settings.SaveDocumentOutputSettingsCommand.Execute(null);
            (FindVisualChildByName(this, "InspectorPinBox") as PasswordBox)?.Clear();
            (FindVisualChildByName(this, "InspectorPinConfirmationBox") as PasswordBox)?.Clear();
        }
    }

    private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is InputEventArgs input &&
            (input.RoutedEvent == Keyboard.KeyDownEvent ||
             input.RoutedEvent == Mouse.MouseDownEvent ||
             input.RoutedEvent == Mouse.MouseMoveEvent))
        {
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    private async void OnIdleTimerTick(object? sender, EventArgs e)
    {
        var timeout = TimeSpan.FromMinutes(Math.Clamp(_currentSession.User?.IdleLockMinutes ?? 10, 1, 240));
        if (_showingLockScreen || DateTime.UtcNow - _lastInputUtc < timeout)
        {
            return;
        }

        _showingLockScreen = true;
        _currentSession.SignOut();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var lockWindow = scope.ServiceProvider.GetRequiredService<AuthenticationWindow>();
            lockWindow.Owner = this;
            if (lockWindow.ShowDialog() != true)
            {
                Application.Current.Shutdown();
                return;
            }

            _viewModel.RefreshLoggedInUser();
            _lastInputUtc = DateTime.UtcNow;
        }
        finally
        {
            _showingLockScreen = false;
        }
    }

    private void OpenAccessSimulator_Click(object sender, RoutedEventArgs e)
    {
#if DEBUG
        var window = new AccessSimulatorWindow { Owner = this };
        window.ShowDialog();
#endif
    }

    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        using var scope = _scopeFactory.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<DiagnosticsWindow>();
        window.Owner = this;
        window.ShowDialog();
    }

    private void OpenCatalogImport_Click(object sender, RoutedEventArgs e)
    {
        using var scope = _scopeFactory.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<CatalogImportWindow>();
        window.Owner = this;
        window.ShowDialog();
    }

    private void OpenRecentBills_Click(object sender, RoutedEventArgs e)
    {
        using var scope = _scopeFactory.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<RecentBillsWindow>();
        window.Owner = this;
        window.ShowDialog();
    }

    private void PurchaseDocument_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void PurchaseDocument_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files)
        {
            return;
        }

        var extension = Path.GetExtension(files[0]);
        if (!string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Drop a PDF, PNG or JPEG purchase document.",
                "Unsupported purchase document",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_viewModel.CurrentPage is PurchasePageViewModel purchasePage)
        {
            await purchasePage.ImportDocumentFromPathAsync(files[0]);
        }
    }
}

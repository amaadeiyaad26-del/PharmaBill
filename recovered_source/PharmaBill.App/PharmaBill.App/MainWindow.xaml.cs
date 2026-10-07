using System;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Data.Services;

namespace PharmaBill.App;

public partial class MainWindow : Window, IComponentConnector, IStyleConnector
{
	private readonly string[] _searchHints = new string[5] { "Search 'Paracetamol 650mg'...", "Search 'Amoxicillin 500mg'...", "Search 'Azithromycin'...", "Search 'Pantoprazole'...", "Search 'Cetirizine 10mg'..." };

	private readonly MainWindowViewModel _viewModel;

	private readonly IServiceScopeFactory _scopeFactory;

	private readonly CurrentSession _currentSession;

	private readonly CatalogueService _catalogueService;

	private readonly DispatcherTimer _idleTimer;

	private readonly BarcodeScannerListener _barcodeListener = new BarcodeScannerListener();

	private DispatcherTimer? _hintTimer;

	private DispatcherTimer? _profilePopupCloseTimer;

	private int _hintIndex;

	private DateTime _lastInputUtc = DateTime.UtcNow;

	private bool _showingLockScreen;

	private RetailBillingViewModel? _attachedRetailPage;

	public MainWindow(MainWindowViewModel viewModel, IServiceScopeFactory scopeFactory, CurrentSession currentSession, CatalogueService catalogueService)
	{
		_viewModel = viewModel;
		_scopeFactory = scopeFactory;
		_currentSession = currentSession;
		_catalogueService = catalogueService;
		InitializeComponent();
		DataContext = viewModel;
		Loaded += OnLoaded;
		Closed += OnClosed;
		InputManager.Current.PreProcessInput += OnPreProcessInput;
		_idleTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(15L)
		};
		_idleTimer.Tick += OnIdleTimerTick;
		_idleTimer.Start();
		_barcodeListener.BarcodeScanned += OnBarcodeScanned;
		PreviewKeyDown += (object _, KeyEventArgs e) =>
		{
			if (e.Key == Key.B && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
			{
				OpenBarcodeSimulator();
				e.Handled = true;
			}
			else
			{
				_barcodeListener.ProcessKeyDown(e);
			}
		};
		PreviewTextInput += (object _, TextCompositionEventArgs e) =>
		{
			_barcodeListener.ProcessTextInput(e);
		};
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		_viewModel.PropertyChanged += OnMainViewModelPropertyChanged;
		_viewModel.LockRequested += OnLockRequested;
		AttachRetailPage(_viewModel.CurrentPage);
		InitAnimatedSearchHint();
		await _viewModel.InitializeAsync();
		AttachRetailPage(_viewModel.CurrentPage);
		_viewModel.StartBackgroundCatalogImportAsync();
	}

	private async void OnClosed(object? sender, EventArgs e)
	{
		_idleTimer.Stop();
		_hintTimer?.Stop();
		_hintTimer = null;
		try
		{
			await _viewModel.SyncGoogleDriveOnExitAsync();
		}
		catch
		{
		}
		_barcodeListener.BarcodeScanned -= OnBarcodeScanned;
		_barcodeListener.Reset();
		InputManager.Current.PreProcessInput -= OnPreProcessInput;
		_viewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
		_viewModel.LockRequested -= OnLockRequested;
		AttachRetailPage(null);
	}

	private void OpenBarcodeSimulator()
	{
		SimulateScannerWindow simulateScannerWindow = new SimulateScannerWindow
		{
			Owner = this
		};
		if (simulateScannerWindow.ShowDialog() == true && !string.IsNullOrWhiteSpace(simulateScannerWindow.ResultBarcode))
		{
			_barcodeListener.SimulateScan(simulateScannerWindow.ResultBarcode);
		}
	}

	private void ShowItemQuickToast(string message)
	{
		_viewModel.ShowItemQuickToast(message);
	}

	private async void OnBarcodeScanned(string barcode)
	{
		_ = 1;
		try
		{
			SystemSounds.Beep.Play();
			CatalogueBarcodeMatch catalogueBarcodeMatch = await _catalogueService.FindByBarcodeAsync(barcode);
			if ((object)catalogueBarcodeMatch != null)
			{
				string value = (string.IsNullOrWhiteSpace(catalogueBarcodeMatch.BrandName) ? catalogueBarcodeMatch.Name : catalogueBarcodeMatch.BrandName);
				string value2 = (string.IsNullOrWhiteSpace(catalogueBarcodeMatch.Strength) ? "—" : catalogueBarcodeMatch.Strength);
				ShowItemQuickToast($"Scanned: {value} ({value2})");
				await _viewModel.HandleBarcodeScannedAsync(barcode, showLookupToast: false);
			}
			else
			{
				ShowItemQuickToast("Barcode " + barcode + " not found in inventory.");
				_viewModel.ClearScannedItemPopover();
			}
		}
		catch (Exception)
		{
		}
	}

	private void InitAnimatedSearchHint()
	{
		if (_hintTimer != null)
		{
			return;
		}
		TxtSearchHint.Text = _searchHints[0];
		_hintTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(2.5)
		};
		_hintTimer.Tick += (object? _, EventArgs _) =>
		{
			if (!string.IsNullOrEmpty(SearchBox.Text) || SearchBox.IsKeyboardFocusWithin)
			{
				TxtSearchHint.Visibility = Visibility.Collapsed;
			}
			else
			{
				TxtSearchHint.Visibility = Visibility.Visible;
				_hintIndex = (_hintIndex + 1) % _searchHints.Length;
				DoubleAnimation animation = new DoubleAnimation(0.0, -10.0, TimeSpan.FromMilliseconds(180L));
				DoubleAnimation doubleAnimation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(180L));
				doubleAnimation.Completed += (object? obj, EventArgs e) =>
				{
					TxtSearchHint.Text = _searchHints[_hintIndex];
					DoubleAnimation animation2 = new DoubleAnimation(10.0, 0.0, TimeSpan.FromMilliseconds(180L));
					DoubleAnimation animation3 = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180L));
					TxtSearchHint.RenderTransform.BeginAnimation(TranslateTransform.YProperty, animation2);
					TxtSearchHint.BeginAnimation(UIElement.OpacityProperty, animation3);
				};
				TxtSearchHint.RenderTransform.BeginAnimation(TranslateTransform.YProperty, animation);
				TxtSearchHint.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
			}
		};
		_hintTimer.Start();
	}

	private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		TxtSearchHint.Visibility = ((!string.IsNullOrEmpty(SearchBox.Text)) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void SearchBox_FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (SearchBox.IsKeyboardFocusWithin || !string.IsNullOrEmpty(SearchBox.Text))
		{
			TxtSearchHint.Visibility = Visibility.Collapsed;
		}
		else
		{
			TxtSearchHint.Visibility = Visibility.Visible;
		}
	}

	private void NavTab_PreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		SoundHelper.PlayTick();
	}

	private void DashboardTile_MouseEnter(object sender, MouseEventArgs e)
	{
		SoundHelper.PlayHoverTick();
	}

	private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "CurrentPage")
		{
			AttachRetailPage(_viewModel.CurrentPage);
		}
		else if (e.PropertyName == "IsBusy" && _viewModel.IsBusy)
		{
			BusySpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0.0, 360.0, TimeSpan.FromSeconds(0.9))
			{
				RepeatBehavior = RepeatBehavior.Forever
			});
		}
	}

	private void AttachRetailPage(object? page)
	{
		if (_attachedRetailPage != null)
		{
			_attachedRetailPage.PropertyChanged -= OnRetailPagePropertyChanged;
		}
		_attachedRetailPage = page as RetailBillingViewModel;
		if (_attachedRetailPage != null)
		{
			_attachedRetailPage.PropertyChanged += OnRetailPagePropertyChanged;
			FocusRetailControl("PatientNameBox");
		}
	}

	private void OnRetailPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (!(e.PropertyName != "FocusRequest") && sender is RetailBillingViewModel retailBillingViewModel)
		{
			string text = retailBillingViewModel.FocusRequest switch
			{
				"ItemSearch" => "RetailItemSearchBox", 
				"PatientName" => "PatientNameBox", 
				"PatientPhone" => "PatientPhoneBox", 
				"PatientAddress" => "PatientAddressBox", 
				"DoctorName" => "DoctorNameBox", 
				"DoctorRegistration" => "DoctorRegistrationBox", 
				"PaymentAmount" => "PaymentAmountBox", 
				"SubstituteFlyout" => "RetailSubstituteList", 
				_ => null, 
			};
			if (text != null)
			{
				FocusRetailControl(text);
			}
		}
	}

	private void FocusRetailControl(string name)
	{
		Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
		{
			FrameworkElement? frameworkElement = FindVisualChildByName(this, name);
			frameworkElement?.Focus();
			if (frameworkElement is TextBox textBox)
			{
				textBox.SelectAll();
			}
		}));
	}

	private void RetailSubstitute_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (_viewModel.CurrentPage is RetailBillingViewModel { SelectedSubstitute: not null } retailBillingViewModel && retailBillingViewModel.AddFromSubstituteCommand.CanExecute(retailBillingViewModel.SelectedSubstitute))
		{
			retailBillingViewModel.AddFromSubstituteCommand.Execute(retailBillingViewModel.SelectedSubstitute);
		}
	}

	private static FrameworkElement? FindVisualChildByName(DependencyObject parent, string name)
	{
		if (!(parent is Visual) && !(parent is Visual3D))
		{
			return null;
		}
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, i);
			if (child is FrameworkElement frameworkElement && frameworkElement.Name == name)
			{
				return frameworkElement;
			}
			FrameworkElement frameworkElement2 = FindVisualChildByName(child, name);
			if (frameworkElement2 != null)
			{
				return frameworkElement2;
			}
		}
		return null;
	}

	private void SmtpPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
	{
		if (sender is PasswordBox { DataContext: SettingsPageViewModel dataContext } passwordBox)
		{
			dataContext.SmtpPassword = passwordBox.Password;
		}
	}

	private void SaveDocumentOutputSettings_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SettingsPageViewModel dataContext })
		{
			dataContext.SaveDocumentOutputSettingsCommand.Execute(null);
			(FindVisualChildByName(this, "SmtpPasswordBox") as PasswordBox)?.Clear();
		}
	}

	private void RegisterTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (e.Source is TabControl { DataContext: StatutoryRegistersPageViewModel dataContext } && sender is TabControl { SelectedValue: string selectedValue })
		{
			dataContext.SelectedRegisterType = selectedValue;
			dataContext.SearchCommand.Execute(null);
		}
	}

	private void InspectorPinBox_PasswordChanged(object sender, RoutedEventArgs e)
	{
		if (sender is PasswordBox { DataContext: SettingsPageViewModel dataContext } passwordBox)
		{
			dataContext.InspectorPinInput = passwordBox.Password;
		}
	}

	private void InspectorPinConfirmationBox_PasswordChanged(object sender, RoutedEventArgs e)
	{
		if (sender is PasswordBox { DataContext: SettingsPageViewModel dataContext } passwordBox)
		{
			dataContext.InspectorPinConfirmation = passwordBox.Password;
		}
	}

	private void SaveInspectorPin_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: SettingsPageViewModel dataContext })
		{
			dataContext.SaveDocumentOutputSettingsCommand.Execute(null);
			(FindVisualChildByName(this, "InspectorPinBox") as PasswordBox)?.Clear();
			(FindVisualChildByName(this, "InspectorPinConfirmationBox") as PasswordBox)?.Clear();
		}
	}

	private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
	{
		InputEventArgs input = e.StagingItem.Input;
		if (input != null && (input.RoutedEvent == Keyboard.KeyDownEvent || input.RoutedEvent == Mouse.MouseDownEvent || input.RoutedEvent == Mouse.MouseMoveEvent))
		{
			_lastInputUtc = DateTime.UtcNow;
		}
	}

	private async void OnIdleTimerTick(object? sender, EventArgs e)
	{
		TimeSpan timeSpan = TimeSpan.FromMinutes(Math.Clamp(_currentSession.User?.IdleLockMinutes ?? 10, 1, 240));
		if (!_showingLockScreen && !(DateTime.UtcNow - _lastInputUtc < timeSpan))
		{
			LockTerminal();
		}
	}

	private void OnLockRequested(object? sender, EventArgs e)
	{
		if (!_showingLockScreen)
		{
			LockTerminal();
		}
	}

	private void OpenPillMenu_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { ContextMenu: { } contextMenu } button)
		{
			contextMenu.PlacementTarget = button;
			contextMenu.Placement = PlacementMode.Bottom;
			contextMenu.IsOpen = true;
		}
	}

	private void LockTerminal()
	{
		_showingLockScreen = true;
		_currentSession.SignOut();
		try
		{
			using IServiceScope serviceScope = _scopeFactory.CreateScope();
			AuthenticationWindow requiredService = serviceScope.ServiceProvider.GetRequiredService<AuthenticationWindow>();
			requiredService.Owner = this;
			if (requiredService.ShowDialog() != true)
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

	private void ProfilePill_OnMouseEnter(object sender, MouseEventArgs e)
	{
		_profilePopupCloseTimer?.Stop();
		_viewModel.IsProfileStatusPopupOpen = true;
	}

	private void ProfilePill_OnMouseLeave(object sender, MouseEventArgs e)
	{
		ScheduleProfilePopupClose();
	}

	private void ProfilePopup_OnMouseEnter(object sender, MouseEventArgs e)
	{
		_profilePopupCloseTimer?.Stop();
		_viewModel.IsProfileStatusPopupOpen = true;
	}

	private void ProfilePopup_OnMouseLeave(object sender, MouseEventArgs e)
	{
		ScheduleProfilePopupClose();
	}

	private void ScheduleProfilePopupClose()
	{
		_profilePopupCloseTimer?.Stop();
		_profilePopupCloseTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(220L)
		};
		_profilePopupCloseTimer.Tick += (object? _, EventArgs _) =>
		{
			_profilePopupCloseTimer.Stop();
			_viewModel.IsProfileStatusPopupOpen = false;
		};
		_profilePopupCloseTimer.Start();
	}

	private void OpenAccessSimulator_Click(object sender, RoutedEventArgs e)
	{
	}

	private void SupportEmail_RequestNavigate(object sender, RequestNavigateEventArgs e)
	{
		Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
		{
			UseShellExecute = true
		});
		e.Handled = true;
	}

	private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
	{
		using IServiceScope serviceScope = _scopeFactory.CreateScope();
		DiagnosticsWindow requiredService = serviceScope.ServiceProvider.GetRequiredService<DiagnosticsWindow>();
		requiredService.Owner = this;
		requiredService.ShowDialog();
	}

	private void OpenCatalogImport_Click(object sender, RoutedEventArgs e)
	{
		using IServiceScope serviceScope = _scopeFactory.CreateScope();
		CatalogImportWindow requiredService = serviceScope.ServiceProvider.GetRequiredService<CatalogImportWindow>();
		requiredService.Owner = this;
		requiredService.ShowDialog();
	}

	private void ReportsGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
	{
		if (e.PropertyName == "__key")
		{
			e.Cancel = true;
			return;
		}
		if (e.Column is DataGridBoundColumn dataGridBoundColumn && e.PropertyName.IndexOfAny(new char[4] { '/', '.', '[', ']' }) >= 0)
		{
			dataGridBoundColumn.Binding = new Binding("[" + e.PropertyName + "]")
			{
				Mode = BindingMode.OneWay
			};
			dataGridBoundColumn.SortMemberPath = "[" + e.PropertyName + "]";
		}
		if (e.PropertyName == "Invoice" && sender is DataGrid { ItemsSource: DataView { Table: { } table } } dataGrid && table.Columns.Contains("__key"))
		{
			e.Column = new DataGridTemplateColumn
			{
				Header = "Invoice",
				SortMemberPath = "Invoice",
				CellTemplate = (DataTemplate)dataGrid.FindResource("InvoiceLinkTemplate")
			};
		}
	}

	private void OpenRecentBills_Click(object sender, RoutedEventArgs e)
	{
		using IServiceScope serviceScope = _scopeFactory.CreateScope();
		RecentBillsWindow requiredService = serviceScope.ServiceProvider.GetRequiredService<RecentBillsWindow>();
		requiredService.Owner = this;
		requiredService.ShowDialog();
	}

	private void PurchaseDocument_DragEnter(object sender, DragEventArgs e)
	{
		e.Effects = (e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None);
		e.Handled = true;
	}

	private async void PurchaseDocument_Drop(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] array && array.Length == 1)
		{
			string extension = Path.GetExtension(array[0]);
			if (!string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
			{
				MessageBox.Show("Drop a PDF, PNG or JPEG purchase document.", "Unsupported purchase document", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
			else if (_viewModel.CurrentPage is PurchasePageViewModel purchasePageViewModel)
			{
				await purchasePageViewModel.ImportDocumentFromPathAsync(array[0]);
			}
		}
	}
}

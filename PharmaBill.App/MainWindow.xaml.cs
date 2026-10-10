using System;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
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

public partial class MainWindow : Window
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

	private PurchasePageViewModel? _attachedPurchasePage;

	private WholesaleBillingPageViewModel? _attachedWholesalePage;

	private readonly IScannerService _scanner;

	public MainWindow(MainWindowViewModel viewModel, IServiceScopeFactory scopeFactory, CurrentSession currentSession, CatalogueService catalogueService, IScannerService scanner)
	{
		_viewModel = viewModel;
		_scopeFactory = scopeFactory;
		_currentSession = currentSession;
		_catalogueService = catalogueService;
		_scanner = scanner;
		_scanner.BarcodeReceived += OnSerialBarcode;
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
			// Ctrl+Shift+K is handled globally in App (admin key generator).
			ModifierKeys mods = Keyboard.Modifiers;
			if (e.Key == Key.B && (mods & ModifierKeys.Control) == ModifierKeys.Control && (mods & ModifierKeys.Shift) != ModifierKeys.Shift)
			{
				OpenBarcodeSimulator();
				e.Handled = true;
				return;
			}

			Key erpKey = e.Key == Key.System ? e.SystemKey : e.Key;
			bool erpCtrl = (mods & ModifierKeys.Control) == ModifierKeys.Control;
			bool erpShift = (mods & ModifierKeys.Shift) == ModifierKeys.Shift;

			// Shift+F12 — compliance calendar (plain F12 / Ctrl+K open calculator via InputBindings)
			if (erpKey == Key.F12 && erpShift && !erpCtrl)
			{
				new ComplianceCalendarWindow(_scopeFactory) { Owner = this }.Show();
				e.Handled = true;
				return;
			}

			// Ctrl+F3 — quick search / modify bill (distinct from Ctrl+3 patient focus)
			if (erpCtrl && erpKey == Key.F3)
			{
				_viewModel.NavigateCommand.Execute("Billing");
				if (_viewModel.CurrentPage is RetailBillingViewModel retail)
				{
					retail.ItemSearch = string.Empty;
					retail.StatusMessage = "Quick search / modify bill — type invoice no. or medicine (Ctrl+F3).";
				}

				e.Handled = true;
				return;
			}

			// Ctrl+I — quick item / salt stock lookup
			if (erpCtrl && erpKey == Key.I)
			{
				_viewModel.NavigateCommand.Execute("Stock");
				try
				{
					if (FindName("GlobalMedicineSearchBox") is TextBox searchBox)
					{
						searchBox.Focus();
						Keyboard.Focus(searchBox);
					}
					else
					{
						_viewModel.CatalogSearch.Query = string.Empty;
					}
				}
				catch
				{
					// Focus best-effort
				}

				e.Handled = true;
				return;
			}

			// Ctrl+L — supplier / customer ledger
			if (erpCtrl && erpKey == Key.L)
			{
				_viewModel.NavigateCommand.Execute("Accounts");
				e.Handled = true;
				return;
			}

			_barcodeListener.ProcessKeyDown(e);
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
		_viewModel.CalculatorRequested += OnCalculatorRequested;
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
		_scanner.BarcodeReceived -= OnSerialBarcode;
		_scanner.Stop();
		InputManager.Current.PreProcessInput -= OnPreProcessInput;
		_viewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
		_viewModel.LockRequested -= OnLockRequested;
		_viewModel.CalculatorRequested -= OnCalculatorRequested;
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

	private void OnSerialBarcode(object? sender, string barcode)
	{
		Dispatcher.InvokeAsync(() => _barcodeListener.SimulateScan(barcode));
	}

	private void TogglePurchaseHardware()
	{
		if (_scanner.IsListening)
		{
			_scanner.Stop();
			if (IsPurchasePage(out PurchasePageViewModel? purchase))
			{
				purchase.NotifyHardwareStatus(_scanner.Status);
			}

			return;
		}

		_scanner.Start();
		if (IsPurchasePage(out PurchasePageViewModel? purchasePage))
		{
			purchasePage.NotifyHardwareStatus(_scanner.Status);
			_ = purchasePage.ScanInvoiceCommand.ExecuteAsync(null);
		}
	}

	private bool IsPurchasePage(out PurchasePageViewModel? purchase)
	{
		purchase = _viewModel.CurrentPage as PurchasePageViewModel;
		return purchase != null;
	}

	private async void OnBarcodeScanned(string barcode)
	{
		_ = 1;
		try
		{
			SoundHelper.PlayTick();
			if (IsPurchasePage(out PurchasePageViewModel? purchasePage))
			{
				await purchasePage.ApplyScannedBarcodeAsync(barcode);
				return;
			}

			Gs1Scan scan = Gs1Scan.Parse(barcode);
			CatalogueBarcodeMatch? catalogueBarcodeMatch = null;
			foreach (string key in scan.LookupKeys())
			{
				catalogueBarcodeMatch = await _catalogueService.FindByBarcodeAsync(key);
				if (catalogueBarcodeMatch != null)
				{
					break;
				}
			}

			if (catalogueBarcodeMatch != null)
			{
				string value = string.IsNullOrWhiteSpace(catalogueBarcodeMatch.BrandName) ? catalogueBarcodeMatch.Name : catalogueBarcodeMatch.BrandName;
				string value2 = string.IsNullOrWhiteSpace(catalogueBarcodeMatch.Strength) ? "—" : catalogueBarcodeMatch.Strength;
				ShowItemQuickToast($"Scanned: {value} ({value2})");
				await _viewModel.HandleBarcodeScannedAsync(barcode, showLookupToast: false);
			}
			else
			{
				await _viewModel.HandleBarcodeScannedAsync(barcode);
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
			AttachPurchasePage(_viewModel.CurrentPage);
			AttachWholesalePage(_viewModel.CurrentPage);
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

	private void AttachPurchasePage(object? page)
	{
		if (_attachedPurchasePage != null)
		{
			_attachedPurchasePage.PropertyChanged -= OnPurchasePagePropertyChanged;
		}
		_attachedPurchasePage = page as PurchasePageViewModel;
		if (_attachedPurchasePage != null)
		{
			_attachedPurchasePage.PropertyChanged += OnPurchasePagePropertyChanged;
		}
	}

	private void AttachWholesalePage(object? page)
	{
		if (_attachedWholesalePage != null)
		{
			_attachedWholesalePage.PropertyChanged -= OnWholesalePagePropertyChanged;
		}
		_attachedWholesalePage = page as WholesaleBillingPageViewModel;
		if (_attachedWholesalePage != null)
		{
			_attachedWholesalePage.PropertyChanged += OnWholesalePagePropertyChanged;
			FocusRetailControl("WholesaleRetailerSearchBox");
		}
	}

	private void OnWholesalePagePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != "FocusRequest" || sender is not WholesaleBillingPageViewModel wholesale)
		{
			return;
		}

		string? controlName = wholesale.FocusRequest switch
		{
			"CustomerSearch" => "WholesaleRetailerSearchBox",
			"MedicineSearch" => "WholesaleMedicineSearchBox",
			"Payment" => "WholesalePaymentMethodBox",
			_ => null
		};
		if (controlName != null)
		{
			FocusRetailControl(controlName);
		}
	}

	private void OnPurchasePagePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != "FocusRequest" || sender is not PurchasePageViewModel purchase)
		{
			return;
		}

		string? controlName = purchase.FocusRequest switch
		{
			"NewItemMedicine" => "PurchaseMedicineSearchBox",
			"NewItemFormulation" => "PurchaseNewItemFormulation",
			"NewItemHsn" => "PurchaseNewItemHsn",
			"NewItemBatch" => "PurchaseNewItemBatch",
			"NewItemExpiry" => "PurchaseNewItemExpiry",
			"NewItemQuantity" => "PurchaseNewItemQuantity",
			"NewItemFree" => "PurchaseNewItemFree",
			"NewItemMrp" => "PurchaseNewItemMrp",
			"NewItemRate" => "PurchaseNewItemRate",
			"NewItemGst" => "PurchaseNewItemGst",
			"NewItemRack" => "PurchaseNewItemRack",
			_ => null
		};
		if (controlName != null)
		{
			FocusRetailControl(controlName);
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
			Keyboard.Focus(frameworkElement);
			if (frameworkElement is TextBox textBox)
			{
				textBox.SelectAll();
			}
			else if (frameworkElement is ComboBox { IsEditable: true } combo)
			{
				combo.Focus();
				if (combo.Template?.FindName("PART_EditableTextBox", combo) is TextBox editable)
				{
					editable.Focus();
					editable.SelectAll();
				}
			}
		}));
	}

	private void RetailSubstitute_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (e.OriginalSource is DependencyObject source)
		{
			DependencyObject? current = source;
			while (current != null)
			{
				if (current is Button)
				{
					return;
				}

				current = VisualTreeHelper.GetParent(current);
			}
		}

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

	private void OnCalculatorRequested(object? sender, EventArgs e)
	{
		try
		{
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("calc.exe")
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				this,
				"Could not open Windows Calculator.\n\n" + ex.Message,
				"PharmaBill",
				MessageBoxButton.OK,
				MessageBoxImage.Warning);
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
				string? stored = await purchasePageViewModel.AttachExistingDocumentAsync(array[0]);
				await purchasePageViewModel.ImportDocumentFromPathAsync(stored ?? array[0]);
			}
		}
	}

	private void PurchaseMedicineSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (_viewModel.CurrentPage is not PurchasePageViewModel purchase)
		{
			return;
		}

		if (e.Key == Key.Escape)
		{
			purchase.IsMedicineDropDownOpen = false;
			e.Handled = true;
			return;
		}

		if (e.Key == Key.Down)
		{
			purchase.OpenMedicineSuggestionsCommand.Execute(null);
			if (FindVisualChildByName(this, "PurchaseMedicineSuggestionList") is ListBox list && list.Items.Count > 0)
			{
				list.Focus();
				list.SelectedIndex = Math.Max(0, list.SelectedIndex);
				if (list.SelectedItem != null)
				{
					list.ScrollIntoView(list.SelectedItem);
				}
			}
			e.Handled = true;
			return;
		}

		if (e.Key == Key.Up && purchase.IsMedicineDropDownOpen)
		{
			if (FindVisualChildByName(this, "PurchaseMedicineSuggestionList") is ListBox upList && upList.Items.Count > 0)
			{
				upList.Focus();
				upList.SelectedIndex = upList.SelectedIndex <= 0 ? 0 : upList.SelectedIndex - 1;
				if (upList.SelectedItem != null)
				{
					upList.ScrollIntoView(upList.SelectedItem);
				}
			}
			e.Handled = true;
			return;
		}

		if (e.Key == Key.Enter)
		{
			if (purchase.IsMedicineDropDownOpen && purchase.MedicinePickerItems.Count > 0)
			{
				PurchaseMedicinePickerItem? pick = purchase.SelectedMedicineItem ?? purchase.MedicinePickerItems[0];
				purchase.SelectMedicineSuggestion(pick);
				e.Handled = true;
			}
		}
	}

	private void PurchaseMedicineSuggestionList_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (_viewModel.CurrentPage is not PurchasePageViewModel purchase || sender is not ListBox list)
		{
			return;
		}

		if (e.Key == Key.Escape)
		{
			purchase.IsMedicineDropDownOpen = false;
			FocusRetailControl("PurchaseMedicineSearchBox");
			e.Handled = true;
			return;
		}

		if (e.Key == Key.Enter && list.SelectedItem is PurchaseMedicinePickerItem item)
		{
			purchase.SelectMedicineSuggestion(item);
			e.Handled = true;
		}
	}

	private void WholesaleRetailerSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key != Key.Enter && e.Key != Key.Return)
		{
			return;
		}

		if (sender is ComboBox { IsDropDownOpen: true })
		{
			return;
		}

		e.Handled = true;
		FocusRetailControl("WholesaleMedicineSearchBox");
	}

	private void WholesaleMedicineSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (_viewModel.CurrentPage is not WholesaleBillingPageViewModel wholesale)
		{
			return;
		}

		if (e.Key == Key.Escape)
		{
			wholesale.IsMedicineDropDownOpen = false;
			e.Handled = true;
			return;
		}

		if (e.Key == Key.Down && wholesale.MedicinePickerItems.Count > 0)
		{
			wholesale.NoteExplicitMedicinePick();
			wholesale.IsMedicineDropDownOpen = true;
			int index = wholesale.SelectedMedicineItem == null ? -1 : wholesale.MedicinePickerItems.IndexOf(wholesale.SelectedMedicineItem);
			int next = index < 0 ? 0 : Math.Min(wholesale.MedicinePickerItems.Count - 1, index + 1);
			wholesale.SelectedMedicineItem = wholesale.MedicinePickerItems[next];
			e.Handled = true;
			return;
		}

		if (e.Key == Key.Up && wholesale.IsMedicineDropDownOpen && wholesale.MedicinePickerItems.Count > 0)
		{
			wholesale.NoteExplicitMedicinePick();
			int index = wholesale.SelectedMedicineItem == null ? 0 : wholesale.MedicinePickerItems.IndexOf(wholesale.SelectedMedicineItem);
			int next = index <= 0 ? 0 : index - 1;
			wholesale.SelectedMedicineItem = wholesale.MedicinePickerItems[next];
			e.Handled = true;
			return;
		}

		if (e.Key == Key.Enter || e.Key == Key.Return)
		{
			WholesaleMedicinePickerItem? chosen = wholesale.TakeEnterPick();
			if (chosen != null)
			{
				wholesale.CommitMedicinePick(chosen);
				e.Handled = true;
			}
		}
	}

	private void WholesaleMedicineItem_Click(object sender, MouseButtonEventArgs e)
	{
		if (_viewModel.CurrentPage is not WholesaleBillingPageViewModel wholesale)
		{
			return;
		}

		if (sender is FrameworkElement { DataContext: WholesaleMedicinePickerItem item })
		{
			wholesale.NoteExplicitMedicinePick();
			wholesale.CommitMedicinePick(item);
			e.Handled = true;
		}
	}

	private void RetailItemSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (_viewModel.CurrentPage is not RetailBillingViewModel retail)
		{
			return;
		}

		if (e.Key == Key.Down && retail.SearchResults.Count > 0)
		{
			retail.MoveSearchHighlight(1);
			ScrollRetailSuggestion(retail);
			e.Handled = true;
			return;
		}

		if (e.Key == Key.Up && retail.SearchResults.Count > 0)
		{
			retail.MoveSearchHighlight(-1);
			ScrollRetailSuggestion(retail);
			e.Handled = true;
			return;
		}

		if (e.Key != Key.Enter && e.Key != Key.Return)
		{
			return;
		}

		e.Handled = true;
		if (retail.SearchResults.Count > 0)
		{
			retail.CommitSearchHighlight();
			return;
		}

		if (retail.SearchItemsCommand.CanExecute(null))
		{
			_ = retail.SearchItemsCommand.ExecuteAsync(null);
		}
	}

	private void ScrollRetailSuggestion(RetailBillingViewModel retail)
	{
		if (FindVisualChildByName(this, "RetailMedicineSuggestionList") is ListBox list && retail.SelectedSearchResult != null)
		{
			list.ScrollIntoView(retail.SelectedSearchResult);
		}
	}

	private void InvoiceToolsButton_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { ContextMenu: { } menu } button)
		{
			menu.PlacementTarget = button;
			menu.IsOpen = true;
		}
	}

	private void PurchaseNewItemExpiry_LostFocus(object sender, RoutedEventArgs e)
	{
		if (_viewModel.CurrentPage is PurchasePageViewModel purchase)
		{
			purchase.NormalizeNewItemExpiry();
		}
	}

	private void RetailMedicineSuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (_viewModel.CurrentPage is RetailBillingViewModel retail)
		{
			retail.NoteExplicitBatchPick();
		}
	}

	private void PurchaseMedicineSuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (_viewModel.CurrentPage is not PurchasePageViewModel purchase)
		{
			return;
		}

		if (sender is ListBox list && list.SelectedItem is PurchaseMedicinePickerItem item)
		{
			purchase.SelectMedicineSuggestion(item);
			e.Handled = true;
		}
	}
}

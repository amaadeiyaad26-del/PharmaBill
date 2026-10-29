using PharmaBill.Core;
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

	private DateTime _lastInputUtc = BusinessClock.UtcNow;

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

	private bool IsPurchasePage(out PurchasePageViewModel? purchase)
	{
		purchase = _viewModel.CurrentPage as PurchasePageViewModel;
		return purchase != null;
	}

	private async void OnBarcodeScanned(string barcode)
	{
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
			_lastInputUtc = BusinessClock.UtcNow;
		}
	}

	private async void OnIdleTimerTick(object? sender, EventArgs e)
	{
		TimeSpan timeSpan = TimeSpan.FromMinutes(Math.Clamp(_currentSession.User?.IdleLockMinutes ?? 10, 1, 240));
		if (!_showingLockScreen && !(BusinessClock.UtcNow - _lastInputUtc < timeSpan))
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
				"Could not open Windows Calculator.\n\n" + PharmaBill.Core.UserMessage.Of(ex),
				"Pharma Remit",
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
			_lastInputUtc = BusinessClock.UtcNow;
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

	// Diagnostics and the medicine data import live in Settings > Data & Maintenance.
	private void OpenDiagnostics_Click(object sender, RoutedEventArgs e) => _viewModel.OpenMaintenance(MaintenanceTab.Diagnostics);

	private void OpenCatalogImport_Click(object sender, RoutedEventArgs e) => _viewModel.OpenMaintenance(MaintenanceTab.Import);

	// Columns that say which row it is: kept in view while the rest scroll sideways.
	private static readonly HashSet<string> ReportKeyColumns = new(StringComparer.OrdinalIgnoreCase)
	{
		"Date", "Date & Time", "Bill Date", "Invoice", "Invoice No", "Bill No", "Item Name"
	};

	/// <summary>
	/// Freezes the leading Date / Invoice / Item Name columns (as far as the last of them among the first four), or the
	/// first column when a report has none of them, so a wide report never scrolls its row labels away.
	/// </summary>
	private void ReportsGrid_AutoGeneratedColumns(object? sender, EventArgs e)
	{
		if (sender is not DataGrid grid)
		{
			return;
		}
		List<string> headers = grid.Columns.OrderBy(column => column.DisplayIndex).Select(column => Convert.ToString(column.Header) ?? string.Empty).ToList();
		int frozen = 1;
		for (int index = 0; index < Math.Min(4, headers.Count); index++)
		{
			if (ReportKeyColumns.Contains(headers[index]))
			{
				frozen = index + 1;
			}
		}
		grid.FrozenColumnCount = Math.Min(frozen, headers.Count);
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
		bool keyed = sender is DataGrid { ItemsSource: DataView { Table: { } table } } && table.Columns.Contains("__key");
		// The invoice number links to the Invoice details popup in every report whose rows carry an invoice key.
		if (e.PropertyName is "Invoice" or "Invoice No" && keyed)
		{
			e.Column = TemplateColumn(e.PropertyName, InvoiceLinkTemplate(e.PropertyName));
		}
		else if (e.PropertyName == "Supplier Bill" && keyed)
		{
			e.Column = TemplateColumn(e.PropertyName, SupplierBillLinkTemplate(e.PropertyName));
		}
		else if (e.PropertyName == "Payment Status")
		{
			e.Column = TemplateColumn(e.PropertyName, PaymentStatusTemplate(e.PropertyName));
		}
		else if (e.Column is DataGridTextColumn text)
		{
			// The default cell template ignores the cell style's padding, so the text carries its own spacing.
			bool amount = IsAmountColumn(e.PropertyName);
			text.ElementStyle = ReportCellStyle(amount);
			if (amount && sender is DataGrid grid && grid.TryFindResource(typeof(DataGridColumnHeader)) is Style header)
			{
				// The heading sits over its figures.
				text.HeaderStyle = new Style(typeof(DataGridColumnHeader), header) { Setters = { new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Right) } };
			}
		}
	}

	private static readonly string[] AmountWords = ["Total", "Subtotal", "GST", "Tax", "Taxable", "Amount", "Balance", "Due", "Rate", "Qty", "Paid", "Value", "Profit", "Margin", "Sales", "Cost", "MRP", "Price", "Stock"];

	// Money and quantity columns line up on the right, as on a ledger; codes and numbers that are labels (HSN, invoice) do not.
	private static bool IsAmountColumn(string header) =>
		!header.Contains("No", StringComparison.Ordinal) && !header.Contains("HSN", StringComparison.Ordinal) && !header.Contains("Date", StringComparison.Ordinal)
		&& AmountWords.Any(word => header.Split(' ', '/', '(', ')').Contains(word, StringComparer.OrdinalIgnoreCase));

	private static Style ReportCellStyle(bool alignRight)
	{
		var style = new Style(typeof(TextBlock));
		style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(10, 7, 10, 7)));
		style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
		style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
		if (alignRight)
		{
			style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right));
			style.Setters.Add(new Setter(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular));
		}
		return style;
	}

	private static DataGridTemplateColumn TemplateColumn(string column, DataTemplate template) => new DataGridTemplateColumn
	{
		Header = column,
		SortMemberPath = "[" + column + "]",
		CellTemplate = template
	};

	private static DataTemplate SupplierBillLinkTemplate(string column) => (DataTemplate)System.Windows.Markup.XamlReader.Parse($$$"""
		<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
		  <Grid Margin="6,4">
		    <TextBlock x:Name="None" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Text="—" Foreground="{DynamicResource SecondaryTextBrush}" VerticalAlignment="Center" Margin="4,0" />
		    <Button x:Name="Link" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Visibility="Collapsed" Cursor="Hand" Background="#00FFFFFF" BorderThickness="0" Padding="4,2" HorizontalAlignment="Left"
		            Foreground="{DynamicResource AccentBrush}" ToolTip="Open the supplier's bill attached to this purchase" AutomationProperties.Name="View supplier bill"
		            Command="{Binding DataContext.OpenSupplierBillCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}" CommandParameter="{Binding}">
		      <Button.Template>
		        <ControlTemplate TargetType="Button">
		          <StackPanel Orientation="Horizontal" Background="Transparent">
		            <TextBlock FontFamily="Segoe MDL2 Assets" Text="&#xE723;" FontSize="13" VerticalAlignment="Center" Margin="0,0,6,0" Foreground="{TemplateBinding Foreground}" />
		            <TextBlock Text="View bill" TextDecorations="Underline" FontWeight="SemiBold" VerticalAlignment="Center" Foreground="{TemplateBinding Foreground}" />
		          </StackPanel>
		        </ControlTemplate>
		      </Button.Template>
		    </Button>
		  </Grid>
		  <DataTemplate.Triggers>
		    <DataTrigger Binding="{Binding Path=[{{{column}}}]}" Value="Attached">
		      <Setter TargetName="Link" Property="Visibility" Value="Visible" />
		      <Setter TargetName="None" Property="Visibility" Value="Collapsed" />
		    </DataTrigger>
		  </DataTemplate.Triggers>
		</DataTemplate>
		""");

	// Paid, part paid and unpaid read at a glance: a small coloured badge rather than plain text.
	private static DataTemplate PaymentStatusTemplate(string column) => (DataTemplate)System.Windows.Markup.XamlReader.Parse($$$"""
		<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
		  <Border x:Name="Badge" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="8,4" Padding="10,2" CornerRadius="10" Background="#FFF1F5F9">
		    <TextBlock x:Name="Label" Text="{Binding Path=[{{{column}}}]}" FontSize="12" FontWeight="SemiBold" Foreground="#FF475569" />
		  </Border>
		  <DataTemplate.Triggers>
		    <DataTrigger Binding="{Binding Path=[{{{column}}}]}" Value="Paid">
		      <Setter TargetName="Badge" Property="Background" Value="#FFDCFCE7" />
		      <Setter TargetName="Label" Property="Foreground" Value="#FF166534" />
		    </DataTrigger>
		    <DataTrigger Binding="{Binding Path=[{{{column}}}]}" Value="Partially paid">
		      <Setter TargetName="Badge" Property="Background" Value="#FFFEF3C7" />
		      <Setter TargetName="Label" Property="Foreground" Value="#FF92400E" />
		    </DataTrigger>
		    <DataTrigger Binding="{Binding Path=[{{{column}}}]}" Value="Unpaid">
		      <Setter TargetName="Badge" Property="Background" Value="#FFFEE2E2" />
		      <Setter TargetName="Label" Property="Foreground" Value="#FFB42318" />
		    </DataTrigger>
		  </DataTemplate.Triggers>
		</DataTemplate>
		""");

	private static DataTemplate InvoiceLinkTemplate(string column) => (DataTemplate)System.Windows.Markup.XamlReader.Parse($$$"""
		<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
		  <Button Cursor="Hand" Background="#00FFFFFF" BorderThickness="0" Padding="6,0" Margin="4,7" Foreground="{DynamicResource AccentBrush}" HorizontalAlignment="Left" VerticalAlignment="Center"
		          ToolTip="Open full invoice details" AutomationProperties.Name="Open invoice"
		          Command="{Binding DataContext.OpenInvoiceCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}" CommandParameter="{Binding}"
		          Content="{Binding Path=[{{{column}}}]}">
		    <Button.Template>
		      <ControlTemplate TargetType="Button">
		        <TextBlock Text="{TemplateBinding Content}" TextDecorations="Underline" Foreground="{TemplateBinding Foreground}" FontWeight="SemiBold" />
		      </ControlTemplate>
		    </Button.Template>
		  </Button>
		</DataTemplate>
		""");

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
		if (PageOf<PurchasePageViewModel>(sender) is not { } purchase)
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
			if (PurchaseSuggestionList() is ListBox list && list.Items.Count > 0)
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
			if (PurchaseSuggestionList() is ListBox upList && upList.Items.Count > 0)
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

	// Handlers on controls inside a page's template take that page from the control's own DataContext, not from the
	// window's current page: the control always knows which page it belongs to.
	private static T? PageOf<T>(object sender) where T : class => (sender as FrameworkElement)?.DataContext as T;

	// A dropdown item's DataContext is the item; its page is the owning ComboBox's.
	private static WholesaleBillingPageViewModel? WholesalePageOfItem(object sender) =>
		sender is DependencyObject item ? ItemsControl.ItemsControlFromItemContainer(item)?.DataContext as WholesaleBillingPageViewModel : null;

	// The suggestion list lives in a popup, outside the window's visual tree: reach it through the popup.
	private ListBox? PurchaseSuggestionList() =>
		FindVisualChildByName(this, "PurchaseMedicineFlyout") is Popup { Child: { } flyout }
			? LogicalTreeHelper.FindLogicalNode(flyout, "PurchaseMedicineSuggestionList") as ListBox
			: null;

	private void PurchaseMedicineSuggestionList_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (PageOf<PurchasePageViewModel>(sender) is not { } purchase || sender is not ListBox list)
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
		if (PageOf<WholesaleBillingPageViewModel>(sender) is not { } wholesale)
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
		if (WholesalePageOfItem(sender) is not { } wholesale)
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

	private void NavSearchBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			_viewModel.NavFilter = string.Empty;
			e.Handled = true;
			return;
		}

		if (e.Key is Key.Enter or Key.Return && !string.IsNullOrWhiteSpace(_viewModel.NavFilter))
		{
			string typed = _viewModel.NavFilter.Trim();
			_viewModel.NavFilter = string.Empty;
			if (_viewModel.NavigationItemsView.Cast<NavigationItem>().FirstOrDefault() is { } first)
			{
				_viewModel.NavigateCommand.Execute(first.SectionKey);
			}
			else
			{
				OpenSidebarToolMatch(typed, e);
				// A dialog hands focus back to whatever was focused before it, which can be a sidebar button.
				// Return it to the search box so the next Enter doesn't activate that button.
				Dispatcher.BeginInvoke(() => NavSearchBox.Focus(), DispatcherPriority.Input);
			}
			e.Handled = true;
		}
	}

	/// <summary>The sidebar tools at the bottom are not menu items, so search opens them by name.</summary>
	private void OpenSidebarToolMatch(string text, RoutedEventArgs e)
	{
		if ("settings".Contains(text, StringComparison.OrdinalIgnoreCase))
		{
			_viewModel.NavigateCommand.Execute("Settings");
		}
		else if ("diagnostics".Contains(text, StringComparison.OrdinalIgnoreCase))
		{
			OpenDiagnostics_Click(this, e);
		}
		else if ("import medicine data".Contains(text, StringComparison.OrdinalIgnoreCase))
		{
			OpenCatalogImport_Click(this, e);
		}
	}

	private void RetailItemSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (PageOf<RetailBillingViewModel>(sender) is not { } retail)
		{
			return;
		}

		if (e.Key == Key.Down && retail.SearchResults.Count > 0)
		{
			retail.ReopenSearchFlyoutCommand.Execute(null);
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
		// The list lives in the search flyout, a popup outside the window's visual tree.
		if (FindVisualChildByName(this, "RetailSearchFlyout") is Popup { Child: { } flyout }
			&& FindVisualChildByName(flyout, "RetailMedicineSuggestionList") is ListBox list && retail.SelectedSearchResult != null)
		{
			list.ScrollIntoView(retail.SelectedSearchResult);
		}
	}


	private void PurchaseNewItemExpiry_LostFocus(object sender, RoutedEventArgs e)
	{
		// The page this box belongs to.
		if ((sender as FrameworkElement)?.DataContext is PurchasePageViewModel purchase)
		{
			purchase.NormalizeNewItemExpiry();
		}
	}

	private void RetailItemSearchBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		// Back in the search box after the flyout was closed: show the results again.
		if ((sender as FrameworkElement)?.DataContext is RetailBillingViewModel retail)
		{
			retail.ReopenSearchFlyoutCommand.Execute(null);
		}
	}

	private void RetailMedicineSuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (PageOf<RetailBillingViewModel>(sender) is { } retail)
		{
			retail.NoteExplicitBatchPick();
		}
	}

	private void PurchaseMedicineSuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (PageOf<PurchasePageViewModel>(sender) is not { } purchase)
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

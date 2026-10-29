using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using Windows.Devices.Enumeration;

namespace PharmaBill.App.ViewModels;

/// <summary>One button in the invoice tools panel: what it does, its shortcut, and whether it can run now (and if not, why).</summary>
public sealed class InvoiceTool(string icon, string label, string shortLabel, string shortcut, string description, string automationName, ICommand command, Func<bool> canRun, Func<string> reason) : ObservableObject
{
	public string Icon { get; } = icon;
	public string Label { get; } = label;
	public string ShortLabel { get; } = shortLabel;
	public string Shortcut { get; } = shortcut;
	public string AutomationName { get; } = automationName;
	public ICommand Command { get; } = command;
	public bool CanRun => canRun();
	public string Reason => reason();
	public bool HasReason => Reason.Length > 0;

	/// <summary>Hover text: what it does and its shortcut, or why it cannot run.</summary>
	public string ToolTip => HasReason ? $"{Label}: {Reason}" : $"{description} ({Shortcut})";

	public void Refresh()
	{
		OnPropertyChanged(nameof(CanRun));
		OnPropertyChanged(nameof(Reason));
		OnPropertyChanged(nameof(HasReason));
		OnPropertyChanged(nameof(ToolTip));
	}
}

public sealed record InvoiceToolGroup(string Heading, IReadOnlyList<InvoiceTool> Tools);

// The invoice tools panel down the right of the Purchases screen: capture the bill, import, history. Each button says
// up front when it cannot run (an invoice is being read, no scanner, no camera) instead of failing after the click.
// The panel sits beside the purchase lines (the title and add-item rows keep the full width) and narrows to an icon
// strip on a small screen, or when the user collapses it, so the purchase grid keeps its width. The keyboard shortcuts are the screen's own and work exactly as before, whatever the panel shows.
public sealed partial class PurchasePageViewModel
{
	/// <summary>Below this width of the Purchases screen the panel starts as an icon strip.</summary>
	public const double ToolsPanelCompactBelow = 1150;

	private double _screenWidth;
	private bool? _toolsPanelChoice;
	private bool? _hasHandheldScanner;
	private bool? _hasCamera;
	private RelayCommand? _toggleToolsPanelCommand;
	private AsyncRelayCommand? _checkDevicesCommand;

	/// <summary>Whether a WIA (handheld / USB) scanner is connected. Tests replace it.</summary>
	internal Func<bool> ScannerProbe { get; set; } = () => new HardwareScannerService().HasWiaScanner();

	/// <summary>Whether a camera is connected. Tests replace it.</summary>
	internal Func<Task<bool>> CameraProbe { get; set; } = async () => (await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)).Count > 0;

	/// <summary>The Purchases screen's width, reported by the view as it changes.</summary>
	public double ScreenWidth
	{
		get => _screenWidth;
		set
		{
			if (SetProperty(ref _screenWidth, value))
			{
				NotifyToolsPanelLayout();
			}
		}
	}

	/// <summary>An icon strip: the user collapsed it, or (until they choose) the screen is narrow.</summary>
	public bool IsToolsPanelCompact => _toolsPanelChoice ?? (ScreenWidth > 0 && ScreenWidth < ToolsPanelCompactBelow);

	public bool IsToolsPanelExpanded => !IsToolsPanelCompact;

	public string ToolsPanelToggleTip => IsToolsPanelCompact ? "Show the invoice tools with their names" : "Narrow the invoice tools to icons to give the purchase grid more room";

	public IRelayCommand ToggleToolsPanelCommand => _toggleToolsPanelCommand ??= new RelayCommand(() =>
	{
		_toolsPanelChoice = !IsToolsPanelCompact;
		NotifyToolsPanelLayout();
	});

	/// <summary>The device check started by the last page load, for tests to wait on.</summary>
	internal Task DeviceCheck { get; private set; } = Task.CompletedTask;

	/// <summary>Looks for the scanner and camera again (after plugging one in).</summary>
	public IAsyncRelayCommand CheckDevicesCommand => _checkDevicesCommand ??= new AsyncRelayCommand(CheckDevicesAsync);

	public bool CanCaptureOrImport => !IsImporting;
	public bool CanUseHandheldScanner => !IsImporting && _hasHandheldScanner != false;
	public bool CanUseWebcam => !IsImporting && _hasCamera != false;

	private const string BusyReason = "An invoice is being read. Wait for it to finish.";

	public string CaptureReason => IsImporting ? BusyReason : string.Empty;

	public string ScannerReason => IsImporting ? BusyReason
		: _hasHandheldScanner == false ? "No handheld or USB scanner is connected. Plug one in, then Check devices." : string.Empty;

	public string WebcamReason => IsImporting ? BusyReason
		: _hasCamera == false ? "No camera found. Connect a USB camera or allow camera access, then Check devices." : string.Empty;

	public bool HasCaptureReason => CaptureReason.Length > 0;
	public bool HasScannerReason => ScannerReason.Length > 0;
	public bool HasWebcamReason => WebcamReason.Length > 0;
	public bool HasMissingDevice => _hasHandheldScanner == false || _hasCamera == false;

	private async Task CheckDevicesAsync()
	{
		try
		{
			// WIA is COM: asked on this (UI) thread, where the scanner dialog itself runs.
			_hasHandheldScanner = ScannerProbe();
		}
		catch (Exception)
		{
			_hasHandheldScanner = null; // Unknown: the button stays available and reports at the click, as before.
		}
		try
		{
			_hasCamera = await CameraProbe();
		}
		catch (Exception)
		{
			_hasCamera = null;
		}
		NotifyToolStates();
	}

	private IReadOnlyList<InvoiceToolGroup>? _toolGroups;

	/// <summary>The panel's groups: capture the bill, import, history.</summary>
	public IReadOnlyList<InvoiceToolGroup> ToolGroups => _toolGroups ??=
	[
		new InvoiceToolGroup("Capture the bill",
		[
			new InvoiceTool("📄", "Extract from Invoice (PDF/Image)", "Extract", "Ctrl+Shift+E", "Read distributor invoice PDFs or images into the purchase table",
				"Extract from invoice", ExtractInvoicesCommand, () => CanCaptureOrImport, () => CaptureReason),
			new InvoiceTool("📷", "Scan with Webcam", "Webcam", "F7", "Capture the supplier invoice with a USB document camera or webcam",
				"Scan with webcam", ScanInvoiceCommand, () => CanUseWebcam, () => WebcamReason),
			new InvoiceTool("🖨", "Handheld / USB Scanner", "Scanner", "Ctrl+Alt+S", "Scan the purchase invoice with a handheld or USB scanner",
				"Scan purchase invoice with handheld scanner", ScanPurchaseWithHandheldScannerCommand, () => CanUseHandheldScanner, () => ScannerReason),
			new InvoiceTool("📱", "Scan via Phone App", "Phone", "Ctrl+Shift+P", "Pair an Android phone on this Wi-Fi and receive scanned bills live",
				"Scan via phone app", ScanViaPhoneCommand, () => CanCaptureOrImport, () => CaptureReason),
		]),
		new InvoiceToolGroup("Import",
		[
			new InvoiceTool("📁", "Load CSV / Excel", "CSV", "Ctrl+Shift+L", "Import purchase items and batches from a distributor Excel or CSV file",
				"Load CSV or Excel", LoadSpreadsheetCommand, () => CanCaptureOrImport, () => CaptureReason),
			new InvoiceTool("📎", "Attach file", "Attach", "Ctrl+Shift+A", "Attach an invoice photo or PDF already saved on this PC",
				"Attach invoice file", AttachInvoiceFileCommand, () => CanCaptureOrImport, () => CaptureReason),
		]),
		new InvoiceToolGroup("History",
		[
			new InvoiceTool("📂", "Invoices History", "History", "Ctrl+Shift+H", "Browse saved purchase bills by supplier and date, and open the attached invoice",
				"Invoices history", ShowInvoiceHistoryCommand, () => true, () => string.Empty),
		]),
	];

	private void NotifyToolStates()
	{
		foreach (InvoiceTool tool in _toolGroups?.SelectMany(group => group.Tools) ?? [])
		{
			tool.Refresh();
		}
		foreach (string name in new[]
		{
			nameof(CanCaptureOrImport), nameof(CanUseHandheldScanner), nameof(CanUseWebcam), nameof(CaptureReason), nameof(ScannerReason),
			nameof(WebcamReason), nameof(HasCaptureReason), nameof(HasScannerReason), nameof(HasWebcamReason), nameof(HasMissingDevice),
		})
		{
			OnPropertyChanged(name);
		}
	}

	private void NotifyToolsPanelLayout()
	{
		OnPropertyChanged(nameof(IsToolsPanelCompact));
		OnPropertyChanged(nameof(IsToolsPanelExpanded));
		OnPropertyChanged(nameof(ToolsPanelToggleTip));
	}
}

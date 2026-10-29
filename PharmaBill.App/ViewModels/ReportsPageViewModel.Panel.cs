using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

/// <summary>One report in the panel: its icon, its names, its group's colour, and whether it is the one shown.</summary>
public sealed class ReportPanelItem(ReportChoice choice, string glyph, string shortLabel, Brush accent) : ObservableObject
{
	private bool _isSelected;

	public ReportChoice Choice { get; } = choice;
	public string Name => Choice.Name;
	public string Glyph { get; } = glyph;
	public string ShortLabel { get; } = shortLabel;
	public Brush Accent { get; } = accent;

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}
}

public sealed record ReportPanelGroup(string Heading, Brush Accent, IReadOnlyList<ReportPanelItem> Items);

// The report types down the right of Reports & Daily MIS, grouped, every one a button. Choosing one runs it at once for
// the period, branch and filter already set (as choosing it from the old dropdown did). The panel narrows to an icon
// strip on a small window, or when the user narrows it, so the results grid keeps its width.
public partial class ReportsPageViewModel
{
	/// <summary>Below this width of the Reports screen the panel starts as an icon strip.</summary>
	public const double ReportPanelCompactBelow = 1150;

	private double _screenWidth;
	private bool? _reportPanelChoice;
	private RelayCommand? _toggleReportPanelCommand;
	private RelayCommand<ReportPanelItem>? _selectReportCommand;
	private IReadOnlyList<ReportPanelGroup>? _reportGroups;

	private static Brush Tone(string hex)
	{
		var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
		brush.Freeze();
		return brush;
	}

	/// <summary>Every report type, grouped. Each entry of <see cref="ReportChoices"/> appears exactly once.</summary>
	public IReadOnlyList<ReportPanelGroup> ReportGroups => _reportGroups ??= BuildReportGroups();

	private IReadOnlyList<ReportPanelGroup> BuildReportGroups()
	{
		ReportChoice Find(ReportKind kind) => ReportChoices.First(choice => choice.Kind == kind);
		ReportPanelGroup Group(string heading, string hex, params (ReportKind Kind, string Glyph, string Short)[] items)
		{
			Brush accent = Tone(hex);
			return new ReportPanelGroup(heading, accent, items.Select(item => new ReportPanelItem(Find(item.Kind), item.Glyph, item.Short, accent)
			{
				IsSelected = SelectedReport?.Kind == item.Kind
			}).ToList());
		}

		return
		[
			Group("Sales", "#FF0F766E",
				(ReportKind.Sales, "", "Summary"),
				(ReportKind.CustomerPartyWise, "", "Parties"),
				(ReportKind.SaleBookDaily, "", "Daily"),
				(ReportKind.SaleBookMonthly, "", "Monthly"),
				(ReportKind.TopSellingDrugs, "", "Top"),
				(ReportKind.SaleReturnBook, "", "Returns")),
			Group("Purchases & suppliers", "#FF1D4ED8",
				(ReportKind.PurchaserSupplierWise, "", "Suppliers"),
				(ReportKind.Purchases, "", "Invoices"),
				(ReportKind.PurchaseReturnBook, "", "Returns")),
			Group("Stock & product", "#FF6D28D9",
				(ReportKind.BatchWise, "", "Batch"),
				(ReportKind.BrandProductWise, "", "Brand"),
				(ReportKind.ManufacturerWise, "", "Maker"),
				(ReportKind.AbcAnalysis, "", "ABC"),
				(ReportKind.Expiry, "", "Expiry"),
				(ReportKind.LowStock, "", "Low")),
			Group("Profit & accounts", "#FFB45309",
				(ReportKind.Profit, "", "Profit"),
				(ReportKind.DailyMis, "", "MIS"),
				(ReportKind.DayBook, "", "Day book"),
				(ReportKind.PartyBalances, "", "Balances")),
			Group("Compliance & people", "#FFBE123C",
				(ReportKind.GstSummary, "", "GST"),
				(ReportKind.PrescriberWise, "", "Doctors")),
		];
	}

	/// <summary>Shows that report, run now for the chosen period.</summary>
	public IRelayCommand<ReportPanelItem> SelectReportCommand => _selectReportCommand ??= new RelayCommand<ReportPanelItem>(item =>
	{
		if (item != null && SelectedReport != item.Choice)
		{
			SelectedReport = item.Choice;
		}
	});

	private void MarkSelectedReport(ReportChoice? selected)
	{
		if (_reportGroups == null)
		{
			return;
		}
		foreach (ReportPanelItem item in _reportGroups.SelectMany(group => group.Items))
		{
			item.IsSelected = item.Choice == selected;
		}
	}

	/// <summary>The Reports screen's width, reported by the view as it changes.</summary>
	public double ScreenWidth
	{
		get => _screenWidth;
		set
		{
			if (SetProperty(ref _screenWidth, value))
			{
				NotifyReportPanelLayout();
			}
		}
	}

	/// <summary>An icon strip: the user narrowed it, or (until they choose) the screen is small.</summary>
	public bool IsReportPanelCompact => _reportPanelChoice ?? (ScreenWidth > 0 && ScreenWidth < ReportPanelCompactBelow);

	public bool IsReportPanelExpanded => !IsReportPanelCompact;

	public string ReportPanelToggleTip => IsReportPanelCompact ? "Show the report names" : "Narrow the report list to icons to give the results more room";

	public IRelayCommand ToggleReportPanelCommand => _toggleReportPanelCommand ??= new RelayCommand(() =>
	{
		_reportPanelChoice = !IsReportPanelCompact;
		NotifyReportPanelLayout();
	});

	private void NotifyReportPanelLayout()
	{
		OnPropertyChanged(nameof(IsReportPanelCompact));
		OnPropertyChanged(nameof(IsReportPanelExpanded));
		OnPropertyChanged(nameof(ReportPanelToggleTip));
	}
}

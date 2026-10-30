using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

/// <summary>One report in the panel: its icon, its names, its group's colour, and whether it is the one shown.</summary>
public sealed class ReportPanelItem(ReportChoice choice, string glyph, string shortLabel, Brush accent, Brush tint) : ObservableObject
{
	/// <summary>The group's colour, faint: the icon's badge and the selected card's fill.</summary>
	public Brush Tint { get; } = tint;

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

public sealed record ReportPanelGroup(string Heading, Brush Accent, IReadOnlyList<ReportPanelItem> Items)
{
	/// <summary>The heading as set in the panel: small capitals.</summary>
	public string Caps => Heading.ToUpperInvariant();
}

/// <summary>A quick period above the report ("This month"), and whether it is the one in use.</summary>
public sealed class PeriodChip(string key, string label) : ObservableObject
{
	private bool _isSelected;

	public string Key { get; } = key;
	public string Label { get; } = label;

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}
}

// The report types down the right of Reports & Daily MIS, grouped, every one a button. Choosing one runs it at once for
// the period, branch and filter already set (as choosing it from the old dropdown did). The panel is always open at its
// full width: on a narrow window the results grid scrolls instead.
public partial class ReportsPageViewModel
{
	private RelayCommand<ReportPanelItem>? _selectReportCommand;
	private IReadOnlyList<ReportPanelGroup>? _reportGroups;

	private static Brush Tone(string hex, byte alpha = 0xFF)
	{
		Color color = (Color)ColorConverter.ConvertFromString(hex);
		color.A = alpha;
		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	public const string CustomPeriod = "Custom";

	/// <summary>The quick periods on one row; Custom opens the date range beside them.</summary>
	public IReadOnlyList<PeriodChip> PeriodChips { get; } =
	[
		new("Today", "Today"),
		new("This month", "This month"),
		new("Last month", "Last month"),
		new("Last 3 months", "Last 3 months"),
		new("This year", "This year"),
		new("Active window", "Active window"),
		new(CustomPeriod, "Custom"),
	];

	/// <summary>The From / To dates and Apply show beside the chips while Custom is chosen.</summary>
	public bool IsCustomPeriod => SelectedQuickPeriod == CustomPeriod;

	private void MarkSelectedPeriod()
	{
		foreach (PeriodChip chip in PeriodChips)
		{
			chip.IsSelected = chip.Key == SelectedQuickPeriod;
		}
		OnPropertyChanged(nameof(IsCustomPeriod));
	}

	private string _summaryReport = string.Empty;
	private string _summaryRange = string.Empty;
	private string _summaryRows = string.Empty;
	private string _summaryBranch = string.Empty;

	/// <summary>The summary strip over the results: the report, its dates, how many rows, and the branch.</summary>
	public string SummaryReport { get => _summaryReport; private set => SetProperty(ref _summaryReport, value); }
	public string SummaryRange { get => _summaryRange; private set => SetProperty(ref _summaryRange, value); }
	public string SummaryRows { get => _summaryRows; private set => SetProperty(ref _summaryRows, value); }
	public string SummaryBranch { get => _summaryBranch; private set => SetProperty(ref _summaryBranch, value); }

	private void SetSummary(DateOnly from, DateOnly to, DateOnly reportTo, bool lookAhead, ReportTable table)
	{
		static string Day(DateOnly date) => date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
		SummaryReport = SelectedReport.Name;
		SummaryRange = (from == to ? Day(from) : Day(from) + " – " + Day(to)) + (lookAhead ? " · expiring up to " + Day(reportTo) : string.Empty);
		SummaryRows = table.Rows.Count == 1 ? "1 row" : table.Rows.Count.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("en-IN")) + " rows";
		SummaryBranch = SelectedBranchFilter.DisplayName;
	}

	/// <summary>Every report type, grouped. Each entry of <see cref="ReportChoices"/> appears exactly once.</summary>
	public IReadOnlyList<ReportPanelGroup> ReportGroups => _reportGroups ??= BuildReportGroups();

	private IReadOnlyList<ReportPanelGroup> BuildReportGroups()
	{
		ReportChoice Find(ReportKind kind) => ReportChoices.First(choice => choice.Kind == kind);
		ReportPanelGroup Group(string heading, string hex, params (ReportKind Kind, string Glyph, string Short)[] items)
		{
			Brush accent = Tone(hex);
			Brush tint = Tone(hex, 0x1F);
			return new ReportPanelGroup(heading, accent, items.Select(item => new ReportPanelItem(Find(item.Kind), item.Glyph, item.Short, accent, tint)
			{
				IsSelected = SelectedReport?.Kind == item.Kind
			}).ToList());
		}

		return
		[
			Group("Sales", "#FF0E7C6B",
				(ReportKind.Sales, "", "Summary"),
				(ReportKind.CustomerPartyWise, "", "Parties"),
				(ReportKind.SaleBookDaily, "", "Daily"),
				(ReportKind.SaleBookMonthly, "", "Monthly"),
				(ReportKind.TopSellingDrugs, "", "Top"),
				(ReportKind.SaleReturnBook, "", "Returns")),
			Group("Purchases & suppliers", "#FF2F5DA8",
				(ReportKind.PurchaserSupplierWise, "", "Suppliers"),
				(ReportKind.Purchases, "", "Invoices"),
				(ReportKind.PurchaseReturnBook, "", "Returns"),
				(ReportKind.ExpiryClaimsAgeing, "", "Expiry ageing"),
				(ReportKind.ExpiryClaimRecovery, "", "Recovery")),
			Group("Stock & product", "#FF6B4FA0",
				(ReportKind.BatchWise, "", "Batch"),
				(ReportKind.BrandProductWise, "", "Brand"),
				(ReportKind.ManufacturerWise, "", "Maker"),
				(ReportKind.AbcAnalysis, "", "ABC"),
				(ReportKind.Expiry, "", "Expiry"),
				(ReportKind.LowStock, "", "Low")),
			Group("Profit & accounts", "#FFA0621B",
				(ReportKind.Profit, "", "Profit"),
				(ReportKind.DailyMis, "", "MIS"),
				(ReportKind.DayBook, "", "Day book"),
				(ReportKind.PartyBalances, "", "Balances")),
			Group("Compliance & people", "#FFA8445A",
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

}

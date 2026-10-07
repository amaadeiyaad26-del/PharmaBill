using System;
using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record DashboardAnalytics(IReadOnlyList<ChartBar> DailySales, IReadOnlyList<ChartBar> HourlyBills, IReadOnlyList<TopMedicine> TopMedicines, IReadOnlyList<CategoryShare> Categories)
{
	public static DashboardAnalytics Empty { get; } = new DashboardAnalytics(Array.Empty<ChartBar>(), Array.Empty<ChartBar>(), Array.Empty<TopMedicine>(), Array.Empty<CategoryShare>());
}

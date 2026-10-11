using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App;

public partial class ComplianceCalendarWindow : Window
{
	public sealed record ComplianceEvent(DateOnly DueDate, string Category, string Title, string Status);

	public ComplianceCalendarWindow(IServiceScopeFactory scopeFactory)
	{
		InitializeComponent();
		Loaded += async (_, _) => await LoadAsync(scopeFactory);
	}

	private async Task LoadAsync(IServiceScopeFactory scopeFactory)
	{
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		List<ComplianceEvent> events = new();

		using (IServiceScope scope = scopeFactory.CreateScope())
		{
			PharmaBillDbContext db = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			foreach (LicenceRecord licence in await db.LicenceRecords.AsNoTracking().ToListAsync())
			{
				if (!licence.ExpiresOn.HasValue)
				{
					continue;
				}

				string status = licence.ExpiresOn.Value < today
					? "Expired"
					: licence.ExpiresOn.Value <= today.AddDays(60) ? "Renew soon" : "Valid";
				events.Add(new ComplianceEvent(
					licence.ExpiresOn.Value,
					"Drug licence",
					$"Form {licence.LicenceType} ({licence.LicenceNumber}) renewal",
					status));
			}
		}

		for (int offset = 0; offset < 6; offset++)
		{
			DateTime month = DateTime.Today.AddMonths(offset);
			DateOnly periodStart = new DateOnly(month.Year, month.Month, 1);
			DateOnly periodEnd = periodStart.AddMonths(1).AddDays(-1);
			DateOnly gstr1Due = periodStart.AddMonths(1).AddDays(10);
			DateOnly gstr3bDue = periodStart.AddMonths(1).AddDays(19);
			string periodLabel = periodEnd.ToString("MMM yyyy");
			events.Add(new ComplianceEvent(gstr1Due, "GSTR-1", $"File GSTR-1 for {periodLabel}", StatusFor(gstr1Due, today)));
			events.Add(new ComplianceEvent(gstr3bDue, "GSTR-3B", $"File GSTR-3B for {periodLabel}", StatusFor(gstr3bDue, today)));
		}

		EventsGrid.ItemsSource = events.OrderBy(e => e.DueDate).ThenBy(e => e.Category).ToList();
	}

	private static string StatusFor(DateOnly due, DateOnly today)
		=> due < today ? "Overdue" : due <= today.AddDays(15) ? "Due soon" : "Upcoming";
}

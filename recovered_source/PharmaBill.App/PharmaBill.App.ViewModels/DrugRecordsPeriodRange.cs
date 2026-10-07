using System;

namespace PharmaBill.App.ViewModels;

public static class DrugRecordsPeriodRange
{
	public static (DateTime StartUtc, DateTime EndUtc) GetRange(DrugRecordsPeriod period, DateTime localNow, int selectedMonth, int selectedYear, DateOnly? customStart = null, DateOnly? customEndInclusive = null)
	{
		DateOnly dateOnly = DateOnly.FromDateTime(localNow);
		DateOnly dateOnly2 = new DateOnly(selectedYear, selectedMonth, 1);
		DateOnly dateOnly3 = period switch
		{
			DrugRecordsPeriod.Today => dateOnly, 
			DrugRecordsPeriod.ThisMonth => dateOnly2, 
			DrugRecordsPeriod.LastMonth => dateOnly2.AddMonths(-1), 
			DrugRecordsPeriod.LastThreeMonths => dateOnly2.AddMonths(-2), 
			DrugRecordsPeriod.ThisYear => new DateOnly(selectedYear, 1, 1), 
			DrugRecordsPeriod.Custom => customStart ?? throw new ArgumentException("Choose a custom start date.", "customStart"), 
			_ => throw new ArgumentOutOfRangeException("period"), 
		};
		DateOnly dateOnly4 = period switch
		{
			DrugRecordsPeriod.Today => dateOnly.AddDays(1), 
			DrugRecordsPeriod.ThisMonth => dateOnly3.AddMonths(1), 
			DrugRecordsPeriod.LastMonth => dateOnly3.AddMonths(1), 
			DrugRecordsPeriod.LastThreeMonths => dateOnly2.AddMonths(1), 
			DrugRecordsPeriod.ThisYear => dateOnly3.AddYears(1), 
			DrugRecordsPeriod.Custom => (customEndInclusive ?? throw new ArgumentException("Choose a custom end date.", "customEndInclusive")).AddDays(1), 
			_ => throw new ArgumentOutOfRangeException("period"), 
		};
		if (period == DrugRecordsPeriod.Custom && dateOnly4 <= dateOnly3)
		{
			throw new ArgumentException("The custom end date must not be earlier than the start date.");
		}
		return (StartUtc: dateOnly3.ToDateTime(TimeOnly.MinValue).ToUniversalTime(), EndUtc: dateOnly4.ToDateTime(TimeOnly.MinValue).ToUniversalTime());
	}
}

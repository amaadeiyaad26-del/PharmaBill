using System;

namespace PharmaBill.Core;

public static class StatutoryRetentionPolicy
{
	public const int UnverifiedRetentionYears = 0;

	public const int RecommendedRetentionYears = 5;

	public const int RecommendedRetentionDays = 1825;

	public const int ExtendedRetentionYears = 7;

	public static DateTime GetCutoffUtc(DataRetentionPeriod period, DateTime? utcNow = null)
	{
		DateTime dateTime = utcNow ?? DateTime.UtcNow;
		return period switch
		{
			DataRetentionPeriod.FiveYears => dateTime.AddDays(-1825.0), 
			DataRetentionPeriod.SevenYears => dateTime.AddYears(-7), 
			DataRetentionPeriod.Permanent => DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc), 
			_ => dateTime.AddDays(-1825.0), 
		};
	}

	public static int GetRetentionYears(DataRetentionPeriod period)
	{
		return period switch
		{
			DataRetentionPeriod.FiveYears => 5, 
			DataRetentionPeriod.SevenYears => 7, 
			DataRetentionPeriod.Permanent => 0, 
			_ => 5, 
		};
	}

	public static string Describe(DataRetentionPeriod period)
	{
		return period switch
		{
			DataRetentionPeriod.FiveYears => "5 Years (Recommended / Statutory)", 
			DataRetentionPeriod.SevenYears => "7 Years", 
			DataRetentionPeriod.Permanent => "Permanent / Do Not Purge", 
			_ => "5 Years (Recommended / Statutory)", 
		};
	}
}

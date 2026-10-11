using System;
using System.Collections.Generic;
using System.Linq;

namespace PharmaBill.Core.Security;

public static class TrialClock
{
	public const int TRIAL_DAYS = 7;

	public static TimeSpan TrialDuration => TimeSpan.FromDays(TRIAL_DAYS);

	public static TrialEvaluation Evaluate(DateTime utcNow, IEnumerable<DateTime> persistedTrialStartsUtc, DateTime? lastObservedUtc)
	{
		DateTime dateTime = persistedTrialStartsUtc.DefaultIfEmpty(utcNow).Min();
		bool flag = lastObservedUtc.HasValue && utcNow < lastObservedUtc.Value;
		TimeSpan timeSpan = (flag ? TrialDuration : (utcNow - dateTime));
		TimeSpan timeSpan2 = TrialDuration - timeSpan;
		return new TrialEvaluation(dateTime, flag, (timeSpan2 > TimeSpan.Zero) ? timeSpan2 : TimeSpan.Zero, !flag && timeSpan2 > TimeSpan.Zero);
	}
}

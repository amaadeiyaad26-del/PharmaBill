using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class NumberSeriesService(IUnitOfWork unitOfWork)
{
	public const string DefaultPrefix = "WIN1";

	public async Task<string> PreviewNextAsync(string seriesPrefix = "WIN1", DateOnly? date = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(seriesPrefix, "seriesPrefix");
		DateOnly date2 = date ?? DateOnly.FromDateTime(DateTime.Today);
		string financialYear = GetFinancialYear(date2);
		NumberSeries numberSeries = await unitOfWork.Context.NumberSeries.AsNoTracking().SingleOrDefaultAsync((NumberSeries item) => item.SeriesPrefix == seriesPrefix && item.FinancialYear == financialYear, cancellationToken);
		long value = ((numberSeries == null) ? 1 : checked(numberSeries.LastNumber + 1));
		return $"{seriesPrefix}/{financialYear}/{value:D6}";
	}

	public async Task<string> AllocateAsync(string seriesPrefix = "WIN1", DateOnly? date = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(seriesPrefix, "seriesPrefix");
		if (unitOfWork.Context.Database.CurrentTransaction == null)
		{
			throw new InvalidOperationException("Number allocation requires an active unit-of-work transaction.");
		}
		DateOnly date2 = date ?? DateOnly.FromDateTime(DateTime.Today);
		string financialYear = GetFinancialYear(date2);
		NumberSeries series = await unitOfWork.Context.NumberSeries.SingleOrDefaultAsync((NumberSeries item) => item.SeriesPrefix == seriesPrefix && item.FinancialYear == financialYear, cancellationToken);
		checked
		{
			if (series == null)
			{
				series = new NumberSeries
				{
					SeriesPrefix = seriesPrefix,
					FinancialYear = financialYear,
					LastNumber = 1L
				};
				unitOfWork.Context.NumberSeries.Add(series);
			}
			else
			{
				series.LastNumber++;
			}
			await unitOfWork.SaveChangesAsync(cancellationToken);
			return $"{seriesPrefix}/{financialYear}/{series.LastNumber:D6}";
		}
	}

	public static string GetFinancialYear(DateOnly date)
	{
		int num = ((date.Month >= 4) ? date.Year : (date.Year - 1));
		return $"{num}-{(num + 1) % 100:D2}";
	}
}

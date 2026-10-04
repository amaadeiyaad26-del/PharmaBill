using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class NumberSeriesService(IUnitOfWork unitOfWork)
{
    public const string DefaultPrefix = "WIN1";

    public async Task<string> PreviewNextAsync(
        string seriesPrefix = DefaultPrefix,
        DateOnly? date = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seriesPrefix);
        var onDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var financialYear = GetFinancialYear(onDate);
        var series = await unitOfWork.Context.NumberSeries
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.SeriesPrefix == seriesPrefix && item.FinancialYear == financialYear,
                cancellationToken);
        var nextNumber = series is null ? 1 : checked(series.LastNumber + 1);
        return $"{seriesPrefix}/{financialYear}/{nextNumber:D6}";
    }

    public async Task<string> AllocateAsync(
        string seriesPrefix = DefaultPrefix,
        DateOnly? date = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seriesPrefix);
        if (unitOfWork.Context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Number allocation requires an active unit-of-work transaction.");
        }

        var onDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var financialYear = GetFinancialYear(onDate);
        var series = await unitOfWork.Context.NumberSeries
            .SingleOrDefaultAsync(
                item => item.SeriesPrefix == seriesPrefix && item.FinancialYear == financialYear,
                cancellationToken);

        if (series is null)
        {
            series = new NumberSeries
            {
                SeriesPrefix = seriesPrefix,
                FinancialYear = financialYear,
                LastNumber = 1
            };
            unitOfWork.Context.NumberSeries.Add(series);
        }
        else
        {
            series.LastNumber = checked(series.LastNumber + 1);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return $"{seriesPrefix}/{financialYear}/{series.LastNumber:D6}";
    }

    public static string GetFinancialYear(DateOnly date)
    {
        var startYear = date.Month >= 4 ? date.Year : date.Year - 1;
        return $"{startYear}-{(startYear + 1) % 100:D2}";
    }
}

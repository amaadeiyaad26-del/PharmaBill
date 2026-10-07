using System;
using System.Runtime.CompilerServices;

namespace PharmaBill.App.ViewModels;

public sealed record WholesaleStockChoice(Guid DrugId, Guid BatchId, string DrugName, string BatchNo, DateOnly? ExpiryDate, decimal Available, decimal Mrp, string? Schedule, decimal GstRate)
{
	public string Display
	{
		get
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(31, 4);
			defaultInterpolatedStringHandler.AppendFormatted(DrugName);
			defaultInterpolatedStringHandler.AppendLiteral("  ·  Batch ");
			defaultInterpolatedStringHandler.AppendFormatted(BatchNo);
			defaultInterpolatedStringHandler.AppendLiteral("  ·  Exp ");
			DateOnly? expiryDate = ExpiryDate;
			defaultInterpolatedStringHandler.AppendFormatted((!expiryDate.HasValue) ? "—" : expiryDate.GetValueOrDefault().ToString("MM/yyyy"));
			defaultInterpolatedStringHandler.AppendLiteral("  ·  Stock ");
			defaultInterpolatedStringHandler.AppendFormatted(Available, "0.##");
			return defaultInterpolatedStringHandler.ToStringAndClear();
		}
	}
}

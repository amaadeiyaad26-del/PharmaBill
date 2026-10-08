using System;
using System.Runtime.CompilerServices;

namespace PharmaBill.App.ViewModels;

public sealed record WholesaleStockChoice(
	Guid DrugId,
	Guid BatchId,
	string DrugName,
	string BatchNo,
	DateOnly? ExpiryDate,
	decimal Available,
	decimal Mrp,
	decimal? Ptr,
	string? PackLabel,
	decimal UnitsPerStrip,
	decimal UnitsPerBox,
	string? Schedule,
	decimal GstRate)
{
	/// <summary>Default trade rate: PTR when present and ≤ MRP, otherwise MRP.</summary>
	public decimal DefaultWholesaleRate
	{
		get
		{
			if (Ptr is > 0m)
			{
				return Ptr.Value > Mrp && Mrp > 0m ? Mrp : Ptr.Value;
			}

			return Mrp;
		}
	}

	public decimal GetUnitsPerSaleUnit(string? saleUnit)
	{
		return saleUnit?.Trim().ToUpperInvariant() switch
		{
			"BOX" or "CARTON" => UnitsPerBox > 0m ? UnitsPerBox : 1m,
			"STRIP" or "PACK" => UnitsPerStrip > 0m ? UnitsPerStrip : 1m,
			_ => 1m
		};
	}

	public string Display
	{
		get
		{
			DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(48, 5);
			handler.AppendFormatted(DrugName);
			handler.AppendLiteral("  ·  Batch ");
			handler.AppendFormatted(BatchNo);
			handler.AppendLiteral("  ·  Exp ");
			DateOnly? expiryDate = ExpiryDate;
			handler.AppendFormatted((!expiryDate.HasValue) ? "—" : expiryDate.GetValueOrDefault().ToString("MM/yyyy"));
			handler.AppendLiteral("  ·  Stock ");
			handler.AppendFormatted(Available, "0.##");
			if (Ptr is > 0m)
			{
				handler.AppendLiteral("  ·  PTR ₹");
				handler.AppendFormatted(Ptr.Value, "0.##");
			}

			return handler.ToStringAndClear();
		}
	}
}

using System;

namespace PharmaBill.Data.Services;

public sealed record ChartBar(string Label, decimal Value, double Fraction)
{
	public double BarHeight
	{
		get
		{
			if (!(Value > 0m))
			{
				return 0.0;
			}
			return Math.Max(Fraction * 140.0, 3.0);
		}
	}

	public string ValueText
	{
		get
		{
			if (!(Value > 0m))
			{
				return string.Empty;
			}
			return Value.ToString("N0");
		}
	}

	public const double MaxBarHeight = 140.0;
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace PharmaBill.App.Controls;

public sealed class NavIconConverter : IValueConverter
{
	private static readonly Dictionary<string, string> Glyphs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["Dashboard"] = "\ue80f",
		["Billing"] = "\ue719", // Shopping cart / register
		["WholesaleBilling"] = "\ue8a5", // Document / invoice
		["Stock"] = "\ue7b8",
		["Purchases"] = "\ue7b8",
		["Customers"] = "\ue716",
		["Pricing"] = "\ue8ec",
		["Accounts"] = "\ue8c7",
		["BankReconciliation"] = "\ue8c8",
		["CollectionsDunning"] = "\ue8bd",
		["Returns"] = "\ue7a7",
		["StockInHand"] = "\ue8f1",
		["Registers"] = "\ue8a5",
		["DrugRecords"] = "\ueb51",
		["Reports"] = "\ue9d2",
		["GstReturns"] = "\ue8ef",
		["StockTransfer"] = "\ue8ab",
		["Backup"] = "\ue74e",
		["Sync"] = "\ue895",
		["Inspector"] = "\ue721",
		["Settings"] = "\ue713",
		["Diagnostics"] = "\ue9d9",
		["Import"] = "\ue896",
		["Simulator"] = "\ue9f9"
	};

	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (!(value is string key) || !Glyphs.TryGetValue(key, out string value2))
		{
			return "\ue700";
		}
		return value2;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}

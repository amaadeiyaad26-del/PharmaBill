using System.Collections.Generic;

namespace PharmaBill.App.Services;

public sealed record PurchaseSpreadsheetData(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

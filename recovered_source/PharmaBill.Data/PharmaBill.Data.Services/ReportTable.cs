using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record ReportTable(string Title, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<string>? RowKeys = null);

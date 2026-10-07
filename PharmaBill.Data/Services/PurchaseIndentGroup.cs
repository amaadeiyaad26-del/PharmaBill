using System;
using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record PurchaseIndentGroup(Guid? SupplierId, string SupplierName, IReadOnlyList<PurchaseIndentItem> Items);

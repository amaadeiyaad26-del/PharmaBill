using System;

namespace PharmaBill.App.ViewModels;

public sealed record BatchReturnOption(Guid BatchId, Guid SupplierId, string BatchNo, string Display, decimal PurchasePrice);

using System;
using PharmaBill.Core.Inventory;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed record StockRowViewModel(Guid DrugId, string Medicine, string? Schedule, StockBatchRow Batch, string Status, decimal ReorderLevel, decimal TotalStock)
{
	public Guid BatchId => Batch.BatchId;

	public string BatchNo => Batch.BatchNo;

	public DateOnly? ExpiryDate => Batch.ExpiryDate;

	public decimal Mrp => Batch.Mrp;

	public decimal PurchaseRate => Batch.PurchaseRate;

	public decimal Quantity => Batch.Quantity;

	public bool IsShortage => StockShortage.IsShortage(TotalStock, ReorderLevel);
}

using System;
using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record WholesaleDashboardSnapshot(
	decimal TodayTurnover,
	int TodayOrderCount,
	decimal YesterdayTurnover,
	decimal TurnoverTrendPercent,
	decimal OutstandingReceivables,
	decimal OverdueOver45,
	decimal SupplierPayables,
	decimal PayablesDueThisWeek,
	int DraftOrders,
	int ReadyToPackOrders,
	int DispatchedOrders,
	int PendingEWayOrders,
	decimal Ageing0To30,
	decimal Ageing31To60,
	decimal Ageing61To90,
	decimal AgeingOver90,
	IReadOnlyList<WholesaleOverdueBuyerRow> TopOverdueBuyers,
	int PipelineReceivedCount,
	decimal PipelineReceivedValue,
	int PipelinePickingCount,
	int PipelineDispatchedCount,
	int PipelineDeliveredCount,
	IReadOnlyList<WholesaleBulkVelocityRow> FastMovingBulk,
	int EInvoicesToday,
	int PendingIrn,
	int EWayExpiringSoon);

public sealed record WholesaleOverdueBuyerRow(
	Guid CustomerId,
	string PharmacyName,
	string? Phone,
	decimal CreditLimit,
	decimal OverdueAmount,
	int DaysOverdue);

public sealed record WholesaleBulkVelocityRow(
	Guid DrugId,
	string ProductName,
	decimal PacksSold,
	decimal WarehouseQty,
	string? TopBatchNo);

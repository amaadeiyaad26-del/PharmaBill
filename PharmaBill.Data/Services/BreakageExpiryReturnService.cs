using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record BrkExpCandidateRow(
	Guid BatchId,
	Guid DrugId,
	string DrugName,
	string BatchNo,
	DateOnly? ExpiryDate,
	decimal Quantity,
	decimal PurchaseRate,
	Guid? SupplierId,
	string? SupplierName,
	string ReasonHint);

public sealed record BrkExpLineInput(Guid BatchId, decimal Quantity);

public sealed record ReplacementLineInput(Guid BatchId, decimal Quantity, string? Notes);

public sealed class BreakageExpiryReturnService(
	IUnitOfWork unitOfWork,
	PurchaseService purchaseService,
	NumberSeriesService numberSeries,
	IEntitlementService entitlement)
{
	public async Task<IReadOnlyList<BrkExpCandidateRow>> GetCandidatesAsync(CancellationToken cancellationToken = default)
	{
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		List<Batch> batches = await unitOfWork.Context.Batches.AsNoTracking()
			.Where(b => b.Quantity > 0m)
			.ToListAsync(cancellationToken);
		Dictionary<Guid, Drug> drugs = await unitOfWork.Context.Drugs.AsNoTracking().ToDictionaryAsync(d => d.Id, cancellationToken);
		Dictionary<Guid, string> suppliers = await unitOfWork.Context.Suppliers.AsNoTracking()
			.ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

		return batches
			.Where(b => drugs.ContainsKey(b.DrugId))
			.Where(b => b.ExpiryDate < today || b.IsBanned)
			.Select(b => new BrkExpCandidateRow(
				b.Id,
				b.DrugId,
				drugs[b.DrugId].Name,
				b.BatchNo,
				b.ExpiryDate,
				b.Quantity,
				b.PurchasePrice,
				b.SupplierId,
				b.SupplierId.HasValue ? suppliers.GetValueOrDefault(b.SupplierId.Value) : null,
				b.IsBanned ? "Banned / Recalled" : "Expired"))
			.OrderBy(r => r.ExpiryDate)
			.ThenBy(r => r.DrugName)
			.ToList();
	}

	public async Task<PurchaseReturn> IssueBrkExpReturnAsync(
		Guid supplierId,
		IReadOnlyList<BrkExpLineInput> lines,
		string reason,
		Guid actingUserId,
		UserRole role,
		CancellationToken cancellationToken = default)
	{
		if (!(await entitlement.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken)))
		{
			throw new InvalidOperationException("Breakage / expiry returns are unavailable in read-only mode.");
		}

		string returnNo = await numberSeries.AllocateAsync("BE", DateOnly.FromDateTime(DateTime.Today), cancellationToken);
		IReadOnlyList<PurchaseReturnLineInput> mapped = lines
			.Where(l => l.Quantity > 0m)
			.Select(l => new PurchaseReturnLineInput(l.BatchId, l.Quantity))
			.ToArray();
		string note = string.IsNullOrWhiteSpace(reason)
			? "Breakage / Expiry outward issue (debit note)"
			: "Brk/Exp: " + reason.Trim();
		return await purchaseService.SavePurchaseReturnAsync(
			supplierId,
			returnNo,
			DateOnly.FromDateTime(DateTime.Today),
			mapped,
			note,
			actingUserId,
			role,
			cancellationToken);
	}

	/// <summary>
	/// Logs free replacement units from a distributor without creating taxable purchase amounts.
	/// </summary>
	public async Task ReceiveReplacementAsync(
		IReadOnlyList<ReplacementLineInput> lines,
		Guid actingUserId,
		UserRole role,
		CancellationToken cancellationToken = default)
	{
		_ = role;
		if (!(await entitlement.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken)))
		{
			throw new InvalidOperationException("Replacement receive is unavailable in read-only mode.");
		}

		if (lines.Count == 0 || lines.Any(l => l.Quantity <= 0m))
		{
			throw new InvalidOperationException("Provide at least one replacement line with quantity greater than zero.");
		}

		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		foreach (ReplacementLineInput line in lines)
		{
			Batch batch = await unitOfWork.Context.Batches.SingleAsync(b => b.Id == line.BatchId, cancellationToken);
			batch.Quantity += line.Quantity;
			unitOfWork.Context.StockAdjustments.Add(new StockAdjustment
			{
				BatchId = batch.Id,
				DrugId = batch.DrugId,
				QuantityChange = line.Quantity,
				Reason = "ReplacementReceive (non-taxable)"
			});
			unitOfWork.Context.StockMovements.Add(new StockMovement
			{
				BatchId = batch.Id,
				DrugId = batch.DrugId,
				QuantityChange = line.Quantity,
				MovementType = "ReplacementReceive",
				ReferenceType = "Replacement",
				Notes = string.IsNullOrWhiteSpace(line.Notes) ? "Free replacement — no taxable purchase" : line.Notes.Trim()
			});
		}

		unitOfWork.Context.AuditLogs.Add(new AuditLog
		{
			UserId = actingUserId,
			Action = "ReplacementReceive",
			EntityName = "StockAdjustment",
			Details = $"Free replacement units: {lines.Sum(l => l.Quantity):0.##}"
		});
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	/// <summary>
	/// Issues free replacement stock outward (company sample / FOC) without sales tax.
	/// </summary>
	public async Task IssueReplacementAsync(
		IReadOnlyList<ReplacementLineInput> lines,
		Guid actingUserId,
		UserRole role,
		CancellationToken cancellationToken = default)
	{
		_ = role;
		if (!(await entitlement.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken)))
		{
			throw new InvalidOperationException("Replacement issue is unavailable in read-only mode.");
		}

		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		foreach (ReplacementLineInput line in lines.Where(l => l.Quantity > 0m))
		{
			Batch batch = await unitOfWork.Context.Batches.SingleAsync(b => b.Id == line.BatchId, cancellationToken);
			if (batch.Quantity < line.Quantity)
			{
				throw new InvalidOperationException($"Insufficient stock on batch {batch.BatchNo} for replacement issue.");
			}

			batch.Quantity -= line.Quantity;
			unitOfWork.Context.StockAdjustments.Add(new StockAdjustment
			{
				BatchId = batch.Id,
				DrugId = batch.DrugId,
				QuantityChange = -line.Quantity,
				Reason = "ReplacementIssue (non-taxable)"
			});
			unitOfWork.Context.StockMovements.Add(new StockMovement
			{
				BatchId = batch.Id,
				DrugId = batch.DrugId,
				QuantityChange = -line.Quantity,
				MovementType = "ReplacementIssue",
				ReferenceType = "Replacement",
				Notes = string.IsNullOrWhiteSpace(line.Notes) ? "Replacement issue — no taxable sale" : line.Notes.Trim()
			});
		}

		unitOfWork.Context.AuditLogs.Add(new AuditLog
		{
			UserId = actingUserId,
			Action = "ReplacementIssue",
			EntityName = "StockAdjustment",
			Details = $"Replacement issued: {lines.Sum(l => l.Quantity):0.##}"
		});
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}
}

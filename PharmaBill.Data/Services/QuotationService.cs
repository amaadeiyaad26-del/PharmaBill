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

public sealed record QuotationLineInput(Guid DrugId, Guid? BatchId, string DrugName, decimal Quantity, decimal FreeQuantity, decimal UnitPrice, decimal DiscountAmount);

/// <summary>
/// Quotations for retail and wholesale. Creating, cancelling or converting a quotation never changes stock or
/// ledgers; those happen only when the converted bill is posted from the billing page.
/// </summary>
public sealed class QuotationService(IUnitOfWork unitOfWork, NumberSeriesService numberSeriesService)
{
	public const string RetailMode = "Retail";

	public const string WholesaleMode = "Wholesale";

	public async Task<Quotation> CreateAsync(string mode, Guid? customerId, string partyName, string? partyPhone, DateOnly? validUntil, IReadOnlyList<QuotationLineInput> lines, string? notes, Guid userId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(role, AppPermission.CreateBill))
		{
			throw new UnauthorizedAccessException("Your role cannot create quotations.");
		}
		if (mode != RetailMode && mode != WholesaleMode)
		{
			throw new ArgumentException("Quotation mode must be Retail or Wholesale.", nameof(mode));
		}
		if (lines.Count == 0)
		{
			throw new InvalidOperationException("Add at least one medicine to the quotation.");
		}
		if (lines.Any(line => line.Quantity <= 0m))
		{
			throw new InvalidOperationException("Every quotation line needs a quantity above zero.");
		}
		if (mode == WholesaleMode && !lines.All(line => line.BatchId.HasValue))
		{
			throw new InvalidOperationException("Wholesale quotation lines need a batch.");
		}

		PharmacyProfile profile = await unitOfWork.Context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		string quoteNo = await numberSeriesService.AllocateAsync(profile.InvoicePrefix + "-QT", today, cancellationToken);
		Quotation quotation = new Quotation
		{
			Mode = mode,
			QuoteNo = quoteNo,
			QuoteDate = today,
			ValidUntil = validUntil,
			CustomerId = customerId,
			PartyName = string.IsNullOrWhiteSpace(partyName) ? "Walk-in customer" : partyName.Trim(),
			PartyPhone = string.IsNullOrWhiteSpace(partyPhone) ? null : partyPhone.Trim(),
			Status = Quotation.OpenStatus,
			Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
		};
		decimal total = 0m;
		foreach (QuotationLineInput line in lines)
		{
			decimal lineTotal = Math.Max(0m, Math.Round(line.Quantity * line.UnitPrice - line.DiscountAmount, 2, MidpointRounding.AwayFromZero));
			total += lineTotal;
			unitOfWork.Context.QuotationItems.Add(new QuotationItem
			{
				QuotationId = quotation.Id,
				DrugId = line.DrugId,
				BatchId = line.BatchId,
				DrugName = line.DrugName,
				Quantity = line.Quantity,
				FreeQuantity = line.FreeQuantity,
				UnitPrice = line.UnitPrice,
				DiscountAmount = line.DiscountAmount,
				LineTotal = lineTotal
			});
		}
		quotation.EstimatedTotal = total;
		unitOfWork.Context.Quotations.Add(quotation);
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
		return quotation;
	}

	public async Task<IReadOnlyList<Quotation>> ListAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return await unitOfWork.Context.Quotations.AsNoTracking()
			.OrderByDescending(item => item.QuoteDate)
			.ThenByDescending(item => item.QuoteNo)
			.ToListAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<QuotationItem>> GetItemsAsync(Guid quotationId, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await unitOfWork.Context.QuotationItems.AsNoTracking()
			.Where(item => item.QuotationId == quotationId)
			.ToListAsync(cancellationToken);
	}

	public async Task<Quotation> GetOpenAsync(Guid quotationId, CancellationToken cancellationToken = default(CancellationToken))
	{
		Quotation quotation = await unitOfWork.Context.Quotations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == quotationId, cancellationToken)
			?? throw new InvalidOperationException("Quotation was not found.");
		if (quotation.Status != Quotation.OpenStatus)
		{
			throw new InvalidOperationException($"Quotation {quotation.QuoteNo} is {quotation.Status.ToLowerInvariant()} and cannot be used for a bill.");
		}
		return quotation;
	}

	public async Task CancelAsync(Guid quotationId, Guid userId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(role, AppPermission.CreateBill))
		{
			throw new UnauthorizedAccessException("Your role cannot cancel quotations.");
		}
		Quotation quotation = await unitOfWork.Context.Quotations.SingleOrDefaultAsync(item => item.Id == quotationId, cancellationToken)
			?? throw new InvalidOperationException("Quotation was not found.");
		if (quotation.Status != Quotation.OpenStatus)
		{
			throw new InvalidOperationException($"Quotation {quotation.QuoteNo} is {quotation.Status.ToLowerInvariant()}; only an open quotation can be cancelled.");
		}
		quotation.Status = Quotation.CancelledStatus;
		await unitOfWork.SaveChangesAsync(cancellationToken);
	}

	/// <summary>Called after the bill made from this quotation is posted.</summary>
	public async Task MarkConvertedAsync(Guid quotationId, string invoiceNo, CancellationToken cancellationToken = default(CancellationToken))
	{
		Quotation quotation = await unitOfWork.Context.Quotations.SingleOrDefaultAsync(item => item.Id == quotationId, cancellationToken)
			?? throw new InvalidOperationException("Quotation was not found.");
		if (quotation.Status != Quotation.OpenStatus)
		{
			return;
		}
		quotation.Status = Quotation.ConvertedStatus;
		quotation.ConvertedInvoiceNo = invoiceNo;
		await unitOfWork.SaveChangesAsync(cancellationToken);
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class StatutoryRegisterService(IServiceScopeFactory scopeFactory)
{
	public async Task<IReadOnlyList<StatutoryRegisterRow>> SearchAsync(string registerType, Guid actingUserId, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(registerType, "registerType");
		using IServiceScope scope = scopeFactory.CreateScope();
		IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
		PharmaBillDbContext context = unitOfWork.Context;
		List<ScheduleRegisterEntry> entries = await (from item in context.ScheduleRegisterEntries.AsNoTracking()
			orderby item.EntryAtUtc
			select item).ToListAsync(cancellationToken);
		if (!registerType.Equals("All", StringComparison.OrdinalIgnoreCase))
		{
			entries = entries.Where((ScheduleRegisterEntry item) => item.RegisterType.Contains(registerType, StringComparison.OrdinalIgnoreCase) || (registerType == "Habit-forming" && (item.RegisterType.Contains("Habit", StringComparison.OrdinalIgnoreCase) || item.RegisterType.Equals("HABIT", StringComparison.OrdinalIgnoreCase)))).ToList();
		}
		Guid[] saleIds = (from item in entries
			where item.SaleId.HasValue
			select item.SaleId.Value).Distinct().ToArray();
		Dictionary<Guid, Sale> saleIdMap = await (from item in context.Sales.AsNoTracking()
			where saleIds.Contains(item.Id)
			select item).ToDictionaryAsync((Sale item) => item.Id, cancellationToken);
		Guid[] prescriptionIds = (from item in saleIdMap.Values
			where item.PrescriptionId.HasValue
			select item.PrescriptionId.Value).Distinct().ToArray();
		Dictionary<Guid, Prescription> prescriptions = await (from item in context.Prescriptions.AsNoTracking()
			where prescriptionIds.Contains(item.Id)
			select item).ToDictionaryAsync((Prescription item) => item.Id, cancellationToken);
		Guid[] patientIds = (from item in entries
			where item.PatientId.HasValue
			select item.PatientId.Value).Distinct().ToArray();
		Dictionary<Guid, Patient> patients = await (from item in context.Patients.AsNoTracking()
			where patientIds.Contains(item.Id)
			select item).ToDictionaryAsync((Patient item) => item.Id, cancellationToken);
		Guid[] drugIds = (from item in entries
			where item.DrugId.HasValue
			select item.DrugId.Value).Distinct().ToArray();
		Dictionary<Guid, Drug> drugs = await (from item in context.Drugs.AsNoTracking()
			where drugIds.Contains(item.Id)
			select item).ToDictionaryAsync((Drug item) => item.Id, cancellationToken);
		Guid[] batchIds = (from item in entries
			where item.BatchId.HasValue
			select item.BatchId.Value).Distinct().ToArray();
		Dictionary<Guid, Batch> batches = await (from item in context.Batches.AsNoTracking()
			where batchIds.Contains(item.Id)
			select item).ToDictionaryAsync((Batch item) => item.Id, cancellationToken);
		Guid[] invoiceIds = (from item in entries
			where item.SupplierId.HasValue
			select item.SupplierId.Value).Distinct().ToArray();
		Dictionary<Guid, Supplier> suppliers = await (from item in context.Suppliers.AsNoTracking()
			where invoiceIds.Contains(item.Id)
			select item).ToDictionaryAsync((Supplier item) => item.Id, cancellationToken);
		Dictionary<Guid, Customer> buyersById = (await context.Customers.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary((Customer item) => item.Id);
		Guid[] buyerIds = buyersById.Keys.ToArray();
		Dictionary<Guid, CustomerLicence[]> buyerLicences = (from item in await (from item in context.CustomerLicences.AsNoTracking()
				where buyerIds.Contains(item.CustomerId)
				select item).ToListAsync(cancellationToken)
			group item by item.CustomerId).ToDictionary((IGrouping<Guid, CustomerLicence> group) => group.Key, (IGrouping<Guid, CustomerLicence> group) => group.ToArray());
		Dictionary<Guid, (DateTime, decimal)[]> dictionary = (from item in await (from item in context.StockMovements.AsNoTracking()
				where batchIds.Contains(item.BatchId)
				select item).ToListAsync(cancellationToken)
			group item by item.BatchId).ToDictionary((IGrouping<Guid, StockMovement> group) => group.Key, (IGrouping<Guid, StockMovement> group) =>
		{
			decimal running = 0m;
			return group.OrderBy((StockMovement item) => item.MovementAtUtc).Select((StockMovement item) =>
			{
				running += item.QuantityChange;
				return (AtUtc: item.MovementAtUtc, Balance: running);
			}).ToArray();
		});
		List<StatutoryRegisterRow> results = new List<StatutoryRegisterRow>(entries.Count);
		using (List<ScheduleRegisterEntry>.Enumerator enumerator = entries.GetEnumerator())
		{
			decimal balance;
			decimal num;
			string documentNo;
			Sale? obj;
			ScheduleRegisterEntry entry;
			Patient patient;
			Customer customer;
			Supplier supplier;
			string buyerLicenceNo;
			Prescription prescription;
			Drug drug;
			Batch batch;
			for (; enumerator.MoveNext(); balance = num, documentNo = obj?.InvoiceNo ?? ExtractDocumentNumber(entry.Notes), results.Add(new StatutoryRegisterRow(entry.Id, entry.EntryAtUtc, entry.RegisterType, documentNo, patient?.Name ?? customer?.Name ?? supplier?.Name ?? entry.PatientName ?? string.Empty, patient?.Address ?? customer?.Address ?? supplier?.Address ?? string.Empty, patient?.Phone ?? customer?.Phone ?? supplier?.Phone ?? string.Empty, buyerLicenceNo, prescription?.PrescriberName ?? entry.PrescriberName ?? string.Empty, prescription?.PrescriberRegistrationNumber ?? entry.PrescriberRegistrationNumber ?? string.Empty, drug?.Name ?? string.Empty, batch?.BatchNo ?? entry.BatchNo ?? string.Empty, entry.Quantity.GetValueOrDefault(), balance, entry.Notes ?? string.Empty)))
			{
				entry = enumerator.Current;
				obj = (entry.SaleId.HasValue ? saleIdMap.GetValueOrDefault(entry.SaleId.Value) : null);
				patient = (entry.PatientId.HasValue ? patients.GetValueOrDefault(entry.PatientId.Value) : null);
				supplier = (entry.SupplierId.HasValue ? suppliers.GetValueOrDefault(entry.SupplierId.Value) : null);
				Guid? guid = obj?.PatientId;
				object obj2;
				if (guid.HasValue)
				{
					Guid valueOrDefault = guid.GetValueOrDefault();
					obj2 = buyersById.GetValueOrDefault(valueOrDefault);
				}
				else
				{
					obj2 = null;
				}
				customer = (Customer)obj2;
				batch = (entry.BatchId.HasValue ? batches.GetValueOrDefault(entry.BatchId.Value) : null);
				drug = (entry.DrugId.HasValue ? drugs.GetValueOrDefault(entry.DrugId.Value) : null);
				guid = obj?.PrescriptionId;
				object obj3;
				if (guid.HasValue)
				{
					Guid valueOrDefault2 = guid.GetValueOrDefault();
					obj3 = prescriptions.GetValueOrDefault(valueOrDefault2);
				}
				else
				{
					obj3 = null;
				}
				prescription = (Prescription)obj3;
				buyerLicenceNo = ((customer != null) ? (buyerLicences.GetValueOrDefault(customer.Id, Array.Empty<CustomerLicence>()).FirstOrDefault((CustomerLicence item) => item.ExpiresOn >= DateOnly.FromDateTime(entry.EntryAtUtc))?.LicenceNumber ?? string.Empty) : (supplier?.DrugLicenceNumber ?? string.Empty));
				guid = entry.BatchId;
				if (guid.HasValue)
				{
					Guid valueOrDefault3 = guid.GetValueOrDefault();
					if (dictionary.TryGetValue(valueOrDefault3, out var value))
					{
						num = GetBalanceAt(value, entry.EntryAtUtc);
						continue;
					}
				}
				num = 0m;
			}
		}
		context.AuditLogs.Add(new AuditLog
		{
			UserId = actingUserId,
			Action = "StatutoryRegisterSearched",
			EntityName = "ScheduleRegisterEntry",
			Details = $"Register filter: {registerType}; {results.Count} entries."
		});
		IReadOnlyList<StatutoryRegisterRow> result;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			await unitOfWork.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			result = results;
		}
		return result;
	}

	public async Task AppendCorrectionAsync(Guid entryId, string reason, Guid actingUserId, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(reason, "reason");
		using IServiceScope scope = scopeFactory.CreateScope();
		IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
		PharmaBillDbContext context = unitOfWork.Context;
		ScheduleRegisterEntry source = (await context.ScheduleRegisterEntries.AsNoTracking().SingleOrDefaultAsync((ScheduleRegisterEntry item) => item.Id == entryId, cancellationToken)) ?? throw new InvalidOperationException("The statutory register entry was not found.");
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		ScheduleRegisterEntry scheduleRegisterEntry = new ScheduleRegisterEntry
		{
			RegisterType = source.RegisterType + "-Correction",
			SaleId = source.SaleId,
			PatientId = source.PatientId,
			DrugId = source.DrugId,
			SupplierId = source.SupplierId,
			BatchId = source.BatchId,
			Quantity = source.Quantity,
			BatchNo = source.BatchNo,
			PatientName = source.PatientName,
			PrescriberName = source.PrescriberName,
			PrescriberRegistrationNumber = source.PrescriberRegistrationNumber,
			Notes = $"Correction to entry {source.Id:D}. Reason: {reason.Trim()}"
		};
		context.ScheduleRegisterEntries.Add(scheduleRegisterEntry);
		context.AuditLogs.Add(new AuditLog
		{
			UserId = actingUserId,
			Action = "StatutoryRegisterCorrectionAppended",
			EntityName = "ScheduleRegisterEntry",
			EntityId = scheduleRegisterEntry.Id,
			Details = scheduleRegisterEntry.Notes
		});
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	private static string ExtractDocumentNumber(string? notes)
	{
		if (string.IsNullOrWhiteSpace(notes))
		{
			return string.Empty;
		}
		if (notes.StartsWith("Purchase invoice ", StringComparison.OrdinalIgnoreCase))
		{
			return notes.Substring("Purchase invoice ".Length).Trim();
		}
		if (notes.StartsWith("Outgoing purchase return ", StringComparison.OrdinalIgnoreCase))
		{
			return notes.Substring("Outgoing purchase return ".Length).Split(';', 2)[0].Trim();
		}
		return notes;
	}

	private static decimal GetBalanceAt((DateTime AtUtc, decimal Balance)[] points, DateTime atUtc)
	{
		int num = 0;
		int num2 = points.Length;
		while (num < num2)
		{
			int num3 = num + (num2 - num) / 2;
			if (points[num3].AtUtc <= atUtc)
			{
				num = num3 + 1;
			}
			else
			{
				num2 = num3;
			}
		}
		if (num != 0)
		{
			return points[num - 1].Balance;
		}
		return 0m;
	}
}

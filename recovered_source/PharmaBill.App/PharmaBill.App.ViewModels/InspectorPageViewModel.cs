using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.ViewModels;

public sealed class InspectorPageViewModel(IServiceScopeFactory scopeFactory, SensitiveAccessService sensitiveAccess) : SectionPageViewModel("Inspector view"), ILoadablePage
{
	public DataView? Licences { get; private set; }

	public DataView? Stock { get; private set; }

	public DataView? Registers { get; private set; }

	public DataView? Invoices { get; private set; }

	public string StatusMessage { get; private set; } = "Read-only inspector view.";

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!(await sensitiveAccess.RequestInspectorPinAsync(Application.Current?.MainWindow, cancellationToken)))
		{
			StatusMessage = "Inspector access cancelled or PIN verification failed.";
			OnPropertyChanged("StatusMessage");
			return;
		}
		using IServiceScope scope = scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		bool wholesaleOnly = await (from item in context.PharmacyProfiles.AsNoTracking()
			select item.BusinessMode).SingleAsync(cancellationToken) == BusinessMode.Wholesaler;
		List<LicenceRecord> source = await (from item in context.LicenceRecords.AsNoTracking()
			orderby item.LicenceType
			select item).ToListAsync(cancellationToken);
		Licences = ToView(new string[5] { "Licence type", "Number", "Issue date", "Expiry", "Document" }, source.Select((LicenceRecord item) => new string[5]
		{
			item.LicenceType,
			item.LicenceNumber,
			item.IssuedOn?.ToString("d") ?? string.Empty,
			item.ExpiresOn?.ToString("d") ?? string.Empty,
			item.DocumentPath ?? string.Empty
		}));
		List<Batch> batches = await (from item in context.Batches.AsNoTracking()
			orderby item.ExpiryDate
			select item).ToListAsync(cancellationToken);
		Guid[] batchIds = batches.Select((Batch item) => item.Id).ToArray();
		Guid[] drugIds = batches.Select((Batch item) => item.DrugId).Distinct().ToArray();
		Dictionary<Guid, string> drugs = await (from item in context.Drugs.AsNoTracking()
			where drugIds.Contains(item.Id)
			select item).ToDictionaryAsync((Drug item) => item.Id, (Drug item) => item.Name, cancellationToken);
		Dictionary<Guid, decimal> movementTotals = (from item in await (from item in context.StockMovements.AsNoTracking()
				where batchIds.Contains(item.BatchId)
				select item).ToListAsync(cancellationToken)
			group item by item.BatchId).ToDictionary((IGrouping<Guid, StockMovement> group) => group.Key, (IGrouping<Guid, StockMovement> group) => group.Sum((StockMovement item) => item.QuantityChange));
		Stock = ToView(new string[6] { "Drug", "Batch", "Expiry", "Current quantity", "MRP", "Rack" }, batches.Select((Batch item) => new string[6]
		{
			drugs.GetValueOrDefault(item.DrugId, string.Empty),
			item.BatchNo,
			item.ExpiryDate?.ToString("d") ?? string.Empty,
			movementTotals.GetValueOrDefault(item.Id).ToString("0.##"),
			item.Mrp?.ToString("N2") ?? string.Empty,
			item.Rack ?? string.Empty
		}));
		List<ScheduleRegisterEntry> registers = await (from item in context.ScheduleRegisterEntries.AsNoTracking()
			orderby item.EntryAtUtc descending
			select item).Take(5000).ToListAsync(cancellationToken);
		Registers = ToView(new string[7] { "Date", "Register", "Patient / buyer", "Drug", "Batch", "Quantity", "Reason" }, registers.Select((ScheduleRegisterEntry item) =>
		{
			string[] array = new string[7]
			{
				item.EntryAtUtc.ToLocalTime().ToString("g"),
				item.RegisterType,
				wholesaleOnly ? string.Empty : (item.PatientName ?? string.Empty),
				null,
				null,
				null,
				null
			};
			Guid? drugId = item.DrugId;
			array[3] = ((!drugId.HasValue) ? string.Empty : CollectionExtensions.GetValueOrDefault(key: drugId.GetValueOrDefault(), dictionary: drugs, defaultValue: string.Empty));
			array[4] = item.BatchNo ?? string.Empty;
			array[5] = item.Quantity?.ToString("0.##") ?? string.Empty;
			array[6] = item.Notes ?? string.Empty;
			return array;
		}));
		List<Sale> sales = await (from item in context.Sales.AsNoTracking()
			orderby item.SaleAtUtc descending
			select item).Take(5000).ToListAsync(cancellationToken);
		List<WholesaleInvoice> wholesale = await (from item in context.WholesaleInvoices.AsNoTracking()
			orderby item.InvoiceAtUtc descending
			select item).Take(5000).ToListAsync(cancellationToken);
		List<PurchaseInvoice> purchaseInvoices = await (from item in context.PurchaseInvoices.AsNoTracking()
			orderby item.InvoiceDate descending
			select item).Take(5000).ToListAsync(cancellationToken);
		Guid[] patientIds = (from item in sales
			where item.PatientId.HasValue
			select item.PatientId.Value).Distinct().ToArray();
		Dictionary<Guid, string> patients = await (from item in context.Patients.AsNoTracking()
			where patientIds.Contains(item.Id)
			select item).ToDictionaryAsync((Patient item) => item.Id, (Patient item) => item.Name, cancellationToken);
		Dictionary<Guid, string> customers = await context.Customers.AsNoTracking().ToDictionaryAsync((Customer item) => item.Id, (Customer item) => item.Name, cancellationToken);
		Dictionary<Guid, string> suppliers = await context.Suppliers.AsNoTracking().ToDictionaryAsync((Supplier item) => item.Id, (Supplier item) => item.Name, cancellationToken);
		IEnumerable<string[]> rows = sales.Select((Sale item) => new string[5]
		{
			item.SaleAtUtc.ToLocalTime().ToString("g"),
			"Retail sale",
			item.InvoiceNo,
			(!wholesaleOnly && item.PatientId.HasValue) ? patients.GetValueOrDefault(item.PatientId.Value, string.Empty) : string.Empty,
			item.TotalAmount.ToString("N2")
		}).Concat(wholesale.Select((WholesaleInvoice item) => new string[5]
		{
			item.InvoiceAtUtc.ToLocalTime().ToString("g"),
			"Wholesale invoice",
			item.InvoiceNo,
			customers.GetValueOrDefault(item.CustomerId, string.Empty),
			item.TotalAmount.ToString("N2")
		})).Concat(purchaseInvoices.Select((PurchaseInvoice item) => new string[5]
		{
			item.InvoiceDate.ToString("d"),
			"Purchase invoice",
			item.InvoiceNo,
			suppliers.GetValueOrDefault(item.SupplierId, string.Empty),
			item.TotalAmount.ToString("N2")
		}))
			.OrderByDescending((string[] item) => item[0], StringComparer.Ordinal)
			.Take(5000);
		Invoices = ToView(new string[5] { "Date", "Type", "Document no.", "Party", "Total" }, rows);
		StatusMessage = $"Read-only inspector view loaded. {_columnsCount(Licences)} licence(s), {batches.Count} batches, {registers.Count} register rows.";
		OnPropertyChanged("Licences");
		OnPropertyChanged("Stock");
		OnPropertyChanged("Registers");
		OnPropertyChanged("Invoices");
		OnPropertyChanged("StatusMessage");
	}

	private static int _columnsCount(DataView? view)
	{
		return view?.Count ?? 0;
	}

	private static DataView ToView(string[] columns, IEnumerable<string[]> rows)
	{
		DataTable dataTable = new DataTable();
		foreach (string columnName in columns)
		{
			dataTable.Columns.Add(columnName);
		}
		foreach (string[] row in rows)
		{
			dataTable.Rows.Add(row.Cast<object>().ToArray());
		}
		return dataTable.DefaultView;
	}
}

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Compliance;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class PurchaseInvoiceHistoryViewModel(IServiceScopeFactory scopeFactory, IFilePickerService filePicker) : ObservableObject
{
	private SupplierFilterChoice? _selectedSupplier;

	private DateTime? _fromDate;

	private DateTime? _toDate;

	private string _searchText = string.Empty;

	private string _statusMessage = string.Empty;

	private PurchaseInvoiceHistoryRow? _selectedInvoice;

	private bool _hasDetails;

	public ObservableCollection<SupplierFilterChoice> Suppliers { get; } = new ObservableCollection<SupplierFilterChoice>();

	public ObservableCollection<PurchaseInvoiceHistoryRow> Rows { get; } = new ObservableCollection<PurchaseInvoiceHistoryRow>();

	public ObservableCollection<PurchaseInvoiceHistoryLine> DetailLines { get; } = new ObservableCollection<PurchaseInvoiceHistoryLine>();

	public SupplierFilterChoice? SelectedSupplier
	{
		get => _selectedSupplier;
		set => SetProperty(ref _selectedSupplier, value);
	}

	public DateTime? FromDate
	{
		get => _fromDate;
		set => SetProperty(ref _fromDate, value);
	}

	public DateTime? ToDate
	{
		get => _toDate;
		set => SetProperty(ref _toDate, value);
	}

	public string SearchText
	{
		get => _searchText;
		set => SetProperty(ref _searchText, value ?? string.Empty);
	}

	public string StatusMessage
	{
		get => _statusMessage;
		set => SetProperty(ref _statusMessage, value ?? string.Empty);
	}

	public PurchaseInvoiceHistoryRow? SelectedInvoice
	{
		get => _selectedInvoice;
		set => SetProperty(ref _selectedInvoice, value);
	}

	public bool HasDetails
	{
		get => _hasDetails;
		set => SetProperty(ref _hasDetails, value);
	}

	public string DetailHeading => SelectedInvoice == null
		? "Invoice details"
		: SelectedInvoice.SupplierName + " · " + SelectedInvoice.InvoiceNo;

	public IAsyncRelayCommand RefreshCommand => new AsyncRelayCommand(LoadAsync);

	public IAsyncRelayCommand TodayCommand => new AsyncRelayCommand(async () =>
	{
		FromDate = DateTime.Today;
		ToDate = DateTime.Today;
		await LoadAsync();
	});

	public IAsyncRelayCommand ThisMonthCommand => new AsyncRelayCommand(async () =>
	{
		FromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
		ToDate = DateTime.Today;
		await LoadAsync();
	});

	public IAsyncRelayCommand AllDatesCommand => new AsyncRelayCommand(async () =>
	{
		FromDate = null;
		ToDate = null;
		await LoadAsync();
	});

	public IRelayCommand<PurchaseInvoiceHistoryRow?> ViewAttachmentCommand => new RelayCommand<PurchaseInvoiceHistoryRow?>(ViewAttachment);

	public IAsyncRelayCommand<PurchaseInvoiceHistoryRow?> AttachCommand => new AsyncRelayCommand<PurchaseInvoiceHistoryRow?>(AttachAsync);

	public IAsyncRelayCommand<PurchaseInvoiceHistoryRow?> OpenDetailsCommand => new AsyncRelayCommand<PurchaseInvoiceHistoryRow?>(LoadDetailsAsync);

	public async Task LoadAsync()
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		if (Suppliers.Count == 0)
		{
			Suppliers.Add(new SupplierFilterChoice(null, "All Suppliers"));
			foreach (var supplier in await context.Suppliers.AsNoTracking().OrderBy(item => item.Name).ToListAsync())
			{
				Suppliers.Add(new SupplierFilterChoice(supplier.Id, supplier.Name));
			}

			SelectedSupplier = Suppliers[0];
		}

		IQueryable<PurchaseInvoice> query = context.PurchaseInvoices.AsNoTracking();
		if (SelectedSupplier?.Id is Guid supplierId)
		{
			query = query.Where(invoice => invoice.SupplierId == supplierId);
		}

		if (FromDate is DateTime from)
		{
			DateOnly start = DateOnly.FromDateTime(from);
			query = query.Where(invoice => invoice.InvoiceDate >= start);
		}

		if (ToDate is DateTime to)
		{
			DateOnly end = DateOnly.FromDateTime(to);
			query = query.Where(invoice => invoice.InvoiceDate <= end);
		}

		List<PurchaseInvoice> invoices = await query
			.OrderByDescending(invoice => invoice.InvoiceDate)
			.ThenByDescending(invoice => invoice.CreatedAtUtc)
			.Take(500)
			.ToListAsync();
		Guid[] invoiceIds = invoices.Select(invoice => invoice.Id).ToArray();
		Guid[] supplierIds = invoices.Select(invoice => invoice.SupplierId).Distinct().ToArray();
		Dictionary<Guid, string> supplierNames = await context.Suppliers.AsNoTracking()
			.Where(supplier => supplierIds.Contains(supplier.Id))
			.ToDictionaryAsync(supplier => supplier.Id, supplier => supplier.Name);
		Dictionary<Guid, int> itemCounts = await context.PurchaseItems.AsNoTracking()
			.Where(item => invoiceIds.Contains(item.PurchaseInvoiceId))
			.GroupBy(item => item.PurchaseInvoiceId)
			.Select(group => new { group.Key, Count = group.Count() })
			.ToDictionaryAsync(item => item.Key, item => item.Count);
		string search = SearchText.Trim();
		if (search.Length > 0)
		{
			List<Guid> drugIds = await context.Drugs.AsNoTracking()
				.Where(drug => drug.Name.Contains(search) || (drug.BrandName != null && drug.BrandName.Contains(search)))
				.Select(drug => drug.Id)
				.ToListAsync();
			HashSet<Guid> matchingInvoices = (await context.PurchaseItems.AsNoTracking()
				.Where(item => invoiceIds.Contains(item.PurchaseInvoiceId) && drugIds.Contains(item.DrugId))
				.Select(item => item.PurchaseInvoiceId)
				.Distinct()
				.ToListAsync()).ToHashSet();
			invoices = invoices.Where(invoice =>
				invoice.InvoiceNo.Contains(search, StringComparison.OrdinalIgnoreCase)
				|| matchingInvoices.Contains(invoice.Id)
				|| supplierNames.GetValueOrDefault(invoice.SupplierId, string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
		}

		Rows.Clear();
		foreach (PurchaseInvoice invoice in invoices)
		{
			string? path = string.IsNullOrWhiteSpace(invoice.AttachedInvoicePath)
				? PurchaseAttachmentStore.PathFromLegacyNotes(invoice.Notes)
				: invoice.AttachedInvoicePath;
			bool hasFile = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
			Rows.Add(new PurchaseInvoiceHistoryRow
			{
				InvoiceId = invoice.Id,
				InvoiceDate = invoice.InvoiceDate,
				SupplierId = invoice.SupplierId,
				SupplierName = supplierNames.GetValueOrDefault(invoice.SupplierId, "Supplier"),
				InvoiceNo = invoice.InvoiceNo,
				ItemCount = itemCounts.GetValueOrDefault(invoice.Id),
				TotalAmount = invoice.TotalAmount,
				PaymentStatus = string.Equals(invoice.Status, "Posted", StringComparison.OrdinalIgnoreCase) ? "Unpaid" : (invoice.Status ?? "Unpaid"),
				AttachmentPath = hasFile ? path : null,
				OriginalFileName = invoice.OriginalFileName,
				CreatedAtUtc = invoice.CreatedAtUtc
			});
		}

		StatusMessage = Rows.Count + " purchase invoice(s).";
		if (SelectedInvoice != null)
		{
			PurchaseInvoiceHistoryRow? still = Rows.FirstOrDefault(row => row.InvoiceId == SelectedInvoice.InvoiceId);
			SelectedInvoice = still;
			if (still == null)
			{
				DetailLines.Clear();
				HasDetails = false;
				OnPropertyChanged(nameof(DetailHeading));
			}
		}
	}

	public async Task LoadDetailsAsync(PurchaseInvoiceHistoryRow? row)
	{
		if (row == null)
		{
			return;
		}

		SelectedInvoice = row;
		OnPropertyChanged(nameof(DetailHeading));
		DetailLines.Clear();
		using IServiceScope scope = scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		List<PurchaseItem> items = await context.PurchaseItems.AsNoTracking()
			.Where(item => item.PurchaseInvoiceId == row.InvoiceId)
			.ToListAsync();
		Guid[] drugIds = items.Select(item => item.DrugId).Distinct().ToArray();
		Dictionary<Guid, string> drugs = await context.Drugs.AsNoTracking()
			.Where(drug => drugIds.Contains(drug.Id))
			.ToDictionaryAsync(drug => drug.Id, drug => drug.Name);
		foreach (PurchaseItem item in items)
		{
			DetailLines.Add(new PurchaseInvoiceHistoryLine
			{
				MedicineName = drugs.GetValueOrDefault(item.DrugId, "Medicine"),
				BatchNo = item.BatchNo,
				Expiry = item.ExpiryDate?.ToString("MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
				Quantity = item.Quantity,
				FreeQuantity = item.FreeQuantity,
				Rate = item.UnitPrice,
				LineTotal = item.LineTotal
			});
		}

		HasDetails = true;
	}

	private void ViewAttachment(PurchaseInvoiceHistoryRow? row)
	{
		if (row == null || string.IsNullOrWhiteSpace(row.AttachmentPath) || !File.Exists(row.AttachmentPath))
		{
			StatusMessage = "No attachment is stored for this invoice.";
			return;
		}

		Process.Start(new ProcessStartInfo(row.AttachmentPath) { UseShellExecute = true });
	}

	private async Task AttachAsync(PurchaseInvoiceHistoryRow? row)
	{
		if (row == null)
		{
			return;
		}

		string? source = filePicker.PickPurchaseDocument();
		if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
		{
			return;
		}

		string destination = PurchaseAttachmentStore.Copy(source, row.InvoiceDate.Year, row.SupplierId);
		using IServiceScope scope = scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		PurchaseInvoice? invoice = await context.PurchaseInvoices.FirstOrDefaultAsync(item => item.Id == row.InvoiceId);
		if (invoice == null)
		{
			StatusMessage = "That purchase invoice is no longer available.";
			return;
		}

		try
		{
			await RecordLockUi.RunAsync(scope.ServiceProvider, async () =>
			{
				RecordLockGuard.Demand(invoice.CreatedAtUtc, "PurchaseInward", invoice.Id, "MODIFIED_AFTER_LOCK");
				string oldSnapshot = ComplianceAuditWriter.Snapshot(new { invoice.InvoiceNo, invoice.AttachedInvoicePath, invoice.TotalAmount, invoice.Status });
				invoice.AttachedInvoicePath = destination;
				invoice.OriginalFileName = Path.GetFileName(source);
				await ComplianceAuditWriter.AppendIfUnlockedAsync(context, invoice.CreatedAtUtc, new ComplianceAuditEntry
				{
					EntityType = "PurchaseInward",
					EntityId = invoice.Id,
					Action = "MODIFIED_AFTER_LOCK",
					AuthorizedBy = string.Empty,
					Reason = "Attachment update",
					OldSnapshotJson = oldSnapshot,
					NewSnapshotJson = ComplianceAuditWriter.Snapshot(new { invoice.InvoiceNo, invoice.AttachedInvoicePath, invoice.OriginalFileName, invoice.TotalAmount })
				});
				await context.SaveChangesAsync();
			});
			StatusMessage = "Attachment saved with " + invoice.InvoiceNo + ".";
			await LoadAsync();
		}
		catch (Exception ex)
		{
			StatusMessage = ex.Message;
		}
	}
}

public sealed record SupplierFilterChoice(Guid? Id, string Name);

public sealed class PurchaseInvoiceHistoryRow
{
	public Guid InvoiceId { get; init; }

	public DateOnly InvoiceDate { get; init; }

	public string InvoiceDateText => InvoiceDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

	public Guid SupplierId { get; init; }

	public string SupplierName { get; init; } = string.Empty;

	public string InvoiceNo { get; init; } = string.Empty;

	public int ItemCount { get; init; }

	public decimal TotalAmount { get; init; }

	public string PaymentStatus { get; init; } = "Unpaid";

	public string? AttachmentPath { get; init; }

	public string? OriginalFileName { get; init; }

	public DateTime CreatedAtUtc { get; init; }

	public bool IsAgeLocked => RecordLockPolicy.IsLocked(CreatedAtUtc);

	public string AuditStatus => IsAgeLocked ? "🔒 Locked" : RecordLockPolicy.EditableBadge;

	public string AuditToolTip => IsAgeLocked ? RecordLockPolicy.LockedToolTip : RecordLockPolicy.EditableBadge;

	public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentPath);

	public bool MissingAttachment => !HasAttachment;
}

public sealed class PurchaseInvoiceHistoryLine
{
	public string MedicineName { get; init; } = string.Empty;

	public string BatchNo { get; init; } = string.Empty;

	public string Expiry { get; init; } = string.Empty;

	public decimal Quantity { get; init; }

	public decimal FreeQuantity { get; init; }

	public decimal Rate { get; init; }

	public decimal LineTotal { get; init; }
}

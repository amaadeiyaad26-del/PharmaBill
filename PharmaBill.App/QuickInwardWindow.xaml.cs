using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App;

public partial class QuickInwardWindow : Window
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly CurrentSession _session;
	private readonly Guid? _drugId;
	private readonly Guid? _catalogMedicineId;
	private readonly string _medicineName;
	private readonly string? _composition;
	private readonly string? _manufacturer;

	public AddStockResult? Result { get; private set; }
	public decimal QuantityEntered { get; private set; }
	public decimal PtrEntered { get; private set; }

	public QuickInwardWindow(
		IServiceScopeFactory scopeFactory,
		CurrentSession session,
		string medicineName,
		Guid? drugId,
		Guid? catalogMedicineId,
		string? composition,
		string? manufacturer,
		decimal? suggestedMrp,
		decimal? purchaseRate = null,
		string? defaultBatchNo = null,
		decimal? defaultQuantity = null,
		string? notice = null)
	{
		_scopeFactory = scopeFactory;
		_session = session;
		_drugId = drugId;
		_catalogMedicineId = catalogMedicineId;
		_medicineName = medicineName?.Trim() ?? string.Empty;
		_composition = composition;
		_manufacturer = manufacturer;
		InitializeComponent();
		MedicineTitleText.Text = _medicineName;
		if (!string.IsNullOrWhiteSpace(notice))
		{
			NoticeText.Text = notice.Trim();
		}
		if (suggestedMrp is > 0m)
		{
			MrpBox.Text = suggestedMrp.Value.ToString("0.##", CultureInfo.InvariantCulture);
			PtrBox.Text = suggestedMrp.Value.ToString("0.##", CultureInfo.InvariantCulture);
		}

		if (purchaseRate is > 0m)
		{
			PurchaseRateBox.Text = purchaseRate.Value.ToString("0.##", CultureInfo.InvariantCulture);
		}
		else if (suggestedMrp is > 0m)
		{
			PurchaseRateBox.Text = suggestedMrp.Value.ToString("0.##", CultureInfo.InvariantCulture);
		}

		if (!string.IsNullOrWhiteSpace(defaultBatchNo))
		{
			BatchNoBox.Text = defaultBatchNo.Trim();
		}

		if (defaultQuantity is > 0m)
		{
			QuantityBox.Text = defaultQuantity.Value.ToString("0.##", CultureInfo.InvariantCulture);
		}

		if (!string.IsNullOrWhiteSpace(defaultBatchNo))
		{
			DateTime expiry = DateTime.Today.AddYears(1);
			ExpiryBox.Text = expiry.ToString("MM/yyyy", CultureInfo.InvariantCulture);
			ExpiryBox.Focus();
			ExpiryBox.SelectAll();
		}
		else
		{
			BatchNoBox.Focus();
			BatchNoBox.SelectAll();
		}
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}

	private async void Save_Click(object sender, RoutedEventArgs e)
	{
		StatusText.Text = string.Empty;
		try
		{
			string batchNo = BatchNoBox.Text?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(batchNo))
			{
				StatusText.Text = "Batch number is required.";
				return;
			}

			if (!TryParseExpiry(ExpiryBox.Text, out DateOnly expiry))
			{
				StatusText.Text = "Enter expiry as MM/YYYY (e.g. 08/2027).";
				return;
			}

			if (!TryAmount(MrpBox.Text, out decimal mrp) || mrp <= 0m)
			{
				StatusText.Text = "Enter a valid MRP.";
				return;
			}

			if (!TryAmount(PurchaseRateBox.Text, out decimal purchaseRate) || purchaseRate < 0m)
			{
				StatusText.Text = "Enter a valid purchase rate.";
				return;
			}

			if (!TryAmount(PtrBox.Text, out decimal ptr) || ptr <= 0m)
			{
				StatusText.Text = "Enter a valid wholesale rate / PTR.";
				return;
			}

			if (ptr > mrp)
			{
				StatusText.Text = "PTR cannot exceed MRP.";
				return;
			}

			if (!TryAmount(QuantityBox.Text, out decimal qty) || qty <= 0m)
			{
				StatusText.Text = "Enter quantity received greater than zero.";
				return;
			}

			if (!TryAmount(PackSizeBox.Text, out decimal packSize) || packSize < 0m)
			{
				packSize = 1m;
			}

			string schedule = (ScheduleBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim() ?? "G";
			AppUser user = _session.User ?? throw new UnauthorizedAccessException("Sign in before adding stock.");

			using IServiceScope scope = _scopeFactory.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			AddStockService addStock = scope.ServiceProvider.GetRequiredService<AddStockService>();
			Guid supplierId = await EnsureQuickInwardSupplierAsync(context);

			AddStockResult result = await addStock.AddStockAsync(new AddStockInput(
				_drugId,
				_catalogMedicineId,
				_medicineName,
				_composition,
				_manufacturer,
				schedule,
				batchNo,
				expiry,
				mrp,
				purchaseRate,
				qty,
				0m,
				packSize,
				0m,
				supplierId,
				"QI-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
				DateOnly.FromDateTime(DateTime.Today),
				null), user.Id, user.Role);

			Batch batch = await context.Batches.SingleAsync(b => b.Id == result.BatchId);
			batch.Ptr = ptr;
			batch.PurchasePrice = purchaseRate;
			batch.Mrp = mrp;
			await context.SaveChangesAsync();

			Result = result;
			QuantityEntered = qty;
			PtrEntered = ptr;
			DialogResult = true;
			Close();
		}
		catch (DbUpdateException dbEx)
		{
			string message = dbEx.InnerException != null ? dbEx.InnerException.Message : dbEx.Message;
			StatusText.Text = $"Database error: {message}";
			System.Diagnostics.Debug.WriteLine($"[QuickInward Save Error]: {dbEx}");
		}
		catch (Exception ex)
		{
			StatusText.Text = ex.Message;
		}
	}

	private static async Task<Guid> EnsureQuickInwardSupplierAsync(PharmaBillDbContext context)
	{
		Supplier? existing = await context.Suppliers.AsNoTracking()
			.Where(s => s.IsActive && s.Name == "Quick Inward (Wholesale)")
			.FirstOrDefaultAsync();
		if (existing != null)
		{
			return existing.Id;
		}

		Supplier supplier = new Supplier
		{
			Name = "Quick Inward (Wholesale)",
			IsActive = true
		};
		context.Suppliers.Add(supplier);
		await context.SaveChangesAsync();
		return supplier.Id;
	}

	private static bool TryParseExpiry(string? text, out DateOnly expiry)
	{
		expiry = default;
		string raw = (text ?? string.Empty).Trim();
		if (DateTime.TryParseExact(raw, new[] { "MM/yyyy", "M/yyyy", "MM/yy", "M/yy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
		{
			expiry = new DateOnly(dt.Year, dt.Month, DateTime.DaysInMonth(dt.Year, dt.Month));
			return true;
		}

		return false;
	}

	private static bool TryAmount(string? text, out decimal value)
	{
		return decimal.TryParse((text ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value)
			|| decimal.TryParse((text ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
	}
}
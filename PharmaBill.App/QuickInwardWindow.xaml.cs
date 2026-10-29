using PharmaBill.Core;
using System.Globalization;
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
			// Never a made-up date: the cursor waits in Expiry for the date on the pack (or the tick).
			ExpiryBox.Text = string.Empty;
			ExpiryBox.Focus();
		}
		else
		{
			BatchNoBox.Focus();
			BatchNoBox.SelectAll();
		}
		Loaded += async (_, _) => await SelectSavedScheduleAsync();
	}

	// An existing medicine opens on the schedule it is saved with, not on G: another schedule needs an Admin override.
	private async Task SelectSavedScheduleAsync()
	{
		if (_drugId == null && _catalogMedicineId == null)
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			string? saved = await scope.ServiceProvider.GetRequiredService<AddStockService>().SavedScheduleAsync(_drugId, _catalogMedicineId);
			ComboBoxItem? item = ScheduleBox.Items.OfType<ComboBoxItem>().FirstOrDefault(option => string.Equals(option.Content as string, saved, StringComparison.OrdinalIgnoreCase));
			if (item != null)
			{
				ScheduleBox.SelectedItem = item;
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine("[QuickInward] saved schedule not loaded: " + ex.Message);
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

			DateOnly? expiry = null;
			if (ExpiryBox.Text.Trim().Length > 0)
			{
				if (!PharmaBill.Core.Inventory.ExpiryRule.TryParseTyped(ExpiryBox.Text, out DateOnly parsed))
				{
					StatusText.Text = "Type the expiry as MM/YY, for example 10/27.";
					return;
				}
				expiry = parsed;
			}
			else if (NoExpiryBox.IsChecked != true)
			{
				StatusText.Text = "Type the expiry from the pack (MM/YY), or tick \"No expiry printed on pack\".";
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

			AddStockInput? input = await NewProductTaxPrompt.CompleteAsync(addStock, scope.ServiceProvider.GetRequiredService<IPromptService>(), new AddStockInput(
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
				"QI-" + BusinessClock.UtcNow.ToString("yyyyMMddHHmmss"),
				BusinessClock.Today,
				null,
				NoExpiryPrinted: expiry == null && NoExpiryBox.IsChecked == true));
			if (input == null)
			{
				StatusText.Text = _medicineName + " is a new medicine and needs its HSN code and GST rate. Stock was not added.";
				return;
			}

			AddStockResult result = await ScheduleOverrideFlow.AddStockAsync(addStock, input, user, scope.ServiceProvider.GetService<IScheduleOverridePrompt>());

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
			StatusText.Text = PharmaBill.Core.UserMessage.Of(ex);
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

	private static bool TryAmount(string? text, out decimal value)
	{
		return decimal.TryParse((text ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value)
			|| decimal.TryParse((text ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
	}
}
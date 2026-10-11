using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PharmaBill.App;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class PrescriptionReviewViewModel : ObservableObject
{
	private readonly IServiceScopeFactory? _scopes;

	private readonly CurrentSession? _session;

	private string _errorMessage = string.Empty;

	public string Summary { get; }

	public bool ApiKeyMissing { get; }

	public bool OpenSettingsRequested { get; set; }

	public Window? OwnerWindow { get; set; }

	public ObservableCollection<PrescriptionReviewRow> Rows { get; } = new ObservableCollection<PrescriptionReviewRow>();

	public IAsyncRelayCommand<PrescriptionReviewRow> LinkCommand { get; }

	public IAsyncRelayCommand<PrescriptionReviewRow> InwardCommand { get; }

	public IAsyncRelayCommand InwardMissingCommand { get; }

	public string ErrorMessage
	{
		get
		{
			return _errorMessage;
		}
		private set
		{
			SetProperty(ref _errorMessage, value);
		}
	}

	public PrescriptionReviewViewModel(PrescriptionParseResultDto result, IReadOnlyDictionary<Guid, RetailStockChoice> fefoChoices, IReadOnlyDictionary<Guid, string> matchedNames, IServiceScopeFactory? scopes = null, CurrentSession? session = null)
	{
		_scopes = scopes;
		_session = session;
		ApiKeyMissing = result.ApiKeyMissing;
		Summary = $"{result.Source}: {result.DetectedMedicines.Count} medicine(s) detected. {result.Message}".Trim();
		foreach (PrescribedItemDto detectedMedicine in result.DetectedMedicines)
		{
			RetailStockChoice value = null;
			string value2 = null;
			Guid? matchedCatalogProductId = detectedMedicine.MatchedCatalogProductId;
			if (matchedCatalogProductId.HasValue)
			{
				Guid valueOrDefault = matchedCatalogProductId.GetValueOrDefault();
				fefoChoices.TryGetValue(valueOrDefault, out value);
				matchedNames.TryGetValue(valueOrDefault, out value2);
			}
			Rows.Add(new PrescriptionReviewRow(detectedMedicine, value, value2));
		}

		LinkCommand = new AsyncRelayCommand<PrescriptionReviewRow>(LinkAsync);
		InwardCommand = new AsyncRelayCommand<PrescriptionReviewRow>(InwardCommandAsync);
		InwardMissingCommand = new AsyncRelayCommand(InwardMissingAsync);
	}

	public string? Validate()
	{
		foreach (PrescriptionReviewRow item in Rows.Where((PrescriptionReviewRow row) => row.Include && row.IsMatched))
		{
			if (!item.IsValid)
			{
				return "Check the quantity for " + item.MatchText + ": it must be above zero and within stock.";
			}
		}
		return null;
	}

	public async Task<bool> TryAcceptAsync()
	{
		string? quantityError = Validate();
		if (quantityError != null)
		{
			ErrorMessage = quantityError;
			return false;
		}

		List<PrescriptionReviewRow> unmatched = Rows.Where((PrescriptionReviewRow row) => row.Include && row.IsUnmatched).ToList();
		if (unmatched.Count > 0)
		{
			bool? choice = Ask(
				"Some items are not mapped to current inventory. Would you like to map them or add them to stock now?",
				"Unmatched medicines",
				"Map or add to stock",
				"Add as counter items");
			if (choice == null)
			{
				return false;
			}

			if (choice == true)
			{
				ErrorMessage = "Use Link to Stock or + Inward Stock on each unmatched row, then Add to Bill.";
				return false;
			}

			if (!await CreateCounterItemsAsync(unmatched))
			{
				return false;
			}

			quantityError = Validate();
			if (quantityError != null)
			{
				ErrorMessage = quantityError;
				return false;
			}
		}

		if (!Rows.Any((PrescriptionReviewRow row) => row.Include && row.IsValid))
		{
			ErrorMessage = "Tick at least one matched medicine to add to the bill.";
			return false;
		}

		ErrorMessage = string.Empty;
		return true;
	}

	public IReadOnlyList<ConfirmedPrescriptionItem> GetConfirmedItems()
	{
		return (from row in Rows
			where row.Include && row.IsValid
			select new ConfirmedPrescriptionItem(row.Choice!, row.Quantity)).ToList();
	}

	private async Task LinkAsync(PrescriptionReviewRow? row)
	{
		if (row == null)
		{
			return;
		}

		if (_scopes == null)
		{
			ErrorMessage = "Stock search is not available from this window.";
			return;
		}

		PrescriptionLinkStockWindow window = new PrescriptionLinkStockWindow(_scopes, row.PrescribedName)
		{
			Owner = OwnerWindow
		};
		if (window.ShowDialog() != true || window.Selected == null)
		{
			return;
		}

		StockLinkChoice selected = window.Selected;
		if (selected.Stock != null)
		{
			row.ApplyStock(selected.Stock);
			ErrorMessage = string.Empty;
			return;
		}

		if (selected.DrugId is Guid drugId)
		{
			row.ApplyLink(drugId, selected.CatalogMedicineId, selected.Name);
			ErrorMessage = selected.Name + " is linked. Use + Inward Stock to put a batch on the shelf.";
		}
	}

	private Task InwardCommandAsync(PrescriptionReviewRow? row)
	{
		return InwardAsync(row);
	}

	private async Task<bool> InwardAsync(PrescriptionReviewRow? row)
	{
		if (row == null)
		{
			return false;
		}

		PrescriptionOpeningStockWindow window = new PrescriptionOpeningStockWindow(row.MatchedName ?? row.PrescribedName, row.Quantity > 0m ? row.Quantity : 1m)
		{
			Owner = OwnerWindow
		};
		if (window.ShowDialog() != true || window.Draft == null)
		{
			return false;
		}

		try
		{
			RetailStockChoice? choice = await SaveOpeningAsync(window.Draft, row.LinkedDrugId, row.LinkedCatalogMedicineId);
			if (choice == null)
			{
				ErrorMessage = "The batch was saved, but it is not saleable yet. Check expiry and quantity.";
				return false;
			}

			row.ApplyStock(choice);
			ErrorMessage = string.Empty;
			return true;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
			return false;
		}
	}

	private async Task InwardMissingAsync()
	{
		foreach (PrescriptionReviewRow row in Rows.Where((PrescriptionReviewRow item) => item.IsUnmatched).ToList())
		{
			if (!await InwardAsync(row))
			{
				return;
			}
		}
	}

	private async Task<bool> CreateCounterItemsAsync(IReadOnlyList<PrescriptionReviewRow> rows)
	{
		DateTime expiryMonth = DateTime.Today.AddYears(1);
		DateOnly expiry = new DateOnly(expiryMonth.Year, expiryMonth.Month, DateTime.DaysInMonth(expiryMonth.Year, expiryMonth.Month));
		foreach (PrescriptionReviewRow row in rows)
		{
			decimal quantity = row.Quantity > 0m ? row.Quantity : 1m;
			OpeningStockDraft draft = new OpeningStockDraft(row.PrescribedName, "OPENING", expiry, 0m, 0m, quantity);
			try
			{
				RetailStockChoice? choice = await SaveOpeningAsync(draft, row.LinkedDrugId, row.LinkedCatalogMedicineId);
				if (choice == null)
				{
					ErrorMessage = row.PrescribedName + " was saved, but the counter batch is not saleable yet.";
					return false;
				}

				row.ApplyStock(choice);
			}
			catch (Exception ex)
			{
				ErrorMessage = ex.Message;
				return false;
			}
		}

		return true;
	}

	private async Task<RetailStockChoice?> SaveOpeningAsync(OpeningStockDraft draft, Guid? drugId, Guid? catalogId)
	{
		if (_scopes == null || _session?.User == null)
		{
			throw new InvalidOperationException("Sign in before adding stock.");
		}

		AppUser user = _session.User;
		using IServiceScope scope = _scopes.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		Guid supplierId = await EnsureQuickInwardSupplierAsync(context);
		string schedule = "G";
		decimal gst = 0m;
		if (drugId is Guid existingId)
		{
			Drug? drug = await context.Drugs.AsNoTracking().FirstOrDefaultAsync((Drug item) => item.Id == existingId && item.IsActive);
			if (drug != null)
			{
				string normalized = drug.Schedule?.Trim().ToUpperInvariant() ?? string.Empty;
				if (normalized.Length > 0 && AddStockService.Schedules.Contains(normalized))
				{
					schedule = normalized;
				}

				gst = drug.GstRate ?? 0m;
			}
		}

		AddStockService addStock = scope.ServiceProvider.GetRequiredService<AddStockService>();
		AddStockResult result = await addStock.AddStockAsync(new AddStockInput(
			drugId,
			catalogId,
			draft.BrandName,
			null,
			null,
			schedule,
			draft.BatchNo,
			draft.Expiry,
			draft.Mrp,
			draft.PurchaseRate,
			draft.Quantity,
			0m,
			1m,
			gst,
			supplierId,
			"RX" + Guid.NewGuid().ToString("N")[..12],
			DateOnly.FromDateTime(DateTime.Today),
			null), user.Id, user.Role);
		string name = await context.Drugs.AsNoTracking().Where((Drug item) => item.Id == result.DrugId).Select((Drug item) => item.Name).FirstAsync();
		IReadOnlyList<RetailStockChoice> choices = await scope.ServiceProvider.GetRequiredService<RetailBillingService>().SearchStockAsync(name);
		return choices.FirstOrDefault((RetailStockChoice choice) => choice.BatchId == result.BatchId)
			?? choices.FirstOrDefault((RetailStockChoice choice) => choice.DrugId == result.DrugId);
	}

	private static async Task<Guid> EnsureQuickInwardSupplierAsync(PharmaBillDbContext context)
	{
		Supplier? existing = await context.Suppliers.AsNoTracking()
			.Where((Supplier supplier) => supplier.IsActive && supplier.Name == "Quick Inward (Wholesale)")
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

	private bool? Ask(string message, string title, string acceptLabel, string alternateLabel)
	{
		bool? choice = null;
		Window window = new Window
		{
			Title = title,
			MinWidth = 420,
			MaxWidth = 560,
			SizeToContent = SizeToContent.WidthAndHeight,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			ResizeMode = ResizeMode.NoResize
		};
		if (OwnerWindow != null && OwnerWindow.IsLoaded)
		{
			window.Owner = OwnerWindow;
		}

		StackPanel panel = new StackPanel { Margin = new Thickness(20) };
		panel.Children.Add(new TextBlock
		{
			Text = message,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0, 0, 0, 16)
		});
		Button accept = new Button { Content = acceptLabel, MinWidth = 280, Margin = new Thickness(0, 0, 0, 8), IsDefault = true, Padding = new Thickness(12, 8, 12, 8) };
		Button alternate = new Button { Content = alternateLabel, MinWidth = 280, Padding = new Thickness(12, 8, 12, 8) };
		accept.Click += (_, _) =>
		{
			choice = true;
			window.Close();
		};
		alternate.Click += (_, _) =>
		{
			choice = false;
			window.Close();
		};
		panel.Children.Add(accept);
		panel.Children.Add(alternate);
		window.Content = panel;
		window.ShowDialog();
		return choice;
	}
}

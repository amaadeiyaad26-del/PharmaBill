using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App;

public partial class PrescriptionLinkStockWindow : Window
{
	private readonly IServiceScopeFactory _scopes;

	public StockLinkChoice? Selected { get; private set; }

	public PrescriptionLinkStockWindow(IServiceScopeFactory scopes, string initialQuery)
	{
		_scopes = scopes;
		InitializeComponent();
		QueryBox.Text = initialQuery?.Trim() ?? string.Empty;
		Loaded += async (_, _) => await SearchAsync();
	}

	private async void Search_Click(object sender, RoutedEventArgs e)
	{
		await SearchAsync();
	}

	private async void QueryBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			e.Handled = true;
			await SearchAsync();
		}
	}

	private void Use_Click(object sender, RoutedEventArgs e)
	{
		if (ResultsList.SelectedItem is not StockLinkChoice choice)
		{
			StatusText.Text = "Select a brand or batch.";
			return;
		}

		Selected = choice;
		DialogResult = true;
	}

	private async Task SearchAsync()
	{
		string term = QueryBox.Text.Trim();
		StatusText.Text = string.Empty;
		if (term.Length < 2)
		{
			StatusText.Text = "Type at least 2 letters.";
			return;
		}

		try
		{
			using IServiceScope scope = _scopes.CreateScope();
			RetailBillingService billing = scope.ServiceProvider.GetRequiredService<RetailBillingService>();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			IReadOnlyList<RetailStockChoice> stock = await billing.SearchStockAsync(term);
			List<Drug> drugs = await context.Drugs.AsNoTracking()
				.Where((Drug drug) => drug.IsActive && (EF.Functions.Like(drug.Name, $"%{term}%") || EF.Functions.Like(drug.BrandName ?? string.Empty, $"%{term}%") || EF.Functions.Like(drug.GenericName ?? string.Empty, $"%{term}%")))
				.OrderBy((Drug drug) => drug.Name)
				.Take(40)
				.ToListAsync();
			List<StockLinkChoice> rows = new List<StockLinkChoice>();
			foreach (RetailStockChoice choice in stock)
			{
				rows.Add(new StockLinkChoice
				{
					Title = choice.DrugName,
					Detail = $"{choice.BatchNo}  exp {(choice.ExpiryDate.HasValue ? choice.ExpiryDate.Value.ToString("MM/yyyy") : "-")}  stock {choice.AvailableQuantity:0.##}",
					Stock = choice,
					DrugId = choice.DrugId,
					Name = choice.DrugName
				});
			}

			foreach (Drug drug in drugs)
			{
				if (rows.Any((StockLinkChoice row) => row.DrugId == drug.Id && row.Stock != null))
				{
					continue;
				}

				if (rows.Any((StockLinkChoice row) => row.DrugId == drug.Id && row.Stock == null))
				{
					continue;
				}

				if (stock.Any((RetailStockChoice choice) => choice.DrugId == drug.Id))
				{
					continue;
				}

				rows.Add(new StockLinkChoice
				{
					Title = drug.Name,
					Detail = "Not in stock — link, then inward a batch",
					DrugId = drug.Id,
					CatalogMedicineId = drug.CatalogMedicineId,
					Name = drug.Name
				});
			}

			ResultsList.ItemsSource = rows;
			if (rows.Count == 0)
			{
				StatusText.Text = "No matching brand. Use + Inward Stock to create it.";
			}
		}
		catch (Exception ex)
		{
			StatusText.Text = ex.Message;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>
/// Statutory GST compliance engine: GSTR-1 tables, GSTR-2/2B ITC recon, GSTR-3B offset,
/// e-Invoice (schema v1.03) and e-Way Bill Part-A/B payloads.
/// </summary>
public sealed class GstService(PharmaBillDbContext context, GstReturnExportService exportService)
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNamingPolicy = null
	};

	private static readonly decimal[] B2cSlabs = [0m, 5m, 12m, 18m];

	public Task<string> ExportGstr1OfflineJsonAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
		=> exportService.ExportGstr1OfflineJsonAsync(from, to, cancellationToken);

	public Task<IReadOnlyList<Gstr3bWorksheetRow>> GetGstr3bWorksheetAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
		=> exportService.GetGstr3bWorksheetAsync(from, to, cancellationToken);

	public Task<GstReturnDashboardSummary> GetDashboardSummaryAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
		=> exportService.GetDashboardSummaryAsync(from, to, cancellationToken);

	public async Task<Gstr1Tables> BuildGstr1TablesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
	{
		ValidatePeriod(from, to);
		PharmacyProfile pharmacy = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
		string pharmacyPos = IndianGstStateCodes.Resolve(pharmacy.State, pharmacy.Gstin);
		IReadOnlyList<GstSaleDocument> documents = await LoadSaleDocumentsAsync(from, to, cancellationToken);

		List<Gstr1B2bRow> b2b = documents
			.Where(d => !string.IsNullOrWhiteSpace(d.CounterpartyGstin) && GstinValidator.IsValid(d.CounterpartyGstin))
			.Select(d => new Gstr1B2bRow(
				GstinValidator.Normalize(d.CounterpartyGstin!),
				d.InvoiceNo,
				d.InvoiceDate,
				Round(d.Taxable),
				Round(d.Cgst),
				Round(d.Sgst),
				Round(d.Igst),
				Round(d.InvoiceValue),
				d.PlaceOfSupply))
			.OrderBy(r => r.InvoiceDate)
			.ThenBy(r => r.InvoiceNo, StringComparer.OrdinalIgnoreCase)
			.ToList();

		var b2cLines = documents
			.Where(d => string.IsNullOrWhiteSpace(d.CounterpartyGstin) || !GstinValidator.IsValid(d.CounterpartyGstin))
			.SelectMany(d => d.Lines.Select(line => new { line.Rate, line.Taxable, line.Cgst, line.Sgst, line.Igst, d.InvoiceNo }))
			.ToList();

		List<Gstr1B2cSlabRow> b2c = B2cSlabs.Select(slab =>
		{
			var rows = b2cLines.Where(line => NearestSlab(line.Rate) == slab).ToList();
			return new Gstr1B2cSlabRow(
				slab,
				Round(rows.Sum(r => r.Taxable)),
				Round(rows.Sum(r => r.Cgst)),
				Round(rows.Sum(r => r.Sgst)),
				Round(rows.Sum(r => r.Igst)),
				rows.Select(r => r.InvoiceNo).Distinct(StringComparer.OrdinalIgnoreCase).Count());
		}).ToList();

		List<Gstr1HsnRow> hsn = documents
			.SelectMany(d => d.Lines)
			.GroupBy(line => new
			{
				Hsn = string.IsNullOrWhiteSpace(line.HsnCode) ? "NOHSN" : line.HsnCode.Trim(),
				Rate = line.Rate
			})
			.OrderBy(g => g.Key.Hsn, StringComparer.OrdinalIgnoreCase)
			.ThenBy(g => g.Key.Rate)
			.Select(g => new Gstr1HsnRow(
				g.Key.Hsn,
				Round(g.Sum(x => x.Quantity)),
				Round(g.Sum(x => x.Taxable)),
				Round(g.Sum(x => x.Cgst)),
				Round(g.Sum(x => x.Sgst)),
				Round(g.Sum(x => x.Igst)),
				g.Key.Rate))
			.ToList();

		_ = pharmacyPos;
		return new Gstr1Tables(b2b, b2c, hsn);
	}

	public async Task<IReadOnlyList<Gstr2PurchaseItcRow>> GetPurchaseItcAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
	{
		ValidatePeriod(from, to);
		List<PurchaseInvoice> invoices = await context.PurchaseInvoices.AsNoTracking()
			.Where(invoice =>
				(invoice.Status == PurchaseInvoice.PostedStatus || invoice.Status == PurchaseInvoice.CommittedStatus)
				&& invoice.InvoiceDate >= from
				&& invoice.InvoiceDate <= to)
			.ToListAsync(cancellationToken);
		Guid[] invoiceIds = invoices.Select(i => i.Id).ToArray();
		List<PurchaseItem> items = await context.PurchaseItems.AsNoTracking()
			.Where(item => invoiceIds.Contains(item.PurchaseInvoiceId))
			.ToListAsync(cancellationToken);
		Dictionary<Guid, Supplier> suppliers = await context.Suppliers.AsNoTracking()
			.ToDictionaryAsync(s => s.Id, cancellationToken);
		PharmacyProfile pharmacy = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
		string pharmacyPos = IndianGstStateCodes.Resolve(pharmacy.State, pharmacy.Gstin);

		List<Gstr2PurchaseItcRow> rows = new();
		foreach (PurchaseInvoice invoice in invoices.OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNo))
		{
			Supplier supplier = suppliers.GetValueOrDefault(invoice.SupplierId) ?? new Supplier { Name = "Unknown" };
			List<PurchaseItem> invoiceItems = items.Where(i => i.PurchaseInvoiceId == invoice.Id).ToList();
			decimal taxable = 0m, cgst = 0m, sgst = 0m, igst = 0m;
			string supplierPos = IndianGstStateCodes.Resolve(null, supplier.Gstin);
			bool interState = !string.IsNullOrWhiteSpace(supplier.Gstin)
				&& !string.Equals(supplierPos, pharmacyPos, StringComparison.Ordinal);
			foreach (PurchaseItem item in invoiceItems)
			{
				decimal lineTaxable = Round(item.Quantity * item.UnitPrice - item.DiscountAmount);
				decimal tax = Round(lineTaxable * item.TaxRate / 100m);
				taxable += lineTaxable;
				if (interState)
				{
					igst += tax;
				}
				else
				{
					decimal half = Round(tax / 2m);
					cgst += half;
					sgst += tax - half;
				}
			}

			rows.Add(new Gstr2PurchaseItcRow(
				supplier.Name,
				supplier.Gstin,
				invoice.InvoiceNo,
				invoice.InvoiceDate,
				Round(taxable),
				Round(cgst),
				Round(sgst),
				Round(igst),
				Round(invoice.TotalAmount)));
		}

		return rows;
	}

	public async Task<IReadOnlyList<Gstr2bReconRow>> ReconcileGstr2bAsync(
		DateOnly from,
		DateOnly to,
		IReadOnlyList<Gstr2bImportedRow> portalRows,
		CancellationToken cancellationToken = default)
	{
		IReadOnlyList<Gstr2PurchaseItcRow> books = await GetPurchaseItcAsync(from, to, cancellationToken);
		Dictionary<string, Gstr2PurchaseItcRow> bookMap = books
			.GroupBy(r => NormalizeInvoiceKey(r.SupplierGstin, r.InvoiceNo), StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
		Dictionary<string, Gstr2bImportedRow> portalMap = portalRows
			.GroupBy(r => NormalizeInvoiceKey(r.SupplierGstin, r.InvoiceNo), StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

		HashSet<string> keys = new(bookMap.Keys, StringComparer.OrdinalIgnoreCase);
		foreach (string key in portalMap.Keys)
		{
			keys.Add(key);
		}

		List<Gstr2bReconRow> result = new();
		foreach (string key in keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
		{
			bookMap.TryGetValue(key, out Gstr2PurchaseItcRow? book);
			portalMap.TryGetValue(key, out Gstr2bImportedRow? portal);
			decimal booksItc = book is null ? 0m : Round(book.Cgst + book.Sgst + book.Igst);
			decimal portalItc = portal is null ? 0m : Round(portal.Cgst + portal.Sgst + portal.Igst);
			string status;
			string notes;
			if (book is null)
			{
				status = "Portal only";
				notes = "Present in supplier GSTR-2B but missing from PharmaBill inward books.";
			}
			else if (portal is null)
			{
				status = "Books only";
				notes = "Inwarded in PharmaBill but not found in imported GSTR-2B.";
			}
			else if (Math.Abs(book.TaxableValue - portal.TaxableValue) <= 1m && Math.Abs(booksItc - portalItc) <= 1m)
			{
				status = "Matched";
				notes = "Invoice and ITC align within ₹1.";
			}
			else
			{
				status = "Mismatch";
				notes = $"Taxable Δ {Round(book.TaxableValue - portal.TaxableValue):0.00}; ITC Δ {Round(booksItc - portalItc):0.00}.";
			}

			result.Add(new Gstr2bReconRow(
				book?.InvoiceNo ?? portal!.InvoiceNo,
				book?.InvoiceDate,
				portal?.InvoiceDate,
				book?.SupplierGstin ?? portal?.SupplierGstin,
				book?.TaxableValue ?? 0m,
				portal?.TaxableValue ?? 0m,
				booksItc,
				portalItc,
				status,
				notes));
		}

		return result;
	}

	public static IReadOnlyList<Gstr2bImportedRow> ParseGstr2bCsv(string csvText)
	{
		List<Gstr2bImportedRow> rows = new();
		string[] lines = csvText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
		foreach (string line in lines.Skip(1))
		{
			string[] parts = line.Split(',');
			if (parts.Length < 8)
			{
				continue;
			}

			if (!DateOnly.TryParse(parts[2].Trim().Trim('"'), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
				&& !DateOnly.TryParseExact(parts[2].Trim().Trim('"'), ["dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
			{
				continue;
			}

			rows.Add(new Gstr2bImportedRow(
				NullIfEmpty(parts[0]),
				parts[1].Trim().Trim('"'),
				date,
				ParseDecimal(parts[3]),
				ParseDecimal(parts[4]),
				ParseDecimal(parts[5]),
				ParseDecimal(parts[6]),
				ParseDecimal(parts[7])));
		}

		return rows;
	}

	public async Task<Gstr3bOffsetSummary> GetGstr3bOffsetAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
	{
		GstReturnDashboardSummary dash = await exportService.GetDashboardSummaryAsync(from, to, cancellationToken);
		IReadOnlyList<Gstr2PurchaseItcRow> itc = await GetPurchaseItcAsync(from, to, cancellationToken);
		decimal itcCgst = Round(itc.Sum(r => r.Cgst));
		decimal itcSgst = Round(itc.Sum(r => r.Sgst));
		decimal itcIgst = Round(itc.Sum(r => r.Igst));
		return new Gstr3bOffsetSummary(
			dash.TotalTaxableTurnover,
			dash.OutputCgst,
			dash.OutputSgst,
			dash.OutputIgst,
			itcCgst,
			itcSgst,
			itcIgst,
			Round(Math.Max(0m, dash.OutputCgst - itcCgst)),
			Round(Math.Max(0m, dash.OutputSgst - itcSgst)),
			Round(Math.Max(0m, dash.OutputIgst - itcIgst)),
			Round(Math.Max(0m, dash.OutputCgst + dash.OutputSgst + dash.OutputIgst - itcCgst - itcSgst - itcIgst)));
	}

	public async Task<EInvoicePayloadResult> BuildEInvoiceJsonAsync(Guid wholesaleInvoiceId, CancellationToken cancellationToken = default)
	{
		WholesaleInvoice invoice = await context.WholesaleInvoices.AsNoTracking()
			.SingleAsync(i => i.Id == wholesaleInvoiceId, cancellationToken);
		if (!string.Equals(invoice.Status, "Posted", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("e-Invoice JSON can only be generated for posted wholesale invoices.");
		}

		Customer customer = await context.Customers.AsNoTracking().SingleAsync(c => c.Id == invoice.CustomerId, cancellationToken);
		PharmacyProfile seller = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
		List<WholesaleInvoiceItem> items = await context.WholesaleInvoiceItems.AsNoTracking()
			.Where(i => i.WholesaleInvoiceId == invoice.Id)
			.ToListAsync(cancellationToken);
		Dictionary<Guid, Drug> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync(d => d.Id, cancellationToken);
		Dictionary<Guid, Batch> batches = await context.Batches.AsNoTracking().ToDictionaryAsync(b => b.Id, cancellationToken);

		List<object> itemList = new();
		int sl = 1;
		foreach (WholesaleInvoiceItem item in items)
		{
			Drug drug = drugs[item.DrugId];
			Batch batch = batches[item.BatchId];
			itemList.Add(new Dictionary<string, object?>
			{
				["SlNo"] = sl++.ToString(CultureInfo.InvariantCulture),
				["PrdDesc"] = drug.Name,
				["IsServc"] = "N",
				["HsnCd"] = drug.HsnCode ?? "3004",
				["Qty"] = item.Quantity,
				["Unit"] = string.IsNullOrWhiteSpace(drug.Unit) ? "NOS" : drug.Unit,
				["UnitPrice"] = item.UnitPrice,
				["TotAmt"] = Round(item.Quantity * item.UnitPrice),
				["Discount"] = item.DiscountAmount,
				["AssAmt"] = item.TaxableAmount,
				["GstRt"] = item.TaxRate,
				["IgstAmt"] = item.IgstAmount,
				["CgstAmt"] = item.CgstAmount,
				["SgstAmt"] = item.SgstAmount,
				["TotItemVal"] = item.LineTotal,
				["BchDtls"] = new Dictionary<string, object?>
				{
					["Nm"] = batch.BatchNo,
					["ExpDt"] = batch.ExpiryDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
				}
			});
		}

		Dictionary<string, object?> payload = new()
		{
			["Version"] = "1.03",
			["TranDtls"] = new Dictionary<string, object?>
			{
				["TaxSch"] = "GST",
				["SupTyp"] = "B2B",
				["RegRev"] = "N",
				["EcmGstin"] = null,
				["IgstOnIntra"] = "N"
			},
			["DocDtls"] = new Dictionary<string, object?>
			{
				["Typ"] = "INV",
				["No"] = invoice.InvoiceNo,
				["Dt"] = DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
			},
			["SellerDtls"] = new Dictionary<string, object?>
			{
				["Gstin"] = seller.Gstin ?? string.Empty,
				["LglNm"] = seller.LegalName ?? seller.Name,
				["Addr1"] = seller.Address ?? string.Empty,
				["Loc"] = seller.State ?? string.Empty,
				["Pin"] = 0,
				["Stcd"] = IndianGstStateCodes.Resolve(seller.State, seller.Gstin)
			},
			["BuyerDtls"] = new Dictionary<string, object?>
			{
				["Gstin"] = customer.Gstin ?? string.Empty,
				["LglNm"] = customer.Name,
				["Pos"] = IndianGstStateCodes.Resolve(customer.State, customer.Gstin),
				["Addr1"] = customer.Address ?? string.Empty,
				["Loc"] = customer.State ?? string.Empty,
				["Pin"] = 0,
				["Stcd"] = IndianGstStateCodes.Resolve(customer.State, customer.Gstin)
			},
			["ItemList"] = itemList,
			["ValDtls"] = new Dictionary<string, object?>
			{
				["AssVal"] = invoice.Subtotal,
				["CgstVal"] = invoice.CgstAmount,
				["SgstVal"] = invoice.SgstAmount,
				["IgstVal"] = invoice.IgstAmount,
				["Discount"] = invoice.DiscountAmount,
				["RndOffAmt"] = invoice.RoundOff,
				["TotInvVal"] = invoice.TotalAmount
			}
		};

		string json = JsonSerializer.Serialize(payload, JsonOptions);
		return new EInvoicePayloadResult(json, $"eInvoice_{invoice.InvoiceNo}.json");
	}

	public async Task<EWayBillPayloadResult> BuildEWayBillJsonAsync(
		Guid wholesaleInvoiceId,
		string? transporterId,
		string? vehicleNumber,
		decimal distanceKm,
		CancellationToken cancellationToken = default)
	{
		WholesaleInvoice invoice = await context.WholesaleInvoices.AsNoTracking()
			.SingleAsync(i => i.Id == wholesaleInvoiceId, cancellationToken);
		Customer customer = await context.Customers.AsNoTracking().SingleAsync(c => c.Id == invoice.CustomerId, cancellationToken);
		PharmacyProfile seller = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);

		Dictionary<string, object?> payload = new()
		{
			["version"] = "1.0.0621",
			["billLists"] = new object[]
			{
				new Dictionary<string, object?>
				{
					["userGstin"] = seller.Gstin ?? string.Empty,
					["supplyType"] = "O",
					["subSupplyType"] = "1",
					["docType"] = "INV",
					["docNo"] = invoice.InvoiceNo,
					["docDate"] = DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
					["fromGstin"] = seller.Gstin ?? string.Empty,
					["fromTrdName"] = seller.LegalName ?? seller.Name,
					["fromAddr1"] = seller.Address ?? string.Empty,
					["fromPincode"] = 0,
					["fromStateCode"] = int.TryParse(IndianGstStateCodes.Resolve(seller.State, seller.Gstin), out int fs) ? fs : 0,
					["toGstin"] = customer.Gstin ?? "URP",
					["toTrdName"] = customer.Name,
					["toAddr1"] = customer.Address ?? string.Empty,
					["toPincode"] = 0,
					["toStateCode"] = int.TryParse(IndianGstStateCodes.Resolve(customer.State, customer.Gstin), out int ts) ? ts : 0,
					["totalValue"] = invoice.Subtotal,
					["cgstValue"] = invoice.CgstAmount,
					["sgstValue"] = invoice.SgstAmount,
					["igstValue"] = invoice.IgstAmount,
					["totInvValue"] = invoice.TotalAmount,
					["transMode"] = "1",
					["transDistance"] = distanceKm <= 0m ? 1m : distanceKm,
					["transporterId"] = string.IsNullOrWhiteSpace(transporterId) ? null : transporterId.Trim(),
					["transDocNo"] = invoice.InvoiceNo,
					["vehicleNo"] = string.IsNullOrWhiteSpace(vehicleNumber)
						? (invoice.VehicleNumber ?? string.Empty)
						: vehicleNumber.Trim(),
					["vehicleType"] = "R",
					["partA"] = new Dictionary<string, object?>
					{
						["transporterId"] = string.IsNullOrWhiteSpace(transporterId) ? null : transporterId.Trim(),
						["docNo"] = invoice.InvoiceNo,
						["docDate"] = DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
					},
					["partB"] = new Dictionary<string, object?>
					{
						["vehicleNo"] = string.IsNullOrWhiteSpace(vehicleNumber)
							? (invoice.VehicleNumber ?? string.Empty)
							: vehicleNumber.Trim(),
						["transDistance"] = distanceKm <= 0m ? 1m : distanceKm
					}
				}
			}
		};

		string json = JsonSerializer.Serialize(payload, JsonOptions);
		return new EWayBillPayloadResult(json, $"eWayBill_{invoice.InvoiceNo}.json");
	}

	private async Task<IReadOnlyList<GstSaleDocument>> LoadSaleDocumentsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
	{
		DateTime startUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		DateTime endUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		PharmacyProfile pharmacy = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
		string pharmacyPos = IndianGstStateCodes.Resolve(pharmacy.State, pharmacy.Gstin);

		List<WholesaleInvoice> wholesale = await context.WholesaleInvoices.AsNoTracking()
			.Where(i => i.Status == "Posted" && i.InvoiceAtUtc >= startUtc && i.InvoiceAtUtc < endUtc)
			.ToListAsync(cancellationToken);
		Guid[] wholesaleIds = wholesale.Select(i => i.Id).ToArray();
		List<WholesaleInvoiceItem> wholesaleItems = await context.WholesaleInvoiceItems.AsNoTracking()
			.Where(i => wholesaleIds.Contains(i.WholesaleInvoiceId))
			.ToListAsync(cancellationToken);
		Dictionary<Guid, Customer> customers = await context.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);
		Dictionary<Guid, Drug> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync(d => d.Id, cancellationToken);

		List<GstSaleDocument> documents = new();
		foreach (WholesaleInvoice invoice in wholesale)
		{
			Customer customer = customers.GetValueOrDefault(invoice.CustomerId) ?? new Customer { Name = "Unknown" };
			string pos = IndianGstStateCodes.Resolve(customer.State, customer.Gstin);
			if (string.IsNullOrWhiteSpace(pos))
			{
				pos = pharmacyPos;
			}

			List<GstSaleLine> lines = wholesaleItems
				.Where(i => i.WholesaleInvoiceId == invoice.Id)
				.Select(i =>
				{
					Drug drug = drugs.GetValueOrDefault(i.DrugId) ?? new Drug { Name = "Item" };
					return new GstSaleLine(drug.Name, drug.HsnCode, i.Quantity, i.TaxRate, i.TaxableAmount, i.CgstAmount, i.SgstAmount, i.IgstAmount, i.LineTotal);
				})
				.ToList();
			documents.Add(new GstSaleDocument(
				invoice.InvoiceNo,
				DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()),
				customer.Gstin,
				pos,
				invoice.Subtotal,
				invoice.CgstAmount,
				invoice.SgstAmount,
				invoice.IgstAmount,
				invoice.TotalAmount,
				lines));
		}

		List<Sale> retail = await context.Sales.AsNoTracking()
			.Where(s => s.SaleAtUtc >= startUtc && s.SaleAtUtc < endUtc)
			.ToListAsync(cancellationToken);
		Guid[] saleIds = retail.Select(s => s.Id).ToArray();
		List<SaleItem> saleItems = await context.SaleItems.AsNoTracking()
			.Where(i => saleIds.Contains(i.SaleId))
			.ToListAsync(cancellationToken);

		foreach (Sale sale in retail)
		{
			List<GstSaleLine> lines = new();
			foreach (SaleItem item in saleItems.Where(i => i.SaleId == sale.Id))
			{
				Drug drug = drugs.GetValueOrDefault(item.DrugId) ?? new Drug { Name = "Item" };
				decimal rate = item.TaxRate;
				decimal taxable = Round(item.LineTotal / (1m + rate / 100m));
				decimal tax = Round(item.LineTotal - taxable);
				decimal half = Round(tax / 2m);
				lines.Add(new GstSaleLine(drug.Name, drug.HsnCode, item.Quantity, rate, taxable, half, tax - half, 0m, item.LineTotal));
			}

			documents.Add(new GstSaleDocument(
				sale.InvoiceNo,
				DateOnly.FromDateTime(sale.SaleAtUtc.ToLocalTime()),
				null,
				pharmacyPos,
				Round(lines.Sum(l => l.Taxable)),
				Round(lines.Sum(l => l.Cgst)),
				Round(lines.Sum(l => l.Sgst)),
				0m,
				sale.TotalAmount,
				lines));
		}

		return documents;
	}

	private sealed record GstSaleLine(string Description, string? HsnCode, decimal Quantity, decimal Rate, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal LineTotal);

	private sealed record GstSaleDocument(string InvoiceNo, DateOnly InvoiceDate, string? CounterpartyGstin, string PlaceOfSupply, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal InvoiceValue, IReadOnlyList<GstSaleLine> Lines);

	private static void ValidatePeriod(DateOnly from, DateOnly to)
	{
		if (to < from)
		{
			throw new ArgumentException("The GST return period end date must be on or after the start date.");
		}
	}

	private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

	private static decimal NearestSlab(decimal rate)
	{
		decimal best = B2cSlabs[0];
		decimal bestDelta = Math.Abs(rate - best);
		foreach (decimal slab in B2cSlabs)
		{
			decimal delta = Math.Abs(rate - slab);
			if (delta < bestDelta)
			{
				best = slab;
				bestDelta = delta;
			}
		}

		return best;
	}

	private static string NormalizeInvoiceKey(string? gstin, string invoiceNo)
		=> $"{(string.IsNullOrWhiteSpace(gstin) ? "-" : GstinValidator.Normalize(gstin))}|{invoiceNo.Trim()}";

	private static string? NullIfEmpty(string value)
	{
		string trimmed = value.Trim().Trim('"');
		return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
	}

	private static decimal ParseDecimal(string value)
		=> decimal.TryParse(value.Trim().Trim('"'), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal number) ? number : 0m;

}

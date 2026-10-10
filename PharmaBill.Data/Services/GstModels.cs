using System;
using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record Gstr1B2bRow(
	string Gstin,
	string InvoiceNo,
	DateOnly InvoiceDate,
	decimal TaxableValue,
	decimal Cgst,
	decimal Sgst,
	decimal Igst,
	decimal InvoiceValue,
	string PlaceOfSupply);

public sealed record Gstr1B2cSlabRow(
	decimal TaxRate,
	decimal TaxableValue,
	decimal Cgst,
	decimal Sgst,
	decimal Igst,
	int InvoiceCount);

public sealed record Gstr1HsnRow(
	string HsnCode,
	decimal Quantity,
	decimal TaxableValue,
	decimal Cgst,
	decimal Sgst,
	decimal Igst,
	decimal TaxRate);

public sealed record Gstr1Tables(
	IReadOnlyList<Gstr1B2bRow> Table4B2b,
	IReadOnlyList<Gstr1B2cSlabRow> Table7B2c,
	IReadOnlyList<Gstr1HsnRow> Table12Hsn);

public sealed record Gstr2PurchaseItcRow(
	string SupplierName,
	string? SupplierGstin,
	string InvoiceNo,
	DateOnly InvoiceDate,
	decimal TaxableValue,
	decimal Cgst,
	decimal Sgst,
	decimal Igst,
	decimal InvoiceValue);

public sealed record Gstr2bImportedRow(
	string? SupplierGstin,
	string InvoiceNo,
	DateOnly InvoiceDate,
	decimal TaxableValue,
	decimal Cgst,
	decimal Sgst,
	decimal Igst,
	decimal InvoiceValue);

public sealed record Gstr2bReconRow(
	string InvoiceNo,
	DateOnly? BooksDate,
	DateOnly? PortalDate,
	string? SupplierGstin,
	decimal BooksTaxable,
	decimal PortalTaxable,
	decimal BooksItc,
	decimal PortalItc,
	string MatchStatus,
	string Notes);

public sealed record Gstr3bOffsetSummary(
	decimal OutwardTaxable,
	decimal OutputCgst,
	decimal OutputSgst,
	decimal OutputIgst,
	decimal EligibleItcCgst,
	decimal EligibleItcSgst,
	decimal EligibleItcIgst,
	decimal NetCgstPayable,
	decimal NetSgstPayable,
	decimal NetIgstPayable,
	decimal NetGstPayable);

public sealed record EInvoicePayloadResult(string Json, string SuggestedFileName);

public sealed record EWayBillPayloadResult(string Json, string SuggestedFileName);

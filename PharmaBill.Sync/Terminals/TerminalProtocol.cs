using System;
using System.Collections.Generic;

namespace PharmaBill.Sync.Terminals;

// The billing-counter protocol between a terminal PC and the server PC that holds the shop's one database.
// HTTPS only (the terminal pins the server's certificate at pairing); every call carries the terminal's own key,
// and every call after sign-in also carries the signed-in user's session. The server decides everything that
// matters - stock, invoice numbers, prices against MRP, permissions - inside its own transactions.
public static class TerminalProtocol
{
	public const int Version = 1;

	public const int DefaultPort = 5056;

	public const string BasePath = "/api/terminal/v1";

	/// <summary>The terminal's device ID (a GUID issued at pairing).</summary>
	public const string DeviceHeader = "X-Terminal-Device";

	/// <summary>The terminal's secret, sent as "Authorization: Bearer &lt;secret&gt;".</summary>
	public const string AuthorizationScheme = "Bearer";

	/// <summary>The signed-in user's session token, issued by POST /session.</summary>
	public const string SessionHeader = "X-Terminal-Session";

	/// <summary>One ID per save attempt, reused on retry, so a lost reply never bills twice.</summary>
	public const string RequestIdHeader = "Idempotency-Key";

	public const long MaxPrescriptionBytes = 10L * 1024 * 1024;

	public static readonly string[] PrescriptionExtensions = [".jpg", ".jpeg", ".png", ".pdf"];
}

public sealed record TerminalHello(int ProtocolVersion, string ServerName, string ShopName, Guid DeviceId, string DeviceName, DateTime ServerTimeUtc, bool BillingAllowed, string? BillingBlockedReason);

public sealed record TerminalSignInRequest(string UserName, string Secret);

public sealed record TerminalSignInResponse(string SessionToken, Guid UserId, string DisplayName, string Role, DateTime ExpiresAtUtc);

public sealed record TerminalShop(string Name, string? LegalName, string? Address, string? Phone, string? Gstin, string? State, string BusinessMode, string NextInvoiceNo);

public sealed record TerminalAdminOverride(string UserName, string Secret);

public sealed record TerminalSaleLine(Guid DrugId, Guid BatchId, decimal Quantity, decimal DiscountAmount, decimal UnitPrice, string? HsnCode = null, decimal? GstRate = null, bool IsNilRated = false);

public sealed record TerminalPayment(string Method, decimal AppliedAmount, decimal TenderedAmount = 0m);

/// <summary>A bill as entered at the counter. Prices, stock and tax are re-checked on the server; nothing here is trusted.</summary>
public sealed record TerminalSaleRequest(
	string PatientName,
	string PatientPhone,
	string? PatientAddress,
	string? PrescriberName,
	string? PrescriberRegistrationNumber,
	IReadOnlyList<TerminalSaleLine> Items,
	IReadOnlyList<TerminalPayment> Payments,
	bool UpiPaymentReceived,
	string? Notes = null,
	string? MrdNumber = null,
	bool AddToCustomers = false,
	TerminalAdminOverride? PrescriptionOverride = null);

public sealed record TerminalSaleResponse(Guid SaleId, string InvoiceNo, decimal Subtotal, decimal TaxAmount, decimal DiscountAmount, decimal TotalAmount, decimal PaidAmount, decimal ChangeDue, string NextInvoiceNo);

public sealed record TerminalProductTaxRequest(string? HsnCode, decimal? GstRate, bool IsNilRated);

public sealed record TerminalBatchMrpRequest(decimal Mrp);

public sealed record TerminalFefoRequest(IReadOnlyList<Guid> DrugIds);

public sealed record TerminalError(string Error);

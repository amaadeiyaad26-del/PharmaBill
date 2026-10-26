
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

public sealed record TerminalReturnLine(Guid SaleItemId, decimal Quantity);

/// <summary>A return at the counter. A bill older than the record-lock period also needs an admin's unlock.</summary>
public sealed record TerminalReturnRequest(IReadOnlyList<TerminalReturnLine> Lines, string Reason, bool Restock, TerminalAdminOverride? Unlock = null);

public sealed record TerminalReturnResponse(string ReturnNo, decimal CreditAmount, decimal RestockedQuantity);

/// <summary>Voiding a bill always needs an admin, verified by PIN on the server.</summary>
public sealed record TerminalVoidRequest(string Reason, TerminalAdminOverride Admin);

public sealed record TerminalVoidResponse(Guid SaleId, string InvoiceNo, bool AlreadyVoided);

/// <summary>GET /pair: whether a code is open, and the server's nonce for this pairing.</summary>
public sealed record TerminalPairingChallenge(int ProtocolVersion, bool Open, string? ServerNonce, int Iterations, string ServerName, string ShopName);

/// <summary>POST /pair. The code itself is never sent - only a proof built from it and the certificate the terminal saw.</summary>
public sealed record TerminalPairRequest(string TerminalName, string TerminalNonce, string Proof);

public sealed record TerminalPairResponse(Guid DeviceId, string Secret, string ServerProof, string ServerName, string ShopName);

/// <summary>An error the cashier can read, plus a code the terminal acts on (see TerminalErrorCodes).</summary>
public sealed record TerminalError(string Error, string? Code = null);

public static class TerminalErrorCodes
{
	public const string Unpaired = "unpaired";

	public const string SignedOut = "signed-out";

	public const string WrongSignIn = "wrong-sign-in";

	public const string Locked = "locked";
}

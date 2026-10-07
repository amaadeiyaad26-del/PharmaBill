namespace PharmaBill.Data.Services;

public sealed record PeerBranchStockHint(string BranchCode, string BranchName, string MedicineName, string? CompositionKey, decimal Quantity);

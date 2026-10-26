
namespace PharmaBill.Data.Services;

public sealed record WholesaleCustomerImportResult(int Imported, int SkippedDuplicates, IReadOnlyList<string> Errors);

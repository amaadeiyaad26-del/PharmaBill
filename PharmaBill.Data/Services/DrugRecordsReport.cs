
namespace PharmaBill.Data.Services;

public sealed record DrugRecordsReport(IReadOnlyList<DrugRecordsRow> Rows, DrugRecordsSummary Summary, DateTime StartUtc, DateTime EndUtc);

using PharmaBill.Core.Entities;

namespace PharmaBill.App.ViewModels;

public sealed class ImportStatusViewModel(CatalogImportState state)
{
	public string ImportKey { get; } = state.ImportKey;

	public string FilePath { get; } = state.FilePath;

	public string Status { get; } = state.Status;

	public long ImportedRows { get; } = state.ImportedRows;

	public long SkippedRows { get; } = state.SkippedRows;

	public string? LastError { get; } = state.LastError;
}

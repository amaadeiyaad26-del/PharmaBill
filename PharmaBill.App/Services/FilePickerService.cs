using Microsoft.Win32;

namespace PharmaBill.App.Services;

public sealed class FilePickerService : IFilePickerService
{
    public string? PickLicenceDocument()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select licence copy",
            Filter = "Licence copies (*.pdf;*.png;*.jpg;*.jpeg;*.bmp)|*.pdf;*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickCsvFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose catalogue CSV",
            Filter = "CSV files (*.csv)|*.csv",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickPurchaseDocument()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose purchase bill",
            Filter = "Purchase documents (*.pdf;*.png;*.jpg;*.jpeg)|*.pdf;*.png;*.jpg;*.jpeg",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickPurchaseSpreadsheet()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose purchase spreadsheet",
            Filter = "Purchase spreadsheets (*.csv;*.xlsx)|*.csv;*.xlsx",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickCustomerExcel()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import wholesale customers",
            Filter = "Excel workbooks (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickPrescriptionDocument()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose prescription image or PDF",
            Filter = "Prescription documents (*.pdf;*.png;*.jpg;*.jpeg;*.bmp)|*.pdf;*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickExportDestination(string extension, string suggestedName)
    {
        var normalizedExtension = extension.TrimStart('.');
        var dialog = new SaveFileDialog
        {
            Title = $"Export {suggestedName}",
            Filter = normalizedExtension.ToLowerInvariant() switch
            {
                "xlsx" => "Excel workbook (*.xlsx)|*.xlsx",
                "csv" => "CSV files (*.csv)|*.csv",
                "json" => "JSON data (*.json)|*.json",
                "pbbak" => "PharmaBill encrypted backup (*.pbbak)|*.pbbak",
                "pbsync" => "PharmaBill sync package (*.pbsync)|*.pbsync",
                _ => "PDF files (*.pdf)|*.pdf"
            },
            DefaultExt = $".{normalizedExtension}",
            FileName = $"{suggestedName}.{normalizedExtension}",
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickBackupFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose encrypted PharmaBill backup",
            Filter = "PharmaBill encrypted backups (*.pbbak)|*.pbbak",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSyncPackage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose signed PharmaBill sync package",
            Filter = "PharmaBill sync packages (*.pbsync)|*.pbsync",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

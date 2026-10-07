using Microsoft.Win32;

namespace PharmaBill.App.Services;

public sealed class FilePickerService : IFilePickerService
{
	public string? PickLicenceDocument()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Select licence copy",
			Filter = "Licence copies (*.pdf;*.png;*.jpg;*.jpeg;*.bmp)|*.pdf;*.png;*.jpg;*.jpeg;*.bmp",
			CheckFileExists = true,
			Multiselect = false
		};
		if (openFileDialog.ShowDialog() != true)
		{
			return null;
		}
		return openFileDialog.FileName;
	}

	public string? PickCsvFile()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Choose catalogue CSV",
			Filter = "CSV files (*.csv)|*.csv",
			CheckFileExists = true,
			Multiselect = false
		};
		if (openFileDialog.ShowDialog() != true)
		{
			return null;
		}
		return openFileDialog.FileName;
	}

	public string? PickPurchaseDocument()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Choose purchase bill",
			Filter = "Purchase documents (*.pdf;*.png;*.jpg;*.jpeg)|*.pdf;*.png;*.jpg;*.jpeg",
			CheckFileExists = true,
			Multiselect = false
		};
		if (openFileDialog.ShowDialog() != true)
		{
			return null;
		}
		return openFileDialog.FileName;
	}

	public string? PickPurchaseSpreadsheet()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Choose purchase spreadsheet",
			Filter = "Purchase spreadsheets (*.csv;*.xlsx)|*.csv;*.xlsx",
			CheckFileExists = true,
			Multiselect = false
		};
		if (openFileDialog.ShowDialog() != true)
		{
			return null;
		}
		return openFileDialog.FileName;
	}

	public string? PickCustomerExcel()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Import wholesale customers",
			Filter = "Excel workbooks (*.xlsx)|*.xlsx",
			CheckFileExists = true,
			Multiselect = false
		};
		if (openFileDialog.ShowDialog() != true)
		{
			return null;
		}
		return openFileDialog.FileName;
	}

	public string? PickPrescriptionDocument()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Choose prescription image or PDF",
			Filter = "Prescription documents (*.pdf;*.png;*.jpg;*.jpeg;*.bmp)|*.pdf;*.png;*.jpg;*.jpeg;*.bmp",
			CheckFileExists = true,
			Multiselect = false
		};
		if (openFileDialog.ShowDialog() != true)
		{
			return null;
		}
		return openFileDialog.FileName;
	}

	public string? PickExportDestination(string extension, string suggestedName)
	{
		string text = extension.TrimStart('.');
		SaveFileDialog saveFileDialog = new SaveFileDialog();
		saveFileDialog.Title = "Export " + suggestedName;
		SaveFileDialog saveFileDialog2 = saveFileDialog;
		saveFileDialog2.Filter = text.ToLowerInvariant() switch
		{
			"xlsx" => "Excel workbook (*.xlsx)|*.xlsx", 
			"csv" => "CSV files (*.csv)|*.csv", 
			"json" => "JSON data (*.json)|*.json", 
			"pbbak" => "PharmaBill encrypted backup (*.pbbak)|*.pbbak", 
			"pbsync" => "PharmaBill sync package (*.pbsync)|*.pbsync", 
			_ => "PDF files (*.pdf)|*.pdf", 
		};
		saveFileDialog.DefaultExt = "." + text;
		saveFileDialog.FileName = suggestedName + "." + text;
		saveFileDialog.AddExtension = true;
		saveFileDialog.OverwritePrompt = true;
		SaveFileDialog saveFileDialog3 = saveFileDialog;
		if (saveFileDialog3.ShowDialog() != true)
		{
			return null;
		}
		return saveFileDialog3.FileName;
	}

	public string? PickFolder(string title)
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = title,
			Multiselect = false
		};
		if (openFolderDialog.ShowDialog() != true)
		{
			return null;
		}
		return openFolderDialog.FolderName;
	}

	public string? PickBackupFile()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Choose encrypted PharmaBill backup",
			Filter = "PharmaBill encrypted backups (*.pbbak)|*.pbbak",
			CheckFileExists = true,
			Multiselect = false
		};
		if (openFileDialog.ShowDialog() != true)
		{
			return null;
		}
		return openFileDialog.FileName;
	}

	public string? PickSyncPackage()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Choose signed PharmaBill sync package",
			Filter = "PharmaBill sync packages (*.pbsync)|*.pbsync",
			CheckFileExists = true,
			Multiselect = false
		};
		if (openFileDialog.ShowDialog() != true)
		{
			return null;
		}
		return openFileDialog.FileName;
	}
}

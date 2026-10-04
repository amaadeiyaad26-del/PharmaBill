namespace PharmaBill.App.Services;

public interface IFilePickerService
{
    string? PickLicenceDocument();

    string? PickCsvFile();

    string? PickPurchaseDocument();

    string? PickPurchaseSpreadsheet();

    string? PickCustomerExcel();

    string? PickPrescriptionDocument();

    string? PickExportDestination(string extension, string suggestedName);

    string? PickFolder(string title);

    string? PickBackupFile();

    string? PickSyncPackage();
}

using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using PharmaBill.Core.Entities;

namespace PharmaBill.App.ViewModels;

public partial class LicenceDraftViewModel : ObservableObject
{
    [ObservableProperty]
    private string _licenceType = "20";

    [ObservableProperty]
    private string _licenceNumber = string.Empty;

    [ObservableProperty]
    private DateTime? _issueDate;

    [ObservableProperty]
    private DateTime? _expiryDate;

    [ObservableProperty]
    private string? _documentPath;

    public LicenceRecord ToEntity()
    {
        if (string.IsNullOrWhiteSpace(LicenceNumber))
        {
            throw new InvalidOperationException("Enter the licence number.");
        }

        if (!IssueDate.HasValue || !ExpiryDate.HasValue)
        {
            throw new InvalidOperationException("Enter both the licence issue date and expiry date.");
        }

        if (IssueDate.Value.Date > DateTime.Today)
        {
            throw new InvalidOperationException("The licence issue date cannot be in the future.");
        }

        if (ExpiryDate.HasValue && IssueDate.HasValue && ExpiryDate.Value.Date < IssueDate.Value.Date)
        {
            throw new InvalidOperationException("The licence expiry date cannot be earlier than its issue date.");
        }

        var storedDocument = CopyDocument(DocumentPath);
        return new LicenceRecord
        {
            LicenceType = LicenceType.Trim(),
            LicenceNumber = LicenceNumber.Trim(),
            IssuedOn = IssueDate.HasValue ? DateOnly.FromDateTime(IssueDate.Value) : null,
            ExpiresOn = ExpiryDate.HasValue ? DateOnly.FromDateTime(ExpiryDate.Value) : null,
            DocumentPath = storedDocument
        };
    }

    private static string? CopyDocument(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not ".pdf" and not ".png" and not ".jpg" and not ".jpeg" and not ".bmp")
        {
            throw new InvalidOperationException("Licence copy must be a PDF or image file.");
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaBill",
            "licences");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
        File.Copy(sourcePath, destination, overwrite: false);
        return destination;
    }
}

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PharmaBill.App.Services;

public enum DocumentPaperSize
{
    A4,
    A5,
    Thermal80
}

public sealed class DocumentOutputSettings
{
    public DocumentPaperSize RetailMemoPaperSize { get; set; } = DocumentPaperSize.A4;
    public DocumentPaperSize RetailInvoicePaperSize { get; set; } = DocumentPaperSize.A4;
    public string? RetailMemoPrinter { get; set; }
    public string? RetailInvoicePrinter { get; set; }
    public string FooterText { get; set; } = string.Empty;
    public string? LogoPath { get; set; }
    public string WhatsAppNumber { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpFromAddress { get; set; } = string.Empty;
    public string SmtpUserName { get; set; } = string.Empty;
    public bool SmtpEnableSsl { get; set; } = true;
    public bool CashDrawerPulse { get; set; }
    public string? InspectorPinHash { get; set; }
}

public sealed class DocumentOutputSettingsStore
{
    private readonly string _settingsPath;
    private readonly string _smtpPasswordPath;

    public DocumentOutputSettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaBill");
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "document-output.json");
        _smtpPasswordPath = Path.Combine(directory, "smtp-password.dat");
    }

    public DocumentOutputSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new DocumentOutputSettings();
        }

        var json = File.ReadAllText(_settingsPath);
        return JsonSerializer.Deserialize<DocumentOutputSettings>(json)
               ?? throw new InvalidDataException("Document output settings are invalid.");
    }

    public void Save(DocumentOutputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void SaveSmtpPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(password),
            null,
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_smtpPasswordPath, encrypted);
    }

    public void SaveInspectorPin(string pin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pin);
        var settings = Load();
        settings.InspectorPinHash = PharmaBill.Core.Security.PasswordHasher.Hash(pin);
        Save(settings);
    }

    public bool VerifyInspectorPin(string pin)
    {
        var hash = Load().InspectorPinHash;
        return !string.IsNullOrWhiteSpace(hash) && PharmaBill.Core.Security.PasswordHasher.Verify(pin, hash);
    }

    public string? ReadSmtpPassword()
    {
        if (!File.Exists(_smtpPasswordPath))
        {
            return null;
        }

        var encrypted = File.ReadAllBytes(_smtpPasswordPath);
        return Encoding.UTF8.GetString(
            ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
    }

    public void DeleteSmtpPassword()
    {
        if (File.Exists(_smtpPasswordPath))
        {
            File.Delete(_smtpPasswordPath);
        }
    }
}

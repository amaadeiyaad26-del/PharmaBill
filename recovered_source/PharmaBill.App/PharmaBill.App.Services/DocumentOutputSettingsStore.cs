using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PharmaBill.Core.Security;

namespace PharmaBill.App.Services;

public sealed class DocumentOutputSettingsStore
{
	private readonly string _settingsPath;

	private readonly string _smtpPasswordPath;

	public DocumentOutputSettingsStore()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill");
		Directory.CreateDirectory(text);
		_settingsPath = Path.Combine(text, "document-output.json");
		_smtpPasswordPath = Path.Combine(text, "smtp-password.dat");
	}

	public DocumentOutputSettings Load()
	{
		if (!File.Exists(_settingsPath))
		{
			return new DocumentOutputSettings();
		}
		return JsonSerializer.Deserialize<DocumentOutputSettings>(File.ReadAllText(_settingsPath)) ?? throw new InvalidDataException("Document output settings are invalid.");
	}

	public void Save(DocumentOutputSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings, "settings");
		string contents = JsonSerializer.Serialize(settings, new JsonSerializerOptions
		{
			WriteIndented = true
		});
		string text = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
		try
		{
			File.WriteAllText(text, contents);
			File.Move(text, _settingsPath, overwrite: true);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	public void SaveSmtpPassword(string password)
	{
		if (!string.IsNullOrEmpty(password))
		{
			byte[] bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser);
			File.WriteAllBytes(_smtpPasswordPath, bytes);
		}
	}

	public void SaveInspectorPin(string pin)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pin, "pin");
		DocumentOutputSettings documentOutputSettings = Load();
		documentOutputSettings.InspectorPinHash = PasswordHasher.Hash(pin);
		Save(documentOutputSettings);
	}

	public bool VerifyInspectorPin(string pin)
	{
		string inspectorPinHash = Load().InspectorPinHash;
		if (!string.IsNullOrWhiteSpace(inspectorPinHash))
		{
			return PasswordHasher.Verify(pin, inspectorPinHash);
		}
		return false;
	}

	public string? ReadSmtpPassword()
	{
		if (!File.Exists(_smtpPasswordPath))
		{
			return null;
		}
		byte[] encryptedData = File.ReadAllBytes(_smtpPasswordPath);
		return Encoding.UTF8.GetString(ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser));
	}

	public void DeleteSmtpPassword()
	{
		if (File.Exists(_smtpPasswordPath))
		{
			File.Delete(_smtpPasswordPath);
		}
	}
}

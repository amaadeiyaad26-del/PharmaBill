using System;
using System.IO;
using System.Text.Json;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.Services;

public sealed class UpdateCheckPreferencesStore
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private readonly string _path;

	public UpdateCheckPreferencesStore(DatabaseStorageOptions storage)
	{
		Directory.CreateDirectory(storage.RootDirectory);
		_path = Path.Combine(storage.RootDirectory, "update-check-preferences.json");
	}

	public UpdateCheckPreferences Load()
	{
		try
		{
			if (!File.Exists(_path))
			{
				return new UpdateCheckPreferences(null, null, null);
			}
			return JsonSerializer.Deserialize<UpdateCheckPreferences>(File.ReadAllText(_path), JsonOptions) ?? new UpdateCheckPreferences(null, null, null);
		}
		catch
		{
			return new UpdateCheckPreferences(null, null, null);
		}
	}

	public void Save(UpdateCheckPreferences preferences)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_path));
		string text = $"{_path}.{Guid.NewGuid():N}.tmp";
		try
		{
			File.WriteAllText(text, JsonSerializer.Serialize(preferences, JsonOptions));
			if (File.Exists(_path))
			{
				File.Replace(text, _path, null);
			}
			else
			{
				File.Move(text, _path);
			}
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}
}

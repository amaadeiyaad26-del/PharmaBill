using System;
using System.IO;
using System.Text.Json;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.Services;

public sealed class UiSoundPreferencesStore
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private readonly string _path;

	public UiSoundPreferencesStore(DatabaseStorageOptions storage)
	{
		Directory.CreateDirectory(storage.RootDirectory);
		_path = Path.Combine(storage.RootDirectory, "ui-sound-preferences.json");
	}

	public UiSoundPreferencesStore(string rootDirectory)
	{
		Directory.CreateDirectory(rootDirectory);
		_path = Path.Combine(rootDirectory, "ui-sound-preferences.json");
	}

	public UiSoundPreferences Load()
	{
		try
		{
			if (!File.Exists(_path))
			{
				return new UiSoundPreferences(EnableUiSounds: true);
			}

			return JsonSerializer.Deserialize<UiSoundPreferences>(File.ReadAllText(_path), JsonOptions)
				?? new UiSoundPreferences(EnableUiSounds: true);
		}
		catch
		{
			return new UiSoundPreferences(EnableUiSounds: true);
		}
	}

	public void Save(UiSoundPreferences preferences)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
		string temp = $"{_path}.{Guid.NewGuid():N}.tmp";
		try
		{
			File.WriteAllText(temp, JsonSerializer.Serialize(preferences, JsonOptions));
			if (File.Exists(_path))
			{
				File.Replace(temp, _path, null);
			}
			else
			{
				File.Move(temp, _path);
			}
		}
		finally
		{
			if (File.Exists(temp))
			{
				File.Delete(temp);
			}
		}

		SoundHelper.SetEnabled(preferences.EnableUiSounds);
	}
}
using System;
using System.IO;
using System.Text.Json;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class BranchSettingsStore
{
	private readonly string _path;

	private readonly object _gate = new object();

	private BranchSettings _cached = new BranchSettings();

	public BranchSettingsStore(DatabaseStorageOptions storage)
	{
		Directory.CreateDirectory(storage.RootDirectory);
		_path = Path.Combine(storage.RootDirectory, "branch-settings.json");
		_cached = LoadFromDisk();
	}

	public BranchSettings Load()
	{
		lock (_gate)
		{
			return Clone(_cached);
		}
	}

	public void Save(BranchSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings, "settings");
		lock (_gate)
		{
			_cached = Clone(settings);
			string contents = JsonSerializer.Serialize(_cached, new JsonSerializerOptions
			{
				WriteIndented = true
			});
			string text = _path + ".tmp";
			File.WriteAllText(text, contents);
			File.Move(text, _path, overwrite: true);
		}
	}

	private BranchSettings LoadFromDisk()
	{
		if (!File.Exists(_path))
		{
			return new BranchSettings();
		}
		try
		{
			return JsonSerializer.Deserialize<BranchSettings>(File.ReadAllText(_path)) ?? new BranchSettings();
		}
		catch
		{
			return new BranchSettings();
		}
	}

	private static BranchSettings Clone(BranchSettings source)
	{
		return new BranchSettings
		{
			CurrentBranchId = source.CurrentBranchId,
			CurrentBranchCode = source.CurrentBranchCode,
			CurrentBranchName = source.CurrentBranchName,
			IsHeadOffice = source.IsHeadOffice
		};
	}
}

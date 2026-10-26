using System.IO;
using System.Text.Json;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.Services;

/// <summary>Remembers which one-time notices the user has dismissed (per Windows user, in the app data folder).</summary>
public sealed class OneTimeNoticeStore(string path)
{
	public static OneTimeNoticeStore Default { get; } = new(Path.Combine(AppDataPaths.DataDirectory, "dismissed-notices.json"));

	public bool IsDismissed(string key) => Load().Contains(key);

	public void Dismiss(string key)
	{
		HashSet<string> keys = Load();
		if (keys.Add(key))
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JsonSerializer.Serialize(keys));
		}
	}

	private HashSet<string> Load()
	{
		try
		{
			return File.Exists(path)
				? new HashSet<string>(JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? Array.Empty<string>(), StringComparer.Ordinal)
				: new HashSet<string>(StringComparer.Ordinal);
		}
		catch (Exception)
		{
			// An unreadable file only means the notice shows again; it never blocks the screen.
			return new HashSet<string>(StringComparer.Ordinal);
		}
	}
}

using System;
using System.IO;
using System.Text.Json;

namespace PharmaBill.App.Services;

public sealed class UserManualEmailQueueStore
{
	private readonly string _path;

	public UserManualEmailQueueStore()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill");
		Directory.CreateDirectory(text);
		_path = Path.Combine(text, "pending-user-manual-email.json");
	}

	public void Enqueue(PendingUserManualEmail pending)
	{
		ArgumentNullException.ThrowIfNull(pending, "pending");
		string contents = JsonSerializer.Serialize(pending, new JsonSerializerOptions
		{
			WriteIndented = true
		});
		string text = $"{_path}.{Guid.NewGuid():N}.tmp";
		try
		{
			File.WriteAllText(text, contents);
			File.Move(text, _path, overwrite: true);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	public PendingUserManualEmail? Peek()
	{
		if (!File.Exists(_path))
		{
			return null;
		}
		try
		{
			return JsonSerializer.Deserialize<PendingUserManualEmail>(File.ReadAllText(_path));
		}
		catch
		{
			return null;
		}
	}

	public void Clear()
	{
		if (File.Exists(_path))
		{
			File.Delete(_path);
		}
	}
}

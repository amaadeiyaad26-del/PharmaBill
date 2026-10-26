using PharmaBill.Data.Persistence;
using System.IO;
using System.Text.Json;

namespace PharmaBill.App.Services;

/// <summary>One line of an unsaved wholesale invoice, kept by batch so it can be matched again on restore.</summary>
public sealed record WholesaleDraftLine(Guid DrugId, Guid BatchId, decimal Quantity, decimal FreeQuantity, decimal UnitPrice, decimal DiscountAmount);

/// <summary>The unsaved wholesale invoice: buyer, notes and lines.</summary>
public sealed record WholesaleDraft(Guid? CustomerId, string Notes, DateTime SavedAtUtc, IReadOnlyList<WholesaleDraftLine> Lines);

/// <summary>Keeps the wholesale invoice in progress in the user's profile, so it survives closing the app.</summary>
public sealed class WholesaleDraftStore
{
	private readonly string _path = Path.Combine(AppDataPaths.RootDirectory, "wholesale-draft.json");

	public void Save(WholesaleDraft draft)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
			File.WriteAllText(_path, JsonSerializer.Serialize(draft));
		}
		catch (IOException)
		{
			// A draft is a convenience; failing to write it must not stop billing.
		}
	}

	public WholesaleDraft? Load()
	{
		try
		{
			return File.Exists(_path) ? JsonSerializer.Deserialize<WholesaleDraft>(File.ReadAllText(_path)) : null;
		}
		catch (Exception ex) when (ex is IOException or JsonException or NotSupportedException)
		{
			return null;
		}
	}

	public void Clear()
	{
		try
		{
			if (File.Exists(_path))
			{
				File.Delete(_path);
			}
		}
		catch (IOException)
		{
		}
	}
}

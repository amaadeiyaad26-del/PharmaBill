using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>The three milestones of a new shop, read from its records: nothing is ticked by hand.</summary>
public sealed record OnboardingProgress(bool StoreDetails, bool FirstDrug, bool FirstSale)
{
	public int Done => (StoreDetails ? 1 : 0) + (FirstDrug ? 1 : 0) + (FirstSale ? 1 : 0);

	public bool Complete => Done == 3;
}

public sealed class OnboardingProgressService(PharmaBillDbContext context)
{
	public async Task<OnboardingProgress> GetAsync(CancellationToken cancellationToken = default)
	{
		PharmacyProfile? profile = await context.PharmacyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
		// Store details: what every invoice prints. A name, an address, and the GSTIN or a drug licence.
		bool storeDetails = profile != null
			&& !string.IsNullOrWhiteSpace(profile.Name)
			&& !string.IsNullOrWhiteSpace(profile.Address)
			&& (!string.IsNullOrWhiteSpace(profile.Gstin) || await context.LicenceRecords.AnyAsync(cancellationToken));
		bool firstDrug = await context.Batches.AnyAsync(cancellationToken);
		bool firstSale = await context.Sales.AnyAsync(cancellationToken);
		return new OnboardingProgress(storeDetails, firstDrug, firstSale);
	}
}

/// <summary>Whether the owner hid the getting-started checklist. Kept beside the shop's other settings.</summary>
public sealed class OnboardingSettingsStore(string directory)
{
	private readonly string _path = Path.Combine(directory, "onboarding.json");

	public bool IsChecklistHidden()
	{
		try
		{
			return File.Exists(_path) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path))?.ChecklistHidden == true;
		}
		catch (Exception exception) when (exception is IOException or JsonException)
		{
			return false;
		}
	}

	public void HideChecklist()
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
		File.WriteAllText(_path, JsonSerializer.Serialize(new Settings(true)));
	}

	private sealed record Settings(bool ChecklistHidden);
}

using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

/// <summary>
/// The shop's name, contact details and logo (or generated monogram), shared by both dashboards and the window
/// header. One instance for the app; <see cref="ReloadAsync"/> runs when Firm Profile, branch details or the logo
/// are saved, so every screen updates at once.
/// </summary>
public sealed class ShopIdentityViewModel(IServiceScopeFactory scopeFactory, DocumentOutputSettingsStore documentSettings, ILogger<ShopIdentityViewModel> logger) : ObservableObject
{
	private ShopIdentity _identity = new ShopIdentity(ShopIdentityResolver.FallbackName, null, null, null);

	private ImageSource? _logo;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	/// <summary>Raised after the identity has been reloaded.</summary>
	public event EventHandler? Changed;

	public string Name => _identity.Name;

	/// <summary>Long names step down in size before they wrap, so the heading stays a lockup rather than a poster.</summary>
	public double NameFontSize => Name.Length <= 26 ? 32 : Name.Length <= 44 ? 27 : 23;

	public string? Address => _identity.Address;

	public string? Phone => _identity.Phone;

	public bool HasAddress => !string.IsNullOrWhiteSpace(Address);

	public bool HasPhone => !string.IsNullOrWhiteSpace(Phone);

	/// <summary>The dot between address and phone, only when both are present.</summary>
	public bool HasContactSeparator => HasAddress && HasPhone;

	public string ContactSeparatorText => HasContactSeparator ? "   ·   " : string.Empty;

	public bool HasContactLine => HasAddress || HasPhone;

	public string Initials => _identity.Initials;

	public ImageSource? Logo
	{
		get => _logo;
		private set
		{
			if (SetProperty(ref _logo, value))
			{
				OnPropertyChanged(nameof(HasLogo));
				OnPropertyChanged(nameof(ShowMonogram));
			}
		}
	}

	public bool HasLogo => Logo != null;

	/// <summary>No uploaded logo: the lockup shows a generated monogram badge instead.</summary>
	public bool ShowMonogram => !HasLogo;

	public async Task ReloadAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			PharmacyProfile? profile = await context.PharmacyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
			Guid? branchId = scope.ServiceProvider.GetRequiredService<BranchSettingsStore>().Load().CurrentBranchId;
			Branch? branch = branchId is Guid id
				? await context.Branches.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken)
				: null;
			ShopIdentity identity = ShopIdentityResolver.Resolve(profile, branch);
			Apply(identity, ShopLogoImporter.ToImageSource(identity.LogoPng, 192) ?? LegacyPrintLogo());
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The dashboards still work without the identity block; keep the last good values.
			logger.LogWarning(ex, "Could not load the shop identity.");
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>Shows the given identity and logo (null logo means the monogram). Also used to preview the lockup.</summary>
	internal void Apply(ShopIdentity identity, ImageSource? logo)
	{
		_identity = identity;
		OnPropertyChanged(nameof(Name));
		OnPropertyChanged(nameof(NameFontSize));
		OnPropertyChanged(nameof(Address));
		OnPropertyChanged(nameof(Phone));
		OnPropertyChanged(nameof(HasAddress));
		OnPropertyChanged(nameof(HasPhone));
		OnPropertyChanged(nameof(HasContactSeparator));
		OnPropertyChanged(nameof(ContactSeparatorText));
		OnPropertyChanged(nameof(HasContactLine));
		OnPropertyChanged(nameof(Initials));
		Logo = logo;
		Changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>A logo chosen earlier for printed bills only, before logos were stored with Firm Profile.</summary>
	private ImageSource? LegacyPrintLogo()
	{
		string? path = documentSettings.Load().LogoPath;
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return null;
		}

		try
		{
			return ShopLogoImporter.ToImageSource(File.ReadAllBytes(path), 192);
		}
		catch (Exception ex)
		{
			logger.LogInformation(ex, "Print logo {Path} could not be read; showing the monogram.", path);
			return null;
		}
	}
}

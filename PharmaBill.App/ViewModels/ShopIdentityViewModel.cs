using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
/// The shop's name, contact line and logo, shared by both dashboards and the window header. One instance for the
/// app; <see cref="ReloadAsync"/> runs when Firm Profile, branch details or the shop logo are saved, so every screen
/// updates at once.
/// </summary>
public sealed class ShopIdentityViewModel(IServiceScopeFactory scopeFactory, DocumentOutputSettingsStore documentSettings, ILogger<ShopIdentityViewModel> logger) : ObservableObject
{
	private ShopIdentity _identity = new ShopIdentity(ShopIdentityResolver.FallbackName, null, null, null);

	private ImageSource? _logo;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	/// <summary>Raised after the identity has been reloaded.</summary>
	public event EventHandler? Changed;

	public string Name => _identity.Name;

	public string ContactLine => _identity.ContactLine;

	public bool HasContactLine => ContactLine.Length > 0;

	public ImageSource? Logo
	{
		get => _logo;
		private set
		{
			if (SetProperty(ref _logo, value))
			{
				OnPropertyChanged(nameof(HasLogo));
			}
		}
	}

	public bool HasLogo => Logo != null;

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
			ShopIdentity identity = ShopIdentityResolver.Resolve(profile, branch, documentSettings.Load().LogoPath);

			bool logoChanged = !string.Equals(identity.LogoPath, _identity.LogoPath, StringComparison.OrdinalIgnoreCase) || Logo == null;
			_identity = identity;
			OnPropertyChanged(nameof(Name));
			OnPropertyChanged(nameof(ContactLine));
			OnPropertyChanged(nameof(HasContactLine));
			if (logoChanged)
			{
				Logo = LoadLogo(identity.LogoPath);
			}

			Changed?.Invoke(this, EventArgs.Empty);
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

	/// <summary>Loads the uploaded logo without locking the file, so the shop can replace it. Null when there is none.</summary>
	private ImageSource? LoadLogo(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return null;
		}

		try
		{
			BitmapImage image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
			image.DecodePixelHeight = 160;
			image.UriSource = new Uri(path, UriKind.Absolute);
			image.EndInit();
			image.Freeze();
			return image;
		}
		catch (Exception ex)
		{
			logger.LogInformation(ex, "Shop logo {Path} could not be read; showing the name only.", path);
			return null;
		}
	}
}

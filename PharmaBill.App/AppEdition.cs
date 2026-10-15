namespace PharmaBill.App;

#if STORE_EDITION && LOCAL_MARKET_EDITION
#error Both STORE_EDITION and LOCAL_MARKET_EDITION are defined. Build with -p:Edition=Store or -p:Edition=LocalMarket.
#elif !STORE_EDITION && !LOCAL_MARKET_EDITION
#error No edition constant. Build with -p:Edition=Store or -p:Edition=LocalMarket.
#endif

/// <summary>Which distribution this binary was built for. Both editions share every service, view model and migration.</summary>
public enum Edition
{
	/// <summary>Microsoft Store (MSIX). The Store installs updates.</summary>
	Store,

	/// <summary>USB and direct installer (Inno Setup). Updates come from the release feed.</summary>
	LocalMarket
}

/// <summary>
/// The only place the edition constant is read. Edition-specific behaviour goes through these helpers, which take
/// the edition as a parameter so the test suite covers both editions in one run.
/// </summary>
public static class AppEdition
{
#if STORE_EDITION
	public const Edition Current = Edition.Store;
#else
	public const Edition Current = Edition.LocalMarket;
#endif

	/// <summary>Opens the Microsoft Store's Downloads and updates page, which updates this app.</summary>
	public const string StoreUpdatesUri = "ms-windows-store://downloadsandupdates";

	public static string ProductName(Edition edition) => edition == Edition.Store ? "Pharma Remit" : "Pharma Remit Local Market";

	/// <summary>The Store edition shows a "Microsoft Store edition" badge in About; the Local Market edition shows none.</summary>
	public static bool ShowsStoreBadge(Edition edition) => edition == Edition.Store;

	/// <summary>
	/// Store builds never point at the downloadable installer: the Store installs updates, and the installer would
	/// put a second copy beside the Store one. Local Market builds read the release feed.
	/// </summary>
	public static bool UsesReleaseFeed(Edition edition) => edition == Edition.LocalMarket;

	/// <summary>The user-manual tip about keeping the app up to date.</summary>
	public static string UpdateAdvice(Edition edition) => edition == Edition.Store
		? "Keep Microsoft Store updates enabled so Pharma Remit stays current."
		: "Use Settings > About > Check for updates, or install the latest version from your supplier's USB or download link.";
}

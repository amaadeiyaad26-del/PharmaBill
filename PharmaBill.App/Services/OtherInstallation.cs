using System.IO;
using Microsoft.Win32;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.Services;

/// <summary>What this computer has besides the running app: the other edition of Pharma Remit, and where it keeps records.</summary>
/// <param name="StorePackageInstalled">The Microsoft Store version (package S.A.E.R.PharmaBill) is installed for this Windows user.</param>
/// <param name="StoreDataFolder">The data folder the Store version chose, when its pointer file could be read.</param>
/// <param name="WebsiteVersionInstalled">The website version (Inno Setup, the stable AppId) is installed on this computer.</param>
/// <param name="WebsiteInstallFolder">Where the website version's program files are.</param>
/// <param name="StorePointerFiles">Every place the Store version's data-location pointer may be, for the takeover check.</param>
public sealed record OtherInstallationFacts(
	bool StorePackageInstalled,
	string? StoreDataFolder,
	bool WebsiteVersionInstalled,
	string? WebsiteInstallFolder,
	IReadOnlyList<string> StorePointerFiles);

public interface IOtherInstallationProbe
{
	OtherInstallationFacts Probe();
}

/// <summary>
/// Looks for the other edition the way Windows records it: the Store package in this user's package repository, and
/// the website installer's uninstall entry (its AppId has been the same in every release).
/// </summary>
public sealed class WindowsInstallationProbe : IOtherInstallationProbe
{
	public const string StorePackageName = "S.A.E.R.PharmaBill";

	/// <summary>The website installer's AppId, the same in every release (installer\PharmaRemitLocalMarket.iss).</summary>
	public const string WebsiteAppId = "{6F0B9C1E-3A52-4D8B-9E47-5B1D2C7A8F31}";

	private const string PackageRepository = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

	private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + WebsiteAppId + "_is1";

	public OtherInstallationFacts Probe()
	{
		IReadOnlyList<string> pointers = StorePointerFiles();
		string? storeFolder = pointers.Select(StoreDataLocation.ReadPointer).FirstOrDefault(folder => !string.IsNullOrWhiteSpace(folder));
		(bool websiteInstalled, string? websiteFolder) = WebsiteInstall();
		return new OtherInstallationFacts(StorePackageInstalled(), storeFolder, websiteInstalled, websiteFolder, pointers);
	}

	/// <summary>The same test the website installer makes: a package of this name in the user's package repository.</summary>
	public static bool StorePackageInstalled()
	{
		try
		{
			using RegistryKey? packages = Registry.CurrentUser.OpenSubKey(PackageRepository);
			return packages?.GetSubKeyNames().Any(name => name.StartsWith(StorePackageName + "_", StringComparison.OrdinalIgnoreCase)) == true;
		}
		catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
		{
			return false;
		}
	}

	/// <summary>
	/// Where the Store version's pointer may be: in its private package storage (where Windows keeps the files a Store app
	/// creates under %LocalAppData%), or in %LocalAppData% itself (written by a version run outside its package).
	/// </summary>
	public static IReadOnlyList<string> StorePointerFiles()
	{
		var files = new List<string>();
		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string packages = Path.Combine(localAppData, "Packages");
		try
		{
			if (Directory.Exists(packages))
			{
				files.AddRange(Directory.EnumerateDirectories(packages, StorePackageName + "_*")
					.Select(package => Path.Combine(package, "LocalCache", "Local", AppDataPaths.RootFolderName, StoreDataLocation.PointerFileName)));
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
		files.Add(Path.Combine(localAppData, AppDataPaths.RootFolderName, StoreDataLocation.PointerFileName));
		return files;
	}

	private static (bool Installed, string? Folder) WebsiteInstall()
	{
		foreach ((RegistryHive hive, RegistryView view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
		{
			try
			{
				using RegistryKey root = RegistryKey.OpenBaseKey(hive, view);
				using RegistryKey? key = root.OpenSubKey(UninstallKey);
				if (key != null)
				{
					return (true, key.GetValue("InstallLocation") as string);
				}
			}
			catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
			{
			}
		}
		return (false, null);
	}
}

/// <summary>The warning each edition shows when the other is installed too.</summary>
public static class OtherInstallationNotice
{
	/// <summary>Remembered once dismissed (per data folder).</summary>
	public const string DismissKey = "other-installation-warning";

	/// <summary>Where the website version keeps its records (it does not let the pharmacist choose).</summary>
	public static string WebsiteDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill");

	/// <summary>The warning for this edition, or null when the other edition is not installed.</summary>
	public static string? For(Edition edition, OtherInstallationFacts facts, string thisDataFolder)
	{
		if (edition == Edition.LocalMarket && facts.StorePackageInstalled)
		{
			return "Pharma Remit from the Microsoft Store is also installed on this computer. It keeps its own records"
				+ (string.IsNullOrWhiteSpace(facts.StoreDataFolder) ? " in the folder chosen when it was first opened" : " in " + facts.StoreDataFolder)
				+ ". This app (the website version) uses " + thisDataFolder + ". A bill or stock entry made in one is not in the other, "
				+ "and both number invoices the same way. Use only one of them: see \"Both versions installed\" in the support notes.";
		}
		if (edition == Edition.Store && facts.WebsiteVersionInstalled)
		{
			return "The website version of Pharma Remit is also installed on this computer"
				+ (string.IsNullOrWhiteSpace(facts.WebsiteInstallFolder) ? "" : " (" + facts.WebsiteInstallFolder.TrimEnd('\\') + ")")
				+ ". It keeps its own records in " + WebsiteDataFolder + ". This app (the Microsoft Store version) uses " + thisDataFolder
				+ ". A bill or stock entry made in one is not in the other, and both number invoices the same way. "
				+ "Use only one of them: see \"Both versions installed\" in the support notes.";
		}
		return null;
	}
}

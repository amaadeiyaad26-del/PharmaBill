using System;
using System.IO;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.Services;

public sealed class ActiveBillingDeskModeStore(DatabaseStorageOptions storage)
{
	private readonly string _path = Path.Combine(storage.RootDirectory, "active-billing-desk-mode.txt");

	public ActiveBillingDeskMode Load(BusinessMode pharmacyMode)
	{
		ActiveBillingDeskMode result = DefaultFor(pharmacyMode);
		if (!File.Exists(_path))
		{
			return result;
		}
		try
		{
			if (!Enum.TryParse<ActiveBillingDeskMode>(File.ReadAllText(_path).Trim(), ignoreCase: true, out var result2))
			{
				return result;
			}
			return Clamp(result2, pharmacyMode);
		}
		catch (IOException)
		{
			return result;
		}
	}

	public void Save(ActiveBillingDeskMode mode)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_path));
		string text = $"{_path}.{Guid.NewGuid():N}.tmp";
		try
		{
			File.WriteAllText(text, mode.ToString());
			if (File.Exists(_path))
			{
				File.Replace(text, _path, null);
			}
			else
			{
				File.Move(text, _path);
			}
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	public static ActiveBillingDeskMode DefaultFor(BusinessMode pharmacyMode)
	{
		return pharmacyMode switch
		{
			BusinessMode.Retail => ActiveBillingDeskMode.Retail, 
			BusinessMode.Wholesaler => ActiveBillingDeskMode.Wholesale, 
			_ => ActiveBillingDeskMode.Combined, 
		};
	}

	public static ActiveBillingDeskMode Clamp(ActiveBillingDeskMode mode, BusinessMode pharmacyMode)
	{
		return pharmacyMode switch
		{
			BusinessMode.Retail => ActiveBillingDeskMode.Retail, 
			BusinessMode.Wholesaler => ActiveBillingDeskMode.Wholesale, 
			_ => mode, 
		};
	}
}

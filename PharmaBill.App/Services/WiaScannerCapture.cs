using System.IO;
using System.Runtime.InteropServices;

namespace PharmaBill.App.Services;

public static class WiaScannerCapture
{
	private const int ScannerDeviceType = 1;

	private const string JpegFormatId = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}";

	public static bool HasScanner()
	{
		object? manager = null;
		try
		{
			manager = Create("WIA.DeviceManager");
			if (manager == null)
			{
				return false;
			}

			dynamic infos = ((dynamic)manager).DeviceInfos;
			foreach (dynamic info in infos)
			{
				if ((int)info.Type == ScannerDeviceType)
				{
					return true;
				}
			}

			return false;
		}
		catch
		{
			return false;
		}
		finally
		{
			Release(manager);
		}
	}

	public static string? ScanToJpeg()
	{
		object? dialog = null;
		object? image = null;
		try
		{
			dialog = Create("WIA.CommonDialog");
			if (dialog == null)
			{
				throw new InvalidOperationException("Windows Image Acquisition is not available on this PC.");
			}

			image = ((dynamic)dialog).ShowAcquireImage(ScannerDeviceType, 0, 0, JpegFormatId, false, true, false);
			if (image == null)
			{
				return null;
			}

			string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "prescription-captures");
			Directory.CreateDirectory(directory);
			string path = Path.Combine(directory, $"scan-{DateTime.Now:yyyyMMdd-HHmmss}.jpg");
			((dynamic)image).SaveFile(path);
			return path;
		}
		catch (COMException ex) when (IsCancel(ex))
		{
			return null;
		}
		catch (COMException ex)
		{
			throw new InvalidOperationException("The scanner could not capture the page. " + ex.Message, ex);
		}
		finally
		{
			Release(image);
			Release(dialog);
		}
	}

	private static bool IsCancel(COMException ex)
	{
		return ex.ErrorCode == unchecked((int)0x800704C7)
			|| ex.ErrorCode == unchecked((int)0x80210064)
			|| ex.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase);
	}

	private static object? Create(string progId)
	{
		Type? type = Type.GetTypeFromProgID(progId);
		return type == null ? null : Activator.CreateInstance(type);
	}

	private static void Release(object? com)
	{
		if (com != null && Marshal.IsComObject(com))
		{
			Marshal.ReleaseComObject(com);
		}
	}
}

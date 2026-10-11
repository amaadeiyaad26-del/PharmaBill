using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace PharmaBill.App.Services;

/// <summary>Acquires a page from a WIA flatbed / document feeder, or a USB document camera as fallback.</summary>
public sealed class HardwareScannerService
{
	private const int ScannerDeviceType = 1;

	private const string JpegFormatId = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}";

	public bool HasWiaScanner()
	{
		return WiaScannerCapture.HasScanner();
	}

	public async Task<string?> CaptureFromHardwareScannerAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (HasWiaScanner())
		{
			string? wiaPath = await Task.Run(CaptureWiaToTemp, cancellationToken).ConfigureAwait(true);
			if (wiaPath != null)
			{
				return wiaPath;
			}

			return null;
		}

		return await CaptureDocumentCameraAsync().ConfigureAwait(true);
	}

	private static Task<string?> CaptureDocumentCameraAsync()
	{
		WebcamCaptureWindow window = new WebcamCaptureWindow(prescriptionOcrMode: false)
		{
			Title = "Document camera / USB scanner",
			Owner = Application.Current?.MainWindow
		};
		if (window.ShowDialog() != true || string.IsNullOrWhiteSpace(window.CapturedPath))
		{
			return Task.FromResult<string?>(null);
		}

		string destination = NewTempScanPath(".jpg");
		File.Copy(window.CapturedPath, destination, overwrite: true);
		return Task.FromResult<string?>(destination);
	}

	private static string? CaptureWiaToTemp()
	{
		object? manager = null;
		object? dialog = null;
		object? device = null;
		object? image = null;
		try
		{
			manager = Create("WIA.DeviceManager");
			if (manager == null)
			{
				throw new InvalidOperationException("Windows Image Acquisition is not available on this PC.");
			}

			dynamic infos = ((dynamic)manager).DeviceInfos;
			dynamic? scannerInfo = null;
			foreach (dynamic info in infos)
			{
				if ((int)info.Type == ScannerDeviceType)
				{
					scannerInfo = info;
					break;
				}
			}

			if (scannerInfo == null)
			{
				return null;
			}

			// Prefer a silent transfer when the device can be connected without a prompt.
			try
			{
				device = scannerInfo.Connect();
				dynamic items = ((dynamic)device).Items;
				if (items != null && items.Count >= 1)
				{
					dynamic item = items[1];
					dialog = Create("WIA.CommonDialog");
					if (dialog != null)
					{
						image = ((dynamic)dialog).ShowTransfer(item, JpegFormatId, true);
					}
				}
			}
			catch
			{
				Release(image);
				image = null;
				Release(dialog);
				dialog = null;
				Release(device);
				device = null;
			}

			if (image == null)
			{
				dialog ??= Create("WIA.CommonDialog");
				if (dialog == null)
				{
					throw new InvalidOperationException("Windows Image Acquisition is not available on this PC.");
				}

				image = ((dynamic)dialog).ShowAcquireImage(ScannerDeviceType, 0, 0, JpegFormatId, false, true, false);
			}

			if (image == null)
			{
				return null;
			}

			string path = NewTempScanPath(".jpg");
			((dynamic)image).SaveFile(path);
			return path;
		}
		catch (COMException ex) when (IsCancel(ex))
		{
			return null;
		}
		catch (COMException ex)
		{
			throw new InvalidOperationException("The handheld / USB scanner could not capture the page. " + ex.Message, ex);
		}
		finally
		{
			Release(image);
			Release(device);
			Release(dialog);
			Release(manager);
		}
	}

	private static string NewTempScanPath(string extension)
	{
		string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "Scans");
		Directory.CreateDirectory(directory);
		return Path.Combine(directory, "TempScan_" + Guid.NewGuid().ToString("N") + extension);
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

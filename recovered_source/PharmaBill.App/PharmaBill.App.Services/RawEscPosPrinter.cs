using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.App.Services;

internal static class RawEscPosPrinter
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DocInfo
	{
		[MarshalAs(UnmanagedType.LPWStr)]
		public string DocumentName;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string? OutputFile;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string DataType;
	}

	private const int ErrorInvalidHandle = 6;

	public static Task PrintAsync(string printerName, string receipt, bool pulseCashDrawer, CancellationToken cancellationToken)
	{
		List<byte> list = new List<byte> { 27, 64 };
		list.AddRange(Encoding.ASCII.GetBytes(receipt));
		list.AddRange(new _003C_003Ez__ReadOnlyArray<byte>(new byte[5] { 10, 10, 29, 86, 0 }));
		if (pulseCashDrawer)
		{
			list.AddRange(new _003C_003Ez__ReadOnlyArray<byte>(new byte[5] { 27, 112, 0, 25, 250 }));
		}
		return PrintBytesAsync(printerName, list.ToArray(), cancellationToken);
	}

	public static Task PrintBytesAsync(string printerName, byte[] buffer, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(buffer, "buffer");
		cancellationToken.ThrowIfCancellationRequested();
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Raw ESC/POS printing is supported only on Windows.");
		}
		if (!OpenPrinter(printerName, out var printerHandle, IntPtr.Zero))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open printer '" + printerName + "'.");
		}
		bool flag = false;
		try
		{
			DocInfo documentInfo = new DocInfo
			{
				DocumentName = "PharmaBill receipt",
				DataType = "RAW"
			};
			if (StartDocPrinter(printerHandle, 1, ref documentInfo) == 0)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the raw printer job.");
			}
			flag = true;
			if (!StartPagePrinter(printerHandle))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the thermal receipt page.");
			}
			if (!WritePrinter(printerHandle, buffer, buffer.Length, out var written))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not write the receipt to the printer.");
			}
			if (written != buffer.Length)
			{
				throw new IOException($"The printer accepted {written} of {buffer.Length} receipt bytes.");
			}
			EndPagePrinter(printerHandle);
			EndDocPrinter(printerHandle);
			flag = false;
			return Task.CompletedTask;
		}
		finally
		{
			if (flag)
			{
				EndDocPrinter(printerHandle);
			}
			if (!ClosePrinter(printerHandle) && Marshal.GetLastWin32Error() == 6)
			{
				throw new Win32Exception(6, "The thermal printer handle became invalid.");
			}
		}
	}

	[DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool OpenPrinter(string printerName, out nint printerHandle, nint defaults);

	[DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern int StartDocPrinter(nint printerHandle, int level, ref DocInfo documentInfo);

	[DllImport("winspool.drv", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool StartPagePrinter(nint printerHandle);

	[DllImport("winspool.drv", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool WritePrinter(nint printerHandle, byte[] buffer, int count, out int written);

	[DllImport("winspool.drv", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EndPagePrinter(nint printerHandle);

	[DllImport("winspool.drv", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EndDocPrinter(nint printerHandle);

	[DllImport("winspool.drv", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool ClosePrinter(nint printerHandle);
}

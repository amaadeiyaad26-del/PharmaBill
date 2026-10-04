using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PharmaBill.App.Services;

internal static class RawEscPosPrinter
{
    private const int ErrorInvalidHandle = 6;

    public static Task PrintAsync(
        string printerName,
        string receipt,
        bool pulseCashDrawer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Raw ESC/POS printing is supported only on Windows.");
        }

        if (!OpenPrinter(printerName, out var printerHandle, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open printer '{printerName}'.");
        }

        var documentStarted = false;
        try
        {
            var document = new DocInfo
            {
                DocumentName = "PharmaBill receipt",
                DataType = "RAW"
            };
            if (StartDocPrinter(printerHandle, 1, ref document) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the raw printer job.");
            }

            documentStarted = true;
            if (!StartPagePrinter(printerHandle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the thermal receipt page.");
            }

            var bytes = new List<byte> { 0x1B, 0x40 };
            bytes.AddRange(Encoding.ASCII.GetBytes(receipt));
            bytes.AddRange([0x0A, 0x0A, 0x1D, 0x56, 0x00]);
            if (pulseCashDrawer)
            {
                bytes.AddRange([0x1B, 0x70, 0x00, 0x19, 0xFA]);
            }

            var buffer = bytes.ToArray();
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
            documentStarted = false;
            return Task.CompletedTask;
        }
        finally
        {
            if (documentStarted)
            {
                EndDocPrinter(printerHandle);
            }

            if (!ClosePrinter(printerHandle) && Marshal.GetLastWin32Error() == ErrorInvalidHandle)
            {
                throw new Win32Exception(ErrorInvalidHandle, "The thermal printer handle became invalid.");
            }
        }
    }

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

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printerHandle, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(IntPtr printerHandle, int level, ref DocInfo documentInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartPagePrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePrinter(IntPtr printerHandle, byte[] buffer, int count, out int written);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPagePrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndDocPrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClosePrinter(IntPtr printerHandle);
}

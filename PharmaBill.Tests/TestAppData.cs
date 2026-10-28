using System.Runtime.CompilerServices;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Tests;

/// <summary>
/// The suite never touches the real AppData folder: every path under AppDataPaths.RootDirectory (attachments, the
/// Gemini key store, invoices…) points at a throwaway folder for this run.
/// </summary>
internal static class TestAppData
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "PharmaBill.Tests.AppData", Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Redirect()
    {
        Directory.CreateDirectory(Root);
        AppDataPaths.UseDataRoot(Root);
    }
}

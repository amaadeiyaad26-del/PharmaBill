using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Controls;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// The Backup page the way a shopkeeper uses it: type the password into the real password boxes, save it, choose a
// folder, back up, lose the records, restore from the file, and get every bill, stock line and register entry back.
public sealed class BackupPageEndToEndTests
{
    private const string Password = "Srinagar-2026";

    [Fact]
    public void PasswordBox_TypingReachesTheViewModel()
    {
        RunSta(() =>
        {
            var target = new PasswordTarget();
            var box = new PasswordBox();
            BindingOperations.SetBinding(box, PasswordBoxBinding.PasswordProperty, new Binding(nameof(PasswordTarget.Value)) { Source = target, Mode = BindingMode.TwoWay });

            box.Password = "eleven-char";
            Assert.Equal("eleven-char", target.Value);

            target.Value = string.Empty;
            Assert.Equal(string.Empty, box.Password);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void SetPassword_BackUp_Wipe_Restore_ThroughThePage()
    {
        string root = Path.Combine(Path.GetTempPath(), $"PharmaBill.BackupPage.{Guid.NewGuid():N}");
        string data = Path.Combine(root, "data-root");
        string backups = Path.Combine(root, "Shop backups");
        try
        {
            RunSta(async () =>
            {
                var storage = new DatabaseStorageOptions(data);
                Directory.CreateDirectory(storage.DataDirectory);
                using ServiceProvider services = FolderBackupTests.BuildServices(storage);
                FolderBackupTests.Before before;
                await using (var context = FolderBackupTests.OpenContext(storage))
                {
                    await FolderBackupTests.InitializeAsync(context, storage);
                    before = await FolderBackupTests.SeedShopAsync(context, storage);
                }

                var picker = new FakePicker { Folder = backups };
                var page = new BackupPageViewModel(services.GetRequiredService<BackupSettingsStore>(), services.GetRequiredService<FolderBackupService>(),
                    new UsbBackupService(services.GetRequiredService<IServiceScopeFactory>(), new UsbBackupSettingsStore(storage), services.GetRequiredService<BackupSettingsStore>(), storage),
                    new UsbBackupSettingsStore(storage), picker, new AlwaysYes(), new CurrentSession());
                bool closeRequested = false;
                page.CloseApplication = () => closeRequested = true;
                await page.LoadAsync();

                // No password yet: backing up is refused with a plain instruction.
                await page.BackUpNowCommand.ExecuteAsync(null);
                Assert.Contains("press Save password first", page.ErrorMessage, StringComparison.OrdinalIgnoreCase);

                // Typed into the real password boxes, exactly as on screen.
                var first = new PasswordBox();
                var second = new PasswordBox();
                BindingOperations.SetBinding(first, PasswordBoxBinding.PasswordProperty, new Binding(nameof(BackupPageViewModel.NewPassword)) { Source = page, Mode = BindingMode.TwoWay });
                BindingOperations.SetBinding(second, PasswordBoxBinding.PasswordProperty, new Binding(nameof(BackupPageViewModel.ConfirmPassword)) { Source = page, Mode = BindingMode.TwoWay });
                first.Password = Password;
                second.Password = Password + "x";
                page.SavePasswordCommand.Execute(null);
                Assert.Contains("not the same", page.ErrorMessage);
                Assert.DoesNotContain("Parameter", page.ErrorMessage);
                second.Password = Password;
                Assert.Equal(string.Empty, page.ErrorMessage);

                // Typed but not saved: still refused, and says why.
                await page.BackUpNowCommand.ExecuteAsync(null);
                Assert.Contains("press Save password first", page.ErrorMessage, StringComparison.OrdinalIgnoreCase);

                page.SavePasswordCommand.Execute(null);
                Assert.Equal(string.Empty, page.ErrorMessage);
                Assert.True(page.HasSavedPassword);

                page.ChooseFolderCommand.Execute(null);
                Assert.Equal(backups, page.BackupFolder);
                await page.BackUpNowCommand.ExecuteAsync(null);
                Assert.Equal(string.Empty, page.ErrorMessage);
                string backup = Directory.GetFiles(backups, "*.pbbak").Single();
                Assert.True(new FileInfo(backup).Length > 0);
                Assert.Contains(Path.GetFileName(backup), page.StatusMessage);
                Assert.DoesNotContain("No backup yet", page.LastBackupLabel);

                // The PC's records are lost - database, key, attachments and saved settings - and Pharma Remit starts
                // empty. Only the backup file survives. It is restored with the password typed on the Restore line.
                SqliteConnection.ClearAllPools();
                Directory.Delete(data, recursive: true);
                Directory.CreateDirectory(storage.DataDirectory);
                await using (var empty = FolderBackupTests.OpenContext(storage))
                {
                    // Start-up initialises each database file once per process, so build this fresh one directly.
                    await empty.Database.MigrateAsync();
                    Assert.Equal(0, await empty.Sales.CountAsync());
                }
                var restoreBox = new PasswordBox();
                BindingOperations.SetBinding(restoreBox, PasswordBoxBinding.PasswordProperty, new Binding(nameof(BackupPageViewModel.RestorePassword)) { Source = page, Mode = BindingMode.TwoWay });
                restoreBox.Password = Password;
                picker.BackupFile = backup;
                await page.RestoreBackupCommand.ExecuteAsync(null);
                Assert.True(page.ErrorMessage.Length == 0, page.ErrorMessage);
                Assert.StartsWith("Restored.", page.StatusMessage);
                Assert.True(closeRequested, "the page did not close Pharma Remit after the restore");
                SqliteConnection.ClearAllPools();

                await using var restored = FolderBackupTests.OpenContext(storage);
                Assert.Equal(before.InvoiceNumbers, await restored.Sales.OrderBy(s => s.InvoiceNo).Select(s => s.InvoiceNo).ToListAsync());
                Assert.Equal(before.SalesTotal, (await restored.Sales.Select(s => s.TotalAmount).ToListAsync()).Sum());
                Assert.Equal(before.StockOnHand, await FolderBackupTests.StockOnHandAsync(restored));
                ScheduleRegisterEntry entry = await restored.ScheduleRegisterEntries.SingleAsync(e => e.SaleId != null);
                Assert.Equal(before.RegisterEntry, FolderBackupTests.Describe(entry));
                string? scan = await restored.Sales.Where(s => s.PrescriptionFilePath != null).Select(s => s.PrescriptionFilePath).SingleAsync();
                Assert.True(File.Exists(scan), "prescription scan did not come back: " + scan);
            });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Downloads_IsRefusedAsABackupFolder()
    {
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Assert.True(FolderBackupService.IsInDownloads(downloads) || !Directory.Exists(downloads));
        Assert.False(FolderBackupService.IsInDownloads(FolderBackupService.DefaultFolder));
        Assert.EndsWith("Pharma Remit Backups", FolderBackupService.DefaultFolder);
    }

    private static void RunSta(Func<Task> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await body(); }
                catch (Exception ex) { failure = ex; }
                finally { dispatcher.InvokeShutdown(); }
            });
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class PasswordTarget : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        private string _value = string.Empty;
        public string Value { get => _value; set => SetProperty(ref _value, value); }
    }

    private sealed class AlwaysYes : IConfirmationService
    {
        public bool Confirm(string message, string title) => true;
        public bool? Choose(string message, string title, string acceptLabel, string alternateLabel) => true;
        public void Notify(string title, string message) { }
        public void NotifyError(string title, string message) { }
    }

    private sealed class FakePicker : IFilePickerService
    {
        public string? Folder { get; set; }
        public string? BackupFile { get; set; }
        public string? PickFolder(string title) => Folder;
        public string? PickBackupFile() => BackupFile;
        public string? PickLicenceDocument() => null;
        public string? PickCsvFile() => null;
        public string? PickPurchaseDocument() => null;
        public IReadOnlyList<string> PickInvoiceDocuments() => [];
        public string? PickPurchaseSpreadsheet() => null;
        public string? PickCustomerExcel() => null;
        public string? PickPrescriptionDocument() => null;
        public string? PickExportDestination(string extension, string suggestedName) => null;
        public string? PickSyncPackage() => null;
    }
}

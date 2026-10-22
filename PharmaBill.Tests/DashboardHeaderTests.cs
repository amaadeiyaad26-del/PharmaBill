using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.ViewModels;
using PharmaBill.App.Views;
using Xunit;

namespace PharmaBill.Tests;

// The top of the Retail and Wholesale dashboards: "Reports & Daily MIS" opens Reports; the Sync Station shortcut is gone
// from the header (Sync itself stays in the sidebar and Settings).
public sealed class DashboardHeaderTests
{
    [Fact]
    public void ReportsShortcut_OnBothDashboards_OpensReports()
    {
        Ui.Run(async () =>
        {
            var trace = new TraceCapture();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(trace);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            var window = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(window);
            var retailTemplate = (DataTemplate)window.Resources[new DataTemplateKey(typeof(DashboardPageViewModel))];

            foreach (Func<FrameworkElement> dashboard in new Func<FrameworkElement>[]
            {
                () => new ContentControl { Content = RuntimeHelpers.GetUninitializedObject(typeof(DashboardPageViewModel)), ContentTemplate = retailTemplate, Resources = window.Resources },
                () => new WholesaleDashboardView { DataContext = RuntimeHelpers.GetUninitializedObject(typeof(WholesaleDashboardPageViewModel)) },
            })
            {
                // As in the app: the dashboard is opened inside the main window, whose view-model is already in place.
                var shell = new DashboardHeaderShell();
                var host = new Window { DataContext = shell, Width = 1366, Height = 768 };
                Ui.Prepare(host);
                host.Show();
                host.Content = dashboard();
                await Ui.SettleAsync();

                Button reports = Ui.Button(host, "Reports & Daily MIS");
                BindingExpression? binding = BindingOperations.GetBindingExpression(reports, ButtonBase.CommandProperty);
                Assert.True(reports.Command != null, $"command not bound ({binding?.Status}): " + string.Join(" | ", trace.Lines.Where(l => l.Contains("NavigateCommand")).Take(3)));
                await Ui.PressAsync(reports);
                Assert.Equal(["Reports"], shell.Opened);
                Assert.DoesNotContain(Descendants(host).OfType<Button>(), b => b.Content as string == "Sync Station" || (b.CommandParameter as string) == "Sync");
                host.Close();
            }
            PresentationTraceSources.DataBindingSource.Listeners.Remove(trace);
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (DependencyObject child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root, i)))
            {
                yield return child;
            }
        }
    }
}

// Stands in for the main window's view-model: the dashboard header buttons call its NavigateCommand.
public sealed class DashboardHeaderShell
{
    public DashboardHeaderShell() => NavigateCommand = new RelayCommand<string>(key => Opened.Add(key!));

    public List<string> Opened { get; } = [];

    public System.Windows.Input.ICommand NavigateCommand { get; }
}

public sealed class TraceCapture : TraceListener
{
    public List<string> Lines { get; } = [];

    public override void Write(string? message) { }

    public override void WriteLine(string? message)
    {
        if (message != null)
        {
            Lines.Add(message);
        }
    }
}

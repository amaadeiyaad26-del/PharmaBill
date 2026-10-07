using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using PharmaBill.App.ViewModels;
using Xunit;

namespace PharmaBill.Tests;

public sealed class BindingSafetyTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string AppDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "PharmaBill.App")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine(dir!, "PharmaBill.App");
    }

    private static Type? ResolveType(string? typeExtension)
    {
        var match = Regex.Match(typeExtension ?? string.Empty, @"\{x:Type\s+vm:(\w+)\}");
        return match.Success ? typeof(MainWindowViewModel).Assembly.GetType($"PharmaBill.App.ViewModels.{match.Groups[1].Value}") : null;
    }

    private static string? SimplePath(string? binding)
    {
        var match = Regex.Match(binding ?? string.Empty, @"^\{Binding\s+(?:Path=)?([A-Za-z_]\w*)\s*(?:,|\})");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static Type? ElementType(Type collection) =>
        collection.GetInterfaces().Append(collection)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];

    [Fact]
    public void DataGrid_columns_never_two_way_bind_read_only_properties()
    {
        var problems = new List<string>();
        var checkedColumns = 0;
        foreach (var file in Directory.EnumerateFiles(AppDirectory(), "*.xaml", SearchOption.AllDirectories)
                     .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\")))
        {
            var document = XDocument.Load(file);
            foreach (var grid in document.Descendants(Wpf + "DataGrid"))
            {
                var template = grid.Ancestors(Wpf + "DataTemplate").FirstOrDefault();
                var viewModelType = ResolveType((string?)template?.Attribute("DataType"));
                var itemsPath = SimplePath((string?)grid.Attribute("ItemsSource"));
                var itemType = viewModelType is null || itemsPath is null
                    ? null
                    : ElementType(viewModelType.GetProperty(itemsPath)?.PropertyType ?? typeof(object));
                if (itemType is null)
                {
                    continue;
                }

                foreach (var column in grid.Descendants().Where(e => e.Name.LocalName is "DataGridTextColumn" or "DataGridCheckBoxColumn" or "DataGridComboBoxColumn"))
                {
                    var binding = (string?)column.Attribute("Binding");
                    var path = SimplePath(binding);
                    if (path is null || binding!.Contains("Mode=", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    checkedColumns++;
                    var property = itemType.GetProperty(path);
                    if (property is not null && (!property.CanWrite || property.SetMethod is { IsPublic: false }))
                    {
                        problems.Add($"{Path.GetFileName(file)}: {itemType.Name}.{path} is read-only but the column binding is TwoWay");
                    }
                }
            }
        }

        Assert.True(checkedColumns > 0);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private sealed class BindingTrace : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { if (message is not null) Messages.Add(message); }
    }

    [Fact]
    public void Every_page_template_loads_and_lays_out_without_binding_errors()
    {
        Exception? failure = null;
        var listener = new BindingTrace();
        var thread = new Thread(() =>
        {
            try
            {
                _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Clear();
                foreach (var path in new[] { "Themes/Light.xaml", "Resources/Strings.en.xaml", "Resources/Controls.xaml" })
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/PharmaBill.App;component/{path}", UriKind.Absolute)
                    });
                }

                PresentationTraceSources.Refresh();
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning | SourceLevels.Error;

                var window = new PharmaBill.App.MainWindow(null!, null!, null!, null!);
                foreach (var key in window.Resources.Keys.OfType<DataTemplateKey>())
                {
                    var template = (DataTemplate)window.Resources[key];
                    var type = (Type)key.DataType;
                    var viewModel = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
                    var host = new ContentControl
                    {
                        Content = viewModel,
                        ContentTemplate = template,
                        Width = 1366,
                        Height = 768,
                        Resources = window.Resources
                    };
                    host.Measure(new Size(1366, 768));
                    host.Arrange(new Rect(0, 0, 1366, 768));
                    host.UpdateLayout();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.True(failure is null, failure?.ToString());
        var pathErrors = listener.Messages
            .Where(m => m.Contains("BindingExpression path error", StringComparison.Ordinal) &&
                        !m.Contains("RelativeSource", StringComparison.Ordinal))
            .ToList();
        Assert.True(pathErrors.Count == 0, string.Join(Environment.NewLine, pathErrors.Take(10)));
    }
}
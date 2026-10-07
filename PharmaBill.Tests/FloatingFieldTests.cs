using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PharmaBill.App.Controls;
using Xunit;

namespace PharmaBill.Tests;

public sealed class FloatingFieldTests
{
    private static void RunSta(Action action) => StaRunner.Run(action);

    private static ResourceDictionary Load(string path) =>
        new() { Source = new Uri($"pack://application:,,,/PharmaBill.App;component/{path}", UriKind.Absolute) };

    [Fact]
    public void Floating_field_renders_in_light_and_dark_and_tracks_value_state()
    {
        RunSta(() =>
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var theme in new[] { "Themes/Light.xaml", "Themes/Dark.xaml" })
            {
                var resources = new ResourceDictionary();
                resources.MergedDictionaries.Add(Load(theme));
                resources.MergedDictionaries.Add(Load("Resources/Controls.xaml"));

                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(Load(theme));
                Application.Current.Resources.MergedDictionaries.Add(Load("Resources/Controls.xaml"));

                var empty = new TextBox();
                var filled = new TextBox { Text = "Asha" };
                var combo = new ComboBox { ItemsSource = new[] { "Cash", "UPI" }, SelectedIndex = 0 };
                var error = new TextBox();
                var panel = new StackPanel { Width = 360, Resources = resources, Background = (Brush)resources["WindowBackgroundBrush"] };
                var fields = new[]
                {
                    new FloatingField { Label = "Patient name", IsRequired = true, Content = empty, ErrorText = "Patient name is required" },
                    new FloatingField { Label = "Phone number", Content = filled, HelperText = "10 digits" },
                    new FloatingField { Label = "Payment mode", Content = combo },
                    new FloatingField { Label = "Change due (\u20B9)", Content = new TextBox { IsReadOnly = true, Text = "12.50" } },
                    new FloatingField { Label = "Other", Content = error }
                };
                foreach (var field in fields) { panel.Children.Add(field); }
                panel.Measure(new Size(360, 600));
                panel.Arrange(new Rect(0, 0, 360, panel.DesiredSize.Height));
                panel.UpdateLayout();

                Assert.False(fields[0].HasValue);
                Assert.True(fields[0].HasError);
                Assert.True(fields[1].HasValue);
                Assert.True(fields[2].HasValue);
                Assert.True(fields[3].IsContentReadOnly);
                Assert.Equal(48, fields[4].ActualHeight, 1);

                empty.Text = "typed";
                Assert.True(fields[0].HasValue);

                var bitmap = new RenderTargetBitmap(360, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(panel);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(Path.GetTempPath(), $"floating-{Path.GetFileNameWithoutExtension(theme)}.png"));
                encoder.Save(stream);
            }
        });
    }

    [Fact]
    public void Retail_billing_template_loads_and_renders()
    {
        RunSta(() =>
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var theme in new[] { "Themes/Light.xaml", "Themes/Dark.xaml" })
            {
                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(Load(theme));
                Application.Current.Resources.MergedDictionaries.Add(Load("Resources/Strings.en.xaml"));
                Application.Current.Resources.MergedDictionaries.Add(Load("Resources/Controls.xaml"));

                var window = new PharmaBill.App.MainWindow(null!, null!, null!, null!);
                var template = (DataTemplate)window.Resources[new DataTemplateKey(typeof(PharmaBill.App.ViewModels.RetailBillingViewModel))]!;
                var viewModel = new PharmaBill.App.ViewModels.RetailBillingViewModel(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)
                {
                    PatientNameError = "Patient name is required",
                    PrescriptionFileName = "rx-scan.pdf"
                };
                var host = new ContentControl
                {
                    Content = viewModel,
                    ContentTemplate = template,
                    Width = 1366,
                    Height = 640,
                    Resources = window.Resources,
                    Background = (Brush)Application.Current.Resources["WindowBackgroundBrush"]
                };
                host.Measure(new Size(1366, 640));
                host.Arrange(new Rect(0, 0, 1366, 640));
                host.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1366, 640, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(host);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(Path.GetTempPath(), $"retail-{Path.GetFileNameWithoutExtension(theme)}.png"));
                encoder.Save(stream);
            }
        });
    }
}
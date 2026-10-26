using System.Diagnostics;
using Xunit;

namespace PharmaBill.Tests;

/// <summary>
/// Records the binding errors WPF reports while a screen test runs (the lines the Output window would show), so a
/// test that drives a screen with real data can assert that every binding on it resolved.
/// </summary>
internal sealed class BindingErrorWatch : IDisposable
{
    private readonly Recorder _recorder = new();

    private BindingErrorWatch()
    {
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning | SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(_recorder);
    }

    public static BindingErrorWatch Start() => new();

    public void AssertNone()
    {
        List<string> errors;
        lock (_recorder.Messages)
        {
            errors = _recorder.Messages.ToList();
        }
        errors = errors
            .Where(message => message.Contains("BindingExpression path error", StringComparison.Ordinal)
                || message.Contains("Cannot find source for binding", StringComparison.Ordinal)
                || message.Contains("Cannot convert", StringComparison.Ordinal))
            // Bindings to the main window's own view model (shortcuts, licence banner) cannot resolve when a page template
            // is hosted on its own, and the never-shown MainWindow these tests borrow templates from carries them too.
            .Where(message => !message.Contains("AncestorType='System.Windows.Window'", StringComparison.Ordinal))
            .Distinct()
            .ToList();
        Assert.True(errors.Count == 0, "Binding errors:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Take(10)));
    }

    public void Dispose() => PresentationTraceSources.DataBindingSource.Listeners.Remove(_recorder);

    private sealed class Recorder : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (message is not null)
            {
                lock (Messages)
                {
                    Messages.Add(message);
                }
            }
        }
    }
}

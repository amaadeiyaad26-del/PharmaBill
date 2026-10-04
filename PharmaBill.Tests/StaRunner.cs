using System.Collections.Concurrent;

namespace PharmaBill.Tests;

// WPF allows one Application per AppDomain, so all UI tests share one long-lived STA thread.
internal static class StaRunner
{
    private static readonly BlockingCollection<(Action Work, TaskCompletionSource Done)> Queue = new();

    static StaRunner()
    {
        var thread = new Thread(() =>
        {
            foreach (var (work, done) in Queue.GetConsumingEnumerable())
            {
                try { work(); done.SetResult(); } catch (Exception exception) { done.SetException(exception); }
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static void Run(Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add((work, done));
        try { done.Task.GetAwaiter().GetResult(); }
        catch (Exception exception) { throw new InvalidOperationException(exception.Message, exception); }
    }
}

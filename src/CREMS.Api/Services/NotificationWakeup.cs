namespace CREMS.Api.Services;

// Carries no message content; each waiting request rechecks its own inbox permissions.
public static class NotificationWakeup
{
    private static readonly object Gate = new();
    private static long version;
    private static TaskCompletionSource signal = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static long Version { get { lock (Gate) return version; } }
    public static void Publish()
    {
        TaskCompletionSource previous;
        lock (Gate) { version++; previous = signal; signal = NewSignal(); }
        previous.TrySetResult();
    }
    public static async Task WaitAsync(long since, CancellationToken token)
    {
        Task pending;
        lock (Gate) { if (since != version) return; pending = signal.Task; }
        try { await pending.WaitAsync(TimeSpan.FromSeconds(25), token); }
        catch (TimeoutException) { }
    }
}

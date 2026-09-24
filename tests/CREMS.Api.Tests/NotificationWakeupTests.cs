using CREMS.Api.Services;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class NotificationWakeupTests
{
    [Fact]
    public async Task Publishing_wakes_connected_recipients_and_reconnects_do_not_miss_changes()
    {
        var before = NotificationWakeup.Version;
        var waiting = NotificationWakeup.WaitAsync(before, TestContext.Current.CancellationToken);
        NotificationWakeup.Publish();
        await waiting.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.NotEqual(before, NotificationWakeup.Version);
        await NotificationWakeup.WaitAsync(before, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Disconnected_request_cancels_without_waiting_for_another_message()
    {
        using var cancellation = new CancellationTokenSource();
        var waiting = NotificationWakeup.WaitAsync(NotificationWakeup.Version, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}

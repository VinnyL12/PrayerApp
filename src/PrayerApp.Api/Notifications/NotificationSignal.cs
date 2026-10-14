using System.Threading.Channels;

namespace PrayerApp.Api.Notifications;

/// <summary>
/// A doorbell between request handlers and <see cref="NotificationProcessor"/>: "there are
/// new rows in the outbox". It carries no data (the rows are the data), so losing it costs
/// nothing but latency; the processor also checks the table on startup and on a timer.
///
/// A channel with room for one item that drops further writes: fifty acknowledgments
/// arriving together ring once, and the processor's next drain picks all fifty up.
/// </summary>
public sealed class NotificationSignal
{
    private readonly Channel<bool> _bell = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>Call after the outbox row has committed. Never blocks.</summary>
    public void Ring() => _bell.Writer.TryWrite(true);

    /// <summary>Waits until rung or until the timeout passes, whichever comes first.</summary>
    public async Task WaitAsync(TimeSpan timeout, TimeProvider time, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await _bell.Reader.ReadAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timed out: time for the periodic check.
        }
    }
}

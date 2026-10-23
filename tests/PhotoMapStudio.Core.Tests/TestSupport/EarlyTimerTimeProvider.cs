using System.Threading.Channels;

using Microsoft.Extensions.Time.Testing;

namespace PhotoMapStudio.Core.Tests.TestSupport;

// OSタイマーが期限より早く通知する状況を、実時間を使わず再現する。
internal sealed class EarlyTimerTimeProvider : TimeProvider
{
    private readonly FakeTimeProvider clock = new();
    private readonly Channel<TimeSpan> scheduled = Channel.CreateUnbounded<TimeSpan>();

    public override DateTimeOffset GetUtcNow() => this.clock.GetUtcNow();
    public override long GetTimestamp() => this.clock.GetTimestamp();
    public override long TimestampFrequency => this.clock.TimestampFrequency;
    public void Advance(TimeSpan elapsed) => this.clock.Advance(elapsed);
    public Task<TimeSpan> NextDelayAsync()
        => this.scheduled.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ITimer timer = this.clock.CreateTimer(callback, state, dueTime > TimeSpan.Zero ? dueTime / 2 : dueTime, period);
        this.scheduled.Writer.TryWrite(dueTime);
        return timer;
    }
}

using PhotoMapStudio.Core.Tests.TestSupport;
using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public sealed class RequestIntervalGateTimingTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(1000)]
    public async Task タイマーの早い通知後も期限まで次の要求を通さない(int milliseconds)
    {
        var clock = new EarlyTimerTimeProvider();
        TimeSpan interval = TimeSpan.FromMilliseconds(milliseconds);
        using var gate = new RequestIntervalGate(new TileRateLimit(1, interval), clock);
        using (await gate.EnterAsync(CancellationToken.None)) { }
        Task<IDisposable> pending = gate.EnterAsync(CancellationToken.None);
        Assert.Equal(interval, await clock.NextDelayAsync());
        Assert.False(pending.IsCompleted);
        clock.Advance(interval / 2);
        Task<TimeSpan> remaining = clock.NextDelayAsync();
        await Task.WhenAny(pending, remaining).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pending.IsCompleted);
        Assert.Equal(TimeSpan.FromMilliseconds(Math.Ceiling(interval.TotalMilliseconds / 2)), await remaining.ConfigureAwait(true));
        clock.Advance(interval / 2);
        using (await pending.WaitAsync(TimeSpan.FromSeconds(5))) { }
    }
}

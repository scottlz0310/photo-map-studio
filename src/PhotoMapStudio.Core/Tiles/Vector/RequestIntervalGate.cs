namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// 連続する要求の間に、最小間隔を空けるための関門。要求は 1 つずつ通す。
/// </summary>
/// <remarks>
/// 間隔が十分に短い方針（この PC 内の配信元など）では待たない。
/// </remarks>
internal sealed class RequestIntervalGate : IDisposable
{
    // これ未満の間隔は、他者の配信元への負荷を気にする必要がない（ループバック用の設定）として待たない
    private static readonly TimeSpan MinimumGatedInterval = TimeSpan.FromMilliseconds(50);

    private readonly TimeSpan interval;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset nextAllowed = DateTimeOffset.MinValue;

    public RequestIntervalGate(TileRateLimit rateLimit, TimeProvider timeProvider)
    {
        this.interval = rateLimit.MinimumInterval;
        this.timeProvider = timeProvider;
    }

    public bool IsActive => this.interval >= MinimumGatedInterval;

    /// <summary>
    /// 要求を出してよい時刻まで待つ。完了まで次の要求を通さない（戻り値を破棄すると次の要求を通す）。
    /// </summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        if (!this.IsActive)
        {
            return NullLease.Instance;
        }

        await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = this.timeProvider.GetUtcNow();
            TimeSpan wait = this.nextAllowed - now;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, this.timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            this.gate.Release();
            throw;
        }

        return new Lease(this);
    }

    public void Dispose() => this.gate.Dispose();

    private void Exit()
    {
        this.nextAllowed = this.timeProvider.GetUtcNow() + this.interval;
        this.gate.Release();
    }

    private sealed class Lease(RequestIntervalGate owner) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.released, 1) == 0)
            {
                owner.Exit();
            }
        }
    }

    private sealed class NullLease : IDisposable
    {
        public static readonly NullLease Instance = new();

        public void Dispose()
        {
        }
    }
}

using System.Net;

namespace PhotoMapStudio.Core.Tiles;

/// <summary>一括生成単位の取得量と連続失敗を保持する。</summary>
public sealed class TileFetchSession(TimeProvider timeProvider)
{
    /// <summary>連続失敗による中止の閾値。</summary>
    public const int FailureLimit = 10;
    private readonly long started = timeProvider.GetTimestamp();
    private readonly Queue<long> recentRequests = new();
    private readonly object synchronization = new();
    private int networkCount;
    private int cacheCount;
    private int consecutiveFailures;
    private int maximumRate;
    private TileFetchException? stoppedBy;

    /// <summary>取得・キャッシュ命中の途中経過の通知先。</summary>
    public IProgress<TileFetchStatistics>? Progress { get; set; }

    /// <summary>連続失敗の閾値に到達したかどうか。</summary>
    public bool IsStopped { get { lock (this.synchronization) { return this.stoppedBy is not null; } } }

    /// <summary>ネットワーク要求を記録する。</summary>
    public void RecordRequest()
    {
        lock (this.synchronization)
        {
            this.ThrowIfStopped();
            long now = timeProvider.GetTimestamp();
            while (this.recentRequests.TryPeek(out long oldest) && timeProvider.GetElapsedTime(oldest, now) >= TimeSpan.FromSeconds(1))
            {
                this.recentRequests.Dequeue();
            }

            this.recentRequests.Enqueue(now);
            this.networkCount++;
            this.maximumRate = Math.Max(this.maximumRate, this.recentRequests.Count);
        }
        this.Progress?.Report(this.Snapshot());
    }

    /// <summary>成功を記録する。配信元の連続失敗はネットワーク取得の成功で解除する。</summary>
    public void RecordSuccess(bool cached)
    {
        lock (this.synchronization)
        {
            if (cached)
            {
                this.cacheCount++;
            }
            else
            {
                this.consecutiveFailures = 0;
            }
        }
        this.Progress?.Report(this.Snapshot());
    }

    /// <summary>404 以外の取得失敗を記録する。</summary>
    public void RecordFailure(TileFetchException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (this.synchronization)
        {
            if (exception.StatusCode != HttpStatusCode.NotFound && ++this.consecutiveFailures >= FailureLimit)
            {
                this.stoppedBy = exception;
            }
        }
    }

    /// <summary>中止後に新しい要求を送らない。</summary>
    public void ThrowIfStopped()
    {
        lock (this.synchronization)
        {
            if (this.stoppedBy is not null)
            {
                throw new InvalidOperationException($"タイル取得が連続{FailureLimit}件失敗したため中止しました。{this.stoppedBy.Message}", this.stoppedBy);
            }
        }
    }

    /// <summary>現在の取得統計。</summary>
    public TileFetchStatistics Snapshot()
    {
        lock (this.synchronization)
        {
            double seconds = timeProvider.GetElapsedTime(this.started).TotalSeconds;
            return new(this.networkCount, this.cacheCount, seconds > 0 ? this.networkCount / seconds : 0, this.maximumRate);
        }
    }
}

/// <summary>要求数と、開始からの平均・直近1秒窓の最大要求数。</summary>
public sealed record TileFetchStatistics(int NetworkCount, int CacheCount, double AverageRate, int MaximumRate);

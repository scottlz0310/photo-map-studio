using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.Tiles.Vector.Pmtiles;

namespace PhotoMapStudio.Tiles.Vector;

/// <summary>
/// 利用者が指定したベクタータイルの取得元（<see cref="VectorSourceSpec"/>）から、取得元を組み立てて保持する。
/// </summary>
/// <remarks>
/// 他者の配信元（HTTP）はキャッシュを通し、ネットワーク要求の間隔を空ける。この PC 上のファイルはキャッシュも間隔も使わない。
/// 同じ指定は 1 つの取得元を共有する（PMTiles のディレクトリを再取得しない）。
/// </remarks>
public sealed class VectorTileSourceFactory : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly ITileCache cache;
    private readonly TimeProvider timeProvider;
    private readonly Lock gate = new();
    private readonly ConcurrentDictionary<VectorSourceSpec, IVectorTileSource> sources = new();
    private bool disposed;

    /// <summary>
    /// 取得元の生成器を初期化する。
    /// </summary>
    /// <param name="httpClient">HTTP クライアント。</param>
    /// <param name="cache">HTTP の取得元に使うタイルキャッシュ。</param>
    /// <param name="timeProvider">要求間隔の判定に使う時刻源。</param>
    public VectorTileSourceFactory(HttpClient httpClient, ITileCache cache, TimeProvider? timeProvider = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 指定に対応する取得元を返す。
    /// </summary>
    /// <param name="spec">利用者が指定した取得元。</param>
    /// <returns>取得元。同じ指定には同じインスタンスを返す。</returns>
    public IVectorTileSource Resolve(VectorSourceSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ObjectDisposedException.ThrowIf(this.disposed, this);

        lock (this.gate)
        {
            if (this.sources.TryGetValue(spec, out IVectorTileSource? existing))
            {
                return existing;
            }

            // ファイルは使用中の間ロックされるため、別のファイルに切り替わったら前のファイルを閉じる
            if (spec.Kind == VectorSourceKind.PmtilesFile)
            {
                foreach (VectorSourceSpec other in this.sources.Keys.Where(k => k.Kind == VectorSourceKind.PmtilesFile).ToArray())
                {
                    if (this.sources.TryRemove(other, out IVectorTileSource? previous))
                    {
                        (previous as IDisposable)?.Dispose();
                    }
                }
            }

            IVectorTileSource created = this.Create(spec);
            this.sources[spec] = created;
            return created;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (this.gate)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            foreach (IVectorTileSource source in this.sources.Values)
            {
                (source as IDisposable)?.Dispose();
            }

            this.sources.Clear();
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "生成した取得元・関門の所有権は戻り値の取得元に移り、ファクトリーの破棄時に破棄する。")]
    private IVectorTileSource Create(VectorSourceSpec spec)
    {
        switch (spec.Kind)
        {
            case VectorSourceKind.PmtilesFile:
                // この PC 上のファイル。通信しないため、キャッシュも間隔も使わない
                return new PmtilesVectorTileSource(
                    new FilePmtilesByteSource(spec.Location),
                    $"pmtiles-file:{spec.Location}");

            case VectorSourceKind.PmtilesUrl:
                {
                    var gate = new RequestIntervalGate(spec.RateLimit, this.timeProvider);
                    IPmtilesByteSource bytes = new RateLimitedPmtilesByteSource(
                        new HttpPmtilesByteSource(this.httpClient, new Uri(spec.Location), maxConcurrency: 2),
                        gate);
                    var pmtiles = new PmtilesVectorTileSource(bytes, $"pmtiles-url:{spec.Location}");
                    return new OwnedCachedVectorTileSource(pmtiles, this.cache);
                }

            default:
                {
                    var gate = new RequestIntervalGate(spec.RateLimit, this.timeProvider);
                    var xyz = new OpenFreeMapTileSource(this.httpClient, urlTemplate: spec.Location, maxZoom: spec.MaxZoom);
                    return new OwnedCachedVectorTileSource(new RateLimitedVectorTileSource(xyz, gate), this.cache);
                }
        }
    }

    /// <summary>キャッシュ付きの取得元。破棄すると内側の取得元も破棄する。</summary>
    private sealed class OwnedCachedVectorTileSource : IVectorTileSource, IDisposable
    {
        private readonly IVectorTileSource inner;
        private readonly CachedVectorTileSource cached;

        public OwnedCachedVectorTileSource(IVectorTileSource inner, ITileCache cache)
        {
            this.inner = inner;
            this.cached = new CachedVectorTileSource(inner, cache);
        }

        public string SourceId => this.cached.SourceId;

        public int MaxZoom => this.cached.MaxZoom;

        public Task<string> GetSourceIdAsync(CancellationToken cancellationToken = default)
            => this.cached.GetSourceIdAsync(cancellationToken);

        public Task PrepareAsync(TileFetchSession? session, CancellationToken cancellationToken = default)
            => this.cached.PrepareAsync(session, cancellationToken);

        public Task<byte[]?> GetTileAsync(int z, int x, int y, TileFetchSession? session, CancellationToken cancellationToken = default)
            => this.cached.GetTileAsync(z, x, y, session, cancellationToken);

        public void Dispose() => (this.inner as IDisposable)?.Dispose();
    }
}

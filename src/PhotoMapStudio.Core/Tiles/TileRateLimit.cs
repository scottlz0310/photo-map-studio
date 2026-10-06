namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// タイルソースごとのレート制御方針。
/// </summary>
/// <param name="MaxConcurrentRequests">同時に発行してよいリクエスト数。</param>
/// <param name="MinimumInterval">連続するリクエストの最小間隔。</param>
public readonly record struct TileRateLimit(int MaxConcurrentRequests, TimeSpan MinimumInterval)
{
    /// <summary>OSM Tile Usage Policy に配慮した控えめな設定。</summary>
    public static TileRateLimit Conservative { get; } = new(2, TimeSpan.FromMilliseconds(125));

    /// <summary>OSM公式サーバーの対話プレビューに適用する単一接続の設定。</summary>
    public static TileRateLimit OpenStreetMap { get; } = new(1, TimeSpan.FromMilliseconds(125));

    /// <summary>公的機関のタイル配信など、比較的余裕のある設定。</summary>
    public static TileRateLimit Relaxed { get; } = new(4, TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// この PC 上の配信元（ループバック）用の設定。他者のサーバーに負荷を掛けない。
    /// 静的配信の実測（約 150〜260 件/秒で頭打ち）に対して、上限が 200 件/秒になる値。
    /// </summary>
    public static TileRateLimit Loopback { get; } = new(8, TimeSpan.FromMilliseconds(5));
}

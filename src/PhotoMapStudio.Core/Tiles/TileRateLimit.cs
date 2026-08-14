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

    /// <summary>一括生成で OSM に適用する単一接続の設定。</summary>
    public static TileRateLimit OpenStreetMap { get; } = new(1, TimeSpan.FromMilliseconds(125));

    /// <summary>公的機関のタイル配信など、比較的余裕のある設定。</summary>
    public static TileRateLimit Relaxed { get; } = new(4, TimeSpan.FromMilliseconds(50));
}

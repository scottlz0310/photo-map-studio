namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// キャッシュを介してタイル画像を供給する。
/// </summary>
public interface ITileProvider
{
    /// <summary>
    /// タイル画像を取得する。キャッシュにあればそれを返し、なければ取得して保存する。
    /// </summary>
    /// <param name="source">タイルソース。</param>
    /// <param name="zoom">ズームレベル。</param>
    /// <param name="x">タイル番号 X。</param>
    /// <param name="y">タイル番号 Y。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>タイル画像のバイト列。</returns>
    /// <exception cref="TileFetchException">取得に失敗した場合。</exception>
    Task<byte[]> GetTileAsync(TileSource source, int zoom, int x, int y, CancellationToken cancellationToken);
}

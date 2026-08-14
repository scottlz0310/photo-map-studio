namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// タイル配信元からタイル画像を取得する。
/// </summary>
public interface ITileClient
{
    /// <summary>
    /// タイル画像を取得する。
    /// </summary>
    /// <param name="source">タイルソース。</param>
    /// <param name="zoom">ズームレベル。</param>
    /// <param name="x">タイル番号 X。</param>
    /// <param name="y">タイル番号 Y。</param>
    /// <param name="cancellationToken">キャンセルトークン（NFR-UI-03）。</param>
    /// <returns>タイル画像のバイト列。</returns>
    /// <exception cref="TileFetchException">取得に失敗した場合。</exception>
    Task<byte[]> GetTileAsync(TileSource source, int zoom, int x, int y, CancellationToken cancellationToken);
}

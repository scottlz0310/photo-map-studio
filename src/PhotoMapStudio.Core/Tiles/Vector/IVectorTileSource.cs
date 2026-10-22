namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// ベクタータイル（MVT / PBF）を取得する抽象データソース。
/// </summary>
public interface IVectorTileSource
{
    /// <summary>
    /// タイルソースの一意の識別子（キャッシュ分離用）。
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// このソースが提供する最大ズームレベル（これ以上のズームは過拡大で描画する）。
    /// </summary>
    int MaxZoom { get; }

    /// <summary>
    /// 指定されたズーム・タイル座標のベクタータイルデータを取得する。
    /// </summary>
    /// <param name="z">ズームレベル。</param>
    /// <param name="x">X タイル座標。</param>
    /// <param name="y">Y タイル座標。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>ベクタータイルのバイト列。存在しない場合や取得失敗時は <see langword="null"/>。</returns>
    Task<byte[]?> GetTileAsync(int z, int x, int y, CancellationToken cancellationToken = default);
}

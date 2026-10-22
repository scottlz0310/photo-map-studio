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
    /// 実効的な識別子を非同期に解決・確定して取得する。
    /// 動的メタデータ（TileJSON等）を解決するソースではメタデータ取得後に確定した識別子を返す。
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>確定した識別子。</returns>
    Task<string> GetSourceIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(this.SourceId);

    /// <summary>
    /// 指定されたズーム・タイル座標のベクタータイルデータを取得する。
    /// </summary>
    /// <param name="z">ズームレベル。</param>
    /// <param name="x">X タイル座標。</param>
    /// <param name="y">Y タイル座標。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>ベクタータイルのバイト列。存在しない場合や取得失敗時は <see langword="null"/>。</returns>
    Task<byte[]?> GetTileAsync(
        int z,
        int x,
        int y,
        CancellationToken cancellationToken = default)
        => this.GetTileAsync(z, x, y, session: null, cancellationToken);

    /// <summary>
    /// 指定されたズーム・タイル座標のベクタータイルデータをセッション統計・レート管理付きで取得する。
    /// </summary>
    /// <param name="z">ズームレベル。</param>
    /// <param name="x">X タイル座標。</param>
    /// <param name="y">Y タイル座標。</param>
    /// <param name="session">一括生成単位の取得統計・レート管理セッション。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>ベクタータイルのバイト列。存在しない場合や取得失敗時は <see langword="null"/>。</returns>
    Task<byte[]?> GetTileAsync(
        int z,
        int x,
        int y,
        TileFetchSession? session,
        CancellationToken cancellationToken = default);
}

namespace PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

/// <summary>
/// PMTiles アーカイブからのバイト範囲読み取り（Range Read）抽象を提供します。
/// </summary>
public interface IPmtilesByteSource : IDisposable
{
    /// <summary>
    /// 指定されたオフセットと長さのバイト範囲を読み取ります。
    /// </summary>
    /// <param name="offset">開始バイトオフセット。</param>
    /// <param name="length">読み取りバイト長。</param>
    /// <param name="session">セッション統計更新用の TileFetchSession（任意）。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>読み取られたバイト配列。</returns>
    Task<byte[]> ReadRangeAsync(long offset, int length, TileFetchSession? session, CancellationToken cancellationToken);
}

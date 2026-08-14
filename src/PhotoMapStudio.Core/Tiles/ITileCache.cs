namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// 取得済みタイルのローカルキャッシュ（FR-07）。
/// </summary>
public interface ITileCache
{
    /// <summary>
    /// キャッシュを読み出す。
    /// </summary>
    /// <param name="key"><see cref="TileCacheKey"/> が算出したキー。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>キャッシュの内容。未保存・期限切れ・読み取り失敗の場合は <see langword="null"/>。</returns>
    Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// キャッシュへ書き出す。
    /// </summary>
    /// <param name="key"><see cref="TileCacheKey"/> が算出したキー。</param>
    /// <param name="content">レスポンスのバイト列（再エンコードしない）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>書き出しの完了を表すタスク。</returns>
    Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken);
}

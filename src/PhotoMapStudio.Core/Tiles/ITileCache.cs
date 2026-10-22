namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// 取得済みタイルのローカルキャッシュ（FR-07）。
/// </summary>
public interface ITileCache
{
    /// <summary>
    /// キャッシュ容量上限（バイト数）。0 以下の場合は無制限。
    /// </summary>
    long MaxSizeBytes { get => 0; set { } }

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

    /// <summary>
    /// 現在のキャッシュ総容量（バイト数）を取得する。
    /// </summary>
    /// <returns>キャッシュ合計バイト数。</returns>
    long GetCurrentSizeBytes() => 0;

    /// <summary>
    /// キャッシュ内のすべてのタイルを削除する。
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>すべてのファイルが正常に削除された場合は <see langword="true"/>、一部または全部の削除に失敗した場合は <see langword="false"/>。</returns>
    Task<bool> ClearAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <summary>
    /// 指定された容量（バイト数）以下になるよう古いタイルを削除する。
    /// </summary>
    /// <param name="targetSizeBytes">目標の最大容量（バイト数）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>整理完了を表すタスク。</returns>
    Task PruneAsync(long targetSizeBytes, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

namespace PhotoMapStudio.Rendering;

/// <summary>
/// 描画せずに、タイルの取得とデコードだけを先に行い、デコード済みキャッシュへ入れる契約。
/// </summary>
/// <remarks>
/// <see cref="IVectorMapRenderer"/> とは別の契約で、描画の実装を持つ側（通信やキャッシュを所有するラッパーなど）が、
/// 事前デコードを提供するかどうかを選べる。
/// </remarks>
public interface IVectorMapPrefetcher
{
    /// <summary>
    /// 指定した中心を囲む 2×2 枚のタイルを、取得ズーム（連続ズームの切り捨て。取得元の最大ズームで頭打ち）で取得・デコードし、
    /// デコード済みキャッシュへ入れる。描画はしない。
    /// 取得ズームで範囲外のタイル（南北の外）は飛ばし、経度方向は世界一周で折り返す。配信範囲外のタイルは入れるものがないので飛ばす。
    /// </summary>
    /// <param name="request">事前デコードの要求（中心・連続ズームなど）。</param>
    /// <param name="cancellationToken">
    /// キャンセルトークン。タイルの境界と取得・デコードの途中で確認し、キャンセルされたときは
    /// <see cref="OperationCanceledException"/> で終わる。キャッシュへは、デコードが済んだタイルだけを 1 枚ずつ入れる（途中のタイルは入れない）。
    /// </param>
    /// <returns>全タイルの処理が済んだとき完了する。</returns>
    Task PrefetchAsync(VectorPrefetchRequest request, CancellationToken cancellationToken = default);
}

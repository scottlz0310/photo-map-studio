namespace PhotoMapStudio.Rendering;

/// <summary>
/// ベクタータイルから、表示範囲全体を 1 枚の画像として描くレンダラーの契約。
/// </summary>
public interface IVectorMapRenderer
{
    /// <summary>
    /// 表示範囲全体を 1 枚の画像として描く。ズームは連続値で、位置・拡大率は連続値のまま描き、取得するタイルのズームだけを整数にする。
    /// 画像は呼び出し側へ所有権を渡す（共有・再利用しない）。
    /// </summary>
    /// <param name="request">描画要求（中心・連続ズーム・物理ピクセル寸法など）。</param>
    /// <param name="cancellationToken">
    /// キャンセルトークン。取得・描画の途中で確認し、キャンセルされたときは画像を返さず
    /// <see cref="OperationCanceledException"/> で終わる。
    /// </param>
    /// <returns>画像と欠落タイルの集計。使い終わったら <see cref="VectorViewResult.Dispose"/> する（画像を取り出したときは不要）。</returns>
    Task<VectorViewResult> RenderViewAsync(VectorViewRequest request, CancellationToken cancellationToken = default);
}

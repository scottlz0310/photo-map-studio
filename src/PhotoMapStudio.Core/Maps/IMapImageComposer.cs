namespace PhotoMapStudio.Core.Maps;

/// <summary>
/// 撮影地点を中心とした地図画像を合成する（FR-02）。
/// </summary>
public interface IMapImageComposer
{
    /// <summary>
    /// 地図画像を合成する。
    /// </summary>
    /// <param name="request">合成条件。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>合成結果。</returns>
    /// <exception cref="MapCompositionException">合成に失敗した場合。</exception>
    Task<MapCompositionResult> ComposeAsync(MapCompositionRequest request, CancellationToken cancellationToken);
}

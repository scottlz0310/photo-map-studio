using System.Net;

using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

namespace PhotoMapStudio.Core.Maps;

/// <summary>
/// タイルソースの配信範囲外だった場合に、全世界を覆う代替ソースで合成し直すデコレーター。
/// </summary>
/// <remarks>
/// 地理院タイルは日本国外で HTTP 404 を返すため、既定のままでは国外の写真を生成できない。
/// 写真 1 枚単位で切り替えるので、国内の写真は既定ソースの速度のまま処理される。
/// 切り替え先が OpenStreetMap の場合、一括生成で使うと Tile Usage Policy の bulk downloading に
/// 該当するため、一括生成では <see cref="MapCompositionRequest.AllowWorldwideFallback"/> を
/// <see langword="false"/> にして切り替えを止める。
/// この PC 上の PMTiles ファイルは「通信しない」取得元として使われるため、収録範囲外でも
/// 撮影地点のタイル座標を外部へ送らないよう、一括生成かどうかによらず切り替えない。
/// </remarks>
public sealed class FallbackMapImageComposer : IMapImageComposer
{
    private readonly IMapImageComposer inner;
    private readonly TileSource fallbackSource;

    /// <summary>
    /// デコレーターを初期化する。
    /// </summary>
    /// <param name="inner">実際に合成するコンポーザー。</param>
    /// <param name="fallbackSource">配信範囲外のときに使う代替タイルソース。</param>
    public FallbackMapImageComposer(IMapImageComposer inner, TileSource fallbackSource)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(fallbackSource);
        this.inner = inner;
        this.fallbackSource = fallbackSource;
    }

    /// <inheritdoc />
    public async Task<MapCompositionResult> ComposeAsync(
        MapCompositionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await this.inner.ComposeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TileFetchException ex) when (this.CanFallback(ex, request))
        {
            MapCompositionResult result = await this.inner
                .ComposeAsync(request with { TileSource = this.fallbackSource }, cancellationToken)
                .ConfigureAwait(false);

            return result with { UsedFallback = true };
        }
    }

    // 404 は「そのタイルが配信されていない」ことを表す。認証エラーや障害では切り替えない
    private bool CanFallback(TileFetchException exception, MapCompositionRequest request)
        => request.AllowWorldwideFallback
            && request.TileSource.VectorSource?.Kind != VectorSourceKind.PmtilesFile
            && exception.StatusCode == HttpStatusCode.NotFound
            && request.TileSource != this.fallbackSource
            && this.fallbackSource.SupportsZoom(request.Zoom);
}

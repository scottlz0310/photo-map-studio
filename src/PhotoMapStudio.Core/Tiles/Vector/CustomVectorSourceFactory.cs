using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// 利用者が指定したベクタータイルの取得元（<see cref="VectorSourceSpec"/>）から、取得元と描画スタイルの組を返す。
/// </summary>
/// <remarks>
/// 取得元の組み立て（キャッシュ・要求間隔・共有）は <see cref="VectorTileSourceFactory"/> が行い、ここでは取得元のスキーマに合う描画スタイルを選ぶ。
/// </remarks>
public sealed class CustomVectorSourceFactory : IDisposable
{
    private readonly VectorTileSourceFactory sourceFactory;
    private readonly IVectorMapStyle openMapTilesStyle;
    private readonly IVectorMapStyle gsiStyle;

    /// <summary>
    /// 取得元の生成器を初期化する。
    /// </summary>
    /// <param name="httpClient">HTTP クライアント。</param>
    /// <param name="cache">HTTP の取得元に使うタイルキャッシュ。</param>
    /// <param name="openMapTilesStyle">OpenMapTiles スキーマの描画スタイル。</param>
    /// <param name="gsiStyle">国土地理院 最適化ベクトルの描画スタイル。</param>
    /// <param name="timeProvider">要求間隔の判定に使う時刻源。</param>
    public CustomVectorSourceFactory(
        HttpClient httpClient,
        ITileCache cache,
        IVectorMapStyle openMapTilesStyle,
        IVectorMapStyle gsiStyle,
        TimeProvider? timeProvider = null)
    {
        this.sourceFactory = new VectorTileSourceFactory(httpClient, cache, timeProvider);
        this.openMapTilesStyle = openMapTilesStyle ?? throw new ArgumentNullException(nameof(openMapTilesStyle));
        this.gsiStyle = gsiStyle ?? throw new ArgumentNullException(nameof(gsiStyle));
    }

    /// <summary>
    /// 指定に対応する取得元と描画スタイルを返す。
    /// </summary>
    /// <param name="spec">利用者が指定した取得元。</param>
    /// <returns>取得元と描画スタイル。</returns>
    public (IVectorTileSource Source, IVectorMapStyle Style) Resolve(VectorSourceSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        // 破棄後の呼び出しは、取得元の生成器が ObjectDisposedException にする
        IVectorTileSource source = this.sourceFactory.Resolve(spec);
        IVectorMapStyle style = spec.Schema == VectorStyleSchema.GsiOptimalBvmap ? this.gsiStyle : this.openMapTilesStyle;
        return (source, style);
    }

    /// <inheritdoc />
    public void Dispose() => this.sourceFactory.Dispose();
}

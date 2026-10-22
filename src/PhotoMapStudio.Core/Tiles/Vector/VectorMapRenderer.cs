using PhotoMapStudio.Core.Geo;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// ベクタータイルから SkiaSharp キャンバスへ直接地図を描画するレンダラー。
/// </summary>
public sealed class VectorMapRenderer
{
    private readonly IVectorTileSource tileSource;
    private readonly IVectorMapStyle style;

    /// <summary>
    /// ベクター地図レンダラーを初期化する。
    /// </summary>
    /// <param name="tileSource">ベクタータイルデータソース。</param>
    /// <param name="style">地図描画スタイル。</param>
    public VectorMapRenderer(IVectorTileSource tileSource, IVectorMapStyle style)
    {
        this.tileSource = tileSource ?? throw new ArgumentNullException(nameof(tileSource));
        this.style = style ?? throw new ArgumentNullException(nameof(style));
    }

    /// <summary>
    /// 指定された地理座標、ズームレベル、出力サイズでベクター地図をラスタライズして <see cref="SKBitmap"/> を生成する。
    /// </summary>
    /// <param name="center">描画領域の中心地理座標。</param>
    /// <param name="zoom">表示ズームレベル。</param>
    /// <param name="width">出力画像の幅（ピクセル）。</param>
    /// <param name="height">出力画像の高さ（ピクセル）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>描画されたビットマップ画像。</returns>
    public async Task<SKBitmap> RenderMapAsync(
        GeoCoordinate center,
        int zoom,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(zoom);

        // 1. タイル取得用ズームとスケール比の決定（過拡大対応）
        int tileZoom = Math.Min(zoom, this.tileSource.MaxZoom);
        double scaleRatio = Math.Pow(2.0, zoom - tileZoom);

        // 2. 世界ピクセル座標系での表示範囲計算
        TilePoint centerTilePoint = WebMercator.ToTilePoint(center, zoom);
        double left = centerTilePoint.WorldPixelX - (width / 2.0);
        double top = centerTilePoint.WorldPixelY - (height / 2.0);
        double right = left + width;
        double bottom = top + height;

        // 3. 必要なタイルの座標範囲計算
        double tileLeft = left / scaleRatio;
        double tileTop = top / scaleRatio;
        double tileRight = right / scaleRatio;
        double tileBottom = bottom / scaleRatio;

        int minTx = (int)Math.Floor(tileLeft / WebMercator.TileSize);
        int minTy = (int)Math.Floor(tileTop / WebMercator.TileSize);
        int maxTx = (int)Math.Floor(tileRight / WebMercator.TileSize);
        int maxTy = (int)Math.Floor(tileBottom / WebMercator.TileSize);

        // 4. タイルの非同期取得とデコード
        var loadedTiles = new List<(int tx, int ty, VectorTile tile, float scale, float dx, float dy)>();

        for (int ty = minTy; ty <= maxTy; ty++)
        {
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                byte[]? tileData = await this.tileSource.GetTileAsync(tileZoom, tx, ty, cancellationToken).ConfigureAwait(false);
                if (tileData is null || tileData.Length == 0)
                {
                    continue;
                }

                VectorTile tile = VectorTileDecoder.Decode(tileData);
                uint extent = 4096;
                if (tile.Layers.Count > 0)
                {
                    extent = tile.Layers[0].Extent;
                }

                float scale = (float)((WebMercator.TileSize * scaleRatio) / extent);
                float dx = (float)((tx * WebMercator.TileSize * scaleRatio) - left);
                float dy = (float)((ty * WebMercator.TileSize * scaleRatio) - top);

                loadedTiles.Add((tx, ty, tile, scale, dx, dy));
            }
        }

        // 5. SkiaSharp によるキャンバス描画
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        // 背景クリア
        canvas.Clear(this.style.BackgroundColor);

        var viewport = new SKRect(0, 0, width, height);
        var collisionGrid = new LabelCollisionGrid();

        // パス 1: 全タイルの面（ポリゴン）
        for (int i = 0; i < loadedTiles.Count; i++)
        {
            var item = loadedTiles[i];
            canvas.Save();
            canvas.Translate(item.dx, item.dy);
            canvas.Scale(item.scale, item.scale);

            this.style.RenderPolygons(canvas, item.tile, item.scale, zoom);

            canvas.Restore();
        }

        // パス 2: 全タイルの線（道路・鉄道・ライン）
        for (int i = 0; i < loadedTiles.Count; i++)
        {
            var item = loadedTiles[i];
            canvas.Save();
            canvas.Translate(item.dx, item.dy);
            canvas.Scale(item.scale, item.scale);

            this.style.RenderLines(canvas, item.tile, item.scale, zoom);

            canvas.Restore();
        }

        // パス 3: 全タイルの注記・ラベル
        for (int i = 0; i < loadedTiles.Count; i++)
        {
            var item = loadedTiles[i];
            canvas.Save();
            canvas.Translate(item.dx, item.dy);
            canvas.Scale(item.scale, item.scale);

            this.style.RenderLabels(canvas, item.tile, item.scale, zoom, collisionGrid, viewport);

            canvas.Restore();
        }

        return bitmap;
    }
}

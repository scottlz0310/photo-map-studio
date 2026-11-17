namespace PhotoMapStudio.Rendering;

/// <summary>事前デコードの対象にするタイルを選ぶ。</summary>
internal static class PrefetchTileSelector
{
    /// <summary>
    /// 中心を囲む 2×2 枚のタイル座標を返す（中心が属するタイルと、中心から最も近い隣の 3 枚）。
    /// 南北の範囲外は除き、東西は世界一周で折り返す。折り返しで同じ座標になる場合（ズーム 0・1）は 1 枚にまとめる。
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> Around(double latitude, double longitude, int tileZoom)
    {
        int maxTiles = 1 << tileZoom;
        (double worldX, double worldY) = MercatorProjection.ToWorldPixel(latitude, longitude, tileZoom);
        double tileX = worldX / MercatorProjection.TileSize;
        double tileY = worldY / MercatorProjection.TileSize;

        var tiles = new List<(int X, int Y)>(4);
        foreach (double candidateY in new[] { Math.Floor(tileY - 0.5), Math.Floor(tileY + 0.5) })
        {
            // 緯度が範囲外・非数のときも、整数へ変換する前に範囲で除く
            if (!(candidateY >= 0 && candidateY < maxTiles))
            {
                continue;
            }

            foreach (double candidateX in new[] { Math.Floor(tileX - 0.5), Math.Floor(tileX + 0.5) })
            {
                if (!double.IsFinite(candidateX))
                {
                    continue;
                }

                long wrapped = (((long)candidateX % maxTiles) + maxTiles) % maxTiles;
                var tile = ((int)wrapped, (int)candidateY);
                if (!tiles.Contains(tile))
                {
                    tiles.Add(tile);
                }
            }
        }

        return tiles;
    }
}

namespace PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

/// <summary>
/// PMTiles v3 仕様に準拠した Hilbert 曲線タイル ID と (Z, X, Y) 座標の相互変換を提供します。
/// </summary>
public static class PmtilesHilbert
{
    /// <summary>
    /// 指定されたタイル座標 (Z, X, Y) を PMTiles v3 の Tile ID に変換します。
    /// </summary>
    /// <param name="z">ズームレベル (0〜26)。</param>
    /// <param name="x">X 座標。</param>
    /// <param name="y">Y 座標。</param>
    /// <returns>64-bit 符号なし整数による Hilbert Tile ID。</returns>
    public static ulong ZxyToTileId(int z, int x, int y)
    {
        if (z is < 0 or > 26)
        {
            throw new ArgumentOutOfRangeException(nameof(z), z, "ズームレベルは 0 から 26 の範囲である必要があります。");
        }

        long maxCoord = (1L << z) - 1;
        if (x < 0 || x > maxCoord)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, $"X 座標は 0 から {maxCoord} の範囲である必要があります。");
        }

        if (y < 0 || y > maxCoord)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, $"Y 座標は 0 から {maxCoord} の範囲である必要があります。");
        }

        ulong acc = ((1UL << (2 * z)) - 1UL) / 3UL;
        long n = 1L << z;
        long rx;
        long ry;
        ulong d = 0;
        long currX = x;
        long currY = y;
        long s = n / 2;

        while (s > 0)
        {
            rx = (currX & s) > 0 ? 1 : 0;
            ry = (currY & s) > 0 ? 1 : 0;
            d += (ulong)(s * s * ((3 * rx) ^ ry));
            Rotate(s, ref currX, ref currY, rx, ry);
            s /= 2;
        }

        return acc + d;
    }

    /// <summary>
    /// PMTiles v3 の Tile ID をタイル座標 (Z, X, Y) に逆変換します。
    /// </summary>
    /// <param name="tileId">Hilbert Tile ID。</param>
    /// <returns>ズームレベル、X 座標、Y 座標のタプル。</returns>
    public static (int Z, int X, int Y) TileIdToZxy(ulong tileId)
    {
        if (tileId == 0)
        {
            return (0, 0, 0);
        }

        int z = 0;
        while (z < 26)
        {
            ulong nextAcc = ((1UL << (2 * (z + 1))) - 1UL) / 3UL;
            if (tileId < nextAcc)
            {
                break;
            }

            z++;
        }

        ulong acc = ((1UL << (2 * z)) - 1UL) / 3UL;
        ulong d = tileId - acc;
        long n = 1L << z;
        long currX = 0;
        long currY = 0;
        long t = (long)d;

        for (long s = 1; s < n; s *= 2)
        {
            long rx = 1 & (t / 2);
            long ry = 1 & (t ^ rx);
            Rotate(s, ref currX, ref currY, rx, ry);
            currX += s * rx;
            currY += s * ry;
            t /= 4;
        }

        return (z, (int)currX, (int)currY);
    }

    private static void Rotate(long n, ref long x, ref long y, long rx, long ry)
    {
        if (ry == 0)
        {
            if (rx == 1)
            {
                x = n - 1 - x;
                y = n - 1 - y;
            }

            (x, y) = (y, x);
        }
    }
}

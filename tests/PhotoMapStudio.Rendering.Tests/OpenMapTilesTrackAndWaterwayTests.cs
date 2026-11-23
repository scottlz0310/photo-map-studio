using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests;

/// <summary>林道（<c>class=track</c>）の破線と、水路（<c>waterway</c>）の <c>class</c> ごとの太さ。</summary>
public sealed class OpenMapTilesTrackAndWaterwayTests
{
    private const int Size = 200;
    private const int LineY = 100;

    // 端の丸い線端の影響を避けて、線の途中だけを調べる
    private const int From = 20;
    private const int To = 180;

    [Theory]
    [InlineData("track", true)]
    [InlineData("path", true)]
    [InlineData("service", false)]
    [InlineData("minor", false)]
    [InlineData("residential", false)]
    [InlineData("tertiary", false)]
    public void 林道は破線で描き舗装路は従来の実線で描く(string roadClass, bool dashed)
    {
        using SKBitmap bitmap = RenderRoad(roadClass, zoom: 16, scale: 1f);

        int covered = Enumerable.Range(From, To - From).Count(x => ColumnCoverage(bitmap, x) > 0);

        if (dashed)
        {
            Assert.InRange(covered, 1, To - From - 20);
        }
        else
        {
            Assert.Equal(To - From, covered);
        }
    }

    [Fact]
    public void 林道は白い道ではなく茶色の線で描く()
    {
        using SKBitmap bitmap = RenderRoad("track", zoom: 16, scale: 1f);

        SKColor strongest = default;
        for (int x = From; x < To; x++)
        {
            for (int y = LineY - 4; y <= LineY + 4; y++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha > strongest.Alpha)
                {
                    strongest = pixel;
                }
            }
        }

        // 細い線の縁は半透明になるため、不透明度の閾値は緩くしている
        Assert.True(strongest.Alpha >= 150, "林道が描かれている必要があります。");
        Assert.True(strongest.Red > strongest.Blue + 40, $"茶色であるはず: {strongest}");
        Assert.True(strongest.Red < 200, $"白い道ではないはず: {strongest}");
    }

    [Fact]
    public void 林道は舗装路より細く道路網より目立たない()
    {
        using SKBitmap track = RenderRoad("track", zoom: 16, scale: 1f);
        using SKBitmap service = RenderRoad("service", zoom: 16, scale: 1f);

        Assert.True(Thickness(track) < Thickness(service) / 2, $"林道 {Thickness(track):F2} 舗装路 {Thickness(service):F2}");
    }

    [Theory]
    [InlineData("river", 4.0)]
    [InlineData("canal", 3.0)]
    [InlineData("stream", 1.8)]
    [InlineData("stream_intermittent", 1.8)]
    [InlineData("drain", 1.8)]
    [InlineData("ditch", 1.8)]
    [InlineData(null, 1.8)]
    public void 水路の太さはクラスごとに決まる(string? waterwayClass, double expectedWidth)
    {
        // ズーム 16 では ZoomScale が 1
        using SKBitmap bitmap = RenderWaterway(waterwayClass, zoom: 16, scale: 1f);

        Assert.Equal(expectedWidth, Thickness(bitmap), 0.2);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    public void 川は沢より太く太さの比はズームによらず一定になる(int zoom)
    {
        using SKBitmap river = RenderWaterway("river", zoom, scale: 1f);
        using SKBitmap stream = RenderWaterway("stream", zoom, scale: 1f);

        // どちらも ZoomScale と同じ伸び方なので、比は 4 / 1.8
        double scale = Math.Pow(2, (zoom - 16) * 0.75);
        Assert.Equal(4.0 * scale, Thickness(river), 0.25 * Math.Max(1, scale));
        Assert.Equal(1.8 * scale, Thickness(stream), 0.25 * Math.Max(1, scale));
        Assert.True(Thickness(river) > Thickness(stream));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void 水路と林道の太さは画素密度に追従する(double ratio)
    {
        // スタイルは論理ピクセルで描き、キャンバスが画素密度倍に拡大される（VectorMapRenderer と同じ）
        using SKBitmap river = RenderWaterway("river", zoom: 16, (float)ratio);
        using SKBitmap stream = RenderWaterway("stream", zoom: 16, (float)ratio);

        Assert.Equal(4.0 * ratio, Thickness(river), 0.3);
        Assert.Equal(1.8 * ratio, Thickness(stream), 0.3);
        Assert.True(Thickness(river) > Thickness(stream) * 1.5);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void 林道の破線の間隔は画素密度に追従する(double ratio)
    {
        using SKBitmap bitmap = RenderRoad("track", zoom: 16, (float)ratio);

        // 周期（線 6 + 間 3 の論理ピクセル）あたりの空きの割合は画素密度によらず同じ
        int from = (int)(From * ratio);
        int to = (int)(To * ratio);
        int gaps = Enumerable.Range(from, to - from).Count(x => ColumnCoverage(bitmap, x, ratio) == 0);
        double gapRatio = (double)gaps / (to - from);
        Assert.InRange(gapRatio, 0.25, 0.42);
    }

    private static SKBitmap RenderRoad(string roadClass, int zoom, float scale)
    {
        using var style = new OpenMapTilesStyle();
        SKBitmap bitmap = NewBitmap(scale);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);

        style.RenderLines(canvas, Tile("transportation", roadClass), 1f, zoom, CancellationToken.None);
        return bitmap;
    }

    private static SKBitmap RenderWaterway(string? waterwayClass, int zoom, float scale)
    {
        using var style = new OpenMapTilesStyle();
        SKBitmap bitmap = NewBitmap(scale);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);

        style.RenderLines(canvas, Tile("waterway", waterwayClass), 1f, zoom, CancellationToken.None);
        return bitmap;
    }

    private static SKBitmap NewBitmap(float scale) => new((int)(Size * scale), (int)(Size * scale));

    private static VectorTile Tile(string layer, string? featureClass)
    {
        var properties = new Dictionary<string, object>();
        if (featureClass is not null)
        {
            properties["class"] = featureClass;
        }

        var line = new VectorTileFeature
        {
            GeometryType = VectorGeometryType.LineString,
            Properties = properties,
            GeometryParts = [[new(0, LineY), new(Size, LineY)]],
        };
        return new VectorTile { Layers = [new VectorTileLayer { Name = layer, Extent = Size, Features = [line] }] };
    }

    /// <summary>1 列の不透明度の合計（画素数換算）。縁の半透明画素を含めた線の太さになる。</summary>
    private static double ColumnCoverage(SKBitmap bitmap, int x, double scale = 1.0)
    {
        double sum = 0;
        int centre = (int)(LineY * scale);
        for (int y = Math.Max(0, centre - 20); y <= Math.Min(bitmap.Height - 1, centre + 20); y++)
        {
            sum += bitmap.GetPixel(x, y).Alpha / 255.0;
        }

        return sum;
    }

    /// <summary>線の途中の列の、不透明度の合計の最大値（線が破線でも、線のある所の太さ）。</summary>
    private static double Thickness(SKBitmap bitmap)
    {
        double scale = bitmap.Width / (double)Size;
        int from = (int)(From * scale);
        int to = (int)(To * scale);
        return Enumerable.Range(from, to - from).Max(x => ColumnCoverage(bitmap, x, scale));
    }
}

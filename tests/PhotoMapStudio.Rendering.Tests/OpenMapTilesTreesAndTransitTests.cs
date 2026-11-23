using PhotoMapStudio.Rendering;
using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests;

/// <summary>森の樹木の模様・バス停と駅の記号・ロープウェイ・小道の色分け・番地。</summary>
public sealed class OpenMapTilesTreesAndTransitTests
{
    private const int Size = 200;
    private static readonly SKRect Viewport = new(0, 0, Size, Size);
    private static readonly SKColor WoodFill = SKColor.Parse("#b5d6a2");

    [Theory]
    [InlineData("wood", 14, true)]
    [InlineData("wood", 16, true)]
    [InlineData("wood", 13, false)]
    [InlineData("grass", 14, false)]
    [InlineData("farmland", 16, false)]
    public void 樹木の模様は森にだけ高いズームで重ねる(string kind, int zoom, bool drawn)
    {
        using var style = new OpenMapTilesStyle();
        SKColor fill = OpenMapTilesLandPalette.GetFill(landuse: false, kind)!.Value;
        using SKBitmap bitmap = RenderArea(style, "landcover", kind, zoom, Size, scale: 1f);

        Assert.Equal(drawn, CountDifferent(bitmap, fill, 0, 0, Size, Size) > 0);
    }

    [Fact]
    public void 樹木の模様は森の面の内側だけに描く()
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var leftHalf = new VectorTileFeature
        {
            GeometryType = VectorGeometryType.Polygon,
            Properties = new Dictionary<string, object> { ["class"] = "wood" },
            GeometryParts = [[new(0, 0), new(100, 0), new(100, Size), new(0, Size), new(0, 0)]],
        };

        style.RenderPolygons(canvas, Tile("landcover", leftHalf), 1f, 15, CancellationToken.None);

        Assert.True(CountDifferent(bitmap, WoodFill, 0, 0, 100, Size) > 0, "森の中に樹木が描かれている必要があります。");
        Assert.Equal(0, CountOpaque(bitmap, 104, 0, Size, Size));
    }

    [Fact]
    public void 樹木の模様は描画先の拡大に合わせて大きくなり本数は変わらない()
    {
        using var style = new OpenMapTilesStyle();
        using SKBitmap normal = RenderArea(style, "landcover", "wood", 15, Size, scale: 1f);
        using SKBitmap doubled = RenderArea(style, "landcover", "wood", 15, Size * 2, scale: 2f);

        // 同じ論理範囲に同じ本数の樹木があり、それぞれの面積が 4 倍になる（縁の半透明画素の数は 2 倍にしかならないため、色の差の総量で比べる）
        double ratio = (double)ColorDistance(doubled, WoodFill) / ColorDistance(normal, WoodFill);
        Assert.InRange(ratio, 3.5, 4.5);
    }

    [Fact]
    public void 樹木の模様は範囲が異常に広いときは描かない()
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var wood = new VectorTileFeature
        {
            GeometryType = VectorGeometryType.Polygon,
            Properties = new Dictionary<string, object> { ["class"] = "wood" },
            GeometryParts = [[new(0, 0), new(Size, 0), new(Size, Size), new(0, Size), new(0, 0)]],
        };

        // 模様の間隔が極端に小さくなる拡大率（タイル座標 1 単位 = 1/1000 ピクセルより細かい）では、本数の上限を超えて描かない
        style.RenderPolygons(canvas, Tile("landcover", wood), 0.0005f, 15, CancellationToken.None);

        Assert.Equal(0, CountDifferent(bitmap, WoodFill, 0, 0, Size, Size));
    }

    [Theory]
    [InlineData("bus", "BusStop")]
    [InlineData("railway", "Station")]
    [InlineData("parking", "Parking")]
    [InlineData("hospital", "Medical")]
    [InlineData("pharmacy", "Medical")]
    [InlineData("restaurant", "Dot")]
    [InlineData("shop", "Dot")]
    [InlineData(null, "Dot")]
    public void POIの種別から記号の形を決める(string? poiClass, string expected)
        => Assert.Equal(expected, OpenMapTilesPoiSymbols.GetMarker(poiClass).ToString());

    [Theory]
    [InlineData("BusStop", 16)]
    [InlineData("Station", 0)]
    [InlineData("Dot", 0)]
    [InlineData("Parking", 0)]
    public void バス停だけ高いズームで描く(string marker, int minZoom)
        => Assert.Equal(minZoom, OpenMapTilesPoiSymbols.GetMinZoom(Enum.Parse<PoiMarker>(marker)));

    [Theory]
    [InlineData("bus", 15, false)]
    [InlineData("bus", 16, true)]
    [InlineData("bus", 17, true)]
    [InlineData("railway", 15, true)]
    [InlineData("restaurant", 15, true)]
    public void バス停の記号はズーム16から描き駅は15から描く(string poiClass, int zoom, bool drawn)
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        style.RenderLabels(canvas, Tile("poi", Point(100, 100, new() { ["class"] = poiClass, ["name"] = "停留所", ["rank"] = 1L })), SKMatrix.Identity, zoom, new LabelCollisionGrid(), Viewport, CancellationToken.None);

        Assert.Equal(drawn, CountOpaque(bitmap, 90, 90, 110, 110) > 0);
    }

    [Theory]
    [InlineData("bus")]
    [InlineData("railway")]
    public void バス停と駅は円とは違う四角の記号で描く(string poiClass)
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        style.RenderLabels(canvas, Tile("poi", Point(100, 100, new() { ["class"] = poiClass, ["name"] = "停留所", ["rank"] = 1L })), SKMatrix.Identity, 17, new LabelCollisionGrid(), Viewport, CancellationToken.None);

        // 円（半径 3.5）の外側（中心から 3.7〜4.6 ピクセル）に、分類の色の画素がある＝円より外へ張り出す四角の隅
        SKColor transport = OpenMapTilesPoiSymbols.GetColor(PoiCategory.Transport);
        bool corner = false;
        for (int y = 94; y < 107; y++)
        {
            for (int x = 94; x < 107; x++)
            {
                double distance = Math.Sqrt(Math.Pow(x + 0.5 - 100, 2) + Math.Pow(y + 0.5 - 100, 2));
                corner |= distance is > 3.7 and < 4.6 && bitmap.GetPixel(x, y) == transport;
            }
        }

        Assert.True(corner);
    }

    [Theory]
    [InlineData("aerialway", 12, true)]
    [InlineData("aerialway", 14, true)]
    [InlineData("aerialway", 11, false)]
    [InlineData("ferry", 14, false)]
    public void ロープウェイは線と点で描く(string roadClass, int zoom, bool drawn)
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var line = new VectorTileFeature
        {
            GeometryType = VectorGeometryType.LineString,
            Properties = new Dictionary<string, object> { ["class"] = roadClass, ["subclass"] = "cable_car" },
            GeometryParts = [[new(0, 100), new(Size, 100)]],
        };

        style.RenderLines(canvas, Tile("transportation", line), 1f, zoom, CancellationToken.None);

        // 道路としては描かれない種類（class=aerialway）が、線として描かれる
        Assert.Equal(drawn, CountOpaque(bitmap, 20, 96, 180, 104) > 0);
    }

    [Theory]
    [InlineData("cycleway", "Cycle")]
    [InlineData("footway", "Foot")]
    [InlineData("pedestrian", "Foot")]
    [InlineData("steps", "Foot")]
    [InlineData("path", "Trail")]
    [InlineData("track", "Trail")]
    [InlineData(null, "Trail")]
    public void 小道の種類を細目から決める(string? subclass, string expected)
    {
        var properties = new Dictionary<string, object> { ["class"] = "path" };
        if (subclass is not null)
        {
            properties["subclass"] = subclass;
        }

        Assert.Equal(expected, OpenMapTilesStyle.GetPathKind(new VectorTileFeature { Properties = properties }).ToString());
    }

    [Theory]
    [InlineData("cycleway", true)]
    [InlineData("footway", false)]
    public void 自転車道は青く歩道は赤みのある色で描き分ける(string subclass, bool blue)
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var path = new VectorTileFeature
        {
            GeometryType = VectorGeometryType.LineString,
            Properties = new Dictionary<string, object> { ["class"] = "path", ["subclass"] = subclass },
            GeometryParts = [[new(0, 100), new(Size, 100)]],
        };

        style.RenderLines(canvas, Tile("transportation", path), 1f, 18, CancellationToken.None);

        SKColor strongest = default;
        for (int x = 0; x < Size; x++)
        {
            for (int y = 96; y < 104; y++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha > strongest.Alpha)
                {
                    strongest = pixel;
                }
            }
        }

        Assert.True(strongest.Alpha > 0, "小道が描かれている必要があります。");
        Assert.Equal(blue, strongest.Blue > strongest.Red);
    }

    [Theory]
    [InlineData("12", "12")]
    [InlineData("2-9-14", "2-9-14")]
    [InlineData(" 7 ", "7")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("123456789", null)]
    [InlineData("5 ○○タワー1F", null)]
    public void 番地は短く空白のない値だけを描く(string? raw, string? expected)
        => Assert.Equal(expected, OpenMapTilesHouseNumbers.GetText(raw));

    [Theory]
    [InlineData(17, true)]
    [InlineData(18, true)]
    [InlineData(16, false)]
    public void 番地は高いズームだけで描く(int zoom, bool drawn)
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        style.RenderLowPriorityLabels(canvas, Tile("housenumber", Point(100, 100, new() { ["housenumber"] = "12" })), SKMatrix.Identity, zoom, new LabelCollisionGrid(), Viewport, CancellationToken.None);

        Assert.Equal(drawn, CountOpaque(bitmap, 90, 88, 112, 104) > 0);
    }

    [Fact]
    public void 番地は先に置いた注記と重ならない所だけに描く()
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var grid = new LabelCollisionGrid();
        Assert.True(grid.TryPlace(new SKRect(80, 80, 120, 120), Viewport));

        style.RenderLowPriorityLabels(canvas, Tile("housenumber", Point(100, 100, new() { ["housenumber"] = "12" })), SKMatrix.Identity, 17, grid, Viewport, CancellationToken.None);

        Assert.Equal(0, CountOpaque(bitmap, 0, 0, Size, Size));
    }

    [Fact]
    public void 長い番地や点でない番地は描かない()
    {
        using var style = new OpenMapTilesStyle();
        using SKBitmap longText = new(Size, Size);
        using var canvas = new SKCanvas(longText);
        canvas.Clear(SKColors.Transparent);

        style.RenderLowPriorityLabels(canvas, Tile("housenumber", Point(100, 100, new() { ["housenumber"] = "5 ○○タワー1F" })), SKMatrix.Identity, 18, new LabelCollisionGrid(), Viewport, CancellationToken.None);

        Assert.Equal(0, CountOpaque(longText, 0, 0, Size, Size));
    }

    private static SKBitmap RenderArea(OpenMapTilesStyle style, string layer, string kind, int zoom, int pixels, float scale)
    {
        var bitmap = new SKBitmap(pixels, pixels);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        var area = new VectorTileFeature
        {
            GeometryType = VectorGeometryType.Polygon,
            Properties = new Dictionary<string, object> { ["class"] = kind },
            GeometryParts = [[new(0, 0), new(Size, 0), new(Size, Size), new(0, Size), new(0, 0)]],
        };
        style.RenderPolygons(canvas, Tile(layer, area), 1f, zoom, CancellationToken.None);
        return bitmap;
    }

    private static VectorTile Tile(string layer, VectorTileFeature feature)
        => new() { Layers = [new VectorTileLayer { Name = layer, Extent = Size, Features = [feature] }] };

    private static VectorTileFeature Point(int x, int y, Dictionary<string, object> properties) => new()
    {
        GeometryType = VectorGeometryType.Point,
        Properties = properties,
        GeometryParts = [[new VectorPoint(x, y)]],
    };

    private static int CountDifferent(SKBitmap bitmap, SKColor color, int left, int top, int right, int bottom)
    {
        int count = 0;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                if (bitmap.GetPixel(x, y) != color)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static long ColorDistance(SKBitmap bitmap, SKColor color)
    {
        long sum = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                sum += Math.Abs(pixel.Red - color.Red) + Math.Abs(pixel.Green - color.Green) + Math.Abs(pixel.Blue - color.Blue);
            }
        }

        return sum;
    }

    private static int CountOpaque(SKBitmap bitmap, int left, int top, int right, int bottom)
    {
        int count = 0;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    count++;
                }
            }
        }

        return count;
    }
}

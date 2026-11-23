using PhotoMapStudio.Rendering;
using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests;

/// <summary>山頂（<c>mountain_peak</c>）・POI の分類別の記号・保護区の扱い。</summary>
public sealed class OpenMapTilesSymbolsTests
{
    private const int Size = 200;
    private static readonly SKRect Viewport = new(0, 0, Size, Size);

    [Theory]
    [InlineData("bus", "Transport")]
    [InlineData("railway", "Transport")]
    [InlineData("parking", "Parking")]
    [InlineData("hospital", "Medical")]
    [InlineData("pharmacy", "Medical")]
    [InlineData("restaurant", "Food")]
    [InlineData("beer", "Food")]
    [InlineData("shop", "Shop")]
    [InlineData("grocery", "Shop")]
    [InlineData("lodging", "Lodging")]
    [InlineData("school", "Education")]
    [InlineData("library", "Education")]
    [InlineData("museum", "Culture")]
    [InlineData("place_of_worship", "Culture")]
    [InlineData("park", "Recreation")]
    [InlineData("pitch", "Recreation")]
    [InlineData("office", "Other")]
    [InlineData("post", "Other")]
    [InlineData("未知の種別", "Other")]
    [InlineData(null, "Other")]
    public void POIの種別を分類へ振り分ける(string? poiClass, string expected)
        => Assert.Equal(expected, OpenMapTilesPoiSymbols.GetCategory(poiClass).ToString());

    [Fact]
    public void 分類ごとの記号の色はすべて異なる()
    {
        var categories = Enum.GetValues<PoiCategory>();
        Assert.Equal(categories.Length, categories.Select(OpenMapTilesPoiSymbols.GetColor).Distinct().Count());
    }

    [Fact]
    public void 注記の文字色は記号の色より暗く白い縁取りの上で読める()
    {
        foreach (PoiCategory category in Enum.GetValues<PoiCategory>())
        {
            SKColor symbol = OpenMapTilesPoiSymbols.GetColor(category);
            SKColor text = OpenMapTilesPoiSymbols.GetTextColor(category);
            Assert.True(Luminance(text) < Luminance(symbol), category.ToString());
            Assert.True(Luminance(text) < 0.35, category.ToString());
        }
    }

    [Theory]
    [InlineData(10, 1L, false)]
    [InlineData(11, 2L, true)]
    [InlineData(11, 3L, false)]
    [InlineData(12, 3L, true)]
    [InlineData(12, 4L, false)]
    [InlineData(13, 6L, true)]
    [InlineData(13, 7L, false)]
    [InlineData(14, 99L, true)]
    [InlineData(16, 1L, true)]
    [InlineData(14, null, false)]
    public void 山頂はズームに応じて順位の上位だけを描く(int zoom, long? rank, bool expected)
        => Assert.Equal(expected, OpenMapTilesPeakSymbols.ShouldShow(zoom, rank));

    [Theory]
    [InlineData("2291", "2291 m")]
    [InlineData("0", "0 m")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("-5", null)]
    [InlineData("2291.5", null)]
    [InlineData("高い", null)]
    public void 標高を文字列へ整える(string? elevation, string? expected)
        => Assert.Equal(expected, OpenMapTilesPeakSymbols.FormatElevation(elevation));

    [Fact]
    public void 火山は赤く他の山頂は茶色で描き分ける()
    {
        Assert.NotEqual(OpenMapTilesPeakSymbols.GetColor("volcano"), OpenMapTilesPeakSymbols.GetColor("peak"));
        Assert.Equal(OpenMapTilesPeakSymbols.GetColor("peak"), OpenMapTilesPeakSymbols.GetColor(null));
    }

    [Theory]
    [InlineData("peak")]
    [InlineData("volcano")]
    public void 山頂は記号を山の位置に描く(string peakClass)
    {
        using var style = new OpenMapTilesStyle();
        using SKBitmap bitmap = Render(style, "mountain_peak", 14, Point(100, 100, new() { ["class"] = peakClass, ["name:ja"] = "旭岳", ["ele"] = "2291", ["rank"] = 1L }));

        // 三角の中心（頂点と底辺の間）が山頂の色。名前・標高の文字は三角の下に描く
        Assert.Equal(OpenMapTilesPeakSymbols.GetColor(peakClass), bitmap.GetPixel(100, 101));
        Assert.True(HasInk(bitmap, 90, 108, 112, 136), "三角の下に山名と標高が描かれている必要があります。");
    }

    [Fact]
    public void 名前のない山頂と上位でない山頂は描かない()
    {
        using var style = new OpenMapTilesStyle();
        using SKBitmap cliff = Render(style, "mountain_peak", 14, Point(100, 100, new() { ["class"] = "cliff", ["rank"] = 1L }));
        using SKBitmap lowRank = Render(style, "mountain_peak", 12, Point(100, 100, new() { ["class"] = "peak", ["name"] = "小山", ["rank"] = 9L }));

        Assert.False(HasInk(cliff, 0, 0, Size, Size));
        Assert.False(HasInk(lowRank, 0, 0, Size, Size));
    }

    [Theory]
    [InlineData("saddle")]
    [InlineData("cliff")]
    [InlineData("")]
    [InlineData(null)]
    public void 峠や崖など山頂と火山以外は名前があっても山頂の記号を描かない(string? peakClass)
    {
        using var style = new OpenMapTilesStyle();
        var properties = new Dictionary<string, object> { ["name:ja"] = "三国峠", ["ele"] = "1139", ["rank"] = 1L };
        if (peakClass is not null)
        {
            properties["class"] = peakClass;
        }

        using SKBitmap bitmap = Render(style, "mountain_peak", 14, Point(100, 100, properties));

        Assert.False(HasInk(bitmap, 0, 0, Size, Size));
    }

    [Theory]
    [InlineData("peak", true)]
    [InlineData("volcano", true)]
    [InlineData("saddle", false)]
    [InlineData("cliff", false)]
    [InlineData(null, false)]
    public void 山頂の記号を描く種類は山頂と火山だけ(string? peakClass, bool expected)
        => Assert.Equal(expected, OpenMapTilesPeakSymbols.IsPeak(peakClass));
    [Fact]
    public void 標高のない山頂は山名だけを描く()
    {
        using var style = new OpenMapTilesStyle();
        using SKBitmap withElevation = Render(style, "mountain_peak", 14, Point(100, 100, new() { ["class"] = "peak", ["name"] = "旭岳", ["ele"] = "2291", ["rank"] = 1L }));
        using SKBitmap withoutElevation = Render(style, "mountain_peak", 14, Point(100, 100, new() { ["class"] = "peak", ["name"] = "旭岳", ["rank"] = 1L }));

        Assert.True(CountInk(withElevation, 0, 124, Size, Size) > 0, "標高の行が描かれている必要があります。");
        Assert.Equal(0, CountInk(withoutElevation, 0, 124, Size, Size));
    }

    [Fact]
    public void 山頂の記号は先に置いた注記と重ならない()
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        var grid = new LabelCollisionGrid();
        Assert.True(grid.TryPlace(new SKRect(80, 80, 120, 140), Viewport));

        style.RenderLabels(canvas, Tile("mountain_peak", Point(100, 100, new() { ["class"] = "peak", ["name"] = "旭岳", ["ele"] = "2291", ["rank"] = 1L })), SKMatrix.Identity, 14, grid, Viewport, CancellationToken.None);

        Assert.False(HasInk(bitmap, 0, 0, Size, Size));
    }

    [Theory]
    [InlineData("hospital")]
    [InlineData("bus")]
    [InlineData("restaurant")]
    [InlineData("shop")]
    [InlineData("park")]
    [InlineData("office")]
    public void POIは分類の色の記号を描く(string poiClass)
    {
        using var style = new OpenMapTilesStyle();
        using SKBitmap bitmap = Render(style, "poi", 16, Point(100, 100, new() { ["class"] = poiClass, ["name"] = "施設", ["rank"] = 1L }));

        PoiCategory category = OpenMapTilesPoiSymbols.GetCategory(poiClass);

        // 医療の白い十字・駐車場の白い P が重なるため、記号の範囲内に分類の色の画素があることで確認する
        Assert.True(HasColor(bitmap, OpenMapTilesPoiSymbols.GetColor(category), 94, 94, 107, 107));
    }

    [Fact]
    public void POIの注記の文字は分類の色を暗くした色で描く()
    {
        using var style = new OpenMapTilesStyle();
        using SKBitmap bitmap = Render(style, "poi", 16, Point(100, 100, new() { ["class"] = "restaurant", ["name"] = "食堂", ["rank"] = 1L }));

        Assert.True(
            HasColor(bitmap, OpenMapTilesPoiSymbols.GetTextColor(PoiCategory.Food), 85, 104, 115, 125),
            "文字の芯の画素が、分類の暗い色で描かれている必要があります。");
    }

    [Theory]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(15, false)]
    public void 名前のない駐車場は高いズームで記号だけを描く(int zoom, bool drawn)
    {
        using var style = new OpenMapTilesStyle();
        using SKBitmap bitmap = Render(style, "poi", zoom, Point(100, 100, new() { ["class"] = "parking", ["rank"] = 1L }));

        Assert.Equal(drawn, HasInk(bitmap, 90, 90, 110, 110));
        if (drawn)
        {
            Assert.True(HasColor(bitmap, OpenMapTilesPoiSymbols.GetColor(PoiCategory.Parking), 94, 94, 107, 107));
        }
    }

    [Fact]
    public void 名前のない駐車場以外の施設は記号も描かない()
    {
        using var style = new OpenMapTilesStyle();
        using SKBitmap bitmap = Render(style, "poi", 17, Point(100, 100, new() { ["class"] = "restaurant", ["rank"] = 1L }));

        Assert.False(HasInk(bitmap, 0, 0, Size, Size));
    }

    [Fact]
    public void 名前のない駐車場の記号は先に置いた注記と重ならない()
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        var grid = new LabelCollisionGrid();
        Assert.True(grid.TryPlace(new SKRect(90, 90, 110, 110), Viewport));

        style.RenderLabels(canvas, Tile("poi", Point(100, 100, new() { ["class"] = "parking", ["rank"] = 1L })), SKMatrix.Identity, 17, grid, Viewport, CancellationToken.None);

        Assert.False(HasInk(bitmap, 0, 0, Size, Size));
    }

    [Theory]
    [InlineData("recreation_ground", true)]
    [InlineData("historic", true)]
    [InlineData("national_park", false)]
    [InlineData("nature_reserve", false)]
    [InlineData("protected_area", false)]
    [InlineData("conservation", false)]
    [InlineData("wilderness_preserve", false)]
    [InlineData("wildlife_refuge", false)]
    [InlineData("sustainable", false)]
    [InlineData("marine", false)]
    [InlineData("aboriginal_lands", false)]
    [InlineData("国定公園", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 指定区域は塗りつぶさず用途を表す公園だけを塗る(string? parkClass, bool filled)
        => Assert.Equal(filled, OpenMapTilesLandPalette.ShouldFillPark(parkClass));

    [Theory]
    [InlineData("national_park", "#b5d6a2")]
    [InlineData("nature_reserve", "#b5d6a2")]
    [InlineData("conservation", "#b5d6a2")]
    [InlineData("wilderness_preserve", "#b5d6a2")]
    [InlineData("wildlife_refuge", "#b5d6a2")]
    [InlineData("国定公園", "#b5d6a2")]
    [InlineData("recreation_ground", "#d8e8c8")]
    [InlineData("historic", "#d8e8c8")]
    public void 指定区域は下の植生を隠さず用途を表す公園は上に塗る(string parkClass, string expected)
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        static VectorTileFeature Square(string layerClass) => new()
        {
            GeometryType = VectorGeometryType.Polygon,
            Properties = new Dictionary<string, object> { ["class"] = layerClass },
            GeometryParts = [[new(0, 0), new(Size, 0), new(Size, Size), new(0, Size), new(0, 0)]],
        };
        var tile = new VectorTile
        {
            Layers =
            [
                new VectorTileLayer { Name = "landcover", Extent = Size, Features = [Square("wood")] },
                new VectorTileLayer { Name = "park", Extent = Size, Features = [Square(parkClass)] },
            ],
        };

        style.RenderPolygons(canvas, tile, 1f, 14, CancellationToken.None);

        Assert.Equal(SKColor.Parse(expected), bitmap.GetPixel(100, 100));
    }
    private static SKBitmap Render(OpenMapTilesStyle style, string layer, int zoom, VectorTileFeature feature)
    {
        var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        style.RenderLabels(canvas, Tile(layer, feature), SKMatrix.Identity, zoom, new LabelCollisionGrid(), Viewport, CancellationToken.None);
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

    private static bool HasColor(SKBitmap bitmap, SKColor color, int left, int top, int right, int bottom)
    {
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                if (bitmap.GetPixel(x, y) == color)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasInk(SKBitmap bitmap, int left, int top, int right, int bottom) => CountInk(bitmap, left, top, right, bottom) > 0;

    private static int CountInk(SKBitmap bitmap, int left, int top, int right, int bottom)
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

    private static double Luminance(SKColor color) => ((0.2126 * color.Red) + (0.7152 * color.Green) + (0.0722 * color.Blue)) / 255.0;
}

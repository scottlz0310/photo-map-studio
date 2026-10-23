using PhotoMapStudio.Core.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public sealed class OpenMapTilesRoadSymbolsTests
{
    private static readonly SKColor ExpresswayGreen = new(0x0A, 0x8A, 0x3C);
    private static readonly SKColor NationalBlue = new(0x00, 0x62, 0xB8);
    private static readonly SKColor PrefecturalOutline = new(0x00, 0x62, 0xB8);

    [Theory]
    [InlineData("motorway", null, "Expressway")]
    [InlineData("trunk", "road", "National")]
    [InlineData("primary", "road", "National")]
    [InlineData("secondary", "road", "Prefectural")]
    [InlineData("tertiary", null, "Prefectural")]
    [InlineData("minor", "road", "None")]
    [InlineData("service", null, "None")]
    [InlineData(null, null, "None")]
    // 経路網が jp-* のときは道路種別より優先する
    [InlineData("tertiary", "jp-national", "National")]
    [InlineData("primary", "JP-Prefectural", "Prefectural")]
    [InlineData("trunk", "jp-expressway", "Expressway")]
    public void 道路種別と経路網から盾の種別を決める(string? roadClass, string? network, string expected)
        => Assert.Equal(expected, OpenMapTilesRoadSymbols.GetShieldKind(roadClass, network).ToString());

    [Theory]
    [InlineData("1", "1")]
    [InlineData("E1", "E1")]
    [InlineData("1;8", "1")]
    [InlineData(" 299 ", "299")]
    [InlineData("123456", null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void 経路番号は先頭の番号だけを盾に表示する(string? reference, string? expected)
        => Assert.Equal(expected, OpenMapTilesRoadSymbols.GetShieldText(reference));

    [Theory]
    [InlineData("motorway", "E1")]
    [InlineData("trunk", "1")]
    public void 高速と国道の盾は固定の色で描画される(string roadClass, string reference)
    {
        SKColor expected = roadClass == "motorway" ? ExpresswayGreen : NationalBlue;

        using SKBitmap bitmap = RenderSymbols(zoom: 14, Line(roadClass, reference, name: null, y: 2048));

        Assert.True(CountNear(bitmap, expected) >= 20, "盾の塗りが描画されていない");
    }

    [Fact]
    public void 都道府県道の盾は白地に青い縁で描画される()
    {
        using SKBitmap bitmap = RenderSymbols(zoom: 14, Line("secondary", "299", name: null, y: 2048));

        Assert.True(CountNear(bitmap, PrefecturalOutline) >= 10, "盾の縁が描画されていない");
    }

    [Fact]
    public void 経路番号を持たない道路には盾を描かない()
    {
        using SKBitmap bitmap = RenderSymbols(zoom: 14, Line("trunk", reference: null, name: null, y: 2048));

        Assert.Equal(0, CountNear(bitmap, NationalBlue));
    }

    [Fact]
    public void 盾はズーム12未満では描かない()
    {
        using SKBitmap bitmap = RenderSymbols(zoom: 11, Line("trunk", "1", name: null, y: 2048));

        Assert.Equal(0, CountNear(bitmap, NationalBlue));
    }

    [Fact]
    public void 同じ位置で重なる盾は高速を優先して1つだけ描く()
    {
        using SKBitmap bitmap = RenderSymbols(
            zoom: 14,
            Line("trunk", "1", name: null, y: 2048),
            Line("motorway", "E1", name: null, y: 2048));

        Assert.True(CountNear(bitmap, ExpresswayGreen) >= 20);
        Assert.Equal(0, CountNear(bitmap, NationalBlue));
    }

    [Fact]
    public void 同じ路線の盾は1つのタイルで最も長い区間にだけ描く()
    {
        // 上段は長い区間、下段は短い区間（どちらも同じ路線）。短い区間の位置には描かれない
        VectorTileLayer layer = Layer(
            Line("trunk", "1", name: null, y: 1000, x0: 0, x1: 4096),
            Line("trunk", "1", name: null, y: 3000, x0: 1500, x1: 2600));

        using SKBitmap bitmap = RenderSymbols(zoom: 14, layer);

        int upper = CountNear(bitmap, NationalBlue, rowFrom: 0, rowTo: 127);
        int lower = CountNear(bitmap, NationalBlue, rowFrom: 128, rowTo: 255);
        Assert.True(upper >= 20);
        Assert.Equal(0, lower);
    }

    [Fact]
    public void 道路名は直線道路に沿って描画される()
    {
        using SKBitmap bitmap = RenderLabels(zoom: 14, Line("tertiary", reference: null, name: "桜田通り", y: 2048));

        Assert.True(CountDark(bitmap) >= 8, "道路名が描画されていない");
    }

    [Fact]
    public void 名前より短い道路には道路名を描かない()
    {
        // 画面上で 16 ピクセルほどの短い区間に、長い名前は置かない
        using SKBitmap bitmap = RenderLabels(
            zoom: 14,
            Line("tertiary", reference: null, name: "とても長い名前の通り", y: 2048, x0: 2000, x1: 2256));

        Assert.Equal(0, CountDark(bitmap));
    }

    [Theory]
    [InlineData("tertiary", 13)]
    [InlineData("minor", 14)]
    [InlineData("path", 16)]
    public void 道路名はズームと道路種別に応じて表示を絞る(string roadClass, int zoom)
    {
        using SKBitmap bitmap = RenderLabels(zoom, Line(roadClass, reference: null, name: "桜田通り", y: 2048));

        Assert.Equal(0, CountDark(bitmap));
    }

    [Theory]
    [InlineData("minor", 15)]
    [InlineData("residential", 15)]
    [InlineData("unclassified", 15)]
    [InlineData("service", 15)]
    [InlineData("path", 17)]
    [InlineData("track", 17)]
    [InlineData("pedestrian", 17)]
    [InlineData("living_street", 17)]
    public void 拡大時はデータのある細街路や小道の名前を表示する(string roadClass, int zoom)
    {
        using SKBitmap bitmap = RenderLabels(zoom, Line(roadClass, reference: null, name: "北２丁目通り", y: 2048));
        Assert.True(CountDark(bitmap) >= 8);
    }

    // 過拡大（OpenFreeMap は最大ズーム 14 のタイルを拡大する）では、1 つのタイルが画面より大きくなる。
    // 画面幅 512、倍率 2、平行移動 (-1024, -3970): 道路 (0,2048)→(4096,2048) は画面を横切るが、区間全体の中点は画面外。
    private static readonly SKMatrix OverzoomMatrix = SKMatrix.CreateScaleTranslation(2f, 2f, -1024f, -3970f);

    [Fact]
    public void 過拡大で区間全体の中点が画面外でも画面内に見える道路に盾を描く()
    {
        using SKBitmap bitmap = Render(
            zoom: 19, Layer(Line("trunk", "1", name: null, y: 2048)), symbols: true, OverzoomMatrix, width: 512, height: 256);

        Assert.True(CountNear(bitmap, NationalBlue) >= 20, "画面を横切る道路の盾が描画されていない");
    }

    [Fact]
    public void 過拡大で区間全体の中点が画面外でも画面内に見える道路に道路名を描く()
    {
        using SKBitmap bitmap = Render(
            zoom: 19, Layer(Line("tertiary", reference: null, name: "桜田通り", y: 2048)), symbols: false, OverzoomMatrix, width: 512, height: 256);

        Assert.True(CountDark(bitmap) >= 8, "画面を横切る道路の道路名が描画されていない");
    }

    [Fact]
    public void 同じ路線の長い区間の中点が画面外でも可視の区間がある場合は描く()
    {
        // 長い区間（中点が画面外）と、同じ ref・name の可視の短い区間。長い区間の見えている部分でも、短い区間でも描かれる
        VectorTileLayer layer = Layer(
            Line("tertiary", "299", name: "桜田通り", y: 2048, x0: 0, x1: 4096),
            Line("tertiary", "299", name: "桜田通り", y: 2048, x0: 600, x1: 700));

        using SKBitmap symbols = Render(zoom: 19, layer, symbols: true, OverzoomMatrix, width: 512, height: 256);
        using SKBitmap labels = Render(zoom: 19, layer, symbols: false, OverzoomMatrix, width: 512, height: 256);

        Assert.True(CountNear(symbols, PrefecturalOutline) >= 10, "盾が描画されていない");
        Assert.True(CountDark(labels) >= 8, "道路名が描画されていない");
    }

    [Fact]
    public void 画面内に見えない道路には盾も道路名も描かない()
    {
        // タイルの上端の道路は、同じ変換で画面の上に外れる
        VectorTileLayer layer = Layer(Line("trunk", "1", name: "桜田通り", y: 0));

        using SKBitmap symbols = Render(zoom: 19, layer, symbols: true, OverzoomMatrix, width: 512, height: 256);
        using SKBitmap labels = Render(zoom: 19, layer, symbols: false, OverzoomMatrix, width: 512, height: 256);

        Assert.Equal(0, CountNear(symbols, NationalBlue));
        Assert.Equal(0, CountDark(labels));
    }

    [Fact]
    public void 折れ線が画面の端をまたぐ場合は見えている区間の中央に盾を描く()
    {
        // 画面内 → 画面外 → 画面内と出入りする折れ線。見えている区間のうち長い方の中央に描く
        var feature = new VectorTileFeature
        {
            GeometryType = VectorGeometryType.LineString,
            Properties = new Dictionary<string, object> { ["class"] = "trunk", ["ref"] = "1" },
            GeometryParts = [[new VectorPoint(0, 2000), new VectorPoint(4096, 2000), new VectorPoint(4096, -8000), new VectorPoint(0, -8000)]],
        };

        using SKBitmap bitmap = Render(zoom: 14, Layer(feature), symbols: true);

        Assert.True(CountNear(bitmap, NationalBlue) >= 20);
    }

    private static SKBitmap RenderSymbols(int zoom, params VectorTileFeature[] features)
        => RenderSymbols(zoom, Layer(features));

    private static SKBitmap RenderSymbols(int zoom, VectorTileLayer layer)
        => Render(zoom, layer, symbols: true);

    private static SKBitmap RenderLabels(int zoom, params VectorTileFeature[] features)
        => Render(zoom, Layer(features), symbols: false);

    private static SKBitmap Render(int zoom, VectorTileLayer layer, bool symbols, SKMatrix? tileToCanvas = null, int width = 256, int height = 256)
    {
        using var style = new OpenMapTilesStyle();
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        var tile = new VectorTile { Layers = [layer] };
        SKMatrix matrix = tileToCanvas ?? SKMatrix.CreateScale(256f / 4096f, 256f / 4096f);
        var grid = new LabelCollisionGrid();
        var viewport = new SKRect(0, 0, width, height);

        if (symbols)
        {
            style.RenderSymbols(canvas, tile, matrix, zoom, grid, viewport);
        }
        else
        {
            style.RenderLineLabels(canvas, tile, matrix, zoom, grid, viewport);
        }

        return bitmap;
    }

    private static VectorTileLayer Layer(params VectorTileFeature[] features)
        => new() { Name = "transportation_name", Extent = 4096, Features = features };

    private static VectorTileFeature Line(string roadClass, string? reference, string? name, int y, int x0 = 0, int x1 = 4096)
    {
        var properties = new Dictionary<string, object> { ["class"] = roadClass };
        if (reference is not null)
        {
            properties["ref"] = reference;
        }

        if (name is not null)
        {
            properties["name"] = name;
        }

        return new VectorTileFeature
        {
            GeometryType = VectorGeometryType.LineString,
            Properties = properties,
            GeometryParts = [[new VectorPoint(x0, y), new VectorPoint(x1, y)]],
        };
    }

    /// <summary>白い背景に対して、文字の本体にあたる濃いピクセル（ハロー・アンチエイリアスの淡い部分を除く）を数える。</summary>
    private static int CountDark(SKBitmap bitmap)
    {
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor c = bitmap.GetPixel(x, y);
                if (c.Red < 160 && c.Green < 160 && c.Blue < 160)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountNear(SKBitmap bitmap, SKColor target, int tolerance = 12, int rowFrom = 0, int rowTo = 255)
    {
        int count = 0;
        for (int y = rowFrom; y <= rowTo && y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor c = bitmap.GetPixel(x, y);
                if (Math.Abs(c.Red - target.Red) <= tolerance
                    && Math.Abs(c.Green - target.Green) <= tolerance
                    && Math.Abs(c.Blue - target.Blue) <= tolerance)
                {
                    count++;
                }
            }
        }

        return count;
    }
}

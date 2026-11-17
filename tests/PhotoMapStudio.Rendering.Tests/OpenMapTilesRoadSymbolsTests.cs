using PhotoMapStudio.Rendering;
using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests;

public sealed class OpenMapTilesRoadSymbolsTests
{
    private const uint ExpresswayColor = 0xFF0A8A3C;
    private const uint NationalColor = 0xFF0062B8;
    private const uint PrefecturalColor = 0xFF0062B8;

    private static readonly SKColor ExpresswayGreen = new(ExpresswayColor);
    private static readonly SKColor NationalBlue = new(NationalColor);
    private static readonly SKColor PrefecturalOutline = new(PrefecturalColor);

    [Theory]
    // OSM の路線関係の network（OpenFreeMap の route_N_network）
    [InlineData("JP:national", "National")]
    [InlineData("JP:prefectural", "Prefectural")]
    [InlineData("JP:prefectural:tottori", "Prefectural")]
    [InlineData("JP:E", "Expressway")]
    [InlineData("JP:expressway:urban:Hanshin Expressway", "Expressway")]
    // Protomaps 系のタイルが network に持つ表記
    [InlineData("jp-national", "National")]
    [InlineData("JP-Prefectural", "Prefectural")]
    [InlineData("jp-expressway", "Expressway")]
    // 盾の対象にしない経路網（市道は route_N_ref が内部番号、旧街道は国道・県道ではない）
    [InlineData("JP:municipal:osaka-shi:regional_arterial", "None")]
    [InlineData("JP:city:kyoto", "None")]
    [InlineData("JP:Gokaido", "None")]
    [InlineData("JP:Express", "None")]
    [InlineData("JP:E:other", "None")]
    [InlineData("JP:nationalx", "None")]
    [InlineData("road", "None")]
    [InlineData("", "None")]
    [InlineData(null, "None")]
    public void 経路網の名前から盾の種別を決める(string? network, string expected)
        => Assert.Equal(expected, OpenMapTilesRoadSymbols.GetShieldKind(network).ToString());

    [Theory]
    // OpenFreeMap: network は road、国道・県道は route_N_* に入る
    [InlineData("National", "53", "trunk", "road", "53;373", "JP:national", "53", "JP:national", "373", null, null)]
    [InlineData("Prefectural", "184", "secondary", "road", "184", "JP:prefectural:tottori", "184", null, null, null, null)]
    // 先の路線が市道で番号を持たなくても、後の路線で決まる
    [InlineData("National", "423", "trunk", "road", "423", "JP:city:Osaka", null, "JP:national", "423", null, null)]
    // 複数の種別があれば高速、国道、都道府県道の順に優先する
    [InlineData("Expressway", "E1", "motorway", null, null, "JP:national", "1", "JP:E", "E1", null, null)]
    [InlineData("National", "9", "primary", null, null, "JP:prefectural", "186", "JP:national", "9", null, null)]
    // 3 番目・4 番目の路線も見る
    [InlineData("Prefectural", "32", "primary", null, null, "JP:city:kyoto", "0060043", "JP:municipal:kyoto", "0412", "JP:prefectural", "32")]
    // 道路種別（class）だけで路線関係を持たない道路には、盾を描かない
    [InlineData("None", null, "trunk", "road", "1", null, null, null, null, null, null)]
    [InlineData("None", null, "primary", "road", "186", null, null, null, null, null, null)]
    [InlineData("None", null, "motorway", null, "E1", null, null, null, null, null, null)]
    // 路線が市道だけ、または番号を表示できない場合
    [InlineData("None", null, "tertiary", "road", null, "JP:municipal:kyoto", "0412120", null, null, null, null)]
    [InlineData("None", null, "trunk", "road", null, "JP:national", null, null, null, null, null)]
    [InlineData("None", null, "trunk", "road", null, "JP:national", "0060043", null, null, null, null)]
    // Protomaps 系: network と ref の組
    [InlineData("National", "1", "trunk", "jp-national", "1", null, null, null, null, null, null)]
    public void 道路が属する路線から盾の種別と番号を決める(
        string expectedKind, string? expectedText, string roadClass, string? network, string? reference,
        string? network1, string? ref1, string? network2, string? ref2, string? network3, string? ref3)
    {
        var properties = new Dictionary<string, object> { ["class"] = roadClass };
        AddIfNotNull(properties, "network", network);
        AddIfNotNull(properties, "ref", reference);
        AddIfNotNull(properties, "route_1_network", network1);
        AddIfNotNull(properties, "route_1_ref", ref1);
        AddIfNotNull(properties, "route_2_network", network2);
        AddIfNotNull(properties, "route_2_ref", ref2);
        AddIfNotNull(properties, "route_3_network", network3);
        AddIfNotNull(properties, "route_3_ref", ref3);
        var feature = new VectorTileFeature { GeometryType = VectorGeometryType.LineString, Properties = properties };

        (RouteShieldKind kind, string? text) = OpenMapTilesRoadSymbols.GetRouteShield(feature);

        Assert.Equal(expectedKind, kind.ToString());
        Assert.Equal(expectedText, text);
    }

    private static void AddIfNotNull(Dictionary<string, object> properties, string key, string? value)
    {
        if (value is not null)
        {
            properties[key] = value;
        }
    }

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
    [InlineData("motorway", "JP:E", "E1")]
    [InlineData("trunk", "JP:national", "1")]
    public void 高速と国道の盾は固定の色で描画される(string roadClass, string network, string routeRef)
    {
        SKColor expected = network == "JP:E" ? ExpresswayGreen : NationalBlue;

        using SKBitmap bitmap = RenderSymbols(zoom: 14, RouteLine(roadClass, network, routeRef, name: null, y: 2048));

        Assert.True(CountNear(bitmap, expected) >= 20, "盾の塗りが描画されていない");
    }

    [Fact]
    public void 都道府県道の盾は白地に青い縁で描画される()
    {
        using SKBitmap bitmap = RenderSymbols(zoom: 14, RouteLine("secondary", "JP:prefectural", "299", name: null, y: 2048));

        Assert.True(CountNear(bitmap, PrefecturalOutline) >= 10, "盾の縁が描画されていない");
    }

    [Fact]
    public void 経路番号を持たない道路には盾を描かない()
    {
        using SKBitmap bitmap = RenderSymbols(zoom: 14, RouteLine("trunk", "JP:national", routeRef: null, name: null, y: 2048));

        Assert.Equal(0, CountNear(bitmap, NationalBlue));
    }

    [Theory]
    [InlineData("motorway", "E1", ExpresswayColor)]
    [InlineData("trunk", "1", NationalColor)]
    [InlineData("primary", "186", NationalColor)]
    [InlineData("secondary", "299", PrefecturalColor)]
    public void 路線関係を持たない道路には道路種別と番号があっても盾を描かない(string roadClass, string reference, uint color)
    {
        // OpenFreeMap の日本の道路は network が road。class からの推定は国道を取り違えるため行わない
        using SKBitmap bitmap = RenderSymbols(zoom: 14, Line(roadClass, reference, name: null, y: 2048, network: "road"));

        Assert.Equal(0, CountNear(bitmap, new SKColor(color)));
    }

    [Fact]
    public void 盾はズーム12未満では描かない()
    {
        using SKBitmap bitmap = RenderSymbols(zoom: 11, RouteLine("trunk", "JP:national", "1", name: null, y: 2048));

        Assert.Equal(0, CountNear(bitmap, NationalBlue));
    }

    [Fact]
    public void 同じ位置で重なる盾は高速を優先して1つだけ描く()
    {
        using SKBitmap bitmap = RenderSymbols(
            zoom: 14,
            RouteLine("trunk", "JP:national", "1", name: null, y: 2048),
            RouteLine("motorway", "JP:E", "E1", name: null, y: 2048));

        Assert.True(CountNear(bitmap, ExpresswayGreen) >= 20);
        Assert.Equal(0, CountNear(bitmap, NationalBlue));
    }

    [Fact]
    public void 同じ路線の盾は1つのタイルで最も長い区間にだけ描く()
    {
        // 上段は長い区間、下段は短い区間（どちらも同じ路線）。短い区間の位置には描かれない
        VectorTileLayer layer = Layer(
            RouteLine("trunk", "JP:national", "1", name: null, y: 1000, x0: 0, x1: 4096),
            RouteLine("trunk", "JP:national", "1", name: null, y: 3000, x0: 1500, x1: 2600));

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
            zoom: 19, Layer(RouteLine("trunk", "JP:national", "1", name: null, y: 2048)), symbols: true, OverzoomMatrix, width: 512, height: 256);

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
            RouteLine("tertiary", "JP:prefectural", "299", name: "桜田通り", y: 2048, x0: 0, x1: 4096),
            RouteLine("tertiary", "JP:prefectural", "299", name: "桜田通り", y: 2048, x0: 600, x1: 700));

        using SKBitmap symbols = Render(zoom: 19, layer, symbols: true, OverzoomMatrix, width: 512, height: 256);
        using SKBitmap labels = Render(zoom: 19, layer, symbols: false, OverzoomMatrix, width: 512, height: 256);

        Assert.True(CountNear(symbols, PrefecturalOutline) >= 10, "盾が描画されていない");
        Assert.True(CountDark(labels) >= 8, "道路名が描画されていない");
    }

    [Fact]
    public void 画面内に見えない道路には盾も道路名も描かない()
    {
        // タイルの上端の道路は、同じ変換で画面の上に外れる
        VectorTileLayer layer = Layer(RouteLine("trunk", "JP:national", "1", name: "桜田通り", y: 0));

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
            Properties = new Dictionary<string, object>
            {
                ["class"] = "trunk",
                ["route_1_network"] = "JP:national",
                ["route_1_ref"] = "1",
            },
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
            style.RenderSymbols(canvas, tile, matrix, zoom, grid, viewport, CancellationToken.None);
        }
        else
        {
            style.RenderLineLabels(canvas, tile, matrix, zoom, grid, viewport, CancellationToken.None);
        }

        return bitmap;
    }

    private static VectorTileLayer Layer(params VectorTileFeature[] features)
        => new() { Name = "transportation_name", Extent = 4096, Features = features };

    private static VectorTileFeature Line(
        string roadClass, string? reference, string? name, int y, int x0 = 0, int x1 = 4096, string? network = null)
    {
        var properties = new Dictionary<string, object> { ["class"] = roadClass };
        AddIfNotNull(properties, "ref", reference);
        AddIfNotNull(properties, "name", name);
        AddIfNotNull(properties, "network", network);
        return HorizontalLine(properties, y, x0, x1);
    }

    /// <summary>OpenFreeMap と同じ形で、路線関係（<c>route_1_network</c>・<c>route_1_ref</c>）を持つ道路。<c>network</c> は <c>road</c>。</summary>
    private static VectorTileFeature RouteLine(
        string roadClass, string routeNetwork, string? routeRef, string? name, int y, int x0 = 0, int x1 = 4096)
    {
        var properties = new Dictionary<string, object>
        {
            ["class"] = roadClass,
            ["network"] = "road",
            ["route_1_network"] = routeNetwork,
        };
        AddIfNotNull(properties, "route_1_ref", routeRef);
        AddIfNotNull(properties, "ref", routeRef);
        AddIfNotNull(properties, "name", name);
        return HorizontalLine(properties, y, x0, x1);
    }

    private static VectorTileFeature HorizontalLine(Dictionary<string, object> properties, int y, int x0, int x1)
        => new()
        {
            GeometryType = VectorGeometryType.LineString,
            Properties = properties,
            GeometryParts = [[new VectorPoint(x0, y), new VectorPoint(x1, y)]],
        };

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

using SkiaSharp;

namespace PhotoMapStudio.Rendering;

/// <summary>POI（施設・店舗など）の分類。記号の色と形を決める。</summary>
internal enum PoiCategory
{
    /// <summary>上のどれにも当てはまらない施設（事務所・銀行・郵便・案内など）。</summary>
    Other,

    /// <summary>交通（バス停・駅・貸し自転車など）。</summary>
    Transport,

    /// <summary>駐車場。</summary>
    Parking,

    /// <summary>医療（病院・薬局・診療所など）。</summary>
    Medical,

    /// <summary>飲食。</summary>
    Food,

    /// <summary>買い物。</summary>
    Shop,

    /// <summary>宿泊・野営。</summary>
    Lodging,

    /// <summary>教育（学校・図書館など）。</summary>
    Education,

    /// <summary>文化・観光（博物館・神社寺院・名所など）。</summary>
    Culture,

    /// <summary>公園・運動・水遊び。</summary>
    Recreation,
}

/// <summary>POI の記号の形。</summary>
internal enum PoiMarker
{
    /// <summary>円。</summary>
    Dot,

    /// <summary>青い角丸の四角に白い「P」（駐車場）。</summary>
    Parking,

    /// <summary>白い十字の入った円（医療）。</summary>
    Medical,

    /// <summary>角丸の四角に白いバス（バス停）。</summary>
    BusStop,

    /// <summary>角丸の四角に白い輪（鉄道駅）。</summary>
    Station,
}

/// <summary>
/// OpenMapTiles スキーマの <c>poi</c> レイヤーの記号。<c>class</c> から分類し、分類ごとの色と形で描き分ける。
/// </summary>
/// <remarks>
/// 寸法は論理ピクセル。キャンバスが <see cref="VectorViewRequest.PixelRatio"/> 倍に拡大されるため、物理ピクセルの固定値にはならない。
/// </remarks>
internal static class OpenMapTilesPoiSymbols
{
    /// <summary>バス停の記号を描く最小のズーム。バス停は数が多く、低いズームでは他の施設を押しのけるため、駅より高くする。</summary>
    internal const int BusStopMinZoom = 16;

    /// <summary>名前のない駐車場の記号を描く最小のズーム。名前のない施設で描くのは駐車場だけにする（他は無名だと何かが分からない）。</summary>
    internal const int NamelessParkingMinZoom = 16;

    /// <summary>記号の外接矩形の半辺。衝突判定の矩形に使う。</summary>
    internal const float MarkerHalfSize = 5f;

    private const float DotRadius = 3.5f;
    private const float SquareHalfSize = 4.5f;
    private const float CrossHalfLength = 2.5f;

    /// <summary>OpenMapTiles の <c>poi</c> の <c>class</c> から分類する。</summary>
    /// <param name="poiClass">地物の <c>class</c> 属性。</param>
    /// <returns>分類。未知の値は <see cref="PoiCategory.Other"/>。</returns>
    internal static PoiCategory GetCategory(string? poiClass) => poiClass switch
    {
        "bus" or "railway" or "aerialway" or "bicycle_rental" => PoiCategory.Transport,
        "parking" => PoiCategory.Parking,
        "hospital" or "pharmacy" or "doctors" or "dentist" or "veterinary" => PoiCategory.Medical,
        "restaurant" or "cafe" or "fast_food" or "bar" or "beer" or "bakery" or "ice_cream" => PoiCategory.Food,
        "shop" or "grocery" or "clothing_store" or "alcohol_shop" or "butcher" or "hairdresser" or "laundry" or "music" or "bicycle" or "car" => PoiCategory.Shop,
        "lodging" or "campsite" => PoiCategory.Lodging,
        "school" or "college" or "kindergarten" or "library" => PoiCategory.Education,
        "museum" or "art_gallery" or "attraction" or "place_of_worship" or "theatre" or "cinema" or "monument" or "castle" or "aquarium" => PoiCategory.Culture,
        "park" or "garden" or "pitch" or "playground" or "swimming_pool" or "golf" or "sports_centre" or "running" or "athletics" or "baseball" or "picnic_site" => PoiCategory.Recreation,
        _ => PoiCategory.Other,
    };

    /// <summary>POI の <c>class</c> から記号の形を決める。</summary>
    /// <param name="poiClass">地物の <c>class</c> 属性。</param>
    internal static PoiMarker GetMarker(string? poiClass) => poiClass switch
    {
        "parking" => PoiMarker.Parking,
        "bus" => PoiMarker.BusStop,
        "railway" => PoiMarker.Station,
        _ => GetCategory(poiClass) == PoiCategory.Medical ? PoiMarker.Medical : PoiMarker.Dot,
    };

    /// <summary>記号を描く最小のズーム（<c>poi</c> レイヤー全体の最小ズームより高い場合だけ意味を持つ）。</summary>
    internal static int GetMinZoom(PoiMarker marker) => marker == PoiMarker.BusStop ? BusStopMinZoom : 0;

    /// <summary>分類の記号の色。</summary>
    internal static SKColor GetColor(PoiCategory category) => category switch
    {
        PoiCategory.Transport => new SKColor(0x2F, 0x6F, 0xB5),
        PoiCategory.Parking => new SKColor(0x3C, 0x6F, 0xB6),
        PoiCategory.Medical => new SKColor(0xD1, 0x2F, 0x2F),
        PoiCategory.Food => new SKColor(0xB8, 0x66, 0x2B),
        PoiCategory.Shop => new SKColor(0x8A, 0x4F, 0x9E),
        PoiCategory.Lodging => new SKColor(0x26, 0x86, 0x8A),
        PoiCategory.Education => new SKColor(0x39, 0x73, 0x9D),
        PoiCategory.Culture => new SKColor(0x8B, 0x6B, 0x3E),
        PoiCategory.Recreation => new SKColor(0x3C, 0x8A, 0x3C),
        _ => new SKColor(0xD5, 0x7B, 0x35),
    };

    /// <summary>注記の文字色。記号の色を暗くして、白い縁取りの上でも読める濃さにする。</summary>
    internal static SKColor GetTextColor(PoiCategory category)
    {
        SKColor color = GetColor(category);
        return new SKColor((byte)(color.Red * 0.62f), (byte)(color.Green * 0.62f), (byte)(color.Blue * 0.62f));
    }

    /// <summary>記号の中心 <paramref name="center"/> に、記号を描く。</summary>
    /// <param name="canvas">描画先。</param>
    /// <param name="center">記号の中心（画面座標）。</param>
    /// <param name="marker">記号の形。</param>
    /// <param name="category">分類（色を決める）。</param>
    /// <param name="halo">縁取りのペイント（白）。</param>
    /// <param name="glyphFont">駐車場の「P」を描くフォント。</param>
    internal static void DrawMarker(SKCanvas canvas, SKPoint center, PoiMarker marker, PoiCategory category, SKPaint halo, SKFont glyphFont)
    {
        using var fill = new SKPaint { Color = GetColor(category), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        switch (marker)
        {
            case PoiMarker.BusStop:
                {
                    var square = new SKRect(center.X - SquareHalfSize + 0.5f, center.Y - SquareHalfSize + 0.5f, center.X + SquareHalfSize - 0.5f, center.Y + SquareHalfSize - 0.5f);
                    canvas.DrawRoundRect(new SKRect(square.Left - 1f, square.Top - 1f, square.Right + 1f, square.Bottom + 1f), 2f, 2f, halo);
                    canvas.DrawRoundRect(square, 1.5f, 1.5f, fill);

                    // 白い車体（窓の帯と 2 つの車輪つき）
                    canvas.DrawRoundRect(new SKRect(center.X - 2.6f, center.Y - 2.2f, center.X + 2.6f, center.Y + 1.4f), 0.8f, 0.8f, white);
                    canvas.DrawRect(new SKRect(center.X - 1.8f, center.Y - 1.5f, center.X + 1.8f, center.Y - 0.3f), fill);
                    canvas.DrawCircle(center.X - 1.6f, center.Y + 2.3f, 0.8f, white);
                    canvas.DrawCircle(center.X + 1.6f, center.Y + 2.3f, 0.8f, white);
                    break;
                }

            case PoiMarker.Station:
                {
                    var square = new SKRect(center.X - SquareHalfSize, center.Y - SquareHalfSize, center.X + SquareHalfSize, center.Y + SquareHalfSize);
                    canvas.DrawRoundRect(new SKRect(square.Left - 1f, square.Top - 1f, square.Right + 1f, square.Bottom + 1f), 2f, 2f, halo);
                    canvas.DrawRoundRect(square, 1.5f, 1.5f, fill);
                    canvas.DrawCircle(center, 2.4f, white);
                    canvas.DrawCircle(center, 1.1f, fill);
                    break;
                }

            case PoiMarker.Parking:
                {
                    var square = new SKRect(center.X - SquareHalfSize, center.Y - SquareHalfSize, center.X + SquareHalfSize, center.Y + SquareHalfSize);
                    var outline = new SKRect(square.Left - 1f, square.Top - 1f, square.Right + 1f, square.Bottom + 1f);
                    canvas.DrawRoundRect(outline, 2f, 2f, halo);
                    canvas.DrawRoundRect(square, 1.5f, 1.5f, fill);
                    canvas.DrawText("P", center.X, center.Y + (glyphFont.Size * 0.35f), SKTextAlign.Center, glyphFont, white);
                    break;
                }

            case PoiMarker.Medical:
                {
                    canvas.DrawCircle(center, DotRadius + 1.6f, halo);
                    canvas.DrawCircle(center, DotRadius + 0.6f, fill);
                    using var stroke = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, IsAntialias = true };
                    canvas.DrawLine(center.X - CrossHalfLength, center.Y, center.X + CrossHalfLength, center.Y, stroke);
                    canvas.DrawLine(center.X, center.Y - CrossHalfLength, center.X, center.Y + CrossHalfLength, stroke);
                    break;
                }

            default:
                canvas.DrawCircle(center, DotRadius + 1f, halo);
                canvas.DrawCircle(center, DotRadius, fill);
                break;
        }
    }
}

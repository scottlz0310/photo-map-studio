using SkiaSharp;

namespace PhotoMapStudio.Rendering;

/// <summary>
/// OpenMapTiles スキーマの <c>landcover</c>（植生・地表）と <c>landuse</c>（土地利用）の塗り色。
/// </summary>
/// <remarks>
/// 背景の上で面の種類が見分けられ、かつ道路・注記を邪魔しない淡さに揃える。重なる面は、後から描く <c>landuse</c> が <c>landcover</c> を上書きする。
/// </remarks>
internal static class OpenMapTilesLandPalette
{
    private static readonly SKColor Wood = SKColor.Parse("#b5d6a2");
    private static readonly SKColor Grass = SKColor.Parse("#d3e8c0");
    private static readonly SKColor Farmland = SKColor.Parse("#ecefcf");
    private static readonly SKColor Wetland = SKColor.Parse("#cfe2d6");
    private static readonly SKColor Sand = SKColor.Parse("#f2e8c4");
    private static readonly SKColor Rock = SKColor.Parse("#dcd2c6");
    private static readonly SKColor Ice = SKColor.Parse("#f4f8fb");

    private static readonly SKColor Pitch = SKColor.Parse("#bfe4cf");
    private static readonly SKColor Playground = SKColor.Parse("#dcebcc");
    private static readonly SKColor Cemetery = SKColor.Parse("#d3dfcb");
    private static readonly SKColor Institution = SKColor.Parse("#ebe3cf");
    private static readonly SKColor Industrial = SKColor.Parse("#dfdad5");
    private static readonly SKColor Residential = SKColor.Parse("#e6e1d8");
    private static readonly SKColor Retail = SKColor.Parse("#f3ddd2");
    private static readonly SKColor Commercial = SKColor.Parse("#efdcd9");

    /// <summary>
    /// 面の塗り色を返す。
    /// </summary>
    /// <param name="landuse"><see langword="true"/> は <c>landuse</c> レイヤー、<see langword="false"/> は <c>landcover</c> レイヤー。</param>
    /// <param name="featureClass">地物の <c>class</c> 属性。</param>
    /// <returns>塗り色。描かない種類は <see langword="null"/>。</returns>
    internal static SKColor? GetFill(bool landuse, string? featureClass)
        => landuse ? GetLanduseFill(featureClass) : GetLandcoverFill(featureClass);

    /// <summary>
    /// <c>park</c> レイヤーの地物を塗りつぶすか。塗るのは、土地の用途を表す <c>recreation_ground</c>（運動場など）と
    /// <c>historic</c>（史跡）だけにする。
    /// </summary>
    /// <remarks>
    /// このレイヤーの大半は、国立公園・保護区・自然保護区といった指定区域で、範囲を示すだけである。塗ると、その範囲の植生・岩
    /// （<c>landcover</c>）が全面に隠れ、山地が一色になる。指定区域の <c>class</c> は、<c>national_park</c>・<c>nature_reserve</c>・
    /// <c>conservation</c>・<c>wilderness_preserve</c>・<c>wildlife_refuge</c>・<c>sustainable</c>・<c>marine</c> のほか、
    /// OSM の <c>protected_area</c> や保護の名称の自由記述の値まで広がり、値を列挙して除くことができない。そのため、塗る区分を列挙する
    /// （Planetiler の <c>Park.java</c> の <c>parkClass</c>。<c>class</c> が無い地物も、塗らない側に倒す）。
    /// </remarks>
    /// <param name="parkClass"><c>park</c> レイヤーの地物の <c>class</c> 属性。</param>
    internal static bool ShouldFillPark(string? parkClass)
        => parkClass is "recreation_ground" or "historic";

    private static SKColor? GetLandcoverFill(string? featureClass) => featureClass switch
    {
        "wood" => Wood,
        "grass" => Grass,
        "farmland" => Farmland,
        "wetland" => Wetland,
        "sand" => Sand,
        "rock" => Rock,
        "ice" => Ice,
        _ => null,
    };

    private static SKColor? GetLanduseFill(string? featureClass) => featureClass switch
    {
        "pitch" or "track" or "stadium" => Pitch,
        "playground" => Playground,
        "cemetery" => Cemetery,
        "school" or "college" or "university" or "hospital" or "kindergarten" or "education" or "library" => Institution,
        "industrial" or "railway" or "bus_station" => Industrial,
        "residential" => Residential,
        "retail" => Retail,
        "commercial" => Commercial,
        _ => null,
    };
}

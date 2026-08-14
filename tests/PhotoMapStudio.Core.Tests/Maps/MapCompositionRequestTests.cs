using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.Core.Tests.Maps;

public class MapCompositionRequestTests
{
    [Fact]
    public void 既定値は移植仕様書の値と一致する()
    {
        var request = new MapCompositionRequest { Center = new GeoCoordinate(35.681166, 139.767111) };

        Assert.Equal(800, request.Width);
        Assert.Equal(600, request.Height);
        Assert.Equal(15, request.Zoom);
    }

    [Fact]
    public void 既定のタイルソースは地理院タイル淡色である()
        => Assert.Equal(TileSources.GsiPale, new MapCompositionRequest { Center = default }.TileSource);

    [Fact]
    public void 代替ソースは全世界を覆うOpenStreetMapである()
    {
        Assert.Equal(TileSources.OpenStreetMap, TileSources.WorldwideFallback);

        // 既定ソースが対応するズームは、代替ソースでも必ず使える
        for (int zoom = TileSources.Default.MinZoom; zoom <= TileSources.Default.MaxZoom; zoom++)
        {
            Assert.True(
                TileSources.WorldwideFallback.SupportsZoom(zoom),
                $"代替ソースがズーム {zoom} に対応していません。");
        }
    }
}

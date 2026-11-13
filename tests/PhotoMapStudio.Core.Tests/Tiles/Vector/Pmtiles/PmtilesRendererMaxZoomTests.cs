using PhotoMapStudio.Core.Tiles.Vector;
using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector.Pmtiles;

public sealed class PmtilesRendererMaxZoomTests
{
    [Fact]
    public async Task レンダラーは準備で確定した最大ズームのタイルを要求する()
    {
        // 最大ズーム 14 のアーカイブに対してズーム 15 で描画するとき、存在しないズーム 15 を要求しない
        var requestedZooms = new List<int>();
        var source = new PreparedSource(maxZoomAfterPrepare: 14, requestedZooms);
        using var style = new OpenMapTilesStyle();
        var renderer = new VectorMapRenderer(source, style);

        await Assert.ThrowsAsync<TileFetchException>(
            () => renderer.RenderMapAsync(new PhotoMapStudio.Core.Geo.GeoCoordinate(35.0, 139.0), 15, 256, 256, session: null, tileSource: null, CancellationToken.None));

        Assert.True(source.Prepared);
        Assert.All(requestedZooms, z => Assert.Equal(14, z));
    }

    private sealed class PreparedSource(int maxZoomAfterPrepare, List<int> requestedZooms) : IVectorTileSource
    {
        public bool Prepared { get; private set; }

        public string SourceId => "prepared";

        // 準備の前は既定値（16）、準備の後はヘッダーの値
        public int MaxZoom => this.Prepared ? maxZoomAfterPrepare : 16;

        public Task PrepareAsync(TileFetchSession? session, CancellationToken cancellationToken = default)
        {
            this.Prepared = true;
            return Task.CompletedTask;
        }

        public Task<byte[]?> GetTileAsync(int z, int x, int y, TileFetchSession? session, CancellationToken cancellationToken = default)
        {
            requestedZooms.Add(z);
            return Task.FromResult<byte[]?>(null);
        }
    }
}

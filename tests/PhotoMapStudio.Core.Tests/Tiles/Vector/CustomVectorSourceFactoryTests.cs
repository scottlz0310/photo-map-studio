using System.Diagnostics.CodeAnalysis;
using System.Net;

using PhotoMapStudio.Core.Tiles.Vector;
using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

// 取得元の組み立て（キャッシュ・要求間隔・共有・ファイルの解放）は、Tiles.Tests の VectorTileSourceFactoryTests が検証する。
// ここでは、取得元のスキーマに合う描画スタイルを選ぶことと、取得元の組み立てへの委譲を確認する。
public sealed class CustomVectorSourceFactoryTests : IDisposable
{
    private readonly OpenMapTilesStyle openMapTilesStyle = new();
    private readonly GsiVectorStyle gsiStyle = new();
    private readonly NullTileCache cache = new();
    private readonly List<HttpClient> clients = [];

    public void Dispose()
    {
        this.openMapTilesStyle.Dispose();
        this.gsiStyle.Dispose();
        foreach (HttpClient client in this.clients)
        {
            client.Dispose();
        }
    }

    [Theory]
    [InlineData(VectorStyleSchema.OpenMapTiles)]
    [InlineData(VectorStyleSchema.GsiOptimalBvmap)]
    public void スキーマに応じて描画スタイルを選ぶ(VectorStyleSchema schema)
    {
        using CustomVectorSourceFactory factory = this.CreateFactory();

        var (_, style) = factory.Resolve(VectorSourceSpec.Create("https://example.com/japan.pmtiles", schema));

        Assert.Same(schema == VectorStyleSchema.GsiOptimalBvmap ? this.gsiStyle : this.openMapTilesStyle, style);
    }

    [Fact]
    public void 同じ指定は取得元を共有し別の指定は別の取得元になる()
    {
        using CustomVectorSourceFactory factory = this.CreateFactory();

        var (first, _) = factory.Resolve(VectorSourceSpec.Create("https://example.com/a.pmtiles", VectorStyleSchema.OpenMapTiles));
        var (second, _) = factory.Resolve(VectorSourceSpec.Create("https://example.com/a.pmtiles", VectorStyleSchema.OpenMapTiles));
        var (other, _) = factory.Resolve(VectorSourceSpec.Create("https://example.com/{z}/{x}/{y}.pbf", VectorStyleSchema.OpenMapTiles));

        Assert.Same(first, second);
        Assert.NotSame(first, other);
    }

    [Fact]
    public void 破棄後は取得元を解決できない()
    {
        CustomVectorSourceFactory factory = this.CreateFactory();
        var spec = VectorSourceSpec.Create("https://example.com/a.pmtiles", VectorStyleSchema.OpenMapTiles);

        factory.Dispose();

        Assert.Throws<ObjectDisposedException>(() => factory.Resolve(spec));
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "作成した HttpClient は一覧に保持し、テストの破棄時に破棄する。")]
    private CustomVectorSourceFactory CreateFactory()
    {
        var client = new HttpClient(new StubHandler());
        this.clients.Add(client);
        return new CustomVectorSourceFactory(client, this.cache, this.openMapTilesStyle, this.gsiStyle);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class NullTileCache : ITileCache
    {
        public Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);

        public Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

using Microsoft.Extensions.DependencyInjection;

using PhotoMapStudio.Core.DependencyInjection;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.Core.Tests.DependencyInjection;

public sealed class PhotoMapStudioCoreServiceCollectionExtensionsTests : IDisposable
{
    private readonly string cacheRoot = Path.Combine(Path.GetTempPath(), $"pms-di-{Guid.NewGuid():N}");
    private readonly ServiceProvider provider;

    public PhotoMapStudioCoreServiceCollectionExtensionsTests()
        => this.provider = new ServiceCollection().AddPhotoMapStudioCore(this.cacheRoot).BuildServiceProvider();

    public void Dispose()
    {
        this.provider.Dispose();
        if (Directory.Exists(this.cacheRoot))
        {
            Directory.Delete(this.cacheRoot, recursive: true);
        }
    }

    [Fact]
    public void 既定の合成経路は配信範囲外の切り替えを含む()
        => Assert.IsType<FallbackMapImageComposer>(this.provider.GetRequiredService<IMapImageComposer>());

    [Fact]
    public void 既定のタイル取得はレート制御を含む()
        => Assert.IsType<ThrottledTileClient>(this.provider.GetRequiredService<ITileClient>());

    [Fact]
    public void 既定のタイル供給はキャッシュを含む()
    {
        Assert.IsType<TileProvider>(this.provider.GetRequiredService<ITileProvider>());
        Assert.IsType<FileSystemTileCache>(this.provider.GetRequiredService<ITileCache>());
    }

    [Fact]
    public void タイル取得のHttpClientに既定のタイムアウトを適用する()
    {
        IHttpClientFactory factory = this.provider.GetRequiredService<IHttpClientFactory>();

        using HttpClient client = factory.CreateClient(HttpTileClient.HttpClientName);

        Assert.Equal(PhotoMapStudioCoreServiceCollectionExtensions.DefaultTileRequestTimeout, client.Timeout);
    }

    [Fact]
    public void 事前に登録した実装を上書きしない()
    {
        var services = new ServiceCollection();
        var stub = new StubComposer();
        services.AddSingleton<IMapImageComposer>(stub);

        using ServiceProvider custom = services.AddPhotoMapStudioCore(this.cacheRoot).BuildServiceProvider();

        Assert.Same(stub, custom.GetRequiredService<IMapImageComposer>());
    }

    private sealed class StubComposer : IMapImageComposer
    {
        public Task<MapCompositionResult> ComposeAsync(
            MapCompositionRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}

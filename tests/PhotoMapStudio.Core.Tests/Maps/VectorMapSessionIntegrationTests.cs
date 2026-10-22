using System.Net;

using Microsoft.Extensions.DependencyInjection;

using PhotoMapStudio.Core.DependencyInjection;
using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Tests.Tiles;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.Core.Tests.Maps;

/// <summary>
/// ベクタータイル取得における <see cref="TileFetchSession"/> 連携と連続失敗による停止の統合テスト。
/// </summary>
public sealed class VectorMapSessionIntegrationTests : IDisposable
{
    private static readonly GeoCoordinate Tokyo = new(35.681166, 139.767111);
    private readonly string cacheRoot = Path.Combine(Path.GetTempPath(), $"pms-vec-session-{Guid.NewGuid():N}");

    public VectorMapSessionIntegrationTests()
    {
        Directory.CreateDirectory(this.cacheRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.cacheRoot))
        {
            Directory.Delete(this.cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task 障害時は10回連続失敗後にセッションが停止し以後の要求を送信しない()
    {
        int networkRequestCount = 0;
        using var factory = new StubHttpClientFactory(_ =>
        {
            Interlocked.Increment(ref networkRequestCount);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        var services = new ServiceCollection();
        services.AddSingleton<IHttpClientFactory>(factory);
        services.AddPhotoMapStudioCore(this.cacheRoot);

        using ServiceProvider provider = services.BuildServiceProvider();
        IMapImageComposer composer = provider.GetRequiredService<IMapImageComposer>();

        var session = new TileFetchSession(TimeProvider.System);

        Exception? lastException = null;
        for (int i = 0; i < 12; i++)
        {
            var request = new MapCompositionRequest
            {
                Center = Tokyo,
                TileSource = TileSources.OpenFreeMap,
                TileSession = session,
                Width = 200,
                Height = 200,
            };

            try
            {
                await composer.ComposeAsync(request, CancellationToken.None);
            }
            catch (Exception ex) when (ex is TileFetchException or InvalidOperationException)
            {
                lastException = ex;
            }
        }

        // 10回の失敗でセッションが停止状態になり、最後の例外は InvalidOperationException であること
        Assert.True(session.IsStopped);
        Assert.IsType<InvalidOperationException>(lastException);
        int requestCountAtStop = networkRequestCount;

        // 停止後にさらに要求しても即座に InvalidOperationException となりネットワーク要求は一切増加しない
        for (int i = 0; i < 2; i++)
        {
            var request = new MapCompositionRequest
            {
                Center = Tokyo,
                TileSource = TileSources.OpenFreeMap,
                TileSession = session,
                Width = 200,
                Height = 200,
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => composer.ComposeAsync(request, CancellationToken.None));
        }

        Assert.Equal(requestCountAtStop, networkRequestCount);
    }

    [Fact]
    public async Task 正常時はセッションの取得数およびキャッシュ数が更新される()
    {
        byte[] validTile = [0x1A, 0x00];
        using var factory = new StubHttpClientFactory(req =>
        {
            if (req.RequestUri?.AbsolutePath.EndsWith("/planet", StringComparison.Ordinal) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"tiles":["https://tiles.openfreemap.org/planet/{z}/{x}/{y}.pbf"]}"""),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(validTile),
            };
        });

        var services = new ServiceCollection();
        services.AddSingleton<IHttpClientFactory>(factory);
        services.AddPhotoMapStudioCore(this.cacheRoot);

        using ServiceProvider provider = services.BuildServiceProvider();
        IMapImageComposer composer = provider.GetRequiredService<IMapImageComposer>();

        var session = new TileFetchSession(TimeProvider.System);
        var request = new MapCompositionRequest
        {
            Center = Tokyo,
            TileSource = TileSources.OpenFreeMap,
            TileSession = session,
            Width = 200,
            Height = 200,
        };

        // 1回目：ネットワークから取得
        MapCompositionResult result1 = await composer.ComposeAsync(request, CancellationToken.None);
        Assert.NotNull(result1);
        TileFetchStatistics stats1 = session.Snapshot();
        Assert.True(stats1.NetworkCount > 0);
        Assert.Equal(0, stats1.CacheCount);

        // 2回目：キャッシュから取得
        MapCompositionResult result2 = await composer.ComposeAsync(request, CancellationToken.None);
        Assert.NotNull(result2);
        TileFetchStatistics stats2 = session.Snapshot();
        Assert.True(stats2.CacheCount > 0);
    }
}

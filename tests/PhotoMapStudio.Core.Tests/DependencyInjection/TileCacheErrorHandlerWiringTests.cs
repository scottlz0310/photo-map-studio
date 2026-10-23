using Microsoft.Extensions.DependencyInjection;

using PhotoMapStudio.Core.DependencyInjection;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.Core.Tests.DependencyInjection;

/// <summary>
/// アプリが登録したタイルキャッシュのエラー通知先が、既定のキャッシュへ結線されることを確認する。
/// </summary>
public sealed class TileCacheErrorHandlerWiringTests : IDisposable
{
    private readonly string cacheRoot = Path.Combine(Path.GetTempPath(), $"pms-di-err-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(this.cacheRoot))
        {
            Directory.Delete(this.cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task 登録した通知先へキャッシュの整理で起きたIOエラーを渡す()
    {
        if (!OperatingSystem.IsWindows())
        {
            // 開いたままのファイルを削除できないのは Windows の挙動
            return;
        }

        var errors = new List<Exception>();
        var services = new ServiceCollection();
        services.AddSingleton<TileCacheErrorHandler>(errors.Add);
        services.AddPhotoMapStudioCore(this.cacheRoot);
        using ServiceProvider provider = services.BuildServiceProvider();
        ITileCache cache = provider.GetRequiredService<ITileCache>();
        await cache.WriteAsync("locked.pbf", new byte[10], CancellationToken.None);

        using (new FileStream(Path.Combine(this.cacheRoot, "locked.pbf"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await cache.PruneAsync(0);
        }

        Assert.IsAssignableFrom<IOException>(Assert.Single(errors));
    }

    [Fact]
    public async Task 通知先を登録しなくてもキャッシュは動作する()
    {
        var services = new ServiceCollection();
        services.AddPhotoMapStudioCore(this.cacheRoot);
        using ServiceProvider provider = services.BuildServiceProvider();
        ITileCache cache = provider.GetRequiredService<ITileCache>();

        await cache.WriteAsync("a.pbf", new byte[10], CancellationToken.None);

        Assert.Equal(10, cache.GetCurrentSizeBytes());
    }
}

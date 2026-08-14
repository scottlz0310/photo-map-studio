using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Photos;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.Core.DependencyInjection;

/// <summary>
/// ドメイン層の既定の構築経路。
/// </summary>
public static class PhotoMapStudioCoreServiceCollectionExtensions
{
    /// <summary>タイル取得の既定タイムアウト。</summary>
    public static TimeSpan DefaultTileRequestTimeout { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// ドメイン層のサービスを既定の構成で登録する。
    /// </summary>
    /// <param name="services">登録先。</param>
    /// <param name="tileCacheRootPath">タイルキャッシュのディレクトリ。</param>
    /// <returns>登録先。</returns>
    /// <remarks>
    /// レート制御・キャッシュ・配信範囲外の切り替えを組み合わせた構成を 1 か所に固定する。
    /// 個別に差し替える場合は、この呼び出しより前に対象のインターフェースを登録する。
    /// </remarks>
    public static IServiceCollection AddPhotoMapStudioCore(this IServiceCollection services, string tileCacheRootPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(tileCacheRootPath);

        services.AddHttpClient(HttpTileClient.HttpClientName, client => client.Timeout = DefaultTileRequestTimeout);

        services.TryAddSingleton<IExifGpsReader, ExifGpsReader>();
        services.TryAddSingleton<IPhotoFileEnumerator, PhotoFileEnumerator>();

        services.TryAddSingleton<ITileCache>(_ => new FileSystemTileCache(tileCacheRootPath));
        services.TryAddSingleton<ITileClient>(provider => new ThrottledTileClient(
            new HttpTileClient(provider.GetRequiredService<IHttpClientFactory>())));
        services.TryAddSingleton<ITileProvider>(provider => new TileProvider(
            provider.GetRequiredService<ITileClient>(),
            provider.GetRequiredService<ITileCache>()));

        // 既定のタイルソース（地理院タイル）は日本国外を配信しないため、切り替えを既定の経路に組み込む
        services.TryAddSingleton<IMapImageComposer>(provider => new FallbackMapImageComposer(
            new SkiaMapImageComposer(provider.GetRequiredService<ITileProvider>()),
            TileSources.WorldwideFallback));

        return services;
    }
}

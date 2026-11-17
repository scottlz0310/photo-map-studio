using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

namespace PhotoMapStudio.Rendering.Tests.TestSupport;

/// <summary>タイルの内容をテストが決める取得元。</summary>
internal sealed class MockVectorTileSource(int maxZoom, Func<int, int, int, byte[]?> fetchHandler) : IVectorTileSource
{
    public string SourceId => "mock-source";

    public int MaxZoom => maxZoom;

    public Task<byte[]?> GetTileAsync(int z, int x, int y, TileFetchSession? session, CancellationToken cancellationToken = default)
        => Task.FromResult(fetchHandler(z, x, y));
}

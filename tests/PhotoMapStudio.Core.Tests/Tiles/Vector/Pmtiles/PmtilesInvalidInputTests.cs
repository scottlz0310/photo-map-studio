using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;
using PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector.Pmtiles;

public sealed class PmtilesInvalidInputTests
{
    [Fact]
    public async Task PMTilesではない内容はTileFetchExceptionとして理由つきで失敗する()
    {
        using var bytes = new FixedBytesSource(new byte[4096]);
        using var source = new PmtilesVectorTileSource(bytes, "invalid");

        TileFetchException exception = await Assert.ThrowsAsync<TileFetchException>(
            () => source.GetTileAsync(14, 1, 2, session: null, CancellationToken.None));

        Assert.Contains("PMTiles として読み取れません", exception.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<FormatException>(exception.InnerException);
    }

    [Fact]
    public async Task ファイルが短すぎる場合や読み取れない場合はTileFetchExceptionとして失敗する()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pms-short-{Guid.NewGuid():N}.pmtiles");
        await File.WriteAllBytesAsync(path, "not a pmtiles file"u8.ToArray());
        try
        {
            using var bytes = new FilePmtilesByteSource(path);
            using var source = new PmtilesVectorTileSource(bytes, "short");

            TileFetchException exception = await Assert.ThrowsAsync<TileFetchException>(
                () => source.GetTileAsync(14, 1, 2, session: null, CancellationToken.None));

            Assert.Contains("PMTiles ファイルを読み取れません", exception.Message, StringComparison.Ordinal);
            Assert.IsAssignableFrom<IOException>(exception.InnerException);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task 準備ではPMTilesとして読めなくても例外にしない()
    {
        using var bytes = new FixedBytesSource(new byte[4096]);
        using var source = new PmtilesVectorTileSource(bytes, "invalid", fallbackMaxZoom: 16);

        await source.PrepareAsync(session: null, CancellationToken.None);

        Assert.Equal(16, source.MaxZoom);
    }

    [Fact]
    public async Task 準備ではPMTilesの取得に失敗しても例外にしない()
    {
        using var bytes = new FailingBytesSource();
        using var source = new PmtilesVectorTileSource(bytes, "offline", fallbackMaxZoom: 16);

        await source.PrepareAsync(session: null, CancellationToken.None);

        // 失敗は後続のタイル取得が報告する
        Assert.Equal(16, source.MaxZoom);
        await Assert.ThrowsAsync<TileFetchException>(() => source.GetTileAsync(14, 1, 2, session: null, CancellationToken.None));
    }

    [Fact]
    public async Task 準備でヘッダーを読むと最大ズームがヘッダーの値になる()
    {
        byte[] header = new byte[PmtilesHeader.HeaderSize];
        "PMTiles"u8.CopyTo(header);
        header[7] = 3;
        header[97] = (byte)PmtilesCompression.None; // 内部（ディレクトリ）の圧縮
        header[98] = (byte)PmtilesCompression.None; // タイルの圧縮
        header[100] = 0;
        header[101] = 14;
        using var bytes = new FixedBytesSource(header);
        using var source = new PmtilesVectorTileSource(bytes, "valid-header", fallbackMaxZoom: 16);

        Assert.Equal(16, source.MaxZoom);
        await source.PrepareAsync(session: null, CancellationToken.None);

        // ルートディレクトリが空でも、ヘッダーの最大ズームが使われる
        Assert.Equal(14, source.MaxZoom);
    }

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

    private sealed class FixedBytesSource(byte[] data) : IPmtilesByteSource
    {
        public Task<byte[]> ReadRangeAsync(long offset, int length, TileFetchSession? session, CancellationToken cancellationToken)
        {
            byte[] result = new byte[length];
            Array.Copy(data, offset, result, 0, Math.Min(length, data.Length - (int)offset));
            return Task.FromResult(result);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FailingBytesSource : IPmtilesByteSource
    {
        public Task<byte[]> ReadRangeAsync(long offset, int length, TileFetchSession? session, CancellationToken cancellationToken)
            => throw new TileFetchException("オフライン");

        public void Dispose()
        {
        }
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

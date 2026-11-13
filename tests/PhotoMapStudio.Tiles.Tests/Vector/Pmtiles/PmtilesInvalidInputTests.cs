using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;
using PhotoMapStudio.Tiles.Vector.Pmtiles;

namespace PhotoMapStudio.Tiles.Tests.Vector.Pmtiles;

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
    public async Task ローカルファイルの読み取りはネットワーク要求にもキャッシュ命中にも数えない()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pms-stats-{Guid.NewGuid():N}.pmtiles");
        await File.WriteAllBytesAsync(path, new byte[256]);
        try
        {
            using var bytes = new FilePmtilesByteSource(path);
            var session = new TileFetchSession(TimeProvider.System);

            byte[] read = await bytes.ReadRangeAsync(0, 127, session, CancellationToken.None);

            Assert.Equal(127, read.Length);
            Assert.Equal(new TileFetchStatistics(0, 0, 0, 0), session.Snapshot());
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
}

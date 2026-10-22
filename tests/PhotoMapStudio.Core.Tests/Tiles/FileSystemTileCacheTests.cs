using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Tests.TestSupport;

namespace PhotoMapStudio.Core.Tests.Tiles;

public sealed class FileSystemTileCacheTests : IDisposable
{
    private static readonly DateTimeOffset BaseTime = new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
    private const string Key = "abcd1234_15_1_2.png";

    private readonly string root = Path.Combine(Path.GetTempPath(), $"pms-cache-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    [Fact]
    public async Task 書き出した内容をそのまま読み出せる()
    {
        using var cache = new FileSystemTileCache(this.root);
        byte[] content = [0x89, 0x50, 0x4E, 0x47];

        await cache.WriteAsync(Key, content, CancellationToken.None);

        Assert.Equal(content, await cache.TryReadAsync(Key, CancellationToken.None));
    }

    [Fact]
    public async Task 未保存のキーではnullを返す()
    {
        using var cache = new FileSystemTileCache(this.root);

        Assert.Null(await cache.TryReadAsync(Key, CancellationToken.None));
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(8, true)]
    public async Task 保持期間を過ぎたキャッシュは無効とする(int elapsedDays, bool expectedExpired)
    {
        var clock = new FixedTimeProvider(BaseTime);
        using var cache = new FileSystemTileCache(this.root, TimeSpan.FromDays(7), clock);

        await cache.WriteAsync(Key, [0x01], CancellationToken.None);
        File.SetLastWriteTimeUtc(Path.Combine(this.root, Key), BaseTime.UtcDateTime);
        clock.Advance(TimeSpan.FromDays(elapsedDays));

        byte[]? actual = await cache.TryReadAsync(Key, CancellationToken.None);

        Assert.Equal(expectedExpired, actual is null);
    }

    [Fact]
    public void 保持期間の下限を下回る設定は受け付けない()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new FileSystemTileCache(this.root, TimeSpan.FromDays(6)));

    [Theory]
    [InlineData("../escape.png")]
    [InlineData("sub/dir.png")]
    [InlineData("sub\\dir.png")]
    public async Task キャッシュディレクトリ外を指すキーは拒否する(string key)
    {
        using var cache = new FileSystemTileCache(this.root);

        await Assert.ThrowsAsync<ArgumentException>(() => cache.TryReadAsync(key, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.WriteAsync(key, [0x01], CancellationToken.None));
    }

    [Fact]
    public async Task 同じキーへの書き出しは上書きする()
    {
        using var cache = new FileSystemTileCache(this.root);

        await cache.WriteAsync(Key, [0x01], CancellationToken.None);
        await cache.WriteAsync(Key, [0x02, 0x03], CancellationToken.None);

        Assert.Equal(new byte[] { 0x02, 0x03 }, await cache.TryReadAsync(Key, CancellationToken.None));
    }

    [Fact]
    public async Task 読み取りに失敗したキャッシュは無視する()
    {
        using var cache = new FileSystemTileCache(this.root);
        await cache.WriteAsync(Key, [0x01], CancellationToken.None);

        // 他プロセスが掴んでいる状態を再現する
        using FileStream locked = File.Open(
            Path.Combine(this.root, Key),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        Assert.Null(await cache.TryReadAsync(Key, CancellationToken.None));
    }

    [Fact]
    public async Task 一時ファイルを残さない()
    {
        using var cache = new FileSystemTileCache(this.root);

        await cache.WriteAsync(Key, [0x01], CancellationToken.None);

        Assert.Empty(Directory.EnumerateFiles(this.root, "*.tmp"));
    }

    [Fact]
    public async Task 容量上限を超過した場合はアクセス日時の古いファイルから自動削除される()
    {
        var clock = new FixedTimeProvider(BaseTime);
        using var cache = new FileSystemTileCache(this.root, timeProvider: clock, maxSizeBytes: 25);

        byte[] data1 = new byte[10];
        byte[] data2 = new byte[10];
        byte[] data3 = new byte[10];

        // 1つ目を書き込む（時刻 BaseTime）
        await cache.WriteAsync("tile1.pbf", data1, CancellationToken.None);
        File.SetLastWriteTimeUtc(Path.Combine(this.root, "tile1.pbf"), BaseTime.UtcDateTime);
        File.SetLastAccessTimeUtc(Path.Combine(this.root, "tile1.pbf"), BaseTime.UtcDateTime);

        clock.Advance(TimeSpan.FromMinutes(1));

        // 2つ目を書き込む（時刻 BaseTime + 1m）
        await cache.WriteAsync("tile2.pbf", data2, CancellationToken.None);
        File.SetLastWriteTimeUtc(Path.Combine(this.root, "tile2.pbf"), BaseTime.AddMinutes(1).UtcDateTime);
        File.SetLastAccessTimeUtc(Path.Combine(this.root, "tile2.pbf"), BaseTime.AddMinutes(1).UtcDateTime);

        Assert.Equal(20, cache.GetCurrentSizeBytes());

        clock.Advance(TimeSpan.FromMinutes(1));

        // 3つ目を書き込む（時刻 BaseTime + 2m）-> 合計 30 バイトとなり上限 25 バイトを超過
        await cache.WriteAsync("tile3.pbf", data3, CancellationToken.None);

        // 最も古い tile1.pbf が削除され、tile2 と tile3 の計 20 バイトが残る
        Assert.Null(await cache.TryReadAsync("tile1.pbf", CancellationToken.None));
        Assert.NotNull(await cache.TryReadAsync("tile2.pbf", CancellationToken.None));
        Assert.NotNull(await cache.TryReadAsync("tile3.pbf", CancellationToken.None));
        Assert.Equal(20, cache.GetCurrentSizeBytes());
    }

    [Fact]
    public async Task 読み取られたファイルはLRU削除対象から保護される()
    {
        var clock = new FixedTimeProvider(BaseTime);
        using var cache = new FileSystemTileCache(this.root, timeProvider: clock, maxSizeBytes: 25);

        byte[] data1 = new byte[10];
        byte[] data2 = new byte[10];
        byte[] data3 = new byte[10];

        // 1つ目を書き込む
        await cache.WriteAsync("tile1.pbf", data1, CancellationToken.None);
        File.SetLastWriteTimeUtc(Path.Combine(this.root, "tile1.pbf"), BaseTime.UtcDateTime);
        File.SetLastAccessTimeUtc(Path.Combine(this.root, "tile1.pbf"), BaseTime.UtcDateTime);

        clock.Advance(TimeSpan.FromMinutes(1));

        // 2つ目を書き込む
        await cache.WriteAsync("tile2.pbf", data2, CancellationToken.None);
        File.SetLastWriteTimeUtc(Path.Combine(this.root, "tile2.pbf"), BaseTime.AddMinutes(1).UtcDateTime);
        File.SetLastAccessTimeUtc(Path.Combine(this.root, "tile2.pbf"), BaseTime.AddMinutes(1).UtcDateTime);

        clock.Advance(TimeSpan.FromMinutes(1));

        // tile1 を読み取る（アクセス日時が BaseTime + 2m に更新される）
        byte[]? read1 = await cache.TryReadAsync("tile1.pbf", CancellationToken.None);
        Assert.NotNull(read1);

        clock.Advance(TimeSpan.FromMinutes(1));

        // 3つ目を書き込む（時刻 BaseTime + 3m）-> 超過！
        // tile1 は直前に読み取られたため、tile2（BaseTime + 1m）の方が古くなり、tile2 が削除されるはず
        await cache.WriteAsync("tile3.pbf", data3, CancellationToken.None);

        Assert.NotNull(await cache.TryReadAsync("tile1.pbf", CancellationToken.None));
        Assert.Null(await cache.TryReadAsync("tile2.pbf", CancellationToken.None));
        Assert.NotNull(await cache.TryReadAsync("tile3.pbf", CancellationToken.None));
    }

    [Fact]
    public async Task ClearAsyncで全ファイルが削除される()
    {
        using var cache = new FileSystemTileCache(this.root);
        await cache.WriteAsync("tile1.pbf", [0x01], CancellationToken.None);
        await cache.WriteAsync("tile2.pbf", [0x02], CancellationToken.None);

        Assert.Equal(2, cache.GetCurrentSizeBytes());

        bool result = await cache.ClearAsync(CancellationToken.None);

        Assert.True(result);

        Assert.Equal(0, cache.GetCurrentSizeBytes());
        Assert.Null(await cache.TryReadAsync("tile1.pbf", CancellationToken.None));
        Assert.Null(await cache.TryReadAsync("tile2.pbf", CancellationToken.None));
    }

    [Fact]
    public async Task ClearAsyncとWriteAsyncが並行実行されても例外なく正常完了する()
    {
        using var cache = new FileSystemTileCache(this.root);
        const int writeIterations = 200;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var writeTask = Task.Run(async () =>
        {
            for (int i = 0; i < writeIterations; i++)
            {
                byte[] data = new byte[100];
                data[0] = (byte)(i % 256);
                await cache.WriteAsync($"tile_{i % 20}.pbf", data, cts.Token).ConfigureAwait(false);
            }
        });

        var clearTask = Task.Run(async () =>
        {
            for (int i = 0; i < 20; i++)
            {
                await cache.ClearAsync(cts.Token).ConfigureAwait(false);
                await Task.Delay(5).ConfigureAwait(false);
            }
        });

        await Task.WhenAll(writeTask, clearTask).ConfigureAwait(true);

        Assert.True(cache.GetCurrentSizeBytes() >= 0);
    }

    [Fact]
    public async Task 他プロセスにロックされたファイルが残る場合ClearAsyncはfalseを返す()
    {
        using var cache = new FileSystemTileCache(this.root);
        await cache.WriteAsync("locked.pbf", new byte[1000], CancellationToken.None);

        string path = Path.Combine(this.root, "locked.pbf");
        using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            bool success = await cache.ClearAsync(CancellationToken.None);
            Assert.False(success);
            Assert.True(cache.GetCurrentSizeBytes() > 0);
        }

        bool retrySuccess = await cache.ClearAsync(CancellationToken.None);
        Assert.True(retrySuccess);
        Assert.Equal(0, cache.GetCurrentSizeBytes());
    }

    [Fact]
    public async Task 容量上限を縮小した際に追加ダウンロードなしでも指定容量以下に自動整理される()
    {
        using var cache = new FileSystemTileCache(this.root, maxSizeBytes: 2000);

        await cache.WriteAsync("large.pbf", new byte[1000], CancellationToken.None);
        Assert.Equal(1000, cache.GetCurrentSizeBytes());

        cache.MaxSizeBytes = 100;

        Assert.True(cache.GetCurrentSizeBytes() <= 100);
        Assert.Null(await cache.TryReadAsync("large.pbf", CancellationToken.None));
    }

    [Fact]
    public async Task 初期化時に既存キャッシュが上限を超過していれば自動整理される()
    {
        Directory.CreateDirectory(this.root);
        await File.WriteAllBytesAsync(Path.Combine(this.root, "existing.pbf"), new byte[1000]);

        using var cache = new FileSystemTileCache(this.root, maxSizeBytes: 100);

        Assert.True(cache.GetCurrentSizeBytes() <= 100);
    }
}

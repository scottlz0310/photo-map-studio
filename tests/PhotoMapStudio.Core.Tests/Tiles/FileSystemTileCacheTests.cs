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
    public async Task 容量上限の代入では整理せずPruneAsyncで指定容量以下に整理される()
    {
        using var cache = new FileSystemTileCache(this.root, maxSizeBytes: 2000);

        await cache.WriteAsync("large.pbf", new byte[1000], CancellationToken.None);
        Assert.Equal(1000, cache.GetCurrentSizeBytes());

        // 代入だけでは UI スレッドで列挙・削除をしない
        cache.MaxSizeBytes = 100;
        Assert.Equal(1000, cache.GetCurrentSizeBytes());

        await cache.PruneAsync(cache.MaxSizeBytes);

        Assert.True(cache.GetCurrentSizeBytes() <= 100);
        Assert.Null(await cache.TryReadAsync("large.pbf", CancellationToken.None));
    }

    [Fact]
    public async Task コンストラクタ直後は即時削除を行わず明示的な整理で容量以下になる()
    {
        Directory.CreateDirectory(this.root);
        await File.WriteAllBytesAsync(Path.Combine(this.root, "existing.pbf"), new byte[1000]).ConfigureAwait(true);

        // DI登録時と同じく引数なし（既定 1GB）で初期化
        using var cache = new FileSystemTileCache(this.root);

        // 保存済み設定が復元される前は、既定値による既存キャッシュの勝手な削除は走らない
        Assert.Equal(1000, cache.GetCurrentSizeBytes());

        // 保存済み設定（例: 100 バイト）を適用し、整理を明示的に行う
        cache.MaxSizeBytes = 100;
        await cache.PruneAsync(cache.MaxSizeBytes);

        Assert.True(cache.GetCurrentSizeBytes() <= 100);
    }

    [Fact]
    public async Task 合計サイズは書き込みと上書きと整理と消去のたびに実体と一致する()
    {
        using var cache = new FileSystemTileCache(this.root, maxSizeBytes: 100_000);

        await cache.WriteAsync("a.pbf", new byte[300], CancellationToken.None);
        await cache.WriteAsync("b.pbf", new byte[500], CancellationToken.None);
        AssertTrackedSizeMatchesDisk(cache, 800);

        // 同じキーの上書きは差分だけ増減する
        await cache.WriteAsync("a.pbf", new byte[100], CancellationToken.None);
        AssertTrackedSizeMatchesDisk(cache, 600);

        await cache.PruneAsync(200);
        AssertTrackedSizeMatchesDisk(cache, DiskSize());
        Assert.True(cache.GetCurrentSizeBytes() <= 200);

        await cache.WriteAsync("c.pbf", new byte[50], CancellationToken.None);
        AssertTrackedSizeMatchesDisk(cache, DiskSize());

        Assert.True(await cache.ClearAsync());
        Assert.Equal(0, cache.GetCurrentSizeBytes());
        await cache.WriteAsync("d.pbf", new byte[70], CancellationToken.None);
        AssertTrackedSizeMatchesDisk(cache, 70);
    }

    [Fact]
    public async Task 初回の書き込みで前回の異常終了で残った一時ファイルを回収し容量に含めない()
    {
        Directory.CreateDirectory(this.root);
        string orphan = Path.Combine(this.root, "tile.pbf.0123456789abcdef.tmp");
        await File.WriteAllBytesAsync(orphan, new byte[400]).ConfigureAwait(true);
        using var cache = new FileSystemTileCache(this.root, maxSizeBytes: 100_000);

        await cache.WriteAsync("a.pbf", new byte[100], CancellationToken.None);

        Assert.False(File.Exists(orphan));
        Assert.Equal(100, cache.GetCurrentSizeBytes());
    }

    [Fact]
    public async Task 整理で消せないファイルがあっても残りを整理しエラーを1回だけ通知する()
    {
        if (!OperatingSystem.IsWindows())
        {
            // 開いたままのファイルを削除できないのは Windows の挙動
            return;
        }

        var errors = new List<Exception>();
        using var cache = new FileSystemTileCache(this.root, maxSizeBytes: 100_000, errorObserver: errors.Add);
        var clock = BaseTime;
        foreach (string name in new[] { "locked.pbf", "x.pbf", "y.pbf" })
        {
            await cache.WriteAsync(name, new byte[100], CancellationToken.None);
            File.SetLastAccessTimeUtc(Path.Combine(this.root, name), clock.UtcDateTime);
            File.SetLastWriteTimeUtc(Path.Combine(this.root, name), clock.UtcDateTime);
            clock = clock.AddMinutes(1);
        }

        // 最も古い locked.pbf を開いたままにして、削除に失敗させる
        using (new FileStream(Path.Combine(this.root, "locked.pbf"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await cache.PruneAsync(0);
        }

        Assert.True(File.Exists(Path.Combine(this.root, "locked.pbf")));
        Assert.False(File.Exists(Path.Combine(this.root, "x.pbf")));
        Assert.False(File.Exists(Path.Combine(this.root, "y.pbf")));
        Assert.Single(errors);
        Assert.IsAssignableFrom<IOException>(errors[0]);
        Assert.Equal(100, cache.GetCurrentSizeBytes());
    }

    private long DiskSize()
        => new DirectoryInfo(this.root).EnumerateFiles().Where(f => f.Extension != ".tmp").Sum(f => f.Length);

    private void AssertTrackedSizeMatchesDisk(FileSystemTileCache cache, long expected)
    {
        Assert.Equal(expected, cache.GetCurrentSizeBytes());
        Assert.Equal(DiskSize(), cache.GetCurrentSizeBytes());
    }
}

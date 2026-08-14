using PhotoMapStudio.Core.Tiles;

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
        var cache = new FileSystemTileCache(this.root);
        byte[] content = [0x89, 0x50, 0x4E, 0x47];

        await cache.WriteAsync(Key, content, CancellationToken.None);

        Assert.Equal(content, await cache.TryReadAsync(Key, CancellationToken.None));
    }

    [Fact]
    public async Task 未保存のキーではnullを返す()
    {
        var cache = new FileSystemTileCache(this.root);

        Assert.Null(await cache.TryReadAsync(Key, CancellationToken.None));
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(8, true)]
    public async Task 保持期間を過ぎたキャッシュは無効とする(int elapsedDays, bool expectedExpired)
    {
        var clock = new FixedTimeProvider(BaseTime);
        var cache = new FileSystemTileCache(this.root, TimeSpan.FromDays(7), clock);

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
        var cache = new FileSystemTileCache(this.root);

        await Assert.ThrowsAsync<ArgumentException>(() => cache.TryReadAsync(key, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.WriteAsync(key, [0x01], CancellationToken.None));
    }

    [Fact]
    public async Task 同じキーへの書き出しは上書きする()
    {
        var cache = new FileSystemTileCache(this.root);

        await cache.WriteAsync(Key, [0x01], CancellationToken.None);
        await cache.WriteAsync(Key, [0x02, 0x03], CancellationToken.None);

        Assert.Equal(new byte[] { 0x02, 0x03 }, await cache.TryReadAsync(Key, CancellationToken.None));
    }

    [Fact]
    public async Task 一時ファイルを残さない()
    {
        var cache = new FileSystemTileCache(this.root);

        await cache.WriteAsync(Key, [0x01], CancellationToken.None);

        Assert.Empty(Directory.EnumerateFiles(this.root, "*.tmp"));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public void Advance(TimeSpan elapsed) => this.current += elapsed;

        public override DateTimeOffset GetUtcNow() => this.current;
    }
}

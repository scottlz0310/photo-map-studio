using PhotoMapStudio.Core.Photos;

namespace PhotoMapStudio.Tests.TestSupport;

internal sealed class PhotoTree : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"photo-map-scale-{Guid.NewGuid():N}");
    public PhotoTree() => Directory.CreateDirectory(this.Root);
    public string Add(string relative)
    {
        string full = Path.Combine(this.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, []);
        return full;
    }

    public IReadOnlyList<string> Populate(int count, int seed = 34)
    {
        int folderCount = Math.Max(1, (int)Math.Round(count * 225d / 5200));
        var photos = new List<string>();
        for (int index = 0; index < count; index++)
        {
            string folder = index % 15 == 0 ? string.Empty : Path.Combine("年度　２０２６", "地区(東)【写真】", $"組{index % folderCount:D3}");
            // 深い階層と、別フォルダの同名写真を固定シードで再現する。
            if (index % folderCount == 0) { folder = Path.Combine(folder, string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat("下位", 11))); }
            string name = $"IMG_{(string.IsNullOrEmpty(folder) ? index : index / folderCount):D5}_{(index * seed) % 3}";
            photos.Add(this.Add(Path.Combine(folder, name + (index % 2 == 0 ? ".JPG" : ".heic"))));
            if (index % 10 == 0) { this.Add(Path.Combine(folder, name + ".png")); }
        }
        return photos;
    }

    public void Dispose() => Directory.Delete(this.Root, recursive: true);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Core/App両方にリンクする共有ヘルパー。App.Testsで使用する。")]
internal sealed class StubPhotoFileEnumerator(IReadOnlyList<string> files) : IPhotoFileEnumerator
{
    public IReadOnlyList<string> Enumerate(string folderPath, bool includeSubfolders = false,
        IProgress<PhotoEnumerationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return [.. files.OrderBy(file => Path.GetRelativePath(folderPath, file), StringComparer.Ordinal)];
    }
}

internal sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset current = now;
    public void Advance(TimeSpan elapsed) => this.current += elapsed;
    public override DateTimeOffset GetUtcNow() => this.current;
    public override long GetTimestamp() => this.current.UtcTicks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
}

using PhotoMapStudio.Core.Photos;
using PhotoMapStudio.Tests.TestSupport;

namespace PhotoMapStudio.Core.Tests.Photos;

public class RecursiveEnumerationTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5200)]
    public void 規模別の列挙は相対パス順で再現し直下だけの回帰を維持する(int count)
    {
        using var tree = new PhotoTree();
        IReadOnlyList<string> expected = tree.Populate(count);
        var progress = new List<PhotoEnumerationProgress>();
        var enumerator = new PhotoFileEnumerator();
        IReadOnlyList<string> actual = enumerator.Enumerate(tree.Root, true, new SynchronousProgress<PhotoEnumerationProgress>(progress.Add));
        Assert.Equal(count, actual.Count);
        Assert.Equal(expected.OrderBy(p => Path.GetRelativePath(tree.Root, p), StringComparer.Ordinal), actual);
        Assert.Equal(actual, enumerator.Enumerate(tree.Root, true));
        Assert.Equal(expected.Where(p => Path.GetDirectoryName(p) == tree.Root).OrderBy(Path.GetFileName, StringComparer.Ordinal), enumerator.Enumerate(tree.Root));
        Assert.Equal(count, progress[^1].PhotoCount);
        Assert.True(progress[^1].FolderCount > 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void システムフォルダと拡張子付きフォルダを写真として数えない(bool recursive)
    {
        using var tree = new PhotoTree();
        string accepted = tree.Add("写真 (東)　【１】.JPG");
        tree.Add(Path.Combine("$RECYCLE.BIN", "trash.jpg"));
        Directory.CreateDirectory(Path.Combine(tree.Root, "folder.jpg"));
        Assert.Equal([accepted], new PhotoFileEnumerator().Enumerate(tree.Root, recursive));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void フォルダ走査中のキャンセルを通知直後に確認する(int cancelAfter)
    {
        using var tree = new PhotoTree();
        tree.Populate(100);
        using var cancellation = new CancellationTokenSource();
        int reported = 0;
        var progress = new SynchronousProgress<PhotoEnumerationProgress>(_ => { if (++reported == cancelAfter) { cancellation.Cancel(); } });
        Assert.ThrowsAny<OperationCanceledException>(() => new PhotoFileEnumerator().Enumerate(tree.Root, true, progress, cancellation.Token));
        Assert.Equal(cancelAfter, reported);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void アクセス失敗はログに残して別フォルダへ続行する(bool unauthorized)
    {
        using var tree = new PhotoTree();
        string good = tree.Add(Path.Combine("good", "photo.jpg"));
        string bad = Path.Combine(tree.Root, "bad");
        Directory.CreateDirectory(bad);
        var enumerator = new PhotoFileEnumerator(folder => folder == bad
            ? throw (unauthorized ? new UnauthorizedAccessException("拒否") : new IOException("切断"))
            : new DirectoryInfo(folder).EnumerateFileSystemInfos());
        var logs = new List<PhotoEnumerationProgress>();
        Assert.Equal([good], enumerator.Enumerate(tree.Root, true, new SynchronousProgress<PhotoEnumerationProgress>(logs.Add)));
        Assert.Equal("bad", Assert.Single(logs, log => log.Error is not null).RelativePath);
    }

    [Theory]
    [InlineData(270)]
    [InlineData(350)]
    public void 二百六十文字を超えるパスを列挙できる(int length)
    {
        using var tree = new PhotoTree();
        string folder = string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat("長いフォルダ名", length / 8));
        string photo = tree.Add(Path.Combine(folder, "photo.jpg"));
        Assert.True(photo.Length > 260);
        Assert.Equal([photo], new PhotoFileEnumerator().Enumerate(tree.Root, true));
    }

    [Theory]
    [InlineData(FileAttributes.ReparsePoint)]
    [InlineData(FileAttributes.System)]
    public void 属性による除外は走査前に判定する(FileAttributes attribute)
    {
        using var tree = new PhotoTree();
        string folder = Path.Combine(tree.Root, "skip");
        Directory.CreateDirectory(folder);
        if (attribute == FileAttributes.System)
        {
            File.SetAttributes(folder, FileAttributes.Directory | FileAttributes.System);
            try { Assert.Empty(new PhotoFileEnumerator().Enumerate(tree.Root, true)); }
            finally { File.SetAttributes(folder, FileAttributes.Directory); }
        }
        else
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { CreateNoWindow = true, UseShellExecute = false };
            start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J");
            start.ArgumentList.Add(Path.Combine(folder, "loop")); start.ArgumentList.Add(tree.Root);
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(start)!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
            try { Assert.Empty(new PhotoFileEnumerator().Enumerate(tree.Root, true)); }
            finally { Directory.Delete(Path.Combine(folder, "loop")); }
        }
    }
}

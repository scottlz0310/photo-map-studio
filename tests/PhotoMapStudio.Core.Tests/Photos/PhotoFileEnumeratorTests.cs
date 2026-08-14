using PhotoMapStudio.Core.Photos;

namespace PhotoMapStudio.Core.Tests.Photos;

public sealed class PhotoFileEnumeratorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pms-enum-{Guid.NewGuid():N}");

    public PhotoFileEnumeratorTests() => Directory.CreateDirectory(this.root);

    public void Dispose() => Directory.Delete(this.root, recursive: true);

    [Theory]
    [InlineData("photo.jpg", true)]
    [InlineData("photo.jpeg", true)]
    [InlineData("photo.tif", true)]
    [InlineData("photo.tiff", true)]
    [InlineData("photo.heic", true)]
    [InlineData("photo.JPG", true)]
    [InlineData("photo.HEIC", true)]
    [InlineData("photo.png", false)]
    [InlineData("photo.txt", false)]
    [InlineData("photo", false)]
    public void 対応拡張子のみを列挙する(string fileName, bool expected)
    {
        this.CreateFile(fileName);

        IReadOnlyList<string> actual = new PhotoFileEnumerator().Enumerate(this.root);

        Assert.Equal(expected, actual.Count == 1);
    }

    [Fact]
    public void ファイル名の昇順で列挙する()
    {
        this.CreateFile("c.jpg");
        this.CreateFile("a.jpg");
        this.CreateFile("b.heic");

        IReadOnlyList<string> actual = new PhotoFileEnumerator().Enumerate(this.root);

        Assert.Equal(["a.jpg", "b.heic", "c.jpg"], actual.Select(Path.GetFileName));
    }

    [Fact]
    public void サブフォルダは走査しない()
    {
        this.CreateFile("a.jpg");
        string child = Path.Combine(this.root, "child");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "b.jpg"), string.Empty);

        IReadOnlyList<string> actual = new PhotoFileEnumerator().Enumerate(this.root);

        Assert.Equal(["a.jpg"], actual.Select(Path.GetFileName));
    }

    [Fact]
    public void 拡張子に一致する名前のフォルダは除外する()
    {
        Directory.CreateDirectory(Path.Combine(this.root, "album.jpg"));

        Assert.Empty(new PhotoFileEnumerator().Enumerate(this.root));
    }

    [Fact]
    public void 存在しないフォルダでは空を返す()
        => Assert.Empty(new PhotoFileEnumerator().Enumerate(Path.Combine(this.root, "missing")));

    private void CreateFile(string fileName) => File.WriteAllText(Path.Combine(this.root, fileName), string.Empty);
}

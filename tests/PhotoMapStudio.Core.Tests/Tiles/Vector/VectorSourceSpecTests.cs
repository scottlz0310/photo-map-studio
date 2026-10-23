using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public sealed class VectorSourceSpecTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"pms-spec-{Guid.NewGuid():N}");

    public VectorSourceSpecTests() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, recursive: true);

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(16)]
    [InlineData(19)]
    public void URLテンプレートは指定した最大ズームを保持する(int maxZoom)
    {
        VectorSourceSpec spec = VectorSourceSpec.Create("https://tiles.example.com/{z}/{x}/{y}.pbf", VectorStyleSchema.OpenMapTiles, maxZoom);

        Assert.Equal(maxZoom, spec.MaxZoom);
    }

    [Fact]
    public void URLテンプレートの最大ズームは省略すると既定値になる()
    {
        VectorSourceSpec spec = VectorSourceSpec.Create("https://tiles.example.com/{z}/{x}/{y}.pbf", VectorStyleSchema.OpenMapTiles);

        Assert.Equal(VectorSourceSpec.DefaultXyzMaxZoom, spec.MaxZoom);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(20)]
    public void URLテンプレートの最大ズームが範囲外なら拒否する(int maxZoom)
    {
        bool created = VectorSourceSpec.TryCreate(
            "https://tiles.example.com/{z}/{x}/{y}.pbf", VectorStyleSchema.OpenMapTiles, out _, out string error, maxZoom);

        Assert.False(created);
        Assert.Contains("最大ズーム", error, StringComparison.Ordinal);
    }

    [Fact]
    public void PMTilesは最大ズームの指定を無視し同じ指定は等価になる()
    {
        VectorSourceSpec a = VectorSourceSpec.Create("https://example.com/japan.pmtiles", VectorStyleSchema.OpenMapTiles, 5);
        VectorSourceSpec b = VectorSourceSpec.Create("https://example.com/japan.pmtiles", VectorStyleSchema.OpenMapTiles, 17);

        // ヘッダーから分かるため、関係のない指定で取得元を分けない
        Assert.Equal(a, b);
        Assert.Equal(VectorSourceSpec.DefaultXyzMaxZoom, a.MaxZoom);
    }

    [Theory]
    [InlineData("https://tiles.example.com/{z}/{x}/{y}.pbf", VectorSourceKind.XyzTemplate)]
    [InlineData("http://localhost:8080/{z}/{x}/{y}.mvt", VectorSourceKind.XyzTemplate)]
    [InlineData("  https://tiles.example.com/{z}/{x}/{y}.pbf  ", VectorSourceKind.XyzTemplate)]
    [InlineData("https://example.com/maps/japan.pmtiles", VectorSourceKind.PmtilesUrl)]
    [InlineData("https://example.com/maps/Japan.PMTILES", VectorSourceKind.PmtilesUrl)]
    [InlineData("http://127.0.0.1:9000/japan.pmtiles?v=2", VectorSourceKind.PmtilesUrl)]
    public void URLの指定から種別を判定する(string location, VectorSourceKind expected)
    {
        ArgumentNullException.ThrowIfNull(location);
        VectorSourceSpec spec = VectorSourceSpec.Create(location, VectorStyleSchema.OpenMapTiles);

        Assert.Equal(expected, spec.Kind);
        Assert.Equal(location.Trim(), spec.Location);
    }

    [Fact]
    public void ローカルのPMTilesファイルはファイルとして判定する()
    {
        string path = this.CreateFile("japan.pmtiles");

        VectorSourceSpec spec = VectorSourceSpec.Create(path, VectorStyleSchema.GsiOptimalBvmap);

        Assert.Equal(VectorSourceKind.PmtilesFile, spec.Kind);
        Assert.Equal(VectorStyleSchema.GsiOptimalBvmap, spec.Schema);
    }

    [Theory]
    [InlineData(null, "指定してください")]
    [InlineData("", "指定してください")]
    [InlineData("   ", "指定してください")]
    [InlineData("ftp://example.com/{z}/{x}/{y}.pbf", "http または https")]
    [InlineData("ftp://example.com/japan.pmtiles", "http または https")]
    [InlineData("https://example.com/map.json", ".pmtiles")]
    [InlineData("https://example.com/{z}/{x}", ".pmtiles")]
    [InlineData("https://user:secret@example.com/{z}/{x}/{y}.pbf", "認証情報")]
    [InlineData("https://user:secret@example.com/japan.pmtiles", "認証情報")]
    [InlineData("https://example.com/japan.pmtiles#frag", "フラグメント")]
    [InlineData("relative/path/japan.pmtiles", "絶対パス")]
    public void 不正な指定は理由つきで拒否する(string? location, string expectedMessagePart)
    {
        bool created = VectorSourceSpec.TryCreate(location, VectorStyleSchema.OpenMapTiles, out VectorSourceSpec? spec, out string error);

        Assert.False(created);
        Assert.Null(spec);
        Assert.Contains(expectedMessagePart, error, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => VectorSourceSpec.Create(location, VectorStyleSchema.OpenMapTiles));
    }

    [Fact]
    public void PMTiles以外のファイルと存在しないファイルを拒否する()
    {
        string notPmtiles = this.CreateFile("japan.json");
        string missing = Path.Combine(this.directory, "missing.pmtiles");

        Assert.False(VectorSourceSpec.TryCreate(notPmtiles, VectorStyleSchema.OpenMapTiles, out _, out string notPmtilesError));
        Assert.Contains(".pmtiles", notPmtilesError, StringComparison.Ordinal);
        Assert.False(VectorSourceSpec.TryCreate(missing, VectorStyleSchema.OpenMapTiles, out _, out string missingError));
        Assert.Contains("見つかりません", missingError, StringComparison.Ordinal);
    }

    [Fact]
    public void 未定義のスキーマを拒否する()
    {
        bool created = VectorSourceSpec.TryCreate("https://example.com/japan.pmtiles", (VectorStyleSchema)99, out _, out string error);

        Assert.False(created);
        Assert.Contains("スキーマ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void 同じ指定は等価で別の指定とは区別される()
    {
        var a = VectorSourceSpec.Create("https://example.com/japan.pmtiles", VectorStyleSchema.OpenMapTiles);
        var same = VectorSourceSpec.Create("https://example.com/japan.pmtiles", VectorStyleSchema.OpenMapTiles);
        var otherSchema = VectorSourceSpec.Create("https://example.com/japan.pmtiles", VectorStyleSchema.GsiOptimalBvmap);

        Assert.Equal(a, same);
        Assert.NotEqual(a, otherSchema);
    }

    [Fact]
    public void URLテンプレートはTileSourceの検証を通り配信元のホストを判定できる()
    {
        string file = this.CreateFile("japan.pmtiles");
        var xyz = VectorSourceSpec.Create("https://tiles.example.com/{z}/{x}/{y}.pbf", VectorStyleSchema.OpenMapTiles);
        var url = VectorSourceSpec.Create("https://example.com/maps/japan.pmtiles", VectorStyleSchema.OpenMapTiles);
        var local = VectorSourceSpec.Create(file, VectorStyleSchema.OpenMapTiles);

        TileSource xyzSource = TileSources.CustomVector(xyz, "© 出典");
        TileSource urlSource = TileSources.CustomVector(url, "© 出典");
        TileSource fileSource = TileSources.CustomVector(local, "© 出典");

        Assert.Equal("tiles.example.com", xyzSource.BuildTileUri(0, 0, 0).Host);
        Assert.Equal("example.com", urlSource.BuildTileUri(0, 0, 0).Host);
        Assert.True(fileSource.IsLoopback);
        Assert.False(xyzSource.IsLoopback);
        Assert.False(urlSource.IsLoopback);
    }

    [Fact]
    public void ファイルごとにURLテンプレートが異なりキャッシュや統計で混ざらない()
    {
        string a = this.CreateFile("a.pmtiles");
        string b = this.CreateFile("b.pmtiles");

        string templateA = VectorSourceSpec.Create(a, VectorStyleSchema.OpenMapTiles).UrlTemplate;
        string templateB = VectorSourceSpec.Create(b, VectorStyleSchema.OpenMapTiles).UrlTemplate;

        Assert.NotEqual(templateA, templateB);
        Assert.Equal(templateA, VectorSourceSpec.Create(a, VectorStyleSchema.OpenMapTiles).UrlTemplate);
    }

    [Fact]
    public void レート制御は取得元に応じて決まる()
    {
        string file = this.CreateFile("japan.pmtiles");

        Assert.Equal(TileRateLimit.Loopback, VectorSourceSpec.Create(file, VectorStyleSchema.OpenMapTiles).RateLimit);
        Assert.Equal(TileRateLimit.Loopback, VectorSourceSpec.Create("http://localhost:8080/japan.pmtiles", VectorStyleSchema.OpenMapTiles).RateLimit);
        Assert.Equal(TileRateLimit.Loopback, VectorSourceSpec.Create("http://127.0.0.1/{z}/{x}/{y}.pbf", VectorStyleSchema.OpenMapTiles).RateLimit);
        Assert.Equal(TileRateLimit.Conservative, VectorSourceSpec.Create("https://example.com/japan.pmtiles", VectorStyleSchema.OpenMapTiles).RateLimit);
        Assert.Equal(TileRateLimit.Conservative, VectorSourceSpec.Create("https://example.com/{z}/{x}/{y}.pbf", VectorStyleSchema.OpenMapTiles).RateLimit);
    }

    [Fact]
    public void 利用者指定のベクタータイルソースはベクターとして取得元を保持する()
    {
        var spec = VectorSourceSpec.Create("https://example.com/japan.pmtiles", VectorStyleSchema.OpenMapTiles);

        TileSource source = TileSources.CustomVector(spec, "© 出典");

        Assert.True(source.IsVector);
        Assert.Equal(spec, source.VectorSource);
        Assert.Equal("© 出典", source.Attribution);
        Assert.Null(TileSources.GsiVector.VectorSource);
    }

    private string CreateFile(string name)
    {
        string path = Path.Combine(this.directory, name);
        File.WriteAllBytes(path, [0]);
        return path;
    }
}

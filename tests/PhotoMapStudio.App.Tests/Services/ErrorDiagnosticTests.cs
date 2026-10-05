using System.Text;
using System.Text.Json;

using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Photos;
using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Tests.TestSupport;

namespace PhotoMapStudio.App.Tests.Services;

public class ErrorDiagnosticTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 許可した情報だけを保存し例外内の個人情報は含めない(bool aggregate)
    {
        const string secret = "C:\\秘密の写真\\GPS_35_139.jpg https://example.com/tiles?token=secret";
        Exception inner;
        try { throw new IOException(secret, unchecked((int)0x80070020)); }
        catch (IOException exception) { inner = exception; }
        inner.Data["secret"] = secret;
        Exception failure = aggregate ? new AggregateException(secret, inner, new UnauthorizedAccessException(secret)) : new ExifGpsReadException(secret, secret, inner);
        var store = new Store();
        new LocalErrorDiagnosticSink(store, "0.2.0.0", new FixedTimeProvider(DateTimeOffset.UnixEpoch)).Record(ErrorDiagnosticStage.BatchOutput, 7, failure);
        string json = Encoding.UTF8.GetString(Assert.Single(store.Records));
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("GPS_35_139", json, StringComparison.Ordinal);
        Assert.DoesNotContain("token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ErrorDiagnosticTests.cs", json, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal("0.2.0.0", root.GetProperty("AppVersion").GetString());
        Assert.Equal("BatchOutput", root.GetProperty("Stage").GetString());
        Assert.Equal(7, root.GetProperty("ItemIndex").GetInt32());
        Assert.Equal(DateTimeOffset.UnixEpoch, root.GetProperty("Timestamp").GetDateTimeOffset());
        JsonElement cause = root.GetProperty("Exceptions")[1];
        Assert.Equal("System.IO.IOException", cause.GetProperty("Type").GetString());
        Assert.Equal("0x80070020", cause.GetProperty("HResult").GetString());
        Assert.NotEmpty(cause.GetProperty("Calls").EnumerateArray());
        Assert.Equal(aggregate ? 3 : 2, root.GetProperty("Exceptions").GetArrayLength());
        Assert.Equal(["SchemaVersion", "Timestamp", "AppVersion", "Stage", "ItemIndex", "Exceptions"], root.EnumerateObject().Select(property => property.Name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 保存失敗は元の例外と診断保存の例外を保持して伝播する(bool unauthorized)
    {
        Exception writeFailure = unauthorized ? new UnauthorizedAccessException("書き込み拒否") : new IOException("空き容量不足");
        var original = new IOException("元の読み取りエラー");
        var sink = new LocalErrorDiagnosticSink(new Store(writeFailure), "0.2.0.0", TimeProvider.System);
        IOException failure = Assert.Throws<IOException>(() => sink.Record(ErrorDiagnosticStage.BatchGps, 1, original));
        Assert.Contains("診断ログ", failure.Message, StringComparison.Ordinal);
        Assert.Equal([original, writeFailure], Assert.IsType<AggregateException>(failure.InnerException).InnerExceptions);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    public void ローテーションは現行と直前の二ファイルに容量を制限する(int limit)
    {
        using var tree = new PhotoTree();
        var store = new DiagnosticFileStore(tree.Root, limit);
        byte[] record = Encoding.UTF8.GetBytes("{\"Error\":\"保存失敗\"}");
        for (int index = 0; index < 20; index++) { store.Append(record); }
        string[] files = Directory.GetFiles(tree.Root);
        Assert.Equal(2, files.Length);
        Assert.All(files, path => Assert.InRange(new FileInfo(path).Length, 1, limit));
        Assert.All(files, path => Assert.All(File.ReadAllLines(path), line => { using JsonDocument document = JsonDocument.Parse(line); Assert.Equal("保存失敗", document.RootElement.GetProperty("Error").GetString()); }));
    }

    [Theory]
    [InlineData(ErrorDiagnosticStage.BatchGps)]
    [InlineData(ErrorDiagnosticStage.BatchMap)]
    [InlineData(ErrorDiagnosticStage.BatchOutput)]
    public async Task 一括生成の失敗を実際の処理段階で一度だけ記録する(ErrorDiagnosticStage stage)
    {
        using var tree = new PhotoTree(); tree.Add("photo.jpg");
        if (stage == ErrorDiagnosticStage.BatchOutput) { await File.WriteAllTextAsync(Path.Combine(tree.Root, "maps"), "フォルダ作成を妨げるファイル"); }
        var failure = new IOException("失敗");
        var sink = new Sink();
        var service = new BatchGenerationService(new PhotoFileEnumerator(), new Reader(stage == ErrorDiagnosticStage.BatchGps ? failure : null),
            new Composer(stage == ErrorDiagnosticStage.BatchMap ? failure : null), errorDiagnostics: sink);
        BatchGenerationSummary result = await service.GenerateAsync(new() { InputFolderPath = tree.Root, OutputFolderPath = "maps" }, null, CancellationToken.None);
        Assert.Equal(1, result.ErrorCount);
        Assert.Equal(stage, Assert.Single(sink.Entries).Stage);
        Assert.Equal(1, sink.Entries[0].Index);
    }

    [Fact]
    public async Task 並行する記録が行単位で混ざらず保存される()
    {
        using var tree = new PhotoTree();
        var store = new DiagnosticFileStore(tree.Root);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(index => Task.Run(() => store.Append(Encoding.UTF8.GetBytes($"{{\"Index\":{index}}}")))));
        string[] lines = await File.ReadAllLinesAsync(Path.Combine(tree.Root, "errors.jsonl"));
        Assert.Equal(20, lines.Length);
        int[] indexes = lines.Select(line => { using JsonDocument document = JsonDocument.Parse(line); return document.RootElement.GetProperty("Index").GetInt32(); }).Order().ToArray();
        Assert.Equal(Enumerable.Range(0, 20), indexes);
    }

    [Fact]
    public void 上限を超える単一記録は書き込まず保存失敗として通知する()
    {
        using var tree = new PhotoTree();
        Assert.Throws<IOException>(() => new DiagnosticFileStore(tree.Root, 16).Append(new byte[16]));
        Assert.Empty(Directory.GetFiles(tree.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task プレビューのGPS失敗は一覧読み込みと単独生成で記録する(bool generatePhoto)
    {
        using var tree = new PhotoTree(); string photo = tree.Add("photo.jpg");
        var failure = new IOException("読み取り拒否");
        var sink = new Sink();
        var service = new PreviewGenerationService(new PhotoFileEnumerator(), new Reader(failure), new Composer(), sink);
        if (generatePhoto) { Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => service.GenerateAsync(new PreviewPhoto(photo), new(), CancellationToken.None))); }
        else { await using IAsyncEnumerator<PreviewPhoto> photos = service.LoadPhotosAsync(tree.Root, false, null, CancellationToken.None).GetAsyncEnumerator(); Assert.False(await photos.MoveNextAsync()); }
        Assert.Equal(ErrorDiagnosticStage.PreviewGps, Assert.Single(sink.Entries).Stage);
    }

    [Fact]
    public async Task プレビューの地図生成失敗を記録する()
    {
        using var tree = new PhotoTree(); string photo = tree.Add("photo.jpg");
        var sink = new Sink();
        var service = new PreviewGenerationService(new PhotoFileEnumerator(), new Reader(), new Composer(new IOException("生成失敗")), sink);
        Assert.False((await service.GenerateAsync(new PreviewPhoto(photo), new(), CancellationToken.None)).Succeeded);
        Assert.Equal(ErrorDiagnosticStage.PreviewMap, Assert.Single(sink.Entries).Stage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task フォルダ列挙の失敗は一括とプレビューの両経路で記録する(bool preview)
    {
        using var tree = new PhotoTree();
        var failure = new UnauthorizedAccessException("拒否");
        var enumerator = new PhotoFileEnumerator(_ => throw failure);
        var sink = new Sink();
        if (preview) { await using IAsyncEnumerator<PreviewPhoto> photos = new PreviewGenerationService(enumerator, new Reader(), new Composer(), sink).LoadPhotosAsync(tree.Root, true, null, CancellationToken.None).GetAsyncEnumerator(); Assert.False(await photos.MoveNextAsync()); }
        else { await new BatchGenerationService(enumerator, new Reader(), new Composer(), errorDiagnostics: sink).GenerateAsync(new() { InputFolderPath = tree.Root, OutputFolderPath = "maps" }, null, CancellationToken.None); }
        Assert.Same(failure, Assert.Single(sink.Entries).Failure);
        Assert.Equal(ErrorDiagnosticStage.Enumeration, sink.Entries[0].Stage);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task 診断保存に失敗した場合は処理を続行せず上位へ通知する(bool preview, bool enumeration)
    {
        using var tree = new PhotoTree(); tree.Add("photo.jpg");
        var original = new IOException("読み取り失敗");
        var writeFailure = new UnauthorizedAccessException("診断保存拒否");
        var sink = new LocalErrorDiagnosticSink(new Store(writeFailure), "0.2.0.0", TimeProvider.System);
        var enumerator = enumeration ? new PhotoFileEnumerator(_ => throw original) : new PhotoFileEnumerator();
        var reader = new Reader(enumeration ? null : original);
        IOException failure;
        if (preview)
        {
            await using IAsyncEnumerator<PreviewPhoto> photos = new PreviewGenerationService(enumerator, reader, new Composer(), sink)
                .LoadPhotosAsync(tree.Root, true, null, CancellationToken.None).GetAsyncEnumerator();
            failure = await Assert.ThrowsAsync<IOException>(async () => { await photos.MoveNextAsync().ConfigureAwait(false); });
        }
        else
        {
            failure = await Assert.ThrowsAsync<IOException>(() => new BatchGenerationService(enumerator, reader, new Composer(), errorDiagnostics: sink)
                .GenerateAsync(new() { InputFolderPath = tree.Root, OutputFolderPath = "maps" }, null, CancellationToken.None));
        }
        Assert.Equal([original, writeFailure], Assert.IsType<AggregateException>(failure.InnerException).InnerExceptions);
    }

    private sealed class Store(Exception? failure = null) : IErrorDiagnosticStore
    {
        public List<byte[]> Records { get; } = [];
        public void Append(ReadOnlySpan<byte> record) { if (failure is not null) { throw failure; } this.Records.Add(record.ToArray()); }
    }
    private sealed class Sink : IErrorDiagnosticSink
    {
        public List<(ErrorDiagnosticStage Stage, int Index, Exception Failure)> Entries { get; } = [];
        public void Record(ErrorDiagnosticStage stage, int itemIndex, Exception failure) => this.Entries.Add((stage, itemIndex, failure));
    }
    private sealed class Reader(Exception? failure = null) : IExifGpsReader
    {
        public GeoCoordinate? Read(string filePath) { if (failure is not null) { throw failure; } return new(35, 139); }
    }
    private sealed class Composer(Exception? failure = null) : IMapImageComposer
    {
        public Task<MapCompositionResult> ComposeAsync(MapCompositionRequest request, CancellationToken cancellationToken)
            => failure is null ? Task.FromResult(new MapCompositionResult(new byte[] { 1 }, TileSources.GsiPale, false)) : Task.FromException<MapCompositionResult>(failure);
    }
}

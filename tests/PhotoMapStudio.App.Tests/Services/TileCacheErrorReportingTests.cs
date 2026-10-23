using PhotoMapStudio.App.Services;

namespace PhotoMapStudio.App.Tests.Services;

public class TileCacheErrorReportingTests
{
    [Fact]
    public void キャッシュのIOエラーをタイルキャッシュの段階で診断ログへ記録する()
    {
        var sink = new RecordingSink();
        var failure = new IOException("使用中");

        TileCacheErrorReporting.CreateHandler(sink)(failure);

        (ErrorDiagnosticStage stage, int index, Exception recorded) = Assert.Single(sink.Entries);
        Assert.Equal(ErrorDiagnosticStage.TileCache, stage);
        Assert.Equal(0, index);
        Assert.Same(failure, recorded);
    }

    [Fact]
    public void 診断ログを保存できなくてもキャッシュの処理を止めない()
    {
        var sink = new RecordingSink { Failure = new IOException("診断ログの保存に失敗") };

        // 例外を送出しない（診断はあくまで補助）
        TileCacheErrorReporting.CreateHandler(sink)(new IOException("使用中"));

        Assert.Single(sink.Entries);
    }

    [Fact]
    public void シンクがnullなら引数の例外を送出する()
        => Assert.Throws<ArgumentNullException>(() => TileCacheErrorReporting.CreateHandler(null!));

    private sealed class RecordingSink : IErrorDiagnosticSink
    {
        public List<(ErrorDiagnosticStage Stage, int Index, Exception Failure)> Entries { get; } = [];

        public Exception? Failure { get; init; }

        public void Record(ErrorDiagnosticStage stage, int itemIndex, Exception failure)
        {
            this.Entries.Add((stage, itemIndex, failure));
            if (this.Failure is not null)
            {
                throw this.Failure;
            }
        }
    }
}

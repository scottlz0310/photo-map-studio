using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace PhotoMapStudio.App.Services;

internal interface IErrorDiagnosticStore
{
    void Append(ReadOnlySpan<byte> record);
}

internal sealed class LocalErrorDiagnosticSink(IErrorDiagnosticStore store, string appVersion, TimeProvider timeProvider) : IErrorDiagnosticSink
{
    public void Record(ErrorDiagnosticStage stage, int itemIndex, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var exceptions = new List<ExceptionDiagnostic>();
        var pending = new Queue<Exception>();
        pending.Enqueue(failure);
        while (pending.TryDequeue(out Exception? current) && exceptions.Count < 8)
        {
            // Message・Data・生のStackTraceにはパスやURLが入り得るため、許可したフィールドだけ作る。
            string[] calls = new StackTrace(current, false).GetFrames()
                .Select(frame => frame.GetMethod()).OfType<MethodBase>().Take(32)
                .Select(method => $"{method.DeclaringType?.FullName}.{method.Name}").ToArray();
            exceptions.Add(new(current.GetType().FullName, $"0x{current.HResult.ToString("X8", CultureInfo.InvariantCulture)}", calls));
            if (current is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions) { pending.Enqueue(inner); }
            }
            else if (current.InnerException is not null) { pending.Enqueue(current.InnerException); }
        }

        var record = new ErrorDiagnostic(1, timeProvider.GetUtcNow(), appVersion, stage.ToString(), itemIndex, exceptions);
        try { store.Append(JsonSerializer.SerializeToUtf8Bytes(record)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException("ローカル診断ログの保存に失敗しました。保存先の権限と空き容量を確認してください。", new AggregateException(failure, exception));
        }
    }

    private sealed record ErrorDiagnostic(int SchemaVersion, DateTimeOffset Timestamp, string AppVersion, string Stage, int ItemIndex, List<ExceptionDiagnostic> Exceptions);
    private sealed record ExceptionDiagnostic(string? Type, string HResult, string[] Calls);
}

internal sealed class DiagnosticFileStore(string folderPath, long fileLimit = 1024 * 1024) : IErrorDiagnosticStore
{
    private readonly Lock writeGate = new();

    public void Append(ReadOnlySpan<byte> record)
    {
        using (this.writeGate.EnterScope())
        {
            if (record.Length + 1 > fileLimit) { throw new IOException("診断記録がファイルの容量上限を超えています。"); }
            Directory.CreateDirectory(folderPath);
            string current = Path.Combine(folderPath, "errors.jsonl");
            if (File.Exists(current) && new FileInfo(current).Length + record.Length + 1 > fileLimit)
            {
                File.Move(current, Path.Combine(folderPath, "errors.previous.jsonl"), overwrite: true);
            }

            using var stream = new FileStream(current, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(record);
            stream.WriteByte((byte)'\n');
        }
    }
}

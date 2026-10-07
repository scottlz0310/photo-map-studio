using System.Text.Json;
using System.Text.Json.Serialization;

using PhotoMapStudio.App.Models;

namespace PhotoMapStudio.App.Services;

internal sealed class BatchGenerationHistoryStore(string folderPath) : IBatchGenerationHistoryStore
{
    internal const int RetainedRunCount = 30;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public async Task<IReadOnlyList<BatchGenerationHistory>> LoadAsync()
        => (await this.ReadEntriesAsync().ConfigureAwait(false)).Select(entry => entry.History).ToArray();

    public async Task SaveAsync(BatchGenerationHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        List<Entry> entries = await this.ReadEntriesAsync().ConfigureAwait(false);
        string path = Path.Combine(folderPath, $"batch-{history.Id:N}.json");
        string temporary = path + ".tmp";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(history, JsonOptions);
        var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            await stream.WriteAsync(bytes).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        // 完全なJSONだけを履歴として公開し、保存成功後に古い実行を整理する。
        File.Move(temporary, path);
        entries.Add(new(path, history));
        foreach (Entry old in Sort(entries).Skip(RetainedRunCount))
        {
            File.Delete(old.Path);
        }
    }

    private async Task<List<Entry>> ReadEntriesAsync()
    {
        Directory.CreateDirectory(folderPath);
        var entries = new List<Entry>();
        foreach (string path in Directory.EnumerateFiles(folderPath, "batch-*.json", SearchOption.TopDirectoryOnly))
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            BatchGenerationHistory? history;
            await using (stream.ConfigureAwait(false))
            {
                try { history = await JsonSerializer.DeserializeAsync<BatchGenerationHistory>(stream, JsonOptions).ConfigureAwait(false); }
                catch (JsonException exception) { throw new InvalidDataException($"一括生成の履歴を読み取れません: {Path.GetFileName(path)}", exception); }
            }
            if (history is null || history.SchemaVersion != 1 || history.InputFolderPath is null || history.Issues is null)
            {
                throw new InvalidDataException($"一括生成の履歴形式に対応していません: {Path.GetFileName(path)}");
            }

            entries.Add(new(path, history));
        }

        return Sort(entries).ToList();
    }

    private static IOrderedEnumerable<Entry> Sort(IEnumerable<Entry> entries)
        => entries.OrderByDescending(entry => entry.History.FinishedAtUtc).ThenByDescending(entry => entry.History.Id);

    private sealed record Entry(string Path, BatchGenerationHistory History);
}

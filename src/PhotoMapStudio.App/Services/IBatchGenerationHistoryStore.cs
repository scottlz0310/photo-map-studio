using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.App.Models;

namespace PhotoMapStudio.App.Services;

/// <summary>一括生成の履歴保存境界。</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "ViewModelとテストへ注入する保存契約。")]
public interface IBatchGenerationHistoryStore
{
    /// <summary>最新から順に履歴を読む。失敗は呼び出し元へ伝える。</summary>
    Task<IReadOnlyList<BatchGenerationHistory>> LoadAsync();

    /// <summary>結果を保存し、直近30回を残す。</summary>
    Task SaveAsync(BatchGenerationHistory history);
}

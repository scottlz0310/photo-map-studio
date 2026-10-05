using System.Diagnostics.CodeAnalysis;

namespace PhotoMapStudio.App.Services;

/// <summary>個人情報を含めずに運用エラーを記録する。</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "サービスから注入して使用する診断記録の契約。")]
public interface IErrorDiagnosticSink
{
    /// <summary>処理段階・番号と例外の診断情報を記録する。</summary>
    void Record(ErrorDiagnosticStage stage, int itemIndex, Exception failure);
}

/// <summary>診断ログに記録する処理段階。</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "公開サービス契約が使用する処理段階。")]
public enum ErrorDiagnosticStage
{
    /// <summary>写真フォルダの列挙。</summary>
    Enumeration,
    /// <summary>プレビュー用GPS読み取り。</summary>
    PreviewGps,
    /// <summary>プレビューの地図生成。</summary>
    PreviewMap,
    /// <summary>一括生成用GPS読み取り。</summary>
    BatchGps,
    /// <summary>一括生成の地図生成。</summary>
    BatchMap,
    /// <summary>一括生成の出力先準備・PNG保存。</summary>
    BatchOutput,
}

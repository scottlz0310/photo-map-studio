using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.App.Models;

namespace PhotoMapStudio.App.Services;

/// <summary>
/// PhotoMapStudio の設定を読み書きするリポジトリ。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "設定 ViewModel の DI 境界およびテストの差し替え契約として公開する。")]
public interface IPhotoMapSettingsRepository
{
    /// <summary>設定を読み込む。</summary>
    /// <returns>保存された設定。未保存の項目は既定値。</returns>
    PhotoMapSettings Load();

    /// <summary>設定を保存する。</summary>
    /// <param name="settings">保存する設定。</param>
    void Save(PhotoMapSettings settings);
}

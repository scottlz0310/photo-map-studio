using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.App.Models;

/// <summary>
/// 設定 UI で選択できるタイルソース。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1054:URI パラメーターは文字列にしません",
    Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "XAML の選択項目と設定 ViewModel の公開バインディング型として使用する。")]
public sealed record TileSourceChoice
{
    /// <summary>
    /// タイルソースの選択肢を構築する。
    /// </summary>
    /// <param name="key">永続化に使用する安定したキー。</param>
    /// <param name="displayName">UI に表示する名前。</param>
    /// <param name="source">プリセットのタイルソース。カスタム URL の場合は <see langword="null"/>。</param>
    public TileSourceChoice(string key, string displayName, TileSource? source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        this.Key = key;
        this.DisplayName = displayName;
        this.Source = source;
    }

    /// <summary>永続化に使用するキー。</summary>
    public string Key { get; }

    /// <summary>UI に表示する名前。</summary>
    public string DisplayName { get; }

    /// <summary>プリセットのタイルソース。</summary>
    public TileSource? Source { get; }

    /// <summary>カスタム URL の選択肢かどうか。</summary>
    public bool IsCustom => this.Source is null;

    /// <summary>選択可能な最小ズーム。</summary>
    public int MinZoom => this.Source?.MinZoom ?? 1;

    /// <summary>選択可能な最大ズーム。</summary>
    public int MaxZoom => this.Source?.MaxZoom ?? 19;

    /// <summary>
    /// 選択肢から実際のタイルソースを構築する。
    /// </summary>
    /// <param name="urlTemplate">カスタム URL テンプレート。</param>
    /// <param name="attribution">カスタム出典表示。</param>
    /// <returns>使用するタイルソース。</returns>
    public TileSource CreateSource(string urlTemplate, string attribution)
        => this.Source ?? TileSources.Custom(urlTemplate, attribution);
}

/// <summary>
/// タイルソース選択肢のカタログ。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "設定 ViewModel の公開バインディング型を構築するカタログとして使用する。")]
public static class TileSourceChoices
{
    /// <summary>地理院タイル（淡色）のキー。</summary>
    public const string GsiPaleKey = "gsi-pale";

    /// <summary>地理院タイル（標準）のキー。</summary>
    public const string GsiStandardKey = "gsi-standard";

    /// <summary>OpenStreetMap のキー。</summary>
    public const string OpenStreetMapKey = "openstreetmap";

    /// <summary>カスタム URL のキー。</summary>
    public const string CustomKey = "custom";

    /// <summary>地理院タイル（淡色）。</summary>
    public static TileSourceChoice GsiPale { get; } = new(GsiPaleKey, TileSources.GsiPale.Name, TileSources.GsiPale);

    /// <summary>地理院タイル（標準）。</summary>
    public static TileSourceChoice GsiStandard { get; } = new(GsiStandardKey, TileSources.GsiStandard.Name, TileSources.GsiStandard);

    /// <summary>OpenStreetMap。</summary>
    public static TileSourceChoice OpenStreetMap { get; } = new(OpenStreetMapKey, TileSources.OpenStreetMap.Name, TileSources.OpenStreetMap);

    /// <summary>カスタム URL。</summary>
    public static TileSourceChoice Custom { get; } = new(CustomKey, "カスタム URL", null);

    /// <summary>表示順に並べた選択肢。</summary>
    public static IReadOnlyList<TileSourceChoice> All { get; } = [GsiPale, GsiStandard, OpenStreetMap, Custom];

    /// <summary>
    /// 永続化されたキーから選択肢を取得する。未知のキーは既定値へ戻す。
    /// </summary>
    /// <param name="key">永続化されたキー。</param>
    /// <returns>対応する選択肢。</returns>
    public static TileSourceChoice FromKey(string? key)
        => All.FirstOrDefault(choice => string.Equals(choice.Key, key, StringComparison.Ordinal)) ?? GsiPale;
}

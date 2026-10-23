using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

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
    /// <param name="source">プリセットのタイルソース。カスタムの場合は <see langword="null"/>。</param>
    /// <param name="isCustomVector">利用者が指定するベクタータイル（PMTiles または URL）の選択肢かどうか。</param>
    public TileSourceChoice(string key, string displayName, TileSource? source, bool isCustomVector = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        this.Key = key;
        this.DisplayName = displayName;
        this.Source = source;
        this.IsCustomVector = isCustomVector && source is null;
    }

    /// <summary>永続化に使用するキー。</summary>
    public string Key { get; }

    /// <summary>UI に表示する名前。</summary>
    public string DisplayName { get; }

    /// <summary>プリセットのタイルソース。</summary>
    public TileSource? Source { get; }

    /// <summary>カスタム URL（ラスタータイル）の選択肢かどうか。</summary>
    public bool IsCustom => this.Source is null && !this.IsCustomVector;

    /// <summary>利用者が指定するベクタータイル（PMTiles または URL）の選択肢かどうか。</summary>
    public bool IsCustomVector { get; }

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

    /// <summary>
    /// 選択肢から実際のタイルソースを構築する（利用者が指定するベクタータイルを含む）。
    /// </summary>
    /// <param name="urlTemplate">カスタム URL テンプレート。</param>
    /// <param name="attribution">カスタム出典表示。</param>
    /// <param name="vectorLocation">ベクタータイルの場所（PMTiles の URL・ファイルパス、または URL テンプレート）。</param>
    /// <param name="vectorSchema">ベクタータイルのレイヤー構成。</param>
    /// <param name="vectorAttribution">ベクタータイルの出典表示。</param>
    /// <param name="vectorMaxZoom">URL テンプレートのベクタータイルが配信する最大ズーム（PMTiles では無視する）。</param>
    /// <returns>使用するタイルソース。</returns>
    /// <exception cref="ArgumentException">指定が不正な場合。</exception>
    public TileSource CreateSource(
        string urlTemplate,
        string attribution,
        string vectorLocation,
        VectorStyleSchema vectorSchema,
        string vectorAttribution,
        int vectorMaxZoom)
    {
        if (!this.IsCustomVector)
        {
            return this.CreateSource(urlTemplate, attribution);
        }

        VectorSourceSpec spec = VectorSourceSpec.Create(vectorLocation, vectorSchema, vectorMaxZoom);
        if (string.IsNullOrWhiteSpace(vectorAttribution))
        {
            throw new ArgumentException("出典表示を入力してください。地図データの出典は生成画像に焼き込みます。");
        }

        return TileSources.CustomVector(spec, vectorAttribution.Trim());
    }
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

    /// <summary>地理院タイル（最適化ベクトル）のキー。</summary>
    public const string GsiVectorKey = "gsi-vector";

    /// <summary>OpenFreeMap のキー。</summary>
    public const string OpenFreeMapKey = "openfreemap";

    /// <summary>OpenStreetMap のキー。</summary>
    public const string OpenStreetMapKey = "openstreetmap";

    /// <summary>カスタム URL のキー。</summary>
    public const string CustomKey = "custom";

    /// <summary>カスタム（ベクター）のキー。</summary>
    public const string CustomVectorKey = "custom-vector";

    /// <summary>地理院タイル（淡色）。</summary>
    public static TileSourceChoice GsiPale { get; } = new(GsiPaleKey, TileSources.GsiPale.Name, TileSources.GsiPale);

    /// <summary>地理院タイル（標準）。</summary>
    public static TileSourceChoice GsiStandard { get; } = new(GsiStandardKey, TileSources.GsiStandard.Name, TileSources.GsiStandard);

    /// <summary>地理院タイル（最適化ベクトル）。</summary>
    public static TileSourceChoice GsiVector { get; } = new(GsiVectorKey, TileSources.GsiVector.Name, TileSources.GsiVector);

    /// <summary>OpenFreeMap（ベクター）。</summary>
    public static TileSourceChoice OpenFreeMap { get; } = new(OpenFreeMapKey, TileSources.OpenFreeMap.Name, TileSources.OpenFreeMap);

    /// <summary>OpenStreetMap。</summary>
    public static TileSourceChoice OpenStreetMap { get; } = new(OpenStreetMapKey, TileSources.OpenStreetMap.Name, TileSources.OpenStreetMap);

    /// <summary>カスタム URL。</summary>
    public static TileSourceChoice Custom { get; } = new(CustomKey, "カスタム URL", null);

    /// <summary>カスタム（ベクター）。PMTiles または <c>{z}/{x}/{y}</c> のベクタータイル URL を利用者が指定する。</summary>
    public static TileSourceChoice CustomVector { get; } = new(CustomVectorKey, "カスタム（ベクター）", null, isCustomVector: true);

    /// <summary>表示順に並べた選択肢。</summary>
    public static IReadOnlyList<TileSourceChoice> All { get; } = [GsiPale, GsiStandard, GsiVector, OpenFreeMap, OpenStreetMap, Custom, CustomVector];

    /// <summary>
    /// 永続化されたキーから選択肢を取得する。未知のキーは既定値へ戻す。
    /// </summary>
    /// <param name="key">永続化されたキー。</param>
    /// <returns>対応する選択肢。</returns>
    public static TileSourceChoice FromKey(string? key)
        => All.FirstOrDefault(choice => string.Equals(choice.Key, key, StringComparison.Ordinal)) ?? GsiPale;
}

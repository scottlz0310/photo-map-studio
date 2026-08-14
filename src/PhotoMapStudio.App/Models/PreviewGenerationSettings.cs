using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.App.Models;

/// <summary>
/// プレビュー生成に使用する未保存の設定スナップショット。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1056:URI プロパティは文字列にしません",
    Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "XAML と App.Tests から差し替え可能なプレビュー契約として公開する。")]
public sealed record PreviewGenerationSettings
{
    /// <summary>写真を列挙する入力フォルダ。</summary>
    public string InputFolderPath { get; init; } = string.Empty;

    /// <summary>出力幅。</summary>
    public double Width { get; init; } = MapCompositionRequest.DefaultWidth;

    /// <summary>出力高さ。</summary>
    public double Height { get; init; } = MapCompositionRequest.DefaultHeight;

    /// <summary>ズームレベル。</summary>
    public double Zoom { get; init; } = MapCompositionRequest.DefaultZoom;

    /// <summary>ピン画像のパス。</summary>
    public string PinImagePath { get; init; } = string.Empty;

    /// <summary>選択中のタイルソース。</summary>
    public TileSourceChoice SelectedTileSource { get; init; } = TileSourceChoices.GsiPale;

    /// <summary>カスタムタイル URL テンプレート。</summary>
    public string CustomTileUrlTemplate { get; init; } = PhotoMapSettings.DefaultCustomTileUrlTemplate;

    /// <summary>カスタムタイルの出典表示。</summary>
    public string CustomTileAttribution { get; init; } = PhotoMapSettings.DefaultCustomTileAttribution;

    /// <summary>選択中の設定から実際のタイルソースを構築する。</summary>
    public TileSource CreateTileSource()
        => this.SelectedTileSource.CreateSource(
            this.CustomTileUrlTemplate,
            this.CustomTileAttribution);
}

using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.App.Models;

/// <summary>
/// 一括生成に使用する設定スナップショット。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1056:URI プロパティは文字列にしません",
    Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "DI サービスと App.Tests の生成契約として公開する。")]
public sealed record BatchGenerationSettings
{
    /// <summary>写真を列挙する入力フォルダ。</summary>
    public string InputFolderPath { get; init; } = string.Empty;

    /// <summary>PNG を保存する出力フォルダ。</summary>
    public string OutputFolderPath { get; init; } = string.Empty;

    /// <summary>出力幅。</summary>
    public int Width { get; init; } = MapCompositionRequest.DefaultWidth;

    /// <summary>出力高さ。</summary>
    public int Height { get; init; } = MapCompositionRequest.DefaultHeight;

    /// <summary>ズームレベル。</summary>
    public int Zoom { get; init; } = MapCompositionRequest.DefaultZoom;

    /// <summary>ピン画像のパス。</summary>
    public string PinImagePath { get; init; } = string.Empty;

    /// <summary>使用するタイルソース。</summary>
    public TileSource TileSource { get; init; } = TileSources.Default;
}

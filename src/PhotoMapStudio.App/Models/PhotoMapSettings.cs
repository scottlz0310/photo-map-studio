using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

namespace PhotoMapStudio.App.Models;

/// <summary>
/// アプリケーション設定の保存用スナップショット。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1056:URI プロパティは文字列にしません",
    Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "設定リポジトリの公開契約としてテストと将来の UI 構成から参照する。")]
public sealed record PhotoMapSettings
{
    /// <summary>通信失敗時に欠落を明示したベクター地図を生成する。既定は無効。</summary>
    public bool AllowIncompleteVectorMaps { get; init; }

    /// <summary>既定の出力幅。</summary>
    public const int DefaultWidth = MapCompositionRequest.DefaultWidth;

    /// <summary>既定の出力高さ。</summary>
    public const int DefaultHeight = MapCompositionRequest.DefaultHeight;

    /// <summary>既定のズームレベル。</summary>
    public const int DefaultZoom = MapCompositionRequest.DefaultZoom;

    /// <summary>既定のタイルキャッシュ容量上限（MB）。</summary>
    public const int DefaultTileCacheSizeMb = 1024;

    /// <summary>既定のカスタムタイル URL。</summary>
    public static string DefaultCustomTileUrlTemplate => TileSources.OpenStreetMap.UrlTemplate;

    /// <summary>既定のカスタムタイル出典。</summary>
    public static string DefaultCustomTileAttribution => TileSources.OpenStreetMap.Attribution;

    /// <summary>入力フォルダ。</summary>
    public string InputFolderPath { get; init; } = string.Empty;

    /// <summary>出力フォルダ。</summary>
    public string OutputFolderPath { get; init; } = string.Empty;

    /// <summary>下位フォルダも探索する。</summary>
    public bool IncludeSubfolders { get; init; }

    /// <summary>出力名の先頭文字。</summary>
    public string OutputFilePrefix { get; init; } = string.Empty;

    /// <summary>出力名の末尾文字。</summary>
    public string OutputFilePostfix { get; init; } = "_map";

    /// <summary>出力幅。</summary>
    public int Width { get; init; } = DefaultWidth;

    /// <summary>出力高さ。</summary>
    public int Height { get; init; } = DefaultHeight;

    /// <summary>ズームレベル。</summary>
    public int Zoom { get; init; } = DefaultZoom;

    /// <summary>タイルキャッシュ容量上限（MB）。</summary>
    public int TileCacheSizeMb { get; init; } = DefaultTileCacheSizeMb;

    /// <summary>ピン種別。</summary>
    public PinKind PinKind { get; init; } = PinKind.GreenPin;

    /// <summary>ピンアンカー基準点。</summary>
    public PinAnchor PinAnchor { get; init; } = PinAnchor.BottomCenter;

    /// <summary>ピン画像のパス（カスタム画像時等）。</summary>
    public string PinImagePath { get; init; } = string.Empty;

    /// <summary>選択中のタイルソースキー。</summary>
    public string TileSourceKey { get; init; } = TileSourceChoices.GsiPaleKey;

    /// <summary>カスタムタイル URL テンプレート。</summary>
    public string CustomTileUrlTemplate { get; init; } = DefaultCustomTileUrlTemplate;

    /// <summary>カスタム（ベクター）の場所（PMTiles の URL・ファイルパス、またはベクタータイルの URL テンプレート）。</summary>
    public string CustomVectorLocation { get; init; } = string.Empty;

    /// <summary>カスタム（ベクター）のレイヤー構成。</summary>
    public VectorStyleSchema CustomVectorSchema { get; init; } = VectorStyleSchema.OpenMapTiles;

    /// <summary>カスタム（ベクター）の出典表示。</summary>
    public string CustomVectorAttribution { get; init; } = string.Empty;

    /// <summary>カスタム（ベクター）の URL テンプレートが配信する最大ズーム。</summary>
    public int CustomVectorMaxZoom { get; init; } = VectorSourceSpec.DefaultXyzMaxZoom;

    /// <summary>カスタムタイルの出典表示。</summary>
    public string CustomTileAttribution { get; init; } = DefaultCustomTileAttribution;
}

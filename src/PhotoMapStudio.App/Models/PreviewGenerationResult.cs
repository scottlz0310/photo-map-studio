using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.Core.Geo;

namespace PhotoMapStudio.App.Models;

/// <summary>
/// プレビュー生成の結果。
/// </summary>
/// <param name="Image">生成した PNG。失敗時は空。</param>
/// <param name="Coordinate">GPS 座標。合成失敗時にも保持する。</param>
/// <param name="Message">画面に表示する結果メッセージ。</param>
/// <param name="Succeeded">生成に成功したかどうか。</param>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "プレビュー生成サービスと App.Tests の結果契約として公開する。")]
public sealed record PreviewGenerationResult(
    ReadOnlyMemory<byte> Image,
    GeoCoordinate? Coordinate,
    string Message,
    bool Succeeded);

using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.Core.Tiles.Vector;

namespace PhotoMapStudio.App.Models;

/// <summary>
/// 設定 UI で選択できる、ベクタータイルのレイヤー構成（スキーマ）。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "XAML の選択項目と設定 ViewModel の公開バインディング型として使用する。")]
public sealed record VectorSchemaChoice(VectorStyleSchema Schema, string DisplayName)
{
    /// <summary>
    /// 利用可能なすべてのスキーマの選択肢。
    /// </summary>
    public static readonly IReadOnlyList<VectorSchemaChoice> All =
    [
        new(VectorStyleSchema.OpenMapTiles, "OpenMapTiles（OpenStreetMap 系）"),
        new(VectorStyleSchema.GsiOptimalBvmap, "国土地理院 最適化ベクトル"),
    ];

    /// <summary>
    /// 既定のスキーマの選択肢。
    /// </summary>
    public static VectorSchemaChoice Default => All[0];

    /// <summary>
    /// 指定した <see cref="VectorStyleSchema"/> に対応する選択肢を取得する。
    /// </summary>
    /// <param name="schema">スキーマ。</param>
    /// <returns>対応する選択肢。</returns>
    public static VectorSchemaChoice FromSchema(VectorStyleSchema schema)
        => All.FirstOrDefault(c => c.Schema == schema) ?? Default;
}

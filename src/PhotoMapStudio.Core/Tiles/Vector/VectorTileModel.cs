namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// ベクタータイルのジオメトリ種別。
/// </summary>
public enum VectorGeometryType
{
    Unknown = 0,
    Point = 1,
    LineString = 2,
    Polygon = 3,
}

/// <summary>
/// タイルローカル座標系における 2D 整数座標。
/// </summary>
/// <param name="X">タイル左上を原点とする X 座標。</param>
/// <param name="Y">タイル左上を原点とする Y 座標。</param>
public readonly record struct VectorPoint(int X, int Y);

/// <summary>
/// ベクタータイル内の単一地物（フィーチャー）。
/// </summary>
public sealed class VectorTileFeature
{
    /// <summary>地物の一意の ID（省略時は 0）。</summary>
    public ulong Id { get; init; }

    /// <summary>ジオメトリ種別。</summary>
    public VectorGeometryType GeometryType { get; init; }

    /// <summary>属性タグの辞書。</summary>
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>(StringComparer.Ordinal);

    /// <summary>
    /// ジオメトリを構成するパスまたは点群のリスト。
    /// Point の場合は各要素が 1 点のリスト、LineString の場合は各要素が連続線のリスト、
    /// Polygon の場合は外輪および内輪（穴）のリングリスト。
    /// </summary>
    public IReadOnlyList<IReadOnlyList<VectorPoint>> GeometryParts { get; init; } = [];

    /// <summary>
    /// 文字列属性を取得する。存在しない場合は <see langword="null"/>。
    /// </summary>
    /// <param name="key">プロパティ名。</param>
    /// <returns>文字列値または <see langword="null"/>。</returns>
    public string? GetString(string key)
    {
        return this.Properties.TryGetValue(key, out object? val) ? val?.ToString() : null;
    }

    /// <summary>
    /// 整数属性を取得する。存在しないまたは変換できない場合は <see langword="null"/>。
    /// </summary>
    /// <param name="key">プロパティ名。</param>
    /// <returns>64-bit 整数値または <see langword="null"/>。</returns>
    public long? GetInt64(string key)
    {
        if (!this.Properties.TryGetValue(key, out object? val) || val is null)
        {
            return null;
        }

        if (val is long l)
        {
            return l;
        }

        if (val is int i)
        {
            return i;
        }

        if (val is ulong ul && ul <= long.MaxValue)
        {
            return (long)ul;
        }

        return long.TryParse(val.ToString(), out long parsed) ? parsed : null;
    }
}

/// <summary>
/// ベクタータイル内の単一レイヤー（例: "transportation", "water" 等）。
/// </summary>
public sealed class VectorTileLayer
{
    /// <summary>レイヤー名。</summary>
    public required string Name { get; init; }

    /// <summary>タイルローカル座標の最大解像度（既定 4096）。</summary>
    public uint Extent { get; init; } = 4096;

    /// <summary>レイヤーの MVT バージョン（通常 2）。</summary>
    public uint Version { get; init; } = 2;

    /// <summary>レイヤーに含まれる地物一覧。</summary>
    public IReadOnlyList<VectorTileFeature> Features { get; init; } = [];
}

/// <summary>
/// デコード済みのベクタータイルデータ全体。
/// </summary>
public sealed class VectorTile
{
    /// <summary>タイルに含まれる全レイヤー。</summary>
    public IReadOnlyList<VectorTileLayer> Layers { get; init; } = [];

    /// <summary>
    /// 指定された名前のレイヤーを検索して取得する。
    /// </summary>
    /// <param name="name">レイヤー名。</param>
    /// <returns>該当するレイヤー、見つからない場合は <see langword="null"/>。</returns>
    public VectorTileLayer? GetLayer(string name)
    {
        for (int i = 0; i < this.Layers.Count; i++)
        {
            if (string.Equals(this.Layers[i].Name, name, StringComparison.Ordinal))
            {
                return this.Layers[i];
            }
        }

        return null;
    }
}

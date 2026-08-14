using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.Core.Maps;

/// <summary>
/// 地図画像の合成結果。
/// </summary>
/// <param name="Png">生成した PNG のバイト列。</param>
/// <param name="TileSource">実際に使用したタイルソース。要求と異なる場合がある。</param>
/// <param name="UsedFallback">要求したタイルソースが配信範囲外で、代替ソースへ切り替えたかどうか。</param>
public sealed record MapCompositionResult(ReadOnlyMemory<byte> Png, TileSource TileSource, bool UsedFallback);

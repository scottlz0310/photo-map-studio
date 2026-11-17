using PhotoMapStudio.Tiles.Vector;

namespace PhotoMapStudio.Rendering;

/// <summary>
/// デコード済みのベクタータイル（<see cref="VectorTile"/>）のメモリ上の LRU キャッシュ。
/// 同じ地点を続けて描くとき、取得とデコードを省く。複数の描画から同時に使ってよい。
/// </summary>
/// <remarks>
/// 容量は、タイルごとのデコード時のメモリ割り当て量の見積もりの合計で数える。見積もりは実際の保持量と一致しない。
/// 1 枚の描画に要る分は、容量を超えていても捨てない（追加した直後の 1 件は残す）。
/// 欠落したタイル（配信元にない・取得できない）は覚えない。
/// </remarks>
public sealed class DecodedTileCache
{
    private readonly object gate = new();
    private readonly Dictionary<TileKey, LinkedListNode<Entry>> map = [];
    private readonly LinkedList<Entry> lru = new();
    private readonly long capacityBytes;
    private long usedBytes;

    /// <summary>キャッシュを初期化する。</summary>
    /// <param name="capacityBytes">容量（デコード時のメモリ割り当て量の見積もりの合計。バイト）。正の値。</param>
    public DecodedTileCache(long capacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityBytes);
        this.capacityBytes = capacityBytes;
    }

    /// <summary>保持しているタイルの数。</summary>
    public int Count
    {
        get
        {
            lock (this.gate)
            {
                return this.map.Count;
            }
        }
    }

    /// <summary>保持しているタイルの大きさの見積もりの合計（バイト）。</summary>
    public long UsedBytes
    {
        get
        {
            lock (this.gate)
            {
                return this.usedBytes;
            }
        }
    }

    internal bool TryGet(TileKey key, out VectorTile tile)
    {
        lock (this.gate)
        {
            if (this.map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                this.lru.Remove(node);
                this.lru.AddFirst(node);
                tile = node.Value.Tile;
                return true;
            }
        }

        tile = null!;
        return false;
    }

    internal void Add(TileKey key, VectorTile tile, long estimatedBytes)
    {
        long size = Math.Max(estimatedBytes, 64);
        lock (this.gate)
        {
            if (this.map.TryGetValue(key, out LinkedListNode<Entry>? existing))
            {
                this.usedBytes -= existing.Value.Bytes;
                this.lru.Remove(existing);
                this.map.Remove(key);
            }

            this.map[key] = this.lru.AddFirst(new Entry(key, tile, size));
            this.usedBytes += size;

            // 追加した 1 件は、容量を超えていても残す（1 枚の描画に要る分まで捨てない）
            while (this.usedBytes > this.capacityBytes && this.lru.Count > 1)
            {
                LinkedListNode<Entry> last = this.lru.Last!;
                this.lru.RemoveLast();
                this.map.Remove(last.Value.Key);
                this.usedBytes -= last.Value.Bytes;
            }
        }
    }

    internal readonly record struct TileKey(string SourceId, int Z, int X, int Y);

    private sealed record Entry(TileKey Key, VectorTile Tile, long Bytes);
}

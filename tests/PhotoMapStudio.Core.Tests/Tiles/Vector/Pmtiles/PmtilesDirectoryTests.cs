using PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector.Pmtiles;

public sealed class PmtilesDirectoryTests
{
    [Fact]
    public void Varintの読み書きが一貫する()
    {
        ulong[] testValues = [0, 1, 127, 128, 255, 300, 16384, 1000000, ulong.MaxValue / 2, ulong.MaxValue - 1];

        using var ms = new MemoryStream();
        foreach (ulong val in testValues)
        {
            PmtilesVarint.WriteVarint(val, ms);
        }

        byte[] bytes = ms.ToArray();
        int offset = 0;
        foreach (ulong expected in testValues)
        {
            ulong actual = PmtilesVarint.ReadVarint(bytes, ref offset);
            Assert.Equal(expected, actual);
        }

        Assert.Equal(bytes.Length, offset);
    }

    [Fact]
    public void ディレクトリのエンコードとデコードが一致する()
    {
        var originalEntries = new List<PmtilesEntry>
        {
            new() { TileId = 10, Offset = 0, Length = 100, RunLength = 1 },
            new() { TileId = 15, Offset = 100, Length = 250, RunLength = 2 }, // 連続オフセット (o=0)
            new() { TileId = 30, Offset = 500, Length = 80, RunLength = 0 }, // 不連続オフセットかつリーフディレクトリ
        };

        byte[] encoded = PmtilesDirectory.Encode(originalEntries);
        IReadOnlyList<PmtilesEntry> decoded = PmtilesDirectory.Decode(encoded);

        Assert.Equal(originalEntries.Count, decoded.Count);
        for (int i = 0; i < originalEntries.Count; i++)
        {
            Assert.Equal(originalEntries[i].TileId, decoded[i].TileId);
            Assert.Equal(originalEntries[i].Offset, decoded[i].Offset);
            Assert.Equal(originalEntries[i].Length, decoded[i].Length);
            Assert.Equal(originalEntries[i].RunLength, decoded[i].RunLength);
            Assert.Equal(originalEntries[i].IsLeafDirectory, decoded[i].IsLeafDirectory);
        }
    }

    [Fact]
    public void 正確にタイルエントリまたはリーフを二分探索できる()
    {
        var entries = new List<PmtilesEntry>
        {
            new() { TileId = 10, Offset = 0, Length = 100, RunLength = 1 }, // TileId: 10
            new() { TileId = 20, Offset = 100, Length = 200, RunLength = 3 }, // TileId: 20, 21, 22
            new() { TileId = 50, Offset = 300, Length = 150, RunLength = 0 }, // Leaf directory starting at 50
            new() { TileId = 100, Offset = 450, Length = 120, RunLength = 1 }, // TileId: 100
        };

        // 1. 完全一致
        var res10 = PmtilesDirectory.FindTile(entries, 10);
        Assert.NotNull(res10);
        Assert.Equal(10UL, res10.TileId);

        // 2. RunLength による範囲内ヒット (21 は 20..22 の範囲)
        var res21 = PmtilesDirectory.FindTile(entries, 21);
        Assert.NotNull(res21);
        Assert.Equal(20UL, res21.TileId);
        Assert.Equal(100L, res21.Offset);

        // 3. 範囲外の隙間 (15 は 10..10 の後、20 の前)
        var res15 = PmtilesDirectory.FindTile(entries, 15);
        Assert.Null(res15);

        // 4. リーフディレクトリ配下の探索 (50以上100未満は50のリーフに委譲)
        var res75 = PmtilesDirectory.FindTile(entries, 75);
        Assert.NotNull(res75);
        Assert.Equal(50UL, res75.TileId);
        Assert.True(res75.IsLeafDirectory);

        // 5. 先頭より前
        var res5 = PmtilesDirectory.FindTile(entries, 5);
        Assert.Null(res5);

        // 6. 末尾より後 (最後のタイルは 100, runLength=1 なので 101 は null)
        var res101 = PmtilesDirectory.FindTile(entries, 101);
        Assert.Null(res101);
    }
}

using PhotoMapStudio.Core.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public class LabelCollisionGridTests
{
    private readonly SKRect viewport = new(0, 0, 800, 600);

    [Fact]
    public void 曲線注記の一部が衝突した場合は配置全体を登録しない()
    {
        var grid = new LabelCollisionGrid();
        Assert.True(grid.TryPlace(new SKRect(100, 100, 150, 120), this.viewport));
        SKRect free = new(200, 100, 250, 120);
        Assert.False(grid.TryPlace([free, new SKRect(110, 100, 160, 120)], this.viewport));
        Assert.True(grid.TryPlace(free, this.viewport));
    }

    [Fact]
    public void 同じ名前は指定距離を空けて配置しClearで名前も消す()
    {
        var grid = new LabelCollisionGrid();
        SKRect first = new(20, 20, 60, 40);
        SKRect near = new(20, 70, 60, 90);
        Assert.True(grid.TryPlace([first], this.viewport, "place:同じ町名", 200));
        Assert.False(grid.TryPlace([near], this.viewport, "place:同じ町名", 200));
        Assert.True(grid.TryPlace([new SKRect(400, 20, 440, 40)], this.viewport, "place:同じ町名", 200));
        grid.Clear();
        Assert.True(grid.TryPlace([near], this.viewport, "place:同じ町名", 200));
    }

    [Fact]
    public void 画面内に収まる最初の矩形は配置に成功する()
    {
        var grid = new LabelCollisionGrid(padding: 2f);
        var rect = new SKRect(100, 100, 200, 120);

        bool placed = grid.TryPlace(rect, this.viewport);

        Assert.True(placed);
    }

    [Fact]
    public void 画面外にはみ出る矩形は配置に失敗する()
    {
        var grid = new LabelCollisionGrid(padding: 2f);
        var outsideLeft = new SKRect(-10, 100, 50, 120);
        var outsideRight = new SKRect(750, 100, 850, 120);
        var outsideTop = new SKRect(100, -5, 200, 15);
        var outsideBottom = new SKRect(100, 590, 200, 610);

        Assert.False(grid.TryPlace(outsideLeft, this.viewport));
        Assert.False(grid.TryPlace(outsideRight, this.viewport));
        Assert.False(grid.TryPlace(outsideTop, this.viewport));
        Assert.False(grid.TryPlace(outsideBottom, this.viewport));
    }

    [Fact]
    public void 余白を含めて重複する矩形は配置を拒否される()
    {
        var grid = new LabelCollisionGrid(padding: 2f);
        var first = new SKRect(100, 100, 200, 120);
        Assert.True(grid.TryPlace(first, this.viewport));

        // 完全に重なる矩形
        Assert.False(grid.TryPlace(new SKRect(110, 105, 180, 115), this.viewport));

        // padding（2px）範囲内で接触する矩形 (right = 200 に対して left = 201)
        Assert.False(grid.TryPlace(new SKRect(201, 100, 300, 120), this.viewport));

        // 十分に離れた矩形 (left = 205) は配置可能
        Assert.True(grid.TryPlace(new SKRect(205, 100, 300, 120), this.viewport));
    }

    [Fact]
    public void Clearを呼ぶと登録済み矩形がリセットされる()
    {
        var grid = new LabelCollisionGrid(padding: 2f);
        var rect = new SKRect(100, 100, 200, 120);

        Assert.True(grid.TryPlace(rect, this.viewport));
        Assert.False(grid.TryPlace(rect, this.viewport));

        grid.Clear();

        // クリア後は再度同じ位置に配置可能
        Assert.True(grid.TryPlace(rect, this.viewport));
    }
}

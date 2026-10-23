using PhotoMapStudio.Core.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public sealed class PathLabelRendererTests
{
    [Fact]
    public void 注記を選んだ区間の中央に全文描画し原点オフセットで片側へずらさない()
    {
        using var bitmap = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        Assert.True(Render(canvas, [[new(20, 150), new(280, 150)]], new LabelCollisionGrid()));
        var darkX = new List<int>();
        for (int y = 0; y < 300; y++)
        {
            for (int x = 0; x < 300; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 150) { darkX.Add(x); }
            }
        }

        Assert.NotEmpty(darkX);
        Assert.InRange(darkX.Min(), 100, 135);
        Assert.InRange(darkX.Max(), 165, 200);
        Assert.InRange((darkX.Min() + darkX.Max()) / 2f, 145, 155);
    }

    [Theory]
    [InlineData(20, 100, 280, 100)]
    [InlineData(280, 100, 20, 100)]
    [InlineData(150, 20, 150, 280)]
    [InlineData(20, 250, 280, 50)]
    public void 逆向き垂直斜めの道路にも読める向きで注記を描く(int x0, int y0, int x1, int y1)
    {
        using var bitmap = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        Assert.True(Render(canvas, [[new(x0, y0), new(x1, y1)]], new LabelCollisionGrid()));
        Assert.Contains(bitmap.Pixels, c => c.Red < 150);
    }

    [Fact]
    public void 道路の頂点順を反転しても文字を逆さまにしない()
    {
        using var first = new SKBitmap(300, 300);
        using var second = new SKBitmap(300, 300);
        using var a = new SKCanvas(first);
        using var b = new SKCanvas(second);
        a.Clear(SKColors.White);
        b.Clear(SKColors.White);
        Assert.True(Render(a, [[new(20, 200), new(280, 80)]], new LabelCollisionGrid()));
        Assert.True(Render(b, [[new(280, 80), new(20, 200)]], new LabelCollisionGrid()));
        Assert.Equal(first.Pixels, second.Pixels);
    }

    [Fact]
    public void 緩いカーブは追従し文字全体が急旋回する短い折返しは描かない()
    {
        using var bitmap = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bitmap);
        Assert.True(Render(canvas, [[new(20, 160), new(80, 140), new(140, 130), new(200, 135), new(280, 155)]], new LabelCollisionGrid()));
        Assert.False(Render(canvas, [[new(100, 100), new(130, 100), new(130, 130), new(100, 130)]], new LabelCollisionGrid()));
    }

    [Fact]
    public void 過拡大で中点が画面外でも可視区間を使う()
    {
        using var bitmap = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bitmap);
        Assert.True(Render(canvas, [[new(-2000, 150), new(500, 150)]], new LabelCollisionGrid()));
        Assert.False(Render(canvas, [[new(-2000, -100), new(500, -100)]], new LabelCollisionGrid()));
    }

    [Fact]
    public void 同じ道路名の近接重複と他の注記との衝突を抑止する()
    {
        using var bitmap = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bitmap);
        var grid = new LabelCollisionGrid();
        Assert.True(Render(canvas, [[new(20, 120), new(280, 120)]], grid));
        Assert.False(Render(canvas, [[new(20, 150), new(280, 150)]], grid));
        var occupied = new LabelCollisionGrid();
        Assert.True(occupied.TryPlace(new SKRect(0, 0, 300, 300), new SKRect(0, 0, 300, 300)));
        Assert.False(Render(canvas, [[new(20, 120), new(280, 120)]], occupied));
    }

    private static bool Render(SKCanvas canvas, IReadOnlyList<IReadOnlyList<VectorPoint>> parts, LabelCollisionGrid grid)
    {
        using SKTypeface face = SKTypeface.FromFamilyName("Yu Gothic UI");
        using var font = new SKFont(face, 12f);
        using var fill = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var halo = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.StrokeAndFill, StrokeWidth = 2.5f };
        var feature = new VectorTileFeature { GeometryType = VectorGeometryType.LineString, GeometryParts = parts };
        return PathLabelRenderer.Render(canvas, feature, SKMatrix.Identity, "北２丁目通り", font, fill, halo, grid, new SKRect(0, 0, 300, 300), "road:北２丁目通り");
    }
}

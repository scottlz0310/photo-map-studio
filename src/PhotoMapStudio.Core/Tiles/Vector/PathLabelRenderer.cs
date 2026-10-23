using SkiaSharp;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>画面内の折れ線へ、読み進められる向きと曲率で注記を配置する。</summary>
internal static class PathLabelRenderer
{
    internal static bool Render(
        SKCanvas canvas, VectorTileFeature feature, SKMatrix matrix, string text, SKFont font,
        SKPaint fill, SKPaint halo, LabelCollisionGrid grid, SKRect viewport, string key)
    {
        float width = font.MeasureText(text);
        float margin = font.Size + halo.StrokeWidth;
        var clip = viewport;
        clip.Inflate(-margin, -margin);
        if (clip.Width <= 0 || clip.Height <= 0)
        {
            return false;
        }

        foreach (List<SKPoint> run in VisibleRuns(feature, matrix, clip).OrderByDescending(Length))
        {
            float length = Length(run);
            if (length < width + 12f)
            {
                continue;
            }

            // 中央が急カーブや他の注記に重なる場合、同じ道路の別区間も試す。
            foreach (float fraction in new[] { 0.5f, 0.25f, 0.75f })
            {
                float start = Math.Clamp((length * fraction) - (width / 2f), 0f, length - width);
                using SKPath path = Path(run);
                SKPoint first, last;
                using (var measure = new SKPathMeasure(path, false))
                {
                    measure.GetPositionAndTangent(start, out first, out _);
                    measure.GetPositionAndTangent(start + width, out last, out _);
                }
                List<SKPoint> oriented = run;
                if (last.X < first.X || (Math.Abs(last.X - first.X) < 1f && last.Y > first.Y))
                {
                    oriented = run.AsEnumerable().Reverse().ToList();
                    start = length - start - width;
                }

                using SKPath orientedPath = Path(oriented);
                using var orientedMeasure = new SKPathMeasure(orientedPath, false);
                if (!TryBounds(orientedMeasure, start, width, font.Size, halo.StrokeWidth, out List<SKRect> bounds)
                    || !grid.TryPlace(bounds, viewport, key, repeatDistance: 260f))
                {
                    continue;
                }

                // 文字自体を歪ませず、各グリフを道路の接線方向へ回転する。
                // 配置区間を独立したパスにし、描画APIの文字原点と距離指定を重ねない。
                using SKPath labelPath = orientedMeasure.GetSegment(start, start + width, true);
                var offset = new SKPoint(0, font.Size * 0.35f);
                canvas.DrawTextOnPath(text, labelPath, offset, false, SKTextAlign.Left, font, halo);
                canvas.DrawTextOnPath(text, labelPath, offset, false, SKTextAlign.Left, font, fill);
                return true;
            }
        }

        return false;
    }

    private static bool TryBounds(SKPathMeasure measure, float start, float width, float size, float halo, out List<SKRect> bounds)
    {
        bounds = [];
        SKPoint? previousTangent = null;
        SKPoint? firstTangent = null;
        // 曲線全体の大きな矩形ではなく、短い区間ごとの矩形で衝突判定する。
        int steps = Math.Max(1, (int)Math.Ceiling(width / 4f));
        for (int i = 0; i <= steps; i++)
        {
            measure.GetPositionAndTangent(start + (width * i / steps), out SKPoint point, out SKPoint tangent);
            firstTangent ??= tangent;
            if ((previousTangent is SKPoint previous && Dot(previous, tangent) < 0.85f)
                || Dot(firstTangent.Value, tangent) < 0.5f)
            {
                return false;
            }

            previousTangent = tangent;
            float radius = (size * 0.65f) + halo;
            bounds.Add(new SKRect(point.X - radius, point.Y - radius, point.X + radius, point.Y + radius));
        }

        return true;
    }

    private static float Dot(SKPoint a, SKPoint b) => (a.X * b.X) + (a.Y * b.Y);

    private static SKPath Path(List<SKPoint> points)
    {
        using var builder = new SKPathBuilder();
        builder.MoveTo(points[0]);
        for (int i = 1; i < points.Count; i++)
        {
            builder.LineTo(points[i]);
        }

        return builder.Detach();
    }

    private static float Length(List<SKPoint> points)
    {
        float length = 0;
        for (int i = 1; i < points.Count; i++)
        {
            length += SKPoint.Distance(points[i - 1], points[i]);
        }

        return length;
    }

    private static List<List<SKPoint>> VisibleRuns(VectorTileFeature feature, SKMatrix matrix, SKRect clip)
    {
        var runs = new List<List<SKPoint>>();
        foreach (IReadOnlyList<VectorPoint> part in feature.GeometryParts)
        {
            List<SKPoint>? run = null;
            for (int i = 1; i < part.Count; i++)
            {
                SKPoint a = matrix.MapPoint(new SKPoint(part[i - 1].X, part[i - 1].Y));
                SKPoint b = matrix.MapPoint(new SKPoint(part[i].X, part[i].Y));
                if (!Clip(a, b, clip, out SKPoint from, out SKPoint to))
                {
                    run = null;
                    continue;
                }

                if (run is null || SKPoint.Distance(run[^1], from) > 0.01f)
                {
                    run = [from];
                    runs.Add(run);
                }

                run.Add(to);
                if (SKPoint.Distance(to, b) > 0.01f)
                {
                    run = null;
                }
            }
        }

        return runs;
    }

    private static bool Clip(SKPoint a, SKPoint b, SKRect clip, out SKPoint from, out SKPoint to)
    {
        float min = 0, max = 1;
        float dx = b.X - a.X, dy = b.Y - a.Y;
        bool visible = Edge(-dx, a.X - clip.Left, ref min, ref max)
            && Edge(dx, clip.Right - a.X, ref min, ref max)
            && Edge(-dy, a.Y - clip.Top, ref min, ref max)
            && Edge(dy, clip.Bottom - a.Y, ref min, ref max) && max > min;
        from = new SKPoint(a.X + (dx * min), a.Y + (dy * min));
        to = new SKPoint(a.X + (dx * max), a.Y + (dy * max));
        return visible;

        static bool Edge(float p, float q, ref float min, ref float max)
        {
            if (Math.Abs(p) < 1e-9f)
            {
                return q >= 0;
            }

            float t = q / p;
            if (p < 0) { min = Math.Max(min, t); }
            else { max = Math.Min(max, t); }
            return min <= max;
        }
    }
}

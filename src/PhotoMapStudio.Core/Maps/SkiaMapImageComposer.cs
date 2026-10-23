using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Core.Maps;

/// <summary>
/// SkiaSharp による地図画像の合成。
/// </summary>
public sealed class SkiaMapImageComposer : IMapImageComposer
{
    private const int AttributionPadding = 4;
    private const float MinimumFontSize = 6f;

    // タイル境界のにじみを避けるため補間しない（仕様書 §6.3）
    private static readonly SKSamplingOptions NearestNeighbor = new(SKFilterMode.Nearest, SKMipmapMode.None);

    private readonly ITileProvider tileProvider;
    private readonly IVectorMapRenderer? vectorMapRenderer;

    /// <summary>
    /// コンポーザーを初期化する。
    /// </summary>
    /// <param name="tileProvider">タイルの供給元。</param>
    /// <param name="vectorMapRenderer">ベクター地図レンダラー。未指定の場合はベクタータイル描画時に例外を送出する。</param>
    public SkiaMapImageComposer(ITileProvider tileProvider, IVectorMapRenderer? vectorMapRenderer = null)
    {
        ArgumentNullException.ThrowIfNull(tileProvider);
        this.tileProvider = tileProvider;
        this.vectorMapRenderer = vectorMapRenderer;
    }

    /// <inheritdoc />
    public async Task<MapCompositionResult> ComposeAsync(
        MapCompositionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Height);

        using var surface = SKSurface.Create(
            new SKImageInfo(request.Width, request.Height, SKColorType.Rgba8888, SKAlphaType.Premul));

        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (request.TileSource.IsVector)
        {
            if (this.vectorMapRenderer is null)
            {
                throw new InvalidOperationException("ベクタータイルの描画エンジンが設定されていません。");
            }

            await this.vectorMapRenderer.RenderToCanvasAsync(
                canvas,
                request.Center,
                request.Zoom,
                request.Width,
                request.Height,
                request.TileSession,
                request.TileSource,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            TilePoint center = WebMercator.ToTilePoint(request.Center, request.Zoom);
            TileRange range = TileRange.Compute(center, request.Width, request.Height);
            await this.DrawTilesAsync(canvas, request, range, cancellationToken).ConfigureAwait(false);
        }

        DrawPin(canvas, request);
        DrawAttribution(canvas, request);

        using SKImage image = surface.Snapshot();
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new MapCompositionException("PNG へのエンコードに失敗しました。");
        return new MapCompositionResult(encoded.ToArray(), request.TileSource, UsedFallback: false);
    }

    private async Task DrawTilesAsync(
        SKCanvas canvas,
        MapCompositionRequest request,
        TileRange range,
        CancellationToken cancellationToken)
    {
        // タイルは重ならないため、アルファ合成せず上書きで配置する（仕様書 §6.2）
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };

        int tileCount = 1 << request.Zoom;

        for (int tileY = range.MinY; tileY <= range.MaxY; tileY++)
        {
            // メルカトルの南北限より外にタイルは存在しないため、その帯は描かない
            if (tileY < 0 || tileY >= tileCount)
            {
                continue;
            }

            for (int tileX = range.MinX; tileX <= range.MaxX; tileX++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 経度方向は世界が周回するため、要求とキャッシュキーには正規化した番号を使う
                int sourceX = ((tileX % tileCount) + tileCount) % tileCount;

                byte[] content = await this.tileProvider
                    .GetTileAsync(request.TileSource, request.Zoom, sourceX, tileY, cancellationToken, request.TileSession)
                    .ConfigureAwait(false);

                using SKImage tile = SKImage.FromEncodedData(content)
                    ?? throw new MapCompositionException(
                        $"タイル画像を復号できませんでした: {request.TileSource.BuildTileUri(request.Zoom, sourceX, tileY)}");

                // キャンバスを作らず、切り出し位置ぶんずらして直接配置する（結果は §6.2・§6.3 と同じ）
                float left = ((tileX - range.MinX) * WebMercator.TileSize) - range.CropLeft;
                float top = ((tileY - range.MinY) * WebMercator.TileSize) - range.CropTop;
                canvas.DrawImage(tile, left, top, NearestNeighbor, paint);
            }
        }
    }

    private static void DrawPin(SKCanvas canvas, MapCompositionRequest request)
    {
        float centerX = request.Width / 2f;
        float centerY = request.Height / 2f;

        switch (request.PinKind)
        {
            case PinKind.TargetCircle:
                DrawTargetCircle(canvas, centerX, centerY);
                return;

            case PinKind.Crosshair:
                DrawCrosshair(canvas, centerX, centerY);
                return;

            default:
                break;
        }

        using SKImage? pin = LoadPin(request.PinImagePath);
        if (pin is null)
        {
            DrawTargetCircle(canvas, centerX, centerY);
            return;
        }

        // 同梱ピン（GreenPin, RedPin, BluePin）は先端が撮影地点を指すため常に下端中央。
        // カスタム画像で明示的に Center が指定された場合のみ図心中央アンカーを適用する。
        bool useCenter = request.PinKind == PinKind.CustomImage && request.PinAnchor == PinAnchor.Center;
        float x = MathF.Round(centerX - (pin.Width / 2f));
        float y = useCenter
            ? MathF.Round(centerY - (pin.Height / 2f))
            : MathF.Round(centerY - pin.Height);

        canvas.DrawImage(pin, x, y, NearestNeighbor);
    }

    private static SKImage? LoadPin(string? pinImagePath)
    {
        if (string.IsNullOrWhiteSpace(pinImagePath) || !File.Exists(pinImagePath))
        {
            return null;
        }

        try
        {
            return SKImage.FromEncodedData(pinImagePath);
        }
        catch (IOException)
        {
            // 読み込みに失敗した場合は代替描画へ切り替える（仕様書 §6.5）
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void DrawTargetCircle(SKCanvas canvas, float centerX, float centerY)
    {
        // 同心円 3 枚。この場合のアンカーは円の中心（仕様書 §6.5）
        DrawCircle(canvas, centerX, centerY, 12f, new SKColor(255, 255, 255), new SKColor(100, 100, 100));
        DrawCircle(canvas, centerX, centerY, 10f, new SKColor(235, 59, 36), new SKColor(180, 20, 20));
        DrawCircle(canvas, centerX, centerY, 3f, new SKColor(255, 255, 255), outline: null);
    }

    private static void DrawCrosshair(SKCanvas canvas, float centerX, float centerY)
    {
        // 測量標・クロスヘア（十字線マーク）。白フチ付き＋中央ターゲットリング
        const float armLength = 14f;
        const float centerGap = 3f;

        // 1. 白フチ（下地）
        using var haloPaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4.5f,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
        };

        canvas.DrawLine(centerX - armLength, centerY, centerX - centerGap, centerY, haloPaint);
        canvas.DrawLine(centerX + centerGap, centerY, centerX + armLength, centerY, haloPaint);
        canvas.DrawLine(centerX, centerY - armLength, centerX, centerY - centerGap, haloPaint);
        canvas.DrawLine(centerX, centerY + centerGap, centerX, centerY + armLength, haloPaint);

        using var ringHaloPaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4f,
            IsAntialias = true,
        };
        canvas.DrawCircle(centerX, centerY, 8f, ringHaloPaint);

        // 2. 赤色実線
        using var crossPaint = new SKPaint
        {
            Color = new SKColor(235, 59, 36),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.0f,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
        };
        canvas.DrawLine(centerX - armLength, centerY, centerX - centerGap, centerY, crossPaint);
        canvas.DrawLine(centerX + centerGap, centerY, centerX + armLength, centerY, crossPaint);
        canvas.DrawLine(centerX, centerY - armLength, centerX, centerY - centerGap, crossPaint);
        canvas.DrawLine(centerX, centerY + centerGap, centerX, centerY + armLength, crossPaint);

        using var ringPaint = new SKPaint
        {
            Color = new SKColor(235, 59, 36),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true,
        };
        canvas.DrawCircle(centerX, centerY, 8f, ringPaint);

        using var centerDotPaint = new SKPaint
        {
            Color = new SKColor(235, 59, 36),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        canvas.DrawCircle(centerX, centerY, 1.5f, centerDotPaint);
    }

    private static void DrawCircle(SKCanvas canvas, float x, float y, float radius, SKColor fill, SKColor? outline)
    {
        using var fillPaint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(x, y, radius, fillPaint);

        if (outline is not SKColor outlineColor)
        {
            return;
        }

        using var outlinePaint = new SKPaint
        {
            Color = outlineColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = true,
        };
        canvas.DrawCircle(x, y, radius, outlinePaint);
    }

    private static void DrawAttribution(SKCanvas canvas, MapCompositionRequest request)
    {
        string text = request.TileSource.Attribution;

        using SKFont font = CreateAttributionFont(text, request.Width);
        float maxTextWidth = request.Width - (AttributionPadding * 2);

        // 下限フォントでも 1 行に収まらない場合は折り返して全文を描画する
        IReadOnlyList<string> lines = AttributionLayout.Wrap(font, text, maxTextWidth);
        if (lines.Count == 0)
        {
            return;
        }

        SKFontMetrics metrics = font.Metrics;
        float lineHeight = metrics.Descent - metrics.Ascent;
        float boxHeight = (lineHeight * lines.Count) + AttributionPadding;

        // 折り返しても画像内に収まらない極小サイズでは焼き込みを省略する（プレビュー側の表示で担保する）
        if (boxHeight > request.Height)
        {
            return;
        }

        float boxWidth = Math.Min(lines.Max(line => font.MeasureText(line)) + (AttributionPadding * 2), request.Width);
        float boxTop = request.Height - boxHeight;

        // 地図の内容に紛れて読めなくならないよう、半透明の下地を敷く（仕様書 §6.6）
        using var backgroundPaint = new SKPaint { Color = new SKColor(0, 0, 0, 128), Style = SKPaintStyle.Fill };
        canvas.DrawRect(request.Width - boxWidth, boxTop, boxWidth, boxHeight, backgroundPaint);

        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        float baseline = boxTop + (AttributionPadding / 2f) - metrics.Ascent;
        foreach (string line in lines)
        {
            canvas.DrawText(line, request.Width - AttributionPadding, baseline, SKTextAlign.Right, font, textPaint);
            baseline += lineHeight;
        }
    }

    // 出典表示は利用者が入力する（日本語と英数字が混在し得る）ため、先頭の 1 文字ではなく日本語に対応した書体を使う。
    // 書体は読み取りだけで共有するため、プロセスの終了まで保持する
    private static readonly Lazy<SKTypeface> AttributionTypeface = new(Tiles.Vector.VectorMapFontHelper.CreateJapaneseTypeface);

    internal static SKFont CreateAttributionFont(string text, int width)
    {
        var font = new SKFont(AttributionTypeface.Value, Math.Clamp(width * 0.018f, 8f, 14f));

        // 1 行に収まるまでは縮小し、下限に達したら折り返しへ委ねる
        while (font.MeasureText(text) > width - (AttributionPadding * 2) && font.Size > MinimumFontSize)
        {
            font.Size -= 1f;
        }

        return font;
    }
}

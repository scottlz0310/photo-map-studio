# PhotoMapStudio.Rendering

ベクタータイル（MVT）を [SkiaSharp](https://github.com/mono/SkiaSharp) で **1 枚の画像として描く** .NET ライブラリです。WinUI・Windows App SDK には依存しません。

[PhotoMapStudio](https://github.com/scottlz0310/photo-map-studio)（撮影地点の地図画像を生成するアプリ）の描画層を切り出したもので、他のアプリ（PhotoGeoExplorer など）からも使えます。タイルの取得・デコードは [`PhotoMapStudio.Tiles`](https://www.nuget.org/packages/PhotoMapStudio.Tiles/) が担います。

- 対象: .NET 10
- 依存: `PhotoMapStudio.Tiles`、`SkiaSharp`（4.156.0）

## できること

- **表示範囲全体を 1 枚の画像として描く**（256px のタイルに分割しないので、注記がタイルの境界で切れません）
- **連続ズーム**: `Zoom` は `double`。取得するタイルのズームは整数、表示倍率は連続値です
- **物理ピクセルの画像寸法と画素密度**: 画像の寸法とズームは物理ピクセルで指定し、表示の画素密度（DPI）は `PixelRatio` で伝えます。文字・線幅などが、画素密度によらず画面上（論理ピクセル）で同じ大きさになります
- **キャンセル**: 取得と描画の途中（スタイルの中を含む）で確認し、キャンセルされたら `OperationCanceledException` で終わります（画像を返しません）
- **欠落タイルの判別**: オフライン・配信範囲外のタイルを、結果の集計（`UnavailableTileCount`・`AbsentTileCount`・`IsComplete`）で判別できます。失敗を黙って隠しません
- **画像の所有権**: 描画のたびに新しい画像を返します。`VectorViewResult` が所有し、`TakeImage()` で引き取れます
- **事前デコード**: 描画せずに、中心を囲む 2×2 枚のタイルを取得・デコードしてデコード済みキャッシュへ入れます（`IVectorMapPrefetcher.PrefetchAsync`。次の描画でデコードのコストを払わずに済みます）
- **高速化**: 表示範囲外の地物を描かない最適化（常に有効）、帯の並列描画・注記の並列（`BandCount`・`ParallelLabels`。既定は無効）、デコード済みタイルのキャッシュ（`DecodedTileCache`）
- **スタイル**: OpenMapTiles スキーマ（`OpenMapTilesStyle`）と国土地理院の最適化ベクトル（`GsiVectorStyle`）。`IVectorMapStyle` で差し替えられます

## 使い方

```csharp
using PhotoMapStudio.Rendering;
using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;
using SkiaSharp;

using var http = new HttpClient();

// 配信元へは、自分のアプリの名称と連絡先で表明する（PhotoMapStudio.Tiles の README を参照）
http.DefaultRequestHeaders.UserAgent.ParseAdd("MyApp/1.0 (+https://example.com/myapp)");

using var cache = new FileSystemTileCache(Path.Combine(Path.GetTempPath(), "MyApp", "tiles"));
using var openFreeMap = new OpenFreeMapTileSource(http, urlTemplateStore: cache);
IVectorTileSource source = new CachedVectorTileSource(openFreeMap, cache);

using var style = new OpenMapTilesStyle();
var renderer = new VectorMapRenderer(source, style, new DecodedTileCache(128L * 1024 * 1024));

// 中心の緯度経度・連続ズーム・物理ピクセルの寸法で描く
var request = new VectorViewRequest(CenterLatitude: 35.681, CenterLongitude: 139.767, Zoom: 15.4, PixelWidth: 1366, PixelHeight: 1220)
{
    AllowIncompleteMaps = true,   // 一時的な通信失敗のタイルを欠落として許し、結果で判別する
    BandCount = Math.Clamp(Environment.ProcessorCount, 1, 8),
    ParallelLabels = true,
    PixelRatio = 1.0,             // 表示の画素密度（150% DPI なら 1.5）。Zoom と寸法は物理ピクセルのまま渡す
};

using VectorViewResult result = await renderer.RenderViewAsync(request, CancellationToken.None);
if (!result.IsComplete)
{
    Console.WriteLine($"{result.MissingTileCount} 枚のタイルが欠けています");
}

SKImage image = result.Image;           // この結果が所有する。引き取るときは result.TakeImage()
```

- ズームは、256px のタイルを物理ピクセルで数えた連続値です。画素密度の高い表示（例: 200% DPI）では、表示の物理ピクセルに合わせた寸法とズームを渡し、画素密度を `PixelRatio`（200% なら 2.0）で伝えてください。伝えないと、文字・線幅などが物理ピクセルで固定のため、画素密度が高いほど画面上では小さくなります。`PixelRatio` は 1.0 が既定で、そのときの出力は従来と同じです
- `BandCount` の最良値はコア数に近く、コア数より多いと遅くなり得ます。並列にすると、線や注記の縁の半透明画素が、並列にしない場合と最大 20 程度（/255）ずれ得ます（見た目は同じ）
- 描画の途中でキャンセルされたとき、画像は返りません。`OperationCanceledException` になります

### 事前デコード

```csharp
// 同じレンダラー（DecodedTileCache を指定したもの）で、操作が止まったあとの地点を先に読んでおく
IVectorMapPrefetcher prefetcher = renderer;
await prefetcher.PrefetchAsync(new VectorPrefetchRequest(CenterLatitude: 35.681, CenterLongitude: 139.767, Zoom: 15.4), CancellationToken.None);
```

- 取得ズーム（連続ズームの切り捨て。取得元の最大ズームで頭打ち）で、中心を囲む 2×2 枚を取得・デコードします。範囲外（南北の外）は飛ばし、経度は折り返します。配信範囲外のタイルは入れるものがないので飛ばします
- **`DecodedTileCache` を指定していないレンダラーでは、効果がないため `InvalidOperationException` になります**
- 取得・デコードの失敗は握りつぶさず、`TileFetchException` で返します（最適化なので無視するかは呼び出し側が決めます）
- キャンセルされたら `OperationCanceledException` で終わります。キャッシュへは、デコードが済んだタイルだけを 1 枚ずつ入れます
- 描画と同時に呼んでも壊れません。同じタイルを同時に取得・デコードする場合は、二重にデコードすることがあります（結果は同じ）
- いつ・どこへ事前デコードするかは、呼び出し側が決めます。`IVectorMapRenderer` は変えていないので、利用側の既存の実装は壊れません

## フォント

日本語の注記には、システムの書体（Yu Gothic UI・Meiryo・Hiragino Sans・Noto Sans CJK JP・MS Gothic の順。無ければ日本語の文字を描ける書体）を使います。書体は同梱しません。選択は `VectorMapFontHelper` が行い、同じ書体を出典などの描画にも使えます。

## SkiaSharp のバージョン

このパッケージは SkiaSharp 4.156.0 に依存します。利用側のアプリは、Skia 系のパッケージ（`SkiaSharp.Views.WinUI`・`SkiaSharp.HarfBuzz`・`SkiaSharp.NativeAssets.*` など）を**同じ版で明示**してください。別のライブラリが古い SkiaSharp を引くと、版の不一致で実行時に失敗することがあります。

## ライセンスと出典

MIT License。地図データの出典・利用条件は、各配信元（OpenFreeMap / OpenStreetMap、国土地理院など）の規約に従ってください。表示するときは、出典の表示が必要です。

## 公開手順

[docs/development/releasing-rendering.md](https://github.com/scottlz0310/photo-map-studio/blob/main/docs/development/releasing-rendering.md)

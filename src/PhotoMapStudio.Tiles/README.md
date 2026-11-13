# PhotoMapStudio.Tiles

地図タイル（ラスター、ベクター MVT、PMTiles）の**取得・キャッシュ・レート制御・デコード**を行う .NET ライブラリです。描画（SkiaSharp）や UI（WinUI）には依存しません。

[PhotoMapStudio](https://github.com/scottlz0310/photo-map-studio)（撮影地点の地図画像を生成するアプリ）のタイル層を切り出したもので、他のアプリからも使えます。

- 対象: .NET 10
- 依存: `Microsoft.Extensions.Http` のみ

## できること

- **取得元**: OpenFreeMap（OpenMapTiles スキーマ）、国土地理院の最適化ベクトルタイル（PMTiles の HTTP Range 取得）、利用者指定の PMTiles（ファイル・URL）、XYZ の URL テンプレート
- **キャッシュ**: ディスクのタイルキャッシュ（保持期間・容量の上限）。`ITileCache` で差し替えられます
- **レート制御**: 配信元ごとの同時接続数と最小の要求間隔を守ります。呼び出しのキャンセルにも対応します
- **デコード**: ベクタータイル（MVT / PBF）を、レイヤー・地物・属性・ジオメトリの `VectorTile` に変換します

## 使い方

OpenFreeMap から、撮影地点を含むタイルを取得し、道路名のレイヤーを読む例です。

```csharp
using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

using var http = new HttpClient();

// 配信元へは、自分のアプリの名称と連絡先で表明する（設定しないと、PhotoMapStudio の名称になる）
http.DefaultRequestHeaders.UserAgent.ParseAdd("MyApp/1.0 (+https://example.com/myapp)");

using var cache = new FileSystemTileCache(Path.Combine(Path.GetTempPath(), "MyApp", "tiles"));
using var openFreeMap = new OpenFreeMapTileSource(http, urlTemplateStore: cache);
IVectorTileSource source = new CachedVectorTileSource(openFreeMap, cache);

byte[]? bytes = await source.GetTileAsync(z: 14, x: 14675, y: 5991, CancellationToken.None);
if (bytes is not null && VectorTileDecoder.TryDecode(bytes, out VectorTile tile))
{
    VectorTileLayer? roads = tile.GetLayer("transportation_name");
    foreach (VectorTileFeature road in roads?.Features ?? [])
    {
        string? name = road.GetString("name");
    }
}
```

- `GetTileAsync` は、タイルが存在しない（404）ときは `null` を返します。通信の失敗などは `TileFetchException` になることがあります
- 配信元のレート制限やキャッシュ付きの取得元をまとめて作るときは、`VectorTileSourceFactory` が使えます

## User-Agent

OpenFreeMap などの配信元は、問い合わせ先の分かる User-Agent を求めます。`HttpClient` の `DefaultRequestHeaders.UserAgent` を設定すれば、それが使われます。設定がなければ、`PhotoMapStudio/<版> (+https://github.com/scottlz0310/photo-map-studio)` を表明します。`AddHttpClient` を使うときは、その構成で設定します。

## ライセンスと出典

MIT License。地図データの出典・利用条件は、各配信元（OpenFreeMap / OpenStreetMap、国土地理院など）の規約に従ってください。表示するときは、出典の表示が必要です。

## 公開手順

[docs/development/releasing-tiles.md](https://github.com/scottlz0310/photo-map-studio/blob/main/docs/development/releasing-tiles.md)

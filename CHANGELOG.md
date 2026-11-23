# Changelog

このプロジェクトのすべての注目すべき変更をこのファイルに記録します。

形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に基づき、[Semantic Versioning](https://semver.org/lang/ja/) に従います。

## [Unreleased]

### Added

- `OpenMapTilesStyle` に、森の樹木の模様・バス停と駅の記号・ロープウェイの線・小道の色分け・番地を追加した（#147。#146 に続く、OSM ラスタとのにぎやかさの差を埋める第 2 弾）。
  - **樹木の模様**: ズーム 14 以上で、植生の森（`landcover` の `wood`）の中に、樹冠と幹を 24 論理ピクセル間隔の格子（2 本を対角にずらす）で描く。森の面でクリップし、格子の点を 1 回の `DrawPoints` で描く（`OpenMapTilesTreeTexture`）。1 つの地物の樹木が 20,000 本を超える範囲では描かない。寸法は論理ピクセルで、`PixelRatio` に追従する。**描画時間を測って採用した**: 実アプリ相当の画面（1600×1000 物理 px・画素密度 1.5）で、森が大半を占める表示は 5.9 → 10.3 ms（旭岳 z15）、4.3 → 7.5 ms（旭岳 z16）、7.3 → 11.2 ms（層雲峡 z15）、7.2 → 13.0 ms（層雲峡 z16）と約 4〜6 ms 増える（単発 50 ms の基準に対して十分小さい）。森の少ない表示（美瑛 z16・東京 z17）は誤差の範囲。繰り返しパターンのシェーダーで面全体を塗る方式は、`SKPicture` のシェーダーで約 +11 ms、画像（ミップマップ）のシェーダーで約 +17〜23 ms と大きかったため採らなかった。
  - **バス停・駅の記号**: バス停（`class=bus`）は角丸の四角に白いバス、鉄道駅（`class=railway`）は角丸の四角に白い輪で描く（円とは違う形）。バス停は数が多く、他の施設を押しのけるため、ズーム 16 以上でだけ描く（駅は従来どおりズーム 15 以上）。
  - **ロープウェイ**: `transportation` の `class=aerialway` を、ズーム 12 以上で、灰色の線と等間隔の点で描く。名前（`transportation_name`）は、ズーム 14 以上で線に沿って描く。これまでは描かれなかった。
  - **小道の色分け**: 自転車道（`subclass=cycleway`）は青、歩道・歩行者専用道・階段・通路・プラットフォーム（`footway`・`pedestrian`・`steps`・`corridor`・`platform`）は赤みのある茶色、それ以外の小道（`path`・`track` など）は従来の茶色にする。破線の形は変えない。
  - **番地**: `housenumber` を、ズーム 17 以上で、小さな灰色の文字で描く。8 文字以下で空白を含まない値だけを描く（階数や施設名が混じった長い値は読めないため）。地名・施設名・道路名より後に置き、先に置かれた注記と重なる番地は描かない。タイルごとに通常の注記の直後に置くと、先に走査したタイルの番地が、隣のタイルの駅・施設名を境界付近で押しのけるため、`IVectorMapStyle` に**既定の実装を持つ**メソッド `RenderLowPriorityLabels`（優先度の最も低い注記。全タイルの `RenderLabels` の後に、全タイルへ順に呼ばれる）を追加し、`VectorMapRenderer` の逐次・帯の並列・注記の並列の全経路から呼ぶ。既存のスタイルの実装は、変更なしで動く（既定の実装は何も描かない）。
- `OpenMapTilesStyle` に、山頂（`mountain_peak`）の描画を追加した（#146。PhotoGeoExplorer #352 の OSM ラスタとの比較で、山地の表現が乏しかったことへの対応）。三角の記号・山名・標高（`ele`。無い場合は山名だけ）を描き、火山（`class=volcano`）は赤、それ以外は茶色にする。描くのは `class` が `peak`・`volcano` の地物だけで、峠・鞍部（`saddle`）や崖（`cliff`）、名前のない地物は描かない。ズーム 11 未満は描かず、ズーム 11〜13 は `rank`（タイル内の標高順）の上位だけ（11: 2 位、12: 3 位、13: 6 位）、ズーム 14 以上は全て描く。注記の衝突判定を通り、先に置いた注記と重ならない。寸法は論理ピクセルなので、`PixelRatio` に追従する。
- `OpenMapTilesStyle` の POI（`poi`）を、`class` から 10 分類（交通・駐車場・医療・飲食・買い物・宿泊・教育・文化・公園運動・その他）に分け、分類ごとの色で描くようにした（これまでは病院・学校・駅以外は全て同じオレンジの点）。駐車場は青い角丸の四角に白い「P」、医療は白い十字の入った円、その他は円。注記の文字色は、記号の色を暗くした色にする。名前のない駐車場は、ズーム 16 以上で記号だけを描く（名前のない他の施設は、何かが分からないため従来どおり描かない）。
- 面の塗り色に、`landcover` の `farmland`（農地）・`rock`（岩・ガレ場・露岩）、`landuse` の `retail`・`commercial`（商業系）・`kindergarten`・`education`・`library`（施設用地）・`playground`・`stadium` を追加した。色は `OpenMapTilesLandPalette` にまとめた。

### Changed

- `OpenMapTilesStyle` は、林道（`class=track`）を細い茶色の破線で描き、林道の名前を低いズームから描き、川と沢で線幅を変えるようにした（#150。PhotoGeoExplorer #352 の OSM ラスタとの比較。山間部の林道付近の実データ〔z14 タイル `14681/5991`〕で、データにあるのに描いていない・見た目が違う地物を調べた）。
  - **林道の線**: これまでは `service` と同じ白い太い道（幅 3.5・casing つき）だったが、道路から外し、小道（`path`）と同じ枠組みの破線にした。小道より少し太く（1.6）長い（線 6・間 3 の論理ピクセル）濃い茶色で、舗装路の白い道と区別でき、道路網より目立たない。`service`・`minor` などは従来の実線のまま。
  - **林道の名前**: これまではズーム 17 以上でしか描かなかったが、ズーム 14 以上から描く（名前のある林道は少なく混雑しにくい）。優先順位は道路名より低く、先に置かれた注記と重なる所には描かない。`path`・`footway` など他の小道は従来どおりズーム 17 以上。
  - **川と沢の線幅**: `waterway` の線幅を `class` で変える（川 4・運河 3・沢・溝・排水路など 1.8。論理ピクセル）。これまでは全て 2.5。`ZoomScale` と同じ伸び方で、`PixelRatio` に追従する。`class` を持たない水路は沢と同じ細い幅（OpenMapTiles の `waterway` は常に `class` を持つ）。
  - **描画時間は悪化していない**: 実アプリ相当の画面（1600×1000 物理 px・画素密度 1.5。林道付近の実タイル・各 25 回の中央値・変更前後を交互に 3 回）で、前後の範囲が重なる（誤差の範囲）。逐次は、論理ズーム 14.5 が 21.6〜23.0 ms（前）／20.8〜23.1 ms（後）、15.5 が 10.8〜14.0／11.0〜14.1 ms、16 が 9.6〜9.9／9.1〜12.1 ms。帯 8＋注記並列は、14.5 が 8.1〜8.8／7.3〜9.6 ms、15.5 が 5.3〜6.6／5.6〜7.1 ms、16 が 5.0〜7.4／5.3〜7.3 ms。
- `OpenMapTilesStyle` は、`park` レイヤーのうち、用途を表す `recreation_ground`（運動場など）と `historic`（史跡）だけを塗り、国立公園・保護区・自然保護区などの指定区域は塗りつぶさないようにした。指定区域は範囲を示すだけで、塗ると範囲内の植生・岩（`landcover`）が全面に隠れ、山地（例: 旭岳・層雲峡）が淡い一色になっていた。指定区域の `class` は `national_park`・`nature_reserve`・`conservation`・`wilderness_preserve`・`wildlife_refuge`・`sustainable`・`marine` のほか、保護区の名称の自由記述の値まで広がるため、塗る区分を列挙している。
- 森（`wood`）を `#c8dfb8` から `#b5d6a2`、草地（`grass`）を `#d8e8c8` から `#d3e8c0` へ、彩度を上げた。`pitch`・`track` は `#cfe6c4` から `#bfe4cf` にし、`stadium` も同じ色にした。地名の注記のフォントを、地物ごとに作らず、種別ごとに 1 回だけ作る（出力は変わらない）。

- `VectorViewRequest` に `PixelRatio`（表示の画素密度。`MinPixelRatio`（1/16）以上 `MaxPixelRatio`（16）以下で、既定は 1.0。範囲外は `ArgumentOutOfRangeException`）を追加した（#144。PhotoGeoExplorer #359）。これまでスタイルの大きさ（文字 11 px・縁取り・点・盾・線幅。道路幅は `幅 × ZoomScale(整数ズーム)`）が物理ピクセルで決まるため、画素密度の高い表示では画面上（論理ピクセル）で小さくなっていた（PhotoGeoExplorer の実測で、150% で OSM の約 0.7 倍、200% で約 0.55 倍。200% は整数ズームの境界で道路幅が段差状に太くなる）。`PixelRatio` を指定すると、スタイルの判定に使うズーム・タイルの拡大率・表示範囲・注記の衝突判定の範囲を論理ピクセル（`Zoom` から log2(`PixelRatio`) を引いた論理ズーム、画像寸法 ÷ `PixelRatio`）で求め、キャンバスを `PixelRatio` 倍に拡大して描く（MapLibre の pixelRatio と同じ考え方）。文字・線幅が論理ピクセルでは画素密度によらず一定になり、画像は物理ピクセルのまま鮮明に描かれる。`Zoom` と画像寸法は従来どおり物理ピクセルで指定し、取得するタイルのズームも従来どおり `floor(Zoom)`。`IVectorMapStyle` は変更しない（スタイルの実装は無改修）。**既定の 1.0 では、従来と画素単位で同じ画像になる**（既存のテストと、指定しない要求との一致テストで確認）。逐次・帯の並列・注記の並列の全経路を、スタイルへ渡る値（ズーム・倍率・範囲）、描かれた図形の物理ピクセル寸法、地理的な位置のテストで確認した。

- `PhotoMapStudio.Rendering` に、描画せずにタイルを先に取得・デコードしてデコード済みキャッシュへ入れる API `IVectorMapPrefetcher.PrefetchAsync`（`VectorMapRenderer` が実装。要求は `VectorPrefetchRequest`）を追加した（#138。PhotoGeoExplorer #295 の事前デコード）。取得ズームで中心を囲む 2×2 枚を対象にし、範囲外は飛ばし、経度は折り返す。`IVectorMapRenderer` は変えないので、利用側の既存の実装は壊れない。`DecodedTileCache` を指定していないと `InvalidOperationException`、取得の失敗は `TileFetchException`、キャンセルは `OperationCanceledException`（キャッシュへはデコードが済んだタイルだけを入れる）。描画と共通のタイル読み込みを共有し、描画の出力は変わらない。アプリの動作は変わらない。
- `PhotoMapStudio.Rendering` を NuGet パッケージとして公開できるようにし、`0.1.0` を公開した（#130 の PR B3b。タグ `rendering-v0.1.0`。nuget.org の検証・インデックスの完了は最大 1 時間かかる）。`IsPackable=true`・パッケージのメタデータと README（`src/PhotoMapStudio.Rendering/README.md`）・公開のワークフロー `.github/workflows/publish-rendering.yml`（`rendering-vX.Y.Z` のタグの push で起動。`publish-tiles.yml` と同じ作りで、Trusted Publishing・環境 `nuget` の承認）・手順書 `docs/development/releasing-rendering.md` を追加した。CI で、Rendering が WinUI・Windows App SDK・Core へ依存しないことと、SkiaSharp の版が宣言と一致することを検査する（`scripts/Test-RenderingPackage.ps1`。公開では、nupkg が宣言する Tiles への依存の版が nuget.org に公開済みであることも確認する）。Rendering の版は `-p:RenderingPackageVersion` で渡す（`-p:Version` は参照先の Tiles にも流れ、Tiles への依存が存在しない版になるため）。`PhotoMapStudio.Tiles` の csproj に既定の `<Version>`（公開済みの最新）を置き、Rendering の依存の最小の版にした。
- `PhotoMapStudio.Rendering` に、PhotoGeoExplorer の統合に要る描画の高速化を追加した（#130 の PR B2）。`VectorViewRequest.BandCount`（面・線を帯ごとに並列で描く。既定は 1＝並列にしない）と `ParallelLabels`（注記を別スレッドで描いて重ねる。既定は無効）、デコード済みタイルの LRU キャッシュ `DecodedTileCache`（`VectorMapRenderer` の省略可能な引数。既定はなし）。`IVectorMapStyle` の描画メソッドが `CancellationToken` を受け、スタイルの中でレイヤー・地物の単位でキャンセルを確認する（**スタイルを実装している場合は、メソッドの引数にトークンを足す**。0.1.0 の公開前の変更）。`OpenMapTilesStyle` は、描画先の範囲と交わらない地物を描かず（出力は変わらない）、破線の小道は範囲と交わる区間だけを位相を合わせて描く（深いズームの線の描画が数倍速くなる。破線の縁の半透明画素が数画素ずれ得る）。取り込まなかった PoC の最適化（パスの事前生成・道路のバッチ化・間引き・並列デコード）は `docs/development/vector-map-architecture.md` に理由を記録した。アプリの動作は変わらない。
- `PhotoMapStudio.Rendering` に、表示範囲全体を 1 枚の画像として描く API `VectorMapRenderer.RenderViewAsync` を追加した（#130 の PR B1）。連続ズーム（`double`。取得ズームは整数、表示倍率は連続値）、物理ピクセルの画像寸法、キャンセル（途中でキャンセルされたら `OperationCanceledException` で終わり、画像を返さない）、欠落タイルの判別（`AllowIncompleteMaps` と `UnavailableTileCount`・`AbsentTileCount`・`IsComplete`）、画像の所有権（結果は `IDisposable`。`TakeImage()` で引き取れる）に対応する。既存の API の出力は変わらない（保存済みタイルの比較で、ズーム 16〜19 の 4 枚が移行前と完全に一致）。アプリの動作は変わらない。
- `Prepare Release` に、NuGet パッケージ（`PhotoMapStudio.Tiles`・`PhotoMapStudio.Rendering`）の未公開の変更を確認するジョブ `package-check`（`scripts/Test-PackageUnpublishedChanges.ps1`。パッケージごとの引数）を追加した。最新の正式版のタグ（`tiles-vX.Y.Z`・`rendering-vX.Y.Z`）からの各パッケージのソースの差分を「変更なし」「依存の更新だけ」「変更あり」に分類し、公開が要りそうなときは警告を出す。警告だけで、リリース準備は止めず、パッケージの公開も自動では行わない（公開は従来どおり `tiles-vX.Y.Z` のタグで行う）。判断の基準は `docs/development/releasing-tiles.md` に追記した。

### Changed

- `PhotoMapStudio.Rendering` の公開 API を整理した（#130 の PR B3a。0.1.0 の公開前の変更）。`IVectorMapRenderer` を `RenderViewAsync` だけの契約にし、`VectorMapRenderer` の整数ズーム専用の旧 API（`RenderMapAsync`・`RenderToCanvasAsync`）と `VectorMapRenderContext` を削除した。`Core`（`SkiaMapImageComposer`）は新 API で描く。要求に `ColorType`（既定は実行環境の形式）を追加し、`Core` は合成先と同じ Rgba8888 を指定するため、アプリの出力は変わらない（保存済みタイルの出力が B2 と SHA-256 まで一致）。`VectorViewResult` のコンストラクターを公開した（`IVectorMapRenderer` の偽物を作れるように）。`GsiVectorStyle.GsiRoadKind`・`GetRoadCategory` を `internal` にし、公開 API の XML ドキュメントの未記載をビルドエラーにした（`GenerateDocumentationFile`）。
- ベクター地図の描画層（`VectorMapRenderer`・スタイル・注記配置など）を `PhotoMapStudio.Core` から新しいプロジェクト `PhotoMapStudio.Rendering`（名前空間 `PhotoMapStudio.Rendering`。`Tiles` に依存し、`Core` には依存しない）へ移した（#130 の PR A）。描画層の NuGet 化に備えた構成変更で、まだ公開しない（`IsPackable=false`）。描画のテストは `PhotoMapStudio.Rendering.Tests` へ移し、CI に追加した。レンダラーの引数は `GeoCoordinate` から緯度経度（`double`）に変わり、メルカトルの式は `Rendering` の内部に持つ（`Core.Geo.WebMercator` との一致は `Core.Tests` で検証する）。保存済みタイルで移行前後の出力を比較し、ズーム 16〜19 の 4 枚の PNG が完全に一致した。アプリの動作は変わらない。

## [0.3.3] - 2026-11-15

### Added

- タイルの取得・キャッシュ・レート制御・デコードを、NuGet パッケージ `PhotoMapStudio.Tiles`（`src/PhotoMapStudio.Tiles`）として切り出した。SkiaSharp・WinUI・Windows App SDK に依存せず、他のアプリ（PhotoGeoExplorer の最寄り道路名など）から使える。取得元の組み立て（キャッシュ・要求間隔・共有）は `VectorTileSourceFactory` が担い、`CustomVectorSourceFactory` は取得元に描画スタイルを組み合わせる。CI で、Tiles が描画・UI のパッケージへ依存していないことと `dotnet pack` の成功を確認する。
- `PhotoMapStudio.Tiles` を nuget.org へ公開するワークフロー `.github/workflows/publish-tiles.yml` と、手順書 `docs/development/releasing-tiles.md` を追加した。`tiles-vX.Y.Z` のタグの push で起動し（アプリのリリース `vX.Y.Z` とは独立）、タグが `main` のコミットであることの検証・テスト・依存の検査をしたうえで、環境 `nuget`（承認者を設定できる）の承認を待ち、Trusted Publishing（OIDC。長期の API キーなし）で push する。環境に承認者が設定されていない場合（GitHub は未作成の環境を保護ルールなしで自動作成する）は、push の前に失敗する。事前設定（環境と承認者・変数 `NUGET_USER`・nuget.org のポリシー）が済むまでは公開されない。
- `PhotoMapStudio.Tiles` の公開 API の XML ドキュメントを、未記載だった 45 メンバー（PMTiles のヘッダー・エントリー・列挙値、`VectorGeometryType`、`FilePmtilesByteSource` のコンストラクターなど）に追加し、`GenerateDocumentationFile` を有効にした。パッケージに XML ドキュメントが含まれ、未記載の公開メンバーはビルドエラーになる。
- `PhotoMapStudio.Tiles` のパッケージに README（`src/PhotoMapStudio.Tiles/README.md`。概要・使い方・User-Agent の設定）を含め、nuget.org のパッケージのページに表示されるようにした。使い方のサンプルコードは、パッケージを参照する別のプロジェクトでコンパイルできることを確認した。
- リポジトリの README に、`PhotoMapStudio.Tiles` の NuGet バッジ（最新の安定版）と、パッケージの README・公開手順へのリンクを追加した。
- OSM の道路種別（`highway` / `class`）が実際の路線種別（国道・都道府県道・市町村道）と合っているかを、PLATEAU（3D都市モデル）の道路の機能を正解として照合する検証スクリプト `scripts/road-class-validation/`（bun または Node。外部パッケージなし）と、その手順と 8 都市の結果の記録 `docs/development/road-class-validation.md` を追加した（#120 の調査）。盾の種別を決める `GetShieldKind` は変更していない。
- `docs/development/road-class-validation.md` に、道路名が `国道<番号>号` の形のとき国道と確定できるか（Issue #120 の 3 つ目の項目）の確認結果を追記した。精度は 89〜100% だが、路線関係に加えても国道の再現率は最大 7 ポイントしか上がらず、県道には使えないため採用しない。あわせて、鳥取市の国道の数値を、採用した優先順位（`JP:E` と `JP:national` の両方を持つ道路は高速）に揃えて訂正した。

### Changed

- タイル層の名前空間を `PhotoMapStudio.Core.Tiles` から `PhotoMapStudio.Tiles`（`.Vector`・`.Vector.Pmtiles`）へ変更した。描画・スタイルの型は `PhotoMapStudio.Core.Tiles.Vector` のまま。`TileFetchException.IsCacheOnlyMiss` を公開した。アプリの動作は変わらない。
- `PhotoMapStudio.Tiles` の `UserAgentProvider` を `public` から `internal` にした。このライブラリを使う他のアプリ（PhotoGeoExplorer など）は、`HttpClient.DefaultRequestHeaders.UserAgent` で自分の名称を設定すればよく、この型を直接使う必要がない。PhotoGeoExplorer が持つ同名の型との衝突を避け、公開 API を小さく保つ。**先行版 `0.1.0-rc.1` で `UserAgentProvider.Value` を直接使っている場合は、`0.1.0` で使えなくなる**。User-Agent の設定方法の案内は、`HttpTileClient`・`OpenFreeMapTileSource`・`HttpPmtilesByteSource` の XML ドキュメントと README に移した。
- タイルの取得（`HttpTileClient`・`OpenFreeMapTileSource`・`HttpPmtilesByteSource`）の User-Agent を、`HttpClient.DefaultRequestHeaders.UserAgent` が設定されていればそれを使うようにした。`PhotoMapStudio.Tiles` を使う他のアプリが、自分の名称と連絡先で配信元へ表明できる（`AddHttpClient` の構成で設定する）。設定がなければ、これまでどおり `PhotoMapStudio/<版>` を表明するため、このアプリの動作は変わらない。
- `renovate.json` の `extends` から、中央プリセットで解除済みの非推奨プリセット（`options/automerge`・`options/schedule`・`options/security`）への参照を削除した。スケジュール・待機期間・自動マージ等は中央側で既に解除されているため更新の挙動は変わらず、Dependency Dashboard の警告表示が消える。
- `tasks.md` を撤去した。進捗とタスクは Issue と Pull Request、リポジトリの外のハンドオフで管理する。

### Fixed

- .NET 11 SDK で `CsWinRT1032` エラーとなるコレクション式（`IReadOnlyList<T>` / `IEnumerable<T>` への暗黙変換）を明示的な配列式へ修正した（[#108](https://github.com/scottlz0310/photo-map-studio/pull/108) 対応）。
- 経路番号の盾の種別を、道路種別（`class`）からの推定ではなく、道路が属する路線関係（OpenFreeMap の `route_N_network`: `JP:national` は国道、`JP:prefectural` は都道府県道、`JP:E`・`JP:expressway*` は高速）で決めるようにした（#120）。8 都市の PLATEAU との照合で、`primary` を国道とする推定が全都市で外れ（国道の精度 32〜54%）、`tertiary` が都道府県道とは限らないことが分かったため。路線関係を持たない道路には盾を描かない（路線関係で決めた国道・都道府県道の精度は 85〜100%、再現率は 71〜99%）。Protomaps 系の `network`（`jp-national` など）は従来どおり使う。根拠と手順は `docs/development/road-class-validation.md`。

## [0.3.2] - 2026-10-23

### Added

- 通信失敗時にキャッシュ済みのベクタータイルと背景・ピン・出典を使い、欠落警告付きの画像を生成する設定を追加（既定OFF）。警告は画像・プレビュー・進捗・実行履歴へ残し、成功件数の内数として集計する。設定ONでPMTilesのメタデータ取得を含む一時的な通信失敗が連続10件に達すると、以後の通信を止め、キャッシュだけで残りの写真を処理する。認証・設定・破損データのエラーや明示キャンセルは縮退させない（[#57](https://github.com/scottlz0310/photo-map-studio/issues/57)）。

### Fixed

- タイル要求の最小間隔を、タイマー通知後も残時間を確認して守るよう修正。短い待機が期限より早く完了する環境でも発行間隔が詰まらないようにし、対応するテストを仮想時刻へ変更した。

- キャンセル待機テストを制御可能な時計と明示キャンセルへ変更し、CI負荷やタイマーの発火順序によって期待した例外が出なくなる不安定さを解消した。製品の要求間隔・キャンセル動作は変更しない。

## [0.3.1] - 2026-10-23

### Changed

- OpenFreeMap の地図表現を PoC の配色・ズーム連動の道路幅・建物輪郭・土地利用別の色・小道の破線・鉄道の枕木・河川名・施設マーカーに合わせて改善。地名・道路名・経路番号の近接重複を抑えた。
- 道路名を地名・施設名より先に配置し、生活道路名はズーム15以降、小道・歩道などの名称はズーム17以降で表示する。道路の折れ線に沿って文字を配置し、逆さまの文字・急カーブ・短い区間・注記同士の重なりを避ける。地理院の線形状を持つ注記も追従描画する（[#56](https://github.com/scottlz0310/photo-map-studio/issues/56)）。配信データに名称がない道路の名前は補完しない。

## [0.3.0] - 2026-10-23

### Added

- OpenFreeMap の経路番号の盾（高速道路・国道・都道府県道）と道路名注記を追加。道路種別の判定には配信データの network / class を使用する（[#64](https://github.com/scottlz0310/photo-map-studio/issues/64)）
- カスタムベクター配信元としてローカル／HTTP PMTiles と MVT URL テンプレートを指定可能にし、スキーマ・最大ズーム・出典を設定・保存できるようにした（[#65](https://github.com/scottlz0310/photo-map-studio/issues/65)）
- タイルキャッシュ容量上限（既定 1GB / 1024MB）と LRU 自動整理・消去機能を追加。ITileCache / FileSystemTileCache に容量上限指定（MaxSizeBytes）、アクセス日時に基づく LRU クリーンアップ（書き込み時および PruneAsync）、現在のキャッシュ容量取得（GetCurrentSizeBytes）、一括消去（ClearAsync）を実装。設定画面（GenerationOptionsView）から上限容量（MB）の変更、使用量表示、およびキャッシュ消去を実行可能にした（[#49](https://github.com/scottlz0310/photo-map-studio/issues/49)）

- ピン形状選択（緑・赤・青の同梱ピン、二重円ターゲット、十字ターゲット、カスタム画像）およびアンカー基準点（下端中央／中央）指定機能を追加。Core 側でのプロシージャル描画（同心円・白フチ付き十字クロスヘア）およびアンカー追従幾何計算、App 側での PinKindChoice / PinAnchorChoice 選択 UI、設定永続化（pin-kind / pin-anchor）、同梱アセットの出力ディレクトリ展開（NFR-DIST-02準拠）を実装（[#49](https://github.com/scottlz0310/photo-map-studio/issues/49)）

- 国土地理院 最適化ベクトルタイル対応（TileSources.GsiVector、TileSourceChoices.GsiVector）と PMTiles v3 HTTP Range 取得基盤を追加。PmtilesHilbert、PmtilesDirectory、HttpPmtilesByteSource、FilePmtilesByteSource、PmtilesVectorTileSource、GsiVectorTileSource、GsiVectorStyle を実装。国土地理院の最適化ベクトルタイル（optimal_bvmap-v1.pmtiles）から等高線・道路縁・建物・公的漢字注記（Anno / knj）を直接ラスタライズし、役所対応・公的測量基準図としての参考位置図案内、出典焼き込み（地理院タイル（最適化ベクトルタイル））、設定 UI 連携、ヘルプ・README・非保証事項を整備（[#49](https://github.com/scottlz0310/photo-map-studio/issues/49)）

- OpenFreeMap ベクター地図プリセット（TileSources.OpenFreeMap、TileSourceChoices.OpenFreeMap）と SkiaMapImageComposer への直接ベクター描画パイプライン統合を追加。Core 内蔵エンジンによる直接ラスタライズ、ピン合成および ODbL 出典焼き込み（© OpenStreetMap contributors, OpenFreeMap）、設定 UI 連携、ヘルプ・README を整備（[#49](https://github.com/scottlz0310/photo-map-studio/issues/49)）

- ベクタータイル直接描画アーキテクチャ設計仕様書（docs/vector-map-architecture.md）。中間ラスターを介さない直接ベクター合成描画（Direct Vector Composition）を採用し、共通描画エンジン上に OpenStreetMap（OpenFreeMap）と国土地理院（最適化ベクトルタイル）の 2 本立てスタイル（役所対応・公的測量基準図／現場確認・民間・世界対応）を選択可能とするアーキテクチャおよび段階的実装ロードマップ（M0〜M4）を策定（[#49](https://github.com/scottlz0310/photo-map-studio/issues/49)）
- ベクタータイル取得境界・キャッシュ層（IVectorTileSource、VectorTileCacheKey、CachedVectorTileSource、OpenFreeMapTileSource）を追加。OpenFreeMap パブリック配信（TileJSON 動的解決および最新 PBF）からの取得、固有 User-Agent 表明、実効識別子の非同期確定による URL 切替・配信更新に応じたキャッシュ分離、および透過的なローカルディスクキャッシュ機構を実装（[#49](https://github.com/scottlz0310/photo-map-studio/issues/49)）
- Core 内蔵ベクタータイル直接描画エンジン（VectorTileDecoder、VectorTileModel、IVectorMapStyle、OpenMapTilesStyle、LabelCollisionGrid、VectorMapRenderer）を追加。外部依存を持たないゼロアロケーション指向の MVT/PBF デコーダー、OpenMapTiles スキーマ対応の面・線・注記スタイル、および SkiaSharp による過拡大対応・AABB 衝突判定付き直接ラスタライズ機構を実装（[#49](https://github.com/scottlz0310/photo-map-studio/issues/49)）


### Changed

- タイルキャッシュの容量をメモリ上で追跡し、書き込みごとの全ファイル列挙を削減。容量変更時の整理を非同期化し、残存する一時ファイルの回収と I/O 失敗の診断記録を追加（[#69](https://github.com/scottlz0310/photo-map-studio/issues/69)）
- F1 ヘルプにピン形状・アンカー・キャッシュ上限・BYO を反映し、100 枚超の確認を配信元ごとの名称・取得条件に合わせた（[#63](https://github.com/scottlz0310/photo-map-studio/issues/63)）
- プライバシーポリシーに OpenFreeMap、PMTiles の Range 取得、カスタム配信元、ベクターキャッシュと LRU 整理を反映（[#62](https://github.com/scottlz0310/photo-map-studio/issues/62)）
- `docs/` を利用者向けマニュアル中心に整理し、設計・仕様・検証記録を `docs/development/` に移動（[#70](https://github.com/scottlz0310/photo-map-studio/issues/70)）
- release-automate による Prepare Release と、draft 作成・署名資産添付・再取得検証・正式公開の段階的なリリースフローを導入（[#77](https://github.com/scottlz0310/photo-map-studio/issues/77)）

### Fixed

- 国外など地理院ベクトルの描画対象タイルがない場所を 404 と判定し、プレビューでは OSM へ切り替え、一括生成では白紙画像を保存せずスキップする（[#60](https://github.com/scottlz0310/photo-map-studio/issues/60)）
- プレビュー・一括生成の完了やキャンセル後にキャッシュ使用量表示を更新する（[#61](https://github.com/scottlz0310/photo-map-studio/issues/61)）
- OpenFreeMap の URL 解決失敗がプロセス終了まで固定される問題を修正。直近の解決先を保存し、確定した配信バージョンとキャッシュキーを取得まで一致させる。配信バージョンごとのキャッシュ分離は維持する（[#66](https://github.com/scottlz0310/photo-map-studio/issues/66)）
- PMTiles の HTTP Range 取得に User-Agent を付与し、HTTP タイムアウトを取得失敗として通知する（[#67](https://github.com/scottlz0310/photo-map-studio/issues/67)）
- 旧設定の `pin-image-path` に画像が指定されている場合、ピン形状を `CustomImage` へ自動移行し、指定済みの画像を引き続き使用する

- シングルトンのベクタースタイル（`OpenMapTilesStyle` / `GsiVectorStyle`）が、描画のたびに共有の `SKPaint` の線幅を書き換えていたため、プレビューと一括生成が同時に描画すると線幅が混ざる競合が起き得る不具合を修正。共有ペイントは書き換えず、描画ごとに線幅を設定した複製を使う（[#68](https://github.com/scottlz0310/photo-map-studio/issues/68)）
- ベクター地図スタイル（国土地理院 最適化ベクトルおよび OpenFreeMap）において、Windows 標準の日本語フォント（Yu Gothic UI / 日本語対応 Typeface）を自動解決する VectorMapFontHelper を導入し、地名・施設名・等高線注記の漢字・ひらがなが豆腐（□）として描画される不具合を修正（[#49](https://github.com/scottlz0310/photo-map-studio/issues/49)）

## [0.2.3] - 2026-10-18

### Changed

- App Installer の毎回の起動時更新確認を廃止し、Windows による8時間間隔のバックグラウンド確認へ変更。更新確認による起動時の待機画面を避ける。既存インストールは新しい `.appinstaller` の設定適用後に切り替わる（[#21](https://github.com/scottlz0310/photo-map-studio/issues/21) の検討で合意した代替対応）
- 依存パッケージを更新: Windows App SDK 2.5.1、SkiaSharp 4.153.1、Microsoft.Extensions.DependencyInjection / Http 10.0.13

## [0.2.2] - 2026-10-07

### Added

- 一括生成のスキップ・エラーを、生成サービスの確定結果として相対パス・理由・失敗情報とともに保持する。フォルダ列挙失敗、キャンセル・連続失敗中止、開始・継続エラーまでの結果も保持する
- 実行履歴を端末内の `LocalState/batch-history` へ自動保存し、直近30回を保持する。画面で過去の実行を選び、入力フォルダ・集計・スキップ・エラーの相対パスと理由を確認できる
- 履歴の保存・読み取り・整理に失敗した場合は画面へ通知し、処理済みの写真の集計と今回の結果を保持する。履歴には写真本体・GPS・タイルURL・生の例外メッセージを保存しない

## [0.2.1] - 2026-10-06

### Changed

- カスタムタイルの配信元がこの PC 自身（`localhost`、`127.0.0.0/8`、`::1`）の場合、プレビューも一括生成も 8 並列・5 ms 間隔（`TileRateLimit.Loopback`。従来は一括が 1 接続・1 秒間隔）にし、100枚超の確認を省略する。LAN やプライベート IP は他者の資源の可能性があるため対象外で、従来どおり 1 接続・1 秒間隔。画面の案内とヘルプを実態に合わせた（[#37](https://github.com/scottlz0310/photo-map-studio/issues/37)）

## [0.2.0] - 2026-10-06

### Added

- 下位フォルダの再帰探索、各写真フォルダ基準の相対出力、出力名の先頭・末尾文字の設定と永続化（[#34](https://github.com/scottlz0310/photo-map-studio/issues/34)）
- 写真一覧の段階読み込み、列挙・GPS確認・一括生成の進捗、経過時間、処理結果とタイル取得／キャッシュ件数、キャンセル後の候補保持

[Unreleased]: https://github.com/scottlz0310/photo-map-studio/compare/v0.3.3...HEAD
[0.3.3]: https://github.com/scottlz0310/photo-map-studio/compare/v0.3.2...v0.3.3

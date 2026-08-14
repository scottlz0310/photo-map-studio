# Changelog

このプロジェクトのすべての注目すべき変更をこのファイルに記録します。

形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に基づき、[Semantic Versioning](https://semver.org/lang/ja/) に従います。

## [Unreleased]

### Added

- リポジトリ初期化とプロジェクト構成（`PhotoMapStudio.slnx` / `App` / `Core` / `Core.Tests` / `App.Tests`）
- `Directory.Build.props` によるアナライザ設定の集約（`AnalysisMode=AllEnabledByDefault` / `TreatWarningsAsErrors`）
- CI（`dotnet format` / `build` / `test` / Codecov OIDC 連携）
- x64 / ARM64 の MSIX パッケージング検証を CI に追加
- lefthook による pre-commit フック（`dotnet format --verify-no-changes`）
- Renovate 設定（`github>scottlz0310/renovate-config` を extend）
- 移植仕様書 `docs/photo-map-studio-migration-spec.md`（[auto-map-generator](https://github.com/scottlz0310/auto-map-generator) から抽出）
- `PhotoMapStudio.Core` に EXIF GPS 読み取り（`IExifGpsReader` / `DmsCoordinate`）を追加。GPS 情報なしと読み取り失敗を区別する
- `PhotoMapStudio.Core` に Web メルカトル座標変換（`WebMercator` / `TilePoint` / `TileRange`）を追加
- `PhotoMapStudio.Core` に写真ファイルの列挙（`IPhotoFileEnumerator`）を追加。フォルダ直下のみを名前昇順で走査する
- タイルソースのプリセット（`TileSource` / `TileSources`）を追加。地理院タイル（淡色 / 標準）・OpenStreetMap・任意 URL に対応し、URL・ズーム範囲・出典表示・レート制御方針を 1 つの型で束ねる
- タイル取得（`ITileClient` / `HttpTileClient`）を追加。`IHttpClientFactory` と `CancellationToken` に対応し、取得失敗は `TileFetchException` として伝播する
- タイルのローカルキャッシュ（`ITileCache` / `FileSystemTileCache` / `TileCacheKey`）を追加。キャッシュキーに URL テンプレートの SHA-256 を含め、保持期間の下限を 7 日とする
- User-Agent の一元管理（`UserAgentProvider`）とレート制御（`ThrottledTileClient`）を追加
- 地図画像の合成（`IMapImageComposer` / `SkiaMapImageComposer`）を追加。タイルの貼り合わせ・切り出し・ピン合成（アンカーは下端中央）・フォールバックピン・出典表示の焼き込みに対応する
- 既定のタイルソースを地理院タイル（淡色）に決定（`TileSources.Default`）。実測比較の結果は [#8](https://github.com/scottlz0310/photo-map-studio/issues/8) を参照
- 日本国外など配信範囲外の写真で、代替タイルソース（OpenStreetMap）へ自動的に切り替える `FallbackMapImageComposer` を追加
- 合成結果に使用したタイルソースと代替切替の有無を含める（`MapCompositionResult`）

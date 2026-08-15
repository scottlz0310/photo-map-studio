# Changelog

このプロジェクトのすべての注目すべき変更をこのファイルに記録します。

形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に基づき、[Semantic Versioning](https://semver.org/lang/ja/) に従います。

## [Unreleased]

## [0.1.1] - 2026-08-15

### Fixed

- PowerShell 5.1 からの MSIX インストールで、署名証明書を `LocalMachine\TrustedPeople` に登録するよう修正
- UAC 昇格を証明書登録だけに限定し、AppX の登録と結果確認を起動元ユーザーで行うよう修正
- AppX の実体（`AppxManifest.xml`）を確認してからインストール完了を表示するよう修正
- Windows PowerShell 5.1 のスクリプト文字コードと `Invoke-WebRequest` の進捗出力に対応

## [0.1.0] - 2026-08-15

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
- WinUI 3 の基本レイアウト、設定 UI、設定の永続化、テーマ追従、ウィンドウ状態の復元を追加
- GPS 付き写真のプレビュー表示、対象切り替え、attribution / ライセンスリンク、CancellationToken による再生成キャンセルを追加
- 写真フォルダの一括生成、ファイル単位の進捗・ログ・キャンセル、出力名衝突検出、OSM 単一接続レート制御を追加。`--input-dir` / `--output-dir` 起動引数と単一インスタンス転送に対応
- 地図画像の合成（`IMapImageComposer` / `SkiaMapImageComposer`）を追加。タイルの貼り合わせ・切り出し・ピン合成（アンカーは下端中央）・フォールバックピン・出典表示の焼き込みに対応する
- 既定のタイルソースを地理院タイル（淡色）に決定（`TileSources.Default`）。実測比較の結果は [#8](https://github.com/scottlz0310/photo-map-studio/issues/8) を参照
- 日本国外など配信範囲外の写真で、代替タイルソース（OpenStreetMap）へ自動的に切り替える `FallbackMapImageComposer` を追加。切り替えは `MapCompositionRequest.AllowWorldwideFallback` で制御し、OSM の bulk downloading に該当する一括生成では無効にする
- 合成結果に使用したタイルソースと代替切替の有無を含める（`MapCompositionResult`）
- ドメイン層の既定の構築経路を `AddPhotoMapStudioCore` として提供。レート制御・キャッシュ・配信範囲外の切り替えを組み合わせた構成を 1 か所に固定する
- x64 / ARM64 の署名済み MSIX、アーキテクチャ別 App Installer、GitHub Releases を生成するリリースワークフローを追加

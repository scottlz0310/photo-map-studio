# Changelog

このプロジェクトのすべての注目すべき変更をこのファイルに記録します。

形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に基づき、[Semantic Versioning](https://semver.org/lang/ja/) に従います。

## [Unreleased]

### Added

- 下位フォルダの再帰探索、各写真フォルダ基準の相対出力、出力名の先頭・末尾文字の設定と永続化（[#34](https://github.com/scottlz0310/photo-map-studio/issues/34)）
- 写真一覧の段階読み込み、列挙・GPS確認・一括生成の進捗、経過時間、処理結果とタイル取得／キャッシュ件数、キャンセル後の候補保持
- XAMLネイティブのアプリ内ヘルプとF1操作、フォルダツリー例、結果の表とコピー可能なコマンド例
- エラー時のローカル診断ログ。処理段階・例外型・HResultなどを最大2MiBまで保持し、写真・GPS・パス・URLを記録せず、外部へ送信しない
- タイル取得の平均・最大実効レート、404以外の連続10件失敗による中止、Retry-Afterの表示、カスタム一括取得の1接続・1秒間隔と100枚超の確認

### Changed

- 出力衝突を付加文字適用後の最終出力パスで判定し、進捗とプレビュー候補は相対パスで区別
- OSM公式サーバーを対話プレビューに限定し、一括生成は一括取得・画像保存を許可するOSM系配信元をカスタム設定で指定する方針に変更（Issue #34からの合意済み変更）

### Fixed

- ヘルプと大量実行の確認を直列表示し、確認の待機中もキャンセルできるように修正
- 最終出力名が255文字以内であれば、一時ファイル名の長さに影響されず保存できるように修正
- Dropboxなどのクラウド同期ファイルをReparsePoint属性だけで探索対象から除外しないよう修正
- 進捗ログをスクロール可能にしてプレビュー領域を保持し、ヘルプの複数行の例とパッケージのバージョンを正しく表示

## [0.1.3] - 2026-09-28

### Changed

- インストールスクリプト（`Install-PhotoMapStudio.ps1`）で、全体の段階数と現在位置（`[n/5]`）、各段階の結果・所要時間・スキップ理由を表示するよう変更。`-Test` モードでも同じ段階表示を行う（[#29](https://github.com/scottlz0310/photo-map-studio/issues/29)）
- 証明書・`.appinstaller` のダウンロード中、`Add-AppxPackage` の実行中、登録完了の待機中は、5 秒ごとに経過時間（待機中は現在の登録状態も）を出力するよう変更
- 証明書登録で UAC ダイアログが表示される前に承認後の流れを案内し、昇格プロセスの終了結果を元のウィンドウに表示するよう変更
- 依存パッケージを更新: Windows App SDK 2.4.0、Microsoft.Windows.SDK.BuildTools 10.0.28000.2705、SkiaSharp 4.151.3

### Security

- `Microsoft.Extensions.DependencyInjection` / `Microsoft.Extensions.Http` をセキュリティ修正版の 10.0.12 に更新

## [0.1.2] - 2026-08-16

### Changed

- ブランドアセットの刷新: 「レンズ光学 × デジタル現像・スタジオ」をコンセプトとした新しいアプリアイコン・インストーラーロゴ・スプラッシュ画面の導入
- MSIX アセット生成スクリプトによるパッケージ用アセット一式の再生成
- `Package.appxmanifest` のタイル背景色を新デザイン（ダークスレート系）に合わせて更新

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

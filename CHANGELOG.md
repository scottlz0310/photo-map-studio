# Changelog

このプロジェクトのすべての注目すべき変更をこのファイルに記録します。

形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に基づき、[Semantic Versioning](https://semver.org/lang/ja/) に従います。

## [Unreleased]

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

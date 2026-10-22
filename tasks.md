# 作業タスク

## Issue #49: オンデマンド描画とキャッシュによるベクタータイル（OSM）地図対応 (Epic)

- [x] 所有者承認のもと、Epic Issue #49 を起票
- [x] M0: ベクタータイル直接描画アーキテクチャ設計・ドキュメント策定（docs/vector-map-architecture.md、handoff 参照更新、OSM・地理院2本立て方針確立）
- [x] M1: ベクタータイル取得・キャッシュ層の抽象化（IVectorTileSource、VectorTileCacheKey、CachedVectorTileSource、OpenFreeMapTileSource、単体テスト）
- [x] M2: Core 内蔵ベクタータイル描画エンジン（VectorTileDecoder、OpenMapTilesStyle、LabelCollisionGrid、VectorMapRenderer、Core 単体テスト）
- [x] M3: UI 連携・設定・出典表示（WinUI 3 設定UI、ODbL 出典焼き込み、ヘルプ・ドキュメント更新）
- [x] M4: 国土地理院ベクター対応・最適化PMTiles Range取得 & BYOフォールバック（GsiVectorStyle、optimal_bvmap-v1 PMTiles Range取得、PmtilesVectorTileSource、レート制御、設定UI連携・非保証事項案内）
- [ ] （将来・任意）M5: 拡張機能（カスタム PMTiles / スタイル指定、直接ベクター合成描画）
## リリース v0.2.3

- [x] 所有者が v0.2.3 のリリースを指示
- [x] Package.appxmanifest のバージョンを 0.2.3.0 に更新、CHANGELOG を確定
- [ ] リリース準備 PR の CI と独立レビュー、マージ
- [ ] v0.2.3 のタグ・GitHub Release 公開、両アーキテクチャの配布資産を検証

## Issue #21 の代替対応: バックグラウンド更新

- [x] Windows MCP で起動時の App Installer 情報取得画面を確認し、バックグラウンド更新へ変更する方針を所有者が承認
- [x] x64／ARM64 の App Installer を `AutomaticBackgroundTask` のみに変更し、起動時確認を廃止
- [x] README・配布仕様・CHANGELOG に確認間隔、即時更新の手順、既存インストールの移行条件を記載
- [x] インストール完了メッセージもバックグラウンド確認と手動更新の案内に合わせて修正（独立レビューで確認された文言不整合）
- [x] Release と同じ置換で両アーキテクチャの XML・版・Identity・配布 URL・バックグラウンド更新のみの設定を検証、`git diff --check` 成功
- [x] PR #46 の同一 HEAD `a5a3d6e` の独立 APPROVED、CI 全4件成功、未解決0件を確認し、所有者許可でマージ済み（`8da545b`）
- [ ] 次回リリース後、新設定を適用した端末で起動時の確認画面が出ないこととバックグラウンド更新の実動作を確認

## T-045 一括生成の問題と実行履歴（osm-custom-server handoffから着手）

- [x] 生成サービスが相対パス・結果・理由・失敗情報を構造化した確定一覧として返す
- [x] 進捗通知とは独立した完了・キャンセル・途中中止・開始/継続失敗の結果保持
- [x] LocalStateへの実行単位JSON自動保存、正常保存後に直近30回を保持
- [x] 過去の実行を選び、スキップ・エラーとフォルダ読み取り失敗のパス・理由を表示
- [x] 保存/読み込み/整理失敗の画面通知、元の写真集計・今回の結果を保持
- [x] README・ヘルプ・プライバシー・CHANGELOGの更新
- [x] パラメータ化テスト、4961件の唯一のスキップ保持、30回の整理、再読み込み、秘密情報の除外、既存診断の回帰テスト
- [x] ローカルのApp・Coreテスト、Release/x64ビルド（警告0・エラー0）
- [x] 所有者承認のWindows MCPで別Identity・架空履歴2件の表示、履歴切替、再起動後の再読み込みを実画面確認
- [x] PRのCIと独立レビュー、未解決スレッドの確認（PR #40、HEAD `03d6fcc` のAPPROVED、全4check成功、未解決0件、所有者許可でマージ済み）

## リリース v0.2.2

- [x] 所有者がマージ・リリースを許可し、0.2系の継続を指定
- [x] Package.appxmanifest のバージョンを 0.2.2.0 に更新、CHANGELOG を確定

## Issue #37

- [x] ループバック判定（`TileSource.IsLoopback`、`IsLoopbackUrlTemplate`）と、一括生成レート（`BatchRateLimit`）の緩和
- [x] ループバック用のレート（`TileRateLimit.Loopback`: 8並列・5ミリ秒間隔）。静的配信の負荷試験（約150〜260件/秒で頭打ち）に基づく
- [x] ループバックでは100枚超の確認を省略
- [x] 画面の案内（`TileUsageMessage`）とヘルプ（F1）をループバックの実態に合わせる
- [x] パラメータ化テスト（Core・App）。ループバック判定を常に偽にする変異でループバックのテストが失敗することを確認
- [x] ローカルの自動テスト（Core 234件・App 127件）・format
- [x] PRのCI最終結果と独立レビューの完了（PR #38、同じHEAD `afe6fab` のAPPROVED、build・x64／ARM64 package・codecov/patchが成功、未解決0件、マージ済み）

## リリース v0.2.1

- [x] Package.appxmanifest のバージョンを 0.2.1.0 に更新、CHANGELOG を確定

## Issue #34

- [x] 再帰探索・決定的な順序・列挙進捗とキャンセル
- [x] 相対出力・出力名の付加文字・最終パスの衝突判定・永続化
- [x] プレビュー一覧の段階読み込みとキャンセル後の候補保持
- [x] 一括生成の件数・経過時間・取得統計・連続失敗の中止
- [x] OSM公式の対話用途と、許可されたカスタム配信元の一括用途の使い分け
- [x] XAMLヘルプ・README・移植仕様書・CHANGELOG
- [x] 初回独立レビューの2件修正（ダイアログ競合、長い出力名の一時保存）と回帰テスト
- [x] ローカルの自動テスト・format・ビルド・x64／ARM64 MSIX生成
- [x] ローカル実写真の段階検証（109枚を元にした複製を含む。結果は `docs/issue-34-validation.md`）
- [x] Dropbox同期写真の探索除外と実画面の表示不具合の修正・回帰テスト
- [x] エラー時のローカル診断ログ・容量制限・個人情報の除外・保存失敗の通知と回帰テスト
- [x] PRのCI最終結果（HEAD `86a3363` のbuild・x64／ARM64 package・codecov/patchが成功）
- [x] ユーザー指定のDropbox同期フォルダで7,500枚・約5GBの段階検証（SMB共有とは異なる）
- [x] XAMLの手動確認（F1開閉、ライト／ダーク、狭い幅、キーボード操作）
- [x] 独立レビューの完了と未解決スレッドの確認（PR #35、同じHEADのAPPROVED、未解決0件、マージ済み）

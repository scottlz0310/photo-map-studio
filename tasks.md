# 作業タスク

## v0.3.0 リリース前の修正（監査 2026-10-23）

- [x] #60 国外 × 地理院ベクトルで白紙地図が成功扱いになる不具合の修正（レンダラーが描画できるタイルなしを 404 として送出、切替・スキップの回帰テスト）
- [x] #61 キャッシュ使用量表示の更新
- [x] #67 PMTiles の User-Agent・タイムアウトの例外化
- [x] #66 OpenFreeMap の URL 解決失敗の固定化・キャッシュ鍵（#57 と整合）
- [x] #68 スタイルの共有 SKPaint
- [x] #69 FileSystemTileCache の改善
- [x] #64 経路番号の盾・道路名注記
- [x] #65 BYO（PMTiles / ベクタータイル URL）
- [x] #63 ヘルプ・確認ダイアログの文言
- [x] #62 プライバシーポリシーの更新
- [x] #70 docs/ の再編（利用者向けマニュアル中心）
- [x] #71 リリース準備の変更作成（版数 0.3.0.0・CHANGELOG の未記載項目・Epic 記録・最終実機確認）
- [x] #77 release-automate による段階公開の導入（PR #84）
- [ ] #71 準備 PR の独立レビュー・所有者許可後のマージ
- [ ] 所有者許可後に Prepare Release を起動し、版・日付確定 PR のレビュー・マージと配布資産の検証・公開を行う
- 最終実機確認と未実施項目: [v0.3.0-validation.md](docs/development/v0.3.0-validation.md)。CHANGELOG の版・日付確定は Prepare Release が担当する。

## Issue #49: オンデマンド描画とキャッシュによるベクタータイル（OSM）地図対応 (Epic)

- [x] 所有者承認のもと、Epic Issue #49 を起票
- [x] M0: ベクタータイル直接描画アーキテクチャ設計・ドキュメント策定（docs/development/vector-map-architecture.md、handoff 参照更新、OSM・地理院2本立て方針確立）
- [x] M1: ベクタータイル取得・キャッシュ層の抽象化（IVectorTileSource、VectorTileCacheKey、CachedVectorTileSource、OpenFreeMapTileSource、単体テスト）
- [x] M2: Core 内蔵ベクタータイル描画エンジン（VectorTileDecoder、OpenMapTilesStyle、LabelCollisionGrid、VectorMapRenderer、Core 単体テスト）
- [x] M3: UI 連携・設定・出典表示（WinUI 3 設定UI、ODbL 出典焼き込み、ヘルプ・ドキュメント更新）
- [x] M4: 国土地理院ベクター・PMTiles Range 取得・BYO 対応（PR #53・#54・#80。ローカル／HTTP PMTiles と MVT URL の設定・保存。オフライン耐性は #57 で継続）
- [x] ベクター地図の日本語フォント自動解決と文字化け（豆腐）修正（VectorMapFontHelper、GsiVectorStyle、OpenMapTilesStyle、単体テスト）
- [x] ピン機能の強化とアンカー基準点指定（同梱ピン緑/赤/青、二重円ターゲット、十字ターゲット、カスタム画像、アンカーBottomCenter/Center、Core・App・単体テスト）
- [x] （改善案②）ベクタータイルキャッシュ容量制限（1GB既定）とLRU自動クリーンアップ（ITileCache、FileSystemTileCache LRU自動整理、設定UI・消去コマンド、単体テスト）
- [x] 経路番号の盾・道路名注記（PR #79。道路名の折れ線追従は #56 で継続）
- [ ] （将来・任意）M5: カスタムスタイル指定（直接ベクター合成描画とカスタム PMTiles は実装済み）

## リリース v0.2.3

- [x] 所有者が v0.2.3 のリリースを指示
- [x] Package.appxmanifest のバージョンを 0.2.3.0 に更新、CHANGELOG を確定
- [x] リリース準備 PR #47 の CI と独立レビュー、所有者許可によるマージ
- [x] v0.2.3 のタグ・GitHub Release 公開、x64／ARM64 の配布資産 6 件と Install スクリプトの -Test を検証（2026-10-18）

## キャンセル待機テストの安定化

- [x] 実時間とCancelAfterに依存する競合を、制御した時計と明示キャンセルへ変更
- [x] 複数の要求間隔で、待機中のキャンセルと後続要求の通過を検証
- [x] CIと同じローカル自動検証（format・Release/x64 build・Core526件／App239件）
- [ ] PR作成・レビューキュー登録・独立レビュー

## ベクター地図の PoC 品質回復・細街路名・Issue #56

- [x] 所有者が PoC 品質の回復・細街路名優先・#56 を同じ PR にまとめる方針を承認
- [x] 道路階層の色分け・ズーム連動の幅・建物輪郭・土地利用・水路・河川名・小道・鉄道・施設マーカーを復元
- [x] 拡大時の細街路名、道路に沿う注記、急カーブと衝突の回避、近接する同名注記の抑制
- [x] 地理院 Anno の線注記を追従描画（点注記から道路形状を推測しない）
- [x] 同じ保存タイルを使い、同一座標・800×600・z16〜19 の変更前後を比較。都市部・国外 z18 も目視確認
- [x] 最終の format・Release/x64 build・Core 525件・App 239件の tests（CI と同じ coverage 収集付き）
- [ ] PR 作成・review queue 登録・同 HEAD の購読と独立レビュー

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
- [x] ローカル実写真の段階検証（109枚を元にした複製を含む。結果は `docs/development/issue-34-validation.md`）
- [x] Dropbox同期写真の探索除外と実画面の表示不具合の修正・回帰テスト
- [x] エラー時のローカル診断ログ・容量制限・個人情報の除外・保存失敗の通知と回帰テスト
- [x] PRのCI最終結果（HEAD `86a3363` のbuild・x64／ARM64 package・codecov/patchが成功）
- [x] ユーザー指定のDropbox同期フォルダで7,500枚・約5GBの段階検証（SMB共有とは異なる）
- [x] XAMLの手動確認（F1開閉、ライト／ダーク、狭い幅、キーボード操作）
- [x] 独立レビューの完了と未解決スレッドの確認（PR #35、同じHEADのAPPROVED、未解決0件、マージ済み）

## タイル要求の最小間隔の保証

- [x] HTTPなしの30回実測で既存の実時間テストの不安定さを再現（200ms未満4回）
- [x] ラスター・ベクターの関門でタイマー通知後に残時間を再確認し、1ms未満の待機も切り上げる
- [x] 早く通知する仮想タイマーで修正前4件失敗、修正後成功を確認
- [x] 待機中のキャンセル後も後続要求を妨げないことを確認
- [x] CI相当のrestore・format・Release/x64 build・Core532件／App239件
- [ ] PR作成・独立レビュー

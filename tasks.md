# 作業タスク

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

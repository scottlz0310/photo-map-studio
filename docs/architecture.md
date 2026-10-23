# アーキテクチャ・配布仕様

## 実行時の構成

- `src/PhotoMapStudio.App`: WinUI 3 の画面、設定、MSIX マニフェスト
- `src/PhotoMapStudio.Core`: EXIF 解析、タイル取得、キャッシュ、地図合成
- `tests/`: UI とドメインの単体テスト

一括生成の確定結果は `BatchGenerationService` が `BatchGenerationSummary.Issues` として返します。写真の相対パス・結果・理由コード・HTTP状態・例外型/HResultを処理側で保持し、UIの進捗文字列から再構成しません。途中中止までの結果も同じ契約で返します。履歴保存はDI境界の `IBatchGenerationHistoryStore` が担当し、`LocalState/batch-history` に実行単位のJSONを保存して直近30回を保持します。ViewModelは実行を調整し、表示用の日本語文言と履歴選択を担当します。履歴への保存失敗は写真の処理結果と分けて通知します。履歴内容とプライバシーは [README](../README.md#一括生成の履歴) と [プライバシーポリシー](privacy-policy.html)を参照してください。

アプリプロジェクトは `SelfContained=true` と `WindowsAppSDKSelfContained=true` を使用します。配布対象は x64 と ARM64 の個別 MSIX で、`AppxBundle=Never` によりアーキテクチャ混在のバンドルは作成しません。

地図合成の既定ピンは `Assets/MapPins/green_pin.png` です。空の設定は実行時にMSIX内のこのパスへ解決するため、パッケージ更新でインストール先が変わっても古い絶対パスを保存しません。`blue_pin.png` と `red_pin.png` も同梱し、ユーザー指定のピン画像として利用できます。

## リリースフロー

リリースは [release-automate](https://github.com/scottlz0310/release-automate)（Reusable Workflows）で自動化し、準備と公開の 2 段階で行います。参照はすべて固定コミット SHA で、Renovate の更新対象です。

1. **準備**: `Prepare Release`（`.github/workflows/prepare-release.yml`、手動起動）に `target_version`（例: `0.3.0`）を指定して起動します。CHANGELOG の `[Unreleased]` を指定の版で確定し、GitHub App 名義のリリース準備 PR（件名 `chore(release): vX.Y.Z`）を起票します。版数はリポジトリのファイルを更新しません（`bump_strategy: none`）。MSIX の版は、タグからビルド時に `Package.appxmanifest` へ注入します。
2. **公開**: リリース準備 PR を独立レビューのうえマージすると、`Publish Release`（`.github/workflows/publish-release.yml`、`main` への push）が段階的に公開します。対象は、件名が `chore(release):` で始まるコミットだけです。通常の push では何も起きません。

| 段階 | 内容 |
| --- | --- |
| draft | タグと draft Release を作成する。リリースノートは CHANGELOG の該当版から抽出する |
| package | x64 / ARM64 を並列に self-contained ビルドする（署名しない） |
| attach | 署名し、`.cer`、App Installer XML、Install スクリプトとあわせて draft に添付する |
| verify | draft から資産を再取得して検証する（`scripts/Verify-ReleaseAssets.ps1`） |
| finalize | 検証に成功した場合だけ draft を公開する |

タグは `GITHUB_TOKEN` で作られ、タグ push を契機とする別の workflow は起動しないため、すべて同じ workflow の `needs` でつないでいます。いずれかの段階が失敗しても draft は公開されず、同じ run の「Re-run failed jobs」で再開できます。

`scripts/Verify-ReleaseAssets.ps1` は、次の項目を確認します（失敗した項目はすべて列挙します）。

- 6 つの資産（MSIX × 2、`.cer`、`.appinstaller` × 2、Install スクリプト）が過不足なくそろっている
- MSIX の Identity（名前・版・アーキテクチャ・Publisher）がタグと一致する
- MSIX が署名されていて、署名が壊れていない（自己署名のため、信頼済みストアにない環境では「信頼されていない」状態を許容し、署名の欠落と改ざんを失敗とする）
- `.cer` の拇印が、両方の MSIX の署名者と一致する
- `.appinstaller` の版・URL・更新設定が正しい（`AutomaticBackgroundTask` のみ）
- Install スクリプトが構文として解釈でき、リポジトリのものと一致する

公開済みの Release は、`-RunInstallTest` で Install スクリプトの `-Test`（資産の取得と定義の検証だけ。証明書登録とインストールはしない）まで確認できます。`-Test` は最新の公開 Release の資産を取得するため、draft の検証では実行しません。

App Installer の `MainPackage` はタグ付き Release の MSIX を、ルートの `Uri` は最新 Release のアーキテクチャ別 `.appinstaller` を参照します。x64 と ARM64 の App Installer は異なるパッケージ URI を持つため、利用環境に合う方を選択してください。

`UpdateSettings` は `AutomaticBackgroundTask` のみを指定し、`OnLaunch` は指定しません。起動時の更新確認画面を避けるため、Windows による8時間間隔のバックグラウンド確認を使用します。この更新経路は UI を表示しません。独自ランチャーやアプリ内の更新処理は追加しません。[Microsoft の仕様](https://learn.microsoft.com/en-us/uwp/schemas/appinstallerschema/element-s3-automaticbackgroundtask)を参照してください。

更新の即時適用は保証しません。PC の稼働・通信状況などにより反映が遅れる場合があり、すぐに更新する場合は最新 Release の該当アーキテクチャの `.appinstaller` を開きます。既存インストールは新しい `.appinstaller` の設定が適用されるまで従来の起動時確認を継続するため、次のリリース公開だけで全端末が直ちに切り替わるとは扱いません。初回インストールや手動更新の App Installer 画面は引き続き表示されます。

## 署名

ローカルでは次のスクリプトで開発用コード署名証明書を作成できます。

```pwsh
.\scripts\New-SigningCertificate.ps1
```

公開ワークフロー（`publish-release.yml`）には、所有者が次の GitHub Secrets を登録します。

- `SIGNING_CERTIFICATE_BASE64`: PFX ファイルを Base64 化した値
- `SIGNING_CERTIFICATE_PASSWORD`: PFX のパスワード

証明書の秘密鍵とパスワードはリポジトリへコミットせず、ログにも出力しません。証明書の発行と Secret の登録は、リリース所有者が行う作業です。公開は、リリース準備 PR のマージで起動します。リリース準備 PR の起票に使う GitHub App の認証情報（`RELEASE_BOT_CLIENT_ID` / `RELEASE_BOT_PRIVATE_KEY`）は、組織の Actions secrets に登録します。

## ローカル検証

正式な配布前は、次のコマンドで MSIX アセットを再生成し、各プラットフォームを検証できます。

```pwsh
.\scripts\Generate-MsixAssets.ps1
dotnet build PhotoMapStudio.slnx -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true
dotnet build PhotoMapStudio.slnx -c Release -p:Platform=ARM64 -p:GenerateAppxPackageOnBuild=true
```

正式なRelease資産の取得経路だけを確認する場合は、`Install-PhotoMapStudio.ps1 -Test` を使用できます。このオプションは証明書登録とインストールを行いません。自己署名証明書は本番の信頼チェーンを表すものではありません。

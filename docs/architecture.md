# アーキテクチャ・配布仕様

## 実行時の構成

- `src/PhotoMapStudio.App`: WinUI 3 の画面、設定、MSIX マニフェスト
- `src/PhotoMapStudio.Core`: EXIF 解析、タイル取得、キャッシュ、地図合成
- `tests/`: UI とドメインの単体テスト

アプリプロジェクトは `SelfContained=true` と `WindowsAppSDKSelfContained=true` を使用します。配布対象は x64 と ARM64 の個別 MSIX で、`AppxBundle=Never` によりアーキテクチャ混在のバンドルは作成しません。

地図合成の既定ピンは `Assets/MapPins/green_pin.png` です。空の設定は実行時にMSIX内のこのパスへ解決するため、パッケージ更新でインストール先が変わっても古い絶対パスを保存しません。`blue_pin.png` と `red_pin.png` も同梱し、ユーザー指定のピン画像として利用できます。

## リリースフロー

`v<major>.<minor>.<patch>` タグを push すると、`.github/workflows/release.yml` が次の処理を行います。

1. x64 / ARM64 を並列に self-contained ビルドする
2. 生成した MSIX に GitHub Secrets の証明書で署名する
3. 各アーキテクチャ用の App Installer XML とインストールスクリプトを生成する
4. 署名済み MSIX、証明書チェーン用 `.cer`、App Installer、スクリプトを GitHub Release に添付する

App Installer の `MainPackage` と `UpdateSettings` はタグ付き Release の資産を参照し、起動時の更新確認を有効にします。x64 と ARM64 の App Installer は異なるパッケージ URI を持つため、利用環境に合う方を選択してください。

## 署名

ローカルでは次のスクリプトで開発用コード署名証明書を作成できます。

```pwsh
.\scripts\New-SigningCertificate.ps1
```

リリースワークフローには、所有者が次の GitHub Secrets を登録します。

- `SIGNING_CERTIFICATE_BASE64`: PFX ファイルを Base64 化した値
- `SIGNING_CERTIFICATE_PASSWORD`: PFX のパスワード

証明書の秘密鍵とパスワードはリポジトリへコミットせず、ログにも出力しません。証明書の発行、Secret の登録、実際の `v0.1.0` タグ push と公開は、リリース所有者が確認して実行する作業です。

## ローカル検証

正式な配布前は、次のコマンドで MSIX アセットを再生成し、各プラットフォームを検証できます。

```pwsh
.\scripts\Generate-MsixAssets.ps1
dotnet build PhotoMapStudio.slnx -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true
dotnet build PhotoMapStudio.slnx -c Release -p:Platform=ARM64 -p:GenerateAppxPackageOnBuild=true
```

正式なRelease資産の取得経路だけを確認する場合は、`Install-PhotoMapStudio.ps1 -Test` を使用できます。このオプションは証明書登録とインストールを行いません。自己署名証明書は本番の信頼チェーンを表すものではありません。

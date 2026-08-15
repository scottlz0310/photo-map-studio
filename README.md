# PhotoMapStudio

写真の EXIF GPS 情報から、撮影地点を中心とした地図画像を生成・一括出力する Windows アプリケーションです。

`PhotoGeoExplorer` / `tacho-graph-studio` と同じトーンで配布する 3 点ツールスイートの 1 つとして開発しています。

> **開発状況**: 初版リリースに向けて実装中です。本リポジトリは [auto-map-generator](https://github.com/scottlz0310/auto-map-generator)（tkinter 実装）の後継であり、仕様と処理アルゴリズムのみを引き継いだ全面リライトです。移行の経緯は [auto-map-generator#10](https://github.com/scottlz0310/auto-map-generator/issues/10) を参照してください。

## 機能

- 入力フォルダ配下の写真から、EXIF GPS を持つものを列挙する
- 撮影地点を中心とした地図画像を生成し、ピンを合成する
- 出力サイズ・ズームレベル・ピン画像・タイルソースを設定できる
- 一括生成、進捗表示、キャンセル
- 生成前プレビュー（対象写真の切り替え、設定変更時の自動更新）
- 地図タイルのローカルキャッシュ

対応する入力形式: JPEG / TIFF / HEIC

## 地図タイルについて

タイルソースはプリセットから選択できます。生成した画像には出典表示を焼き込みます。

| プリセット | 提供元 | ズーム | 備考 |
| --- | --- | --- | --- |
| **地理院タイル（淡色）** | 国土地理院 | 5–18 | **既定**。日本国内のみ。[地理院タイル一覧](https://maps.gsi.go.jp/development/ichiran.html) |
| 地理院タイル（標準） | 国土地理院 | 5–18 | 日本国内のみ |
| OpenStreetMap | OpenStreetMap contributors | 0–19 | [Tile Usage Policy](https://operations.osmfoundation.org/policies/tiles/) に従い、取得を制限します |

任意のタイル URL も設定できます。

## コマンドライン連携

スイート連携用に、起動時の入力・出力フォルダを指定できます。既に起動中の場合は同じインスタンスへ引数を転送します。

```text
PhotoMapStudio.exe --input-dir "C:\Photos" --output-dir "C:\Maps"
```

既定を地理院タイル（淡色）にしているのは、実測（[#8](https://github.com/scottlz0310/photo-map-studio/issues/8)）で次の結果が出たためです。

- 一括生成は OpenStreetMap の Tile Usage Policy が禁じる bulk downloading に該当するが、地理院タイルにこの制限はない
- 写真 20 枚の一括生成が地理院で約 10 秒、OpenStreetMap で約 16 秒（レート制御を含む実測）
- 山間部では地理院タイルに等高線・登山路・地名が出るのに対し、OpenStreetMap はほぼ緑一色になる
- 淡色は背景の彩度が低く、ピンが最も見分けやすい

**日本国外の写真**は地理院タイルの配信範囲外です。プレビューと単発の生成では自動的に OpenStreetMap へ切り替えて生成し、その旨を結果に表示します。

**一括生成では自動切替を行いません。** OpenStreetMap への一括取得は [Tile Usage Policy](https://operations.osmfoundation.org/policies/tiles/) が禁じる bulk downloading に該当するためです。一括生成で国外の写真に当たった場合はスキップとして結果一覧に残るので、タイルソースを OpenStreetMap に切り替えて実行し直してください。

## 配布版のインストール

GitHub Releases では x64 / ARM64 向けの署名済み MSIX と、アーキテクチャ別の `.appinstaller` を提供します。Windows のアプリ インストーラーから `.appinstaller` を開くと、以後の更新確認を自動化できます。

PowerShell からインストールする場合は、Release から `Install-PhotoMapStudio.ps1` を保存して実行します。

このスクリプトは Windows PowerShell 5.1 でも実行できます。署名証明書が未登録の場合だけ、`LocalMachine\TrustedPeople` への登録のため UAC の管理者許可を求めます。AppX のインストールは起動元のユーザーとして行います。証明書を信頼済みルート（`Root`）へ登録する必要はありません。

```pwsh
Set-ExecutionPolicy -Scope Process Bypass
.\Install-PhotoMapStudio.ps1 -Architecture x64
```

ARM64 環境では `-Architecture ARM64` を指定してください。自己署名証明書を使った開発用パッケージでは、インストール前に証明書を信頼済みにする必要があります。リリース手順と署名Secretの設定は [アーキテクチャ・配布仕様](docs/architecture.md) を、データ取り扱いと地図タイルの通信先は [プライバシーポリシー](docs/privacy-policy.html) を参照してください。

## 開発

### 必要な環境

- .NET 10 SDK
- Visual Studio 2022 以降（MSIX パッケージングに MSBuild が必要）

### ビルドとテスト

```pwsh
dotnet restore PhotoMapStudio.slnx
dotnet build PhotoMapStudio.slnx -c Release -p:Platform=x64
dotnet test tests/PhotoMapStudio.Core.Tests
dotnet test tests/PhotoMapStudio.App.Tests
dotnet format PhotoMapStudio.slnx --verify-no-changes
```

### プロジェクト構成

| プロジェクト | 役割 |
| --- | --- |
| `src/PhotoMapStudio.App` | WinUI 3 の UI 層（XAML / ViewModel / MSIX マニフェスト） |
| `src/PhotoMapStudio.Core` | ドメイン層（EXIF 解析・タイル取得・地図合成）。UI 非依存 |
| `tests/PhotoMapStudio.Core.Tests` | ドメイン層の単体テスト |
| `tests/PhotoMapStudio.App.Tests` | ViewModel の単体テスト |

## ライセンス

[MIT](LICENSE)

## 使用ライブラリ

| ライブラリ | ライセンス | 用途 |
| --- | --- | --- |
| [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) | Apache-2.0 | EXIF GPS の読み取り |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | MIT | 地図タイルの合成・PNG 出力 |
| [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) | MIT | WinUI 3 |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT | MVVM |
| [CommunityToolkit.WinUI.Controls.Sizers](https://github.com/CommunityToolkit/Windows) | MIT | `GridSplitter` |
| [WinUIEx](https://github.com/dotMorten/WinUIEx) | MIT | ウィンドウ状態の永続化 |

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
| 地理院タイル（淡色 / 標準） | 国土地理院 | 5–18 | 日本国内のみ。[地理院タイル一覧](https://maps.gsi.go.jp/development/ichiran.html) |
| OpenStreetMap | OpenStreetMap contributors | 0–19 | [Tile Usage Policy](https://operations.osmfoundation.org/policies/tiles/) に従い、一括生成時は取得を制限します |

任意のタイル URL も設定できます。

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

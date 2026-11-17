# PhotoMapStudio.Rendering の公開手順

`PhotoMapStudio.Rendering`（`src/PhotoMapStudio.Rendering`）は、ベクタータイル（MVT）を SkiaSharp で 1 枚の画像として描く NuGet パッケージです。WinUI・Windows App SDK・`PhotoMapStudio.Core` に依存しないため、PhotoGeoExplorer など他のアプリから使えます。タイルの取得・デコードは [`PhotoMapStudio.Tiles`](releasing-tiles.md) が担い、Rendering はそれに依存します。アプリ（MSIX）のリリース（`vX.Y.Z`、[architecture.md](architecture.md)）とは独立に、このページの手順で公開します。

| 項目 | 内容 |
| --- | --- |
| 公開先 | nuget.org |
| 認証 | Trusted Publishing（GitHub Actions の OIDC。長期の API キーは使わない） |
| タグ | `rendering-vX.Y.Z`（先行版は `rendering-vX.Y.Z-rc.1` のように `-` と識別子を付ける） |
| ワークフロー | `.github/workflows/publish-rendering.yml`（タグの push で起動） |
| 版 | SemVer。`0.x` の間は、互換性を壊す変更で minor を上げる。最初の公開は `0.1.0`（先行版の rc は使わない） |

設計の記録は [vector-map-architecture.md](vector-map-architecture.md) の §2.3（パッケージ構成）・§3.4（表示範囲の描画 API）にあります。

## Tiles への依存の版

Rendering の nupkg は、`PhotoMapStudio.Tiles` への依存を `>= <Tiles の csproj の既定の Version>`（現在は `0.1.0`）で宣言します。

- Rendering の版は、`-p:Version` ではなく **`-p:RenderingPackageVersion`** で渡します（ワークフローがタグから渡します）。`-p:Version` は、グローバルプロパティとして参照先の Tiles にも流れ、nupkg が宣言する Tiles への依存が、存在しない版になってしまうためです。
- **Tiles を公開したら**、`src/PhotoMapStudio.Tiles/PhotoMapStudio.Tiles.csproj` の既定の `<Version>` を、公開した版へ更新してください（Rendering が、公開済みの Tiles の新しい版を前提にする場合）。更新しなくても、Rendering の依存は古い最小の版のままで、Tiles の新しい版でも動きます。ただし、Rendering が Tiles の新しい API を使うようになったときは、更新しないと、最小の版では動かないパッケージになります。
- `pack` ジョブは、nupkg が宣言する Tiles への依存の版が **nuget.org に公開済みであること**を検査します（`scripts/Test-RenderingPackage.ps1 -CheckNuGet`）。公開されていなければ、公開の前に失敗します。

## 事前設定（初回だけ）

次の 3 つを、リポジトリの所有者が設定します。どれが欠けても、ワークフローは公開（nuget.org への push）の前に止まります。環境 `nuget` と変数 `NUGET_USER` は、[Tiles の公開手順](releasing-tiles.md#事前設定初回だけ)で設定済みなら、そのまま使えます（確認だけします）。

1. **GitHub の環境 `nuget`**: Required reviewers に承認者が設定されていること（設定すると、タグを push しても、承認するまで `publish` ジョブは始まらない）。`publish` ジョブは、最初の step で承認者の設定を検査し、設定されていなければ、OIDC ログインと push の前に失敗する。
2. **Actions の変数 `NUGET_USER`**: nuget.org の**プロフィール名**（メールアドレスではない）が設定されていること。
3. **nuget.org の Trusted Publishing ポリシー（Rendering 用に新しく追加する）**: nuget.org にログインし、ユーザー名 → Trusted Publishing でポリシーを追加する。**Tiles のポリシーは流用できません**（ポリシーは、ワークフローのファイル名とパッケージに結び付くため）。

   | 項目 | 値 |
   | --- | --- |
   | Repository Owner | `scottlz0310` |
   | Repository | `photo-map-studio` |
   | Workflow File | `publish-rendering.yml`（ファイル名のみ） |
   | Environment | `nuget` |

   各値は大文字小文字を区別しない。スコープ（新規パッケージの公開・既存パッケージの新バージョンの公開）は、`PhotoMapStudio.Rendering` を対象に、両方を許可する（`PhotoMapStudio.Rendering` は新しいパッケージ ID なので、初回は「新規パッケージの公開」が要る）。

## 公開の手順

1. 公開する変更が `main` にマージされていることを確認する。タグを付けるコミットは `main` に含まれている必要がある（ワークフローが検証し、含まれていなければ失敗する）。
2. `CHANGELOG.md` の `[Unreleased]` にある、パッケージに関する変更を確認する。
3. **公開前の検証**（初回の公開、または API を変えた公開の前）: ローカルで `dotnet pack` した nupkg を、PhotoGeoExplorer の使い捨ての worktree（コミットしない）で、ローカルのフォルダーのフィードから `PackageReference` で取り込み、警告 0・エラー 0 でビルドできること、Mapsui の描画と PNG 保存が動くこと（Skia 系のパッケージを同じ版で明示したうえで）を確認する。公開後に API の不備が見つかって 0.1.1 を出すことを避けるため。

   ```shell
   dotnet pack src/PhotoMapStudio.Rendering/PhotoMapStudio.Rendering.csproj -c Release -p:RenderingPackageVersion=0.1.0 -o <ローカルのフィードのフォルダー>
   ```

4. **タグの push は、リポジトリの所有者の許可を得てから行う。** `main` のコミットにタグを付けて push する。

   ```shell
   git tag rendering-v0.1.0 <main のコミット>
   git push origin rendering-v0.1.0
   ```

5. Actions の `Publish Rendering Package` が起動する。`pack` ジョブは、タグを検証し、テストと依存の検査（WinUI・Windows App SDK・Core へ依存しないこと、SkiaSharp は宣言した版だけであること、nupkg が宣言する依存が Tiles と SkiaSharp だけで、Tiles への依存の版が nuget.org に公開済みであること）をして、タグの版で nupkg を作る。
6. `publish` ジョブが、環境 `nuget` の承認待ちになる。`pack` の結果（版・依存）を確認して承認する。
7. nuget.org に `PhotoMapStudio.Rendering` の新しい版が現れることを確認する（検証のため、反映に数分かかることがある）。

同じ版は公開し直せません。公開が失敗したときの再実行は、同じ run の「Re-run failed jobs」で行います。版が既に公開されている場合は失敗します（`--skip-duplicate` は、公開済みの版の見落としを避けるため使っていません）。先行版（rc）は使いません。予行演習が要る場合は、事前にリポジトリの所有者と合意してください（先行版も nuget.org に残り、削除はできず、非表示にするだけです）。

## 公開が必要かの判断

アプリのリリース準備（`Prepare Release`）の `package-check` ジョブ（`PhotoMapStudio.Tiles` と共通）が、最新の正式版のタグ（`rendering-vX.Y.Z`。`-rc.N` などの先行版は除く）から HEAD までの `src/PhotoMapStudio.Rendering` の変更を調べ、公開が要りそうなときに警告（アノテーションとジョブのサマリー）を出します。正式版のタグが無い間は、「未公開」の警告が毎回出ます。ローカルでは次のコマンドで同じ確認ができます。警告だけで、リリース準備は止めず、公開もしません。

```shell
pwsh -File .\scripts\Test-PackageUnpublishedChanges.ps1 -PackageId PhotoMapStudio.Rendering -PackagePath src/PhotoMapStudio.Rendering -TagPrefix rendering-v -ReleaseGuide docs/development/releasing-rendering.md
```

アプリ（`vX.Y.Z`）の版とパッケージの版は連動させません。アプリ側は `ProjectReference` で常に最新のコードを使うため、パッケージの公開が遅れてもアプリには影響しません。影響するのは、パッケージを参照する他のアプリ（PhotoGeoExplorer など）だけです。

## 利用側（PhotoGeoExplorer など）

```xml
<PackageReference Include="PhotoMapStudio.Rendering" Version="0.1.0" />

<!-- Skia 系のパッケージは、Rendering と同じ版で明示する（別のライブラリが古い SkiaSharp を引くため） -->
<PackageReference Include="SkiaSharp" Version="4.156.0" />
<PackageReference Include="SkiaSharp.Views.WinUI" Version="4.156.0" />
<PackageReference Include="SkiaSharp.HarfBuzz" Version="4.156.0" />
<PackageReference Include="SkiaSharp.NativeAssets.WinUI" Version="4.156.0" />
<PackageReference Include="SkiaSharp.NativeAssets.Win32" Version="4.156.0" />
```

使い方は、[パッケージの README](../../src/PhotoMapStudio.Rendering/README.md) を参照してください。

## 確認できていないこと

- **新規パッケージ ID の初回公開で、Trusted Publishing が使えるか。** [Tiles の手順](releasing-tiles.md#確認できていないこと)と同じ未確認事項です。Tiles を先に公開して確認済みなら、その結果に従ってください。
- **ポリシーの有効化。** ポリシーは作成直後に「仮の有効状態」になることがあり（通常はプライベートリポジトリ）、その場合は 7 日以内に最初の公開が成功しないと無効になります。
- このワークフローは、タグを push するまで一度も実行されていません。

## 参考

- [Trusted publishing（Microsoft Learn）](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
- [NuGet/login（OIDC で一時 API キーを取得するアクション）](https://github.com/NuGet/login)

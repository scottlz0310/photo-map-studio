# PhotoMapStudio.Tiles の公開手順

`PhotoMapStudio.Tiles`（`src/PhotoMapStudio.Tiles`）は、タイルの取得・キャッシュ・レート制御・デコードを担う NuGet パッケージです。SkiaSharp・WinUI・Windows App SDK に依存しないため、PhotoGeoExplorer など他のアプリから使えます。アプリ（MSIX）のリリース（`vX.Y.Z`、[architecture.md](architecture.md)）とは独立に、このページの手順で公開します。

| 項目 | 内容 |
| --- | --- |
| 公開先 | nuget.org |
| 認証 | Trusted Publishing（GitHub Actions の OIDC。長期の API キーは使わない） |
| タグ | `tiles-vX.Y.Z`（先行版は `tiles-vX.Y.Z-rc.1` のように `-` と識別子を付ける） |
| ワークフロー | `.github/workflows/publish-tiles.yml`（タグの push で起動） |
| 版 | SemVer。API が安定するまでは `0.x` で始める（`0.x` の間は、互換性を壊す変更で minor を上げる） |

## 事前設定（初回だけ）

次の 3 つを、リポジトリの所有者が設定します。どれが欠けても、ワークフローは公開（nuget.org への push）の前に止まります。

1. **GitHub の環境 `nuget`**: Settings → Environments → New environment で `nuget` を作り、Required reviewers に承認者を設定する。設定すると、タグを push しても、承認するまで `publish` ジョブは始まらない（誤ったタグでの公開を防ぐ）。
   - GitHub は、存在しない環境を、保護ルールなしで自動作成する。そのため `publish` ジョブは、最初の step で環境 `nuget` に承認者が設定されていることを検査し、設定されていない（または取得できない）場合は、OIDC ログインと push の前に失敗する。検査するのは「承認者が設定されていること」だけで、承認者の適切さ（自己承認の可否など）は見ない。
2. **Actions の変数 `NUGET_USER`**: Settings → Secrets and variables → Actions → Variables に、nuget.org の**プロフィール名**（メールアドレスではない）を設定する。
3. **nuget.org の Trusted Publishing ポリシー**: nuget.org にログインし、ユーザー名 → Trusted Publishing でポリシーを追加する。

   | 項目 | 値 |
   | --- | --- |
   | Repository Owner | `scottlz0310` |
   | Repository | `photo-map-studio` |
   | Workflow File | `publish-tiles.yml`（ファイル名のみ） |
   | Environment | `nuget` |

   各値は大文字小文字を区別しない。スコープ（新規パッケージの公開・既存パッケージの新バージョンの公開）は、`PhotoMapStudio.Tiles` を対象に、両方を許可する。

## 公開の手順

1. 公開する変更が `main` にマージされていることを確認する。タグを付けるコミットは `main` に含まれている必要がある（ワークフローが検証し、含まれていなければ失敗する）。
2. `CHANGELOG.md` の `[Unreleased]` にある、パッケージに関する変更を確認する。
3. `main` のコミットにタグを付けて push する。

   ```shell
   git tag tiles-v0.1.0 <main のコミット>
   git push origin tiles-v0.1.0
   ```

4. Actions の `Publish Tiles Package` が起動する。`pack` ジョブは、タグを検証し、テストと依存の検査（描画・UI のパッケージへ推移的にも依存していないこと）をして、タグの版で nupkg を作る。
5. `publish` ジョブが、環境 `nuget` の承認待ちになる。`pack` の結果（版・依存）を確認して承認する。
6. nuget.org に `PhotoMapStudio.Tiles` の新しい版が現れることを確認する（検証のため、反映に数分かかることがある）。

同じ版は公開し直せません。公開が失敗したときの再実行は、同じ run の「Re-run failed jobs」で行います。版が既に公開されている場合は失敗します（`--skip-duplicate` は、公開済みの版の見落としを避けるため使っていません）。

## 利用側（PhotoGeoExplorer など）

```xml
<PackageReference Include="PhotoMapStudio.Tiles" Version="0.1.0" />
```

配信元へは、既定では `PhotoMapStudio/<版>` の User-Agent で通信します。**他のアプリが使うときは、自分の名称と連絡先で表明するため**、`HttpClient` の既定ヘッダーに User-Agent を設定します（設定されていれば、それが使われます）。

```csharp
services.AddHttpClient(HttpTileClient.HttpClientName, client =>
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MyApp/1.0 (+https://example.com/myapp)"));
```

## 確認できていないこと

- **新規パッケージ ID の初回公開で、Trusted Publishing が使えるか。** nuget.org の公式ドキュメントには、パッケージがまだ存在しないときの扱いは書かれていません。ポリシーのスコープに「新規パッケージの公開」を許可する項目があるため使える見込みですが、初回の公開で確かめる必要があります。認証の失敗で公開できなかった場合の代替（初回だけ API キーで公開するなど）は、リポジトリの所有者が判断してください。
- **ポリシーの有効化。** ドキュメントによると、ポリシーは作成直後に「仮の有効状態」になることがあり（通常はプライベートリポジトリ）、その場合は 7 日以内に最初の公開が成功しないと無効になります。無効になっても、あとで再開できます。
- このワークフローは、タグを push するまで一度も実行されていません。初回の公開の前に、先行版のタグ（例: `tiles-v0.1.0-rc.1`）で、`pack` から `publish` までの動作を確認することを勧めます（先行版も nuget.org に残ります。削除はできず、非表示にするだけです）。

## 参考

- [Trusted publishing（Microsoft Learn）](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
- [NuGet/login（OIDC で一時 API キーを取得するアクション）](https://github.com/NuGet/login)

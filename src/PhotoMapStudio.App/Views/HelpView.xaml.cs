using System.Diagnostics.CodeAnalysis;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.App.Views;

/// <summary>追加の描画依存を使わず、テーマとキーボード操作に追従するヘルプ。</summary>
[ExcludeFromCodeCoverage]
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "XAMLが生成するUserControl。")]
public sealed partial class HelpView : UserControl
{
    private static readonly string[] Titles = ["クイックスタート", "フォルダ設定", "出力ファイル名", "生成オプション", "プレビュー", "一括生成と結果", "地図タイルと利用条件", "コマンドライン", "トラブルシューティング", "バージョン情報"];

    /// <summary>ヘルプの章ナビゲーションを構築する。</summary>
    public HelpView()
    {
        this.InitializeComponent();
        for (int index = 0; index < Titles.Length; index++)
        {
            this.Navigation.MenuItems.Add(new NavigationViewItem { Content = Titles[index], Tag = index });
        }

        this.Navigation.SelectedItem = this.Navigation.MenuItems[0];
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: int index }) { return; }
        this.Chapter.Children.Clear();
        var heading = new TextBlock { Text = Titles[index], FontSize = 24, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(heading, Titles[index]);
        this.Chapter.Children.Add(heading);
        switch (index)
        {
            case 0:
                this.AddText("1. 入力フォルダを指定します。必要なら『下位フォルダも探索する』を有効にします。\n2. 出力先に『.』を指定すると写真と同じフォルダに保存します。\n3. プレビューで位置・サイズ・ズームを確認します。\n4. 一括生成を押し、進捗と結果を確認します。");
                this.AddNotice("初回はタイル取得が必要です。まず100枚程度で確認し、1,000枚、5,000枚へ段階的に増やしてください。");
                break;
            case 1:
                this.AddText("再帰探索の既定はOFFです。ONでは入力からの相対パス順に処理し、リンク／ジャンクションとシステムフォルダを除外します。Dropboxなどのクラウド同期ファイルは対象です。読み取れないフォルダはERRORとして記録して続行します。\n絶対パス（UNCを含む）は全写真の共通出力先です。相対パスは各写真のフォルダを基準に解決します。『..』で外へ出る指定、ルート相対、ドライブ相対は指定できません。");
                this.AddCode("入力\n写真/\n├─ 春/IMG_0001.jpg\n└─ 秋/IMG_0001.jpg\n\n出力先: maps、先頭文字: map_、末尾文字: _z16\n写真/\n├─ 春/\n│  ├─ IMG_0001.jpg\n│  └─ maps/map_IMG_0001_z16.png\n└─ 秋/\n   ├─ IMG_0001.jpg\n   └─ maps/map_IMG_0001_z16.png");
                this.AddText("『.』は写真と同じ場所、『maps』は写真のフォルダ内のmaps、『out\\map』はその下のmapに保存します。GPSなしでスキップするフォルダには相対出力フォルダを作りません。");
                break;
            case 2:
                this.AddText("出力名は『先頭文字 + 元写真の拡張子なしの名前 + 末尾文字 + .png』です。既定は先頭文字なし、末尾文字は_mapです。設定欄に入力した名前の例が表示されます。");
                this.AddCode("IMG_0001.jpg → IMG_0001_map.png\n先頭: map_ / 末尾: _z16 → map_IMG_0001_z16.png");
                this.AddNotice("\\ / : * ? \" < > | と制御文字は使えません。写真と同じ場所に保存する場合は付加文字を指定してください。出力名が255文字を超える写真はERRORになります。");
                break;
            case 3:
                this.AddText($"既定サイズ: {MapCompositionRequest.DefaultWidth}×{MapCompositionRequest.DefaultHeight}px、ズーム: {MapCompositionRequest.DefaultZoom}。ズームを上げるほど狭い範囲を詳しく表示します。ピンはPNGを指定できます。未指定では同梱の緑ピンを使用します。");
                foreach (TileSource source in new[] { TileSources.GsiPale, TileSources.GsiStandard, TileSources.OpenStreetMap })
                {
                    this.AddText($"{source.Name}: ズーム {source.MinZoom}〜{source.MaxZoom}\n{source.Attribution}");
                }
                this.AddText("カスタムはhttp(s)のURLテンプレートと出典を指定します。認証情報をURLへ埋め込まないでください。設定とエラー表示にはURLが含まれます。");
                this.AddCode("https://example.com/{z}/{x}/{y}.png");
                break;
            case 4:
                this.AddText("写真を列挙したあと、GPS付き写真を見つけた順に一覧へ追加し、最初の1枚で地図を表示します。一覧は相対パスで同名写真を区別します。対象・ズーム・サイズの変更で再生成します。\n読み込みをキャンセルしても見つかった候補と生成済み画像は残ります。写真を選び直すとプレビューを使えます。国外で地理院タイルが配信されていない場合は、対話プレビューだけOSMへ切り替えます。");
                break;
            case 5:
                this.AddText("A: 列挙では走査済みフォルダ数と検出写真数を表示します（総数未確定）。\nB: プレビュー一覧では確認済み／全写真数、GPSあり件数を表示します。\nC: 一括生成では処理済み／全写真数、現在の相対パス、成功・スキップ・エラー件数と経過時間を表示します。\n出力の衝突は最終パスで判定し、生成前に全体を中止します。絶対出力＋再帰では別フォルダの同名写真も衝突します。");
                this.AddStatusTable();
                this.AddNotice("キャンセルで完成済みの画像は削除しません。書き込み途中の.tmpは片付けます。再実行は最初から上書き生成し、生成済みの画像をスキップする再開には対応していません。");
                break;
            case 6:
                this.AddText("地理院タイルは日本国内用です。一括生成では国外への自動切替を行いません。画像には配信元の出典を焼き込みます。地理院の利用規約と各タイルの利用手続を確認してください。取得速度の数値上限・大量アクセスの事前連絡条件は、今回確認した一覧・規約・仕様の本文では見つかりませんでした（2026-10-05確認）。");
                this.AddNotice("OSM公式サーバーは対話プレビューに使用します。一括生成には、一括取得・画像保存を許可するOSM系配信元をカスタム設定で指定してください。公式OSMのbulk downloading禁止は直列取得や減速でも解除されません。");
                this.AddText($"カスタム一括取得は1接続・1秒間隔です。同じホストへのプレビューも一括実行中は共有の間隔に揃えます。100枚超は開始前に確認します。ただし、この PC 上の配信元（localhost、127.0.0.0/8、::1）は他者のサーバーではないため、8接続・5ミリ秒間隔で行い、100枚超の確認も省略します。LAN やプライベート IP は対象外です。ループバックの先が他者のサーバーへの転送（SSH のポートフォワードなど）の場合も同じ間隔になるため、その場合は使わないでください。404以外の取得失敗が連続{TileFetchSession.FailureLimit}件になると中止し、自動再試行はしません。Retry-Afterは利用者が再実行時期を判断するため表示します。\nタイルのネットワーク取得／キャッシュ命中、開始からの平均レート／直近1秒窓の最大要求数を表示します。キャッシュはURLテンプレートとズームごとに分かれ、既定30日保持します。キャッシュ命中は減速しません。ただし期限切れやキャッシュ削除後は再取得します。ズームや配信元を変える場合も再取得が必要です。\n既定の800×600では1写真あたり12〜20タイル程度です。5,200枚が全て異なるタイルなら約6〜10万要求ですが、撮影地点が狭い範囲ならタイルを再利用できます。");
                this.AddLink("地理院タイル一覧", "https://maps.gsi.go.jp/development/ichiran.html");
                this.AddLink("国土地理院コンテンツ利用規約", "https://www.gsi.go.jp/kikakuchousei/kikakuchousei40182.html");
                this.AddLink("OSM Tile Usage Policy", "https://operations.osmfoundation.org/policies/tiles/");
                break;
            case 7:
                this.AddText("パスに空白がある場合は引用符で囲みます。--output-dirの相対指定も写真のフォルダ基準です。起動中のインスタンスに設定を転送します。再帰探索と付加文字は画面で設定してください。");
                this.AddCode("PhotoMapStudio.exe --input-dir \"C:\\写真 (2026)\" --output-dir maps");
                break;
            case 8:
                this.AddText("GPSなし: 写真に位置情報があるか確認してください。\nタイル取得失敗: 配信元・HTTPステータス・Retry-Afterを確認し、時間をおいて再実行してください。\n出力衝突: 相対出力に変更するか写真の名前を変更します。\n出力先作成失敗: 保存先の権限とネットワーク接続を確認します。相対出力の失敗は写真単位でERRORにして続行し、絶対出力の作成失敗は開始前に中止します。\n長いパス: パッケージとWindows環境の長いパス対応を確認してください。読み書きに失敗した写真はERRORになります。");
                this.AddText("読み取り・生成・保存のエラーはローカル診断ログに記録します。時刻・処理段階・番号・アプリ版・例外型・HResult・呼び出しメソッドを保存し、写真・GPS・パス・URL・例外メッセージは含めず、外部送信もしません。現行と直前のログを各1MiBまで保持します。番号0は写真番号を持たない処理です。ログ保存に失敗した場合も画面へエラーを表示します。");
                this.AddCode($"%LOCALAPPDATA%\\Packages\\{Windows.ApplicationModel.Package.Current.Id.FamilyName}\\LocalState\\diagnostics");
                break;
            case 9:
                Windows.ApplicationModel.PackageVersion version = Windows.ApplicationModel.Package.Current.Id.Version;
                this.AddText($"PhotoMapStudio {version.Major}.{version.Minor}.{version.Build}.{version.Revision}");
                this.AddLink("README", "https://github.com/scottlz0310/photo-map-studio#readme");
                this.AddLink("Issues", "https://github.com/scottlz0310/photo-map-studio/issues");
                this.AddLink("プライバシーポリシー", "https://scottlz0310.github.io/photo-map-studio/privacy-policy.html");
                break;
        }
    }

    private void AddText(string text) => this.Chapter.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
    private void AddNotice(string text) => this.Chapter.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning, Message = text });
    private void AddCode(string text) => this.Chapter.Children.Add(new TextBox { AcceptsReturn = true, Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), Header = "例（選択してCtrl+Cでコピー）" });
    private void AddLink(string label, string uri) => this.Chapter.Children.Add(new HyperlinkButton { Content = label, NavigateUri = new Uri(uri) });
    private void AddStatusTable()
    {
        var table = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.ColumnDefinitions.Add(new ColumnDefinition());
        string[] labels = ["状態", "SUCCESS", "SKIP", "ERROR", "CANCELLED"];
        string[] meanings = ["意味", "PNGを保存しました。", "GPSなし・配信範囲外です。", "読み取り・取得・保存に失敗しました。", "利用者が中断しました。作成済み画像は残ります。"];
        for (int row = 0; row < labels.Length; row++)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = labels[row] };
            var meaning = new TextBlock { Text = meanings[row], TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(label, row); Grid.SetRow(meaning, row); Grid.SetColumn(meaning, 1);
            table.Children.Add(label); table.Children.Add(meaning);
        }
        this.Chapter.Children.Add(table);
    }
}

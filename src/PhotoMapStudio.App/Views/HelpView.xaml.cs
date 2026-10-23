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
                this.AddText("進捗ログの『スキップ・エラーの実行履歴』で実行を選ぶと、入力フォルダと問題の相対パス・理由を確認できます。ファイル名や理由は選択してコピーできます。完了・キャンセル・途中中止までの確定結果を端末内に自動保存し、直近30回を保持します。アプリの再起動後も履歴を確認できます。");
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
                this.AddText($"既定サイズ: {MapCompositionRequest.DefaultWidth}×{MapCompositionRequest.DefaultHeight}px、ズーム: {MapCompositionRequest.DefaultZoom}。ズームを上げるほど狭い範囲を詳しく表示します。");
                this.AddText("ピン形状: 緑ピン・赤ピン・青ピン（同梱）、二重円ターゲット、十字ターゲット（測量風）、カスタム画像から選びます。カスタム画像はPNGのパスを指定し、未指定では既定のピンを使用します。\nアンカー基準点（カスタム画像のとき）: 『下端中央』はピンの先端が、『中央』は図心やターゲットの中心が撮影地点になります。同梱のピンとターゲットは形に合わせて固定です。");
                this.AddText("キャッシュ容量上限（MB）の既定は1024MBです。取得済みのタイルがこの上限を超えると、最近使われていないものから削除します。『キャッシュ消去』ですべて削除します。使用量は設定欄に表示します。");
                foreach (TileSource source in TileSources.All)
                {
                    this.AddText($"{source.Name}: ズーム {source.MinZoom}〜{source.MaxZoom}\n{source.Attribution}");
                }
                this.AddText("カスタムはhttp(s)のURLテンプレートと出典を指定します。認証情報をURLへ埋め込まないでください。設定とエラー表示にはURLが含まれます。");
                this.AddCode("https://example.com/{z}/{x}/{y}.png");
                this.AddText("カスタム（ベクター）は、利用者が用意したベクタータイルを直接描画します。場所には、この PC 上のPMTilesファイル（.pmtiles。通信しません）、PMTilesのURL（https://…/….pmtiles）、または{z}/{x}/{y}を含むベクタータイルのURL（MVT / PBF）を指定します。URLテンプレートでは、配信する最大ズーム（0〜19、既定14）も指定してください。PMTilesはヘッダーから自動で分かります。出典表示は必須で、生成画像に焼き込みます。認証情報をURLへ埋め込まないでください。");
                this.AddNotice("レイヤー構成（スキーマ）は、配信元のデータに合わせて『OpenMapTiles』か『国土地理院 最適化ベクトル』を選びます。データと異なるスキーマを選ぶと、エラーにはならず背景だけの地図になります。配信元の利用条件と帰属表示は利用者が確認してください。");
                this.AddText("OpenMapTilesスキーマの地図では、高速道路・国道・都道府県道の経路番号の盾と道路名を描画します。");
                break;
            case 4:
                this.AddText("写真を列挙したあと、GPS付き写真を見つけた順に一覧へ追加し、最初の1枚で地図を表示します。一覧は相対パスで同名写真を区別します。対象・ズーム・サイズの変更で再生成します。\n読み込みをキャンセルしても見つかった候補と生成済み画像は残ります。写真を選び直すとプレビューを使えます。地理院タイルが配信されていない国外などの場所（HTTP 404）では、対話プレビューだけOSMへ切り替えます。この切り替えでは撮影地点のタイル座標がOSMへ送られます。一括生成では切り替えません。\nカスタム（ベクター）のローカルPMTilesファイルは、収録範囲外でも外部へ切り替えず、地図を作れないエラーとして表示します。");
                break;
            case 5:
                this.AddText("A: 列挙では走査済みフォルダ数と検出写真数を表示します（総数未確定）。\nB: プレビュー一覧では確認済み／全写真数、GPSあり件数を表示します。\nC: 一括生成では処理済み／全写真数、現在の相対パス、成功・スキップ・エラー件数と経過時間を表示します。\n出力の衝突は最終パスで判定し、生成前に全体を中止します。絶対出力＋再帰では別フォルダの同名写真も衝突します。");
                this.AddStatusTable();
                this.AddNotice("キャンセルで完成済みの画像は削除しません。書き込み途中の.tmpは片付けます。再実行は最初から上書き生成し、生成済みの画像をスキップする再開には対応していません。");
                break;
            case 6:
                this.AddText("地理院タイルは日本国内用です。一括生成では国外への自動切替を行いません。画像には配信元の出典を焼き込みます。地理院の利用規約と各タイルの利用手続を確認してください。取得速度の数値上限・大量アクセスの事前連絡条件は、今回確認した一覧・規約・仕様の本文では見つかりませんでした（2026-10-05確認）。");
                this.AddNotice("OSM公式サーバーは対話プレビューに使用します。一括生成には、一括取得・画像保存を許可するOSM系配信元をカスタム設定で指定してください。公式OSMのbulk downloading禁止は直列取得や減速でも解除されません。");
                this.AddText("地理院タイル（最適化ベクトル）は国土地理院が提供する最適化ベクトルタイル（PMTiles）を HTTP Range 取得し、Core 内蔵エンジンにより直接描画します。等高線・道路縁・建物・公的注記を鮮明に描画します。\n【適用範囲と非保証事項】本スタイルによる描画は、写真台帳や調査報告書における『参考位置図・背景案内図』としての利用を想定したものです。国土地理院の最適化ベクトルタイルは測量法に基づく『基本測量成果』そのものとは位置付けられていないため、法的証明や基本測量成果自体の添付が厳密に義務付けられている用途（不動産登記・境界確定等）の証明能力を保証するものではありません。厳密な測量成果が必須の提出先に対しては、別途正規の測量成果交付データ等の確認・利用を案内します。");
                this.AddText("OpenFreeMap（ベクター）は OpenStreetMap データを基にしたパブリックベクタータイルです。Core 内蔵エンジンによりタイル境界の文字切れやにじみのない鮮明な地図を直接描画します。全世界の写真に対応します。利用規約は、まとまった取得を1回あたり3,000タイルまたは50MB以内・並列4以内とし、超える場合は運営への連絡を求めています。本アプリは1接続ずつ順に取得し、100枚超の一括生成は開始前に確認します。端末内にベクタータイルを自動キャッシュします。");
                this.AddText("一括生成での取得方式は配信元ごとに異なります。\n・地理院タイル（淡色・標準）: 最大4並列・50ミリ秒以上の間隔。確認は省略します。\n・地理院タイル（最適化ベクトル）: PMTilesを最大2並列で範囲取得します。確認は省略します。\n・OpenFreeMap（ベクター）: 1接続ずつ順に取得します。100枚超は開始前に確認します。\n・カスタム（URL）: 1接続・1秒間隔です。100枚超は開始前に確認します。同じホストへのプレビューも一括実行中は共有の間隔に揃えます。\n・カスタム（ベクター）のHTTP: 1接続ずつ125ミリ秒以上の間隔で取得します（PMTilesは範囲取得）。100枚超は開始前に確認します。\n・この PC 上の配信元（localhost、127.0.0.0/8、::1）: 8並列・5ミリ秒間隔で、確認は省略します。LANやプライベートIPは対象外です。ループバックの先が他者のサーバーへの転送（SSHのポートフォワードなど）の場合も同じ間隔になるため、その場合は使わないでください。\n・この PC 上のPMTilesファイル: 通信しません。");
                this.AddText($"404以外の取得失敗が連続{TileFetchSession.FailureLimit}件になると中止し、自動再試行はしません。Retry-Afterは利用者が再実行時期を判断するため表示します。\nタイルのネットワーク取得／キャッシュ命中、開始からの平均レート／直近1秒窓の最大要求数を表示します。この PC 上のファイルの読み取りは数えません。");
                this.AddText("取得したタイルは端末内にキャッシュし、既定で30日間保持します。期限切れやキャッシュ消去の後は再取得します。保存先は配信元ごとに分かれます（ラスターはURLテンプレートとズーム・座標、ベクタータイルは配信元の識別子とズーム・座標ごと）。OpenFreeMapは配信の版が更新されると、以前の版のキャッシュは使えず再取得します。合計が容量上限（既定1GB）を超えると、最近使われていないものから削除します。キャッシュ命中は通信せず、減速しません。ズームや配信元を変える場合は再取得が必要です。");
                this.AddText("既定の800×600では1写真あたり12〜20タイル程度です。5,200枚が全て異なるタイルなら約6〜10万要求ですが、撮影地点が狭い範囲ならタイルを再利用できます。");
                this.AddLink("地理院タイル一覧", "https://maps.gsi.go.jp/development/ichiran.html");
                this.AddLink("地理院 最適化ベクトルタイル（実験）", "https://github.com/gsi-cyberjapan/optimal_bvmap");
                this.AddLink("国土地理院コンテンツ利用規約", "https://www.gsi.go.jp/kikakuchousei/kikakuchousei40182.html");
                this.AddLink("OSM Tile Usage Policy", "https://operations.osmfoundation.org/policies/tiles/");
                this.AddLink("OpenFreeMap", "https://openfreemap.org");
                break;
            case 7:
                this.AddText("パスに空白がある場合は引用符で囲みます。--output-dirの相対指定も写真のフォルダ基準です。起動中のインスタンスに設定を転送します。再帰探索と付加文字は画面で設定してください。");
                this.AddCode("PhotoMapStudio.exe --input-dir \"C:\\写真 (2026)\" --output-dir maps");
                break;
            case 8:
                this.AddText("GPSなし: 写真に位置情報があるか確認してください。\nタイル取得失敗: 配信元・HTTPステータス・Retry-Afterを確認し、時間をおいて再実行してください。\n出力衝突: 相対出力に変更するか写真の名前を変更します。\n出力先作成失敗: 保存先の権限とネットワーク接続を確認します。相対出力の失敗は写真単位でERRORにして続行し、絶対出力の作成失敗は開始前に中止します。\n長いパス: パッケージとWindows環境の長いパス対応を確認してください。読み書きに失敗した写真はERRORになります。");
                this.AddText("読み取り・生成・保存のエラーはローカル診断ログに記録します。時刻・処理段階・番号・アプリ版・例外型・HResult・呼び出しメソッドを保存し、写真・GPS・パス・URL・例外メッセージは含めず、外部送信もしません。現行と直前のログを各1MiBまで保持します。番号0は写真番号を持たない処理です。ログ保存に失敗した場合も画面へエラーを表示します。");
                this.AddCode($"%LOCALAPPDATA%\\Packages\\{Windows.ApplicationModel.Package.Current.Id.FamilyName}\\LocalState\\diagnostics");
                this.AddText("実行履歴は診断ログとは別に、日時・入力フォルダ・問題の相対パス・結果・理由・集計を記録します。写真本体・GPS・タイルURL・生の例外メッセージは記録しません。正常保存後に古い履歴を整理して直近30回を残します。保存・読み込み・整理に失敗したら履歴欄に通知します。今回の結果は画面に残りますが、アプリ終了後の保存を保証するものではありません。保存先の権限・空き容量や読み取りエラーの内容を確認してください。");
                this.AddCode($"%LOCALAPPDATA%\\Packages\\{Windows.ApplicationModel.Package.Current.Id.FamilyName}\\LocalState\\batch-history");
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

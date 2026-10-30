using System;
using System.Collections.Generic;

namespace VInc.AvatarHub.UI
{
    public class GuideStep
    {
        public string text;
        /// <summary>Editor/Guide/Images/{image}.png（null なら画像なし）</summary>
        public string image;

        public GuideStep(string text, string image = null)
        {
            this.text = text;
            this.image = image;
        }
    }

    public class GuideTopic
    {
        public string id;
        public string group;
        /// <summary>「〜したい」「〜とは？」</summary>
        public string title;
        public string summary;
        public bool concept;
        public string[] intro = Array.Empty<string>();
        public string introImage;
        public List<GuideStep> steps = new List<GuideStep>();
        public string actionLabel;
        public Action<ViahWindow> action;
        public string[] related = Array.Empty<string>();
    }

    public class GuideGroup
    {
        public string id;
        public string title;
        public string icon;
        /// <summary>サイドバーのワークフロー（設定で非表示ならホームにも出さない）</summary>
        public string workflow;
        public string conceptId;
        public string[] topics;
    }

    /// <summary>
    /// ホームの「何をしたいですか？」とガイドの中身。
    /// 画像の番号（①②…）は Tests/Editor/GuideCapture.cs で付けている枠の番号と対応する。
    /// </summary>
    public static class GuideContent
    {
        public static readonly List<GuideGroup> Groups = new List<GuideGroup>
        {
            new GuideGroup
            {
                id = "project", title = "プロジェクト（フォルダー構成）", icon = "Folder Icon", workflow = "projects", conceptId = "project-about",
                topics = new[] { "project-new", "project-edit", "project-duplicate", "project-import", "project-share", "preset-edit" },
            },
            new GuideGroup
            {
                id = "profile", title = "プロファイル（書き出す中身）", icon = "Prefab Icon", workflow = "profiles", conceptId = "profile-about",
                topics = new[] { "profile-new", "profile-from-package", "profile-edit", "profile-duplicate", "profile-import", "profile-share" },
            },
            new GuideGroup
            {
                id = "export", title = "エクスポート（書き出し）", icon = "SaveAs", workflow = "export", conceptId = "export-about",
                topics = new[] { "export-single", "export-sequence" },
            },
            new GuideGroup
            {
                id = "import", title = "インポート（unitypackage を入れる）", icon = "Import", workflow = "import", conceptId = "import-about",
                topics = new[] { "import-sequence" },
            },
            new GuideGroup
            {
                id = "bookmark", title = "プロジェクトブックマーカー", icon = "Favorite", workflow = "bookmarks", conceptId = "bookmark-about",
                topics = new[] { "bookmark-use", "bookmark-share" },
            },
            new GuideGroup
            {
                id = "searcher", title = "アセットSearcher", icon = "Search Icon", workflow = "searcher", conceptId = "searcher-about",
                topics = new[] { "searcher-use", "searcher-prefab" },
            },
            new GuideGroup
            {
                id = "other", title = "そのほか", icon = "SettingsIcon", conceptId = null,
                topics = new[] { "search", "undo", "settings" },
            },
        };

        static List<GuideTopic> topics;

        public static List<GuideTopic> Topics => topics ??= Create();

        public static GuideTopic Find(string id) => Topics.Find(t => t.id == id);

        public static GuideGroup GroupOf(GuideTopic t) => Groups.Find(g => g.id == t.group);

        static List<GuideTopic> Create()
        {
            var list = new List<GuideTopic>();

            // ───────────── プロジェクト ─────────────

            list.Add(new GuideTopic
            {
                id = "project-about", group = "project", concept = true,
                title = "プロジェクトとは？",
                summary = "作品ごとのフォルダー階層を、決まった構成でまとめて作る機能です。",
                intro = new[]
                {
                    "プロジェクトとは、プロダクト（ギミック・衣装など）を作るときに使うフォルダー階層のまとまりです。",
                    "テクスチャ・マテリアル・モデル・アニメーションなどを置くフォルダーの並びを「プリセット」としてあらかじめ決めておき、名前を 1 つ入れるだけで一括で作成します。すぐに作業を始められ、Texture / Textures のような表記ゆれも起きません。",
                    "作ったプロジェクトにはタグ（日本語のキーワード）を付けられるので、英語の名前を忘れても上の検索欄から見つけられます。",
                },
                introImage = "project-new-3-plain",
                actionLabel = "プロジェクト作成を開く",
                action = w => w.Show(new ProjectsPage()),
            });

            list.Add(new GuideTopic
            {
                id = "project-new", group = "project",
                title = "プロジェクトを作りたい",
                summary = "名前を入れて、使うフォルダーにチェックするだけで一括作成。",
                steps =
                {
                    new GuideStep("左のサイドバーの ①「プロジェクト作成」を開き、②「＋ 新規」を押します。", "project-new-1"),
                    new GuideStep("③ 名前を入力します（Assets の下に作る、いちばん上のフォルダー名。英数字がおすすめ）。④ 作成場所はふつう Assets のままで OK。会社ごとにまとめたいときは親フォルダーを選びます（Project ウィンドウからドロップしても選べます）。", "project-new-2"),
                    new GuideStep("⑤ 必要なフォルダーにチェックを入れます。子にチェックすると親も自動で入ります。⑥ タグを入れておくと、あとで日本語で検索できます（Enter・読点「、」で区切り）。", "project-new-3"),
                    new GuideStep("⑦「フォルダーを作成」を押すと完成です。間違えたら Ctrl+Z で取り消せます（作ったフォルダーも消えます）。", "project-new-4"),
                },
                actionLabel = "新しいプロジェクトを作る",
                action = w => w.Show(new ProjectsPage(ProjectsPage.NewKey)),
                related = new[] { "preset-edit", "profile-new" },
            });

            list.Add(new GuideTopic
            {
                id = "project-edit", group = "project",
                title = "作成したプロジェクトを編集・修正したい",
                summary = "タグの変更や、あとから必要になったフォルダーの追加。",
                steps =
                {
                    new GuideStep("「プロジェクト作成」の一覧から ① 直したいプロジェクトを選びます（上の検索欄からも探せます）。② タグはその場で追加・削除でき、すぐ保存されます。", "project-edit-1"),
                    new GuideStep("③ あとから必要になったフォルダーにチェックを入れて ④「チェックしたフォルダーを作成」を押します。一覧にないフォルダー（例：Shaders）を足したいときは ⑤「構成を編集」で追加してから作成します。", "project-edit-1"),
                },
                actionLabel = "プロジェクト作成を開く",
                action = w => w.Show(new ProjectsPage()),
                related = new[] { "preset-edit" },
            });

            list.Add(new GuideTopic
            {
                id = "project-duplicate", group = "project",
                title = "プロジェクトを複製して修正したい",
                summary = "同じフォルダー構成・タグで、別の名前のプロジェクトを作る。",
                steps =
                {
                    new GuideStep("複製元のプロジェクトを開き、①「…」→「複製して新しいプロジェクトを作る」を選びます。", "project-dup-1"),
                    new GuideStep("② フォルダー構成とタグが引き継がれた新規作成画面になります（「複製元」の印が付きます）。③ 新しい名前を入れ、必要ならチェックを直して「フォルダーを作成」を押します。", "project-dup-2"),
                },
                actionLabel = "プロジェクト作成を開く",
                action = w => w.Show(new ProjectsPage()),
            });

            list.Add(new GuideTopic
            {
                id = "project-import", group = "project",
                title = "渡されたプロジェクトをインポートしたい",
                summary = "人からもらった .project.json を取り込んで、同じ構成のフォルダーを作る。",
                steps =
                {
                    new GuideStep("「プロジェクト作成」の一覧の上にある ① 読み込みボタンを押し、もらった .project.json を選びます（フォルダー構成のひな形 .preset.json も同じボタンで読み込めます）。", "project-import-1"),
                    new GuideStep("別の Unity プロジェクトで作られたものは、まだフォルダーがありません。② 元と同じ場所に作るか、③ 場所を選んで作成します。", "project-import-2"),
                },
                actionLabel = "プロジェクト作成を開く",
                action = w => w.Show(new ProjectsPage()),
                related = new[] { "project-share" },
            });

            list.Add(new GuideTopic
            {
                id = "project-share", group = "project",
                title = "プロジェクトを誰かに渡したい",
                summary = "フォルダー構成とタグを JSON ファイルに書き出して渡す。",
                steps =
                {
                    new GuideStep("渡したいプロジェクトを開き、①「JSONで渡す」を押して保存します。できた .project.json を相手に送ってください。", "project-share-1"),
                    new GuideStep("相手は「渡されたプロジェクトをインポートしたい」の手順で取り込めます。フォルダー構成のひな形（プリセット）だけを渡したいときは、新規作成画面のプリセットの「…」→「JSON に書き出す」を使います。"),
                },
                actionLabel = "プロジェクト作成を開く",
                action = w => w.Show(new ProjectsPage()),
                related = new[] { "project-import" },
            });

            list.Add(new GuideTopic
            {
                id = "preset-edit", group = "project",
                title = "フォルダー構成のひな形を作りたい・編集したい",
                summary = "いつも使うフォルダーの並び（プリセット）を自分で決めて保存。",
                steps =
                {
                    new GuideStep("新規作成画面の「フォルダー構成」で ①「編集」を押します。新しく作るときや複製・読み込み・書き出しは ②「…」から。", "preset-edit-1"),
                    new GuideStep("③ ボタンでフォルダーの追加・並べ替え（↑↓）・階層の変更（←→）・名前変更・削除ができます。④ 行の左のつまみをドラッグしても動かせます（行の上下に落とすと前後へ、真ん中に落とすとその中へ）。⑤ 番号を付けると、Unity の Project ウィンドウでもこの順番に並びます。", "preset-edit-2"),
                    new GuideStep("最後に「保存」。名前を付けて保存しておけば、新規作成画面のプリセット一覧からいつでも呼び出せます。"),
                },
                actionLabel = "新規作成画面を開く",
                action = w => w.Show(new ProjectsPage(ProjectsPage.NewKey)),
            });

            // ───────────── プロファイル ─────────────

            list.Add(new GuideTopic
            {
                id = "profile-about", group = "profile", concept = true,
                title = "プロファイルとは？",
                summary = "「この unitypackage には何を入れるか」の登録です。",
                intro = new[]
                {
                    "プロファイルとは「この unitypackage には何を入れるか」を登録したものです。",
                    "一度登録すれば、デバッグや修正のたびに書き出しても、余計なファイル（アバター本体・他の人の素材など）が混ざったり、必要なファイルを入れ忘れたりしません。",
                    "自動登録では、プレハブをドロップするだけで、アニメーター（MA Merge Animator なども）からたどって、使っているアニメーション・マテリアル・テクスチャなどを自動でチェックします。",
                    "「基準フォルダー」（ふつうはプロジェクトのフォルダー）の外にあるファイルは「外部」として扱い、最初はチェックを外します。",
                },
                introImage = "profile-new-3-plain",
                actionLabel = "プロファイル登録を開く",
                action = w => w.Show(new ProfilesPage()),
            });

            list.Add(new GuideTopic
            {
                id = "profile-new", group = "profile",
                title = "プロファイルを作りたい",
                summary = "プレハブをドロップすると、使っているファイルを自動でチェック。",
                steps =
                {
                    new GuideStep("「プロファイル登録」で「＋ 新規」を押します。① どのプロジェクトのものかを選び、② 登録のしかた（おすすめは自動登録）を選んで ③「次へ」。", "profile-new-1"),
                    new GuideStep("④ ギミックのプレハブ（またはプレハブが入ったフォルダー）を Project ウィンドウからドロップし、⑤「自動検出を開始」を押します。", "profile-new-2"),
                    new GuideStep("⑥ 使っているファイルに自動でチェックが付きます。クリックで 1 つずつ、Shift+クリックで範囲をまとめて切り替え。フォルダーのチェックは「今後増えるファイルも含めて全部」の意味です。⑦ 表示を「選択中」「外部」などで絞り込めます。⑧ 印の意味はここに出ています。", "profile-new-3"),
                    new GuideStep("⑨ 名前（そのまま unitypackage の名前になります）とタグを入れて、⑩「保存して完了」を押します。", "profile-new-4"),
                },
                actionLabel = "新しいプロファイルを作る",
                action = w => w.Show(new ProfileWizardPage(null)),
                related = new[] { "export-single" },
            });

            list.Add(new GuideTopic
            {
                id = "profile-from-package", group = "profile",
                title = "UnityPackage からプロファイルを作りたい",
                summary = "手動で作った unitypackage を選ぶだけで、同じ中身のプロファイルを作る。",
                intro = new[]
                {
                    "今まで手で作っていた unitypackage を選ぶと、入っていたファイルをそのままプロファイルとして登録します。Avatar Hub に移すときに、中身を 1 つずつ選び直す必要はありません。",
                    "インポートしたあとに移動・名前変更したファイルも、GUID（Unity の内部 ID）で追いかけて見つけます。まだインポートしていなければ、自動でインポートしてから作ります。",
                },
                steps =
                {
                    new GuideStep("「プロファイル登録」で「＋ 新規」を押し、① 登録のしかたで「UnityPackage から作る」を選んで ②「次へ」。", "pkg-profile-1"),
                    new GuideStep("③ unitypackage をドロップするか、「ファイルを選ぶ…」で選びます（エクスプローラーからでも OK）。中身を読むだけで、まだインポートはしません。複数を選ぶと、シーケンスインポートでまとめて作れます。", "pkg-profile-2"),
                    new GuideStep("④ プロジェクトと照らし合わせた結果が出ます。⑤ 名前と前回のバージョンはファイル名から読み取ります（例：Ring_Ver1.0.2 → Ring、次のエクスポートは 1.0.2 の続きから）。⑥ ボタンを押すと、まだ入っていないファイルを自動でインポートしてから作ります。", "pkg-profile-3"),
                    new GuideStep("黄色の「!」（同じ名前の別のファイルがプロジェクトにある）があるときだけ、⑦「Unity の画面で入れる」（入れるファイルを選べます）か「このまま入れる（上書き）」を選びます。", "pkg-profile-4"),
                    new GuideStep("⑧ できたプロファイルはすぐに保存されます（Ctrl+Z で取り消せます）。「入れ忘れの可能性」などの注意が出たら、「中身を確認・修正」で確かめてください。", "pkg-profile-5"),
                    new GuideStep("同じ unitypackage からもう一度作ると、中身がまったく同じなら「既にあります」と出て、新しくは作りません。同じ名前で中身が違うとき（新しいバージョンのパッケージなど）は、何が変わるかを見て「上書きする」か「別名で作る（例：C125 (2)）」を選べます。"),
                },
                actionLabel = "UnityPackage から作る",
                action = w => w.Show(new PackageProfilePage()),
                related = new[] { "import-sequence", "export-single" },
            });

            list.Add(new GuideTopic
            {
                id = "profile-edit", group = "profile",
                title = "作成したプロファイルを編集・修正したい",
                summary = "含めるファイルの追加・削除や、名前・書き出し先の変更。",
                steps =
                {
                    new GuideStep("「プロファイル登録」で直したいプロファイルを選び、①「中身を編集」を押します。エクスポート画面の「修正」からも同じ画面になります。", "profile-detail"),
                    new GuideStep("開いた時点でプレハブをもう一度調べ、前回のあとで新しく使われ始めたファイルには緑の「新規」が付きます。確認して「保存」を押してください。"),
                },
                actionLabel = "プロファイル登録を開く",
                action = w => w.Show(new ProfilesPage()),
            });

            list.Add(new GuideTopic
            {
                id = "profile-duplicate", group = "profile",
                title = "プロファイルを複製して修正したい",
                summary = "似た中身のプロファイルを、コピーから作る。",
                steps =
                {
                    new GuideStep("複製元のプロファイルを選び、②「複製」を押します。「（名前）_copy」ができるので、それを選んで「中身を編集」で直します。", "profile-detail"),
                },
                actionLabel = "プロファイル登録を開く",
                action = w => w.Show(new ProfilesPage()),
            });

            list.Add(new GuideTopic
            {
                id = "profile-import", group = "profile",
                title = "渡されたプロファイルをインポートしたい",
                summary = "人からもらった .profile.json を取り込む。",
                steps =
                {
                    new GuideStep("「プロファイル登録」の一覧の上の ④ 読み込みボタンで、もらった .profile.json を選びます。", "profile-detail"),
                    new GuideStep("中のファイルは GUID（Unity の内部 ID）で探します。相手と同じ素材が入っていないファイルは「見つかりません」と表示されるので、書き出す前にエクスポート画面の「プレビュー」で確認してください。"),
                },
                actionLabel = "プロファイル登録を開く",
                action = w => w.Show(new ProfilesPage()),
                related = new[] { "profile-share" },
            });

            list.Add(new GuideTopic
            {
                id = "profile-share", group = "profile",
                title = "プロファイルを誰かに渡したい",
                summary = "JSON ファイルに書き出して渡す。",
                steps =
                {
                    new GuideStep("渡したいプロファイルを選び、③「JSON に書き出す…」で保存して、できた .profile.json を相手に送ります。", "profile-detail"),
                },
                actionLabel = "プロファイル登録を開く",
                action = w => w.Show(new ProfilesPage()),
                related = new[] { "profile-import" },
            });

            // ───────────── エクスポート ─────────────

            list.Add(new GuideTopic
            {
                id = "export-about", group = "export", concept = true,
                title = "エクスポート・シーケンスエクスポートとは？",
                summary = "プロファイルの中身を unitypackage として書き出すことです。",
                intro = new[]
                {
                    "エクスポートとは、登録したプロファイルの中身を unitypackage として書き出すことです。",
                    "名前の後ろに「種別（Debug など）」「バージョン」「日付」「時刻」をクリックで付け外しできます。名前とのあいだは「_」、要素どうしは「-」でつながります（例：productC_Ver1.0.0-2026_10_25-18_51）。",
                    "シーケンスエクスポートは、複数のプロファイルを上から順番にまとめて書き出す機能です。日付・時刻は押した瞬間の値を全部に使うので、後ろの方だけ時刻がずれることはありません。",
                },
                introImage = "export-single-1-plain",
                actionLabel = "エクスポートを開く",
                action = w => w.Show(new ExportPage()),
            });

            list.Add(new GuideTopic
            {
                id = "export-single", group = "export",
                title = "プロファイルをエクスポートしたい",
                summary = "1 つのプロファイルを、名前を決めて書き出す。",
                steps =
                {
                    new GuideStep("「エクスポート」で ① 書き出すプロファイルを選びます。② 名前に付ける要素（種別・バージョン・日付・時刻）をクリックで ON / OFF。", "export-single-1"),
                    new GuideStep("③ バージョンは ▲▼・ホイール・直接入力で決めます（前回の番号がヒントに出ます）。④ 書き出される名前はここで確認できます。", "export-single-1"),
                    new GuideStep("⑤「プレビュー」で中身を確認し、⑦「エクスポート」で書き出します。中身を直したいときは ⑥「修正」を押します。", "export-single-1"),
                },
                actionLabel = "エクスポートを開く",
                action = w => w.Show(new ExportPage()),
                related = new[] { "export-sequence", "profile-edit" },
            });

            list.Add(new GuideTopic
            {
                id = "export-sequence", group = "export",
                title = "まとめて書き出したい（シーケンスエクスポート）",
                summary = "複数のプロファイルを、順番に自動で書き出す。",
                steps =
                {
                    new GuideStep("「エクスポート」の上の ①「シーケンスエクスポート」に切り替えます。② 名前に付ける要素を選びます（全部に共通）。", "export-seq-1"),
                    new GuideStep("③ 書き出すプロファイルをクリックで選びます（Shift+クリックで範囲選択）。バージョンは行ごとに決められ、④「選択中のバージョンを +0.0.1」でまとめて上げられます。", "export-seq-1"),
                    new GuideStep("⑤「○件をエクスポート」を押すと、書き出す前に全部まとめてチェックしてから、順番に書き出します。", "export-seq-1"),
                },
                actionLabel = "エクスポートを開く",
                action = w =>
                {
                    UnityEditor.SessionState.SetInt("VInc.VIAH.exportMode", 1);
                    w.Show(new ExportPage());
                },
                related = new[] { "export-single" },
            });

            // ───────────── インポート ─────────────

            list.Add(new GuideTopic
            {
                id = "import-about", group = "import", concept = true,
                title = "シーケンスインポートとは？",
                summary = "複数の unitypackage を、上から順番にまとめてインポートする機能です。",
                intro = new[]
                {
                    "シーケンスインポートは、複数の unitypackage を並べておき、上から順番にまとめてインポートする機能です。1 つずつ Unity のインポート画面を開いて「Import」を押す手間がなくなります。",
                    "Unity のインポート画面で黄色の「!」が付くファイル（同じ名前の別のファイルがプロジェクトにある）は、そのまま入れるとプロジェクトのファイルが上書きされます。シーケンスインポートでは、これがあったときに止めて確認するか、無視して入れるかを選べます。",
                    "行ごとに「プロファイルも作る」をオンにすると、インポートのあとに中身をそのままプロファイルとして登録します。手で作っていた unitypackage を、まとめて Avatar Hub に移せます。",
                    "インポートは Unity の機能なので、Ctrl+Z では戻せません（作ったプロファイルは戻せます）。",
                },
                introImage = "import-seq-2-plain",
                actionLabel = "シーケンスインポートを開く",
                action = w => w.Show(new ImportQueuePage()),
            });

            list.Add(new GuideTopic
            {
                id = "import-sequence", group = "import",
                title = "複数の unitypackage をまとめてインポートしたい",
                summary = "キューに並べて、順番に自動でインポートする。",
                steps =
                {
                    new GuideStep("左のサイドバーの ①「シーケンスインポート」を開き、② unitypackage をドロップします（エクスプローラーからでも、複数まとめてでも OK）。「フォルダーからまとめて追加…」で、フォルダーの中の unitypackage をすべて並べることもできます。", "import-seq-1"),
                    new GuideStep("③ 黄色の「!」があったときにどうするかを選びます（おすすめは「止めて確認する」）。④ 行ごとに「プロファイルも作る」を選べます。⑤ ↑↓ で順番を入れ替えられます。", "import-seq-2"),
                    new GuideStep("⑥「開始」を押すと、上から順番にインポートします。⑦ 何件目まで終わったかは、ここで確認できます。スクリプトが入っていると途中でコンパイルが走りますが、終わると自動で続きから進みます。", "import-seq-2"),
                    new GuideStep("黄色の「!」で止まったときは、⑧ その行で「Unity の画面で入れる」「このまま入れる（上書き）」「飛ばす」から選ぶと、続きを進めます。同じ名前で中身の違うプロファイルが既にあるときも止まるので、「上書きする」「別名で作る」「作らない」から選んでください（中身がまったく同じなら作らずに進みます）。", "import-seq-3"),
                    new GuideStep("すべて終わると、⑨ 作ったプロファイルの一覧が出ます。注意があるものには黄色の印が付くので、「中身を確認・修正」で確かめてください。", "import-seq-4"),
                },
                actionLabel = "シーケンスインポートを開く",
                action = w => w.Show(new ImportQueuePage()),
                related = new[] { "profile-from-package" },
            });

            // ───────────── プロジェクトブックマーカー ─────────────

            list.Add(new GuideTopic
            {
                id = "bookmark-about", group = "bookmark", concept = true,
                title = "プロジェクトブックマーカーとは？",
                summary = "よく使うフォルダーを登録して、Project ウィンドウですぐ飛べるようにする機能です。",
                intro = new[]
                {
                    "Project ウィンドウでフォルダーを開いていても、Inspector でマテリアルなどをクリックすると、表示がそちらへ飛んでしまいます。プロジェクトブックマーカーは、よく使うフォルダーを登録しておき、いつでもクリック 1 回でその場所へ戻れるようにする機能です。",
                    "押すと、最後に使った Project タブ（ロックしていないもの）がその場所に切り替わります。ロック中のタブは動かしません。選択中のもの（Inspector の表示）も変わりません。",
                    "「ブックマークのタブを出す」で、Project タブの隣にブックマークだけのタブを出せます。Avatar Hub を開かなくても、クリック 1 回で飛べます。",
                    "ブックマークは自分だけのものです（はじめは空です）。JSON に書き出して人に渡すこともでき、別の Unity プロジェクトでも、同じ場所（パス）にフォルダーがあれば使えます。",
                },
                introImage = "bookmark-1-plain",
                actionLabel = "プロジェクトブックマーカーを開く",
                action = w => w.Show(new BookmarksPage()),
            });

            list.Add(new GuideTopic
            {
                id = "bookmark-use", group = "bookmark",
                title = "よく使うフォルダーへすぐ飛びたい",
                summary = "フォルダーをブックマークして、クリック 1 回で Project ウィンドウを切り替える。",
                steps =
                {
                    new GuideStep("左のサイドバーの ①「プロジェクトブックマーカー」を開き、② Project ウィンドウからフォルダーをドロップします（複数まとめても OK）。Project で選んでから「選択中のものを追加」を押しても登録できます。", "bookmark-1"),
                    new GuideStep("③ 登録したブックマークを押すと、最後に使った（ロックしていない）Project タブがその場所に切り替わります。名前の変更・並べ替え（↑↓）・削除は行の右のボタンで。", "bookmark-1"),
                    new GuideStep("④「ブックマークのタブを出す」で、Project タブの隣にブックマークだけのタブを出せます。出しておけば、Avatar Hub を開かなくてもクリック 1 回で飛べます。", "bookmark-1"),
                },
                actionLabel = "プロジェクトブックマーカーを開く",
                action = w => w.Show(new BookmarksPage()),
                related = new[] { "bookmark-share" },
            });

            list.Add(new GuideTopic
            {
                id = "bookmark-share", group = "bookmark",
                title = "ブックマークを人に渡したい・別のプロジェクトで使いたい",
                summary = "JSON に書き出して渡す。同じ場所にフォルダーがあれば、別のプロジェクトでも使える。",
                steps =
                {
                    new GuideStep("①「JSON に書き出す…」で保存して、相手に渡します。受け取った人は「JSON から読み込む…」で足せます（同じ場所のものは重ねて登録しません）。", "bookmark-share-1"),
                    new GuideStep("ブックマークは PC 全体で共通なので、別の Unity プロジェクトを開いても同じ一覧が出ます。同じ場所（パス）にフォルダーが無いものは薄く表示され、押しても飛びません。"),
                },
                actionLabel = "プロジェクトブックマーカーを開く",
                action = w => w.Show(new BookmarksPage()),
                related = new[] { "bookmark-use" },
            });

            // ───────────── アセットSearcher ─────────────

            list.Add(new GuideTopic
            {
                id = "searcher-about", group = "searcher", concept = true,
                title = "アセットSearcher とは？",
                summary = "選んだアセットを「使っているもの」を探す機能です。",
                intro = new[]
                {
                    "アセットSearcher は、選んだアセットを使っているもの（逆引き）を探す機能です。テクスチャならそれを使っているマテリアル、アニメーションならそれを使っているコントローラーが見つかります。",
                    "探す場所は「Assets 全体」「フォルダーの中」「プレハブの中」から選べます。プレハブの中を調べると、どのオブジェクトの、どのコンポーネントの、どの欄が使っているかまでわかります。",
                    "ふだんは直接使っているものだけを出します。「その先もたどる」をオンにすると、テクスチャ → マテリアル → プレハブのように、間にはさまっているもの（経由）も表示します。",
                    "アセット本体は読み込まず、Unity の依存関係の情報だけで探します（プレハブの中を調べるときだけ、そのプレハブを読み込みます）。",
                },
                introImage = "searcher-2-plain",
                actionLabel = "アセットSearcher を開く",
                action = w => w.Show(new SearcherPage()),
            });

            list.Add(new GuideTopic
            {
                id = "searcher-use", group = "searcher",
                title = "このアセットを使っている場所を探したい",
                summary = "テクスチャを使っているマテリアル、アニメーションを使っているコントローラーなどを探す。",
                steps =
                {
                    new GuideStep("左のサイドバーの「アセットSearcher」を開き、① 探したいアセットを Project ウィンドウからドロップします（選んでから「選択中のものを使う」でも OK）。", "searcher-1"),
                    new GuideStep("② 探す場所を選びます（Assets 全体・フォルダーの中・プレハブの中）。③ 間にはさまっているものも見たいときは「その先もたどる」をオンに。④「検索」を押します。", "searcher-1"),
                    new GuideStep("⑤ 直接使っているものと、その先で使っているもの（紫の「経由」）が分かれて出ます。「Project で表示」で、その場所へ飛べます。", "searcher-2"),
                },
                actionLabel = "アセットSearcher を開く",
                action = w => w.Show(new SearcherPage()),
                related = new[] { "searcher-prefab" },
            });

            list.Add(new GuideTopic
            {
                id = "searcher-prefab", group = "searcher",
                title = "プレハブのどこで使われているか調べたい",
                summary = "どのオブジェクトの、どのコンポーネントの、どの欄が使っているかを表示する。",
                steps =
                {
                    new GuideStep("探す場所で「プレハブの中」を選び、調べたいプレハブをドロップします。「検索」を押すと、① どのオブジェクトの、どのコンポーネントの、どの欄が使っているかが出ます（例：Body › MA Merge Animator › animator）。", "searcher-3"),
                    new GuideStep("「その先もたどる」をオンにすると、コンポーネントが使っているコントローラーなどの中で使われている場合も出ます。「プレハブで選択」で、プレハブを開いてそのオブジェクトを選べます。"),
                },
                actionLabel = "アセットSearcher を開く",
                action = w => w.Show(new SearcherPage()),
                related = new[] { "searcher-use" },
            });

            // ───────────── そのほか ─────────────

            list.Add(new GuideTopic
            {
                id = "search", group = "other",
                title = "名前を忘れたものを探したい",
                summary = "日本語のタグやうろ覚えの名前で検索する。",
                steps =
                {
                    new GuideStep("上の ① 検索欄に、名前やタグを入れます（Ctrl+F でも移動できます）。ひらがな・カタカナ・全角半角の違いは気にしなくて OK。② 結果を選ぶと、その場所に移動します（プロジェクトは Project ウィンドウでフォルダーも表示します）。", "search-1"),
                },
            });

            list.Add(new GuideTopic
            {
                id = "undo", group = "other",
                title = "操作を元に戻したい（Undo）",
                summary = "Ctrl+Z で元に戻す、Ctrl+Y でやり直し。",
                intro = new[]
                {
                    "Ctrl+Z で元に戻す、Ctrl+Y（または Ctrl+Shift+Z）でやり直しができます。Unity の Edit メニューにも「VIAH: 〜」として出ます。",
                    "元に戻せるもの：入力途中の内容、フォルダー構成の編集、プロファイルの中身の選択、保存・削除・複製・JSON の読み込み、設定の変更、シーケンスインポートのキューの編集、ブックマークの追加・削除・名前の変更・並べ替え、フォルダーの作成（取り消すと作ったフォルダーを削除。中にファイルを置いていたら残します）。",
                    "元に戻せないもの：unitypackage と JSON の書き出し（作ったファイルはそのまま）、unitypackage のインポート（入ったファイルはそのまま。一緒に作ったプロファイルは戻せます）、画面の移動や検索。画面を移ると、その画面の入力途中の Undo は消えます（保存したものは戻せます）。",
                },
                actionLabel = "設定で詳しく見る",
                action = w => w.Show(new SettingsPage()),
            });

            list.Add(new GuideTopic
            {
                id = "settings", group = "other",
                title = "見た目や表示を変えたい",
                summary = "配色・アクセントカラー・サイドバーに出す機能・ウィンドウの開き方・効果音。",
                steps =
                {
                    new GuideStep("右上の ①「設定」を開きます。② テーマ（Unity のグレー・ダーク・ライト）とアクセントカラーを選べます。③ サイドバーに出すワークフローも選べます。ウィンドウの開き方や保存場所もここにあります。", "settings-1"),
                    new GuideStep("「効果音」では、時間のかかる作業（シーケンスエクスポート・インポートなど）が終わったときと、確認待ちで止まったときの音を選べます。音量の調整、試し聴き、自分の音（wav / mp3 / ogg）への入れ替え、鳴らす作業のオン・オフもここで。"),
                },
                actionLabel = "設定を開く",
                action = w => w.Show(new SettingsPage()),
            });

            return list;
        }
    }
}

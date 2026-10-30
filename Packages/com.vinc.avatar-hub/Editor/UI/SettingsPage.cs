using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>設定：配色・表示するワークフロー・ウィンドウの開き方・エクスポート・保存場所など。変更は Undo できる。</summary>
    public class SettingsPage : Page
    {
        public override string Section => "settings";

        readonly string focus;
        ScrollView scroll;

        public SettingsPage(string focusSection = null)
        {
            focus = focusSection;
        }

        static ViahSettings S => ViahDatabase.Settings;

        void Apply(string label, string coalesceKey = null)
        {
            ViahDatabase.SaveSettings(label, coalesceKey);
            window.ApplyTheme();
            window.RebuildSidebar();
        }

        public override void Build()
        {
            scroll = UIKit.PageScroll();
            root.Add(scroll);
            var c = scroll.contentContainer;

            c.Add(UIKit.Text("設定", "viah-page-title"));
            c.Add(UIKit.Text("変更はすぐに反映・保存されます。間違えたときは Ctrl+Z で元に戻せます。", "viah-page-sub"));

            c.Add(ThemeCard());
            c.Add(WorkflowCard());
            c.Add(WindowCard());
            var export = ExportCard();
            c.Add(export);
            var sound = SoundCard();
            c.Add(sound);
            c.Add(DataCard());
            c.Add(UndoCard());
            c.Add(AboutCard());

            if (focus == "export") scroll.schedule.Execute(() => scroll.ScrollTo(export)).ExecuteLater(50);
            if (focus == "sound") scroll.schedule.Execute(() => scroll.ScrollTo(sound)).ExecuteLater(50);
        }

        // ───────────── 見た目 ─────────────

        VisualElement ThemeCard()
        {
            var card = UIKit.Card();
            card.name = "card-theme";
            card.Add(UIKit.SectionHead(null, "見た目", "ツールの配色を選びます"));

            card.Add(UIKit.Text("テーマ", "viah-field-label"));
            var row = UIKit.Div("viah-choice-row");
            row.Add(ThemeChoice("unity", "Unity のグレー", "Unity エディターと同じ色づかい（Unity の設定に合わせて明暗が変わります）", "#383838", "#282828", "#d2d2d2"));
            row.Add(ThemeChoice("dark", "ダーク", "少し青みのある暗い配色（はじめの設定）", "#26282c", "#1e2023", "#e9eaec"));
            row.Add(ThemeChoice("light", "ライト", "明るい配色", "#f4f5f6", "#e6e7e9", "#1d1f22"));
            card.Add(row);

            card.Add(UIKit.Text("アクセントカラー", "viah-field-label", "viah-mt16"));
            card.Add(UIKit.Text("ボタンや選択中の項目などに使う色です。", "viah-faint", "viah-small", "viah-mb8"));
            var swatches = UIKit.Div("viah-swatch-row");
            foreach (var a in ViahWindow.Accents)
            {
                var key = a.key;
                var item = UIKit.Div("viah-swatch-item");
                UIKit.SetClass(item, "is-on", S.accent == key);
                var dot = UIKit.Div("viah-swatch");
                dot.style.backgroundColor = ParseColor(a.color);
                item.Add(dot);
                item.Add(UIKit.Text(a.label, "viah-small"));
                item.tooltip = a.label;
                UIKit.Clickable(item, () =>
                {
                    if (S.accent == key) return;
                    S.accent = key;
                    Apply("アクセントカラーを変更");
                    Rebuild();
                });
                swatches.Add(item);
            }
            card.Add(swatches);
            return card;
        }

        VisualElement ThemeChoice(string key, string title, string text, string surface, string bg, string fg)
        {
            var card = UIKit.Div("viah-choice");
            UIKit.SetClass(card, "is-on", S.theme == key);
            var head = UIKit.Div("viah-choice-head");
            var radio = UIKit.Div("viah-radio");
            radio.Add(UIKit.Div("viah-radio-dot"));
            head.Add(radio);
            head.Add(UIKit.Text(title, "viah-choice-title"));
            card.Add(head);
            // 小さな見本
            var sample = UIKit.Div("viah-theme-sample");
            sample.style.backgroundColor = ParseColor(bg);
            var bar = UIKit.Div("viah-theme-sample-bar");
            bar.style.backgroundColor = ParseColor(surface);
            sample.Add(bar);
            var line1 = UIKit.Div("viah-theme-sample-line");
            line1.style.backgroundColor = ParseColor(fg);
            sample.Add(line1);
            var line2 = UIKit.Div("viah-theme-sample-line", "short");
            line2.style.backgroundColor = ParseColor(fg);
            sample.Add(line2);
            card.Add(sample);
            card.Add(UIKit.Text(text, "viah-choice-text", "viah-mt8"));
            card.RegisterCallback<ClickEvent>(_ =>
            {
                if (S.theme == key) return;
                S.theme = key;
                Apply("テーマを変更");
                Rebuild();
            });
            return card;
        }

        // ───────────── ワークフロー ─────────────

        VisualElement WorkflowCard()
        {
            var card = UIKit.Card();
            card.name = "card-workflows";
            card.Add(UIKit.SectionHead(null, "ワークフローの表示", "左のサイドバーに出す機能を選びます。今後機能が増えたら、ここで表示・非表示を切り替えられます"));
            foreach (var wf in ViewWorkflows())
            {
                var w = wf;
                var row = UIKit.Row("viah-setting-row");
                var col = UIKit.Div("viah-grow");
                col.Add(UIKit.Text(w.label, "viah-bold"));
                col.Add(UIKit.Text(w.description, "viah-faint", "viah-small"));
                row.Add(col);
                var sw = new Switch("表示", !S.hiddenWorkflows.Contains(w.key));
                sw.changed += on =>
                {
                    if (on) S.hiddenWorkflows.Remove(w.key);
                    else if (!S.hiddenWorkflows.Contains(w.key)) S.hiddenWorkflows.Add(w.key);
                    Apply(on ? $"「{w.label}」を表示" : $"「{w.label}」を非表示");
                };
                row.Add(sw);
                card.Add(row);
            }
            return card;
        }

        static System.Collections.Generic.IEnumerable<Workflow> ViewWorkflows() => ViahWindow.Workflows;

        // ───────────── ウィンドウ ─────────────

        VisualElement WindowCard()
        {
            var card = UIKit.Card();
            card.name = "card-window";
            card.Add(UIKit.SectionHead(null, "ウィンドウの開き方", "メニュー「V.inc → Avatar Hub」で開くときの形"));
            var row = UIKit.Div("viah-choice-row");
            row.Add(WindowChoice("float", "独立したウィンドウで開く（おすすめ）", "自由に動かせる別ウィンドウ。前回閉じたときの位置と大きさで開きます（はじめては Unity 本体の中央）。Unity の仕様で、Unity 本体よりは前に表示されます。"));
            row.Add(WindowChoice("tab", "Unity のタブとして開く", "Scene ビューの隣にタブとして入ります。大きさはタブの場所で決まります。"));
            card.Add(row);
            card.Add(UIKit.Text($"すでに開いているウィンドウはそのままです。閉じてから開き直すと反映されます。ウィンドウは {ViahWindow.MinSize.x:0}×{ViahWindow.MinSize.y:0} より小さくはできません（それより小さいと表示が崩れるため）。", "viah-faint", "viah-small", "viah-mt8", "viah-wrap"));
            return card;
        }

        VisualElement WindowChoice(string key, string title, string text)
        {
            var card = UIKit.Div("viah-choice");
            UIKit.SetClass(card, "is-on", S.windowMode == key);
            var head = UIKit.Div("viah-choice-head");
            var radio = UIKit.Div("viah-radio");
            radio.Add(UIKit.Div("viah-radio-dot"));
            head.Add(radio);
            head.Add(UIKit.Text(title, "viah-choice-title"));
            card.Add(head);
            card.Add(UIKit.Text(text, "viah-choice-text"));
            card.RegisterCallback<ClickEvent>(_ =>
            {
                if (S.windowMode == key) return;
                S.windowMode = key;
                Apply("ウィンドウの開き方を変更");
                Rebuild();
            });
            return card;
        }

        // ───────────── エクスポート ─────────────

        VisualElement ExportCard()
        {
            var card = UIKit.Card();
            card.name = "card-export-settings";
            card.Add(UIKit.SectionHead(null, "エクスポート"));
            card.Add(UIKit.Text("種別ラベル", "viah-field-label"));
            var label = new ViahTextField("Debug", S.debugLabel);
            label.changed += v =>
            {
                S.debugLabel = string.IsNullOrWhiteSpace(v) ? "Debug" : v.Trim();
                ViahDatabase.SaveSettings("種別ラベルを変更", "debug-label");
            };
            card.Add(label);
            card.Add(UIKit.Text("エクスポート画面で「種別」を ON にすると、名前のすぐ後ろに付きます（例：productC_Debug_Ver1.0.0）。", "viah-faint", "viah-small", "viah-wrap", "viah-mt4"));

            card.Add(UIKit.Text("既定の書き出し先", "viah-field-label", "viah-mt16"));
            var outRow = UIKit.Row();
            var pill = UIKit.Div("viah-path-pill");
            pill.Add(UIKit.Img("Folder Icon"));
            pill.Add(UIKit.Text(string.IsNullOrWhiteSpace(S.defaultOutputFolder) ? "Unity プロジェクトの Exports フォルダー（" + ViahStorage.PrettyPath(ViahStorage.DefaultExportDir) + "）" : S.defaultOutputFolder, "viah-ml6"));
            outRow.Add(pill);
            outRow.Add(UIKit.Btn("変更…", () =>
            {
                var picked = EditorUtility.OpenFolderPanel("既定の書き出し先", string.IsNullOrWhiteSpace(S.defaultOutputFolder) ? ViahStorage.DefaultExportDir : S.defaultOutputFolder, "");
                if (string.IsNullOrEmpty(picked)) return;
                S.defaultOutputFolder = picked;
                ViahDatabase.SaveSettings("既定の書き出し先を変更");
                Rebuild();
            }, "viah-ml8"));
            if (!string.IsNullOrWhiteSpace(S.defaultOutputFolder))
            {
                outRow.Add(UIKit.Btn("初期値に戻す", () =>
                {
                    S.defaultOutputFolder = "";
                    ViahDatabase.SaveSettings("既定の書き出し先を初期値に戻す");
                    Rebuild();
                }, "ghost", "viah-ml4"));
            }
            card.Add(outRow);
            card.Add(UIKit.Text("プロファイルごとに書き出し先を決めていない場合に使います。", "viah-faint", "viah-small", "viah-mt4"));
            return card;
        }

        // ───────────── 効果音 ─────────────

        VisualElement SoundCard()
        {
            var card = UIKit.Card();
            card.name = "card-sound";
            card.Add(UIKit.SectionHead(null, "効果音", "時間のかかる作業が終わったときと、確認待ちで止まったときに音で知らせます"));
            var on = new Switch("効果音を鳴らす", S.soundOn);
            on.changed += v =>
            {
                S.soundOn = v;
                Apply(v ? "効果音をオン" : "効果音をオフ");
                Rebuild();
            };
            card.Add(on);
            var body = UIKit.Div();
            body.SetEnabled(S.soundOn);
            card.Add(body);

            body.Add(UIKit.Text("音量", "viah-field-label", "viah-mt16"));
            var volRow = UIKit.Row();
            var slider = new Slider(0, 100) { value = S.soundVolume * 100 };
            slider.AddToClassList("viah-slider");
            var volLabel = UIKit.Text($"{Mathf.RoundToInt(S.soundVolume * 100)}%", "viah-ml8", "viah-sound-vol");
            slider.RegisterValueChangedCallback(e =>
            {
                S.soundVolume = Mathf.Clamp01(e.newValue / 100f);
                volLabel.text = $"{Mathf.RoundToInt(e.newValue)}%";
            });
            // 動かし終わったときに保存して、その音量で鳴らしてみる
            slider.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                ViahDatabase.SaveSettings("効果音の音量を変更", "sound-volume");
                ViahSound.Play(false);
            });
            volRow.Add(slider);
            volRow.Add(volLabel);
            body.Add(volRow);

            body.Add(SoundRow("作業が終わったとき", false));
            body.Add(SoundRow("確認待ちで止まったとき（衝突・同じ名前のプロファイルなど）", true));

            body.Add(UIKit.Text("鳴らす作業", "viah-field-label", "viah-mt16"));
            var grid = UIKit.Div("viah-sound-events");
            foreach (var (key, label) in ViahSound.Events)
            {
                var k = key;
                var sw = new Switch(label, !S.soundMuted.Contains(k));
                sw.changed += v =>
                {
                    if (v) S.soundMuted.Remove(k);
                    else if (!S.soundMuted.Contains(k)) S.soundMuted.Add(k);
                    Apply(v ? $"「{label}」で鳴らす" : $"「{label}」で鳴らさない");
                };
                grid.Add(sw);
            }
            body.Add(grid);
            var skip = new Switch($"すぐ終わったとき（{ViahSound.ShortSeconds:0} 秒未満）は鳴らさない", S.soundSkipShort);
            skip.style.marginTop = 10;
            skip.changed += v =>
            {
                S.soundSkipShort = v;
                Apply(v ? "すぐ終わったときは鳴らさない" : "すぐ終わっても鳴らす");
            };
            body.Add(skip);
            body.Add(UIKit.Text("内蔵の音はこのツールが作った音なので、自由に使えます。自分の音（wav / mp3 / ogg）を選ぶと保存場所の Sounds フォルダーにコピーし、どの Unity プロジェクトでも同じ音になります。Windows では Windows の機能で鳴らすので、Unity の「Mute Audio」には左右されません。音が小さいときは、Windows の音量ミキサーの「Unity」も確かめてください。",
                "viah-faint", "viah-small", "viah-wrap", "viah-mt8"));
            return card;
        }

        VisualElement SoundRow(string title, bool attention)
        {
            var row = UIKit.Div();
            row.Add(UIKit.Text(title, "viah-field-label", "viah-mt16"));
            var line = UIKit.Row("viah-btn-group");
            var key = attention ? S.soundAttention : S.soundDone;
            var file = attention ? S.soundAttentionFile : S.soundDoneFile;
            var choices = ViahSound.Presets.Select(p => p.key).ToList();
            if (!string.IsNullOrEmpty(file)) choices.Add(ViahSound.FileKey);
            string Label(string k) => k == ViahSound.FileKey ? "自分の音：" + file : ViahSound.LabelOf(k);
            var popup = new PopupField<string>(null, choices, choices.Contains(key) ? key : choices[0], Label, Label);
            popup.AddToClassList("viah-sound-popup");
            popup.RegisterValueChangedCallback(e =>
            {
                if (attention) S.soundAttention = e.newValue;
                else S.soundDone = e.newValue;
                Apply(attention ? "確認待ちの効果音を変更" : "完了の効果音を変更");
                ViahSound.Play(attention);
            });
            line.Add(popup);
            line.Add(UIKit.Btn("PlayButton", "試しに鳴らす", () => ViahSound.Play(attention), "small", "viah-ml8"));
            line.Add(UIKit.Btn("Folder Icon", "自分の音を選ぶ…", () => PickSound(attention), "small", "viah-ml4"));
            row.Add(line);
            return row;
        }

        void PickSound(bool attention)
        {
            var path = EditorUtility.OpenFilePanelWithFilters("効果音を選ぶ", "", new[] { "音のファイル", "wav,mp3,ogg" });
            if (string.IsNullOrEmpty(path)) return;
            string name;
            try
            {
                name = ViahSound.CopyIn(path);
            }
            catch (Exception e)
            {
                window.Toast("ファイルをコピーできませんでした：" + e.Message, "danger");
                return;
            }
            ViahSound.Load(Path.Combine(ViahStorage.SoundsDir, name), loaded =>
            {
                if (loaded == null)
                {
                    window.Toast("この音のファイルは読めませんでした（wav / mp3 / ogg が使えます）", "warn");
                    return;
                }
                if (attention)
                {
                    S.soundAttention = ViahSound.FileKey;
                    S.soundAttentionFile = name;
                }
                else
                {
                    S.soundDone = ViahSound.FileKey;
                    S.soundDoneFile = name;
                }
                Apply(attention ? "確認待ちの効果音を入れ替え" : "完了の効果音を入れ替え");
                Rebuild();
                ViahSound.Play(attention);
            });
        }

        // ───────────── データ ─────────────

        VisualElement DataCard()
        {
            var card = UIKit.Card();
            card.name = "card-data";
            card.Add(UIKit.SectionHead(null, "データの保存場所", "プリセット・プロジェクト記録・プロファイル・設定は PC 全体で共通です。どの Unity プロジェクトを開いても同じ一覧が使えます"));
            void PathRow(string label, string path)
            {
                var row = UIKit.Row("viah-setting-row");
                var col = UIKit.Div("viah-grow");
                col.Add(UIKit.Text(label, "viah-bold"));
                col.Add(UIKit.Text(path, "viah-faint", "viah-small", "viah-wrap"));
                row.Add(col);
                row.Add(UIKit.Btn("FolderOpened Icon", "開く", () => ViahStorage.OpenFolder(path), "small"));
                card.Add(row);
            }
            PathRow("保存場所（まとめて）", ViahStorage.DataDir);
            PathRow("フォルダー構成プリセット", ViahStorage.PresetsDir);
            PathRow("プロジェクト記録", ViahStorage.ProjectsDir);
            PathRow("プロファイル", ViahStorage.ProfilesDir);

            var actions = UIKit.Div("viah-btn-group", "viah-mt12");
            actions.Add(UIKit.Btn("Refresh", "JSON を読み込み直す", () =>
            {
                ViahDatabase.Reload();
                window.ApplyTheme();
                window.RebuildSidebar();
                Rebuild();
                window.Toast("JSON を読み込み直しました");
            }, "small").With(b => b.tooltip = "JSON を手で編集・追加したときに使います"));
            actions.Add(UIKit.Btn("閉じたヒントをすべて再表示", () =>
            {
                UIKit.ResetHints();
                window.Toast("ヒントを再表示するようにしました");
            }, "small"));
            card.Add(actions);
            return card;
        }

        // ───────────── Undo の説明 ─────────────

        VisualElement UndoCard()
        {
            var card = UIKit.Card();
            card.name = "card-undo";
            card.Add(UIKit.SectionHead(null, "元に戻す（Undo）について", "Ctrl+Z で元に戻す、Ctrl+Y（または Ctrl+Shift+Z）でやり直し。Unity の Edit メニューからもできます"));
            card.Add(UIKit.Text("元に戻せる操作", "viah-bold", "viah-mt4"));
            card.Add(Bullet("入力途中の内容（名前・タグ・チェック・作成場所・プリセットの選択など）"));
            card.Add(Bullet("フォルダー構成の編集（追加・名前変更・並べ替え・入れ子・削除）"));
            card.Add(Bullet("プロファイルの中身の選択、起点の追加・削除、自動検出"));
            card.Add(Bullet("保存・削除・複製・JSON の読み込み・設定の変更"));
            card.Add(Bullet("シーケンスインポートのキューの追加・並べ替え・削除（インポート中は除く）"));
            card.Add(Bullet("ブックマークの追加・削除・名前の変更・並べ替え・JSON の読み込み"));
            card.Add(Bullet("フォルダーの作成（取り消すと、作ったフォルダーを削除します。中にファイルを置いていた場合は残します）"));
            card.Add(UIKit.Text("元に戻せない操作", "viah-bold", "viah-mt12"));
            card.Add(Bullet("unitypackage の書き出し（ファイルはそのまま残り、「前回のバージョン」の記録も戻りません）"));
            card.Add(Bullet("JSON の書き出し（人に渡すために作ったファイル）"));
            card.Add(Bullet("unitypackage のインポート（プロジェクトに入ったファイルはそのまま残ります。一緒に作ったプロファイルは戻せます）"));
            card.Add(Bullet("画面の移動・検索・絞り込み（データを変えない操作）"));
            card.Add(Bullet("別の画面に移る前の「入力途中の内容」：画面を移ると、その画面の入力途中の Undo は消えます（保存したものは戻せます）"));
            return card;
        }

        static VisualElement Bullet(string text)
        {
            var row = UIKit.Row("viah-bullet");
            row.style.alignItems = Align.FlexStart;
            row.Add(UIKit.Text("・", "viah-dim"));
            row.Add(UIKit.Text(text, "viah-dim", "viah-wrap", "viah-grow"));
            return row;
        }

        // ───────────── このツールについて ─────────────

        VisualElement AboutCard()
        {
            var card = UIKit.Card();
            card.name = "card-about";
            var head = UIKit.Row();
            if (ViahWindow.Logo != null)
            {
                var img = new Image { image = ViahWindow.Logo, scaleMode = ScaleMode.ScaleToFit };
                img.AddToClassList("viah-about-logo");
                head.Add(img);
            }
            var col = UIKit.Div("viah-ml12");
            col.Add(UIKit.Text("V.inc Avatar Hub（VIAH）", "viah-section-title"));
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SettingsPage).Assembly);
            col.Add(UIKit.Text("バージョン " + (info != null ? info.version : "—"), "viah-faint", "viah-small"));
            col.Add(UIKit.Text("社内の許可された人のみ使用できます。", "viah-faint", "viah-small"));
            head.Add(col);
            card.Add(head);
            var actions = UIKit.Div("viah-btn-group", "viah-mt12");
            actions.Add(UIKit.Btn("利用規約", () => Application.OpenURL("https://github.com/Temu-chan/vinc-avatar-hub/blob/main/LICENSE.md"), "small", "ghost"));
            actions.Add(UIKit.Btn("配布ページ（VCC に追加）", () => Application.OpenURL("https://temu-chan.github.io/vinc-avatar-hub/add/"), "small", "ghost"));
            card.Add(actions);
            return card;
        }

        static Color ParseColor(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.gray;
    }
}

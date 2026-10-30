using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>③ エクスポート：単発とシーケンス。</summary>
    public class ExportPage : Page
    {
        public override string Section => "export";

        const string ModeKey = "VInc.VIAH.exportMode";

        /// <summary>このセッション中に画面で決めたバージョン（画面を切り替えても残す）。</summary>
        static readonly Dictionary<string, PkgVersion> versions = new Dictionary<string, PkgVersion>();
        static readonly HashSet<string> seqChecked = new HashSet<string>();

        string selectedId;
        int mode;
        string filter = "";
        VisualElement content;
        IVisualElementScheduledItem clock;
        readonly List<Action> clockTargets = new List<Action>();

        public ExportPage(string profileId = null)
        {
            selectedId = profileId;
            mode = profileId != null ? 0 : SessionState.GetInt(ModeKey, 0);
        }

        public override void Build()
        {
            var profiles = ViahDatabase.Profiles;
            if (selectedId == null && profiles.Count > 0) selectedId = profiles[0].id;

            // 幅が足りないときは切り替えが下の行に回る
            var top = UIKit.Div("viah-page-top");
            var titles = UIKit.Div("viah-page-top-titles");
            titles.Add(UIKit.Text("エクスポート", "viah-page-title"));
            titles.Add(UIKit.Text("プロファイルを選んで unitypackage を書き出します。", "viah-dim"));
            top.Add(titles);
            var switcher = UIKit.Row();
            switcher.style.flexShrink = 0;
            top.Add(switcher);
            var seg = new Segmented(new[] { "単発エクスポート", "シーケンスエクスポート" }, mode, i =>
            {
                mode = i;
                SessionState.SetInt(ModeKey, i);
                Render();
            });
            switcher.Add(seg);
            var settings = UIKit.IconBtn("SettingsIcon", "エクスポートの設定（種別ラベル・既定の書き出し先）を開く", ShowSettings, "viah-ml8");
            switcher.Add(settings);
            root.Add(top);

            content = UIKit.Div();
            content.style.flexGrow = 1;
            content.style.flexShrink = 1;
            root.Add(content);

            clock = root.schedule.Execute(() =>
            {
                foreach (var a in clockTargets) a();
            }).Every(10000);
            Render();
        }

        public override void OnHide()
        {
            clock?.Pause();
        }

        public override void OnDataChanged()
        {
        }

        // ───────────── Undo（バージョンとシーケンスの選択） ─────────────

        [Serializable]
        class UiState
        {
            public List<string> verIds = new List<string>();
            public List<string> verValues = new List<string>();
            public List<string> seqChecked = new List<string>();
        }

        public override string CaptureState()
        {
            var s = new UiState();
            foreach (var kv in versions.OrderBy(k => k.Key))
            {
                s.verIds.Add(kv.Key);
                s.verValues.Add(kv.Value.ToString());
            }
            s.seqChecked.AddRange(seqChecked.OrderBy(x => x));
            return JsonUtility.ToJson(s);
        }

        public override void RestoreState(string state)
        {
            var s = JsonUtility.FromJson<UiState>(state);
            versions.Clear();
            for (int i = 0; i < s.verIds.Count; i++)
                if (PkgVersion.TryParse(s.verValues[i], out var v)) versions[s.verIds[i]] = v;
            seqChecked.Clear();
            seqChecked.UnionWith(s.seqChecked);
            Render();
        }

        void Render()
        {
            content.Clear();
            clockTargets.Clear();
            if (ViahDatabase.Profiles.Count == 0)
            {
                var empty = UIKit.Div("viah-page");
                empty.Add(UIKit.Hint(null, "まだプロファイルがありません",
                    "エクスポートするには、先に「何を含めるか」をプロファイルとして登録します。"));
                empty.Add(UIKit.Btn("Toolbar Plus", "プロファイルを作る", () => window.Show(new ProfileWizardPage(null)), "primary", "big"));
                content.Add(empty);
                return;
            }
            if (mode == 0) RenderSingle();
            else RenderSequence();
        }

        // ───────────────────────── 共通 ─────────────────────────

        static PkgVersion VersionOf(PackageProfile p)
        {
            if (versions.TryGetValue(p.id, out var v)) return v;
            v = PackageExporter.LastVersion(p) ?? PkgVersion.First;
            versions[p.id] = v;
            return v;
        }

        static string VersionHint(PackageProfile p)
        {
            var last = PackageExporter.LastVersion(p);
            return last.HasValue
                ? $"前回のバージョンは Ver{last.Value} でした。上げるかどうかは自由です。"
                : "はじめてのエクスポートです（Ver1.0.0 から始まります）。";
        }

        static Label Chip(string text, bool on, Action<bool> toggled, string tooltip, params string[] extra)
        {
            var chip = UIKit.Text(text, "viah-chip");
            foreach (var e in extra) chip.AddToClassList(e);
            UIKit.SetClass(chip, "is-on", on);
            chip.tooltip = tooltip;
            chip.RegisterCallback<ClickEvent>(e =>
            {
                bool now = !chip.ClassListContains("is-on");
                UIKit.SetClass(chip, "is-on", now);
                toggled(now);
                e.StopPropagation();
            });
            return chip;
        }

        static Label Joint(string s) => UIKit.Text(s, "viah-joint");

        void ShowSettings() => window.Show(new SettingsPage("export"));

        // ───────────────────────── 単発 ─────────────────────────

        void RenderSingle()
        {
            var split = UIKit.Div("viah-split");
            content.Add(split);

            var listPane = UIKit.Div("viah-list-pane");
            var search = new ViahTextField("名前・タグで絞り込み");
            search.style.flexGrow = 0;
            search.style.marginBottom = 8;
            search.value = filter;
            listPane.Add(search);
            var listScroll = new ScrollView();
            listScroll.style.flexGrow = 1;
            listPane.Add(listScroll);
            split.Add(listPane);

            var detail = UIKit.Div("viah-detail-pane");
            split.Add(detail);

            void RenderList()
            {
                listScroll.Clear();
                var tokens = SearchUtil.Tokens(filter);
                foreach (var p in ViahDatabase.Profiles)
                {
                    if (tokens.Length > 0 && !SearchUtil.Contains(SearchUtil.Normalize(p.name + " " + p.packageName + " " + string.Join(" ", p.tags)), tokens)) continue;
                    var prof = p;
                    var item = UIKit.Div("viah-list-item");
                    UIKit.SetClass(item, "is-active", p.id == selectedId);
                    item.Add(UIKit.Text(p.name, "viah-list-item-title"));
                    var h = ViahDatabase.History.Get(p.id, false);
                    item.Add(UIKit.Text(h == null || string.IsNullOrEmpty(h.lastExportAt)
                        ? "まだ書き出していません"
                        : $"前回 {ViahStorage.ShortDate(h.lastExportAt)}" + (string.IsNullOrEmpty(h.lastVersion) ? "" : $"・Ver{h.lastVersion}"), "viah-list-item-sub"));
                    item.RegisterCallback<ClickEvent>(_ =>
                    {
                        selectedId = prof.id;
                        RenderList();
                        RenderDetail(detail);
                    });
                    listScroll.Add(item);
                }
            }

            search.changed += q =>
            {
                filter = q;
                RenderList();
            };
            RenderList();
            RenderDetail(detail);
        }

        void RenderDetail(VisualElement detail)
        {
            detail.Clear();
            clockTargets.Clear();
            var p = ViahDatabase.FindProfile(selectedId);
            if (p == null)
            {
                detail.Add(UIKit.Text("左からプロファイルを選んでください", "viah-list-empty", "viah-faint"));
                return;
            }

            var scroll = UIKit.PageScroll();
            detail.Add(scroll);
            var c = scroll.contentContainer;

            c.Add(UIKit.Hint("export.single", "名前の付け方",
                "名前の後ろの「種別・バージョン・日付・時刻」はクリックで付け外しできます。名前とのあいだは「_」、要素どうしは「-」でつながります。中身を確認したいときは「プレビュー」、直したいときは「修正」を押してください。"));

            var h = ViahDatabase.Settings.Prefs(p.id, true);
            var opts = new NameOptions
            {
                useDebug = h.useDebug,
                debugLabel = ViahDatabase.Settings.debugLabel,
                useVersion = h.useVersion,
                version = VersionOf(p),
                useDate = h.useDate,
                useTime = h.useTime,
            };

            Label verChip = null, dateChip = null, timeChip = null, debugChip = null, fileLabel = null;
            VisualElement verPanel = null;
            VersionStepper stepper = null;

            var banner = UIKit.Div("viah-banner");
            var line = UIKit.Div("viah-banner-line");
            var nameLabel = UIKit.Text(p.PackageBaseName, "viah-banner-name");
            nameLabel.tooltip = "プロファイル名（変更はプロファイル画面から）";
            line.Add(nameLabel);
            verChip = Chip("Ver" + opts.version, opts.useVersion, on => { opts.useVersion = on; Changed(); }, "クリックでバージョンを付ける / 外す");
            dateChip = Chip(PackageNaming.DateText(DateTime.Now), opts.useDate, on => { opts.useDate = on; Changed(); }, "クリックで日付を付ける / 外す");
            timeChip = Chip(PackageNaming.TimeText(DateTime.Now), opts.useTime, on => { opts.useTime = on; Changed(); }, "クリックで時刻を付ける / 外す（同じ日に何度も書き出すときに）");
            debugChip = Chip(opts.debugLabel, opts.useDebug, on => { opts.useDebug = on; Changed(); }, "クリックで種別（デバッグ用など）を付ける / 外す", "debug");
            line.Add(Joint(" "));
            line.Add(debugChip);
            line.Add(Joint(" "));
            line.Add(verChip);
            line.Add(Joint(" "));
            line.Add(dateChip);
            line.Add(Joint(" "));
            line.Add(timeChip);
            banner.Add(line);

            verPanel = UIKit.Div("viah-ver-panel");
            stepper = new VersionStepper(opts.version);
            verPanel.Add(stepper);
            var verInfo = UIKit.Div("viah-ml12", "viah-grow");
            verInfo.Add(UIKit.Text(VersionHint(p), "viah-dim", "viah-wrap"));
            var quick = UIKit.Row("viah-mt8");
            var last = PackageExporter.LastVersion(p);
            if (last.HasValue)
            {
                quick.Add(UIKit.Btn("前回と同じ", () => SetVersion(last.Value), "small"));
                quick.Add(UIKit.Btn("修正 +0.0.1", () => SetVersion(last.Value.Step(2, +1)), "small", "viah-ml4"));
                quick.Add(UIKit.Btn("機能追加 +0.1", () => SetVersion(last.Value.Step(1, +1)), "small", "viah-ml4"));
            }
            verInfo.Add(quick);
            verPanel.Add(verInfo);
            banner.Add(verPanel);
            stepper.changed += v => SetVersion(v);

            var fileBox = UIKit.Div("viah-filename");
            fileBox.Add(UIKit.Img("Package Manager"));
            fileLabel = UIKit.Text("", "viah-filename-text", "viah-ml8");
            fileBox.Add(fileLabel);
            banner.Add(fileBox);

            var outRow = UIKit.Row("viah-mt8");
            outRow.Add(UIKit.Text("書き出し先", "viah-faint", "viah-mr8", "viah-nowrap"));
            var outLabel = UIKit.Text(ViahStorage.PrettyPath(PackageExporter.OutputDirOf(p)), "viah-dim", "viah-ellipsis", "viah-grow");
            outRow.Add(outLabel);
            outRow.Add(UIKit.Btn("変更…", () =>
            {
                var picked = EditorUtility.OpenFolderPanel("書き出し先を選ぶ", PackageExporter.OutputDirOf(p), "");
                if (string.IsNullOrEmpty(picked)) return;
                p.outputFolder = picked;
                ViahDatabase.SaveProfile(p);
                outLabel.text = ViahStorage.PrettyPath(picked);
            }, "small", "ghost"));
            outRow.Add(UIKit.Btn("開く", () =>
            {
                ViahStorage.OpenFolder(PackageExporter.OutputDirOf(p));
            }, "small", "ghost"));
            banner.Add(outRow);

            var actions = UIKit.Row("viah-mt16");
            var previewBtn = UIKit.Btn("Search Icon", "プレビュー", null);
            actions.Add(previewBtn);
            var fix = UIKit.Btn("editicon.sml", "修正", () => OpenFix(p), "viah-ml8");
            fix.tooltip = "エクスポートを中断して、このプロファイルの中身を編集します";
            actions.Add(fix);
            actions.Add(UIKit.Spacer());
            var exportBtn = UIKit.Btn("SaveAs", "エクスポート", () => ExportSingle(p, opts), "primary", "big");
            actions.Add(exportBtn);
            banner.Add(actions);
            c.Add(banner);

            var previewBox = UIKit.Div();
            c.Add(previewBox);
            bool previewOpen = false;
            previewBtn.clicked += () =>
            {
                previewOpen = !previewOpen;
                previewBox.Clear();
                ((Label)previewBtn.Children().Last()).text = previewOpen ? "プレビューを閉じる" : "プレビュー";
                if (previewOpen) previewBox.Add(BuildPreview(p));
            };

            void SetVersion(PkgVersion v)
            {
                opts.version = v;
                versions[p.id] = v;
                stepper.Value = v;
                Commit("バージョンを変更", "ver:" + p.id);
                if (!opts.useVersion)
                {
                    opts.useVersion = true;
                    UIKit.SetClass(verChip, "is-on", true);
                }
                Changed();
            }

            void Changed()
            {
                verChip.text = "Ver" + opts.version;
                UIKit.Show(verPanel, opts.useVersion);
                h.useVersion = opts.useVersion;
                h.useDate = opts.useDate;
                h.useTime = opts.useTime;
                h.useDebug = opts.useDebug;
                ViahDatabase.SaveSettings("名前に付ける要素を切り替え");
                UpdateFile();
            }

            void UpdateFile()
            {
                var now = DateTime.Now;
                dateChip.text = PackageNaming.DateText(now);
                timeChip.text = PackageNaming.TimeText(now);
                fileLabel.text = PackageNaming.Build(p.PackageBaseName, opts, now) + ".unitypackage";
            }

            clockTargets.Add(UpdateFile);
            UIKit.Show(verPanel, opts.useVersion);
            UpdateFile();
        }

        VisualElement BuildPreview(PackageProfile p)
        {
            var card = UIKit.Card();
            card.Add(UIKit.SectionHead(null, "プレビュー", "いまの状態で書き出したときの中身です"));
            var report = ProfileResolver.Validate(p);
            card.Add(Issues(report, p));
            card.Add(FileList.Build(report.files, ProfileResolver.BaseFolderOf(p)));
            return card;
        }

        VisualElement Issues(ValidationReport r, PackageProfile p)
        {
            var box = UIKit.Div();
            if (!r.HasWarnings && !r.IsEmpty)
            {
                box.Add(UIKit.Hint(null, "問題は見つかりませんでした", "登録した中身と、プレハブが実際に使っているファイルが一致しています。", "success"));
                return box;
            }
            if (r.IsEmpty) box.Add(UIKit.Hint(null, "含めるファイルがありません", "「修正」から中身を選んでください。", "danger"));
            if (r.newDeps.Count > 0)
                box.Add(UIKit.Hint(null, $"使われているのに登録されていないファイルが {r.newDeps.Count} 件あります",
                    "入れ忘れの可能性があります。「修正」を押すと、自動でチェックした状態で確認できます。\n" + string.Join("\n", r.newDeps.Take(6)) + (r.newDeps.Count > 6 ? $"\n…ほか {r.newDeps.Count - 6} 件" : ""), "warn"));
            if (r.newExternalDeps.Count > 0)
                box.Add(UIKit.Hint(null, $"基準フォルダーの外のファイルが新しく使われています（{r.newExternalDeps.Count} 件）",
                    "アバター本体や他の人の素材なら、含めないのが正解です。確認済みにするには「修正」で保存してください。\n" + string.Join("\n", r.newExternalDeps.Take(6)), "warn"));
            if (r.missing.Count > 0)
                box.Add(UIKit.Hint(null, $"登録したファイルが {r.missing.Count} 件見つかりません", string.Join("\n", r.missing.Take(6).Select(m => m.path)), "warn"));
            if (r.missingSeeds.Count > 0)
                box.Add(UIKit.Hint(null, "起点のプレハブが見つかりません", string.Join("\n", r.missingSeeds.Select(m => m.path)), "warn"));
            var fixRow = UIKit.Row("viah-mb8");
            fixRow.Add(UIKit.Btn("editicon.sml", "修正する", () => OpenFix(p), "small"));
            box.Add(fixRow);
            return box;
        }

        void OpenFix(PackageProfile p)
        {
            window.Show(new ProfileWizardPage(p, null, saved => window.Show(new ExportPage(saved.id))));
        }

        static double Clock => EditorApplication.timeSinceStartup;

        void ExportSingle(PackageProfile p, NameOptions opts)
        {
            var started = Clock;
            var now = DateTime.Now; // 押した瞬間の時刻を使う
            var fileName = PackageNaming.Build(p.PackageBaseName, opts, now);
            var report = ProfileResolver.Validate(p);

            if (report.IsEmpty)
            {
                window.ShowDialog("含めるファイルがありません", "このプロファイルには、書き出すファイルが 1 つもありません。", null,
                    new ViahWindow.DialogButton("閉じる", null, "ghost"),
                    new ViahWindow.DialogButton("修正する", () => OpenFix(p), "primary"));
                return;
            }
            if (report.HasWarnings)
            {
                // チェックに時間がかかったときは、離れていても気づけるように知らせる
                ViahSound.Attention("export", Clock - started);
                window.ShowDialog("書き出す前に確認してください", "プロファイルの登録と、実際に使われているファイルに差があります。", Issues(report, p),
                    new ViahWindow.DialogButton("やめる", null, "ghost"),
                    new ViahWindow.DialogButton("修正する", () => OpenFix(p)),
                    new ViahWindow.DialogButton("このまま書き出す", () => DoExport(p, opts, fileName, report, Clock), "primary"));
                return;
            }
            DoExport(p, opts, fileName, report, started);
        }

        /// <param name="started">かかった時間を測り始めた時刻（効果音を鳴らすか決めるため）</param>
        void DoExport(PackageProfile p, NameOptions opts, string fileName, ValidationReport report, double started)
        {
            var path = PackageExporter.OutputPathOf(p, fileName);
            if (File.Exists(path) && !EditorUtility.DisplayDialog("上書きの確認", $"{Path.GetFileName(path)} は既にあります。上書きしますか？", "上書きする", "やめる"))
                return;
            try
            {
                PackageExporter.Export(report.files, path);
                PackageExporter.Record(p, opts, Path.GetFileName(path));
                versions.Remove(p.id);
                Render();
                window.Toast($"書き出しました：{Path.GetFileName(path)}（{report.files.Count} ファイル）", "", "フォルダーを開く", () => ViahStorage.RevealFile(path));
                ViahSound.Done("export", Clock - started);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                window.Toast("書き出しに失敗しました：" + e.Message, "danger");
                ViahSound.Attention("export", Clock - started);
            }
        }

        // ───────────────────────── シーケンス ─────────────────────────

        void RenderSequence()
        {
            var st = ViahDatabase.Settings;
            var profiles = ViahDatabase.Profiles;
            seqChecked.RemoveWhere(id => profiles.All(p => p.id != id));

            var scroll = UIKit.PageScroll();
            content.Add(scroll);
            var c = scroll.contentContainer;

            c.Add(UIKit.Hint("export.seq", "シーケンスエクスポートとは",
                "チェックした複数のプロファイルを、上から順番に自動で書き出します。行をクリックで選択、Shift＋クリックで範囲選択。名前に付ける要素はすべてに共通で、日付・時刻は「エクスポート」を押した瞬間の値を全部に使います（待っているあいだに後ろの方だけ時刻がずれることはありません）。"));

            var opts = new NameOptions
            {
                useDebug = st.seqUseDebug,
                debugLabel = st.debugLabel,
                useVersion = st.seqUseVersion,
                useDate = st.seqUseDate,
                useTime = st.seqUseTime,
            };

            Label dateChip = null, timeChip = null, countLabel = null, summary = null;
            CheckBox allBox = null;
            Button bump = null, run = null;
            VisualElement rowsBox = null;
            int anchor = -1;
            var fileLabels = new List<(Label label, PackageProfile p)>();

            var common = UIKit.Card();
            common.Add(UIKit.SectionHead(null, "名前に付ける要素（すべてに共通）"));
            var chips = UIKit.Div("viah-banner-line");
            chips.Add(UIKit.Text("プロファイル名", "viah-dim", "viah-mr8", "viah-nowrap"));
            dateChip = Chip(PackageNaming.DateText(DateTime.Now), opts.useDate, on => { opts.useDate = on; Save(); }, "日付を付ける / 外す");
            timeChip = Chip(PackageNaming.TimeText(DateTime.Now), opts.useTime, on => { opts.useTime = on; Save(); }, "時刻を付ける / 外す");
            chips.Add(Chip(opts.debugLabel, opts.useDebug, on => { opts.useDebug = on; Save(); }, "種別を付ける / 外す", "debug"));
            chips.Add(Joint(" "));
            chips.Add(Chip("Ver（各行）", opts.useVersion, on => { opts.useVersion = on; Save(); RenderRows(); }, "バージョンを付ける / 外す（番号は行ごとに決めます）"));
            chips.Add(Joint(" "));
            chips.Add(dateChip);
            chips.Add(Joint(" "));
            chips.Add(timeChip);
            common.Add(chips);
            c.Add(common);

            var listCard = UIKit.Card();
            var head = UIKit.Row("viah-mb8");
            allBox = new CheckBox();
            allBox.tooltip = "すべて選択 / 解除";
            head.Add(allBox);
            countLabel = UIKit.Text("", "viah-bold", "viah-ml8");
            head.Add(countLabel);
            head.Add(UIKit.Spacer());
            bump = UIKit.Btn("選択中のバージョンを +0.0.1", () =>
            {
                foreach (var p in profiles.Where(x => seqChecked.Contains(x.id))) versions[p.id] = VersionOf(p).Step(2, +1);
                Commit("バージョンを一括で上げる");
                RenderRows();
            }, "small");
            bump.tooltip = "チェックした行の最下位（0.0.1 の位）をまとめて 1 つ上げます";
            head.Add(bump);
            head.Add(UIKit.Btn("前回と同じに戻す", () =>
            {
                foreach (var p in profiles.Where(x => seqChecked.Contains(x.id))) versions[p.id] = PackageExporter.LastVersion(p) ?? PkgVersion.First;
                Commit("バージョンを前回と同じに戻す");
                RenderRows();
            }, "small", "ghost", "viah-ml4"));
            listCard.Add(head);
            rowsBox = UIKit.Div();
            rowsBox.style.borderTopWidth = 1;
            rowsBox.AddToClassList("viah-seq-list");
            listCard.Add(rowsBox);
            c.Add(listCard);

            var footer = UIKit.Div("viah-footer-bar");
            summary = UIKit.Text("", "viah-dim", "viah-grow", "viah-wrap");
            footer.Add(summary);
            run = UIKit.Btn("SaveAs", "エクスポート", () => RunSequence(opts), "primary", "big");
            footer.Add(run);
            content.Add(footer);

            allBox.RegisterCallback<ClickEvent>(_ =>
            {
                if (seqChecked.Count == profiles.Count) seqChecked.Clear();
                else foreach (var p in profiles) seqChecked.Add(p.id);
                Commit("すべて選択 / 解除");
                RenderRows();
            });

            void Save()
            {
                st.seqUseDebug = opts.useDebug;
                st.seqUseVersion = opts.useVersion;
                st.seqUseDate = opts.useDate;
                st.seqUseTime = opts.useTime;
                ViahDatabase.SaveSettings("名前に付ける要素を切り替え");
                UpdateFiles();
            }

            void RenderRows()
            {
                rowsBox.Clear();
                fileLabels.Clear();
                for (int i = 0; i < profiles.Count; i++)
                {
                    var p = profiles[i];
                    int index = i;
                    var row = UIKit.Row("viah-seq-row");
                    bool on = seqChecked.Contains(p.id);
                    UIKit.SetClass(row, "is-on", on);
                    var cb = new CheckBox();
                    cb.Set(on ? 1 : 0);
                    row.Add(cb);
                    var name = UIKit.Text(p.name, "viah-seq-name");
                    name.tooltip = p.name;
                    row.Add(name);
                    if (opts.useVersion)
                    {
                        var vs = new VersionStepper(VersionOf(p), true);
                        vs.changed += v =>
                        {
                            versions[p.id] = v;
                            Commit("バージョンを変更", "ver:" + p.id);
                            UpdateFiles();
                        };
                        vs.RegisterCallback<ClickEvent>(e => e.StopPropagation());
                        row.Add(vs);
                        var last = PackageExporter.LastVersion(p);
                        row.Add(UIKit.Text(last.HasValue ? $"前回 {last.Value}" : "初回", "viah-faint", "viah-small", "viah-ml8"));
                    }
                    var file = UIKit.Text("", "viah-seq-file");
                    row.Add(file);
                    fileLabels.Add((file, p));
                    row.RegisterCallback<ClickEvent>(e =>
                    {
                        bool target = !seqChecked.Contains(p.id);
                        if (e.shiftKey && anchor >= 0 && anchor != index)
                        {
                            target = seqChecked.Contains(profiles[anchor].id);
                            int a = Math.Min(anchor, index), b = Math.Max(anchor, index);
                            for (int k = a; k <= b; k++)
                            {
                                if (target) seqChecked.Add(profiles[k].id);
                                else seqChecked.Remove(profiles[k].id);
                            }
                        }
                        else
                        {
                            if (target) seqChecked.Add(p.id);
                            else seqChecked.Remove(p.id);
                            anchor = index;
                        }
                        Commit("書き出すプロファイルを選択");
                        RenderRows();
                    });
                    rowsBox.Add(row);
                }
                int n = profiles.Count(p => seqChecked.Contains(p.id));
                allBox.Set(n == 0 ? 0 : n == profiles.Count ? 1 : 2);
                countLabel.text = $"選択 {n} / {profiles.Count}";
                bump.SetEnabled(n > 0 && opts.useVersion);
                run.SetEnabled(n > 0);
                ((Label)run.Children().Last()).text = n > 0 ? $"{n} 件をエクスポート" : "エクスポート";
                summary.text = n > 0
                    ? $"上から順に {n} 件を書き出します。書き出す前に、すべてのプロファイルをまとめてチェックします。"
                    : "書き出すプロファイルにチェックを入れてください（Shift＋クリックで範囲選択）。";
                UpdateFiles();
            }

            void UpdateFiles()
            {
                var now = DateTime.Now;
                dateChip.text = PackageNaming.DateText(now);
                timeChip.text = PackageNaming.TimeText(now);
                foreach (var (label, p) in fileLabels)
                {
                    var o = opts;
                    o.version = VersionOf(p);
                    label.text = "→ " + PackageNaming.Build(p.PackageBaseName, o, now) + ".unitypackage";
                }
            }

            clockTargets.Add(UpdateFiles);
            RenderRows();
        }

        class SeqItem
        {
            public PackageProfile profile;
            public NameOptions opts;
            public string fileName;
            public string path;
            public ValidationReport report;
            public bool overwrite;
        }

        void RunSequence(NameOptions common)
        {
            var started = Clock;
            var now = DateTime.Now; // 全部この時刻で名前を付ける
            var targets = ViahDatabase.Profiles.Where(p => seqChecked.Contains(p.id)).ToList();
            if (targets.Count == 0) return;

            var items = new List<SeqItem>();
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var p = targets[i];
                    EditorUtility.DisplayProgressBar("シーケンスエクスポート", $"書き出し前のチェック中… {p.name}（{i + 1}/{targets.Count}）", (float)i / targets.Count);
                    var o = common;
                    o.version = VersionOf(p);
                    var fileName = PackageNaming.Build(p.PackageBaseName, o, now);
                    var path = PackageExporter.OutputPathOf(p, fileName);
                    items.Add(new SeqItem
                    {
                        profile = p,
                        opts = o,
                        fileName = fileName,
                        path = path,
                        report = ProfileResolver.Validate(p, true, false),
                        overwrite = File.Exists(path),
                    });
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // 同じ名前が並んだら後のものが前のものを上書きしてしまう
            var dupNames = items.GroupBy(x => x.path, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            bool needConfirm = items.Any(x => x.report.HasWarnings || x.report.IsEmpty || x.overwrite) || dupNames.Count > 0;
            if (!needConfirm)
            {
                Execute(items, started);
                return;
            }
            ViahSound.Attention("seq-export", Clock - started);

            var body = UIKit.Div();
            foreach (var it in items)
            {
                var row = UIKit.Div("viah-check-item");
                var dot = UIKit.Div("viah-status-dot");
                string status;
                if (it.report.IsEmpty)
                {
                    dot.AddToClassList("danger");
                    status = "含めるファイルがないためスキップします";
                }
                else if (it.report.HasWarnings)
                {
                    dot.AddToClassList("warn");
                    status = $"警告 {it.report.WarningCount} 件：" +
                             string.Join("、", new[]
                             {
                                 it.report.newDeps.Count > 0 ? $"未登録の依存 {it.report.newDeps.Count}" : null,
                                 it.report.newExternalDeps.Count > 0 ? $"新しい外部依存 {it.report.newExternalDeps.Count}" : null,
                                 it.report.missing.Count > 0 ? $"見つからない登録 {it.report.missing.Count}" : null,
                                 it.report.missingSeeds.Count > 0 ? $"見つからない起点 {it.report.missingSeeds.Count}" : null,
                             }.Where(s => s != null));
                }
                else
                {
                    status = "問題なし";
                }
                if (it.overwrite) status += "　・　同じ名前のファイルを上書きします";
                if (dupNames.Contains(it.path)) status += "　・　ほかのプロファイルと同じファイル名です";
                if ((it.overwrite || dupNames.Contains(it.path)) && !dot.ClassListContains("danger") && !dot.ClassListContains("warn")) dot.AddToClassList("info");
                row.Add(dot);
                var col = UIKit.Div("viah-grow");
                col.Add(UIKit.Text(it.profile.name, "viah-bold"));
                col.Add(UIKit.Text(it.fileName + ".unitypackage", "viah-faint", "viah-small", "viah-wrap"));
                col.Add(UIKit.Text(status, "viah-dim", "viah-small", "viah-wrap"));
                row.Add(col);
                if (it.report.HasWarnings || it.report.IsEmpty)
                {
                    var prof = it.profile;
                    row.Add(UIKit.Btn("修正", () =>
                    {
                        window.CloseDialog();
                        OpenFix(prof);
                    }, "small", "viah-ml8"));
                }
                body.Add(row);
            }
            int runnable = items.Count(x => !x.report.IsEmpty);
            window.ShowDialog("書き出す前のチェック",
                "問題のあるプロファイルがあります。内容を確認して、このまま書き出すか、修正してください。始めたら最後まで止まりません。", body,
                new ViahWindow.DialogButton("やめる", null, "ghost"),
                new ViahWindow.DialogButton($"このまま {runnable} 件を書き出す", () => Execute(items, Clock), "primary"));
        }

        void Execute(List<SeqItem> items, double started)
        {
            var results = new List<(SeqItem item, string error)>();
            var run = items.Where(x => !x.report.IsEmpty).ToList();
            try
            {
                for (int i = 0; i < run.Count; i++)
                {
                    var it = run[i];
                    EditorUtility.DisplayProgressBar("シーケンスエクスポート", $"{i + 1}/{run.Count}：{it.fileName}", (float)i / run.Count);
                    try
                    {
                        PackageExporter.Export(it.report.files, it.path);
                        PackageExporter.Record(it.profile, it.opts, Path.GetFileName(it.path));
                        versions.Remove(it.profile.id);
                        results.Add((it, null));
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        results.Add((it, e.Message));
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            foreach (var skipped in items.Where(x => x.report.IsEmpty)) results.Add((skipped, "含めるファイルがないためスキップ"));

            Render();
            var body = UIKit.Div();
            foreach (var (it, err) in results)
            {
                var row = UIKit.Div("viah-check-item");
                var dot = UIKit.Div("viah-status-dot");
                if (err != null) dot.AddToClassList("danger");
                row.Add(dot);
                var col = UIKit.Div("viah-grow");
                col.Add(UIKit.Text(it.fileName + ".unitypackage", "viah-bold", "viah-wrap"));
                col.Add(UIKit.Text(err ?? $"{it.report.files.Count} ファイル", err != null ? "viah-msg-error" : "viah-faint", "viah-small"));
                row.Add(col);
                body.Add(row);
            }
            int ok = results.Count(r => r.error == null);
            var firstPath = results.FirstOrDefault(r => r.error == null).item?.path;
            if (results.Any(r => r.error != null && !r.item.report.IsEmpty)) ViahSound.Attention("seq-export", Clock - started);
            else ViahSound.Done("seq-export", Clock - started);
            window.ShowDialog(ok == results.Count ? $"{ok} 件を書き出しました" : $"{ok} / {results.Count} 件を書き出しました", null, body,
                new ViahWindow.DialogButton("閉じる", null, "ghost"),
                new ViahWindow.DialogButton("書き出し先を開く", () =>
                {
                    if (firstPath != null) ViahStorage.RevealFile(firstPath);
                }, "primary"));
        }
    }
}

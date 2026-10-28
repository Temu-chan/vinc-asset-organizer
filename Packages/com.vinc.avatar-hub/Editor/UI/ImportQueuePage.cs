using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>
    /// ④ シーケンスインポート：複数の unitypackage を上から順番にまとめてインポートする。
    /// 黄色の「!」（同じ名前の別のファイル）があったら止めて確認するか、無視して入れるかを選べる。
    /// 行ごとに「プロファイルも作る」を選べ、作ったプロファイルは自動で保存して最後に一覧で確認する。
    /// </summary>
    public class ImportQueuePage : Page
    {
        public override string Section => "import";

        static ImportSession S => PackageImportRunner.Sequence;

        VisualElement queueBox;
        VisualElement progressBox;
        VisualElement resultBox;
        VisualElement footer;
        Segmented policy;

        // ───────────── Undo（キューの編集） ─────────────

        [Serializable]
        class UiState
        {
            public List<string> ids = new List<string>();
            public List<string> files = new List<string>();
            public List<bool> makeProfile = new List<bool>();
            public bool stopOnConflict;
        }

        public override string CaptureState()
        {
            var s = new UiState { stopOnConflict = S.stopOnConflict };
            foreach (var j in S.jobs)
            {
                s.ids.Add(j.id);
                s.files.Add(j.file);
                s.makeProfile.Add(j.makeProfile);
            }
            return JsonUtility.ToJson(s);
        }

        public override void RestoreState(string state)
        {
            // インポート中は並びを変えない（進み具合と合わなくなるため）
            if (S.running) return;
            var s = JsonUtility.FromJson<UiState>(state);
            var old = S.jobs.ToDictionary(j => j.id);
            var jobs = new List<ImportJob>();
            for (int i = 0; i < s.ids.Count; i++)
            {
                if (!old.TryGetValue(s.ids[i], out var j)) j = new ImportJob { id = s.ids[i], file = s.files[i] };
                j.makeProfile = s.makeProfile[i];
                jobs.Add(j);
            }
            S.jobs = jobs;
            S.stopOnConflict = s.stopOnConflict;
            PackageImportRunner.Save(S);
            policy?.Set(S.stopOnConflict ? 0 : 1);
        }

        // ───────────── 画面 ─────────────

        public override void Build()
        {
            var page = UIKit.Div();
            page.style.flexGrow = 1;
            root.Add(page);
            var scroll = UIKit.PageScroll();
            page.Add(scroll);
            var c = scroll.contentContainer;

            c.Add(UIKit.Text("シーケンスインポート", "viah-page-title"));
            c.Add(UIKit.Text("複数の unitypackage を、上から順番にまとめてインポートします。", "viah-page-sub"));
            c.Add(UIKit.Hint("import.intro", "使い方",
                "unitypackage をドロップしてキューに並べ、「開始」を押します。黄色の「!」（同じ名前の別のファイルがある）が見つかったときは、止めて確認するか、無視して入れるかを選べます。「プロファイルも作る」をオンにした行は、インポートのあとに中身をそのままプロファイルとして登録します。"));

            var addCard = UIKit.Card();
            addCard.name = "card-add";
            addCard.Add(UIKit.SectionHead("1", "unitypackage を追加", "エクスプローラーや Project ウィンドウからドロップできます（複数可）"));
            addCard.Add(PackageUI.DropZone("ここに unitypackage をドラッグ＆ドロップ", "並べた順番にインポートします。あとから並べ替えもできます。", Add));
            var addRow = UIKit.Row("viah-mt8", "viah-btn-group");
            addRow.Add(UIKit.Btn("Toolbar Plus", "ファイルを追加…", () =>
            {
                var f = PackageUI.PickFile();
                if (f != null) Add(new List<string> { f });
            }, "small"));
            addRow.Add(UIKit.Btn("Folder Icon", "フォルダーからまとめて追加…", () =>
            {
                var files = PackageUI.PickFolder();
                if (files.Count == 0) return;
                Add(files);
            }, "small", "viah-ml4"));
            addCard.Add(addRow);
            c.Add(addCard);

            var optCard = UIKit.Card();
            optCard.name = "card-policy";
            optCard.Add(UIKit.SectionHead("2", "黄色の「!」があったとき", "同じ名前（パス）の別のファイルがプロジェクトにあると、Unity はそのファイルを上書きします"));
            policy = new Segmented(new[] { "止めて確認する（おすすめ）", "無視して入れる" }, S.stopOnConflict ? 0 : 1, i =>
            {
                S.stopOnConflict = i == 0;
                PackageImportRunner.Save(S);
                Commit(i == 0 ? "衝突があったら止める" : "衝突を無視して入れる");
            });
            policy.style.alignSelf = Align.FlexStart;
            optCard.Add(policy);
            optCard.Add(UIKit.Text("止めたときは、その行で「Unity の画面で入れる（入れるファイルを選べます）」「このまま入れる（上書き）」「飛ばす」から選べます。黄色の「!」が無ければ、確認なしで次へ進みます。",
                "viah-faint", "viah-small", "viah-wrap", "viah-mt8"));
            c.Add(optCard);

            var queueCard = UIKit.Card();
            queueCard.name = "card-queue";
            queueBox = UIKit.Div();
            progressBox = UIKit.Div();
            queueCard.Add(progressBox);
            queueCard.Add(queueBox);
            c.Add(queueCard);

            resultBox = UIKit.Div();
            c.Add(resultBox);

            footer = UIKit.Div("viah-footer-bar");
            page.Add(footer);

            PackageImportRunner.Changed -= OnRunnerChanged;
            PackageImportRunner.Changed += OnRunnerChanged;
            Render();
        }

        public override void OnHide()
        {
            PackageImportRunner.Changed -= OnRunnerChanged;
        }

        /// <summary>表示されている間だけ描き直す（別の画面に移ったあとの通知は無視する）。</summary>
        void OnRunnerChanged()
        {
            if (root.panel != null) Render();
        }

        void Add(List<string> files)
        {
            int added = 0;
            foreach (var f in files)
            {
                // 同じファイルがまだ終わっていなければ重ねて入れない
                if (S.jobs.Any(j => !j.Finished && string.Equals(j.file, f, StringComparison.OrdinalIgnoreCase))) continue;
                S.jobs.Add(PackageImportRunner.NewJob(f, true));
                added++;
            }
            PackageImportRunner.Save(S);
            if (added > 0) Commit($"キューに {added} 件追加");
            if (added < files.Count) window.Toast($"{files.Count - added} 件はすでにキューにあります", "warn");
        }

        void Render()
        {
            if (queueBox == null) return;
            RenderProgress();
            RenderQueue();
            RenderResult();
            RenderFooter();
        }

        void RenderProgress()
        {
            progressBox.Clear();
            int total = S.jobs.Count;
            var head = UIKit.Row("viah-queue-head");
            head.Add(UIKit.Text($"キュー（{total} 件）", "viah-section-title"));
            head.Add(UIKit.Spacer());
            if (total > 0)
            {
                bool any = S.jobs.Any(j => !j.Finished);
                var allOn = S.jobs.Where(j => !j.Finished).All(j => j.makeProfile);
                var toggle = UIKit.Btn(allOn ? "プロファイルを作らない（全部）" : "プロファイルも作る（全部）", () =>
                {
                    foreach (var j in S.jobs.Where(x => !x.Finished && x.status != "importing" && x.status != "imported")) j.makeProfile = !allOn;
                    PackageImportRunner.Save(S);
                    Commit(allOn ? "プロファイルを作らない" : "プロファイルも作る");
                }, "small", "ghost");
                toggle.SetEnabled(any);
                head.Add(toggle);
                var clearDone = UIKit.Btn("終わったものを消す", () =>
                {
                    S.jobs.RemoveAll(j => j.Finished);
                    PackageImportRunner.Save(S);
                    Commit("終わったものをキューから消す");
                }, "small", "ghost", "viah-ml4");
                clearDone.SetEnabled(!S.running && S.jobs.Any(j => j.Finished));
                head.Add(clearDone);
                var clearAll = UIKit.Btn("すべて消す", () =>
                {
                    S.jobs.Clear();
                    PackageImportRunner.Save(S);
                    Commit("キューをすべて消す");
                }, "small", "ghost", "viah-ml4");
                clearAll.SetEnabled(!S.running);
                head.Add(clearAll);
            }
            progressBox.Add(head);
            if (total == 0) return;

            int done = S.DoneCount;
            int pct = Mathf.RoundToInt(done * 100f / total);
            var bar = UIKit.Div("viah-progress");
            var fill = UIKit.Div("viah-progress-fill");
            fill.style.width = Length.Percent(pct);
            bar.Add(fill);
            progressBox.Add(bar);
            var current = S.jobs.Find(j => j.status == "importing" || j.status == "imported");
            var text = $"{done} / {total} 件が終わりました（{pct}%）";
            if (current != null) text += $"　・　いま：{current.Name}（{PackageUI.StatusOf(current).text}）";
            else if (!S.running && S.jobs.Any(j => j.status == "conflict" || j.status == "profile-conflict")) text += "　・　確認待ちで止まっています";
            progressBox.Add(UIKit.Text(text, "viah-dim", "viah-small", "viah-mt4", "viah-wrap"));
        }

        void RenderQueue()
        {
            queueBox.Clear();
            if (S.jobs.Count == 0)
            {
                queueBox.Add(UIKit.Text("まだありません。上に unitypackage をドロップしてください。", "viah-faint", "viah-mt8"));
                return;
            }
            for (int i = 0; i < S.jobs.Count; i++) queueBox.Add(Row(S.jobs[i], i));
        }

        VisualElement Row(ImportJob j, int index)
        {
            var row = UIKit.Div("viah-queue-row");
            row.AddToClassList("is-" + j.status);
            var line = UIKit.Row("viah-queue-line");
            line.Add(UIKit.Text((index + 1).ToString(), "viah-queue-num"));
            var col = UIKit.Div("viah-grow", "viah-ml8");
            var title = UIKit.Row();
            title.Add(UIKit.Text(j.Name, "viah-bold", "viah-ellipsis"));
            var st = PackageUI.StatusOf(j);
            title.Add(UIKit.Badge(st.text, st.kind));
            col.Add(title);
            col.Add(UIKit.Text($"{PackageUI.SizeText(j.file)}　{PackageUI.ShowPath(j.file)}", "viah-faint", "viah-small", "viah-ellipsis"));
            if (!string.IsNullOrEmpty(j.message)) col.Add(UIKit.Text(j.message, "viah-small", "viah-wrap", j.status == "failed" ? "viah-msg-error" : "viah-msg-warn"));
            line.Add(col);

            bool editable = j.status == "waiting" || j.status == "conflict" || j.status == "cancelled";
            var make = new Switch("プロファイルも作る", j.makeProfile, "インポートのあと、入っていたファイルをそのままプロファイルとして登録します");
            make.SetEnabled(editable);
            make.changed += on =>
            {
                j.makeProfile = on;
                PackageImportRunner.Save(S);
                Commit(on ? "プロファイルも作る" : "プロファイルを作らない");
            };
            line.Add(make);

            var up = UIKit.Btn("↑", () => Move(index, -1), "small", "ghost", "viah-ml8", "viah-move-up").With(b => b.tooltip = "上へ");
            up.SetEnabled(editable && index > 0);
            line.Add(up);
            var down = UIKit.Btn("↓", () => Move(index, +1), "small", "ghost", "viah-move-down").With(b => b.tooltip = "下へ");
            down.SetEnabled(editable && index < S.jobs.Count - 1);
            line.Add(down);
            var remove = UIKit.Btn("×", () =>
            {
                S.jobs.Remove(j);
                PackageImportRunner.Save(S);
                Commit("キューから外す");
            }, "small", "ghost").With(b => b.tooltip = "キューから外す");
            remove.SetEnabled(j.status != "importing" && j.status != "imported");
            line.Add(remove);
            row.Add(line);

            if (j.status == "conflict")
            {
                var box = UIKit.Div("viah-queue-conflict");
                box.Add(UIKit.Text("次のファイルは、同じ名前の別のファイルがプロジェクトにあります。そのまま入れると上書きされます。", "viah-small", "viah-wrap"));
                box.Add(PackageUI.ConflictList(j.conflicts, j.conflictCount));
                var actions = UIKit.Div("viah-btn-group", "viah-mt8");
                actions.Add(UIKit.Btn("Unity の画面で入れる", () => PackageImportRunner.Resolve(S, j, "unity"), "small", "primary"));
                actions.Add(UIKit.Btn("このまま入れる（上書き）", () => PackageImportRunner.Resolve(S, j, "force"), "small", "viah-ml4"));
                actions.Add(UIKit.Btn("飛ばす", () => PackageImportRunner.Resolve(S, j, "skip"), "small", "ghost", "viah-ml4"));
                box.Add(actions);
                row.Add(box);
            }
            else if (j.status == "profile-conflict")
            {
                var box = UIKit.Div("viah-queue-conflict");
                box.Add(PackageUI.ProfileConflictInfo(window, j));
                var actions = UIKit.Div("viah-btn-group", "viah-mt8");
                actions.Add(UIKit.Btn("上書きする", () => PackageImportRunner.ResolveProfile(S, j, "overwrite"), "small", "primary"));
                actions.Add(UIKit.Btn($"別名で作る（{PackageUI.RenamedProfileName(j)}）", () => PackageImportRunner.ResolveProfile(S, j, "rename"), "small", "viah-ml4"));
                actions.Add(UIKit.Btn("作らない", () => PackageImportRunner.ResolveProfile(S, j, "skip"), "small", "ghost", "viah-ml4"));
                box.Add(actions);
                row.Add(box);
            }
            else if (j.status == "done" || (j.status == "skipped" && j.notes.Count > 0))
            {
                if (j.notes.Count > 0 || j.warnings.Count > 0 || !string.IsNullOrEmpty(j.profileId)) row.Add(PackageUI.JobResult(window, j));
            }
            return row;
        }

        void Move(int index, int dir)
        {
            int to = index + dir;
            if (to < 0 || to >= S.jobs.Count) return;
            var j = S.jobs[index];
            S.jobs.RemoveAt(index);
            S.jobs.Insert(to, j);
            PackageImportRunner.Save(S);
            Commit("キューを並べ替え");
        }

        void RenderResult()
        {
            resultBox.Clear();
            if (S.running || !S.Complete) return;
            var made = S.jobs.Where(j => !string.IsNullOrEmpty(j.profileId) && ViahDatabase.FindProfile(j.profileId) != null).ToList();
            int created = made.Count(j => !j.alreadyExists);
            int existed = made.Count - created;
            var card = UIKit.Card("accent");
            card.name = "card-result";
            int ok = S.jobs.Count(j => j.status == "done");
            int skipped = S.jobs.Count(j => j.status == "skipped");
            int failed = S.jobs.Count(j => j.status == "failed");
            var sub = $"インポート {ok} 件" + (skipped > 0 ? $"・飛ばした {skipped} 件" : "") + (failed > 0 ? $"・失敗 {failed} 件" : "") +
                      (created > 0 ? $"。プロファイルを {created} 件作って保存しました（Ctrl+Z で取り消せます）。" : "。") +
                      (existed > 0 ? $"中身がまったく同じプロファイルが既にあった {existed} 件は、新しく作っていません。" : "");
            card.Add(UIKit.SectionHead("✓", "すべて終わりました", sub));
            var warn = made.Where(j => j.HasWarning).ToList();
            if (warn.Count > 0)
                card.Add(UIKit.Hint(null, $"確認してほしいプロファイルが {warn.Count} 件あります", "「完了（要確認）」の行を見て、必要なら「中身を確認・修正」で直してください。", "warn"));
            foreach (var j in made)
            {
                var p = ViahDatabase.FindProfile(j.profileId);
                var row = UIKit.Row("viah-check-item");
                var dot = UIKit.Div("viah-status-dot");
                if (j.HasWarning) dot.AddToClassList("warn");
                row.Add(dot);
                var col = UIKit.Div("viah-grow");
                var title = UIKit.Row();
                title.Add(UIKit.Text(p.name, "viah-bold"));
                if (j.alreadyExists) title.Add(UIKit.Badge("既にあります", ""));
                col.Add(title);
                col.Add(UIKit.Text(j.HasWarning ? string.Join("\n", j.warnings) : $"{SelectionModel.FromProfile(p).ResolveFiles().Count} ファイル・{(p.mode == "auto" ? "起点のプレハブ " + p.seeds.Count + " 個" : "手動登録")}", "viah-faint", "viah-small", "viah-wrap"));
                row.Add(col);
                row.Add(UIKit.Btn("中身を確認・修正", () => window.Show(new ProfileWizardPage(p, null, saved => window.Show(new ImportQueuePage()))), "small", "ghost"));
                row.Add(UIKit.Btn("エクスポートへ", () => window.Show(new ExportPage(p.id)), "small", "viah-ml4"));
                card.Add(row);
            }
            resultBox.Add(card);
        }

        void RenderFooter()
        {
            footer.Clear();
            var conflict = S.jobs.Find(j => j.status == "conflict" || j.status == "profile-conflict");
            string note = S.running ? "インポート中です。終わるまで Unity の操作は控えてください（スクリプトが入っていると、途中でコンパイルが走ります）。"
                : conflict != null ? $"「{conflict.Name}」で止まっています。その行で入れ方を選んでください。"
                : PackageImportRunner.CannotStart(S) ?? "";
            if (S.Complete && !S.running) note = "";
            footer.Add(UIKit.Text(note, "viah-grow", "viah-wrap", S.running ? "viah-faint" : conflict != null ? "viah-msg-warn" : "viah-faint"));
            if (S.running)
            {
                footer.Add(UIKit.Btn("止める", () => PackageImportRunner.Pause(S), "big"));
                return;
            }
            if (S.Complete)
            {
                // すべて終わったら、次のために空にできるようにする
                footer.Add(UIKit.Btn("キューを空にする", () =>
                {
                    S.jobs.Clear();
                    PackageImportRunner.Save(S);
                    Commit("キューを空にする");
                }, "big"));
                return;
            }
            bool resumed = S.jobs.Any(j => j.Finished);
            var start = UIKit.Btn(resumed ? "続きを開始 ▶" : "開始 ▶", () =>
            {
                // キャンセルした行はもう一度試す
                foreach (var j in S.jobs.Where(x => x.status == "cancelled")) j.status = "waiting";
                PackageImportRunner.Start(S);
            }, "primary", "big");
            bool canStart = conflict == null && S.jobs.Any(j => j.status == "waiting" || j.status == "cancelled") &&
                            (PackageImportRunner.Active == null) && !EditorApplication.isPlayingOrWillChangePlaymode;
            start.SetEnabled(canStart);
            footer.Add(start);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>
    /// UnityPackage からプロファイルを作る（手動で作った unitypackage からの移行用）。
    /// 入っていたファイルをそのまま登録する。まだインポートしていなければ自動でインポートし、終わったらプロファイルを作る。
    /// 黄色の「!」（同じ名前の別のファイル）があるときだけ、入れ方を選んでもらう。
    /// </summary>
    public class PackageProfilePage : Page
    {
        /// <summary>ドメインリロードのあとにこの画面へ戻るためのキー。</summary>
        public const string Key = "profiles-package";

        public override string Section => "profiles";
        public override string RestoreKey => Key;

        static ImportSession S => PackageImportRunner.Single;
        ImportJob Job => S.jobs.FirstOrDefault();

        PackageAnalysis analysis;
        VisualElement body;
        VisualElement footer;

        /// <param name="resume">実行中・実行後の状態をそのまま表示する（リロード後など）</param>
        public PackageProfilePage(bool resume = false)
        {
            if (!resume && !S.running)
            {
                S.jobs.Clear();
                PackageImportRunner.Save(S);
            }
        }

        public override void Build()
        {
            var page = UIKit.Div();
            page.style.flexGrow = 1;
            root.Add(page);
            var scroll = UIKit.PageScroll();
            page.Add(scroll);
            var c = scroll.contentContainer;

            var back = UIKit.Row("viah-back");
            back.Add(UIKit.Text("‹ プロファイル一覧へ"));
            UIKit.Clickable(back, () => window.Show(new ProfilesPage(Job?.profileId)));
            c.Add(back);
            c.Add(UIKit.Text("UnityPackage からプロファイルを作る", "viah-page-title"));
            c.Add(UIKit.Text("手動で作った unitypackage に入っていたファイルを、そのままプロファイルとして登録します。インポートしていなければ自動でインポートします。", "viah-page-sub"));
            body = UIKit.Div();
            c.Add(body);
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

        void Select(List<string> files)
        {
            if (files.Count > 1)
            {
                // 複数ならシーケンスインポートでまとめて作る
                window.ShowDialog("まとめて作りますか？", $"{files.Count} 個の unitypackage が選ばれました。シーケンスインポートに並べて、順番にインポートしながらプロファイルを作ります。", null,
                    new ViahWindow.DialogButton("やめる", null, "ghost"),
                    new ViahWindow.DialogButton("シーケンスインポートで作る", () =>
                    {
                        var seq = PackageImportRunner.Sequence;
                        foreach (var f in files)
                            if (!seq.jobs.Any(j => !j.Finished && string.Equals(j.file, f, StringComparison.OrdinalIgnoreCase)))
                                seq.jobs.Add(PackageImportRunner.NewJob(f, true));
                        PackageImportRunner.Save(seq);
                        window.Show(new ImportQueuePage());
                    }, "primary"));
                return;
            }
            S.jobs.Clear();
            S.jobs.Add(PackageImportRunner.NewJob(files[0], true));
            S.stopOnConflict = true;
            analysis = null;
            PackageImportRunner.Save(S);
        }

        void Render()
        {
            if (body == null) return;
            body.Clear();
            footer.Clear();
            var job = Job;
            if (job == null) RenderPick();
            else if (job.status == "waiting" && !S.running) RenderCheck(job);
            else if (job.status == "done") RenderDone(job);
            else if (job.status == "profile-conflict" && !S.running) RenderProfileConflict(job);
            else if (S.running || job.status == "importing" || job.status == "imported") RenderRunning(job);
            else RenderStopped(job);
        }

        void RenderPick()
        {
            var card = UIKit.Card();
            card.name = "card-pick";
            card.Add(UIKit.SectionHead("1", "unitypackage を選んでください", "エクスプローラーや Project ウィンドウからドロップできます。複数を選ぶと、シーケンスインポートでまとめて作れます"));
            card.Add(PackageUI.DropZone("ここに unitypackage をドラッグ＆ドロップ", "中身を読むだけで、まだインポートはしません。", Select));
            var row = UIKit.Row("viah-mt8");
            row.Add(UIKit.Btn("Folder Icon", "ファイルを選ぶ…", () =>
            {
                var f = PackageUI.PickFile();
                if (f != null) Select(new List<string> { f });
            }, "small"));
            card.Add(row);
            body.Add(card);
            footer.Add(UIKit.Text("インポートしたあとに移動・名前変更したファイルも、GUID（Unity の内部 ID）で追いかけて登録します。", "viah-faint", "viah-grow", "viah-wrap"));
        }

        void RenderCheck(ImportJob job)
        {
            if (analysis == null || analysis.contents.file != job.file)
            {
                try
                {
                    analysis = PackageAnalysis.Read(job.file);
                }
                catch (Exception e)
                {
                    body.Add(UIKit.Hint(null, "unitypackage として読めませんでした", e.Message, "danger"));
                    footer.Add(UIKit.Spacer());
                    footer.Add(UIKit.Btn("別のファイルを選ぶ", Reset, "big"));
                    return;
                }
            }
            var a = analysis;

            var fileCard = UIKit.Card();
            fileCard.name = "card-check";
            var head = UIKit.Row("viah-mb8");
            head.Add(UIKit.Img("Package Manager"));
            var col = UIKit.Div("viah-grow", "viah-ml8");
            col.Add(UIKit.Text(job.Name, "viah-section-title", "viah-ellipsis"));
            col.Add(UIKit.Text($"{PackageUI.SizeText(job.file)}　{PackageUI.ShowPath(job.file)}", "viah-faint", "viah-small", "viah-ellipsis"));
            head.Add(col);
            head.Add(UIKit.Btn("別のファイルを選ぶ", Reset, "small", "ghost"));
            fileCard.Add(head);
            fileCard.Add(UIKit.SectionHead("2", $"中身：ファイル {a.FileCount} 件", "プロジェクトと照らし合わせた結果です"));
            fileCard.Add(PackageUI.Summary(a));
            body.Add(fileCard);

            var name = PackageProfileBuilder.ParseName(job.Name, out var ver);
            var planCard = UIKit.Card();
            planCard.Add(UIKit.SectionHead("3", "作るプロファイル"));
            planCard.Add(UIKit.KeyValue("名前", name));
            planCard.Add(UIKit.KeyValue("前回のバージョン", ver != null ? $"Ver{ver}（次のエクスポートはこの続きから）" : "ファイル名に無いので、初回の 1.0.0 から"));
            planCard.Add(UIKit.KeyValue("登録のしかた", "入っていたファイルだけ（フォルダーごとにはしません）"));
            planCard.Add(UIKit.KeyValue("起点のプレハブ", "ほかのプレハブから使われていないプレハブを自動で登録（次からの入れ忘れチェックに使います）"));
            planCard.Add(UIKit.Text("作ったプロファイルはすぐに保存します（Ctrl+Z で取り消せます）。名前や中身はあとから直せます。", "viah-faint", "viah-small", "viah-wrap", "viah-mt8"));
            body.Add(planCard);

            var conflicts = a.Conflicts;
            if (conflicts.Count > 0)
            {
                var warn = UIKit.Card("warn-card");
                warn.name = "card-conflict";
                warn.Add(UIKit.SectionHead("!", $"黄色の「!」が {conflicts.Count} 件あります", "同じ名前の別のファイルがプロジェクトにあります。そのまま入れると、プロジェクトのファイルが上書きされます"));
                warn.Add(PackageUI.ConflictList(conflicts.Select(x => x.entry.path).ToList(), conflicts.Count));
                body.Add(warn);

                footer.Add(UIKit.Btn("やめる", Reset, "ghost"));
                footer.Add(UIKit.Spacer());
                footer.Add(UIKit.Btn("このまま入れる（上書き）", () => Run(job, "force"), "viah-mr8"));
                footer.Add(UIKit.Btn("Unity の画面で入れる", () => Run(job, "unity"), "primary", "big"));
                return;
            }

            bool needImport = a.NeedsImport;
            footer.Add(UIKit.Text(needImport ? $"まだ入っていない {a.Count(PackageItemState.Missing)} 件をインポートしてから作ります。" : "すべてプロジェクトに入っているので、インポートはしません。",
                "viah-faint", "viah-grow", "viah-wrap"));
            var go = UIKit.Btn(needImport ? "インポートしてプロファイルを作る ▶" : "プロファイルを作る ▶", () => Run(job, null), "primary", "big");
            go.SetEnabled(a.FileCount > 0 && PackageImportRunner.CannotStart(S) == null);
            go.tooltip = PackageImportRunner.CannotStart(S) ?? "";
            footer.Add(go);
        }

        void Run(ImportJob job, string choice)
        {
            job.force = choice == "force";
            job.interactive = choice == "unity";
            PackageImportRunner.Start(S);
        }

        void RenderRunning(ImportJob job)
        {
            var card = UIKit.Card();
            card.Add(UIKit.SectionHead(null, job.status == "imported" ? "プロファイルを作っています…" : job.interactive ? "Unity のインポート画面で確認してください" : "インポートしています…",
                job.interactive && job.status == "importing"
                    ? "入れるファイルを選んで「Import」を押すと、続けてプロファイルを作ります。「Cancel」で中止します。"
                    : "スクリプトが入っていると、途中でコンパイルが走ります。終わるまでお待ちください。"));
            card.Add(UIKit.Text(job.Name, "viah-bold"));
            body.Add(card);
            footer.Add(UIKit.Text("終わると、この画面に結果が出ます。", "viah-faint", "viah-grow"));
        }

        /// <summary>同じ名前で中身が違うプロファイルがあるとき：上書きするか、別名で作るか。</summary>
        void RenderProfileConflict(ImportJob job)
        {
            var card = UIKit.Card("warn-card");
            card.name = "card-profile-conflict";
            card.Add(UIKit.SectionHead("!", job.message, "インポートは終わっています。プロファイルをどうするか選んでください"));
            card.Add(PackageUI.ProfileConflictInfo(window, job));
            body.Add(card);
            footer.Add(UIKit.Btn("作らない", () => PackageImportRunner.ResolveProfile(S, job, "skip"), "ghost"));
            footer.Add(UIKit.Spacer());
            footer.Add(UIKit.Btn($"別名で作る（{PackageUI.RenamedProfileName(job)}）", () => PackageImportRunner.ResolveProfile(S, job, "rename"), "viah-mr8"));
            footer.Add(UIKit.Btn("上書きする", () => PackageImportRunner.ResolveProfile(S, job, "overwrite"), "primary", "big"));
        }

        void RenderStopped(ImportJob job)
        {
            body.Add(UIKit.Hint(null, job.status == "cancelled" ? "インポートをキャンセルしました" : "インポートできませんでした",
                string.IsNullOrEmpty(job.message) ? "もう一度試すか、別のファイルを選んでください。" : job.message, job.status == "failed" ? "danger" : "warn"));
            footer.Add(UIKit.Btn("別のファイルを選ぶ", Reset, "ghost"));
            footer.Add(UIKit.Spacer());
            footer.Add(UIKit.Btn("もう一度", () =>
            {
                job.status = "waiting";
                job.message = "";
                analysis = null;
                PackageImportRunner.Save(S);
            }, "primary", "big"));
        }

        void RenderDone(ImportJob job)
        {
            var p = ViahDatabase.FindProfile(job.profileId);
            var card = UIKit.Card(job.HasWarning ? "warn-card" : "accent");
            card.name = "card-done";
            if (p != null && job.alreadyExists)
            {
                // まったく同じものが既にある：作らずに知らせる
                card = UIKit.Card("accent");
                card.name = "card-done";
                card.Add(UIKit.SectionHead("✓", $"プロファイル「{p.name}」が既にあります", "このパッケージと中身がまったく同じなので、新しくは作りませんでした。"));
                var exists = UIKit.Div("viah-btn-group", "viah-mt8");
                exists.Add(UIKit.Btn("このままエクスポートへ", () => window.Show(new ExportPage(p.id)), "primary"));
                exists.Add(UIKit.Btn("プロファイルを開く", () => window.Show(new ProfilesPage(p.id)), "viah-ml4"));
                card.Add(exists);
                body.Add(card);
                footer.Add(UIKit.Spacer());
                footer.Add(UIKit.Btn("別のファイルを選ぶ", Reset, "big"));
                return;
            }
            if (p == null)
            {
                card.Add(UIKit.SectionHead("!", "プロファイルは作りませんでした", string.Join("\n", job.warnings.Concat(job.notes))));
                body.Add(card);
                footer.Add(UIKit.Spacer());
                footer.Add(UIKit.Btn("別のファイルを選ぶ", Reset, "primary", "big"));
                return;
            }
            bool overwritten = job.profileChoice == "overwrite";
            card.Add(UIKit.SectionHead(job.HasWarning ? "!" : "✓", overwritten ? $"プロファイル「{p.name}」を上書きしました" : $"プロファイル「{p.name}」を作りました",
                $"{SelectionModel.FromProfile(p).ResolveFiles().Count} ファイルを登録しました。" + (p.mode == "auto" ? $"起点のプレハブ {p.seeds.Count} 個で、次からの入れ忘れをチェックします。" : "プレハブが無かったので、手動登録として作りました。")));
            card.Add(PackageUI.JobResult(window, job));
            var actions = UIKit.Div("viah-btn-group", "viah-mt8");
            actions.Add(UIKit.Btn("このままエクスポートへ", () => window.Show(new ExportPage(p.id)), "primary"));
            actions.Add(UIKit.Btn("プロファイル一覧へ", () => window.Show(new ProfilesPage(p.id)), "viah-ml4"));
            card.Add(actions);
            body.Add(card);
            footer.Add(UIKit.Text("名前・タグ・中身は、プロファイル一覧の「中身を編集」からいつでも直せます。", "viah-faint", "viah-grow", "viah-wrap"));
            footer.Add(UIKit.Btn("もう 1 つ作る", Reset, "big"));
        }

        void Reset()
        {
            S.jobs.Clear();
            analysis = null;
            PackageImportRunner.Save(S);
        }
    }
}

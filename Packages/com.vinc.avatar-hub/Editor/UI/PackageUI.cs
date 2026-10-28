using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>unitypackage を扱う画面（UnityPackage から作る・シーケンスインポート）で共通の部品。</summary>
    public static class PackageUI
    {
        const string LastDirKey = "VInc.VIAH.packageDir";

        public static string LastDir
        {
            get => EditorPrefs.GetString(LastDirKey, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/Downloads");
            set => EditorPrefs.SetString(LastDirKey, value);
        }

        /// <summary>unitypackage を 1 つ選ぶ。キャンセルなら null。</summary>
        public static string PickFile()
        {
            var path = EditorUtility.OpenFilePanelWithFilters("unitypackage を選ぶ", LastDir, new[] { "Unity パッケージ", "unitypackage" });
            if (string.IsNullOrEmpty(path)) return null;
            LastDir = Path.GetDirectoryName(path);
            return Path.GetFullPath(path);
        }

        /// <summary>フォルダーを選んで、その中の unitypackage を名前順に返す（サブフォルダーは見ない）。</summary>
        public static List<string> PickFolder()
        {
            var dir = EditorUtility.OpenFolderPanel("unitypackage が入ったフォルダーを選ぶ", LastDir, "");
            if (string.IsNullOrEmpty(dir)) return new List<string>();
            LastDir = dir;
            return Directory.GetFiles(dir, "*.unitypackage", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Select(Path.GetFullPath).ToList();
        }

        /// <summary>ドラッグ中の unitypackage（エクスプローラーからでも Project ウィンドウからでも）。</summary>
        public static List<string> DraggedPackages()
        {
            var list = new List<string>();
            foreach (var p in DragAndDrop.paths ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(p) || !p.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) continue;
                var full = Path.GetFullPath(p.StartsWith("Assets/", StringComparison.Ordinal) ? Path.Combine(ViahStorage.ProjectRoot, p) : p);
                if (File.Exists(full) && !list.Contains(full)) list.Add(full);
            }
            return list;
        }

        /// <summary>unitypackage を落とせる場所。</summary>
        public static VisualElement DropZone(string title, string text, Action<List<string>> onFiles)
        {
            var zone = UIKit.Div("viah-dropzone");
            zone.Add(UIKit.Img("Package Manager", "viah-dropzone-icon"));
            zone.Add(UIKit.Text(title, "viah-dropzone-title"));
            zone.Add(UIKit.Text(text, "viah-dropzone-text"));
            zone.RegisterCallback<DragEnterEvent>(_ => zone.AddToClassList("is-hover"));
            zone.RegisterCallback<DragLeaveEvent>(_ => zone.RemoveFromClassList("is-hover"));
            zone.RegisterCallback<DragExitedEvent>(_ => zone.RemoveFromClassList("is-hover"));
            zone.RegisterCallback<DragUpdatedEvent>(_ =>
            {
                DragAndDrop.visualMode = DraggedPackages().Count > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            });
            zone.RegisterCallback<DragPerformEvent>(_ =>
            {
                zone.RemoveFromClassList("is-hover");
                var files = DraggedPackages();
                if (files.Count == 0) return;
                DragAndDrop.AcceptDrag();
                LastDir = Path.GetDirectoryName(files[0]);
                onFiles(files);
            });
            return zone;
        }

        /// <summary>画面に出すパス。「\」のままだと「\v」などが改行として表示されることがあるので「/」にする。</summary>
        public static string ShowPath(string file) => (file ?? "").Replace('\\', '/');

        public static string SizeText(string file)
        {
            try
            {
                double b = new FileInfo(file).Length;
                return b >= 1 << 30 ? $"{b / (1 << 30):0.0} GB" : b >= 1 << 20 ? $"{b / (1 << 20):0.0} MB" : $"{Math.Max(1, b / 1024):0} KB";
            }
            catch
            {
                return "見つかりません";
            }
        }

        /// <summary>中身とプロジェクトの照らし合わせ結果。</summary>
        public static VisualElement Summary(PackageAnalysis a)
        {
            var box = UIKit.Div("viah-pkg-summary");
            void Line(string label, int count, string kind, string help)
            {
                if (count == 0) return;
                var row = UIKit.Row("viah-pkg-line");
                row.Add(UIKit.Text(count.ToString(), "viah-pkg-count", kind));
                var col = UIKit.Div("viah-grow", "viah-ml8");
                col.Add(UIKit.Text(label, "viah-bold"));
                if (help != null) col.Add(UIKit.Text(help, "viah-faint", "viah-small", "viah-wrap"));
                row.Add(col);
                box.Add(row);
            }
            Line("プロジェクトに入っています", a.Count(PackageItemState.Same), "ok", null);
            Line("プロジェクトに入っています（場所が変わっています）", a.Count(PackageItemState.Moved), "ok",
                "インポートしたあとに移動・名前変更したファイルです。いまの場所で登録します。");
            Line("まだ入っていません", a.Count(PackageItemState.Missing), "info", "インポートしてから登録します。");
            Line("黄色の「!」（同じ名前の別のファイルがある）", a.Count(PackageItemState.Conflict), "warn",
                "Unity のインポート画面で黄色の「!」が付くものです。そのまま入れると、プロジェクトのファイルが上書きされます。");
            Line("Packages などのファイル", a.Count(PackageItemState.OutOfScope), "dim",
                "unitypackage に入れられない場所なので、プロファイルには入れません。");
            if (a.FileCount == 0 && a.Count(PackageItemState.OutOfScope) == 0)
                box.Add(UIKit.Text("ファイルが入っていません。", "viah-msg-warn"));
            return box;
        }

        /// <summary>黄色の「!」が付くファイルの一覧。</summary>
        public static VisualElement ConflictList(IList<string> paths, int total)
        {
            var box = UIKit.Div("viah-conflict-list");
            foreach (var p in paths.Take(8))
            {
                var row = UIKit.Row("viah-conflict-item");
                row.Add(UIKit.Img("console.warnicon.sml"));
                row.Add(UIKit.Text(p, "viah-ml6", "viah-ellipsis"));
                row.tooltip = p + "\nダブルクリックで Project ウィンドウに表示";
                var path = p;
                row.RegisterCallback<ClickEvent>(e =>
                {
                    if (e.clickCount == 2) UIKit.Reveal(path);
                });
                box.Add(row);
            }
            if (total > 8) box.Add(UIKit.Text($"…ほか {total - 8} 件", "viah-faint", "viah-small", "viah-mt4"));
            return box;
        }

        public static (string text, string kind) StatusOf(ImportJob j)
        {
            switch (j.status)
            {
                case "importing": return ("インポート中", "rule");
                case "imported": return (j.makeProfile ? "プロファイル作成中" : "仕上げ中", "rule");
                case "done":
                    if (j.alreadyExists) return ("完了（既にあります）", "done");
                    return (j.HasWarning ? "完了（要確認）" : "完了", j.HasWarning ? "warn" : "done");
                case "conflict": return ("衝突で停止", "warn");
                case "profile-conflict": return ("同じ名前のプロファイル", "warn");
                case "skipped": return ("飛ばしました", "");
                case "failed": return ("失敗", "danger");
                case "cancelled": return ("キャンセル", "warn");
                default: return ("待機中", "");
            }
        }

        /// <summary>「別名で作る」で付く名前（例：C125 (2)）。</summary>
        public static string RenamedProfileName(ImportJob j) =>
            ViahDatabase.UniqueProfileName(PackageProfileBuilder.ParseName(j.Name, out _));

        /// <summary>同じ名前で中身が違うプロファイルがあったときの説明（何が変わるか・今のプロファイル）。</summary>
        public static VisualElement ProfileConflictInfo(ViahWindow window, ImportJob j)
        {
            var box = UIKit.Div("viah-profile-conflict");
            box.Add(UIKit.Text("上書きすると、今のプロファイルの中身がこのパッケージの中身に変わります（名前・タグ・書き出し先はそのまま。Ctrl+Z で戻せます）。", "viah-small", "viah-wrap"));
            foreach (var d in j.differences) box.Add(UIKit.Text("・" + d, "viah-small", "viah-wrap", "viah-mt4"));
            var p = ViahDatabase.FindProfile(j.existingProfileId);
            if (p != null)
            {
                var row = UIKit.Row("viah-mt8");
                row.Add(UIKit.Img("Prefab Icon"));
                row.Add(UIKit.Text($"今のプロファイル「{p.name}」", "viah-bold", "viah-ml6"));
                row.Add(UIKit.Spacer());
                row.Add(UIKit.Btn("開いて見る", () => window.Show(new ProfilesPage(p.id)), "small", "ghost"));
                box.Add(row);
            }
            return box;
        }

        /// <summary>完了した行の結果（作ったプロファイルと注意点）。</summary>
        public static VisualElement JobResult(ViahWindow window, ImportJob j)
        {
            var box = UIKit.Div("viah-job-result");
            foreach (var w in j.warnings) box.Add(UIKit.Text("・" + w, "viah-small", "viah-wrap", "viah-msg-warn"));
            foreach (var n in j.notes) box.Add(UIKit.Text("・" + n, "viah-small", "viah-wrap", "viah-faint"));
            var p = ViahDatabase.FindProfile(j.profileId);
            if (p != null)
            {
                var row = UIKit.Row("viah-mt4");
                row.Add(UIKit.Img("Prefab Icon"));
                row.Add(UIKit.Text($"プロファイル「{p.name}」", "viah-bold", "viah-ml6"));
                if (j.alreadyExists) row.Add(UIKit.Badge("既にあります", "", "中身がまったく同じプロファイルが既にあったので、新しくは作りませんでした"));
                row.Add(UIKit.Spacer());
                row.Add(UIKit.Btn("中身を確認・修正", () => window.Show(new ProfileWizardPage(p, null, saved => window.Show(new ProfilesPage(saved.id)))), "small", "ghost"));
                row.Add(UIKit.Btn("開く", () => window.Show(new ProfilesPage(p.id)), "small", "viah-ml4"));
                box.Add(row);
            }
            else if (!string.IsNullOrEmpty(j.profileId))
            {
                box.Add(UIKit.Text("・作ったプロファイルは削除されています", "viah-faint", "viah-small"));
            }
            return box;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VInc.AvatarHub.UI;

namespace VInc.AvatarHub.Tests
{
    /// <summary>
    /// 開発用：実際のボタンを押して「プロジェクト作成 → プロファイル登録 → 単発 → シーケンス」を通す。
    /// Temp/viah_e2e.request を置いてスクリプトを再読み込みすると実行し、Temp/viah_e2e.log に結果を書く。
    /// </summary>
    [InitializeOnLoad]
    static class E2ETest
    {
        const string Request = "Temp/viah_e2e.request";
        const string LogPath = "Temp/viah_e2e.log";
        const string Name = "VIAH_E2E";
        const string Root = "Assets/" + Name;

        static readonly StringBuilder log = new StringBuilder();
        static readonly List<(string name, Func<bool> action)> steps = new List<(string, Func<bool>)>();
        static readonly List<string> errors = new List<string>();
        static int index, wait;
        static ViahWindow window;
        static int pass, fail;

        static E2ETest()
        {
            if (File.Exists("Temp/viah_cleanup.request"))
            {
                File.Delete("Temp/viah_cleanup.request");
                EditorApplication.delayCall += CleanupAll;
            }
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            EditorApplication.delayCall += Start;
        }

        /// <summary>テストで作ったサンプル・記録・ウィンドウをすべて片付ける（Temp/viah_cleanup.request でも実行）。</summary>
        [MenuItem("V.inc/開発/Avatar Hub テストデータを削除", priority = 1003)]
        static void CleanupAll()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<ViahWindow>()) w.Close();
            foreach (var folder in new[] { Root, "Assets/VIAH_Sample", "Assets/VIAH_External" })
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            foreach (var p in ViahDatabase.Profiles.Where(p => p.name == Name || p.name == "VIAH_Sample").ToList()) ViahDatabase.Delete(p);
            foreach (var p in ViahDatabase.Projects.Where(p => p.name == Name || p.name == "VIAH_Sample").ToList()) ViahDatabase.Delete(p);
            ViahDatabase.Settings.defaultOutputFolder = "";
            ViahDatabase.SaveSettings("テストの後片付け");
            File.WriteAllText("Temp/viah_cleanup.log", $"done projects={ViahDatabase.Projects.Count} profiles={ViahDatabase.Profiles.Count}");
        }

        [MenuItem("V.inc/開発/Avatar Hub 通しテスト", priority = 1002)]
        static void Start()
        {
            log.Clear();
            errors.Clear();
            pass = fail = 0;
            Cleanup();
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;

            string exportDir = Path.GetFullPath("Temp/viah_e2e_exports");
            if (Directory.Exists(exportDir)) Directory.Delete(exportDir, true);
            var oldDefault = ViahDatabase.Settings.defaultOutputFolder;
            ViahDatabase.Settings.defaultOutputFolder = exportDir;

            steps.Clear();
            steps.Add(("プロジェクト作成画面を開く（標準プリセット）", () =>
            {
                // ほかのプリセット（読み込んだものなど）が先頭にあっても、標準プリセットで作る
                var form = typeof(ProjectsPage).GetNestedType("Form", System.Reflection.BindingFlags.NonPublic);
                form.GetMethod("Reset").Invoke(null, null);
                form.GetField("parent").SetValue(null, "Assets");
                form.GetField("presetId").SetValue(null, ViahDatabase.Presets.First(p => p.name == "VRChatギミック標準").id);
                window.Show(new ProjectsPage(ProjectsPage.NewKey));
                return true;
            }));
            steps.Add(("名前とタグを入力", () =>
            {
                var field = window.rootVisualElement.Query<ViahTextField>().ToList().First(f => f.ClassListContains("big"));
                field.field.value = Name;
                var input = window.rootVisualElement.Q<TagField>().Q<TextField>();
                input.value = "通しテスト、くじら,";
                return true;
            }));
            steps.Add(("区切り文字でタグに分かれる（次のフレームで分割）", () =>
            {
                var tag = window.rootVisualElement.Q<TagField>();
                return Check(tag.Tags.Count == 2, "タグ 2 件: " + string.Join("/", tag.Tags));
            }));
            steps.Add(("Models にチェックを追加", () =>
            {
                var row = FindRow("Models");
                Click(row);
                return Check(FindButton("フォルダーを作成（8）") != null, "作成ボタンの数が 8 に増える");
            }));
            steps.Add(("フォルダーを作成", () =>
            {
                Press(FindButton("フォルダーを作成"));
                var rec = ViahDatabase.FindProjectByRoot(Root);
                return Check(AssetDatabase.IsValidFolder(Root + "/Models") && AssetDatabase.IsValidFolder(Root + "/Animations/Controllers"), "フォルダーができている")
                       & Check(rec != null && rec.tags.Contains("くじら"), "記録とタグが保存されている");
            }));
            steps.Add(("Ctrl+Z でフォルダー作成を取り消す", () =>
            {
                Undo.PerformUndo();
                return Check(!AssetDatabase.IsValidFolder(Root + "/Models"), "作ったフォルダーが消える")
                       & Check(ViahDatabase.FindProjectByRoot(Root) == null && ViahDatabase.Projects.All(p => p.name != Name), "記録も消える");
            }));
            steps.Add(("Ctrl+Y でやり直す", () =>
            {
                Undo.PerformRedo();
                var rec = ViahDatabase.FindProjectByRoot(Root);
                return Check(AssetDatabase.IsValidFolder(Root + "/Models") && AssetDatabase.IsValidFolder(Root + "/Animations/Controllers"), "フォルダーが戻る")
                       & Check(rec != null && rec.tags.Contains("くじら"), "記録とタグが戻る");
            }));
            steps.Add(("タグの変更を Undo / Redo", () =>
            {
                var rec = ViahDatabase.FindProjectByRoot(Root);
                window.Show(new ProjectsPage(rec.id));
                Undo.IncrementCurrentGroup();
                rec.tags.Add("Undoテスト");
                ViahDatabase.SaveProject(rec, "タグを変更");
                Undo.IncrementCurrentGroup();
                Undo.PerformUndo();
                bool undone = !ViahDatabase.FindProjectByRoot(Root).tags.Contains("Undoテスト");
                Undo.PerformRedo();
                bool redone = ViahDatabase.FindProjectByRoot(Root).tags.Contains("Undoテスト");
                return Check(undone, "Undo でタグが消える") & Check(redone, "Redo でタグが戻る");
            }));
            steps.Add(("検索（ひらがな）で見つかる", () =>
            {
                var hits = SearchUtil.Search("クジラ");
                return Check(hits.Any(h => h.project != null && h.project.name == Name), "「クジラ」でヒット");
            }));
            steps.Add(("プレハブを用意してプロファイル作成へ", () =>
            {
                AssetDatabase.CopyAsset("Assets/VIAH_Sample/Prefabs/Gimmick.prefab", Root + "/Prefabs/Gimmick.prefab");
                AssetDatabase.CopyAsset("Assets/VIAH_Sample/Textures/Body.png", Root + "/Textures/Own.png");
                var proj = ViahDatabase.FindProjectByRoot(Root);
                window.Show(new ProfileWizardPage(null, proj.id));
                return true;
            }));
            steps.Add(("次へ（自動登録）", () =>
            {
                Press(FindButton("次へ"));
                return Check(FindButton("自動検出を開始 ▶") != null, "プレハブ入力の画面");
            }));
            steps.Add(("Prefabs フォルダーが最初から入っている → 自動検出", () =>
            {
                bool seeded = window.rootVisualElement.Query<Label>().ToList().Any(l => l.text.Contains("中のプレハブ 1 個"));
                Press(FindButton("自動検出を開始 ▶"));
                return Check(seeded, "起点に Prefabs フォルダー");
            }));
            steps.Add(("確認画面（リストの描画を待ってから）", () => Check(FindRow("Gimmick.prefab") != null, "確認画面にプレハブが出る")));
            steps.Add(("外部依存は未選択・自分のフォルダーの Own.png も Shift で範囲選択", () =>
            {
                var summary = SummaryText();
                // Textures フォルダー内の Own.png（未使用）を個別にチェック
                Press(FindButton("すべて開く"));
                Click(FindRow("Own.png"));
                var after = SummaryText();
                return Check(summary.Contains("外部") && summary.Contains("含めていません"), "外部は含めない: " + summary)
                       & Check(after != summary, "クリックで件数が変わる: " + after);
            }));
            steps.Add(("次へ → 名前", () =>
            {
                Press(FindButton("次へ"));
                return Check(FindButton("保存して完了") != null, "名前の画面");
            }));
            steps.Add(("保存して完了", () =>
            {
                Press(FindButton("保存して完了"));
                var prof = ViahDatabase.Profiles.FirstOrDefault(p => p.name == Name);
                var files = prof == null ? new List<string>() : SelectionModel.FromProfile(prof).ResolveFiles();
                return Check(prof != null, "プロファイルが保存された")
                       & Check(files.Contains(Root + "/Prefabs/Gimmick.prefab") && files.Contains(Root + "/Textures/Own.png"), "プレハブと Own.png が入る")
                       & Check(!files.Any(f => f.StartsWith("Assets/VIAH_Sample/")), "外部（VIAH_Sample）は入らない")
                       & Check(prof != null && prof.ignoredDependencies.Count > 0, "外部依存は確認済みとして記録");
            }));
            steps.Add(("エクスポートへ → 単発エクスポート", () =>
            {
                // Undo のテストで設定が読み込み直されるので、書き出し先をもう一度テスト用にする
                ViahDatabase.Settings.defaultOutputFolder = exportDir;
                Press(FindButton("このままエクスポートへ"));
                var prof = ViahDatabase.Profiles.First(p => p.name == Name);
                // 時刻も付ける
                var timeChip = window.rootVisualElement.Query<Label>(className: "viah-chip").ToList()
                    .First(l => System.Text.RegularExpressions.Regex.IsMatch(l.text, @"^\d\d_\d\d$"));
                Click(timeChip);
                Press(FindButton("エクスポート"));
                var h = ViahDatabase.History.Get(prof.id, false);
                var file = h == null ? null : Path.Combine(exportDir, h.lastFile);
                return Check(h != null && h.lastVersion == "1.0.0", "履歴に Ver1.0.0")
                       & Check(file != null && File.Exists(file), "unitypackage ができた: " + h?.lastFile)
                       & Check(h != null && System.Text.RegularExpressions.Regex.IsMatch(h.lastFile, @"^VIAH_E2E_Ver1\.0\.0-\d{4}_\d\d_\d\d-\d\d_\d\d\.unitypackage$"), "名前の形式");
            }));
            steps.Add(("2 回目は前回のバージョンがヒントに出る", () =>
            {
                bool hint = window.rootVisualElement.Query<Label>().ToList().Any(l => l.text.Contains("前回のバージョンは Ver1.0.0"));
                return Check(hint, "ヒント表示");
            }));
            steps.Add(("シーケンス：2 件を Shift で範囲選択して書き出す", () =>
            {
                SessionState.SetInt("VInc.VIAH.exportMode", 1);
                ViahDatabase.Settings.defaultOutputFolder = exportDir;
                window.Show(new ExportPage());
                var rows = window.rootVisualElement.Query(className: "viah-seq-row").ToList();
                Click(rows[0]);
                // クリックで行が作り直されるので取り直す
                rows = window.rootVisualElement.Query(className: "viah-seq-row").ToList();
                Click(rows[rows.Count - 1], shift: true);
                var count = window.rootVisualElement.Query<Label>().ToList().First(l => l.text.StartsWith("選択 ")).text;
                Press(window.rootVisualElement.Query<VersionStepper>().ToList().Count > 0 ? FindButton("選択中のバージョンを +0.0.1") : null);
                var run = FindButton("件をエクスポート");
                Press(run);
                // 上書きや警告があればダイアログで続行
                var go = FindButton("件を書き出す");
                if (go != null) Press(go);
                var done = window.rootVisualElement.Query<Label>(className: "viah-dialog-title").ToList().FirstOrDefault()?.text ?? "";
                var files = Directory.GetFiles(exportDir, "*.unitypackage").Select(Path.GetFileName).ToList();
                log.AppendLine("  出力: " + string.Join(" | ", files));
                return Check(count == $"選択 {rows.Count} / {rows.Count}", "Shift で全行選択: " + count)
                       & Check(done.Contains("書き出しました"), "完了ダイアログ: " + done)
                       & Check(files.Any(f => f.StartsWith("VIAH_E2E_Ver1.0.1")), "+0.0.1 されたファイル");
            }));

            window = ViahWindow.Open();
            window.position = new Rect(80, 80, 1240, 780);
            index = 0;
            wait = 5;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;

            void Restore() => ViahDatabase.Settings.defaultOutputFolder = oldDefault;
            finish = Restore;
        }

        static Action finish;

        static void Tick()
        {
            if (wait-- > 0) return;
            if (index >= steps.Count || window == null)
            {
                Finish();
                return;
            }
            var (name, action) = steps[index++];
            // 1 ステップ = ユーザーの 1 操作として Undo を区切る
            Undo.IncrementCurrentGroup();
            try
            {
                log.AppendLine("■ " + name);
                if (!action()) log.AppendLine("  → 失敗");
            }
            catch (Exception e)
            {
                fail++;
                log.AppendLine("FAIL 例外 " + e);
            }
            wait = 8;
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            finish?.Invoke();
            ViahDatabase.SaveSettings("通しテストの後片付け");
            log.Insert(0, $"RESULT pass={pass} fail={fail} errors={errors.Count}\n");
            foreach (var e in errors) log.AppendLine("ERROR " + e);
            File.WriteAllText(LogPath, log.ToString(), new UTF8Encoding(false));
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error) errors.Add(condition + "\n" + stackTrace);
        }

        static bool Check(bool ok, string what)
        {
            if (ok) pass++;
            else fail++;
            log.AppendLine((ok ? "  OK   " : "  FAIL ") + what);
            return ok;
        }

        static void Cleanup()
        {
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            var rec = ViahDatabase.FindProjectByRoot(Root) ?? ViahDatabase.Projects.FirstOrDefault(p => p.name == Name);
            if (rec != null) ViahDatabase.Delete(rec);
            foreach (var p in ViahDatabase.Profiles.Where(p => p.name == Name).ToList()) ViahDatabase.Delete(p);
        }

        static Button FindButton(string text) =>
            window.rootVisualElement.Query<Button>().ToList().FirstOrDefault(b =>
                b.resolvedStyle.display != DisplayStyle.None &&
                ((b.text ?? "").Contains(text) || b.Query<Label>().ToList().Any(l => l.text.Contains(text))));

        static VisualElement FindRow(string name) =>
            window.rootVisualElement.Query(className: "viah-tree-row").ToList()
                .Concat(window.rootVisualElement.Query(className: "viah-asset-row").ToList())
                .FirstOrDefault(r => r.Query<Label>(className: "viah-tree-name").ToList().Any(l => l.text == name));

        static string SummaryText() =>
            window.rootVisualElement.Query<Label>().ToList().FirstOrDefault(l => l.text.StartsWith("含める："))?.text ?? "";

        static void Press(Button b)
        {
            if (b == null) throw new Exception("ボタンが見つかりません");
            if (!b.enabledInHierarchy) throw new Exception("ボタンが押せない状態です: " + b.text);
            using var e = NavigationSubmitEvent.GetPooled();
            e.target = b;
            b.SendEvent(e);
        }

        static void Click(VisualElement target, bool shift = false)
        {
            if (target == null) throw new Exception("クリック先が見つかりません");
            using var click = ClickEvent.GetPooled();
            click.target = target;
            typeof(ClickEvent).BaseType?.GetProperty("modifiers")?.SetValue(click, shift ? EventModifiers.Shift : EventModifiers.None);
            typeof(ClickEvent).BaseType?.GetProperty("clickCount")?.SetValue(click, 1);
            target.SendEvent(click);
        }
    }
}

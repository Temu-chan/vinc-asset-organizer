using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using VInc.AvatarHub.UI;

namespace VInc.AvatarHub.Tests
{
    /// <summary>
    /// 開発用：ウィンドウを開いて各画面を順に表示し、例外の有無と画面キャプチャ（Temp/viah_ui_*.png）を残す。
    /// Temp/viah_uitest.request を置いてスクリプトを再読み込みすると実行。
    /// </summary>
    [InitializeOnLoad]
    static class UISmokeTest
    {
        const string Request = "Temp/viah_uitest.request";
        const string LogPath = "Temp/viah_uitest.log";

        static readonly StringBuilder log = new StringBuilder();
        static readonly List<(string name, Action action)> steps = new List<(string, Action)>();
        static int stepIndex;
        static int wait;
        static ViahWindow window;
        static readonly List<string> errors = new List<string>();

        static UISmokeTest()
        {
            // 手動確認用：Temp/viah_place.request に "x,y,w,h" を書くと、その位置にウィンドウを開く
            if (File.Exists("Temp/viah_place.request"))
            {
                var v = File.ReadAllText("Temp/viah_place.request").Split(',').Select(float.Parse).ToArray();
                File.Delete("Temp/viah_place.request");
                EditorApplication.delayCall += () =>
                {
                    var w = ViahWindow.Open();
                    w.position = new Rect(v[0], v[1], v[2], v[3]);
                };
            }
            // 開き方の確認用：Temp/viah_open.request（中身が fresh なら前回の位置を消してから）で開き直し、位置を Temp/viah_open.log に書く
            if (File.Exists("Temp/viah_open.request"))
            {
                var openReq = File.ReadAllText("Temp/viah_open.request");
                bool fresh = openReq.Contains("fresh");
                // rect=x,y,w,h なら、閉じる前にその位置・大きさにする（ユーザーが動かした状態のまね）
                var rectArg = openReq.Contains("rect=") ? openReq.Substring(openReq.IndexOf("rect=") + 5).Trim().Split(',').Select(float.Parse).ToArray() : null;
                File.Delete("Temp/viah_open.request");
                EditorApplication.delayCall += () =>
                {
                    var olds = Resources.FindObjectsOfTypeAll<ViahWindow>();
                    if (rectArg != null)
                        foreach (var old in olds) old.position = new Rect(rectArg[0], rectArg[1], rectArg[2], rectArg[3]);
                    // ウィンドウが位置を記録するまで少し待ってから閉じる
                    var until = EditorApplication.timeSinceStartup + (rectArg != null ? 2.5 : 0);
                    EditorApplication.CallbackFunction step = null;
                    step = () =>
                    {
                        if (EditorApplication.timeSinceStartup < until) return;
                        EditorApplication.update -= step;
                        foreach (var old in olds)
                            if (old != null) old.Close();
                        if (fresh && File.Exists(ViahStorage.WindowFile)) File.Delete(ViahStorage.WindowFile);
                        EditorApplication.delayCall += () =>
                        {
                            var w = ViahWindow.Open();
                            EditorApplication.delayCall += () => File.WriteAllText("Temp/viah_open.log",
                                $"pos={w.position} docked={w.docked} mode={ViahDatabase.Settings.windowMode} main={EditorGUIUtility.GetMainWindowPosition()} min={w.minSize}");
                        };
                    };
                    EditorApplication.update += step;
                };
            }
            if (!File.Exists(Request)) return;
            // 中身に "640x480,1000x700" のように書くと、その大きさごとに全画面を撮る
            sizes.Clear();
            foreach (var part in File.ReadAllText(Request).Split(new[] { ',', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var wh = part.Split('x');
                if (wh.Length == 2 && float.TryParse(wh[0], out var sw) && float.TryParse(wh[1], out var sh)) sizes.Add(new Vector2(sw, sh));
            }
            File.Delete(Request);
            EditorApplication.delayCall += Start;
        }

        static readonly List<Vector2> sizes = new List<Vector2>();
        static int sizeIndex;
        static string prefix = "";

        [MenuItem("V.inc/開発/Avatar Hub 画面テスト", priority = 1001)]
        static void Start()
        {
            log.Clear();
            errors.Clear();
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;

            var profile = PrepareData(out var project);
            steps.Clear();
            steps.Add(("home", () => window.Navigate("home")));
            steps.Add(("projects_new", () => window.Show(new ProjectsPage(ProjectsPage.NewKey))));
            steps.Add(("projects_detail", () => window.Show(new ProjectsPage(project.id))));
            steps.Add(("preset_edit", () => window.Show(PresetEditorPage.ForPreset(ViahDatabase.Presets[0].Clone(), false, _ => { }, () => { }))));
            steps.Add(("profiles", () => window.Show(new ProfilesPage(profile.id))));
            steps.Add(("wizard_method", () => window.Show(new ProfileWizardPage(null, project.id))));
            steps.Add(("wizard_seeds", () => Go(1)));
            steps.Add(("wizard_scan", () => Invoke("RunScan")));
            steps.Add(("wizard_name", () => Go(3)));
            steps.Add(("wizard_edit", () => window.Show(new ProfileWizardPage(profile, null, _ => { }))));
            steps.Add(("export_single", () =>
            {
                SessionState.SetInt("VInc.VIAH.exportMode", 0);
                window.Show(new ExportPage(profile.id));
            }));
            steps.Add(("export_seq", () =>
            {
                SessionState.SetInt("VInc.VIAH.exportMode", 1);
                window.Show(new ExportPage());
            }));
            steps.Add(("search", () =>
            {
                var hits = SearchUtil.Search("てすと");
                log.AppendLine($"search 'てすと' → {hits.Count} hits: " + string.Join(", ", hits.Select(h => h.title)));
                window.Navigate("home");
            }));
            steps.Add(("settings", () => window.Show(new SettingsPage())));
            steps.Add(("guide_all", () =>
            {
                // すべてのガイドが例外なく作れること
                foreach (var topic in GuideContent.Topics) window.Show(new GuidePage(topic.id));
                var missing = GuideContent.Topics.SelectMany(x => x.steps.Select(s => s.image).Append(x.introImage))
                    .Where(n => !string.IsNullOrEmpty(n)).Distinct().Where(n => GuidePage.LoadImage(n) == null).ToList();
                log.AppendLine($"guide topics={GuideContent.Topics.Count} missingImages={missing.Count}: " + string.Join(", ", missing));
                window.Show(new GuidePage("project-about"));
            }));
            steps.Add(("guide_project_new", () => window.Show(new GuidePage("project-new"))));
            string oldTheme = ViahDatabase.Settings.theme, oldAccent = ViahDatabase.Settings.accent;
            steps.Add(("theme_light_orange", () =>
            {
                ViahDatabase.Settings.theme = "light";
                ViahDatabase.Settings.accent = "orange";
                window.ApplyTheme();
                window.Navigate("home");
            }));
            steps.Add(("theme_unity_purple", () =>
            {
                ViahDatabase.Settings.theme = "unity";
                ViahDatabase.Settings.accent = "purple";
                window.ApplyTheme();
                window.Show(new ProjectsPage(ProjectsPage.NewKey));
            }));
            steps.Add(("theme_restore", () =>
            {
                ViahDatabase.Settings.theme = oldTheme;
                ViahDatabase.Settings.accent = oldAccent;
                window.ApplyTheme();
                window.Navigate("home");
            }));

            window = ViahWindow.Open();
            sizeIndex = 0;
            ApplySize();
            stepIndex = 0;
            wait = 0;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void ApplySize()
        {
            var size = sizes.Count > 0 ? sizes[sizeIndex] : new Vector2(1240, 780);
            window.position = new Rect(80, 80, size.x, size.y);
            prefix = sizes.Count > 0 ? $"{size.x:0}x{size.y:0}_" : "";
            log.AppendLine($"── size {size.x:0}x{size.y:0}");
        }

        static void Go(int step)
        {
            var page = window.Current;
            page.GetType().GetMethod("Go", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(page, new object[] { step });
        }

        static void Invoke(string method)
        {
            var page = window.Current;
            page.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(page, null);
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
                errors.Add($"[{(stepIndex > 0 ? steps[Math.Min(stepIndex, steps.Count) - 1].name : "start")}] {condition}\n{stackTrace}");
        }

        static void Tick()
        {
            if (window == null)
            {
                Finish();
                return;
            }
            if (wait > 0)
            {
                wait--;
                window.Repaint();
                if (wait == 0) Capture(steps[stepIndex - 1].name);
                return;
            }
            if (stepIndex >= steps.Count)
            {
                if (sizeIndex + 1 < sizes.Count)
                {
                    sizeIndex++;
                    ApplySize();
                    stepIndex = 0;
                    wait = 0;
                    return;
                }
                Finish();
                return;
            }
            var (name, action) = steps[stepIndex++];
            try
            {
                action();
                log.AppendLine("OK   " + name);
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                log.AppendLine("FAIL " + name + ": " + inner);
            }
            window.Focus();
            window.Repaint();
            wait = 40;
        }

        static void Capture(string name)
        {
            try
            {
                var ppp = EditorGUIUtility.pixelsPerPoint;
                var r = window.position;
                int w = Mathf.RoundToInt(r.width * ppp), h = Mathf.RoundToInt(r.height * ppp);
                var px = InternalEditorUtility.ReadScreenPixel(new Vector2(r.x * ppp, r.y * ppp), w, h);
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.SetPixels(px);
                tex.Apply();
                File.WriteAllBytes($"Temp/viah_ui_{prefix}{name}.png", tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            catch (Exception e)
            {
                log.AppendLine("capture failed " + name + ": " + e.Message);
            }
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            log.Insert(0, $"RESULT errors={errors.Count}\n");
            foreach (var e in errors) log.AppendLine("ERROR " + e);
            File.WriteAllText(LogPath, log.ToString(), new UTF8Encoding(false));
        }

        /// <summary>テスト用のプロジェクト記録とプロファイル（VIAH_Sample を使う）。</summary>
        static PackageProfile PrepareData(out ProjectRecord project)
        {
            const string root = "Assets/VIAH_Sample";
            project = ViahDatabase.FindProjectByRoot(root);
            if (project == null)
            {
                project = new ProjectRecord
                {
                    id = ViahStorage.NewId(),
                    name = "VIAH_Sample",
                    createdAt = ViahStorage.Now(),
                    presetName = "VRChatギミック標準",
                    structure = DefaultPresets.Standard().nodes,
                    tags = new List<string> { "テスト", "サンプル" },
                };
                project.root = new AssetRef(root);
                ViahDatabase.SaveProject(project);
            }
            var projectId = project.id;
            var profile = ViahDatabase.Profiles.FirstOrDefault(p => p.projectId == projectId);
            if (profile == null)
            {
                profile = new PackageProfile { id = ViahStorage.NewId(), name = "VIAH_Sample", projectId = project.id };
                profile.Sanitize();
                profile.baseFolder = new AssetRef(root);
                profile.tags = new List<string> { "テスト用" };
                profile.seeds.Add(new AssetRef(root + "/Prefabs"));
                var m = new SelectionModel();
                m.SetFolder(root + "/Textures", true);
                m.SetFile(root + "/Prefabs/Gimmick.prefab", true);
                m.WriteTo(profile);
                ViahDatabase.SaveProfile(profile);
            }
            return profile;
        }
    }
}

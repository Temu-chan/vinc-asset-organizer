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
using VInc.AssetOrganizer.UI;

namespace VInc.AssetOrganizer.Tests
{
    /// <summary>
    /// 開発用：ウィンドウを開いて各画面を順に表示し、例外の有無と画面キャプチャ（Temp/ao_ui_*.png）を残す。
    /// Temp/ao_uitest.request を置いてスクリプトを再読み込みすると実行。
    /// </summary>
    [InitializeOnLoad]
    static class UISmokeTest
    {
        const string Request = "Temp/ao_uitest.request";
        const string LogPath = "Temp/ao_uitest.log";

        static readonly StringBuilder log = new StringBuilder();
        static readonly List<(string name, Action action)> steps = new List<(string, Action)>();
        static int stepIndex;
        static int wait;
        static AOWindow window;
        static readonly List<string> errors = new List<string>();

        static UISmokeTest()
        {
            // 手動確認用：Temp/ao_place.request に "x,y,w,h" を書くと、その位置にウィンドウを開く
            if (File.Exists("Temp/ao_place.request"))
            {
                var v = File.ReadAllText("Temp/ao_place.request").Split(',').Select(float.Parse).ToArray();
                File.Delete("Temp/ao_place.request");
                EditorApplication.delayCall += () =>
                {
                    var w = AOWindow.Open();
                    w.position = new Rect(v[0], v[1], v[2], v[3]);
                };
            }
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            EditorApplication.delayCall += Start;
        }

        [MenuItem("V.inc/開発/Asset Organizer 画面テスト", priority = 1001)]
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
            steps.Add(("preset_edit", () => window.Show(PresetEditorPage.ForPreset(AODatabase.Presets[0].Clone(), false, _ => { }, () => { }))));
            steps.Add(("profiles", () => window.Show(new ProfilesPage(profile.id))));
            steps.Add(("wizard_method", () => window.Show(new ProfileWizardPage(null, project.id))));
            steps.Add(("wizard_seeds", () => Go(1)));
            steps.Add(("wizard_scan", () => Invoke("RunScan")));
            steps.Add(("wizard_name", () => Go(3)));
            steps.Add(("wizard_edit", () => window.Show(new ProfileWizardPage(profile, null, _ => { }))));
            steps.Add(("export_single", () =>
            {
                SessionState.SetInt("VInc.AO.exportMode", 0);
                window.Show(new ExportPage(profile.id));
            }));
            steps.Add(("export_seq", () =>
            {
                SessionState.SetInt("VInc.AO.exportMode", 1);
                window.Show(new ExportPage());
            }));
            steps.Add(("search", () =>
            {
                var hits = SearchUtil.Search("てすと");
                log.AppendLine($"search 'てすと' → {hits.Count} hits: " + string.Join(", ", hits.Select(h => h.title)));
                window.Navigate("home");
            }));

            window = AOWindow.Open();
            window.position = new Rect(80, 80, 1240, 780);
            stepIndex = 0;
            wait = 0;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
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
                File.WriteAllBytes($"Temp/ao_ui_{name}.png", tex.EncodeToPNG());
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

        /// <summary>テスト用のプロジェクト記録とプロファイル（AO_Sample を使う）。</summary>
        static PackageProfile PrepareData(out ProjectRecord project)
        {
            const string root = "Assets/AO_Sample";
            project = AODatabase.FindProjectByRoot(root);
            if (project == null)
            {
                project = new ProjectRecord
                {
                    id = AOStorage.NewId(),
                    name = "AO_Sample",
                    createdAt = AOStorage.Now(),
                    presetName = "VRChatギミック標準",
                    structure = DefaultPresets.Standard().nodes,
                    tags = new List<string> { "テスト", "サンプル" },
                };
                project.root = new AssetRef(root);
                AODatabase.SaveProject(project);
            }
            var projectId = project.id;
            var profile = AODatabase.Profiles.FirstOrDefault(p => p.projectId == projectId);
            if (profile == null)
            {
                profile = new PackageProfile { id = AOStorage.NewId(), name = "AO_Sample", projectId = project.id };
                profile.Sanitize();
                profile.baseFolder = new AssetRef(root);
                profile.tags = new List<string> { "テスト用" };
                profile.seeds.Add(new AssetRef(root + "/Prefabs"));
                var m = new SelectionModel();
                m.SetFolder(root + "/Textures", true);
                m.SetFile(root + "/Prefabs/Gimmick.prefab", true);
                m.WriteTo(profile);
                AODatabase.SaveProfile(profile);
            }
            return profile;
        }
    }
}

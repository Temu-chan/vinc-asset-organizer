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
            // キューは実際のものを汚さないよう、Temp の中に保存する
            PackageImportRunner.DirOverride = Path.GetFullPath("Temp/viah_ui_queue");
            PackageImportRunner.ResetCache();
            var pkg = PreparePackage();
            steps.Clear();
            steps.Add(("home", () => window.Navigate("home")));
            steps.Add(("projects_new", () => window.Show(new ProjectsPage(ProjectsPage.NewKey))));
            steps.Add(("projects_detail", () => window.Show(new ProjectsPage(project.id))));
            steps.Add(("preset_edit", () => window.Show(PresetEditorPage.ForPreset(ViahDatabase.Presets[0].Clone(), false, _ => { }, () => { }))));
            steps.Add(("profiles", () => window.Show(new ProfilesPage(profile.id))));
            steps.Add(("wizard_method", () => window.Show(new ProfileWizardPage(null, project.id))));
            steps.Add(("wizard_method_end", () => ScrollToEnd()));
            steps.Add(("wizard_seeds", () => Go(1)));
            steps.Add(("wizard_scan", () => Invoke("RunScan")));
            steps.Add(("wizard_name", () => Go(3)));
            steps.Add(("wizard_edit", () => window.Show(new ProfileWizardPage(profile, null, _ => { }))));
            steps.Add(("package_pick", () => window.Show(new PackageProfilePage())));
            steps.Add(("package_check", () =>
            {
                var s = PackageImportRunner.Single;
                s.jobs.Clear();
                s.jobs.Add(PackageImportRunner.NewJob(pkg, true));
                PackageImportRunner.Save(s);
                window.Show(new PackageProfilePage(true));
            }));
            steps.Add(("package_done", () =>
            {
                var j = PackageImportRunner.Single.jobs[0];
                j.status = "done";
                j.profileId = profile.id;
                j.warnings.Add("プレハブが使っているのに入っていないファイルが 2 件あります（入れ忘れの可能性）");
                j.notes.Add("黄色の「!」の 1 件は上書きしました");
                PackageImportRunner.Save(PackageImportRunner.Single);
                window.Show(new PackageProfilePage(true));
            }));
            steps.Add(("package_exists", () =>
            {
                var j = PackageImportRunner.Single.jobs[0];
                j.warnings.Clear();
                j.notes.Clear();
                j.alreadyExists = true;
                j.notes.Add("中身がまったく同じプロファイル「VIAH_Sample」が既にあるので、新しくは作りませんでした");
                PackageImportRunner.Save(PackageImportRunner.Single);
                window.Show(new PackageProfilePage(true));
            }));
            steps.Add(("package_profile_conflict", () =>
            {
                var j = PackageImportRunner.Single.jobs[0];
                j.alreadyExists = false;
                j.status = "profile-conflict";
                j.existingProfileId = profile.id;
                j.message = "同じ名前のプロファイル「VIAH_Sample」が既にあります（中身が違います）";
                j.differences = new List<string> { "登録しているファイル：12 件 → 14 件（増える 3 件・なくなる 1 件）", "起点のプレハブが変わります" };
                PackageImportRunner.Save(PackageImportRunner.Single);
                window.Show(new PackageProfilePage(true));
            }));
            steps.Add(("import_queue", () =>
            {
                var q = PackageImportRunner.Sequence;
                q.jobs.Clear();
                var a = PackageImportRunner.NewJob(pkg, true);
                a.status = "done";
                a.profileId = profile.id;
                a.notes.Add("中身はすべてプロジェクトに入っていたので、インポートは省きました");
                var b = PackageImportRunner.NewJob(Path.Combine(Path.GetDirectoryName(pkg), "Ring_Diamond_Ver1.0.0.unitypackage"), true);
                b.status = "conflict";
                b.conflictCount = 3;
                b.conflicts = new List<string> { "Assets/VRCWedding/Ring/Mat.mat", "Assets/VRCWedding/Ring/Ring.fbx", "Assets/VRCWedding/Ring/Tex.png" };
                b.message = "黄色の「!」（同じ名前の別のファイル）が 3 件あるため止めました";
                q.jobs.Add(a);
                q.jobs.Add(b);
                var c = PackageImportRunner.NewJob(Path.Combine(Path.GetDirectoryName(pkg), "C125_Ver1.0.0.unitypackage"), true);
                c.status = "profile-conflict";
                c.existingProfileId = profile.id;
                c.message = "同じ名前のプロファイル「VIAH_Sample」が既にあります（中身が違います）";
                c.differences = new List<string> { "登録しているファイル：277 件 → 279 件（増える 2 件・なくなる 0 件）" };
                q.jobs.Add(c);
                q.jobs.Add(PackageImportRunner.NewJob(Path.Combine(Path.GetDirectoryName(pkg), "TeleportEffect.unitypackage"), false));
                q.jobs.Add(PackageImportRunner.NewJob(Path.Combine(Path.GetDirectoryName(pkg), "HairLayer_v1.2.0.unitypackage"), true));
                PackageImportRunner.Save(q);
                window.Show(new ImportQueuePage());
                ScrollToEnd();
            }));
            steps.Add(("import_done", () =>
            {
                var q = PackageImportRunner.Sequence;
                foreach (var j in q.jobs) j.status = "done";
                q.jobs[1].profileId = profile.id;
                q.jobs[1].warnings.Add("パッケージにあってプロジェクトに無いファイル 1 件は入れていません");
                PackageImportRunner.Save(q);
                window.Show(new ImportQueuePage());
                ScrollToEnd();
            }));
            steps.Add(("bookmarks", () =>
            {
                // 保存はせず、画面に出すだけ（実際のブックマークには触らない）
                var items = ViahDatabase.Bookmarks.items;
                items.Clear();
                items.Add(new Bookmark { id = ViahStorage.NewId(), target = new AssetRef(PkgRoot) });
                items.Add(new Bookmark { id = ViahStorage.NewId(), label = "ギフトボックスのプレハブ", target = new AssetRef(PkgRoot + "/Box.prefab") });
                items.Add(new Bookmark { id = ViahStorage.NewId(), target = new AssetRef { path = "Assets/OtherProject/Characters" } });
                window.Show(new BookmarksPage());
            }));
            steps.Add(("bookmarks_end", () => ScrollToEnd()));
            steps.Add(("bookmark_window_open", () => BookmarkWindow.Open()));
            steps.Add(("bookmark_window_close", () =>
            {
                foreach (var w in Resources.FindObjectsOfTypeAll<BookmarkWindow>()) w.Close();
                ViahDatabase.Reload();
                window.Focus();
            }));
            steps.Add(("searcher_empty", () =>
            {
                SetSearch("", 0, "", "", false);
                window.Show(new SearcherPage());
            }));
            steps.Add(("searcher_folder", () =>
            {
                SetSearch(PkgRoot + "/Box.mat", 1, PkgRoot, "", true);
                window.Show(new SearcherPage());
                Invoke("Run");
                ScrollToEnd();
            }));
            steps.Add(("searcher_prefab", () =>
            {
                SetSearch(PkgRoot + "/Box.mat", 2, "", PkgRoot + "/Box.prefab", false);
                window.Show(new SearcherPage());
                Invoke("Run");
                ScrollToEnd();
            }));
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
            steps.Add(("settings_sound", () => window.Show(new SettingsPage("sound"))));
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

        /// <summary>撮る前に、画面のスクロールをいちばん下へ（下のほうの表示も確かめる）。</summary>
        static void ScrollToEnd()
        {
            window.rootVisualElement.schedule.Execute(() =>
            {
                foreach (var sv in window.rootVisualElement.Query<ScrollView>().ToList())
                    sv.verticalScroller.value = sv.verticalScroller.highValue;
            }).ExecuteLater(300);
        }

        static void SetSearch(string target, int scope, string folder, string prefab, bool indirect)
        {
            var t = typeof(SearcherPage);
            const BindingFlags f = BindingFlags.Static | BindingFlags.NonPublic;
            t.GetField("target", f).SetValue(null, target);
            t.GetField("scope", f).SetValue(null, scope);
            t.GetField("folder", f).SetValue(null, folder);
            t.GetField("prefab", f).SetValue(null, prefab);
            t.GetField("indirect", f).SetValue(null, indirect);
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
            PackageImportRunner.DirOverride = null;
            PackageImportRunner.ResetCache();
            if (AssetDatabase.IsValidFolder(PkgRoot)) AssetDatabase.DeleteAsset(PkgRoot);
            Application.logMessageReceived -= OnLog;
            log.Insert(0, $"RESULT errors={errors.Count}\n");
            foreach (var e in errors) log.AppendLine("ERROR " + e);
            File.WriteAllText(LogPath, log.ToString(), new UTF8Encoding(false));
        }

        const string PkgRoot = "Assets/VIAH_UiPkg";

        /// <summary>
        /// 「UnityPackage から作る」の確認画面用：入っている・まだ入っていない・黄色の「!」が混ざった unitypackage を作る。
        /// </summary>
        static string PreparePackage()
        {
            if (AssetDatabase.IsValidFolder(PkgRoot)) AssetDatabase.DeleteAsset(PkgRoot);
            AssetDatabase.CreateFolder("Assets", "VIAH_UiPkg");
            var mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, PkgRoot + "/Box.mat");
            AssetDatabase.CreateAsset(new Material(Shader.Find("Standard")), PkgRoot + "/Lid.mat");
            AssetDatabase.CreateAsset(new AnimationClip(), PkgRoot + "/Open.anim");
            AssetDatabase.CreateAsset(new AnimationClip(), PkgRoot + "/Close.anim");
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            PrefabUtility.SaveAsPrefabAsset(go, PkgRoot + "/Box.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            var dir = Path.GetFullPath("Temp/viah_ui_pkg");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "GiftBox_Ver1.0.2-2026_10_28.unitypackage");
            AssetDatabase.ExportPackage(new[] { PkgRoot, PkgRoot + "/Box.mat", PkgRoot + "/Lid.mat", PkgRoot + "/Open.anim", PkgRoot + "/Close.anim", PkgRoot + "/Box.prefab" }, file, ExportPackageOptions.Default);
            // まだ入っていない 2 件と、黄色の「!」1 件
            AssetDatabase.DeleteAsset(PkgRoot + "/Open.anim");
            AssetDatabase.DeleteAsset(PkgRoot + "/Close.anim");
            var lid = PkgRoot + "/Lid.mat";
            var bytes = File.ReadAllBytes(lid);
            AssetDatabase.DeleteAsset(lid);
            File.WriteAllBytes(lid, bytes);
            File.WriteAllText(lid + ".meta", "fileFormatVersion: 2\nguid: " + Guid.NewGuid().ToString("N") +
                "\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
            AssetDatabase.ImportAsset(lid);
            return file;
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

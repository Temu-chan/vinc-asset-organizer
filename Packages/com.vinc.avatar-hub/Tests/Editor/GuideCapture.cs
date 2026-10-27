using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using VInc.AvatarHub.UI;
using AnimatorController = UnityEditor.Animations.AnimatorController;
using Debug = UnityEngine.Debug;
using Switch = VInc.AvatarHub.UI.Switch;

namespace VInc.AvatarHub.Tests
{
    /// <summary>
    /// 開発用：ガイドの注釈つきスクリーンショットを作り直す。
    /// 一時的な保存先とサンプル（Assets/WhaleGimmick）を用意して各画面を表示し、
    /// Temp/viah_guide/ に「元の画像」と「枠を付ける位置（JSON）」を書き出してから、
    /// 同じフォルダーの annotate-guide.ps1 で枠・番号・矢印を描いて Editor/Guide/Images/ に保存する。
    /// Temp/viah_guide.request を置いてスクリプトを再読み込みしても実行する。
    /// </summary>
    [InitializeOnLoad]
    static class GuideCapture
    {
        const string Request = "Temp/viah_guide.request";
        const string OutDir = "Temp/viah_guide";
        const string LogPath = "Temp/viah_guide.log";
        const string DataDir = "Temp/VIAH_GuideData";
        const string Whale = "Assets/WhaleGimmick";
        const string Shared = "Assets/SharedAssets";
        const float WaitSeconds = 0.9f;
        static readonly Rect WindowRect = new Rect(60, 60, 1100, 760);

        static readonly string[] HintKeys =
        {
            "projects.intro", "preset.edit", "wizard.method", "wizard.drop", "wizard.select", "export.single", "export.seq", "home.search",
        };

        class Shot
        {
            public string name;
            public Action setup;
            /// <summary>setup のあと少し待ってから行う操作（リストの描画を待つものなど）。</summary>
            public Action after;
            public (int n, string spec, bool arrow)[] marks = Array.Empty<(int, string, bool)>();
            /// <summary>注釈なしの画像（name-plain.png）も残す。</summary>
            public bool plain;
            public Action cleanup;
        }

        [Serializable]
        class MarkJson
        {
            public int n;
            public float x, y, w, h;
            public bool arrow;
        }

        [Serializable]
        class ShotJson
        {
            public string name;
            public int width, height;
            public float ppp;
            public bool plain;
            public List<MarkJson> marks = new List<MarkJson>();
        }

        static List<Shot> shots;
        static int index;
        static int phase;
        static double phaseTime;
        static ViahWindow window;
        static Vector2 origin;
        static bool originReady;
        static readonly StringBuilder log = new StringBuilder();
        static readonly List<string> errors = new List<string>();
        static readonly Dictionary<string, bool> hintBackup = new Dictionary<string, bool>();
        static ProjectRecord whale, lantern;
        static PackageProfile whaleProfile, liteProfile;

        static GuideCapture()
        {
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            EditorApplication.delayCall += Start;
        }

        [MenuItem("V.inc/開発/ガイド画像を撮り直す", priority = 1004)]
        static void Start()
        {
            log.Clear();
            errors.Clear();
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            try
            {
                Prepare();
            }
            catch (Exception e)
            {
                log.AppendLine("FAIL 準備: " + e);
                Finish();
                return;
            }
            shots = CreateShots();
            index = 0;
            phase = 0;
            phaseTime = EditorApplication.timeSinceStartup + 1.0;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        // ───────────── 準備 ─────────────

        static void Prepare()
        {
            if (Directory.Exists(OutDir)) Directory.Delete(OutDir, true);
            Directory.CreateDirectory(OutDir);

            // ヒントは画面を広く使うために閉じておく（あとで元に戻す）
            hintBackup.Clear();
            foreach (var k in HintKeys)
            {
                var key = "VInc.VIAH.hint." + k;
                hintBackup[key] = EditorPrefs.GetBool(key, false);
                EditorPrefs.SetBool(key, true);
            }

            // 自分のデータに混ざらないよう、一時的な保存先を使う
            var dataDir = Path.GetFullPath(DataDir);
            if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true);
            ViahStorage.DataDirOverride = dataDir;
            ViahDatabase.Reload();
            ResetForm();

            CreateSampleAssets();
            var preset = ViahDatabase.Presets.FirstOrDefault(p => p.name == "VRChatギミック標準") ?? ViahDatabase.Presets[0];
            whale = new ProjectRecord
            {
                id = ViahStorage.NewId(),
                name = "WhaleGimmick",
                createdAt = ViahStorage.Now(),
                presetName = preset.name,
                structure = preset.Clone().nodes,
                tags = new List<string> { "くじら", "ギミック", "水族館" },
                unityProject = ViahStorage.UnityProjectName,
            };
            whale.root = new AssetRef(Whale);
            lantern = new ProjectRecord
            {
                id = ViahStorage.NewId(),
                name = "LanternGimmick",
                createdAt = ViahStorage.Now(),
                presetName = preset.name,
                structure = preset.Clone().nodes,
                tags = new List<string> { "ランタン", "ライト" },
                unityProject = "Sample_Other_Project",
            };
            lantern.root = new AssetRef("Assets/LanternGimmick");
            WriteRecord(whale, ViahStorage.ProjectsDir, ViahStorage.ProjectExt);
            WriteRecord(lantern, ViahStorage.ProjectsDir, ViahStorage.ProjectExt);
            ViahDatabase.Reload();
            whale = ViahDatabase.FindProject(whale.id);
            lantern = ViahDatabase.FindProject(lantern.id);

            window = EditorWindow.HasOpenInstances<ViahWindow>() ? EditorWindow.GetWindow<ViahWindow>() : ScriptableObject.CreateInstance<ViahWindow>();
            window.Show();
            window.position = WindowRect;
            window.ApplyTheme();
            window.RebuildSidebar();
            window.Focus();

            // 画面の左上がスクリーンのどこにあるかを IMGUI で調べる
            originReady = false;
            var probe = window.rootVisualElement.Q<IMGUIContainer>("viah-guide-probe");
            if (probe == null)
            {
                probe = new IMGUIContainer(() =>
                {
                    origin = GUIUtility.GUIToScreenPoint(Vector2.zero);
                    originReady = true;
                })
                { name = "viah-guide-probe", pickingMode = PickingMode.Ignore };
                probe.style.position = Position.Absolute;
                probe.style.left = 0;
                probe.style.top = 0;
                probe.style.width = 1;
                probe.style.height = 1;
                window.rootVisualElement.Add(probe);
            }
        }

        static void WriteRecord(object rec, string dir, string ext)
        {
            var name = rec is ProjectRecord p ? p.name : ((PackageProfile)rec).name;
            ViahStorage.Write(ViahStorage.MakeFilePath(dir, name, ext, null), rec);
        }

        static void CreateSampleAssets()
        {
            if (AssetDatabase.IsValidFolder(Whale)) AssetDatabase.DeleteAsset(Whale);
            if (AssetDatabase.IsValidFolder(Shared)) AssetDatabase.DeleteAsset(Shared);
            var preset = ViahDatabase.Presets.FirstOrDefault(p => p.name == "VRChatギミック標準") ?? ViahDatabase.Presets[0];
            var ids = preset.nodes.Where(n => n.defaultOn).Select(n => n.id).ToList();
            FolderBuilder.Create(Whale, preset.nodes, ids, false);
            FolderBuilder.EnsureFolder(Shared);

            var body = MakeTexture(Whale + "/Textures/Whale_Body.png", new Color(0.3f, 0.5f, 0.9f));
            var glow = MakeTexture(Whale + "/Textures/Whale_Glow.png", Color.cyan);
            MakeTexture(Whale + "/Textures/Whale_Old.png", Color.gray);
            var shared = MakeTexture(Shared + "/CommonNoise.png", Color.gray);

            var bodyMat = new Material(Shader.Find("Standard")) { mainTexture = body };
            bodyMat.SetTexture("_EmissionMap", shared);
            AssetDatabase.CreateAsset(bodyMat, Whale + "/Materials/Whale_Body.mat");
            var glowMat = new Material(Shader.Find("Standard")) { mainTexture = glow };
            AssetDatabase.CreateAsset(glowMat, Whale + "/Materials/Whale_Glow.mat");

            var clip = new AnimationClip();
            AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("Body", typeof(MeshRenderer), "m_Materials.Array.data[0]"),
                new[] { new ObjectReferenceKeyframe { time = 0, value = glowMat } });
            AssetDatabase.CreateAsset(clip, Whale + "/Animations/Whale_GlowOn.anim");
            AnimatorController.CreateAnimatorControllerAtPathWithClip(Whale + "/Animations/Controllers/Whale_FX.controller", clip);

            var root = new GameObject("WhaleGimmick");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Body";
            cube.transform.SetParent(root.transform);
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;
            var maType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator"))
                .FirstOrDefault(t => t != null);
            if (maType != null)
            {
                var comp = root.AddComponent(maType);
                maType.GetField("animator").SetValue(comp, AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Whale + "/Animations/Controllers/Whale_FX.controller"));
            }
            PrefabUtility.SaveAsPrefabAsset(root, Whale + "/Prefabs/WhaleGimmick.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static Texture2D MakeTexture(string path, Color c)
        {
            var t = new Texture2D(4, 4);
            t.SetPixels(Enumerable.Repeat(c, 16).ToArray());
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>エクスポートの画面用：プロファイル 2 件と書き出し履歴。</summary>
        static void CreateProfiles()
        {
            PackageProfile Make(string name)
            {
                var p = new PackageProfile { id = ViahStorage.NewId(), name = name, projectId = whale.id };
                p.Sanitize();
                p.baseFolder = new AssetRef(Whale);
                p.tags = new List<string>(whale.tags);
                p.seeds.Add(new AssetRef(Whale + "/Prefabs"));
                var m = new SelectionModel();
                foreach (var f in new[] { "/Prefabs", "/Materials", "/Animations", "/Textures" }) m.SetFolder(Whale + f, true);
                m.SetFile(Whale + "/Textures/Whale_Old.png", false);
                m.WriteTo(p);
                p.ignoredDependencies.Add(new AssetRef(Shared + "/CommonNoise.png"));
                p.createdAt = p.updatedAt = ViahStorage.Now();
                p.unityProject = ViahStorage.UnityProjectName;
                return p;
            }
            whaleProfile = Make("WhaleGimmick");
            liteProfile = Make("WhaleGimmick_Lite");
            WriteRecord(whaleProfile, ViahStorage.ProfilesDir, ViahStorage.ProfileExt);
            WriteRecord(liteProfile, ViahStorage.ProfilesDir, ViahStorage.ProfileExt);
            ViahDatabase.Reload();
            var h = ViahDatabase.History.Get(whaleProfile.id, true);
            h.lastVersion = "1.0.2";
            h.lastExportAt = ViahStorage.Now();
            h.lastFile = "WhaleGimmick_Ver1.0.2.unitypackage";
            var h2 = ViahDatabase.History.Get(liteProfile.id, true);
            h2.lastVersion = "1.0.0";
            h2.lastExportAt = ViahStorage.Now();
            h2.lastFile = "WhaleGimmick_Lite_Ver1.0.0.unitypackage";
            ViahDatabase.SaveHistory();
        }

        // ───────────── 撮る画面 ─────────────

        static List<Shot> CreateShots()
        {
            var list = new List<Shot>
            {
                // プロジェクト
                new Shot
                {
                    name = "project-new-1",
                    setup = () => window.Show(new ProjectsPage(whale.id)),
                    marks = new[] { (1, "nav:プロジェクト作成", true), (2, "#btn-project-new", true) },
                },
                new Shot
                {
                    name = "project-new-2",
                    setup = () =>
                    {
                        ResetForm();
                        SetForm("name", "productC");
                        window.Show(new ProjectsPage(ProjectsPage.NewKey));
                        window.rootVisualElement.Focus();
                    },
                    marks = new[] { (3, "#card-name", false), (4, "#card-where", false) },
                },
                new Shot
                {
                    name = "project-new-3",
                    plain = true,
                    setup = () =>
                    {
                        SetForm("tags", new List<string> { "くじら", "ギミック" });
                        window.Show(new ProjectsPage(ProjectsPage.NewKey));
                    },
                    after = () => ScrollTo("#card-tags"),
                    marks = new[] { (5, "#tree-structure", false), (6, "#card-tags", false) },
                },
                new Shot
                {
                    name = "project-new-4",
                    setup = () => window.Show(new ProjectsPage(ProjectsPage.NewKey)),
                    marks = new[] { (7, "#btn-project-create", true) },
                },
                new Shot
                {
                    name = "project-edit-1",
                    setup = () =>
                    {
                        ResetForm();
                        var page = new ProjectsPage(whale.id);
                        window.Show(page);
                        // まだ作っていない Models にチェックを入れた状態にする
                        var models = whale.structure.Find(n => n.name == "Models");
                        if (models != null)
                        {
                            var field = typeof(ProjectsPage).GetField("detailChecked", BindingFlags.Instance | BindingFlags.NonPublic);
                            ((HashSet<string>)field.GetValue(page)).Add(models.id);
                            page.Rebuild();
                        }
                    },
                    after = () => ScrollTo("#btn-project-make"),
                    marks = new[] { (1, ".viah-list-item.is-active", false), (2, "#card-detail-tags", false), (3, "#tree-detail", false), (4, "#btn-project-make", true), (5, "#btn-project-edit-structure", true) },
                },
                new Shot
                {
                    name = "project-dup-1",
                    setup = () => window.Show(new ProjectsPage(whale.id)),
                    marks = new[] { (1, "#btn-project-menu", true) },
                },
                new Shot
                {
                    name = "project-dup-2",
                    setup = () =>
                    {
                        var page = new ProjectsPage(whale.id);
                        window.Show(page);
                        typeof(ProjectsPage).GetMethod("Duplicate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(page, new object[] { whale });
                        ClearToast();
                    },
                    marks = new[] { (2, ".viah-mb8@のフォルダー構成を使っています", false), (3, "#card-name", false) },
                },
                new Shot
                {
                    name = "project-import-1",
                    setup = () =>
                    {
                        ResetForm();
                        window.Show(new ProjectsPage(whale.id));
                    },
                    marks = new[] { (1, "#btn-project-import", true) },
                },
                new Shot
                {
                    name = "project-import-2",
                    setup = () => window.Show(new ProjectsPage(lantern.id)),
                    marks = new[] { (2, "#btn-project-create-here", true), (3, "btn:場所を選んで作成", true) },
                },
                new Shot
                {
                    name = "project-share-1",
                    setup = () => window.Show(new ProjectsPage(whale.id)),
                    marks = new[] { (1, "#btn-project-share", true) },
                },
                new Shot
                {
                    name = "preset-edit-1",
                    setup = () =>
                    {
                        ResetForm();
                        window.Show(new ProjectsPage(ProjectsPage.NewKey));
                    },
                    after = () => ScrollTo("#card-structure"),
                    marks = new[] { (1, "#btn-preset-edit", true), (2, "#btn-preset-menu", true) },
                },
                new Shot
                {
                    name = "preset-edit-2",
                    setup = () =>
                    {
                        var preset = (ViahDatabase.Presets.FirstOrDefault(p => p.name == "VRChatギミック標準") ?? ViahDatabase.Presets[0]).Clone();
                        window.Show(PresetEditorPage.ForPreset(preset, false, _ => { }, () => { }));
                        var tex = preset.nodes.Find(n => n.name == "Textures");
                        if (tex != null) window.rootVisualElement.Q<FolderTreeView>()?.Select(tex.id);
                    },
                    marks = new[] { (3, ".viah-toolbar", false), (4, ".viah-tree-row.is-selected>.viah-tree-handle", true), (5, "switch:番号", true) },
                },

                // プロファイル（作成の流れ）
                new Shot
                {
                    name = "profile-new-1",
                    setup = () => window.Show(new ProfileWizardPage(null, whale.id)),
                    marks = new[] { (1, "card:どのプロジェクト", false), (2, "card:登録のしかた", false), (3, "btn=次へ", true) },
                },
                new Shot
                {
                    name = "profile-new-2",
                    setup = () => CallPage("Go", 1),
                    marks = new[] { (4, ".viah-dropzone", false), (5, "btn:自動検出を開始", true) },
                },
                new Shot
                {
                    name = "profile-new-3",
                    plain = true,
                    setup = () => CallPage("RunScan"),
                    after = () => Press("btn=すべて開く"),
                    marks = new[] { (6, ".viah-asset-list", false), (7, ".viah-seg", false), (8, ".viah-legend", false) },
                },
                new Shot
                {
                    name = "profile-new-4",
                    setup = () => CallPage("Go", 3),
                    marks = new[] { (9, "card:名前", false), (10, "btn:保存して完了", true) },
                },

                // プロファイル（一覧）
                new Shot
                {
                    name = "profile-detail",
                    setup = () =>
                    {
                        CreateProfiles();
                        window.Show(new ProfilesPage(whaleProfile.id));
                    },
                    marks = new[] { (1, "btn:中身を編集", true), (2, "btn=複製", true), (3, "btn:JSON に書き出す", true), (4, ".viah-list-pane-head>.viah-icon-btn", true) },
                },

                // エクスポート
                new Shot
                {
                    name = "export-single-1",
                    plain = true,
                    setup = () =>
                    {
                        SessionState.SetInt("VInc.VIAH.exportMode", 0);
                        window.Show(new ExportPage(whaleProfile.id));
                    },
                    marks = new[] { (1, ".viah-list-item.is-active", false), (2, ".viah-banner-line", false), (3, ".viah-ver-panel", false), (4, ".viah-filename", false), (5, "btn=プレビュー", true), (6, "btn=修正", true), (7, "btn=エクスポート", true) },
                },
                new Shot
                {
                    name = "export-seq-1",
                    setup = () =>
                    {
                        var seq = (HashSet<string>)typeof(ExportPage).GetField("seqChecked", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                        seq.Clear();
                        seq.Add(whaleProfile.id);
                        seq.Add(liteProfile.id);
                        SessionState.SetInt("VInc.VIAH.exportMode", 1);
                        window.Show(new ExportPage());
                    },
                    marks = new[] { (1, "seg:シーケンスエクスポート", false), (2, "card:名前に付ける要素", false), (3, ".viah-seq-list", false), (4, "btn:選択中のバージョンを +0.0.1", true), (5, "btn:件をエクスポート|btn=エクスポート", true) },
                    cleanup = () => SessionState.SetInt("VInc.VIAH.exportMode", 0),
                },

                // そのほか
                new Shot
                {
                    name = "search-1",
                    setup = () => window.Navigate("home"),
                    after = () => SearchField().field.value = "くじら",
                    marks = new[] { (1, ".viah-search", false), (2, ".viah-search-pop", false) },
                    cleanup = () => SearchField().field.value = "",
                },
                new Shot
                {
                    name = "settings-1",
                    setup = () => window.Show(new SettingsPage()),
                    marks = new[] { (1, ".viah-settings-btn", true), (2, "#card-theme", false), (3, "#card-workflows", false) },
                },
            };
            return list;
        }

        // ───────────── 進行 ─────────────

        static void Tick()
        {
            if (window == null)
            {
                log.AppendLine("FAIL ウィンドウが閉じられました");
                Finish();
                return;
            }
            window.Repaint();
            if (EditorApplication.timeSinceStartup < phaseTime) return;
            if (index >= shots.Count)
            {
                Finish();
                return;
            }
            var shot = shots[index];
            try
            {
                switch (phase)
                {
                    case 0:
                        shot.setup?.Invoke();
                        phase = shot.after != null ? 1 : 2;
                        break;
                    case 1:
                        shot.after();
                        phase = 2;
                        break;
                    default:
                        ClearToast();
                        HideTempPaths();
                        Capture(shot);
                        shot.cleanup?.Invoke();
                        index++;
                        phase = 0;
                        break;
                }
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                log.AppendLine($"FAIL {shot.name}: {inner}");
                index++;
                phase = 0;
            }
            phaseTime = EditorApplication.timeSinceStartup + WaitSeconds;
        }

        static void Capture(Shot shot)
        {
            if (!originReady) throw new Exception("画面の位置がわかりません");
            var ppp = EditorGUIUtility.pixelsPerPoint;
            var root = window.rootVisualElement;
            var size = root.layout.size;
            int w = Mathf.RoundToInt(size.x * ppp), h = Mathf.RoundToInt(size.y * ppp);
            var px = InternalEditorUtility.ReadScreenPixel(new Vector2(origin.x * ppp, origin.y * ppp), w, h);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, shot.name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            var json = new ShotJson { name = shot.name, width = w, height = h, ppp = ppp, plain = shot.plain };
            foreach (var (n, spec, arrow) in shot.marks)
            {
                var e = spec.Split('|').Select(Find).FirstOrDefault(x => x != null);
                if (e == null)
                {
                    log.AppendLine($"  MISSING {shot.name} ({n}) {spec}");
                    continue;
                }
                // スクロールで隠れている部分は画面の中に切り詰める
                var r = e.worldBound;
                var clip = ClipRect(e);
                var x0 = Mathf.Max(r.xMin, clip.xMin);
                var y0 = Mathf.Max(r.yMin, clip.yMin);
                var x1 = Mathf.Min(r.xMax, clip.xMax);
                var y1 = Mathf.Min(r.yMax, clip.yMax);
                if (x1 - x0 < 2 || y1 - y0 < 2)
                {
                    log.AppendLine($"  HIDDEN {shot.name} ({n}) {spec} rect={r}");
                    continue;
                }
                // worldBound はパネル基準（タブの分ずれる）なので、ウィンドウの中身の左上を原点にする
                var o = root.worldBound.position;
                json.marks.Add(new MarkJson { n = n, x = (x0 - o.x) * ppp, y = (y0 - o.y) * ppp, w = (x1 - x0) * ppp, h = (y1 - y0) * ppp, arrow = arrow });
            }
            File.WriteAllText(Path.Combine(OutDir, shot.name + ".json"), JsonUtility.ToJson(json, true), new UTF8Encoding(false));
            log.AppendLine($"OK   {shot.name} ({w}x{h}) marks={json.marks.Count}/{shot.marks.Length}");
        }

        /// <summary>要素が見えている範囲（スクロールビューやウィンドウで切られる範囲）。</summary>
        static Rect ClipRect(VisualElement e)
        {
            var rect = window.rootVisualElement.worldBound;
            for (var p = e.parent; p != null; p = p.parent)
            {
                if (p is ScrollView sv)
                {
                    var vb = sv.contentViewport.worldBound;
                    rect = Rect.MinMaxRect(Mathf.Max(rect.xMin, vb.xMin), Mathf.Max(rect.yMin, vb.yMin), Mathf.Min(rect.xMax, vb.xMax), Mathf.Min(rect.yMax, vb.yMax));
                }
            }
            return rect;
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            try
            {
                foreach (var kv in hintBackup) EditorPrefs.SetBool(kv.Key, kv.Value);
                ResetForm();
                ViahStorage.DataDirOverride = null;
                ViahDatabase.Reload();
                if (window != null)
                {
                    window.ApplyTheme();
                    window.RebuildSidebar();
                    window.Navigate("home");
                }
                if (AssetDatabase.IsValidFolder(Whale)) AssetDatabase.DeleteAsset(Whale);
                if (AssetDatabase.IsValidFolder(Shared)) AssetDatabase.DeleteAsset(Shared);
                Annotate();
            }
            catch (Exception e)
            {
                log.AppendLine("FAIL 後片付け: " + e);
            }
            log.Insert(0, $"RESULT shots={shots?.Count ?? 0} errors={errors.Count}\n");
            foreach (var e in errors) log.AppendLine("ERROR " + e);
            File.WriteAllText(LogPath, log.ToString(), new UTF8Encoding(false));
        }

        /// <summary>annotate-guide.ps1 で枠・番号・矢印を描く。</summary>
        static void Annotate()
        {
            var pkg = Path.GetFullPath("Packages/com.vinc.avatar-hub");
            var script = Path.Combine(pkg, "Tests", "Editor", "annotate-guide.ps1");
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -In \"{Path.GetFullPath(OutDir)}\" -Out \"{Path.Combine(pkg, "Editor", "Guide", "Images")}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            log.AppendLine("annotate: " + output.Trim());
            AssetDatabase.Refresh();
        }
        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error) errors.Add(condition + "\n" + stackTrace);
        }

        // ───────────── 画面の操作 ─────────────

        static readonly Type FormType = typeof(ProjectsPage).GetNestedType("Form", BindingFlags.NonPublic);

        static void ResetForm() => FormType.GetMethod("Reset", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);

        static void SetForm(string field, object value) => FormType.GetField(field, BindingFlags.Static | BindingFlags.Public).SetValue(null, value);

        static void CallPage(string method, params object[] args)
        {
            var page = window.Current;
            var m = page.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) throw new Exception($"{page.GetType().Name}.{method} がありません");
            m.Invoke(page, args.Length == 0 ? null : args);
        }

        static ViahTextField SearchField() =>
            (ViahTextField)typeof(ViahWindow).GetField("searchField", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);

        static void ScrollTo(string spec)
        {
            var e = Find(spec);
            if (e == null) throw new Exception("スクロール先が見つかりません: " + spec);
            for (var p = e.parent; p != null; p = p.parent)
            {
                if (p is ScrollView sv)
                {
                    sv.ScrollTo(e);
                    return;
                }
            }
        }

        static void Press(string spec)
        {
            if (!(Find(spec) is Button b)) throw new Exception("ボタンが見つかりません: " + spec);
            using var e = NavigationSubmitEvent.GetPooled();
            e.target = b;
            b.SendEvent(e);
        }

        /// <summary>一時的な保存先のパスが写らないよう、ふだんの保存先の表記に置き換える。</summary>
        static void HideTempPaths()
        {
            var temp = Path.GetFullPath(DataDir);
            foreach (var l in window.rootVisualElement.Query<Label>().ToList())
            {
                if (l.text == null || l.text.IndexOf(temp, StringComparison.OrdinalIgnoreCase) < 0) continue;
                l.text = l.text.Replace(temp, @"ドキュメント\V.inc\Avatar Hub");
            }
        }

        static void ClearToast()
        {
            window.rootVisualElement.Q(className: "viah-toast-layer")?.Clear();
        }

        /// <summary>
        /// 要素の指定：#名前 / btn:含む文字 / btn=同じ文字 / nav:サイドバー / card:見出し / seg:セグメント / switch:ラベル /
        /// .クラス.クラス@含む文字 / 親の指定>.子のクラス
        /// </summary>
        static VisualElement Find(string spec)
        {
            var root = window.rootVisualElement;
            var gt = spec.IndexOf('>');
            if (gt > 0)
            {
                var parent = Find(spec.Substring(0, gt));
                var childCls = spec.Substring(gt + 1).TrimStart('.').Split('.');
                return parent?.Query(className: childCls[0]).ToList().FirstOrDefault(e => Visible(e) && childCls.All(e.ClassListContains));
            }
            if (spec.StartsWith("#")) return root.Q(spec.Substring(1));
            if (spec.StartsWith("btn:") || spec.StartsWith("btn="))
            {
                var t = spec.Substring(4);
                bool exact = spec[3] == '=';
                bool Match(string s) => !string.IsNullOrEmpty(s) && (exact ? s.Trim() == t : s.Contains(t));
                return root.Query<Button>().ToList().FirstOrDefault(b => Visible(b) && (Match(b.text) || b.Query<Label>().ToList().Any(l => Match(l.text))));
            }
            if (spec.StartsWith("nav:"))
            {
                var t = spec.Substring(4);
                return root.Query(className: "viah-nav-item").ToList().FirstOrDefault(e => e.Query<Label>().ToList().Any(l => l.text == t));
            }
            if (spec.StartsWith("card:"))
            {
                var t = spec.Substring(5);
                return root.Query(className: "viah-card").ToList().FirstOrDefault(c => Visible(c) && c.Query<Label>(className: "viah-section-title").ToList().Any(l => l.text.Contains(t)));
            }
            if (spec.StartsWith("seg:"))
            {
                var t = spec.Substring(4);
                return root.Query<Label>(className: "viah-seg-item").ToList().FirstOrDefault(l => Visible(l) && l.text == t);
            }
            if (spec.StartsWith("switch:"))
            {
                var t = spec.Substring(7);
                return root.Query<Switch>().ToList().FirstOrDefault(s => Visible(s) && s.Query<Label>().ToList().Any(l => l.text.Contains(t)));
            }
            if (spec.StartsWith("."))
            {
                var parts = spec.Substring(1).Split(new[] { '@' }, 2);
                var cls = parts[0].Split('.');
                IEnumerable<VisualElement> list = root.Query(className: cls[0]).ToList().Where(e => Visible(e) && cls.All(e.ClassListContains));
                if (parts.Length > 1)
                {
                    var t = parts[1];
                    list = list.Where(e => (e is Label l && l.text.Contains(t)) || e.Query<Label>().ToList().Any(l => l.text.Contains(t)));
                }
                return list.FirstOrDefault();
            }
            return null;
        }

        static bool Visible(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None) return false;
            return e.worldBound.width > 0 && e.worldBound.height > 0;
        }
    }
}

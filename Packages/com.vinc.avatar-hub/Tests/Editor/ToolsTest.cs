using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace VInc.AvatarHub.Tests
{
    /// <summary>
    /// 開発用：プロジェクトブックマーカー（登録・Undo・JSON・Project タブの切り替えとロック）と、アセットSearcher（逆引き）を確かめる。
    /// Temp/viah_tools.request を置いてスクリプトを再読み込みすると実行し、Temp/viah_tools.log に結果を書く。
    /// データは Temp の中の別の場所に保存し、実際のデータには触らない。Project タブの表示とロックは最後に元へ戻す。
    /// </summary>
    [InitializeOnLoad]
    static class ToolsTest
    {
        const string Request = "Temp/viah_tools.request";
        const string LogPath = "Temp/viah_tools.log";
        const string Root = "Assets/VIAH_ToolsTest";
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly List<string> log = new List<string>();
        static int pass, fail;

        static ToolsTest()
        {
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            EditorApplication.delayCall += Run;
        }

        static void Check(bool ok, string what)
        {
            if (ok) pass++;
            else fail++;
            log.Add((ok ? "OK   " : "NG   ") + what);
        }

        static string work;
        static readonly List<Action> restore = new List<Action>();
        static readonly Queue<Action> steps = new Queue<Action>();
        static int wait;

        static void Run()
        {
            log.Clear();
            pass = fail = 0;
            restore.Clear();
            steps.Clear();
            work = Path.GetFullPath("Temp/viah_tools");
            if (Directory.Exists(work)) Directory.Delete(work, true);
            ViahStorage.DataDirOverride = Path.Combine(work, "data");
            ViahDatabase.Reload();
            steps.Enqueue(Setup);
            steps.Enqueue(() => Bookmarks(work));
            TabSteps();
            steps.Enqueue(Searcher);
            SoundSteps();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        /// <summary>Project タブの切り替えは、タブが描かれてから終わることがあるので、手順のあいだにフレームを待つ。</summary>
        static void Tick()
        {
            if (wait > 0)
            {
                wait--;
                return;
            }
            if (steps.Count == 0)
            {
                Finish();
                return;
            }
            try
            {
                steps.Dequeue()();
            }
            catch (Exception e)
            {
                fail++;
                log.Add("EXCEPTION " + e);
                steps.Clear();
            }
        }

        static void Wait(int frames) => steps.Enqueue(() => wait = frames);

        static void Finish()
        {
            EditorApplication.update -= Tick;
            foreach (var r in restore)
            {
                try { r(); }
                catch (Exception e) { log.Add("元に戻せませんでした: " + e.Message); }
            }
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            ViahStorage.DataDirOverride = null;
            ViahDatabase.Reload();
            if (Directory.Exists(work)) Directory.Delete(work, true);
            log.Add($"== 結果：OK {pass} / NG {fail}");
            File.WriteAllText(LogPath, string.Join("\n", log));
        }

        // ───────────── サンプル ─────────────

        static void Setup()
        {
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            AssetDatabase.CreateFolder("Assets", "VIAH_ToolsTest");
            AssetDatabase.CreateFolder(Root, "A");
            AssetDatabase.CreateFolder(Root, "B");
            AssetDatabase.CreateFolder(Root + "/B", "Sub");

            var t = new Texture2D(4, 4);
            File.WriteAllBytes(Root + "/A/Tex.png", t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(Root + "/A/Tex.png");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/A/Tex.png");
            var mat = new Material(Shader.Find("Standard")) { mainTexture = tex };
            AssetDatabase.CreateAsset(mat, Root + "/A/Mat.mat");
            AssetDatabase.CreateAsset(new Material(Shader.Find("Standard")) { mainTexture = tex }, Root + "/B/Mat2.mat");
            var clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, Root + "/B/Sub/Open.anim");
            var ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip(Root + "/B/Sub/FX.controller", clip);

            var root = new GameObject("Gimmick");
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform);
            body.GetComponent<MeshRenderer>().sharedMaterial = mat;
            root.AddComponent<Animator>().runtimeAnimatorController = ctrl;
            PrefabUtility.SaveAsPrefabAsset(root, Root + "/Gimmick.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
        }

        // ───────────── ブックマーク ─────────────

        static void Bookmarks(string work)
        {
            log.Add("== ブックマーク");
            var a = Root + "/A";
            var sub = Root + "/B/Sub";
            Check(BookmarkActions.Add(new[] { a, sub, a }) == 2, "2 件追加（同じものは重ねない）");
            Check(BookmarkActions.Items.Select(b => b.DisplayLabel).SequenceEqual(new[] { "A", "Sub" }), "名前はフォルダー名");
            Check(File.Exists(ViahStorage.BookmarksFile), "テスト用の保存先に保存した");

            BookmarkActions.Rename(BookmarkActions.Items[1], "アニメーション");
            Check(BookmarkActions.Items[1].DisplayLabel == "アニメーション", "名前を変えられる（全角も OK）");
            BookmarkActions.Move(1, -1);
            Check(BookmarkActions.Items[0].DisplayLabel == "アニメーション", "並べ替えられる");

            Undo.IncrementCurrentGroup();
            BookmarkActions.Remove(BookmarkActions.Items[0]);
            Check(BookmarkActions.Items.Count == 1, "外せる");
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();
            Check(BookmarkActions.Items.Count == 2 && BookmarkActions.Items[0].DisplayLabel == "アニメーション", "Ctrl+Z で戻る");

            var file = Path.Combine(work, "share.json");
            BookmarkActions.Export(file);
            BookmarkActions.Items.Clear();
            ViahDatabase.SaveBookmarks("テスト：空にする");
            var msg = BookmarkActions.Import(file);
            Check(BookmarkActions.Items.Count == 2, "JSON から読み込める：" + msg);
            msg = BookmarkActions.Import(file);
            Check(BookmarkActions.Items.Count == 2 && msg.Contains("飛ばしました"), "同じものは重ねない：" + msg);

            // 別の Unity プロジェクトから来たもの：GUID は違うが、同じパスがあれば使える
            var other = new Bookmark { id = ViahStorage.NewId(), target = new AssetRef { guid = "0123456789abcdef0123456789abcdef", path = a } };
            Check(BookmarkActions.PathOf(other) == a, "別のプロジェクトのもの：同じパスがあれば使える");
            var missing = new Bookmark { id = ViahStorage.NewId(), target = new AssetRef { guid = "", path = "Assets/NotHere/Folder" } };
            Check(BookmarkActions.PathOf(missing) == null && BookmarkActions.Jump(missing).Contains("ありません"), "無い場所は飛ばずに知らせる");
        }

        // ───────────── Project タブの切り替え ─────────────

        static readonly Type BrowserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");

        static string ActiveFolder(EditorWindow w) => BrowserType.GetMethod("GetActiveFolderPath", Any, null, Type.EmptyTypes, null)?.Invoke(w, null) as string;

        static void SetLocked(EditorWindow w, bool on)
        {
            var p = BrowserType.GetProperty("isLocked", Any);
            if (p != null && p.CanWrite) p.SetValue(w, on);
            else BrowserType.GetField("m_IsLocked", Any)?.SetValue(w, on);
        }

        static List<EditorWindow> before;
        static EditorWindow target, next;
        static UnityEngine.Object selBefore;

        static void ShowFolder(EditorWindow w, string folder)
        {
            BrowserType.GetMethod("Init", Any, null, Type.EmptyTypes, null)?.Invoke(w, null);
            BrowserType.GetMethod("ShowFolderContents", Any, null, new[] { typeof(int), typeof(bool) }, null)?.Invoke(w, new object[] { AssetDatabase.LoadMainAssetAtPath(folder).GetInstanceID(), true });
        }

        static void TabSteps()
        {
            var a = Root + "/A";
            var sub = Root + "/B/Sub";
            steps.Enqueue(() =>
            {
                log.Add("== Project タブ");
                foreach (var name in new[] { "Init", "ShowFolderContents", "FrameObject", "IsTwoColumns", "GetActiveFolderPath" })
                    log.Add($"情報：ProjectBrowser.{name} " + (BrowserType.GetMethods(Any).Any(m => m.Name == name) ? "あり" : "なし"));
                before = ProjectTabs.All();
                log.Add($"情報：Project タブ {before.Count} 個（ロック中 {before.Count(ProjectTabs.IsLocked)} 個）");
                foreach (var w in before)
                {
                    var win = w;
                    var locked = ProjectTabs.IsLocked(win);
                    restore.Add(() => SetLocked(win, locked));
                }
                // 新しく開いたタブは最後に閉じる
                restore.Add(() =>
                {
                    foreach (var w in ProjectTabs.All().Where(x => !before.Contains(x))) w.Close();
                });
                foreach (var w in before) SetLocked(w, false);
                selBefore = Selection.activeObject;
                target = ProjectTabs.Target(out _);
                var folderBefore = ActiveFolder(target);
                restore.Add(() =>
                {
                    if (target != null && !string.IsNullOrEmpty(folderBefore) && AssetDatabase.IsValidFolder(folderBefore)) ShowFolder(target, folderBefore);
                });
                Check(ProjectTabs.Jump(a) == null, "フォルダーへ飛ぶ");
            });
            Wait(30);
            steps.Enqueue(() =>
            {
                Check(ActiveFolder(target) == a, "最後に使ったロックなしのタブが切り替わる：" + ActiveFolder(target));
                Check(Selection.activeObject == selBefore, "選択（Inspector の表示）は変わらない");
                // いま切り替えたタブをロックすると、ほかのタブ（無ければ新しいタブ）が切り替わる
                SetLocked(target, true);
                next = ProjectTabs.Target(out var created);
                var nextBefore = ActiveFolder(next);
                restore.Add(() =>
                {
                    if (next != null && !string.IsNullOrEmpty(nextBefore) && AssetDatabase.IsValidFolder(nextBefore)) ShowFolder(next, nextBefore);
                });
                Check(next != target, "ロック中のタブは使わない" + (created ? "（ほかに無いので新しいタブを開いた）" : ""));
                Check(ProjectTabs.Jump(sub) == null, "ほかのタブへ飛ぶ");
            });
            Wait(30);
            steps.Enqueue(() =>
            {
                Check(ActiveFolder(next) == sub, "ほかのタブが切り替わる：" + ActiveFolder(next));
                Check(ActiveFolder(target) == a, "ロック中のタブはそのまま：" + ActiveFolder(target));
                // アセットを登録した場合は、そのフォルダーを開く
                SetLocked(target, false);
                Check(ProjectTabs.Jump(Root + "/B/Mat2.mat") == null, "アセットへ飛ぶ");
            });
            Wait(30);
            steps.Enqueue(() =>
            {
                Check(ActiveFolder(ProjectTabs.Target(out _)) == Root + "/B", "アセットならそのフォルダーを開く：" + ActiveFolder(ProjectTabs.Target(out _)));
                // 全部ロック中なら新しいタブを開く
                foreach (var w in ProjectTabs.All()) SetLocked(w, true);
                int count = ProjectTabs.All().Count;
                ProjectTabs.Target(out var opened);
                Check(opened && ProjectTabs.All().Count == count + 1, "全部ロック中なら新しいタブを開く");
            });
            Wait(10);
        }

        // ───────────── 効果音 ─────────────

        static readonly List<bool> played = new List<bool>();
        static bool? loadedOk;
        static int loadedSamples;

        static void OnPlayed(bool attention) => played.Add(attention);

        static void WriteWav(string path, float[] data, int rate) => ViahSound.WriteWav(path, data, 1, rate);

        static void SoundSteps()
        {
            steps.Enqueue(() =>
            {
                log.Add("== 効果音");
                foreach (var (key, label) in ViahSound.Presets)
                {
                    var d = ViahSound.Synth(key);
                    float peak = d.Max(x => Math.Abs(x));
                    double sec = d.Length / (double)ViahSound.Rate;
                    Check(d.Length > 0 && Math.Abs(peak - 0.9f) < 0.01f && sec < 3, $"内蔵の音「{label}」を合成（{sec:0.00} 秒・最大 {peak:0.00}）");
                    // 試し聴き用に wav でも書き出しておく（テストのあとも残す）
                    var dir = Path.GetFullPath("Temp/viah_sound_preview");
                    Directory.CreateDirectory(dir);
                    WriteWav(Path.Combine(dir, label + ".wav"), d, ViahSound.Rate);
                }
                var util = typeof(Editor).Assembly.GetType("UnityEditor.AudioUtil");
                log.Add("情報：AudioUtil.PlayPreviewClip " + (util?.GetMethod("PlayPreviewClip", Any, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null) != null ? "あり" : "なし（Beep で代わり）"));

                var s = ViahDatabase.Settings;
                s.soundVolume = 0; // テスト中は鳴らさない（鳴らそうとしたかだけ見る）
                s.soundOn = true;
                s.soundSkipShort = true;
                s.soundMuted.Clear();
                played.Clear();
                ViahSound.Played -= OnPlayed;
                ViahSound.Played += OnPlayed;
                ViahSound.Done("seq-export", 1);
                Check(played.Count == 0, "すぐ終わったとき（3 秒未満）は鳴らさない");
                ViahSound.Done("seq-export", 5);
                Check(played.Count == 1 && played[0] == false, "時間がかかったら完了の音");
                ViahSound.Attention("seq-import", 5);
                Check(played.Count == 2 && played[1], "確認待ちは別の音");
                s.soundMuted.Add("seq-export");
                ViahSound.Done("seq-export", 5);
                Check(played.Count == 2, "オフにした作業では鳴らさない");
                s.soundOn = false;
                ViahSound.Attention("seq-import", 5);
                Check(played.Count == 2, "効果音をオフにすると鳴らさない");
                s.soundOn = true;
                s.soundMuted.Clear();

                // 自分の音：wav を作って取り込み、読めるか
                var src = Path.Combine(work, "my chime.wav");
                Directory.CreateDirectory(work);
                WriteWav(src, ViahSound.Synth("pon"), ViahSound.Rate);
                var name = ViahSound.CopyIn(src);
                Check(File.Exists(Path.Combine(ViahStorage.SoundsDir, name)), "Sounds フォルダーにコピー：" + name);
                Check(ViahSound.CopyIn(src) != name, "同じ名前は (2) を付けて別に置く");
                loadedOk = null;
                ViahSound.Load(Path.Combine(ViahStorage.SoundsDir, name), r =>
                {
                    loadedOk = r != null;
                    loadedSamples = r?.data.Length ?? 0;
                });
                s.soundDone = ViahSound.FileKey;
                s.soundDoneFile = name;
            });
            Wait(120);
            steps.Enqueue(() =>
            {
                Check(loadedOk == true && loadedSamples > 1000, $"自分の音（wav）を読めた（{loadedSamples} サンプル）");
                played.Clear();
                ViahSound.Play(false);
                Check(played.Count == 1, "自分の音でも鳴らせる");
                ViahDatabase.Settings.soundVolume = 0.01f;
                ViahSound.Play(true);
                Check(ViahSound.LastResult == "windows", "Windows の機能で鳴らした：" + ViahSound.LastResult);
                ViahDatabase.Settings.soundVolume = 0;
                ViahDatabase.Settings.soundDoneFile = "消えたファイル.wav";
                ViahSound.Play(false);
                Check(played.Count == 3 && ViahSound.LastResult == "音量が 0", "ファイルが消えていても止まらない（内蔵の音で知らせる）");
                ViahSound.Played -= OnPlayed;
            });
            Wait(10);
        }

        // ───────────── アセットSearcher ─────────────

        static void Searcher()
        {
            log.Add("== アセットSearcher");
            string tex = Root + "/A/Tex.png", mat = Root + "/A/Mat.mat", mat2 = Root + "/B/Mat2.mat", clip = Root + "/B/Sub/Open.anim",
                ctrl = Root + "/B/Sub/FX.controller", prefab = Root + "/Gimmick.prefab";

            var direct = ReferenceFinder.FindUsers(tex, Root, false, false);
            Check(direct.Select(h => h.path).OrderBy(x => x).SequenceEqual(new[] { mat, mat2 }.OrderBy(x => x)), "テクスチャ → 使っているマテリアル 2 つ：" + string.Join(",", direct.Select(h => h.path)));
            Check(direct.All(h => h.Direct), "直接のものだけ");

            var deep = ReferenceFinder.FindUsers(tex, Root, true, false);
            var p = deep.FirstOrDefault(h => h.path == prefab);
            Check(p != null && p.via.SequenceEqual(new[] { mat }), "その先：プレハブ（経由 Mat.mat）");

            var byClip = ReferenceFinder.FindUsers(clip, Root, false, false);
            Check(byClip.Count == 1 && byClip[0].path == ctrl, "アニメーション → コントローラー");
            var byClipDeep = ReferenceFinder.FindUsers(clip, Root, true, false);
            Check(byClipDeep.Any(h => h.path == prefab && h.via.SequenceEqual(new[] { ctrl })), "その先：プレハブ（経由 FX.controller）");

            var inFolder = ReferenceFinder.FindUsers(tex, Root + "/B", false, false);
            Check(inFolder.Count == 1 && inFolder[0].path == mat2, "フォルダーの中だけ探す");

            var sw = Stopwatch.StartNew();
            var all = ReferenceFinder.FindUsers(tex, "Assets", false, false);
            Check(all.Count == 2, $"Assets 全体からも見つかる（{ReferenceFinder.AssetsIn("Assets").Count} 件を {sw.ElapsedMilliseconds}ms）");

            var inPrefab = ReferenceFinder.FindInPrefab(prefab, mat, false);
            var h1 = inPrefab.FirstOrDefault();
            Check(inPrefab.Count == 1 && h1.objectPath == "Gimmick/Body" && h1.component == "Mesh Renderer" && h1.property == "Materials[0]",
                $"プレハブの中：{h1?.objectPath} › {h1?.component} › {h1?.property}");
            Check(ReferenceFinder.FindInPrefab(prefab, clip, false).Count == 0, "直接は使っていない");
            var viaCtrl = ReferenceFinder.FindInPrefab(prefab, clip, true);
            var h2 = viaCtrl.FirstOrDefault();
            Check(viaCtrl.Count == 1 && h2.component == "Animator" && h2.path == ctrl && h2.via.SequenceEqual(new[] { ctrl }),
                $"その先：{h2?.objectPath} › {h2?.component} › {h2?.property}（{string.Join(" › ", h2?.via ?? new List<string>())}）");
            Check(ReferenceFinder.PrettyProperty("m_Materials.Array.data[0]") == "Materials[0]", "欄の名前を読みやすくする");
        }
    }
}

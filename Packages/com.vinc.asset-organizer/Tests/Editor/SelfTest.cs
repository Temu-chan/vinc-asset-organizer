using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace VInc.AssetOrganizer.Tests
{
    /// <summary>
    /// 開発用の自己テスト。メニュー「V.inc/開発/Asset Organizer 自己テスト」か、
    /// Temp/ao_selftest.request を置いた状態でスクリプトを再読み込みすると実行し、Temp/ao_selftest.log に結果を書く。
    /// </summary>
    [InitializeOnLoad]
    static class SelfTest
    {
        const string Request = "Temp/ao_selftest.request";
        const string LogPath = "Temp/ao_selftest.log";
        const string Base = "Assets/AO_Sample";
        const string External = "Assets/AO_External";

        static readonly StringBuilder log = new StringBuilder();
        static int pass, fail;

        static SelfTest()
        {
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            EditorApplication.delayCall += Run;
        }

        [MenuItem("V.inc/開発/Asset Organizer 自己テスト", priority = 1000)]
        static void Run()
        {
            log.Clear();
            pass = fail = 0;
            try
            {
                TestVersion();
                TestNaming();
                TestSearch();
                TestFolderBuilder();
                CreateSample();
                TestScanAndExport();
            }
            catch (Exception e)
            {
                Fail("例外: " + e);
            }
            log.Insert(0, $"RESULT pass={pass} fail={fail}\n");
            File.WriteAllText(LogPath, log.ToString(), new UTF8Encoding(false));
            Debug.Log("[Asset Organizer SelfTest]\n" + log);
        }

        static void Check(bool ok, string what)
        {
            if (ok) { pass++; log.AppendLine("OK   " + what); }
            else Fail(what);
        }

        static void Fail(string what)
        {
            fail++;
            log.AppendLine("FAIL " + what);
        }

        static void Eq(string expected, string actual, string what) =>
            Check(expected == actual, $"{what}: expected='{expected}' actual='{actual}'");

        // ───────────── 単体 ─────────────

        static void TestVersion()
        {
            Eq("0.1.0", new PkgVersion(0, 0, 9).Step(2, +1).ToString(), "0.0.9 最下位↑");
            Eq("1.3.0", new PkgVersion(1, 2, 5).Step(1, +1).ToString(), "1.2.5 真ん中↑");
            Eq("2.0.0", new PkgVersion(1, 2, 5).Step(0, +1).ToString(), "1.2.5 先頭↑");
            Eq("9.9.9", new PkgVersion(9, 9, 9).Step(2, +1).ToString(), "9.9.9 上限");
            Eq("0.0.9", new PkgVersion(0, 1, 0).Step(2, -1).ToString(), "0.1.0 最下位↓");
            Eq("0.0.1", new PkgVersion(0, 0, 1).Step(2, -1).ToString(), "0.0.1 下限");
            Check(PkgVersion.TryParse("Ver1.2.3", out var v) && v.ToString() == "1.2.3", "Ver1.2.3 を読める");
        }

        static void TestNaming()
        {
            var t = new DateTime(2026, 10, 25, 18, 51, 0);
            var v = new PkgVersion(0, 0, 1);
            Eq("productC", PackageNaming.Build("productC", new NameOptions(), t), "なし");
            Eq("productC_Ver0.0.1", PackageNaming.Build("productC", new NameOptions { useVersion = true, version = v }, t), "Ver");
            Eq("productC_Ver0.0.1-2026_10_25", PackageNaming.Build("productC", new NameOptions { useVersion = true, version = v, useDate = true }, t), "Ver+日付");
            Eq("productC_Ver0.0.1-2026_10_25-18_51", PackageNaming.Build("productC", new NameOptions { useVersion = true, version = v, useDate = true, useTime = true }, t), "Ver+日付+時刻");
            Eq("productC_2026_10_25-18_51", PackageNaming.Build("productC", new NameOptions { useDate = true, useTime = true }, t), "日付+時刻");
            Eq("productC_Debug_Ver0.0.1", PackageNaming.Build("productC", new NameOptions { useDebug = true, debugLabel = "Debug", useVersion = true, version = v }, t), "Debug+Ver");
        }

        static void TestSearch()
        {
            Eq(SearchUtil.Normalize("てくすちゃ"), SearchUtil.Normalize("テクスチャ"), "ひらがな=カタカナ");
            Eq(SearchUtil.Normalize("ｸｼﾞﾗ"), SearchUtil.Normalize("くじら"), "半角カナ");
            Eq(SearchUtil.Normalize("ProductC"), SearchUtil.Normalize("product_c"), "大文字小文字・区切り");
        }

        static void TestFolderBuilder()
        {
            var p = DefaultPresets.Standard();
            var ctrl = p.nodes.Find(n => n.name == "Controllers");
            Eq("Animations/Controllers", FolderBuilder.RelativePath(ctrl, p.nodes, false), "相対パス");
            Eq("02_Animations/01_Controllers", FolderBuilder.RelativePath(ctrl, p.nodes, true), "番号付き相対パス");
            Check(FolderBuilder.ValidateName("a/b") != null, "スラッシュは不可");
            Check(FolderBuilder.ValidateName("productC") == null, "productC は可");
        }

        // ───────────── サンプル作成 ─────────────

        static void CreateSample()
        {
            if (AssetDatabase.IsValidFolder(Base)) AssetDatabase.DeleteAsset(Base);
            if (AssetDatabase.IsValidFolder(External)) AssetDatabase.DeleteAsset(External);

            var preset = DefaultPresets.Standard();
            var ids = preset.nodes.Where(n => n.defaultOn).Select(n => n.id).ToList();
            int created = FolderBuilder.Create(Base, preset.nodes, ids, false);
            Check(created == ids.Count + 1, $"フォルダー作成数 {created}");
            Check(AssetDatabase.IsValidFolder(Base + "/Animations/Controllers"), "入れ子フォルダー");
            FolderBuilder.EnsureFolder(External);

            var tex1 = MakeTexture(Base + "/Textures/Body.png", Color.red);
            var tex2 = MakeTexture(Base + "/Textures/Glow.png", Color.cyan);
            var shared = MakeTexture(External + "/Shared.png", Color.gray);

            var mat1 = new Material(Shader.Find("Standard")) { mainTexture = tex1 };
            mat1.SetTexture("_EmissionMap", shared);
            AssetDatabase.CreateAsset(mat1, Base + "/Materials/Body.mat");
            var mat2 = new Material(Shader.Find("Standard")) { mainTexture = tex2 };
            AssetDatabase.CreateAsset(mat2, Base + "/Materials/Glow.mat");

            var clip = new AnimationClip();
            var binding = EditorCurveBinding.PPtrCurve("Body", typeof(MeshRenderer), "m_Materials.Array.data[0]");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, new[]
            {
                new ObjectReferenceKeyframe { time = 0, value = mat2 },
            });
            AssetDatabase.CreateAsset(clip, Base + "/Animations/GlowOn.anim");
            AnimatorController.CreateAnimatorControllerAtPathWithClip(Base + "/Animations/Controllers/FX.controller", clip);

            var unused = MakeTexture(Base + "/Textures/Unused.png", Color.black);

            var root = new GameObject("Gimmick");
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform);
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            body.GetComponent<MeshRenderer>().sharedMaterial = mat1;

            var maType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator"))
                .FirstOrDefault(t => t != null);
            Check(maType != null, "MA Merge Animator 型が見つかる");
            if (maType != null)
            {
                var comp = root.AddComponent(maType);
                var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Base + "/Animations/Controllers/FX.controller");
                maType.GetField("animator").SetValue(comp, ctrl);
            }
            PrefabUtility.SaveAsPrefabAsset(root, Base + "/Prefabs/Gimmick.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static Texture2D MakeTexture(string path, Color c)
        {
            var t = new Texture2D(4, 4);
            var px = Enumerable.Repeat(c, 16).ToArray();
            t.SetPixels(px);
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ───────────── 依存解析・ルール・書き出し ─────────────

        static void TestScanAndExport()
        {
            var seeds = DependencyScanner.ExpandSeeds(new[] { Base + "/Prefabs" });
            Check(seeds.Count == 1 && seeds[0].EndsWith("Gimmick.prefab"), "フォルダーからプレハブを展開: " + string.Join(",", seeds));

            var scan = DependencyScanner.Scan(seeds, false);
            foreach (var p in new[]
                     {
                         "/Animations/Controllers/FX.controller", "/Animations/GlowOn.anim",
                         "/Materials/Body.mat", "/Materials/Glow.mat", "/Textures/Body.png", "/Textures/Glow.png",
                     })
                Check(scan.assets.ContainsKey(Base + p), "検出: " + p);
            Check(scan.assets.ContainsKey(External + "/Shared.png"), "外部の依存も検出");
            Check(!scan.assets.ContainsKey(Base + "/Textures/Unused.png"), "使っていないテクスチャは検出しない");
            var reason = scan.ReasonOf(Base + "/Animations/GlowOn.anim");
            Check(reason.Contains("MA Merge Animator"), "理由に MA Merge Animator: " + reason);
            Check(scan.packageDeps.Count > 0, $"Packages 依存は別枠 ({scan.packageDeps.Count}件)");

            var m = new SelectionModel();
            m.SetFolder(Base + "/Textures", true);
            m.SetFile(Base + "/Textures/Unused.png", false);
            m.SetFile(Base + "/Materials/Body.mat", true);
            var files = m.ResolveFiles();
            Check(files.Contains(Base + "/Textures/Body.png") && !files.Contains(Base + "/Textures/Unused.png"), "フォルダールール＋除外");

            MakeTexture(Base + "/Textures/Added.png", Color.white);
            Check(m.ResolveFiles().Contains(Base + "/Textures/Added.png"), "後から増えたファイルも含まれる");

            // プロファイル経由の検証
            var profile = new PackageProfile { name = "SelfTest", baseFolder = new AssetRef(Base) };
            profile.Sanitize();
            profile.seeds.Add(new AssetRef(Base + "/Prefabs"));
            m.WriteTo(profile);
            profile.ignoredDependencies.Add(new AssetRef(External + "/Shared.png"));
            var report = ProfileResolver.Validate(profile, true, false);
            Check(report.newDeps.Contains(Base + "/Animations/GlowOn.anim"), "未登録の依存を警告: " + string.Join(",", report.newDeps));
            Check(!report.newDeps.Contains(Base + "/Textures/Body.png"), "登録済みは警告しない");
            Check(report.newExternalDeps.Count == 0, "無視した外部依存は警告しない");

            // 書き出し
            var all = scan.assets.Keys.Where(k => k.StartsWith(Base + "/")).ToList();
            var outPath = Path.GetFullPath("Temp/ao_selftest.unitypackage");
            if (File.Exists(outPath)) File.Delete(outPath);
            PackageExporter.Export(all, outPath);
            Check(File.Exists(outPath), "unitypackage を書き出せる");
            if (File.Exists(outPath))
            {
                var entries = ReadPackagePaths(outPath);
                log.AppendLine("  中身: " + string.Join(" | ", entries));
                Check(entries.Contains(Base + "/Prefabs/Gimmick.prefab"), "プレハブが入っている");
                Check(entries.Contains(Base + "/Textures"), "親フォルダーが入っている");
                Check(!entries.Contains(External + "/Shared.png"), "外部ファイルは入らない");
                Check(!entries.Contains(Base + "/Textures/Unused.png"), "指定外のファイルは入らない");
                Check(!entries.Any(e => e.StartsWith("Packages/")), "Packages は入らない");
            }
        }

        /// <summary>unitypackage（tar.gz）の中のパス一覧。</summary>
        static List<string> ReadPackagePaths(string file)
        {
            var result = new List<string>();
            using var fs = File.OpenRead(file);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            gz.CopyTo(ms);
            var data = ms.ToArray();
            int pos = 0;
            while (pos + 512 <= data.Length)
            {
                var name = Encoding.ASCII.GetString(data, pos, 100).TrimEnd('\0');
                if (string.IsNullOrEmpty(name)) break;
                var sizeText = Encoding.ASCII.GetString(data, pos + 124, 12).Trim('\0', ' ');
                long size = string.IsNullOrEmpty(sizeText) ? 0 : Convert.ToInt64(sizeText, 8);
                int body = pos + 512;
                if (name.EndsWith("/pathname") || name == "pathname")
                    result.Add(Encoding.UTF8.GetString(data, body, (int)size).Split('\n')[0].Trim());
                pos = body + (int)((size + 511) / 512 * 512);
            }
            return result;
        }
    }
}

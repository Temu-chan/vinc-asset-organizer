using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace VInc.AssetOrganizer
{
    public class DetectedAsset
    {
        public string path;
        /// <summary>どのアセットからたどり着いたか（起点なら null）。</summary>
        public string parent;
        /// <summary>プレハブのどのコンポーネントから参照されていたか（例: "MA Merge Animator"）。</summary>
        public string via;
        public bool isSeed;
    }

    public class ScanResult
    {
        public readonly Dictionary<string, DetectedAsset> assets = new Dictionary<string, DetectedAsset>();
        /// <summary>Packages 配下の依存（VRCSDK・MA・lilToon など。unitypackage には入れられない）。</summary>
        public readonly HashSet<string> packageDeps = new HashSet<string>();
        public readonly List<string> seeds = new List<string>();

        /// <summary>例: "Gimmick.prefab › MA Merge Animator › FX.controller"</summary>
        public string ReasonOf(string path)
        {
            if (!assets.TryGetValue(path, out var a)) return "";
            if (a.isSeed) return "登録したアセット";
            var chain = new List<string>();
            var cur = a;
            int guard = 0;
            string via = null;
            while (cur != null && !cur.isSeed && guard++ < 32)
            {
                var parent = cur.parent;
                if (parent == null) break;
                if (assets.TryGetValue(parent, out var p) && p.isSeed) via = cur.via;
                chain.Insert(0, Path.GetFileName(parent));
                assets.TryGetValue(parent, out cur);
            }
            if (!string.IsNullOrEmpty(via) && chain.Count > 0) chain.Insert(1, via);
            if (chain.Count > 4) chain = new List<string> { chain[0], chain[1], "…", chain[chain.Count - 1] };
            return string.Join(" › ", chain);
        }
    }

    /// <summary>
    /// プレハブから使っているアセットをたどる。
    /// MA Merge Animator・VRC Avatar Descriptor などの参照も普通のシリアライズ参照なので、
    /// MA に依存せず AssetDatabase の依存関係だけで追いかけられる。
    /// </summary>
    public static class DependencyScanner
    {
        /// <summary>フォルダーは中のプレハブに展開する（プレハブが無ければ中のアセットすべて）。</summary>
        public static List<string> ExpandSeeds(IEnumerable<string> seedPaths)
        {
            var result = new List<string>();
            var seen = new HashSet<string>();
            foreach (var s in seedPaths)
            {
                if (string.IsNullOrEmpty(s)) continue;
                if (AssetDatabase.IsValidFolder(s))
                {
                    var found = AssetDatabase.FindAssets("t:Prefab", new[] { s })
                        .Select(AssetDatabase.GUIDToAssetPath).ToList();
                    if (found.Count == 0)
                    {
                        found = AssetDatabase.FindAssets("", new[] { s })
                            .Select(AssetDatabase.GUIDToAssetPath)
                            .Where(p => !AssetDatabase.IsValidFolder(p)).ToList();
                    }
                    foreach (var p in found)
                        if (seen.Add(p)) result.Add(p);
                }
                else if (seen.Add(s))
                {
                    result.Add(s);
                }
            }
            return result;
        }

        public static int CountPrefabsIn(string folder) =>
            AssetDatabase.IsValidFolder(folder) ? AssetDatabase.FindAssets("t:Prefab", new[] { folder }).Length : 0;

        public static ScanResult Scan(IList<string> seeds, bool showProgress = true)
        {
            var r = new ScanResult();
            r.seeds.AddRange(seeds);
            var labels = new Dictionary<string, string>();
            var queue = new Queue<string>();

            try
            {
                for (int i = 0; i < seeds.Count; i++)
                {
                    var s = seeds[i];
                    if (showProgress)
                        EditorUtility.DisplayProgressBar("Asset Organizer", $"コンポーネントを調べています… {Path.GetFileName(s)}", (float)i / Math.Max(1, seeds.Count) * 0.3f);
                    if (r.assets.ContainsKey(s)) continue;
                    r.assets[s] = new DetectedAsset { path = s, isSeed = true };
                    queue.Enqueue(s);
                    CollectComponentLabels(s, labels);
                }

                int processed = 0;
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    processed++;
                    if (showProgress && processed % 20 == 0)
                        EditorUtility.DisplayProgressBar("Asset Organizer", $"依存関係をたどっています… ({r.assets.Count}件)", 0.3f + 0.7f * (1f - 1f / (1f + processed / 200f)));

                    foreach (var dep in AssetDatabase.GetDependencies(cur, false))
                    {
                        if (dep == cur) continue;
                        if (dep.StartsWith("Packages/", StringComparison.Ordinal))
                        {
                            r.packageDeps.Add(dep);
                            continue;
                        }
                        if (!dep.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                        if (r.assets.ContainsKey(dep)) continue;

                        string via = null;
                        if (r.assets.TryGetValue(cur, out var curInfo) && curInfo.isSeed)
                            labels.TryGetValue(cur + "|" + dep, out via);

                        r.assets[dep] = new DetectedAsset { path = dep, parent = cur, via = via };
                        queue.Enqueue(dep);
                    }
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }
            return r;
        }

        /// <summary>プレハブ内のコンポーネントが参照しているアセットに「どのコンポーネント経由か」を記録する。</summary>
        static void CollectComponentLabels(string prefabPath, Dictionary<string, string> labels)
        {
            if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (go == null) return;

            foreach (var comp in go.GetComponentsInChildren<Component>(true))
            {
                if (comp == null || comp is Transform) continue;
                var label = FriendlyName(comp.GetType().Name);
                SerializedObject so;
                try { so = new SerializedObject(comp); }
                catch { continue; }

                var prop = so.GetIterator();
                int guard = 0;
                while (prop.Next(true) && guard++ < 20000)
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (prop.name == "m_Script" || prop.name == "m_GameObject" || prop.name == "m_PrefabInstance" || prop.name == "m_PrefabAsset" || prop.name == "m_CorrespondingSourceObject") continue;
                    var obj = prop.objectReferenceValue;
                    if (obj == null) continue;
                    var path = AssetDatabase.GetAssetPath(obj);
                    if (string.IsNullOrEmpty(path) || path == prefabPath) continue;
                    var key = prefabPath + "|" + path;
                    if (!labels.ContainsKey(key)) labels[key] = label;
                }
                so.Dispose();
            }
        }

        static readonly Regex Words = new Regex("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])");

        /// <summary>"ModularAvatarMergeAnimator" → "MA Merge Animator"</summary>
        public static string FriendlyName(string typeName)
        {
            if (typeName.StartsWith("ModularAvatar", StringComparison.Ordinal))
                return "MA " + Words.Replace(typeName.Substring("ModularAvatar".Length), " ");
            if (typeName.StartsWith("VRC", StringComparison.Ordinal))
                return "VRC " + Words.Replace(typeName.Substring(3), " ");
            return Words.Replace(typeName, " ");
        }

        /// <summary>種類の表示名。</summary>
        public static string KindOf(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return "フォルダー";
            var ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".prefab": return "プレハブ";
                case ".controller": return "アニメーター";
                case ".overridecontroller": return "アニメーター";
                case ".anim": return "アニメーション";
                case ".mat": return "マテリアル";
                case ".png": case ".jpg": case ".jpeg": case ".tga": case ".psd": case ".tif": case ".tiff": case ".exr": case ".hdr": case ".bmp":
                    return "テクスチャ";
                case ".fbx": case ".obj": case ".blend": case ".dae": case ".mesh":
                    return "モデル";
                case ".shader": case ".cginc": case ".hlsl": case ".shadergraph": case ".shadersubgraph":
                    return "シェーダー";
                case ".wav": case ".mp3": case ".ogg": case ".aif": case ".aiff": case ".flac":
                    return "オーディオ";
                case ".cs": case ".asmdef": case ".asmref": return "スクリプト";
                case ".unity": return "シーン";
                case ".asset":
                    var t = AssetDatabase.GetMainAssetTypeAtPath(path);
                    if (t != null && (t.Name == "VRCExpressionsMenu" || t.Name == "VRCExpressionParameters")) return "Expression";
                    if (t != null && t.Name == "BlendTree") return "ブレンドツリー";
                    return "アセット";
                default: return "ファイル";
            }
        }
    }
}

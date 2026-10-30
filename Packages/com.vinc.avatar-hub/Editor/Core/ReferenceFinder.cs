using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace VInc.AvatarHub
{
    /// <summary>アセットSearcher の結果 1 件。</summary>
    public class ReferenceHit
    {
        /// <summary>対象を使っているアセット（プレハブの中を調べたときは、コンポーネントが参照しているアセット）。</summary>
        public string path;
        /// <summary>対象 → via[0] → via[1] → path の途中。直接使っていれば空。</summary>
        public readonly List<string> via = new List<string>();
        public bool Direct => via.Count == 0;

        // プレハブの中を調べたとき
        public string objectPath;
        public string component;
        public string property;
    }

    /// <summary>
    /// 「このアセットを使っているもの」を探す（逆引き）。
    /// Assets 全体・フォルダーの中は AssetDatabase の依存関係だけで調べ、アセットは読み込まない。
    /// プレハブの中を調べるときだけ、そのプレハブを読み込む（参照先は読み込まない）。
    /// </summary>
    [InitializeOnLoad]
    public static class ReferenceFinder
    {
        /// <summary>直接の依存のキャッシュ。アセットが変わったら捨てる。</summary>
        static readonly Dictionary<string, string[]> depsCache = new Dictionary<string, string[]>(StringComparer.Ordinal);

        static ReferenceFinder()
        {
            EditorApplication.projectChanged += depsCache.Clear;
        }

        static string[] DirectDeps(string path)
        {
            if (!depsCache.TryGetValue(path, out var d))
            {
                d = AssetDatabase.GetDependencies(path, false);
                depsCache[path] = d;
            }
            return d;
        }

        /// <summary>フォルダーの中のアセット（フォルダー自体は除く）。</summary>
        public static List<string> AssetsIn(string folder) =>
            AssetDatabase.FindAssets("", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && !AssetDatabase.IsValidFolder(p))
                .Distinct()
                .ToList();

        /// <summary>
        /// folder の中で target を使っているアセット。indirect なら、それを使っているもの…と先までたどる（途中もフォルダーの中だけ）。
        /// キャンセルされたら null。
        /// </summary>
        public static List<ReferenceHit> FindUsers(string target, string folder, bool indirect, bool showProgress = true)
        {
            var assets = AssetsIn(folder);
            // 逆引きの表：依存先 → それを直接使っているアセット
            var users = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            try
            {
                for (int i = 0; i < assets.Count; i++)
                {
                    if (showProgress && i % 100 == 0 &&
                        EditorUtility.DisplayCancelableProgressBar("アセットSearcher", $"使っているものを探しています… {i} / {assets.Count}", (float)i / Math.Max(1, assets.Count)))
                        return null;
                    var a = assets[i];
                    foreach (var dep in DirectDeps(a))
                    {
                        if (dep == a) continue;
                        if (!users.TryGetValue(dep, out var list)) users[dep] = list = new List<string>();
                        list.Add(a);
                    }
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }

            var hits = new List<ReferenceHit>();
            var seen = new HashSet<string>(StringComparer.Ordinal) { target };
            var queue = new Queue<ReferenceHit>();
            void Visit(string from, List<string> via)
            {
                if (!users.TryGetValue(from, out var list)) return;
                foreach (var u in list.OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (!seen.Add(u)) continue;
                    var h = new ReferenceHit { path = u };
                    h.via.AddRange(via);
                    hits.Add(h);
                    queue.Enqueue(h);
                }
            }
            Visit(target, new List<string>());
            while (indirect && queue.Count > 0)
            {
                var h = queue.Dequeue();
                if (h.via.Count >= 8) continue;
                Visit(h.path, new List<string>(h.via) { h.path });
            }
            return hits;
        }

        /// <summary>
        /// プレハブの中で target を使っているところ（オブジェクト › コンポーネント › 欄）。
        /// indirect なら、コンポーネントが参照しているアセット（コントローラーなど）の先で使っているものも出す。
        /// </summary>
        public static List<ReferenceHit> FindInPrefab(string prefabPath, string target, bool indirect)
        {
            var hits = new List<ReferenceHit>();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (root == null) return hits;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var chains = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var comp in root.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue; // スクリプトが見つからないコンポーネント
                SerializedObject so;
                try { so = new SerializedObject(comp); }
                catch { continue; }
                var prop = so.GetIterator();
                int guard = 0;
                while (prop.Next(true) && guard++ < 20000)
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (prop.name == "m_Script" || prop.name == "m_GameObject" || prop.name == "m_PrefabInstance" || prop.name == "m_PrefabAsset" || prop.name == "m_CorrespondingSourceObject") continue;
                    // 参照先は読み込まずにパスだけ調べる
                    var id = prop.objectReferenceInstanceIDValue;
                    if (id == 0) continue;
                    var path = AssetDatabase.GetAssetPath(id);
                    if (string.IsNullOrEmpty(path) || path == prefabPath) continue;

                    List<string> chain = null;
                    if (path != target)
                    {
                        if (!indirect) continue;
                        if (!chains.TryGetValue(path, out chain)) chains[path] = chain = Chain(path, target);
                        if (chain == null) continue;
                    }
                    var h = new ReferenceHit
                    {
                        path = path,
                        objectPath = ObjectPath(comp.transform, root.transform),
                        component = DependencyScanner.FriendlyName(comp.GetType().Name),
                        property = PrettyProperty(prop.propertyPath),
                    };
                    if (chain != null) h.via.AddRange(chain);
                    if (keys.Add(h.objectPath + "|" + h.component + "|" + h.property)) hits.Add(h);
                }
                so.Dispose();
            }
            return hits;
        }

        /// <summary>from から依存をたどって target に着く道（from を含み、target は含まない）。着かなければ null。</summary>
        static List<string> Chain(string from, string target)
        {
            var parent = new Dictionary<string, string>(StringComparer.Ordinal) { [from] = null };
            var queue = new Queue<string>();
            queue.Enqueue(from);
            while (queue.Count > 0 && parent.Count < 5000)
            {
                var cur = queue.Dequeue();
                foreach (var dep in DirectDeps(cur))
                {
                    if (dep == target)
                    {
                        var chain = new List<string>();
                        for (var p = cur; p != null; p = parent[p]) chain.Insert(0, p);
                        return chain;
                    }
                    if (dep.StartsWith("Assets/", StringComparison.Ordinal) && !parent.ContainsKey(dep))
                    {
                        parent[dep] = cur;
                        queue.Enqueue(dep);
                    }
                }
            }
            return null;
        }

        static string ObjectPath(Transform t, Transform root)
        {
            var names = new List<string>();
            for (var p = t; p != null; p = p.parent)
            {
                names.Insert(0, p.name);
                if (p == root) break;
            }
            return string.Join("/", names);
        }

        /// <summary>例: m_Materials.Array.data[0] → Materials[0]</summary>
        public static string PrettyProperty(string propertyPath)
        {
            var p = Regex.Replace(propertyPath, @"\.Array\.data\[(\d+)\]", "[$1]");
            return string.Join(".", p.Split('.').Select(s => s.StartsWith("m_", StringComparison.Ordinal) && s.Length > 2 ? s.Substring(2) : s));
        }
    }
}

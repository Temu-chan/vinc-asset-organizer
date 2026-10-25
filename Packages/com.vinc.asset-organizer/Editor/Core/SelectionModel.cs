using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace VInc.AssetOrganizer
{
    /// <summary>
    /// 「何を含めるか」のルール。いちばん近いルールが勝つ：
    /// ファイル自身の指定 → 近い親フォルダーの指定（含める / 含めない）→ 何もなければ含めない。
    /// フォルダーを「含める」にすると、あとから増えたファイルも自動で含まれる。
    /// </summary>
    public class SelectionModel
    {
        public readonly HashSet<string> includeFolders = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> includeFiles = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> excludes = new HashSet<string>(StringComparer.Ordinal);

        public static SelectionModel FromProfile(PackageProfile p, List<AssetRef> missing = null)
        {
            var m = new SelectionModel();
            Fill(p.includeFolders, m.includeFolders, missing);
            Fill(p.includeFiles, m.includeFiles, missing);
            Fill(p.excludes, m.excludes, null);
            return m;
        }

        static void Fill(List<AssetRef> refs, HashSet<string> set, List<AssetRef> missing)
        {
            foreach (var r in refs)
            {
                var path = r.Resolve();
                if (path != null) set.Add(path);
                else missing?.Add(r);
            }
        }

        public void WriteTo(PackageProfile p)
        {
            p.includeFolders = AssetRef.FromPaths(includeFolders.OrderBy(x => x, StringComparer.Ordinal));
            p.includeFiles = AssetRef.FromPaths(includeFiles.OrderBy(x => x, StringComparer.Ordinal));
            p.excludes = AssetRef.FromPaths(excludes.OrderBy(x => x, StringComparer.Ordinal));
        }

        public SelectionModel Clone()
        {
            var m = new SelectionModel();
            m.includeFolders.UnionWith(includeFolders);
            m.includeFiles.UnionWith(includeFiles);
            m.excludes.UnionWith(excludes);
            return m;
        }

        public bool IsEmpty => includeFolders.Count == 0 && includeFiles.Count == 0;

        static string Parent(string path)
        {
            int i = path.LastIndexOf('/');
            return i > 0 ? path.Substring(0, i) : null;
        }

        /// <summary>親フォルダーのルールから受け継ぐ状態。</summary>
        public bool Inherited(string path)
        {
            var p = Parent(path);
            while (!string.IsNullOrEmpty(p))
            {
                if (includeFolders.Contains(p)) return true;
                if (excludes.Contains(p)) return false;
                p = Parent(p);
            }
            return false;
        }

        public bool IsFileIncluded(string path)
        {
            if (includeFiles.Contains(path)) return true;
            if (excludes.Contains(path)) return false;
            return Inherited(path);
        }

        /// <summary>そのファイルに対して、ユーザーが明示的に「含めない」と決めているか。</summary>
        public bool IsExplicitlyExcluded(string path)
        {
            if (excludes.Contains(path)) return true;
            if (includeFiles.Contains(path)) return false;
            var p = Parent(path);
            while (!string.IsNullOrEmpty(p))
            {
                if (includeFolders.Contains(p)) return false;
                if (excludes.Contains(p)) return true;
                p = Parent(p);
            }
            return false;
        }

        public void SetFile(string path, bool on)
        {
            includeFiles.Remove(path);
            excludes.Remove(path);
            if (Inherited(path) != on) (on ? includeFiles : excludes).Add(path);
        }

        /// <summary>フォルダーの中身すべて（今後増えるファイルも）を含める / 外す。</summary>
        public void SetFolder(string folder, bool on)
        {
            var prefix = folder + "/";
            includeFolders.RemoveWhere(x => x == folder || x.StartsWith(prefix, StringComparison.Ordinal));
            includeFiles.RemoveWhere(x => x.StartsWith(prefix, StringComparison.Ordinal));
            excludes.RemoveWhere(x => x == folder || x.StartsWith(prefix, StringComparison.Ordinal));
            if (Inherited(folder) != on) (on ? includeFolders : excludes).Add(folder);
        }

        /// <summary>実際に含まれるファイルの一覧（並び順はパス順）。</summary>
        public List<string> ResolveFiles()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var folder in includeFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                foreach (var g in AssetDatabase.FindAssets("", new[] { folder }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (string.IsNullOrEmpty(p) || AssetDatabase.IsValidFolder(p)) continue;
                    if (IsFileIncluded(p)) set.Add(p);
                }
            }
            foreach (var f in includeFiles)
            {
                if (AssetRef.Exists(f) && !AssetDatabase.IsValidFolder(f) && IsFileIncluded(f)) set.Add(f);
            }
            var list = set.ToList();
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }
}

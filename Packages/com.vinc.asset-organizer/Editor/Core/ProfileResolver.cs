using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace VInc.AssetOrganizer
{
    /// <summary>エクスポート前のチェック結果。</summary>
    public class ValidationReport
    {
        public PackageProfile profile;
        public List<string> files = new List<string>();
        /// <summary>登録したのに見つからないファイル・フォルダー。</summary>
        public List<AssetRef> missing = new List<AssetRef>();
        /// <summary>起点のプレハブなどが見つからない。</summary>
        public List<AssetRef> missingSeeds = new List<AssetRef>();
        /// <summary>プレハブが新しく使い始めたのに未登録のファイル（基準フォルダーの中）。</summary>
        public List<string> newDeps = new List<string>();
        /// <summary>プレハブが新しく使い始めた、基準フォルダーの外のファイル。</summary>
        public List<string> newExternalDeps = new List<string>();
        /// <summary>含まれているが基準フォルダーの外にあるファイル。</summary>
        public List<string> externalIncluded = new List<string>();
        public ScanResult scan;

        public bool IsEmpty => files.Count == 0;
        public bool HasWarnings => missing.Count > 0 || missingSeeds.Count > 0 || newDeps.Count > 0 || newExternalDeps.Count > 0;
        public int WarningCount => missing.Count + missingSeeds.Count + newDeps.Count + newExternalDeps.Count;

        public string Summary(int maxLines = 12)
        {
            var lines = new List<string>();
            void Section(string title, IEnumerable<string> items)
            {
                var list = items.ToList();
                if (list.Count == 0) return;
                lines.Add($"■ {title}（{list.Count}件）");
                foreach (var s in list.Take(4)) lines.Add("　・" + s);
                if (list.Count > 4) lines.Add($"　…ほか {list.Count - 4} 件");
            }
            Section("使われているのに登録されていないファイル", newDeps);
            Section("新しく使われ始めた外部のファイル", newExternalDeps);
            Section("見つからない登録ファイル", missing.Select(m => m.path));
            Section("見つからない起点プレハブ", missingSeeds.Select(m => m.path));
            return string.Join("\n", lines.Take(maxLines * 2));
        }
    }

    public static class ProfileResolver
    {
        public static string BaseFolderOf(PackageProfile p)
        {
            var b = p.baseFolder?.Resolve();
            if (!string.IsNullOrEmpty(b)) return b;
            var proj = AODatabase.FindProject(p.projectId);
            return proj?.RootPath;
        }

        public static bool IsInside(string path, string baseFolder)
        {
            if (string.IsNullOrEmpty(baseFolder)) return true;
            return path == baseFolder || path.StartsWith(baseFolder + "/", StringComparison.Ordinal);
        }

        public static ValidationReport Validate(PackageProfile p, bool rescan = true, bool showProgress = true)
        {
            var r = new ValidationReport { profile = p };
            var model = SelectionModel.FromProfile(p, r.missing);
            r.files = model.ResolveFiles();
            var baseFolder = BaseFolderOf(p);
            r.externalIncluded = r.files.Where(f => !IsInside(f, baseFolder)).ToList();

            if (!rescan || p.seeds.Count == 0) return r;

            var seedPaths = new List<string>();
            foreach (var s in p.seeds)
            {
                var path = s.Resolve();
                if (path == null) r.missingSeeds.Add(s);
                else seedPaths.Add(path);
            }
            if (seedPaths.Count == 0) return r;

            r.scan = DependencyScanner.Scan(DependencyScanner.ExpandSeeds(seedPaths), showProgress);
            var included = new HashSet<string>(r.files, StringComparer.Ordinal);
            var ignored = new HashSet<string>(StringComparer.Ordinal);
            foreach (var i in p.ignoredDependencies)
            {
                var path = i.Resolve();
                if (path != null) ignored.Add(path);
            }

            foreach (var dep in r.scan.assets.Keys)
            {
                if (included.Contains(dep) || ignored.Contains(dep)) continue;
                if (model.IsExplicitlyExcluded(dep)) continue;
                if (IsInside(dep, baseFolder)) r.newDeps.Add(dep);
                else r.newExternalDeps.Add(dep);
            }
            r.newDeps.Sort(StringComparer.Ordinal);
            r.newExternalDeps.Sort(StringComparer.Ordinal);
            return r;
        }
    }

    public static class PackageExporter
    {
        public static string OutputDirOf(PackageProfile p)
        {
            if (!string.IsNullOrWhiteSpace(p.outputFolder)) return p.outputFolder;
            var d = AODatabase.State.defaultOutputFolder;
            return string.IsNullOrWhiteSpace(d) ? AOStorage.DefaultExportDir : d;
        }

        public static string OutputPathOf(PackageProfile p, string fileNameWithoutExt) =>
            Path.Combine(OutputDirOf(p), fileNameWithoutExt + ".unitypackage");

        /// <summary>
        /// 指定したファイルだけを書き出す（依存の自動追加はしない＝余計なものが混ざらない）。
        /// 親フォルダーも一緒に入れて、フォルダーの GUID を保つ。
        /// </summary>
        public static void Export(IList<string> files, string fullPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            var set = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();
            foreach (var f in files)
            {
                foreach (var folder in ParentFolders(f))
                    if (set.Add(folder)) ordered.Add(folder);
                if (set.Add(f)) ordered.Add(f);
            }
            AssetDatabase.ExportPackage(ordered.ToArray(), fullPath, ExportPackageOptions.Default);
        }

        static IEnumerable<string> ParentFolders(string path)
        {
            var parts = path.Split('/');
            var cur = parts[0];
            for (int i = 1; i < parts.Length - 1; i++)
            {
                cur += "/" + parts[i];
                yield return cur;
            }
        }

        /// <summary>書き出しが終わったら履歴を更新する。</summary>
        public static void Record(PackageProfile p, NameOptions o, string fileName)
        {
            var h = AODatabase.State.Get(p.id, true);
            if (o.useVersion) h.lastVersion = o.version.ToString();
            h.lastExportAt = AOStorage.Now();
            h.lastFile = fileName;
            h.useVersion = o.useVersion;
            h.useDate = o.useDate;
            h.useTime = o.useTime;
            h.useDebug = o.useDebug;
            AODatabase.SaveState();
        }

        /// <summary>前回のバージョン（無ければ null）。</summary>
        public static PkgVersion? LastVersion(PackageProfile p)
        {
            var h = AODatabase.State.Get(p.id, false);
            if (h != null && PkgVersion.TryParse(h.lastVersion, out var v)) return v;
            return null;
        }
    }
}

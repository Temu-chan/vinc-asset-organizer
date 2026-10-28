using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace VInc.AvatarHub
{
    public enum PackageItemState
    {
        /// <summary>同じ GUID のアセットが同じ場所にある。</summary>
        Same,
        /// <summary>同じ GUID のアセットが別の場所にある（インポート後に移動・名前変更した）。</summary>
        Moved,
        /// <summary>まだプロジェクトに入っていない。</summary>
        Missing,
        /// <summary>
        /// 同じパスに GUID の違うファイルがある。Unity のインポート画面で黄色の「!」が付くもの
        /// （File exists in project, but with different GUID）。
        /// </summary>
        Conflict,
        /// <summary>Assets の外（Packages・ProjectSettings など）。プロファイルには入れられない。</summary>
        OutOfScope,
    }

    public class PackageItem
    {
        public PackageEntry entry;
        public PackageItemState state;
        /// <summary>プロジェクト内のいまのパス（無ければ null）。</summary>
        public string projectPath;
    }

    /// <summary>unitypackage の中身と、いまのプロジェクトとの照らし合わせ。</summary>
    public class PackageAnalysis
    {
        public PackageContents contents;
        public readonly List<PackageItem> items = new List<PackageItem>();

        public IEnumerable<PackageItem> Files => items.Where(i => !i.entry.isFolder && i.state != PackageItemState.OutOfScope);
        public int Count(PackageItemState s) => items.Count(i => !i.entry.isFolder && i.state == s);
        public int FileCount => Files.Count();
        public int InProject => Count(PackageItemState.Same) + Count(PackageItemState.Moved);
        public List<PackageItem> Conflicts => items.Where(i => !i.entry.isFolder && i.state == PackageItemState.Conflict).ToList();
        /// <summary>インポートすると増える・変わるものがあるか。</summary>
        public bool NeedsImport => Count(PackageItemState.Missing) > 0 || Count(PackageItemState.Conflict) > 0;

        public static PackageAnalysis Analyze(PackageContents c)
        {
            var a = new PackageAnalysis { contents = c };
            foreach (var e in c.entries)
            {
                var item = new PackageItem { entry = e };
                if (!e.path.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    item.state = PackageItemState.OutOfScope;
                }
                else
                {
                    var byGuid = AssetDatabase.GUIDToAssetPath(e.guid);
                    if (AssetRef.Exists(byGuid))
                    {
                        item.projectPath = byGuid;
                        item.state = byGuid == e.path ? PackageItemState.Same : PackageItemState.Moved;
                    }
                    else if (AssetRef.Exists(e.path) && AssetDatabase.AssetPathToGUID(e.path) != e.guid)
                    {
                        // 黄色の「!」。そのまま入れると、Unity はこのファイルを上書きする
                        item.projectPath = e.path;
                        item.state = e.isFolder ? PackageItemState.Same : PackageItemState.Conflict;
                    }
                    else
                    {
                        item.state = PackageItemState.Missing;
                    }
                }
                a.items.Add(item);
            }
            return a;
        }

        public static PackageAnalysis Read(string file, bool showProgress = true)
        {
            try
            {
                var c = UnityPackageReader.Read(file, showProgress
                    ? p => EditorUtility.DisplayProgressBar("Avatar Hub", $"unitypackage を読んでいます… {Path.GetFileName(file)}", p)
                    : (Action<float>)null);
                return Analyze(c);
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }
        }
    }

    /// <summary>unitypackage の中身からプロファイルを作る。</summary>
    public static class PackageProfileBuilder
    {
        public class Result
        {
            public PackageProfile profile;
            /// <summary>プロジェクトに見つからず、プロファイルに入れなかったファイル（パッケージ内のパス）。</summary>
            public readonly List<string> notFound = new List<string>();
            /// <summary>同じパスに GUID の違う別のファイルがあるため入れなかったもの（黄色の「!」を入れなかった場合など）。</summary>
            public readonly List<string> otherFile = new List<string>();
            /// <summary>起点のプレハブが使っているのに入っていない、基準フォルダー内のファイル（入れ忘れの可能性）。</summary>
            public readonly List<string> uncovered = new List<string>();
            public PkgVersion? version;
        }

        /// <summary>
        /// 入っていたファイルだけを登録する（フォルダールールは使わない）。
        /// ほかのプレハブから使われていないプレハブを起点にして、次からの入れ忘れチェックに使う。
        /// </summary>
        public static Result Build(PackageContents c)
        {
            var r = new Result();
            var files = new List<string>();
            foreach (var e in c.entries)
            {
                if (e.isFolder || !e.path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                // GUID だけで探す。同じパスにあっても GUID が違えば別のファイルなので入れない（余計なものが混ざらないように）。
                // 黄色の「!」を「このまま入れる」で上書きした場合は、パッケージ側の GUID になるので見つかる
                var path = AssetDatabase.GUIDToAssetPath(e.guid);
                if (AssetRef.Exists(path) && !AssetDatabase.IsValidFolder(path)) files.Add(path);
                else if (AssetRef.Exists(e.path)) r.otherFile.Add(e.path);
                else r.notFound.Add(e.path);
            }
            files = files.Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();

            var name = ParseName(c.Name, out var version);
            r.version = version;
            var p = new PackageProfile { id = ViahStorage.NewId(), name = name };
            p.Sanitize();
            if (files.Count == 0)
            {
                r.profile = p;
                return r;
            }

            var basePath = CommonFolder(files);
            var proj = ViahDatabase.Projects.FirstOrDefault(x => x.RootPath != null && ProfileResolver.IsInside(basePath, x.RootPath));
            if (proj != null)
            {
                p.projectId = proj.id;
                p.tags = new List<string>(proj.tags);
            }
            p.baseFolder = new AssetRef(basePath);
            p.includeFiles = AssetRef.FromPaths(files);

            var seeds = RootPrefabs(files);
            p.mode = seeds.Count > 0 ? "auto" : "manual";
            p.seeds = AssetRef.FromPaths(seeds);
            if (seeds.Count > 0)
            {
                var included = new HashSet<string>(files, StringComparer.Ordinal);
                var scan = DependencyScanner.Scan(seeds, false);
                var ignored = new List<string>();
                foreach (var dep in scan.assets.Keys.OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (included.Contains(dep) || AssetDatabase.IsValidFolder(dep)) continue;
                    // 基準フォルダーの外（アバター本体・他の人の素材など）は、元のパッケージでも入れていなかったので警告しない
                    if (ProfileResolver.IsInside(dep, basePath)) r.uncovered.Add(dep);
                    else ignored.Add(dep);
                }
                p.ignoredDependencies = AssetRef.FromPaths(ignored);
            }
            r.profile = p;
            return r;
        }

        /// <summary>ほかのプレハブから使われていないプレハブ（いちばん外側のもの）。</summary>
        static List<string> RootPrefabs(List<string> files)
        {
            var prefabs = files.Where(f => f.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)).ToList();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pf in prefabs)
                foreach (var dep in AssetDatabase.GetDependencies(pf, true))
                    if (dep != pf) used.Add(dep);
            return prefabs.Where(pf => !used.Contains(pf)).ToList();
        }

        /// <summary>すべてのファイルを含む、いちばん深いフォルダー。</summary>
        public static string CommonFolder(List<string> files)
        {
            var common = Path.GetDirectoryName(files[0])?.Replace('\\', '/') ?? "Assets";
            foreach (var f in files.Skip(1))
                while (!ProfileResolver.IsInside(f, common) && common.Contains("/"))
                    common = common.Substring(0, common.LastIndexOf('/'));
            return common;
        }

        // 日付（＋時刻）。時刻だけのときはバージョンの直後にある場合に限る（「_10_28」を時刻と取り違えないように）
        static readonly Regex DateTimeSuffix = new Regex(@"[-_ ]\d{4}[_\-.]\d{2}[_\-.]\d{2}(?:[-_ ]\d{2}[_\-:]\d{2})?$");
        static readonly Regex VersionTimeSuffix = new Regex(@"^(.*v(?:er)?\.?\s*\d+\.\d+\.\d+)[-_ ]\d{2}[_\-:]\d{2}$", RegexOptions.IgnoreCase);
        static readonly Regex VersionSuffix = new Regex(@"[-_ ]*v(?:er)?\.?\s*(\d+)\.(\d+)\.(\d+)$", RegexOptions.IgnoreCase);

        /// <summary>
        /// ファイル名からプロファイル名とバージョンを取り出す。
        /// 例: Ring_Diamond_Gold_Ver1.0.0-2026_10_28 → Ring_Diamond_Gold / 1.0.0、Ring_Debug_Ver1.2.3 → Ring / 1.2.3
        /// </summary>
        public static string ParseName(string fileName, out PkgVersion? version)
        {
            version = null;
            var s = (fileName ?? "").Trim();
            s = DateTimeSuffix.Replace(s, "");
            var vt = VersionTimeSuffix.Match(s);
            if (vt.Success) s = vt.Groups[1].Value;
            var m = VersionSuffix.Match(s);
            if (m.Success)
            {
                int a = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int b = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                int c = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                // 各位 0〜9 の範囲なら引き継ぐ（それ以外は名前から外すだけ）
                if (a <= 9 && b <= 9 && c <= 9 && a + b + c > 0) version = new PkgVersion(a, b, c);
                s = s.Substring(0, m.Index);
            }
            var label = ViahDatabase.Settings.debugLabel?.Trim();
            if (!string.IsNullOrEmpty(label) && s.EndsWith("_" + label, StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - label.Length - 1);
            s = s.Trim(' ', '_', '-');
            return s.Length > 0 ? s : (fileName ?? "").Trim();
        }

        /// <summary>前回のバージョンとして記録し、エクスポート画面で続きの番号から始められるようにする（履歴は Undo の対象外）。</summary>
        public static void RecordVersion(PackageProfile p, PkgVersion version, string packageFile)
        {
            var h = ViahDatabase.History.Get(p.id, true);
            h.lastVersion = version.ToString();
            h.lastFile = Path.GetFileName(packageFile);
            try { h.lastExportAt = File.GetLastWriteTime(packageFile).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }
            catch { h.lastExportAt = ViahStorage.Now(); }
            ViahDatabase.SaveHistory();
        }
    }

    /// <summary>プロファイルどうしの中身（何を入れるか）の比べ合わせ。名前・タグ・書き出し先・日時は見ない。</summary>
    public static class ProfileCompare
    {
        static string Key(AssetRef r) => !string.IsNullOrEmpty(r.guid) ? r.guid : r.path;
        static HashSet<string> Keys(List<AssetRef> refs) => new HashSet<string>(refs.Select(Key), StringComparer.Ordinal);

        public static bool SameContent(PackageProfile a, PackageProfile b) =>
            a.mode == b.mode &&
            Key(a.baseFolder) == Key(b.baseFolder) &&
            Keys(a.includeFiles).SetEquals(Keys(b.includeFiles)) &&
            Keys(a.includeFolders).SetEquals(Keys(b.includeFolders)) &&
            Keys(a.excludes).SetEquals(Keys(b.excludes)) &&
            Keys(a.seeds).SetEquals(Keys(b.seeds));

        /// <summary>今のプロファイル（current）を新しい中身（next）にしたら何が変わるか。</summary>
        public static List<string> Describe(PackageProfile current, PackageProfile next)
        {
            var lines = new List<string>();
            var now = Keys(current.includeFiles);
            var after = Keys(next.includeFiles);
            int added = after.Count(k => !now.Contains(k)), removed = now.Count(k => !after.Contains(k));
            if (added > 0 || removed > 0)
                lines.Add($"登録しているファイル：{now.Count} 件 → {after.Count} 件（増える {added} 件・なくなる {removed} 件）");
            if (current.includeFolders.Count > 0)
                lines.Add($"今のプロファイルは「フォルダーごと」の登録を {current.includeFolders.Count} 件使っています。上書きすると、パッケージに入っていたファイルだけの登録になります");
            if (!Keys(current.seeds).SetEquals(Keys(next.seeds)))
                lines.Add("起点のプレハブが変わります");
            if (Key(current.baseFolder) != Key(next.baseFolder))
                lines.Add($"基準フォルダー：{current.baseFolder.Resolve() ?? current.baseFolder.path} → {next.baseFolder.Resolve() ?? next.baseFolder.path}");
            if (current.mode != next.mode)
                lines.Add(next.mode == "auto" ? "登録のしかたが「自動登録」になります" : "登録のしかたが「手動登録」になります");
            if (lines.Count == 0) lines.Add("除外の指定が変わります");
            return lines;
        }

        /// <summary>中身だけを入れ替える（名前・タグ・書き出し先・作成日はそのまま）。</summary>
        public static void CopyContent(PackageProfile from, PackageProfile to)
        {
            to.mode = from.mode;
            to.baseFolder = from.baseFolder;
            to.seeds = from.seeds;
            to.includeFolders = from.includeFolders;
            to.includeFiles = from.includeFiles;
            to.excludes = from.excludes;
            to.ignoredDependencies = from.ignoredDependencies;
            if (string.IsNullOrEmpty(to.projectId)) to.projectId = from.projectId;
        }
    }
}

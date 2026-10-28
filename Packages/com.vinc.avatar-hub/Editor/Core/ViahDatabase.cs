using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace VInc.AvatarHub
{
    /// <summary>
    /// プリセット・プロジェクト記録・プロファイル・設定の読み込みと保存。
    /// 保存・削除・読み込みは自動で Undo に記録される（UndoHub.Data）。
    /// </summary>
    public static class ViahDatabase
    {
        static List<FolderPreset> presets;
        static List<ProjectRecord> projects;
        static List<PackageProfile> profiles;
        static ViahSettings settings;
        static ExportHistory history;

        /// <summary>何かが保存・削除されたときに呼ばれる。</summary>
        public static event Action Changed;

        static void EnsureMigrated() => ViahStorage.MigrateLegacy();

        public static List<FolderPreset> Presets
        {
            get
            {
                if (presets == null) LoadPresets();
                return presets;
            }
        }

        public static List<ProjectRecord> Projects
        {
            get
            {
                if (projects == null)
                {
                    EnsureMigrated();
                    projects = LoadDir<ProjectRecord>(ViahStorage.ProjectsDir, ViahStorage.ProjectExt);
                    projects.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
                }
                return projects;
            }
        }

        public static List<PackageProfile> Profiles
        {
            get
            {
                if (profiles == null)
                {
                    EnsureMigrated();
                    profiles = LoadDir<PackageProfile>(ViahStorage.ProfilesDir, ViahStorage.ProfileExt);
                    FixDuplicateNames(profiles, ViahStorage.ProfileExt, RenameProfile);
                    profiles.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
                }
                return profiles;
            }
        }

        public static ViahSettings Settings
        {
            get
            {
                if (settings == null)
                {
                    EnsureMigrated();
                    settings = File.Exists(ViahStorage.SettingsFile) ? ViahStorage.Read<ViahSettings>(ViahStorage.SettingsFile) : null;
                    settings ??= new ViahSettings();
                    settings.Sanitize();
                }
                return settings;
            }
        }

        public static ExportHistory History
        {
            get
            {
                if (history == null)
                {
                    EnsureMigrated();
                    history = File.Exists(ViahStorage.HistoryFile) ? ViahStorage.Read<ExportHistory>(ViahStorage.HistoryFile) : null;
                    history ??= new ExportHistory();
                    history.entries ??= new List<HistoryEntry>();
                }
                return history;
            }
        }

        public static void Reload()
        {
            presets = null;
            projects = null;
            profiles = null;
            settings = null;
            history = null;
            Changed?.Invoke();
        }

        public static void NotifyChanged() => Changed?.Invoke();

        // ───────────── 検索用 ─────────────

        public static ProjectRecord FindProject(string id) =>
            string.IsNullOrEmpty(id) ? null : Projects.Find(p => p.id == id);

        public static PackageProfile FindProfile(string id) =>
            string.IsNullOrEmpty(id) ? null : Profiles.Find(p => p.id == id);

        public static FolderPreset FindPreset(string id) =>
            string.IsNullOrEmpty(id) ? null : Presets.Find(p => p.id == id);

        /// <summary>ルートフォルダーのパスからプロジェクト記録を探す。</summary>
        public static ProjectRecord FindProjectByRoot(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath)) return null;
            var guid = AssetDatabase.AssetPathToGUID(rootPath);
            return Projects.Find(p =>
                (!string.IsNullOrEmpty(guid) && p.root.guid == guid) ||
                string.Equals(p.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
        }

        public static List<PackageProfile> ProfilesOf(ProjectRecord project) =>
            project == null ? new List<PackageProfile>() : Profiles.Where(p => p.projectId == project.id).ToList();

        // ───────────── 名前の重なり ─────────────

        static readonly Regex NumberSuffix = new Regex(@"^(.*?) \((\d+)\)$");

        static bool SameName(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// ほかと重ならない名前。重なるときは「名前 (2)」「名前 (3)」…にする（JSON のファイル名の付け方と同じ）。
        /// 同じ名前が並ぶと画面で見分けられないため。
        /// </summary>
        public static string UniqueName<T>(List<T> list, string name, string exceptId = null) where T : class, IStored
        {
            name = (name ?? "").Trim();
            bool Taken(string n) => list.Any(x => x.Id != exceptId && SameName(x.DisplayName, n));
            if (!Taken(name)) return name;
            var m = NumberSuffix.Match(name);
            var baseName = m.Success ? m.Groups[1].Value : name;
            int n = m.Success ? int.Parse(m.Groups[2].Value) + 1 : 2;
            while (Taken($"{baseName} ({n})")) n++;
            return $"{baseName} ({n})";
        }

        public static string UniqueProfileName(string name, string exceptId = null) => UniqueName(Profiles, name, exceptId);

        /// <summary>プロファイルの名前を変える。unitypackage の名前は変えない（書き出すファイル名が変わらないように）。</summary>
        public static void RenameProfile(PackageProfile p, string name)
        {
            if (string.IsNullOrWhiteSpace(p.packageName)) p.packageName = p.name;
            p.name = name;
        }

        /// <summary>
        /// 以前のバージョンでは同じ名前のまま保存でき、ファイル名にだけ「(2)」が付いていた。
        /// 読み込むときに、2 つ目以降の名前をファイル名に合わせて画面でも見分けられるようにする（Undo の対象外）。
        /// </summary>
        static void FixDuplicateNames<T>(List<T> list, string ext, Action<T, string> rename) where T : class, IStored
        {
            string FileBase(T x)
            {
                var f = Path.GetFileName(x.FilePath ?? "");
                return f.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? f.Substring(0, f.Length - ext.Length) : f;
            }
            foreach (var g in list.GroupBy(x => (x.DisplayName ?? "").Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList())
            {
                // ファイル名が名前どおりのものはそのまま残す
                var keep = g.FirstOrDefault(x => SameName(FileBase(x), ViahStorage.SafeFileName(x.DisplayName))) ?? g.First();
                foreach (var x in g.Where(x => x != keep).ToList())
                {
                    var file = FileBase(x);
                    var name = NumberSuffix.IsMatch(file) && !list.Any(o => o != x && SameName(o.DisplayName, file)) ? file : UniqueName(list, x.DisplayName, x.Id);
                    rename(x, name);
                    try { ViahStorage.Write(x.FilePath, x); }
                    catch (Exception e) { Debug.LogWarning($"[Avatar Hub] 名前を直して保存できませんでした: {x.FilePath}\n{e.Message}"); }
                }
            }
        }

        // ───────────── 保存・削除（Undo に記録） ─────────────

        public static void SavePreset(FolderPreset p, string label = "プリセットを保存")
        {
            UndoHub.Data(label, () =>
            {
                p.Sanitize();
                Save(p, ViahStorage.PresetsDir, ViahStorage.PresetExt);
                Upsert(Presets, p);
                presets.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            });
            Changed?.Invoke();
        }

        public static void SaveProject(ProjectRecord p, string label = "プロジェクトを保存", string coalesceKey = null)
        {
            UndoHub.Data(label, () =>
            {
                p.Sanitize();
                if (string.IsNullOrEmpty(p.unityProject)) p.unityProject = ViahStorage.UnityProjectName;
                Save(p, ViahStorage.ProjectsDir, ViahStorage.ProjectExt);
                Upsert(Projects, p);
                projects.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            }, coalesceKey);
            Changed?.Invoke();
        }

        public static void SaveProfile(PackageProfile p, string label = "プロファイルを保存", string coalesceKey = null)
        {
            UndoHub.Data(label, () =>
            {
                p.Sanitize();
                p.updatedAt = ViahStorage.Now();
                if (string.IsNullOrEmpty(p.createdAt)) p.createdAt = p.updatedAt;
                if (string.IsNullOrEmpty(p.unityProject)) p.unityProject = ViahStorage.UnityProjectName;
                Save(p, ViahStorage.ProfilesDir, ViahStorage.ProfileExt);
                Upsert(Profiles, p);
                profiles.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            }, coalesceKey);
            Changed?.Invoke();
        }

        /// <summary>設定を保存する（Undo に記録）。</summary>
        public static void SaveSettings(string label = "設定を変更", string coalesceKey = null)
        {
            UndoHub.Data(label, () => ViahStorage.Write(ViahStorage.SettingsFile, Settings), coalesceKey);
            Changed?.Invoke();
        }

        /// <summary>書き出し履歴を保存する（Undo では戻さない）。</summary>
        public static void SaveHistory()
        {
            ViahStorage.Write(ViahStorage.HistoryFile, History);
        }

        public static void Delete(IStored item, string label = null)
        {
            UndoHub.Data(label ?? $"「{item.DisplayName}」を削除", () =>
            {
                if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath)) File.Delete(item.FilePath);
                switch (item)
                {
                    case FolderPreset fp: Presets.Remove(fp); break;
                    case ProjectRecord pr:
                        Projects.Remove(pr);
                        foreach (var prof in Profiles.Where(x => x.projectId == pr.id).ToList())
                        {
                            prof.projectId = "";
                            Save(prof, ViahStorage.ProfilesDir, ViahStorage.ProfileExt);
                        }
                        break;
                    case PackageProfile pp:
                        Profiles.Remove(pp);
                        Settings.profilePrefs.RemoveAll(h => h.profileId == pp.id);
                        ViahStorage.Write(ViahStorage.SettingsFile, Settings);
                        break;
                }
            });
            Changed?.Invoke();
        }

        /// <summary>はじめから入っているプリセットを作り直す（同じ名前のものは上書き）。</summary>
        public static void RestoreDefaultPresets()
        {
            UndoHub.Data("初期プリセットを復元", () =>
            {
                foreach (var d in DefaultPresets.Create())
                {
                    var existing = Presets.Find(p => p.name == d.name);
                    if (existing != null)
                    {
                        d.id = existing.id;
                        d.filePath = existing.filePath;
                    }
                    SavePreset(d);
                }
            });
            Changed?.Invoke();
        }

        // ───────────── JSON のやりとり ─────────────

        [Serializable]
        class KindProbe
        {
            public string kind;
        }

        /// <summary>JSON ファイルを書き出す（共有用）。</summary>
        public static void ExportJson(IStored item, string destPath)
        {
            ViahStorage.Write(destPath, item);
        }

        /// <summary>他の人からもらった JSON を取り込む。結果のメッセージを返す。</summary>
        public static string ImportJson(string path, out IStored imported)
        {
            imported = null;
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception e) { return "ファイルを読めませんでした: " + e.Message; }

            KindProbe probe = null;
            try { probe = JsonUtility.FromJson<KindProbe>(text); }
            catch { /* 下で判定 */ }

            // 旧名（Asset Organizer）の JSON も読めるようにする
            var kind = (probe?.kind ?? "").Replace("AssetOrganizer.", "AvatarHub.");
            IStored result = null;
            string message = null;
            UndoHub.Data("JSON を読み込み", () =>
            {
                switch (kind)
                {
                    case "AvatarHub.Preset":
                    {
                        var p = JsonUtility.FromJson<FolderPreset>(text);
                        p.kind = kind;
                        p.Sanitize();
                        if (!ResolveConflict(Presets, p, "プリセット")) return;
                        p.name = UniqueName(Presets, p.name, p.id);
                        SavePreset(p);
                        result = p;
                        message = $"プリセット「{p.name}」を読み込みました。";
                        break;
                    }
                    case "AvatarHub.Profile":
                    {
                        var p = JsonUtility.FromJson<PackageProfile>(text);
                        p.kind = kind;
                        p.Sanitize();
                        if (!ResolveConflict(Profiles, p, "プロファイル")) return;
                        if (FindProject(p.projectId) == null) p.projectId = "";
                        var unique = UniqueName(Profiles, p.name, p.id);
                        if (unique != p.name) RenameProfile(p, unique);
                        SaveProfile(p);
                        result = p;
                        message = $"プロファイル「{p.name}」を読み込みました。";
                        break;
                    }
                    case "AvatarHub.Project":
                    {
                        var p = JsonUtility.FromJson<ProjectRecord>(text);
                        p.kind = kind;
                        p.Sanitize();
                        if (!ResolveConflict(Projects, p, "プロジェクト")) return;
                        p.name = UniqueName(Projects, p.name, p.id);
                        SaveProject(p);
                        result = p;
                        message = $"プロジェクト「{p.name}」の記録を読み込みました。";
                        break;
                    }
                    default:
                        message = "Avatar Hub の JSON ではないようです（プリセット・プロジェクト・プロファイルのどれでもありません）。";
                        break;
                }
            });
            imported = result;
            return message;
        }

        static bool ResolveConflict<T>(List<T> list, T incoming, string label) where T : class, IStored
        {
            var same = list.Find(x => x.Id == incoming.Id);
            if (same == null) return true;
            int choice = EditorUtility.DisplayDialogComplex(
                $"同じ{label}が既にあります",
                $"「{same.DisplayName}」は既に登録されています。どうしますか？",
                "上書きする", "やめる", "別のものとして追加");
            switch (choice)
            {
                case 0:
                    incoming.FilePath = same.FilePath;
                    return true;
                case 2:
                    incoming.Id = ViahStorage.NewId();
                    incoming.FilePath = null;
                    return true;
                default:
                    return false;
            }
        }

        // ───────────── 内部 ─────────────

        static void LoadPresets()
        {
            EnsureMigrated();
            presets = LoadDir<FolderPreset>(ViahStorage.PresetsDir, ViahStorage.PresetExt);
            FixDuplicateNames(presets, ViahStorage.PresetExt, (p, n) => p.name = n);
            if (presets.Count == 0)
            {
                // はじめての起動：初期プリセットを用意（Undo には載せない）
                foreach (var p in DefaultPresets.Create())
                {
                    p.Sanitize();
                    Save(p, ViahStorage.PresetsDir, ViahStorage.PresetExt);
                    presets.Add(p);
                }
            }
            presets.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        }

        static List<T> LoadDir<T>(string dir, string ext) where T : class, IStored
        {
            var list = new List<T>();
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*" + ext))
            {
                var o = ViahStorage.Read<T>(f);
                if (o == null) continue;
                o.Sanitize();
                o.FilePath = f;
                if (list.Exists(x => x.Id == o.Id)) o.Id = ViahStorage.NewId();
                list.Add(o);
            }
            return list;
        }

        static void Save(IStored item, string dir, string ext)
        {
            var path = ViahStorage.MakeFilePath(dir, item.DisplayName, ext, item.FilePath);
            if (!string.IsNullOrEmpty(item.FilePath) && !ViahStorage.SamePath(path, item.FilePath) && File.Exists(item.FilePath))
                File.Delete(item.FilePath);
            ViahStorage.Write(path, item);
            item.FilePath = path;
        }

        static void Upsert<T>(List<T> list, T item) where T : class, IStored
        {
            int i = list.FindIndex(x => x.Id == item.Id);
            if (i >= 0) list[i] = item;
            else list.Add(item);
        }
    }
}

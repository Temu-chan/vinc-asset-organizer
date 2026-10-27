using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

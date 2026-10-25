using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VInc.AssetOrganizer
{
    /// <summary>プリセット・プロジェクト記録・プロファイル・状態の読み込みと保存。</summary>
    public static class AODatabase
    {
        static List<FolderPreset> presets;
        static List<ProjectRecord> projects;
        static List<PackageProfile> profiles;
        static OrganizerState state;

        /// <summary>何かが保存・削除されたときに呼ばれる。</summary>
        public static event Action Changed;

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
                    projects = LoadDir<ProjectRecord>(AOStorage.ProjectsDir, AOStorage.ProjectExt);
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
                    profiles = LoadDir<PackageProfile>(AOStorage.ProfilesDir, AOStorage.ProfileExt);
                    profiles.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
                }
                return profiles;
            }
        }

        public static OrganizerState State
        {
            get
            {
                if (state == null)
                {
                    state = File.Exists(AOStorage.StateFile) ? AOStorage.Read<OrganizerState>(AOStorage.StateFile) : null;
                    state ??= new OrganizerState();
                    state.history ??= new List<ExportRecord>();
                    if (string.IsNullOrEmpty(state.debugLabel)) state.debugLabel = "Debug";
                }
                return state;
            }
        }

        public static void Reload()
        {
            presets = null;
            projects = null;
            profiles = null;
            state = null;
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

        // ───────────── 保存・削除 ─────────────

        public static void SavePreset(FolderPreset p, bool notify = true)
        {
            p.Sanitize();
            Save(p, AOStorage.PresetsDir, AOStorage.PresetExt);
            Upsert(Presets, p);
            presets.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            if (notify) Changed?.Invoke();
        }

        public static void SaveProject(ProjectRecord p)
        {
            p.Sanitize();
            Save(p, AOStorage.ProjectsDir, AOStorage.ProjectExt);
            Upsert(Projects, p);
            projects.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            Changed?.Invoke();
        }

        public static void SaveProfile(PackageProfile p)
        {
            p.Sanitize();
            p.updatedAt = AOStorage.Now();
            if (string.IsNullOrEmpty(p.createdAt)) p.createdAt = p.updatedAt;
            Save(p, AOStorage.ProfilesDir, AOStorage.ProfileExt);
            Upsert(Profiles, p);
            profiles.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            Changed?.Invoke();
        }

        public static void SaveState()
        {
            AOStorage.Write(AOStorage.StateFile, State);
        }

        public static void Delete(IStored item)
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
                        SaveProfileSilently(prof);
                    }
                    break;
                case PackageProfile pp:
                    Profiles.Remove(pp);
                    State.history.RemoveAll(h => h.profileId == pp.id);
                    SaveState();
                    break;
            }
            Changed?.Invoke();
        }

        static void SaveProfileSilently(PackageProfile p) => Save(p, AOStorage.ProfilesDir, AOStorage.ProfileExt);

        /// <summary>はじめから入っているプリセットを作り直す（同じ名前のものは上書き）。</summary>
        public static void RestoreDefaultPresets()
        {
            foreach (var d in DefaultPresets.Create())
            {
                var existing = Presets.Find(p => p.name == d.name);
                if (existing != null)
                {
                    d.id = existing.id;
                    d.filePath = existing.filePath;
                }
                SavePreset(d, false);
            }
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
            AOStorage.Write(destPath, item);
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

            switch (probe?.kind)
            {
                case "AssetOrganizer.Preset":
                {
                    var p = JsonUtility.FromJson<FolderPreset>(text);
                    p.Sanitize();
                    if (!ResolveConflict(Presets, p, "プリセット")) return null;
                    SavePreset(p);
                    imported = p;
                    return $"プリセット「{p.name}」を読み込みました。";
                }
                case "AssetOrganizer.Profile":
                {
                    var p = JsonUtility.FromJson<PackageProfile>(text);
                    p.Sanitize();
                    if (!ResolveConflict(Profiles, p, "プロファイル")) return null;
                    if (FindProject(p.projectId) == null) p.projectId = "";
                    SaveProfile(p);
                    imported = p;
                    return $"プロファイル「{p.name}」を読み込みました。";
                }
                case "AssetOrganizer.Project":
                {
                    var p = JsonUtility.FromJson<ProjectRecord>(text);
                    p.Sanitize();
                    if (!ResolveConflict(Projects, p, "プロジェクト")) return null;
                    SaveProject(p);
                    imported = p;
                    return $"プロジェクト「{p.name}」の記録を読み込みました。";
                }
                default:
                    return "Asset Organizer の JSON ではないようです（プリセット・プロジェクト・プロファイルのどれでもありません）。";
            }
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
                    incoming.Id = AOStorage.NewId();
                    incoming.FilePath = null;
                    return true;
                default:
                    return false;
            }
        }

        // ───────────── 内部 ─────────────

        static void LoadPresets()
        {
            presets = LoadDir<FolderPreset>(AOStorage.PresetsDir, AOStorage.PresetExt);
            if (presets.Count == 0)
            {
                foreach (var p in DefaultPresets.Create()) SavePreset(p, false);
            }
            presets.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        }

        static List<T> LoadDir<T>(string dir, string ext) where T : class, IStored
        {
            var list = new List<T>();
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*" + ext))
            {
                var o = AOStorage.Read<T>(f);
                if (o == null) continue;
                o.Sanitize();
                o.FilePath = f;
                if (list.Exists(x => x.Id == o.Id)) o.Id = AOStorage.NewId();
                list.Add(o);
            }
            return list;
        }

        static void Save(IStored item, string dir, string ext)
        {
            var path = AOStorage.MakeFilePath(dir, item.DisplayName, ext, item.FilePath);
            if (!string.IsNullOrEmpty(item.FilePath) && !AOStorage.SamePath(path, item.FilePath) && File.Exists(item.FilePath))
                File.Delete(item.FilePath);
            AOStorage.Write(path, item);
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

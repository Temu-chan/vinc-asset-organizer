using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VInc.AvatarHub
{
    /// <summary>保存場所と JSON の読み書き。</summary>
    public static class ViahStorage
    {
        public const string PresetExt = ".preset.json";
        public const string ProjectExt = ".project.json";
        public const string ProfileExt = ".profile.json";

        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>いま開いている Unity プロジェクトのフォルダー名。</summary>
        public static string UnityProjectName => Path.GetFileName(ProjectRoot.TrimEnd('\\', '/'));

        /// <summary>
        /// データの保存先（PC 全体で共通）：ドキュメント\V.inc\Avatar Hub
        /// どの Unity プロジェクトを開いても同じプリセット・プロジェクト記録・プロファイルが使える。
        /// </summary>
        public static string DataDir => DataDirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "V.inc", "Avatar Hub");

        /// <summary>開発用：ガイドのスクリーンショットを撮るときなどに、一時的に別の保存先を使う（null で元に戻る）。</summary>
        public static string DataDirOverride;
        public static string PresetsDir => Path.Combine(DataDir, "Presets");
        public static string ProjectsDir => Path.Combine(DataDir, "Projects");
        public static string ProfilesDir => Path.Combine(DataDir, "Profiles");
        public static string SettingsFile => Path.Combine(DataDir, "settings.json");
        /// <summary>書き出しの履歴（前回のバージョンなど）。Undo では戻さない。</summary>
        public static string HistoryFile => Path.Combine(DataDir, "history.json");
        /// <summary>独立ウィンドウの前回の位置と大きさ。Undo では戻さない。</summary>
        public static string WindowFile => Path.Combine(DataDir, "window.json");

        /// <summary>unitypackage の既定の書き出し先（Unity プロジェクトごと）。</summary>
        public static string DefaultExportDir => Path.Combine(ProjectRoot, "Exports");

        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static T Read<T>(string path) where T : class
        {
            try
            {
                return JsonUtility.FromJson<T>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Avatar Hub] 読み込めませんでした: {path}\n{e.Message}");
                return null;
            }
        }

        public static void Write(string path, object obj)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(obj, true), Utf8NoBom);
        }

        public static void WriteText(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, Utf8NoBom);
        }

        /// <summary>名前からファイルパスを作る。ほかのファイルと重なったら (2) を付ける。</summary>
        public static string MakeFilePath(string dir, string name, string ext, string currentPath)
        {
            var baseName = SafeFileName(name);
            if (string.IsNullOrEmpty(baseName)) baseName = "untitled";
            var path = Path.Combine(dir, baseName + ext);
            int n = 2;
            while (File.Exists(path) && !SamePath(path, currentPath))
                path = Path.Combine(dir, $"{baseName} ({n++}){ext}");
            return path;
        }

        public static string SafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = s.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars).Trim().TrimEnd('.');
        }

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }

        public static string NewId() => Guid.NewGuid().ToString("N");

        public static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        /// <summary>"yyyy-MM-dd HH:mm:ss" を "yyyy/MM/dd" に。</summary>
        public static string ShortDate(string stamp)
        {
            if (string.IsNullOrEmpty(stamp) || stamp.Length < 10) return stamp ?? "";
            return stamp.Substring(0, 10).Replace('-', '/');
        }

        /// <summary>プロジェクト内の絶対パスなら "Assets/..." に変換する。外なら null。</summary>
        public static string ToAssetPath(string absolute)
        {
            if (string.IsNullOrEmpty(absolute)) return null;
            var full = Path.GetFullPath(absolute).Replace('\\', '/').TrimEnd('/');
            var assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            if (string.Equals(full, assets, StringComparison.OrdinalIgnoreCase)) return "Assets";
            if (full.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase))
                return "Assets/" + full.Substring(assets.Length + 1);
            return null;
        }

        /// <summary>表示用：プロジェクト内ならプロジェクトからの相対パスにする。</summary>
        public static string PrettyPath(string absolute)
        {
            if (string.IsNullOrEmpty(absolute)) return "";
            var full = Path.GetFullPath(absolute).Replace('\\', '/');
            var root = ProjectRoot.Replace('\\', '/').TrimEnd('/');
            if (full.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return full.Substring(root.Length + 1);
            return full;
        }

        // ───────────── エクスプローラーで開く（待たない） ─────────────

        /// <summary>
        /// フォルダーをエクスプローラーで開く。
        /// EditorUtility.RevealInFinder はエクスプローラーが閉じるまで Unity を止めてしまう（「Hold on」が出る）ので使わない。
        /// </summary>
        public static void OpenFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Path.GetFullPath(folder) + "\"") { UseShellExecute = false });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Avatar Hub] フォルダーを開けませんでした: " + e.Message);
            }
        }

        /// <summary>ファイルを選択した状態でエクスプローラーを開く（待たない）。</summary>
        public static void RevealFile(string file)
        {
            if (string.IsNullOrEmpty(file)) return;
            try
            {
                if (!File.Exists(file))
                {
                    OpenFolder(Path.GetDirectoryName(file));
                    return;
                }
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + Path.GetFullPath(file) + "\"") { UseShellExecute = false });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Avatar Hub] フォルダーを開けませんでした: " + e.Message);
            }
        }

        // ───────────── Undo 用のスナップショット ─────────────

        /// <summary>Undo で追いかける JSON（プリセット・プロジェクト記録・プロファイル・設定）の中身を全部読む。</summary>
        public static Dictionary<string, string> SnapshotTracked()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Dir(string dir, string ext)
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir, "*" + ext)) map[f] = File.ReadAllText(f, Encoding.UTF8);
            }
            Dir(PresetsDir, PresetExt);
            Dir(ProjectsDir, ProjectExt);
            Dir(ProfilesDir, ProfileExt);
            if (File.Exists(SettingsFile)) map[SettingsFile] = File.ReadAllText(SettingsFile, Encoding.UTF8);
            return map;
        }

        // ───────────── 旧バージョン（Asset Organizer）からの移行 ─────────────

        static bool migrated;

        /// <summary>旧保存場所のデータを新しい保存場所へコピーする（元のファイルは消さない）。</summary>
        public static void MigrateLegacy()
        {
            if (migrated) return;
            migrated = true;
            try
            {
                // 旧：%AppData%\V.inc\AssetOrganizer\Presets
                var oldPresets = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "V.inc", "AssetOrganizer", "Presets");
                CopyMissing(oldPresets, PresetsDir, PresetExt, "AssetOrganizer.Preset", "AvatarHub.Preset");

                // 旧：<Unityプロジェクト>\AssetOrganizer\{Projects, Profiles, state.json}
                var oldData = Path.Combine(ProjectRoot, "AssetOrganizer");
                var marker = Path.Combine(oldData, ".migrated-to-avatar-hub");
                if (Directory.Exists(oldData) && !File.Exists(marker))
                {
                    CopyMissing(Path.Combine(oldData, "Projects"), ProjectsDir, ProjectExt, "AssetOrganizer.Project", "AvatarHub.Project");
                    CopyMissing(Path.Combine(oldData, "Profiles"), ProfilesDir, ProfileExt, "AssetOrganizer.Profile", "AvatarHub.Profile");
                    var oldState = Path.Combine(oldData, "state.json");
                    if (File.Exists(oldState)) MigrateState(oldState);
                    File.WriteAllText(marker, "このフォルダーのデータは " + DataDir + " に移しました。", Utf8NoBom);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Avatar Hub] 旧データの移行に失敗しました: " + e.Message);
            }
        }

        static void CopyMissing(string from, string to, string ext, string oldKind, string newKind)
        {
            if (!Directory.Exists(from)) return;
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from, "*" + ext))
            {
                var dest = Path.Combine(to, Path.GetFileName(f));
                if (File.Exists(dest)) continue;
                var text = File.ReadAllText(f, Encoding.UTF8).Replace("\"" + oldKind + "\"", "\"" + newKind + "\"");
                File.WriteAllText(dest, text, Utf8NoBom);
            }
        }

        static void MigrateState(string oldState)
        {
            var legacy = Read<LegacyState>(oldState);
            if (legacy == null) return;
            var settings = File.Exists(SettingsFile) ? Read<ViahSettings>(SettingsFile) : null;
            bool newSettings = settings == null;
            settings ??= new ViahSettings();
            var history = File.Exists(HistoryFile) ? Read<ExportHistory>(HistoryFile) : null;
            history ??= new ExportHistory();
            if (newSettings)
            {
                settings.debugLabel = legacy.debugLabel;
                settings.defaultOutputFolder = legacy.defaultOutputFolder;
                settings.seqUseVersion = legacy.seqUseVersion;
                settings.seqUseDate = legacy.seqUseDate;
                settings.seqUseTime = legacy.seqUseTime;
                settings.seqUseDebug = legacy.seqUseDebug;
            }
            foreach (var h in legacy.history ?? new List<LegacyExportRecord>())
            {
                if (string.IsNullOrEmpty(h.profileId)) continue;
                if (settings.Prefs(h.profileId, false) == null)
                {
                    var p = settings.Prefs(h.profileId, true);
                    p.useVersion = h.useVersion;
                    p.useDate = h.useDate;
                    p.useTime = h.useTime;
                    p.useDebug = h.useDebug;
                }
                if (history.Get(h.profileId, false) == null && !string.IsNullOrEmpty(h.lastExportAt))
                {
                    var e = history.Get(h.profileId, true);
                    e.lastVersion = h.lastVersion;
                    e.lastExportAt = h.lastExportAt;
                    e.lastFile = h.lastFile;
                }
            }
            settings.Sanitize();
            Write(SettingsFile, settings);
            Write(HistoryFile, history);
        }
    }
}

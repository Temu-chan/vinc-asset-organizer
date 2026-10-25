using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace VInc.AssetOrganizer
{
    /// <summary>保存場所と JSON の読み書き。</summary>
    public static class AOStorage
    {
        public const string PresetExt = ".preset.json";
        public const string ProjectExt = ".project.json";
        public const string ProfileExt = ".profile.json";

        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>プロジェクト記録・プロファイルの保存先（Assets の外なのでエクスポートに紛れない）。</summary>
        public static string DataDir => Path.Combine(ProjectRoot, "AssetOrganizer");
        public static string ProjectsDir => Path.Combine(DataDir, "Projects");
        public static string ProfilesDir => Path.Combine(DataDir, "Profiles");
        public static string StateFile => Path.Combine(DataDir, "state.json");

        /// <summary>フォルダー構成プリセットは PC 全体で共通。</summary>
        public static string PresetsDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "V.inc", "AssetOrganizer", "Presets");

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
                Debug.LogWarning($"[Asset Organizer] 読み込めませんでした: {path}\n{e.Message}");
                return null;
            }
        }

        public static void Write(string path, object obj)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(obj, true), Utf8NoBom);
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
    }
}

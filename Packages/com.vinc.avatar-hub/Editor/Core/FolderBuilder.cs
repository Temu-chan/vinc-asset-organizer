using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace VInc.AvatarHub
{
    /// <summary>プリセットからフォルダーを作る。</summary>
    public static class FolderBuilder
    {
        static readonly char[] InvalidChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

        static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>フォルダー名として使えなければ理由を返す。</summary>
        public static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "名前を入力してください";
            if (name.IndexOfAny(InvalidChars) >= 0) return "使えない文字が入っています（\\ / : * ? \" < > |）";
            if (name != name.Trim()) return "名前の最初と最後に空白は使えません";
            if (name.EndsWith(".")) return "名前の最後にピリオドは使えません";
            if (Reserved.Contains(name)) return "Windows で予約されている名前のため使えません";
            return null;
        }

        /// <summary>使えるが注意したほうがいい名前なら理由を返す。</summary>
        public static string WarnName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.Any(c => c > 0x7E)) return "全角文字を含む名前は、環境によって文字化けすることがあります（英数字がおすすめです）";
            if (name.Contains(" ")) return "空白を含む名前は、スクリプトやツールによっては扱いにくいことがあります";
            return null;
        }

        /// <summary>番号プレフィックスを反映したフォルダー名。番号はプリセット内の並び順で決まる。</summary>
        public static string FolderName(PresetNode n, List<PresetNode> all, bool numberPrefix)
        {
            if (!numberPrefix) return n.name;
            int idx = 0;
            foreach (var x in all)
            {
                if (x.parentId != n.parentId) continue;
                idx++;
                if (x == n || x.id == n.id) break;
            }
            return $"{idx:00}_{n.name}";
        }

        /// <summary>ルートからの相対パス（例: "Animations/Controllers"）。</summary>
        public static string RelativePath(PresetNode n, List<PresetNode> all, bool numberPrefix)
        {
            var parts = new List<string>();
            var cur = n;
            int guard = 0;
            while (cur != null && guard++ < 64)
            {
                parts.Insert(0, FolderName(cur, all, numberPrefix));
                var pid = cur.parentId;
                cur = string.IsNullOrEmpty(pid) ? null : all.Find(x => x.id == pid);
            }
            return string.Join("/", parts);
        }

        /// <summary>ノードの祖先 ID（近い順）。</summary>
        public static IEnumerable<string> AncestorIds(PresetNode n, List<PresetNode> all)
        {
            var pid = n.parentId;
            int guard = 0;
            while (!string.IsNullOrEmpty(pid) && guard++ < 64)
            {
                yield return pid;
                var p = all.Find(x => x.id == pid);
                pid = p?.parentId;
            }
        }

        /// <summary>ルートと指定ノードのフォルダーを作る。作った数を返す。</summary>
        public static int Create(string rootPath, List<PresetNode> all, IEnumerable<string> nodeIds, bool numberPrefix)
        {
            int created = EnsureFolder(rootPath);
            var ids = new HashSet<string>(nodeIds);
            foreach (var n in all)
            {
                if (!ids.Contains(n.id)) continue;
                created += EnsureFolder(rootPath + "/" + RelativePath(n, all, numberPrefix));
            }
            return created;
        }

        /// <summary>フォルダーが無ければ親から順に作る。作った数を返す。</summary>
        public static int EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(path) || path == "Assets" || AssetDatabase.IsValidFolder(path)) return 0;
            int slash = path.LastIndexOf('/');
            if (slash <= 0) return 0;
            var parent = path.Substring(0, slash);
            var name = path.Substring(slash + 1);
            int count = EnsureFolder(parent);
            var guid = AssetDatabase.CreateFolder(parent, name);
            if (string.IsNullOrEmpty(guid)) return count;
            UndoHub.NoteCreatedFolder(path);
            return count + 1;
        }
    }
}

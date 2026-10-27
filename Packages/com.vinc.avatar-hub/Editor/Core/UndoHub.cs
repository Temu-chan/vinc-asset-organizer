using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VInc.AvatarHub
{
    /// <summary>画面の入力途中の状態を Undo できるようにするためのインターフェース。</summary>
    public interface IUndoState
    {
        /// <summary>画面インスタンスごとに一意。</summary>
        string UndoKey { get; }
        /// <summary>いまの入力状態を文字列にする（null なら Undo しない画面）。</summary>
        string CaptureState();
        /// <summary>文字列から入力状態を戻して画面を描き直す。</summary>
        void RestoreState(string state);
        /// <summary>最後に Undo に積んだ状態。</summary>
        string Committed { get; set; }
    }

    /// <summary>
    /// Unity の Undo（Ctrl+Z / Ctrl+Y・Edit メニュー）にツールの操作を載せる。
    /// ・データの変更（JSON の保存・削除・読み込み、フォルダーの作成）… ファイルの中身を前後で記録して戻す
    /// ・画面の入力途中の変更（名前の入力、チェック、並べ替えなど）… 画面の状態を前後で記録して戻す
    /// Unity の Undo には「目印」の ScriptableObject の番号だけを積み、実際の中身はログに持つ。
    /// </summary>
    public static class UndoHub
    {
        class Marker : ScriptableObject
        {
            public int revision;
        }

        [Serializable]
        class FileChange
        {
            public string path;
            public bool beforeExists;
            public string before;
            public bool afterExists;
            public string after;
        }

        [Serializable]
        class Op
        {
            public int revision;
            public string label;
            public string coalesceKey;
            public double time;
            public List<FileChange> files = new List<FileChange>();
            public List<string> createdFolders = new List<string>();
            public string pageKey;
            public string uiBefore;
            public string uiAfter;
        }

        class Log : ScriptableObject
        {
            public int counter;
            public int dataApplied;
            public int uiApplied;
            public List<Op> dataOps = new List<Op>();
            public List<Op> uiOps = new List<Op>();
        }

        const double CoalesceSeconds = 2.5;

        static Marker dataMarker;
        static Marker uiMarker;
        static Log log;
        static int depth;
        static List<string> collectingFolders;
        static bool applying;

        /// <summary>いま表示している画面（入力途中の Undo の対象）。</summary>
        public static IUndoState CurrentPage;

        /// <summary>データを戻した／やり直したあとに呼ばれる（画面の描き直し用）。label, isUndo</summary>
        public static event Action<string, bool> DataRestored;
        /// <summary>画面の状態を戻した／やり直したあとに呼ばれる。label, isUndo</summary>
        public static event Action<string, bool> UiRestored;
        /// <summary>空でないため消さなかったフォルダーがあったとき。</summary>
        public static event Action<List<string>> FoldersKept;

        public static bool IsApplying => applying;

        [InitializeOnLoadMethod]
        static void Init()
        {
            Undo.undoRedoPerformed -= Sync;
            Undo.undoRedoPerformed += Sync;
        }

        static void Ensure()
        {
            if (log == null) log = Resources.FindObjectsOfTypeAll<Log>().FirstOrDefault();
            if (log == null)
            {
                log = ScriptableObject.CreateInstance<Log>();
                log.hideFlags = HideFlags.HideAndDontSave;
            }
            if (dataMarker == null || uiMarker == null)
            {
                var markers = Resources.FindObjectsOfTypeAll<Marker>();
                dataMarker = markers.FirstOrDefault(m => m.name == "VIAH Data");
                uiMarker = markers.FirstOrDefault(m => m.name == "VIAH UI");
                if (dataMarker == null) dataMarker = CreateMarker("VIAH Data", log.dataApplied);
                if (uiMarker == null) uiMarker = CreateMarker("VIAH UI", log.uiApplied);
            }
        }

        static Marker CreateMarker(string name, int revision)
        {
            var m = ScriptableObject.CreateInstance<Marker>();
            m.name = name;
            m.hideFlags = HideFlags.HideAndDontSave;
            m.revision = revision;
            return m;
        }

        // ───────────── データの変更 ─────────────

        /// <summary>
        /// JSON の保存・削除やフォルダーの作成をまとめて 1 回の Undo にする。
        /// coalesceKey が同じ変更が続いたときは 1 つにまとめる（文字入力など）。
        /// </summary>
        public static void Data(string label, Action mutate, string coalesceKey = null)
        {
            if (applying || depth > 0)
            {
                mutate();
                return;
            }
            depth++;
            var before = ViahStorage.SnapshotTracked();
            collectingFolders = new List<string>();
            List<string> folders;
            try
            {
                mutate();
            }
            finally
            {
                depth--;
                folders = collectingFolders;
                collectingFolders = null;
            }
            var after = ViahStorage.SnapshotTracked();
            var changes = Diff(before, after);
            if (changes.Count == 0 && folders.Count == 0) return;

            Ensure();
            var now = EditorApplication.timeSinceStartup;
            var last = log.dataOps.LastOrDefault();
            if (coalesceKey != null && last != null && folders.Count == 0 && last.createdFolders.Count == 0 &&
                last.coalesceKey == coalesceKey && last.revision == dataMarker.revision && now - last.time < CoalesceSeconds)
            {
                Merge(last, changes);
                last.time = now;
                return;
            }

            log.dataOps.RemoveAll(o => o.revision > dataMarker.revision);
            var op = new Op { revision = ++log.counter, label = label, coalesceKey = coalesceKey, time = now, files = changes, createdFolders = folders };
            Undo.RecordObject(dataMarker, "VIAH: " + label);
            dataMarker.revision = op.revision;
            log.dataOps.Add(op);
            log.dataApplied = op.revision;
            Undo.SetCurrentGroupName("VIAH: " + label);
        }

        /// <summary>FolderBuilder がフォルダーを作ったときに呼ぶ（Undo で消すため）。</summary>
        public static void NoteCreatedFolder(string assetPath)
        {
            collectingFolders?.Add(assetPath);
        }

        static List<FileChange> Diff(Dictionary<string, string> before, Dictionary<string, string> after)
        {
            var list = new List<FileChange>();
            foreach (var path in before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase))
            {
                before.TryGetValue(path, out var b);
                after.TryGetValue(path, out var a);
                if (b == a) continue;
                list.Add(new FileChange { path = path, beforeExists = b != null, before = b ?? "", afterExists = a != null, after = a ?? "" });
            }
            return list;
        }

        static void Merge(Op last, List<FileChange> changes)
        {
            foreach (var c in changes)
            {
                var existing = last.files.Find(f => string.Equals(f.path, c.path, StringComparison.OrdinalIgnoreCase));
                if (existing == null) last.files.Add(c);
                else
                {
                    existing.afterExists = c.afterExists;
                    existing.after = c.after;
                }
            }
        }

        // ───────────── 画面の入力途中の変更 ─────────────

        /// <summary>
        /// 画面の状態が変わったあとに呼ぶ。前回 Undo に積んだ状態との差を 1 回の Undo にする。
        /// </summary>
        public static void Commit(IUndoState page, string label, string coalesceKey = null)
        {
            if (applying || page == null) return;
            var after = page.CaptureState();
            if (after == null) return;
            var before = page.Committed;
            page.Committed = after;
            if (before == null || before == after) return;

            Ensure();
            var now = EditorApplication.timeSinceStartup;
            var last = log.uiOps.LastOrDefault();
            if (coalesceKey != null && last != null && last.coalesceKey == coalesceKey && last.pageKey == page.UndoKey &&
                last.revision == uiMarker.revision && now - last.time < CoalesceSeconds)
            {
                last.uiAfter = after;
                last.time = now;
                return;
            }

            log.uiOps.RemoveAll(o => o.revision > uiMarker.revision);
            var op = new Op { revision = ++log.counter, label = label, coalesceKey = coalesceKey, time = now, pageKey = page.UndoKey, uiBefore = before, uiAfter = after };
            Undo.RecordObject(uiMarker, "VIAH: " + label);
            uiMarker.revision = op.revision;
            log.uiOps.Add(op);
            log.uiApplied = op.revision;
            Undo.SetCurrentGroupName("VIAH: " + label);
        }

        /// <summary>画面を移ったとき：前の画面の入力途中の Undo は意味がなくなるので消す。</summary>
        public static void ClearUi()
        {
            Ensure();
            Undo.ClearUndo(uiMarker);
            log.uiOps.Clear();
            log.uiApplied = uiMarker.revision;
        }

        // ───────────── Undo / Redo の実行 ─────────────

        static void Sync()
        {
            if (applying) return;
            Ensure();
            SyncKind(dataMarker.revision, ref log.dataApplied, log.dataOps, ApplyData);
            SyncKind(uiMarker.revision, ref log.uiApplied, log.uiOps, ApplyUi);
        }

        static void SyncKind(int target, ref int applied, List<Op> ops, Action<Op, bool> apply)
        {
            if (target == applied) return;
            var current = applied;
            applied = target;
            if (target < current)
            {
                foreach (var op in ops.Where(o => o.revision > target && o.revision <= current).OrderByDescending(o => o.revision).ToList())
                    apply(op, true);
            }
            else
            {
                foreach (var op in ops.Where(o => o.revision > current && o.revision <= target).OrderBy(o => o.revision).ToList())
                    apply(op, false);
            }
        }

        static void ApplyData(Op op, bool undo)
        {
            applying = true;
            var kept = new List<string>();
            try
            {
                foreach (var f in op.files)
                {
                    bool exists = undo ? f.beforeExists : f.afterExists;
                    var text = undo ? f.before : f.after;
                    if (exists) ViahStorage.WriteText(f.path, text);
                    else if (File.Exists(f.path)) File.Delete(f.path);
                }
                if (op.createdFolders.Count > 0)
                {
                    if (undo)
                    {
                        // 作ったフォルダーを深い順に、空のときだけ消す（中にファイルを置いていたら残す）
                        foreach (var folder in op.createdFolders.OrderByDescending(p => p.Length))
                        {
                            if (!AssetDatabase.IsValidFolder(folder)) continue;
                            if (AssetDatabase.FindAssets("", new[] { folder }).Length > 0) kept.Add(folder);
                            else AssetDatabase.DeleteAsset(folder);
                        }
                    }
                    else
                    {
                        foreach (var folder in op.createdFolders.OrderBy(p => p.Length)) FolderBuilder.EnsureFolder(folder);
                    }
                }
                ViahDatabase.Reload();
            }
            finally
            {
                applying = false;
            }
            if (kept.Count > 0) FoldersKept?.Invoke(kept);
            DataRestored?.Invoke(op.label, undo);
        }

        static void ApplyUi(Op op, bool undo)
        {
            var page = CurrentPage;
            if (page == null || page.UndoKey != op.pageKey) return;
            applying = true;
            try
            {
                var state = undo ? op.uiBefore : op.uiAfter;
                page.RestoreState(state);
                page.Committed = state;
            }
            finally
            {
                applying = false;
            }
            UiRestored?.Invoke(op.label, undo);
        }
    }
}

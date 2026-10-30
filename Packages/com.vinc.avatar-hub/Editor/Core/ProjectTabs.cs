using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VInc.AvatarHub
{
    /// <summary>
    /// Project ウィンドウ（タブ）を指定の場所へ切り替える。Unity 内部の ProjectBrowser をリフレクションで使う。
    /// 最後に使ったロックなしのタブを切り替え、ロック中のタブは動かさない（全部ロック中なら新しいタブを開く）。
    /// Selection は変えないので、Inspector の表示もそのまま。
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectTabs
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly Type BrowserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
        /// <summary>フォーカスされた Project タブ（新しい順）。</summary>
        static readonly List<EditorWindow> recent = new List<EditorWindow>();
        static EditorWindow lastFocused;

        static ProjectTabs()
        {
            EditorApplication.update += Track;
        }

        /// <summary>どの Project タブを最後に使ったかを覚える（フォーカスが変わったときだけ見る）。</summary>
        static void Track()
        {
            var w = EditorWindow.focusedWindow;
            if (w == lastFocused) return;
            lastFocused = w;
            if (w == null || w.GetType() != BrowserType) return;
            recent.Remove(w);
            recent.Insert(0, w);
        }

        public static List<EditorWindow> All() =>
            BrowserType == null ? new List<EditorWindow>() : Resources.FindObjectsOfTypeAll(BrowserType).OfType<EditorWindow>().Where(w => w != null).ToList();

        public static bool IsLocked(EditorWindow w)
        {
            var p = BrowserType.GetProperty("isLocked", Any);
            if (p != null) return (bool)p.GetValue(w);
            var f = BrowserType.GetField("m_IsLocked", Any);
            return f != null && (bool)f.GetValue(w);
        }

        /// <summary>切り替えるタブ：最後に使ったロックなしのタブ。無ければ新しく開く。</summary>
        public static EditorWindow Target(out bool created)
        {
            created = false;
            var all = All();
            recent.RemoveAll(w => w == null || !all.Contains(w));
            // このツールが読み込まれる前に使っていたタブは、Unity が覚えている「最後に操作したタブ」で補う
            var last = BrowserType?.GetField("s_LastInteractedProjectBrowser", Any)?.GetValue(null) as EditorWindow;
            var order = recent.Concat(last != null ? new[] { last } : Array.Empty<EditorWindow>()).Concat(all).Distinct();
            var target = order.FirstOrDefault(w => w != null && !IsLocked(w));
            if (target != null) return target;
            created = true;
            return OpenNew();
        }

        /// <summary>今ある Project タブの隣に、新しい Project タブを開く。</summary>
        public static EditorWindow OpenNew()
        {
            var create = typeof(EditorWindow).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "CreateWindow" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type[]));
            if (create != null) return (EditorWindow)create.MakeGenericMethod(BrowserType).Invoke(null, new object[] { new[] { BrowserType } });
            var w = (EditorWindow)ScriptableObject.CreateInstance(BrowserType);
            w.Show();
            return w;
        }

        static bool IsTwoColumns(EditorWindow w)
        {
            var m = BrowserType.GetMethod("IsTwoColumns", Any, null, Type.EmptyTypes, null);
            return m == null || (bool)m.Invoke(w, null);
        }

        static readonly MethodInfo MainAssetInstanceId = typeof(AssetDatabase).GetMethod("GetMainAssetInstanceID", Any, null, new[] { typeof(string) }, null);

        /// <summary>アセットの ID。内部の機能で読み込まずに取る（無ければ読み込んで取る）。</summary>
        static int InstanceIdOf(string path)
        {
            if (MainAssetInstanceId != null) return (int)MainAssetInstanceId.Invoke(null, new object[] { path });
            var o = AssetDatabase.LoadMainAssetAtPath(path);
            return o != null ? o.GetInstanceID() : 0;
        }

        static bool Call(EditorWindow w, string name, params object[] args)
        {
            var types = args.Select(a => a.GetType()).ToArray();
            var m = BrowserType.GetMethod(name, Any, null, types, null);
            if (m == null) return false;
            m.Invoke(w, args);
            return true;
        }

        /// <summary>
        /// Project タブをその場所へ切り替える。フォルダーなら中身を表示し、アセットならそのフォルダーを開いて光らせる。
        /// アセットは読み込まない（ID だけ使う）。飛べなかったときは理由を返す。
        /// </summary>
        public static string Jump(string path)
        {
            if (BrowserType == null) return "Project ウィンドウを操作できません（Unity のバージョンが違う可能性があります）";
            if (string.IsNullOrEmpty(path) || !AssetRef.Exists(path)) return "このプロジェクトにはありません";
            bool folder = AssetDatabase.IsValidFolder(path);
            int id = InstanceIdOf(path);
            var folderPath = folder ? path : Path.GetDirectoryName(path)?.Replace('\\', '/');
            int folderId = folder ? id : InstanceIdOf(folderPath);

            var w = Target(out _);
            w.Show();
            w.Focus();
            Navigate(w, folder, id, folderId, 0);
            return null;
        }

        /// <summary>
        /// 一度も表示されていないタブは中身ができていないので、先に作らせる。
        /// それでも切り替えられなければ、タブが描かれるのを待ってもう一度試す。
        /// </summary>
        static void Navigate(EditorWindow w, bool folder, int id, int folderId, int attempt)
        {
            if (w == null) return;
            try
            {
                Call(w, "Init");
                bool done;
                if (IsTwoColumns(w))
                {
                    done = Call(w, "ShowFolderContents", folderId, true);
                    if (!folder) done &= Call(w, "FrameObject", id, true);
                }
                else
                {
                    done = Call(w, "FrameObject", id, !folder);
                }
                if (!done)
                {
                    // 内部の仕組みが変わっていたら、選択して光らせる（Inspector の表示は変わってしまう）
                    Selection.activeInstanceID = id;
                    EditorGUIUtility.PingObject(id);
                }
                w.Repaint();
            }
            catch (TargetInvocationException) when (attempt < 10)
            {
                w.Repaint();
                EditorApplication.delayCall += () => Navigate(w, folder, id, folderId, attempt + 1);
            }
        }
    }
}

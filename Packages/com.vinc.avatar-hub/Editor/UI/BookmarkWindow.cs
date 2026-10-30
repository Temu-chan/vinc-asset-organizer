using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>
    /// ブックマークのタブ。Avatar Hub の⑤「ブックマークのタブを出す」で、Project タブの隣に出す。クリック 1 回でその場所へ飛ぶ。
    /// 登録・並べ替えなどの管理は⑤で行う（ここでもドロップで追加はできる）。
    /// </summary>
    public class BookmarkWindow : EditorWindow
    {
        VisualElement list;

        public static void Open()
        {
            var browser = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
            var w = browser != null ? GetWindow<BookmarkWindow>("ブックマーク", true, browser) : GetWindow<BookmarkWindow>("ブックマーク");
            w.titleContent = new GUIContent("ブックマーク", UIKit.Icon("Favorite"));
            w.minSize = new Vector2(150, 90);
            w.Show();
        }

        void CreateGUI()
        {
            var root = rootVisualElement;
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(ViahWindow.UssPath);
            if (uss != null) root.styleSheets.Add(uss);
            root.AddToClassList("viah-root");
            root.AddToClassList("viah-mini");
            ViahWindow.ApplyThemeTo(root);
            titleContent = new GUIContent("ブックマーク", UIKit.Icon("Favorite"));

            var head = UIKit.Row("viah-mini-head");
            head.Add(UIKit.Text("ブックマーク", "viah-bold", "viah-grow"));
            head.Add(UIKit.IconBtn("Toolbar Plus", "Project で選択中のフォルダーを追加", AddSelection, "small", "ghost"));
            head.Add(UIKit.IconBtn("SettingsIcon", "Avatar Hub で並べ替え・名前の変更・JSON の書き出しをする", () => ViahWindow.OpenAt("bookmarks"), "small", "ghost"));
            root.Add(head);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.style.flexGrow = 1;
            list = scroll.contentContainer;
            root.Add(scroll);

            // 小窓のどこに落としても追加できる
            root.RegisterCallback<DragUpdatedEvent>(_ => DragAndDrop.visualMode = DropZone.DraggedPaths().Count > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected);
            root.RegisterCallback<DragPerformEvent>(_ =>
            {
                var paths = DropZone.DraggedPaths();
                if (paths.Count == 0) return;
                DragAndDrop.AcceptDrag();
                BookmarkActions.Add(paths);
            });

            ViahDatabase.Changed -= Render;
            ViahDatabase.Changed += Render;
            Render();
        }

        void OnDisable()
        {
            ViahDatabase.Changed -= Render;
        }

        void AddSelection()
        {
            var paths = Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).Where(p => p.StartsWith("Assets", StringComparison.Ordinal)).ToList();
            if (paths.Count == 0) ShowNotification(new GUIContent("Project で選んでから押してください"));
            else if (BookmarkActions.Add(paths) == 0) ShowNotification(new GUIContent("すでに登録されています"));
        }

        void Render()
        {
            if (list == null) return;
            // 設定でテーマが変わったときにも合わせる
            ViahWindow.ApplyThemeTo(rootVisualElement);
            list.Clear();
            var items = BookmarkActions.Items;
            if (items.Count == 0)
            {
                list.Add(UIKit.Text("フォルダーをここにドロップすると登録できます。", "viah-faint", "viah-small", "viah-wrap", "viah-mini-empty"));
                return;
            }
            foreach (var b in items)
            {
                var bm = b;
                var path = BookmarkActions.PathOf(b);
                var row = UIKit.Row("viah-mini-item");
                UIKit.SetClass(row, "is-missing", path == null);
                row.Add(UIKit.Img(path != null ? UIKit.AssetIcon(path, false) : UIKit.Icon("console.warnicon.sml")));
                row.Add(UIKit.Text(b.DisplayLabel, "viah-ml6", "viah-ellipsis"));
                row.tooltip = path ?? $"{b.target.path}\nこの Unity プロジェクトにはありません";
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    var err = BookmarkActions.Jump(bm);
                    if (err != null) ShowNotification(new GUIContent(err));
                });
                list.Add(row);
            }
        }
    }
}

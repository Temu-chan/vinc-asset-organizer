using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AssetOrganizer.UI
{
    /// <summary>ウィンドウに表示する 1 画面。</summary>
    public abstract class Page
    {
        public AOWindow window;
        public readonly VisualElement root = UIKit.Div("ao-page-host");

        /// <summary>"home" / "projects" / "profiles" / "export"</summary>
        public abstract string Section { get; }

        public abstract void Build();

        /// <summary>保存などでデータが変わったとき。</summary>
        public virtual void OnDataChanged() { }

        public virtual void OnHide() { }
    }

    public class AOWindow : EditorWindow
    {
        const string UssPath = "Packages/com.vinc.asset-organizer/Editor/UI/AssetOrganizer.uss";
        const string PageKey = "VInc.AO.page";

        VisualElement content;
        VisualElement toastLayer;
        VisualElement overlay;
        VisualElement searchBox;
        VisualElement searchPop;
        AOTextField searchField;
        readonly Dictionary<string, VisualElement> nav = new Dictionary<string, VisualElement>();
        Page current;
        List<SearchHit> hits = new List<SearchHit>();
        int hitIndex;
        IVisualElementScheduledItem toastTimer;

        [MenuItem("V.inc/Asset Organizer", priority = 0)]
        public static AOWindow Open()
        {
            var w = GetWindow<AOWindow>();
            w.titleContent = new GUIContent("Asset Organizer", UIKit.Icon("Folder Icon"));
            w.minSize = new Vector2(900, 580);
            w.Show();
            return w;
        }

        void CreateGUI()
        {
            var root = rootVisualElement;
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null) root.styleSheets.Add(uss);
            root.AddToClassList("ao-root");
            if (!EditorGUIUtility.isProSkin) root.AddToClassList("light");

            root.Add(BuildHeader());
            var body = UIKit.Div("ao-body");
            body.Add(BuildSidebar());
            content = UIKit.Div("ao-content");
            body.Add(content);
            root.Add(body);

            searchPop = UIKit.Div("ao-search-pop");
            root.Add(searchPop);
            toastLayer = UIKit.Div("ao-toast-layer");
            toastLayer.pickingMode = PickingMode.Ignore;
            root.Add(toastLayer);

            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.ctrlKey || e.commandKey) && e.keyCode == KeyCode.F)
                {
                    UIKit.FocusField(searchField.field);
                    e.StopPropagation();
                }
            }, TrickleDown.TrickleDown);

            // 幅に応じてレイアウトを切り替える（ドッキングで狭くなったときに崩れないように）
            root.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var w = root.layout.width;
                if (float.IsNaN(w)) return;
                UIKit.SetClass(root, "narrow", w < 1080);
                UIKit.SetClass(root, "compact", w < 820);
            });

            AODatabase.Changed -= OnDataChanged;
            AODatabase.Changed += OnDataChanged;
            Navigate(SessionState.GetString(PageKey, "home"));
        }

        void OnDisable()
        {
            AODatabase.Changed -= OnDataChanged;
        }

        void OnDataChanged()
        {
            current?.OnDataChanged();
        }

        // ───────────── ヘッダー ─────────────

        VisualElement BuildHeader()
        {
            var header = UIKit.Div("ao-header");
            var brand = UIKit.Div("ao-brand");
            var logo = UIKit.Div("ao-logo");
            logo.Add(UIKit.Text("AO"));
            brand.Add(logo);
            var names = UIKit.Div("ao-brand-text");
            names.Add(UIKit.Text("Asset Organizer", "ao-brand-title"));
            names.Add(UIKit.Text("by V.inc", "ao-brand-sub"));
            brand.Add(names);
            UIKit.Clickable(brand, () => Navigate("home"));
            header.Add(brand);

            searchBox = UIKit.Div("ao-search");
            searchBox.Add(UIKit.Img("Search Icon"));
            searchField = new AOTextField("プロジェクト・プロファイルを検索（日本語のタグでもOK　例：くじら、てくすちゃ）　Ctrl+F");
            searchField.changed += OnSearch;
            searchField.field.RegisterCallback<FocusInEvent>(_ =>
            {
                searchBox.AddToClassList("is-focus");
                OnSearch(searchField.value);
            });
            searchField.field.RegisterCallback<FocusOutEvent>(_ =>
            {
                searchBox.RemoveFromClassList("is-focus");
                // クリックで結果を開けるように少し待ってから閉じる
                searchPop.schedule.Execute(() => searchPop.RemoveFromClassList("is-open")).ExecuteLater(180);
            });
            searchField.field.RegisterCallback<KeyDownEvent>(OnSearchKey, TrickleDown.TrickleDown);
            searchBox.Add(searchField);
            header.Add(searchBox);
            header.Add(UIKit.Spacer());
            var help = UIKit.Btn("TestNormal", "はじめての方へ", () => Navigate("home"), "ghost", "small", "ao-help-btn");
            help.tooltip = "使い方の流れをホームで確認できます";
            header.Add(help);
            return header;
        }

        void OnSearch(string q)
        {
            hits = SearchUtil.Search(q);
            hitIndex = 0;
            searchPop.Clear();
            if (string.IsNullOrWhiteSpace(q))
            {
                searchPop.RemoveFromClassList("is-open");
                return;
            }
            searchPop.style.left = searchBox.worldBound.x;
            searchPop.style.width = Mathf.Max(360, searchBox.worldBound.width);
            if (hits.Count == 0)
            {
                searchPop.Add(UIKit.Text($"「{q}」に一致するものはありません。\nプロジェクトやプロファイルにタグを付けておくと、日本語のうろ覚えでも見つかります。", "ao-search-empty", "ao-dim"));
            }
            for (int i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                var item = UIKit.Row("ao-search-item");
                bool isProject = hit.project != null;
                item.Add(UIKit.Img(isProject ? "Folder Icon" : "Prefab Icon"));
                var col = UIKit.Div("ao-grow", "ao-ml8");
                col.Add(UIKit.Text(hit.title, "ao-bold", "ao-ellipsis"));
                if (!string.IsNullOrEmpty(hit.detail)) col.Add(UIKit.Text(hit.detail, "ao-faint", "ao-small", "ao-ellipsis"));
                item.Add(col);
                item.Add(UIKit.Badge(isProject ? "プロジェクト" : "プロファイル", isProject ? "rule" : "auto"));
                item.RegisterCallback<PointerDownEvent>(e =>
                {
                    OpenHit(hit);
                    e.StopPropagation();
                });
                searchPop.Add(item);
            }
            HighlightHit();
            searchPop.AddToClassList("is-open");
        }

        void OnSearchKey(KeyDownEvent e)
        {
            if (!searchPop.ClassListContains("is-open")) return;
            switch (e.keyCode)
            {
                case KeyCode.DownArrow:
                    hitIndex = Mathf.Min(hits.Count - 1, hitIndex + 1);
                    HighlightHit();
                    e.StopPropagation();
                    break;
                case KeyCode.UpArrow:
                    hitIndex = Mathf.Max(0, hitIndex - 1);
                    HighlightHit();
                    e.StopPropagation();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (hits.Count > 0) OpenHit(hits[Mathf.Clamp(hitIndex, 0, hits.Count - 1)]);
                    e.StopPropagation();
                    break;
                case KeyCode.Escape:
                    searchPop.RemoveFromClassList("is-open");
                    e.StopPropagation();
                    break;
            }
        }

        void HighlightHit()
        {
            int i = 0;
            foreach (var child in searchPop.Children())
                UIKit.SetClass(child, "is-active", i++ == hitIndex && hits.Count > 0);
        }

        void OpenHit(SearchHit hit)
        {
            searchPop.RemoveFromClassList("is-open");
            searchField.value = "";
            if (hit.project != null)
            {
                Show(new ProjectsPage(hit.project.id));
                UIKit.Reveal(hit.project.RootPath);
            }
            else if (hit.profile != null)
            {
                Show(new ExportPage(hit.profile.id));
            }
        }

        // ───────────── サイドバー ─────────────

        VisualElement BuildSidebar()
        {
            var side = UIKit.Div("ao-sidebar");
            side.Add(NavItem("home", null, "Favorite", "ホーム"));
            side.Add(UIKit.Text("ワークフロー", "ao-nav-section"));
            side.Add(NavItem("projects", "1", null, "プロジェクト作成"));
            side.Add(NavItem("profiles", "2", null, "プロファイル登録"));
            side.Add(NavItem("export", "3", null, "エクスポート"));

            var footer = UIKit.Div("ao-sidebar-footer");
            var open = UIKit.Btn("FolderOpened Icon", "データの保存場所", () => EditorUtility.RevealInFinder(AOStorage.DataDir + "/"), "ghost", "small");
            open.tooltip = "プロジェクト記録・プロファイル（JSON）の保存場所を開きます\n" + AOStorage.DataDir;
            open.style.justifyContent = Justify.FlexStart;
            footer.Add(open);
            var reload = UIKit.Btn("Refresh", "再読み込み", () =>
            {
                AODatabase.Reload();
                Navigate(current?.Section ?? "home");
                Toast("JSON を読み込み直しました");
            }, "ghost", "small");
            reload.tooltip = "JSON を手で編集・追加したときに使います";
            reload.style.justifyContent = Justify.FlexStart;
            reload.style.marginTop = 2;
            footer.Add(reload);
            side.Add(footer);
            return side;
        }

        VisualElement NavItem(string key, string num, string icon, string label)
        {
            var item = UIKit.Row("ao-nav-item");
            if (num != null) item.Add(UIKit.Text(num, "ao-nav-num"));
            else item.Add(UIKit.Img(icon));
            item.Add(UIKit.Text(label, "ao-nav-label"));
            item.tooltip = label;
            item.RegisterCallback<ClickEvent>(_ => Navigate(key));
            nav[key] = item;
            return item;
        }

        // ───────────── ナビゲーション ─────────────

        public void Navigate(string section)
        {
            Page page;
            switch (section)
            {
                case "projects": page = new ProjectsPage(); break;
                case "profiles": page = new ProfilesPage(); break;
                case "export": page = new ExportPage(); break;
                default: page = new HomePage(); break;
            }
            Show(page);
        }

        public void Show(Page page)
        {
            CloseDialog();
            current?.OnHide();
            content.Clear();
            current = page;
            page.window = this;
            page.root.Clear();
            page.Build();
            content.Add(page.root);
            foreach (var kv in nav) UIKit.SetClass(kv.Value, "is-active", kv.Key == page.Section);
            SessionState.SetString(PageKey, page.Section);
        }

        public Page Current => current;

        // ───────────── トースト・ダイアログ ─────────────

        public void Toast(string message, string kind = "", string actionLabel = null, Action action = null)
        {
            toastLayer.Clear();
            toastTimer?.Pause();
            var t = UIKit.Row("ao-toast");
            if (!string.IsNullOrEmpty(kind)) t.AddToClassList(kind);
            t.Add(UIKit.Img(kind == "warn" || kind == "danger" ? "console.warnicon.sml" : "TestPassed"));
            t.Add(UIKit.Text(message, "ao-ml8", "ao-wrap"));
            if (actionLabel != null)
            {
                var b = UIKit.Btn(actionLabel, () => action?.Invoke(), "small", "ao-ml12");
                t.Add(b);
                toastLayer.pickingMode = PickingMode.Position;
                t.pickingMode = PickingMode.Position;
            }
            toastLayer.Add(t);
            t.schedule.Execute(() => t.AddToClassList("is-show")).ExecuteLater(10);
            toastTimer = t.schedule.Execute(() =>
            {
                t.RemoveFromClassList("is-show");
                t.schedule.Execute(() =>
                {
                    t.RemoveFromHierarchy();
                    toastLayer.pickingMode = PickingMode.Ignore;
                }).ExecuteLater(250);
            });
            toastTimer.ExecuteLater(actionLabel != null ? 7000 : 3500);
        }

        public struct DialogButton
        {
            public string label;
            public string cls;
            public Action action;
            public bool keepOpen;

            public DialogButton(string label, Action action, string cls = "", bool keepOpen = false)
            {
                this.label = label;
                this.action = action;
                this.cls = cls;
                this.keepOpen = keepOpen;
            }
        }

        /// <summary>ウィンドウ内のダイアログ。</summary>
        public VisualElement ShowDialog(string title, string text, VisualElement body, params DialogButton[] buttons)
        {
            CloseDialog();
            overlay = UIKit.Div("ao-overlay");
            var dlg = UIKit.Div("ao-dialog");
            dlg.Add(UIKit.Text(title, "ao-dialog-title"));
            if (!string.IsNullOrEmpty(text)) dlg.Add(UIKit.Text(text, "ao-dim", "ao-wrap"));
            if (body != null)
            {
                var scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.style.flexShrink = 1;
                scroll.style.marginTop = 8;
                scroll.Add(body);
                dlg.Add(scroll);
            }
            var actions = UIKit.Div("ao-dialog-actions");
            foreach (var b in buttons)
            {
                var btn = b;
                actions.Add(UIKit.Btn(btn.label, () =>
                {
                    if (!btn.keepOpen) CloseDialog();
                    btn.action?.Invoke();
                }, string.IsNullOrEmpty(btn.cls) ? Array.Empty<string>() : btn.cls.Split(' ')));
            }
            dlg.Add(actions);
            overlay.Add(dlg);
            overlay.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) CloseDialog();
            });
            rootVisualElement.Add(overlay);
            return overlay;
        }

        public void CloseDialog()
        {
            overlay?.RemoveFromHierarchy();
            overlay = null;
        }
    }
}

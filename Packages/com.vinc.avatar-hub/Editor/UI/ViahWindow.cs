using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>ウィンドウに表示する 1 画面。入力途中の状態を Undo できる画面は CaptureState / RestoreState を実装する。</summary>
    public abstract class Page : IUndoState
    {
        public ViahWindow window;
        public readonly VisualElement root = UIKit.Div("viah-page-host");
        readonly string undoKey = Guid.NewGuid().ToString("N");

        /// <summary>"home" / "projects" / "profiles" / "export" / "settings" など</summary>
        public abstract string Section { get; }

        public abstract void Build();

        /// <summary>保存などでデータが変わったとき。</summary>
        public virtual void OnDataChanged() { }

        public virtual void OnHide() { }

        /// <summary>データの Undo などで描き直すとき。</summary>
        public virtual void Rebuild()
        {
            root.Clear();
            Build();
        }

        public string UndoKey => undoKey;
        public string Committed { get; set; }
        public virtual string CaptureState() => null;
        public virtual void RestoreState(string state) { }

        /// <summary>入力途中の変更を Undo に積む。</summary>
        protected void Commit(string label, string coalesceKey = null) => UndoHub.Commit(this, label, coalesceKey);
    }

    /// <summary>サイドバーの「ワークフロー」。今後ここに増やしていく。</summary>
    public class Workflow
    {
        public string key;
        public string label;
        public string description;
        public Func<Page> create;
    }

    public class ViahWindow : EditorWindow
    {
        const string UssPath = "Packages/com.vinc.avatar-hub/Editor/UI/AvatarHub.uss";
        const string LogoPath = "Packages/com.vinc.avatar-hub/Editor/UI/Images/viah_logo.png";
        const string PageKey = "VInc.VIAH.page";

        public static readonly List<Workflow> Workflows = new List<Workflow>
        {
            new Workflow { key = "projects", label = "プロジェクト作成", description = "決まった構成のフォルダーを一括で作る", create = () => new ProjectsPage() },
            new Workflow { key = "profiles", label = "プロファイル登録", description = "unitypackage に入れる中身を登録する", create = () => new ProfilesPage() },
            new Workflow { key = "export", label = "エクスポート", description = "unitypackage を書き出す（単発・シーケンス）", create = () => new ExportPage() },
        };

        /// <summary>配色（アクセント）の一覧。key, 表示名, 見本の色</summary>
        public static readonly (string key, string label, string color)[] Accents =
        {
            ("blue", "青", "#4c8df6"),
            ("cyan", "水色", "#2bb3d9"),
            ("green", "緑", "#3cba6f"),
            ("orange", "オレンジ", "#f08a24"),
            ("red", "赤", "#e5534b"),
            ("purple", "紫", "#9b6cf2"),
            ("pink", "ピンク", "#e85d9e"),
        };

        static Texture2D logo;
        public static Texture2D Logo => logo != null ? logo : logo = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);

        VisualElement content;
        VisualElement sidebar;
        VisualElement toastLayer;
        VisualElement overlay;
        VisualElement searchBox;
        VisualElement searchPop;
        Button settingsBtn;
        ViahTextField searchField;
        readonly Dictionary<string, VisualElement> nav = new Dictionary<string, VisualElement>();
        Page current;
        List<SearchHit> hits = new List<SearchHit>();
        int hitIndex;
        IVisualElementScheduledItem toastTimer;

        [MenuItem("V.inc/Avatar Hub", priority = 0)]
        public static ViahWindow Open()
        {
            ViahWindow w;
            if (HasOpenInstances<ViahWindow>() || ViahDatabase.Settings.windowMode == "float")
            {
                w = GetWindow<ViahWindow>(false, "Avatar Hub", true);
            }
            else
            {
                // 既定は Unity のタブとして開く（独立したウィンドウは Unity の仕様で常に本体より前に出るため）
                w = GetWindow<ViahWindow>("Avatar Hub", true, typeof(SceneView));
            }
            w.titleContent = new GUIContent("Avatar Hub", Logo != null ? Logo : UIKit.Icon("Folder Icon"));
            w.minSize = new Vector2(560, 420);
            w.Show();
            return w;
        }

        void CreateGUI()
        {
            var root = rootVisualElement;
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null) root.styleSheets.Add(uss);
            root.AddToClassList("viah-root");
            ApplyTheme();
            titleContent = new GUIContent("Avatar Hub", Logo != null ? Logo : UIKit.Icon("Folder Icon"));

            root.Add(BuildHeader());
            var body = UIKit.Div("viah-body");
            sidebar = UIKit.Div("viah-sidebar");
            body.Add(sidebar);
            content = UIKit.Div("viah-content");
            body.Add(content);
            root.Add(body);
            RebuildSidebar();

            searchPop = UIKit.Div("viah-search-pop");
            root.Add(searchPop);
            toastLayer = UIKit.Div("viah-toast-layer");
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

            ViahDatabase.Changed -= OnDataChanged;
            ViahDatabase.Changed += OnDataChanged;
            UndoHub.DataRestored -= OnDataRestored;
            UndoHub.DataRestored += OnDataRestored;
            UndoHub.UiRestored -= OnUiRestored;
            UndoHub.UiRestored += OnUiRestored;
            UndoHub.FoldersKept -= OnFoldersKept;
            UndoHub.FoldersKept += OnFoldersKept;
            Navigate(SessionState.GetString(PageKey, "home"));
        }

        void OnDisable()
        {
            ViahDatabase.Changed -= OnDataChanged;
            UndoHub.DataRestored -= OnDataRestored;
            UndoHub.UiRestored -= OnUiRestored;
            UndoHub.FoldersKept -= OnFoldersKept;
            if (UndoHub.CurrentPage == current) UndoHub.CurrentPage = null;
        }

        void OnDataChanged()
        {
            current?.OnDataChanged();
        }

        void OnDataRestored(string label, bool undo)
        {
            if (current == null) return;
            ApplyTheme();
            RebuildSidebar();
            current.Rebuild();
            current.Committed = current.CaptureState();
            HighlightNav();
            Toast((undo ? "元に戻しました：" : "やり直しました：") + label);
        }

        void OnUiRestored(string label, bool undo)
        {
            Toast((undo ? "元に戻しました：" : "やり直しました：") + label);
        }

        void OnFoldersKept(List<string> folders)
        {
            Toast("中にファイルがあるため、次のフォルダーは消さずに残しました：" + string.Join("、", folders.Take(3)) + (folders.Count > 3 ? " ほか" : ""), "warn");
        }

        // ───────────── 配色 ─────────────

        static readonly string[] ThemeClasses = { "theme-dark", "theme-light", "theme-unity-dark", "theme-unity-light" };

        public void ApplyTheme()
        {
            var root = rootVisualElement;
            var s = ViahDatabase.Settings;
            foreach (var c in ThemeClasses) root.RemoveFromClassList(c);
            foreach (var a in Accents) root.RemoveFromClassList("accent-" + a.key);
            var theme = s.theme == "unity" ? (EditorGUIUtility.isProSkin ? "theme-unity-dark" : "theme-unity-light")
                : s.theme == "light" ? "theme-light" : "theme-dark";
            root.AddToClassList(theme);
            root.AddToClassList("accent-" + (Accents.Any(a => a.key == s.accent) ? s.accent : "blue"));
            UIKit.SetClass(root, "is-light", theme == "theme-light" || theme == "theme-unity-light");
        }

        // ───────────── ヘッダー ─────────────

        VisualElement BuildHeader()
        {
            var header = UIKit.Div("viah-header");
            var brand = UIKit.Div("viah-brand");
            if (Logo != null)
            {
                var img = new Image { image = Logo, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                img.AddToClassList("viah-logo-img");
                brand.Add(img);
            }
            var names = UIKit.Div("viah-brand-text");
            names.Add(UIKit.Text("V.inc Avatar Hub", "viah-brand-title"));
            names.Add(UIKit.Text("VIAH", "viah-brand-sub"));
            brand.Add(names);
            brand.tooltip = "ホームへ";
            UIKit.Clickable(brand, () => Navigate("home"));
            header.Add(brand);

            searchBox = UIKit.Div("viah-search");
            searchBox.Add(UIKit.Img("Search Icon"));
            searchField = new ViahTextField("プロジェクト・プロファイルを検索（日本語のタグでもOK　例：くじら、てくすちゃ）　Ctrl+F");
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
            settingsBtn = UIKit.Btn("SettingsIcon", "設定", () => Show(new SettingsPage()), "ghost", "small", "viah-settings-btn");
            settingsBtn.tooltip = "配色・表示するワークフロー・保存場所などの設定";
            header.Add(settingsBtn);
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
                searchPop.Add(UIKit.Text($"「{q}」に一致するものはありません。\nプロジェクトやプロファイルにタグを付けておくと、日本語のうろ覚えでも見つかります。", "viah-search-empty", "viah-dim"));
            }
            for (int i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                var item = UIKit.Row("viah-search-item");
                bool isProject = hit.project != null;
                item.Add(UIKit.Img(isProject ? "Folder Icon" : "Prefab Icon"));
                var col = UIKit.Div("viah-grow", "viah-ml8");
                col.Add(UIKit.Text(hit.title, "viah-bold", "viah-ellipsis"));
                if (!string.IsNullOrEmpty(hit.detail)) col.Add(UIKit.Text(hit.detail, "viah-faint", "viah-small", "viah-ellipsis"));
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

        /// <summary>設定で表示するワークフローが変わったときにも呼ぶ。</summary>
        public void RebuildSidebar()
        {
            if (sidebar == null) return;
            sidebar.Clear();
            nav.Clear();
            sidebar.Add(NavItem("home", null, "Favorite", "ホーム"));
            var hidden = ViahDatabase.Settings.hiddenWorkflows;
            var visible = Workflows.Where(w => !hidden.Contains(w.key)).ToList();
            if (visible.Count > 0) sidebar.Add(UIKit.Text("ワークフロー", "viah-nav-section"));
            for (int i = 0; i < visible.Count; i++)
                sidebar.Add(NavItem(visible[i].key, (i + 1).ToString(), null, visible[i].label));
            HighlightNav();
        }

        VisualElement NavItem(string key, string num, string icon, string label)
        {
            var item = UIKit.Row("viah-nav-item");
            if (num != null) item.Add(UIKit.Text(num, "viah-nav-num"));
            else item.Add(UIKit.Img(icon));
            item.Add(UIKit.Text(label, "viah-nav-label"));
            item.tooltip = label;
            item.RegisterCallback<ClickEvent>(_ => Navigate(key));
            nav[key] = item;
            return item;
        }

        void HighlightNav()
        {
            var section = current?.Section;
            foreach (var kv in nav) UIKit.SetClass(kv.Value, "is-active", kv.Key == section);
            if (settingsBtn != null) UIKit.SetClass(settingsBtn, "is-active", section == "settings");
        }

        // ───────────── ナビゲーション ─────────────

        public void Navigate(string section)
        {
            Page page;
            var wf = Workflows.Find(w => w.key == section);
            if (wf != null) page = wf.create();
            else if (section == "settings") page = new SettingsPage();
            else page = new HomePage();
            Show(page);
        }

        public void Show(Page page)
        {
            CloseDialog();
            current?.OnHide();
            // 前の画面の「入力途中の Undo」はもう意味がないので消す（保存したデータの Undo は残る）
            UndoHub.ClearUi();
            content.Clear();
            current = page;
            page.window = this;
            page.root.Clear();
            page.Build();
            content.Add(page.root);
            UndoHub.CurrentPage = page;
            page.Committed = page.CaptureState();
            HighlightNav();
            SessionState.SetString(PageKey, page.Section);
        }

        public Page Current => current;

        // ───────────── トースト・ダイアログ ─────────────

        public void Toast(string message, string kind = "", string actionLabel = null, Action action = null)
        {
            toastLayer.Clear();
            toastTimer?.Pause();
            var t = UIKit.Row("viah-toast");
            if (!string.IsNullOrEmpty(kind)) t.AddToClassList(kind);
            t.Add(UIKit.Img(kind == "warn" || kind == "danger" ? "console.warnicon.sml" : "TestPassed"));
            t.Add(UIKit.Text(message, "viah-ml8", "viah-wrap"));
            if (actionLabel != null)
            {
                var b = UIKit.Btn(actionLabel, () => action?.Invoke(), "small", "viah-ml12");
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
            overlay = UIKit.Div("viah-overlay");
            var dlg = UIKit.Div("viah-dialog");
            dlg.Add(UIKit.Text(title, "viah-dialog-title"));
            if (!string.IsNullOrEmpty(text)) dlg.Add(UIKit.Text(text, "viah-dim", "viah-wrap"));
            if (body != null)
            {
                var scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.style.flexShrink = 1;
                scroll.style.marginTop = 8;
                scroll.Add(body);
                dlg.Add(scroll);
            }
            var actions = UIKit.Div("viah-dialog-actions");
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

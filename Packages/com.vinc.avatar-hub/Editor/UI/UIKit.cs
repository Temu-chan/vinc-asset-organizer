using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>要素を作る小さなヘルパー。</summary>
    public static class UIKit
    {
        public static VisualElement Div(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) e.AddToClassList(c);
            return e;
        }

        public static VisualElement Row(params string[] classes)
        {
            var e = Div(classes);
            e.AddToClassList("viah-hrow");
            return e;
        }

        public static VisualElement Spacer()
        {
            var e = new VisualElement();
            e.style.flexGrow = 1;
            return e;
        }

        public static Label Text(string text, params string[] classes)
        {
            var l = new Label(text);
            // テーマの既定余白を外して、余白は USS のクラスだけで決める
            l.RemoveFromClassList("unity-label");
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        /// <summary>ページ全体の縦スクロール（横スクロールは出さない）。</summary>
        public static ScrollView PageScroll()
        {
            var s = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            s.AddToClassList("viah-scroll");
            s.contentContainer.AddToClassList("viah-scroll-content");
            return s;
        }

        public static Button Btn(string text, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = text };
            b.RemoveFromClassList("unity-button");
            b.AddToClassList("viah-btn");
            foreach (var c in classes) b.AddToClassList(c);
            return b;
        }

        /// <summary>アイコン付きボタン。</summary>
        public static Button Btn(string icon, string text, Action onClick, params string[] classes)
        {
            var b = Btn("", onClick, classes);
            var tex = Icon(icon);
            if (tex != null) b.Add(Img(tex));
            b.Add(Text(text));
            return b;
        }

        public static Button IconBtn(string icon, string tooltip, Action onClick, params string[] classes)
        {
            var b = Btn("", onClick, classes);
            b.AddToClassList("viah-icon-btn");
            b.tooltip = tooltip;
            var tex = Icon(icon);
            if (tex != null) b.Add(Img(tex));
            else b.text = "?";
            return b;
        }

        /// <summary>エディター内蔵アイコン。見つからなければ null（警告は出さない）。</summary>
        public static Texture2D Icon(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Texture2D t = null;
            if (EditorGUIUtility.isProSkin) t = EditorGUIUtility.FindTexture("d_" + name);
            if (t == null) t = EditorGUIUtility.FindTexture(name);
            return t;
        }

        public static Image Img(Texture tex, params string[] classes)
        {
            var i = new Image { image = tex, pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
            i.AddToClassList("viah-icon");
            foreach (var c in classes) i.AddToClassList(c);
            return i;
        }

        public static Image Img(string iconName, params string[] classes) => Img(Icon(iconName), classes);

        public static Label Badge(string text, string kind, string tooltip = null)
        {
            var b = Text(text, "viah-badge");
            if (!string.IsNullOrEmpty(kind)) b.AddToClassList(kind);
            if (tooltip != null) b.tooltip = tooltip;
            return b;
        }

        public static VisualElement Card(params string[] classes)
        {
            var c = Div("viah-card");
            foreach (var x in classes) c.AddToClassList(x);
            return c;
        }

        /// <summary>番号つきの見出し。</summary>
        public static VisualElement SectionHead(string num, string title, string sub = null)
        {
            var row = Row("viah-section-head");
            if (!string.IsNullOrEmpty(num)) row.Add(Text(num, "viah-section-num"));
            var col = Div();
            col.Add(Text(title, "viah-section-title"));
            if (!string.IsNullOrEmpty(sub)) col.Add(Text(sub, "viah-dim", "viah-small", "viah-wrap"));
            col.style.flexShrink = 1;
            row.Add(col);
            return row;
        }

        public static VisualElement KeyValue(string key, string value)
        {
            var r = Div("viah-kv");
            r.Add(Text(key, "viah-kv-key"));
            r.Add(Text(value, "viah-kv-value"));
            return r;
        }

        public static VisualElement Clickable(VisualElement e, Action onClick)
        {
            e.RegisterCallback<ClickEvent>(ev =>
            {
                onClick();
                ev.StopPropagation();
            });
            return e;
        }

        /// <summary>
        /// TextField にキー入力できる状態でフォーカスする。
        /// Unity 2022.3 では TextField.Focus() だと外側の要素にフォーカスが乗り、文字が入力されないため、内側のテキスト要素に当てる。
        /// </summary>
        public static void FocusField(TextField tf)
        {
            if (tf == null) return;
            var inner = tf.Q(className: "unity-text-element--inner-input-field-component");
            if (inner != null && inner.focusable) inner.Focus();
            else tf.Focus();
        }

        /// <summary>入力欄の文字をコードから書き換える。カーソルも末尾に合わせる（ずれたままだと次の入力で例外になる）。</summary>
        public static void SetText(TextField tf, string text)
        {
            tf.SetValueWithoutNotify(text);
            tf.SelectRange(text.Length, text.Length);
        }

        public static void SetClass(VisualElement e, string cls, bool on)
        {
            if (on) e.AddToClassList(cls);
            else e.RemoveFromClassList(cls);
        }

        public static void Show(VisualElement e, bool show) => e.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>
        /// ヒント。key を渡すと「×」で閉じたことを覚えておき、次から出さない。
        /// kind: "" / "warn" / "success" / "danger"。closable なら key が無くてもその場で閉じられる。
        /// </summary>
        public static VisualElement Hint(string key, string title, string body, string kind = "", bool closable = false)
        {
            if (!string.IsNullOrEmpty(key) && EditorPrefs.GetBool(HintPrefKey(key), false)) return new VisualElement();
            var box = Row("viah-hint");
            if (!string.IsNullOrEmpty(kind)) box.AddToClassList(kind);
            var iconName = kind == "warn" || kind == "danger" ? "console.warnicon.sml" : kind == "success" ? "TestPassed" : "console.infoicon.sml";
            var icon = Img(iconName);
            icon.style.marginTop = 1;
            box.Add(icon);
            var body2 = Div("viah-hint-body");
            if (!string.IsNullOrEmpty(title)) body2.Add(Text(title, "viah-hint-title"));
            if (!string.IsNullOrEmpty(body)) body2.Add(Text(body, "viah-hint-text"));
            box.Add(body2);
            if (!string.IsNullOrEmpty(key) || closable)
            {
                var close = Text("×", "viah-hint-close");
                close.tooltip = string.IsNullOrEmpty(key) ? "閉じる" : "このヒントを閉じる（設定の「閉じたヒントをすべて再表示」で戻せます）";
                Clickable(close, () =>
                {
                    if (!string.IsNullOrEmpty(key)) EditorPrefs.SetBool(HintPrefKey(key), true);
                    box.RemoveFromHierarchy();
                });
                box.Add(close);
            }
            return box;
        }

        static readonly string[] HintKeys =
        {
            "projects.intro", "preset.edit", "wizard.method", "wizard.drop", "wizard.select", "export.single", "export.seq", "home.search",
        };

        static string HintPrefKey(string key) => "VInc.VIAH.hint." + key;

        public static void ResetHints()
        {
            foreach (var k in HintKeys) EditorPrefs.DeleteKey(HintPrefKey(k));
        }

        /// <summary>Project ウィンドウでそのアセット/フォルダーを表示する。</summary>
        public static void Reveal(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (obj == null) return;
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
            if (!AssetDatabase.IsValidFolder(assetPath)) return;
            // 2 カラム表示のときは、そのフォルダーの中身を開く
            try
            {
                var type = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
                var browser = Resources.FindObjectsOfTypeAll(type).FirstOrDefault();
                var method = type?.GetMethod("ShowFolderContents", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null, new[] { typeof(int), typeof(bool) }, null);
                if (browser != null && method != null) method.Invoke(browser, new object[] { obj.GetInstanceID(), true });
            }
            catch
            {
                // 内部 API が変わっていても Ping はできているので無視
            }
        }

        /// <summary>Assets 内のフォルダーを選ぶダイアログ。キャンセル・範囲外なら null。</summary>
        public static string PickAssetsFolder(string title, string current)
        {
            var start = string.IsNullOrEmpty(current) ? Application.dataPath : System.IO.Path.GetFullPath(current);
            var abs = EditorUtility.OpenFolderPanel(title, start, "");
            if (string.IsNullOrEmpty(abs)) return null;
            var ap = ViahStorage.ToAssetPath(abs);
            if (ap == null)
            {
                EditorUtility.DisplayDialog("Avatar Hub", "Assets フォルダーの中から選んでください。", "OK");
                return null;
            }
            return ap;
        }

        public static string AssetName(string path) => string.IsNullOrEmpty(path) ? "" : System.IO.Path.GetFileName(path);

        /// <summary>
        /// アセットのアイコン。アセット自体は読み込まない：画像は取り込み時に作られた縮小版、
        /// それ以外は種類のアイコン（GetCachedIcon はマテリアルなどを読み込んでしまう）。
        /// thumbnail = false なら画像も種類のアイコンにする。
        /// </summary>
        public static Texture AssetIcon(string path, bool thumbnail = true)
        {
            if (AssetDatabase.IsValidFolder(path)) return Icon("Folder Icon");
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null) return Icon("DefaultAsset Icon");
            if (thumbnail && type == typeof(Texture2D)) return AssetDatabase.GetCachedIcon(path);
            if (type == typeof(GameObject))
                return Icon(path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ? "Prefab Icon" : "PrefabModel Icon");
            var icon = AssetPreview.GetMiniTypeThumbnail(type);
            return icon != null ? icon : Icon("DefaultAsset Icon");
        }
    }

    // ───────────────────────── テキスト入力（プレースホルダー付き） ─────────────────────────

    public class ViahTextField : VisualElement
    {
        public readonly TextField field;
        readonly Label placeholder;
        public event Action<string> changed;
        bool focused;

        public ViahTextField(string placeholderText, string value = "", bool big = false)
        {
            AddToClassList("viah-textfield");
            if (big) AddToClassList("big");
            field = new TextField { value = value ?? "" };
            field.AddToClassList("viah-input");
            if (big) field.AddToClassList("big");
            placeholder = new Label(placeholderText) { pickingMode = PickingMode.Ignore };
            placeholder.AddToClassList("viah-placeholder");
            Add(field);
            Add(placeholder);
            field.RegisterValueChangedCallback(e =>
            {
                Refresh();
                changed?.Invoke(e.newValue);
            });
            // IME 変換中は value が空のままなので、フォーカス中はプレースホルダーを隠す
            field.RegisterCallback<FocusInEvent>(_ => { focused = true; Refresh(); });
            field.RegisterCallback<FocusOutEvent>(_ => { focused = false; Refresh(); });
            Refresh();
        }

        public string value
        {
            get => field.value;
            set
            {
                UIKit.SetText(field, value ?? "");
                Refresh();
            }
        }

        public void SetError(bool on) => UIKit.SetClass(field, "is-error", on);

        /// <summary>幅に合わせて短くするときなどに使う。</summary>
        public string Placeholder
        {
            get => placeholder.text;
            set => placeholder.text = value;
        }

        void Refresh() => placeholder.style.display = string.IsNullOrEmpty(field.value) && !focused ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ───────────────────────── タグ入力 ─────────────────────────

    /// <summary>Enter・読点・カンマで確定。フォーカスが外れたときも確定する。</summary>
    public class TagField : VisualElement
    {
        readonly List<string> tags = new List<string>();
        readonly VisualElement inputWrap;
        readonly TextField input;
        readonly Label placeholder;
        bool focused;
        public event Action<List<string>> changed;

        public List<string> Tags => new List<string>(tags);

        public TagField(IEnumerable<string> initial, string placeholderText = "キーワードを入力して Enter（例：くじら、ふわふわ）")
        {
            AddToClassList("viah-tagfield");
            if (initial != null) tags.AddRange(initial.Where(t => !string.IsNullOrWhiteSpace(t)));

            inputWrap = UIKit.Div("viah-tag-input-wrap");
            input = new TextField();
            input.AddToClassList("viah-tag-input");
            placeholder = new Label(placeholderText) { pickingMode = PickingMode.Ignore };
            placeholder.AddToClassList("viah-placeholder");
            inputWrap.Add(input);
            inputWrap.Add(placeholder);

            input.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            input.RegisterValueChangedCallback(e =>
            {
                // 入力処理の途中で値を書き換えるとカーソル位置がずれて例外になるので、分割は次のフレームで行う
                if ((e.newValue ?? "").IndexOfAny(Separators) >= 0) schedule.Execute(SplitSeparators);
                RefreshPlaceholder();
            });
            input.RegisterCallback<FocusInEvent>(_ =>
            {
                focused = true;
                AddToClassList("is-focus");
                RefreshPlaceholder();
            });
            input.RegisterCallback<FocusOutEvent>(_ =>
            {
                focused = false;
                RemoveFromClassList("is-focus");
                Commit();
                RefreshPlaceholder();
            });
            RegisterCallback<ClickEvent>(e =>
            {
                // 入力欄そのものをクリックしたときは TextField に任せる（二重に Focus するとキー入力が届かなくなる）
                if (e.target is VisualElement t && input.Contains(t)) return;
                UIKit.FocusField(input);
            });
            Rebuild();
        }

        void OnKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                // Enter は文字の挿入を伴わないので、その場で確定してよい（カーソルは SetText で合わせる）
                e.StopPropagation();
                Commit();
            }
            else if (e.keyCode == KeyCode.Backspace && string.IsNullOrEmpty(input.value) && tags.Count > 0)
            {
                tags.RemoveAt(tags.Count - 1);
                Rebuild();
                changed?.Invoke(Tags);
                e.StopPropagation();
            }
        }

        void Commit()
        {
            var v = input.value;
            if (string.IsNullOrWhiteSpace(v)) return;
            UIKit.SetText(input, "");
            // 速く打って区切り文字の分割より先に確定した場合も、区切りごとにタグにする
            foreach (var part in v.Split(Separators)) AddTag(part);
            RefreshPlaceholder();
        }

        static readonly char[] Separators = { ',', '、', '，' };

        void SplitSeparators()
        {
            var v = input.value ?? "";
            if (v.IndexOfAny(Separators) < 0) return;
            var parts = v.Split(Separators);
            UIKit.SetText(input, parts[parts.Length - 1]);
            for (int i = 0; i < parts.Length - 1; i++) AddTag(parts[i]);
            RefreshPlaceholder();
        }

        void AddTag(string raw)
        {
            var t = (raw ?? "").Trim();
            if (t.Length == 0) return;
            // まったく同じ文字のタグだけを重複とする（「れーるがん」と「レールガン」は別々に登録できる）
            int same = tags.FindIndex(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));
            if (same >= 0)
            {
                Flash(same);
                return;
            }
            tags.Add(t);
            Rebuild();
            changed?.Invoke(Tags);
        }

        /// <summary>既にあるタグを一瞬光らせて「もう登録済み」と伝える。</summary>
        void Flash(int index)
        {
            if (index < 0 || index >= childCount || this[index] == inputWrap) return;
            var chip = this[index];
            chip.AddToClassList("is-flash");
            chip.tooltip = "このタグは登録済みです";
            chip.schedule.Execute(() => chip.RemoveFromClassList("is-flash")).ExecuteLater(700);
        }

        void Rebuild()
        {
            // 入力欄は外さずにタグだけ作り直す（外すとフォーカスが外れて続けて入力できなくなる）
            foreach (var child in Children().ToList())
                if (child != inputWrap) Remove(child);
            int index = 0;
            foreach (var t in tags)
            {
                var tag = t;
                var chip = UIKit.Row("viah-tag");
                chip.Add(UIKit.Text(tag));
                var x = UIKit.Text("×", "viah-tag-x");
                x.tooltip = "このタグを外す";
                UIKit.Clickable(x, () =>
                {
                    tags.Remove(tag);
                    Rebuild();
                    changed?.Invoke(Tags);
                });
                chip.Add(x);
                Insert(index++, chip);
            }
            if (inputWrap.parent != this) Add(inputWrap);
            RefreshPlaceholder();
        }

        void RefreshPlaceholder()
        {
            placeholder.style.display = string.IsNullOrEmpty(input.value) && tags.Count == 0 && !focused ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public static VisualElement ReadOnly(IEnumerable<string> tags, int max = 99)
        {
            var row = UIKit.Div("viah-tag-list");
            int i = 0;
            foreach (var t in tags)
            {
                if (i++ >= max)
                {
                    row.Add(UIKit.Text("…", "viah-faint"));
                    break;
                }
                var chip = UIKit.Row("viah-tag", "readonly");
                chip.Add(UIKit.Text(t));
                row.Add(chip);
            }
            return row;
        }
    }

    // ───────────────────────── チェックボックス ─────────────────────────

    public class CheckBox : VisualElement
    {
        public CheckBox()
        {
            AddToClassList("viah-check");
            var mark = new VisualElement { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("viah-check-mark");
            var dash = new VisualElement { pickingMode = PickingMode.Ignore };
            dash.AddToClassList("viah-check-dash");
            Add(mark);
            Add(dash);
        }

        /// <summary>0=なし 1=チェック 2=一部</summary>
        public void Set(int state, bool locked = false)
        {
            UIKit.SetClass(this, "is-on", state == 1);
            UIKit.SetClass(this, "is-partial", state == 2);
            UIKit.SetClass(this, "is-locked", locked);
        }
    }

    public class Arrow : VisualElement
    {
        public Arrow()
        {
            AddToClassList("viah-arrow");
            var g = new VisualElement { pickingMode = PickingMode.Ignore };
            g.AddToClassList("viah-arrow-glyph");
            Add(g);
        }

        public void Set(bool hasChildren, bool open)
        {
            UIKit.SetClass(this, "is-empty", !hasChildren);
            UIKit.SetClass(this, "is-open", open);
            pickingMode = hasChildren ? PickingMode.Position : PickingMode.Ignore;
        }
    }

    // ───────────────────────── スイッチ ─────────────────────────

    public class Switch : VisualElement
    {
        bool on;
        public event Action<bool> changed;

        public Switch(string label, bool value, string tooltipText = null)
        {
            AddToClassList("viah-switch");
            var track = UIKit.Div("viah-switch-track");
            track.Add(UIKit.Div("viah-switch-knob"));
            Add(track);
            if (!string.IsNullOrEmpty(label)) Add(UIKit.Text(label));
            if (tooltipText != null) tooltip = tooltipText;
            on = value;
            UIKit.SetClass(this, "is-on", on);
            RegisterCallback<ClickEvent>(e =>
            {
                Value = !on;
                changed?.Invoke(on);
                e.StopPropagation();
            });
        }

        public bool Value
        {
            get => on;
            set
            {
                on = value;
                UIKit.SetClass(this, "is-on", on);
            }
        }
    }

    // ───────────────────────── セグメント ─────────────────────────

    public class Segmented : VisualElement
    {
        readonly List<Label> items = new List<Label>();
        public int Index { get; private set; }

        public Segmented(string[] labels, int index, Action<int> onChange, bool small = false)
        {
            AddToClassList("viah-seg");
            if (small) AddToClassList("small");
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var l = UIKit.Text(labels[i], "viah-seg-item");
                UIKit.Clickable(l, () =>
                {
                    if (Index == idx) return;
                    Set(idx);
                    onChange?.Invoke(idx);
                });
                items.Add(l);
                Add(l);
            }
            Set(index);
        }

        public void Set(int index)
        {
            Index = index;
            for (int i = 0; i < items.Count; i++) UIKit.SetClass(items[i], "is-on", i == index);
        }
    }

    // ───────────────────────── ウィザードのステップ ─────────────────────────

    public class Stepper : VisualElement
    {
        readonly string[] labels;
        readonly List<VisualElement> items = new List<VisualElement>();
        public Func<int, bool> canGo;
        public event Action<int> clicked;

        public Stepper(params string[] labels)
        {
            this.labels = labels;
            AddToClassList("viah-stepper");
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                if (i > 0) Add(UIKit.Div("viah-stepper-line"));
                var item = UIKit.Row("viah-stepper-item");
                item.Add(UIKit.Text((i + 1).ToString(), "viah-stepper-dot"));
                item.Add(UIKit.Text(labels[i], "viah-stepper-label"));
                item.RegisterCallback<ClickEvent>(_ =>
                {
                    if (canGo == null || canGo(idx)) clicked?.Invoke(idx);
                });
                items.Add(item);
                Add(item);
            }
        }

        public void Set(int current, Func<int, bool> skipped = null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                bool skip = skipped != null && skipped(i);
                UIKit.SetClass(item, "is-active", i == current);
                UIKit.SetClass(item, "is-done", i < current && !skip);
                UIKit.SetClass(item, "is-disabled", skip || (canGo != null && !canGo(i) && i != current));
                var dot = (Label)item[0];
                dot.text = i < current && !skip ? "✓" : (i + 1).ToString();
            }
        }
    }

    // ───────────────────────── ドロップゾーン ─────────────────────────

    public class DropZone : VisualElement
    {
        public event Action<List<string>> dropped;

        public DropZone(string title, string text)
        {
            AddToClassList("viah-dropzone");
            var icon = UIKit.Img("Prefab Icon", "viah-dropzone-icon");
            Add(icon);
            Add(UIKit.Text(title, "viah-dropzone-title"));
            Add(UIKit.Text(text, "viah-dropzone-text"));

            RegisterCallback<DragEnterEvent>(_ => AddToClassList("is-hover"));
            RegisterCallback<DragLeaveEvent>(_ => RemoveFromClassList("is-hover"));
            RegisterCallback<DragExitedEvent>(_ => RemoveFromClassList("is-hover"));
            RegisterCallback<DragUpdatedEvent>(_ =>
            {
                DragAndDrop.visualMode = DraggedPaths().Count > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            });
            RegisterCallback<DragPerformEvent>(_ =>
            {
                RemoveFromClassList("is-hover");
                var paths = DraggedPaths();
                if (paths.Count == 0) return;
                DragAndDrop.AcceptDrag();
                dropped?.Invoke(paths);
            });
        }

        public static List<string> DraggedPaths()
        {
            var list = new List<string>();
            foreach (var p in DragAndDrop.paths ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(p) && p.StartsWith("Assets/", StringComparison.Ordinal) && !list.Contains(p)) list.Add(p);
            foreach (var o in DragAndDrop.objectReferences ?? Array.Empty<UnityEngine.Object>())
            {
                var p = AssetDatabase.GetAssetPath(o);
                if (!string.IsNullOrEmpty(p) && p.StartsWith("Assets/", StringComparison.Ordinal) && !list.Contains(p)) list.Add(p);
            }
            return list;
        }

        /// <summary>フォルダーのドロップを受け付けるようにする（パス表示などに）。</summary>
        public static void AcceptFolderDrop(VisualElement target, Action<string> onFolder)
        {
            target.RegisterCallback<DragUpdatedEvent>(_ =>
            {
                var ok = DraggedPaths().Any(AssetDatabase.IsValidFolder);
                DragAndDrop.visualMode = ok ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                UIKit.SetClass(target, "is-hover", ok);
            });
            target.RegisterCallback<DragLeaveEvent>(_ => target.RemoveFromClassList("is-hover"));
            target.RegisterCallback<DragPerformEvent>(_ =>
            {
                target.RemoveFromClassList("is-hover");
                var folder = DraggedPaths().FirstOrDefault(AssetDatabase.IsValidFolder);
                if (folder == null) return;
                DragAndDrop.AcceptDrag();
                onFolder(folder);
            });
        }
    }

    // ───────────────────────── バージョン入力 ─────────────────────────

    /// <summary>
    /// Ver [▲][▲][▲] / [1].[0].[3] / [▼][▼][▼]
    /// 各位の上下ボタン・ホイール・↑↓キー・直接入力（"1.2.3" の貼り付けも可）に対応。
    /// </summary>
    public class VersionStepper : VisualElement
    {
        PkgVersion v;
        readonly TextField[] digits = new TextField[3];
        public event Action<PkgVersion> changed;

        static readonly string[] DigitNames = { "1 の位（大きな更新）", "0.1 の位（機能追加）", "0.01 の位（修正）" };

        public VersionStepper(PkgVersion initial, bool compact = false)
        {
            AddToClassList("viah-ver");
            if (compact) AddToClassList("compact");
            Add(UIKit.Text("Ver", "viah-ver-prefix"));
            for (int i = 0; i < 3; i++)
            {
                int d = i;
                var col = UIKit.Div("viah-ver-col");
                var up = UIKit.Btn("▲", () => Step(d, +1), "viah-ver-arrow");
                up.tooltip = DigitNames[d] + "を上げる" + (d < 2 ? "（下の位は 0 に戻ります）" : "");
                var down = UIKit.Btn("▼", () => Step(d, -1), "viah-ver-arrow");
                down.tooltip = DigitNames[d] + "を下げる";
                var tf = new TextField();
                tf.AddToClassList("viah-ver-digit");
                tf.tooltip = DigitNames[d] + "：数字を直接入力できます。ホイールや ↑↓ キーでも変えられます";
                tf.RegisterValueChangedCallback(e => OnTyped(d, e.newValue));
                tf.RegisterCallback<WheelEvent>(e =>
                {
                    Step(d, e.delta.y < 0 ? +1 : -1);
                    e.StopPropagation();
                });
                tf.RegisterCallback<KeyDownEvent>(e => OnKey(d, e), TrickleDown.TrickleDown);
                col.Add(up);
                col.Add(tf);
                col.Add(down);
                Add(col);
                if (i < 2) Add(UIKit.Text(".", "viah-ver-dot"));
                digits[i] = tf;
            }
            v = initial;
            Refresh();
        }

        public PkgVersion Value
        {
            get => v;
            set
            {
                v = value;
                Refresh();
            }
        }

        void Step(int digit, int dir)
        {
            v = v.Step(digit, dir);
            Refresh();
            changed?.Invoke(v);
        }

        void OnKey(int d, KeyDownEvent e)
        {
            switch (e.keyCode)
            {
                case KeyCode.UpArrow: Step(d, +1); e.StopPropagation(); break;
                case KeyCode.DownArrow: Step(d, -1); e.StopPropagation(); break;
                case KeyCode.RightArrow when d < 2: UIKit.FocusField(digits[d + 1]); e.StopPropagation(); break;
                case KeyCode.LeftArrow when d > 0: UIKit.FocusField(digits[d - 1]); e.StopPropagation(); break;
            }
        }

        void OnTyped(int d, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (s.Length > 1 && PkgVersion.TryParse(s, out var parsed))
            {
                v = parsed;
                Refresh();
                changed?.Invoke(v);
                return;
            }
            char last = '\0';
            for (int i = s.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(s[i]) && s[i] <= '9') { last = s[i]; break; }
            }
            if (last == '\0')
            {
                Refresh();
                return;
            }
            v = v.WithDigit(d, last - '0');
            Refresh();
            changed?.Invoke(v);
            if (d < 2)
            {
                var next = digits[d + 1];
                schedule.Execute(() => UIKit.FocusField(next));
            }
        }

        void Refresh()
        {
            for (int i = 0; i < 3; i++) UIKit.SetText(digits[i], v[i].ToString());
        }
    }
}

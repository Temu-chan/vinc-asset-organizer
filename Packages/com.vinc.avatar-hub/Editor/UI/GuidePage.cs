using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>ガイド 1 ページ：「〜とは？」の説明、または「〜したい」の手順（注釈つきスクリーンショット）。</summary>
    public class GuidePage : Page
    {
        public const string ImageDir = "Packages/com.vinc.avatar-hub/Editor/Guide/Images/";
        /// <summary>ガイド画像の解像度（画面上の 1 ポイントあたりのピクセル数）。</summary>
        public const float ImageDensity = 1f;

        public override string Section => "home";

        readonly string topicId;
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public GuidePage(string topicId)
        {
            this.topicId = topicId;
        }

        public static Texture2D LoadImage(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (cache.TryGetValue(name, out var tex) && tex != null) return tex;
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ImageDir + name + ".png");
            cache[name] = tex;
            return tex;
        }

        public override void Build()
        {
            var t = GuideContent.Find(topicId);
            var scroll = UIKit.PageScroll();
            root.Add(scroll);
            var page = UIKit.Div("viah-guide");
            scroll.Add(page);

            var back = UIKit.Row("viah-back");
            back.Add(UIKit.Text("←  何をしたいですか？"));
            UIKit.Clickable(back, () => window.Navigate("home"));
            page.Add(back);

            if (t == null)
            {
                page.Add(UIKit.Text("このガイドは見つかりませんでした。", "viah-dim"));
                return;
            }

            var group = GuideContent.GroupOf(t);
            if (group != null)
            {
                var crumb = UIKit.Row("viah-crumb");
                crumb.Add(UIKit.Text(group.title, "viah-crumb-item"));
                page.Add(crumb);
            }
            page.Add(UIKit.Text(t.title, "viah-page-title"));
            page.Add(UIKit.Text(t.summary, "viah-page-sub"));

            // 手順ページでは、先に読むとよい「〜とは？」を案内する
            if (!t.concept && group?.conceptId != null)
            {
                var concept = GuideContent.Find(group.conceptId);
                var hint = UIKit.Row("viah-hint");
                hint.Add(UIKit.Img("console.infoicon.sml"));
                var body = UIKit.Div("viah-hint-body");
                body.Add(UIKit.Text($"はじめての方は、先に「{concept.title}」を読むとわかりやすくなります。", "viah-hint-text"));
                hint.Add(body);
                hint.Add(UIKit.Btn(concept.title, () => window.Show(new GuidePage(concept.id)), "small", "viah-ml8"));
                hint.style.alignItems = Align.Center;
                page.Add(hint);
            }

            foreach (var para in t.intro) page.Add(UIKit.Text(para, "viah-guide-para"));
            if (!string.IsNullOrEmpty(t.introImage)) page.Add(Shot(t.introImage, "viah-mb8"));

            for (int i = 0; i < t.steps.Count; i++)
            {
                var s = t.steps[i];
                var row = UIKit.Div("viah-guide-step");
                row.Add(UIKit.Text((i + 1).ToString(), "viah-guide-step-num"));
                var col = UIKit.Div("viah-guide-step-body");
                col.Add(UIKit.Text(s.text, "viah-guide-text"));
                // 同じ画像が続くときは 2 回目以降を省く（①②…は最初の画像にまとめて付いている）
                bool sameAsPrev = i > 0 && t.steps[i - 1].image == s.image;
                if (!string.IsNullOrEmpty(s.image) && !sameAsPrev) col.Add(Shot(s.image));
                row.Add(col);
                page.Add(row);
            }

            if (t.action != null)
            {
                var act = UIKit.Div("viah-guide-action");
                act.Add(UIKit.Text(t.steps.Count > 0 ? "実際にやってみましょう" : "画面を見てみましょう", "viah-bold", "viah-grow"));
                act.Add(UIKit.Btn(t.actionLabel ?? "この画面を開く", () => t.action(window), "primary"));
                page.Add(act);
            }

            // 「〜とは？」のページには、その分類でできることを並べる
            if (t.concept && group != null)
            {
                page.Add(UIKit.Text("この機能でできること", "viah-section-title", "viah-mb8"));
                page.Add(Links(group.topics));
            }
            else if (t.related.Length > 0)
            {
                page.Add(UIKit.Text("あわせて読む", "viah-section-title", "viah-mb8"));
                page.Add(Links(t.related));
            }
        }

        VisualElement Links(IEnumerable<string> ids)
        {
            var list = UIKit.Div("viah-guide-links");
            foreach (var id in ids)
            {
                var other = GuideContent.Find(id);
                if (other == null) continue;
                var row = UIKit.Row("viah-guide-link");
                var col = UIKit.Div("viah-grow");
                col.Add(UIKit.Text(other.title, "viah-bold"));
                col.Add(UIKit.Text(other.summary, "viah-faint", "viah-small", "viah-wrap"));
                row.Add(col);
                row.Add(HomePage.Arrow());
                UIKit.Clickable(row, () => window.Show(new GuidePage(other.id)));
                list.Add(row);
            }
            return list;
        }

        /// <summary>注釈つきスクリーンショット。幅に合わせて縮小し、元の大きさより大きくはしない。</summary>
        static VisualElement Shot(string name, params string[] classes)
        {
            var tex = LoadImage(name);
            if (tex == null) return new VisualElement();
            var frame = UIKit.Div("viah-guide-shot");
            foreach (var c in classes) frame.AddToClassList(c);
            var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.AddToClassList("viah-guide-shot-img");
            frame.Add(img);
            frame.style.maxWidth = tex.width / ImageDensity;
            frame.style.width = Length.Percent(100);
            frame.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var w = frame.resolvedStyle.width;
                if (float.IsNaN(w) || w <= 0) return;
                var h = Mathf.Round(w * tex.height / tex.width);
                if (Mathf.Abs(frame.resolvedStyle.height - h) > 0.5f) frame.style.height = h;
            });
            frame.tooltip = "クリックで拡大";
            frame.pickingMode = PickingMode.Position;
            frame.RegisterCallback<ClickEvent>(_ => GuideImageWindow.Open(tex, name));
            return frame;
        }
    }

    /// <summary>ガイドの画像を原寸で見るための小さなウィンドウ。</summary>
    public class GuideImageWindow : EditorWindow
    {
        Texture2D tex;

        public static void Open(Texture2D tex, string title)
        {
            var w = CreateInstance<GuideImageWindow>();
            w.tex = tex;
            w.titleContent = new GUIContent("ガイドの画像");
            float ppp = EditorGUIUtility.pixelsPerPoint;
            var size = new Vector2(tex.width / GuidePage.ImageDensity, tex.height / GuidePage.ImageDensity);
            var max = new Vector2(Screen.currentResolution.width / ppp * 0.9f, Screen.currentResolution.height / ppp * 0.85f);
            float k = Mathf.Min(1f, max.x / size.x, max.y / size.y);
            size *= k;
            w.ShowUtility();
            w.position = new Rect((Screen.currentResolution.width / ppp - size.x) / 2, (Screen.currentResolution.height / ppp - size.y) / 2, size.x, size.y);
        }

        void CreateGUI()
        {
            var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
            img.style.flexGrow = 1;
            rootVisualElement.Add(img);
            rootVisualElement.RegisterCallback<ClickEvent>(_ => Close());
            rootVisualElement.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) Close();
            });
            rootVisualElement.focusable = true;
            rootVisualElement.schedule.Execute(() => rootVisualElement.Focus());
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>
    /// ⑤ プロジェクトブックマーカー：よく使うフォルダーを登録して、Project タブをすぐその場所へ切り替える。
    /// 最後に使ったロックなしのタブを切り替え、Selection（Inspector の表示）は変えない。
    /// </summary>
    public class BookmarksPage : Page
    {
        public override string Section => "bookmarks";

        VisualElement listBox;
        Label countLabel;
        string filter = "";

        public override void Build()
        {
            var scroll = UIKit.PageScroll();
            root.Add(scroll);
            var c = scroll.contentContainer;

            var head = UIKit.Div("viah-page-head");
            head.Add(UIKit.Text("プロジェクトブックマーカー", "viah-page-title", "viah-grow"));
            var tools = UIKit.Row("viah-page-head-tools");
            var tab = UIKit.Btn("Favorite", "ブックマークのタブを出す", BookmarkWindow.Open, "primary");
            tab.name = "btn-bookmark-window";
            tab.tooltip = "Project タブの隣に、ブックマークだけのタブを出します。Avatar Hub を開かなくても、クリック 1 回で飛べます";
            tools.Add(tab);
            head.Add(tools);
            c.Add(head);
            c.Add(UIKit.Text("よく使うフォルダーを登録しておくと、Project ウィンドウですぐにその場所へ飛べます。", "viah-page-sub"));
            c.Add(UIKit.Hint("bookmark.intro", "飛ぶときの動き",
                "最後に使った Project タブ（ロックしていないもの）が、その場所に切り替わります。ロック中のタブは動かしません（全部ロック中なら新しいタブを開きます）。選択中のもの（Inspector の表示）は変わりません。"));

            var addCard = UIKit.Card();
            addCard.name = "card-bookmark-add";
            addCard.Add(UIKit.SectionHead("1", "ブックマークを追加", "Project ウィンドウからフォルダー（またはアセット）をドロップします。複数まとめても OK"));
            var zone = new DropZone("ここにフォルダーをドラッグ＆ドロップ", "アセットを入れると、そのアセットのあるフォルダーを開いて光らせます。");
            zone.dropped += Add;
            addCard.Add(zone);
            var row = UIKit.Row("viah-mt8", "viah-btn-group");
            row.Add(UIKit.Btn("Toolbar Plus", "Project で選択中のものを追加", () =>
            {
                var paths = Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).Where(p => p.StartsWith("Assets", StringComparison.Ordinal)).ToList();
                if (paths.Count == 0)
                {
                    window.Toast("Project ウィンドウで何も選択されていません", "warn");
                    return;
                }
                Add(paths);
            }, "small"));
            addCard.Add(row);
            c.Add(addCard);

            var listCard = UIKit.Card();
            listCard.name = "card-bookmark-list";
            var listHead = UIKit.Row("viah-queue-head");
            countLabel = UIKit.Text("", "viah-section-title");
            listHead.Add(countLabel);
            listHead.Add(UIKit.Spacer());
            var search = new ViahTextField("名前・場所で絞り込み");
            search.style.width = 200;
            search.style.flexGrow = 0;
            search.changed += q =>
            {
                filter = q;
                RenderList();
            };
            listHead.Add(search);
            listCard.Add(listHead);
            listBox = UIKit.Div();
            listCard.Add(listBox);
            c.Add(listCard);

            var shareCard = UIKit.Card();
            shareCard.name = "card-bookmark-share";
            shareCard.Add(UIKit.SectionHead("2", "人に渡す・別のプロジェクトで使う", "JSON に書き出して渡せます。別の Unity プロジェクトでは、同じ場所（パス）にフォルダーがあるものだけ使えます"));
            var shareRow = UIKit.Row("viah-btn-group");
            shareRow.Add(UIKit.Btn("SaveAs", "JSON に書き出す…", () =>
            {
                if (BookmarkActions.Items.Count == 0)
                {
                    window.Toast("ブックマークがまだありません", "warn");
                    return;
                }
                var path = EditorUtility.SaveFilePanel("ブックマークを書き出す", "", "bookmarks.json", "json");
                if (string.IsNullOrEmpty(path)) return;
                BookmarkActions.Export(path);
                window.Toast("書き出しました", "", "場所を開く", () => ViahStorage.RevealFile(path));
            }, "small"));
            shareRow.Add(UIKit.Btn("Import", "JSON から読み込む…", () =>
            {
                var path = EditorUtility.OpenFilePanel("ブックマークを読み込む", "", "json");
                if (string.IsNullOrEmpty(path)) return;
                window.Toast(BookmarkActions.Import(path));
            }, "small", "viah-ml4"));
            shareCard.Add(shareRow);
            c.Add(shareCard);

            RenderList();
        }

        public override void OnDataChanged() => RenderList();

        void Add(List<string> paths)
        {
            int added = BookmarkActions.Add(paths);
            if (added < paths.Count) window.Toast(added == 0 ? "すでに登録されています" : $"{paths.Count - added} 件はすでに登録されています", "warn");
        }

        void RenderList()
        {
            if (listBox == null) return;
            listBox.Clear();
            var items = BookmarkActions.Items;
            countLabel.text = $"ブックマーク（{items.Count} 件）";
            if (items.Count == 0)
            {
                listBox.Add(UIKit.Text("まだありません。上にフォルダーをドロップしてください。", "viah-faint", "viah-mt8"));
                return;
            }
            var tokens = SearchUtil.Tokens(filter);
            for (int i = 0; i < items.Count; i++)
            {
                var b = items[i];
                if (tokens.Length > 0 && !SearchUtil.Contains(SearchUtil.Normalize(b.DisplayLabel + " " + b.target.path), tokens)) continue;
                listBox.Add(Row(b, i, tokens.Length == 0));
            }
        }

        VisualElement Row(Bookmark b, int index, bool canMove)
        {
            var path = BookmarkActions.PathOf(b);
            var row = UIKit.Row("viah-bookmark-row");
            UIKit.SetClass(row, "is-missing", path == null);
            row.Add(UIKit.Img(path != null ? UIKit.AssetIcon(path, false) : UIKit.Icon("console.warnicon.sml")));
            var col = UIKit.Div("viah-grow", "viah-ml8");
            col.Add(UIKit.Text(b.DisplayLabel, "viah-bold", "viah-ellipsis"));
            col.Add(UIKit.Text(path == null ? $"{b.target.path}（この Unity プロジェクトにはありません）" : path, "viah-faint", "viah-small", "viah-ellipsis"));
            row.Add(col);
            row.tooltip = path == null ? "同じ場所にフォルダーがある Unity プロジェクトで使えます" : "クリックで Project ウィンドウをこの場所に切り替えます";

            var rename = UIKit.Btn("名前", () => Rename(b), "small", "ghost", "viah-ml8").With(x => x.tooltip = "表示する名前を変える");
            var up = UIKit.Btn("↑", () => BookmarkActions.Move(index, -1), "small", "ghost", "viah-ml4").With(x => x.tooltip = "上へ");
            up.SetEnabled(canMove && index > 0);
            var down = UIKit.Btn("↓", () => BookmarkActions.Move(index, +1), "small", "ghost").With(x => x.tooltip = "下へ");
            down.SetEnabled(canMove && index < BookmarkActions.Items.Count - 1);
            var remove = UIKit.Btn("×", () => BookmarkActions.Remove(b), "small", "ghost").With(x => x.tooltip = "ブックマークから外す（フォルダーは消えません）");
            foreach (var btn in new[] { rename, up, down, remove })
            {
                btn.RegisterCallback<ClickEvent>(e => e.StopPropagation());
                row.Add(btn);
            }
            row.RegisterCallback<ClickEvent>(_ =>
            {
                var err = BookmarkActions.Jump(b);
                if (err != null) window.Toast(err, "warn");
            });
            return row;
        }

        void Rename(Bookmark b)
        {
            var field = new ViahTextField("表示する名前（空にするとフォルダー名）", b.label);
            window.ShowDialog("ブックマークの名前", $"場所：{b.target.path}", field,
                new ViahWindow.DialogButton("やめる", null, "ghost"),
                new ViahWindow.DialogButton("変更する", () => BookmarkActions.Rename(b, field.value), "primary"));
            field.schedule.Execute(() => UIKit.FocusField(field.field)).ExecuteLater(50);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>ホーム：「何をしたいですか？」の選択肢と、最近使ったもの。</summary>
    public class HomePage : Page
    {
        public override string Section => "home";

        public override void Build()
        {
            var scroll = UIKit.PageScroll();
            root.Add(scroll);
            var c = scroll.contentContainer;

            c.Add(UIKit.Text("何をしたいですか？", "viah-page-title"));
            c.Add(UIKit.Text("選ぶと、実際の画面の画像つきで手順を説明します。", "viah-page-sub"));

            var hidden = ViahDatabase.Settings.hiddenWorkflows;
            foreach (var g in GuideContent.Groups)
            {
                if (g.workflow != null && hidden.Contains(g.workflow)) continue;
                c.Add(GroupBlock(g));
            }

            var projects = ViahDatabase.Projects;
            var profiles = ViahDatabase.Profiles;
            if (projects.Count == 0 && profiles.Count == 0) return;

            c.Add(UIKit.Text("最近使ったもの", "viah-section-title", "viah-mt8"));
            var recent = UIKit.Div("viah-recent-row");
            if (!hidden.Contains("projects"))
            {
                recent.Add(RecentCard("プロジェクト", "Folder Icon", projects.OrderByDescending(p => p.createdAt).Take(5)
                    .Select(p => (p.name, p.RootPath ?? "（この Unity プロジェクトにはフォルダーがありません）", (Action)(() => window.Show(new ProjectsPage(p.id))))).ToList()));
            }
            if (!hidden.Contains("export") || !hidden.Contains("profiles"))
            {
                recent.Add(RecentCard("プロファイル", "Prefab Icon", profiles.OrderByDescending(p => p.updatedAt).Take(5)
                    .Select(p => (p.name, LastExportText(p), (Action)(() =>
                    {
                        if (hidden.Contains("export")) window.Show(new ProfilesPage(p.id));
                        else window.Show(new ExportPage(p.id));
                    }))).ToList()));
            }
            c.Add(recent);
        }

        VisualElement GroupBlock(GuideGroup g)
        {
            var block = UIKit.Div("viah-task-group");
            block.name = "group-" + g.id;
            var head = UIKit.Div("viah-task-group-head");
            head.Add(UIKit.Img(g.icon));
            head.Add(UIKit.Text(g.title, "viah-section-title"));
            block.Add(head);

            var grid = UIKit.Div("viah-task-grid");
            if (g.conceptId != null) grid.Add(TaskTile(GuideContent.Find(g.conceptId)));
            foreach (var id in g.topics)
            {
                var t = GuideContent.Find(id);
                if (t != null) grid.Add(TaskTile(t));
            }
            block.Add(grid);
            return block;
        }

        VisualElement TaskTile(GuideTopic t)
        {
            var cell = UIKit.Div("viah-task-cell");
            var tile = UIKit.Div("viah-task");
            tile.name = "task-" + t.id;
            if (t.concept) tile.AddToClassList("concept");
            var body = UIKit.Div("viah-task-body");
            body.Add(UIKit.Text(t.title, "viah-task-title"));
            body.Add(UIKit.Text(t.summary, "viah-task-text"));
            tile.Add(body);
            tile.Add(Arrow());
            UIKit.Clickable(tile, () => window.Show(new GuidePage(t.id)));
            cell.Add(tile);
            return cell;
        }

        internal static VisualElement Arrow()
        {
            var arrow = UIKit.Div("viah-arrow");
            arrow.Add(UIKit.Div("viah-arrow-glyph"));
            arrow.pickingMode = PickingMode.Ignore;
            return arrow;
        }

        static string LastExportText(PackageProfile p)
        {
            var h = ViahDatabase.History.Get(p.id, false);
            if (h == null || string.IsNullOrEmpty(h.lastExportAt)) return "まだ書き出していません";
            return $"前回 {ViahStorage.ShortDate(h.lastExportAt)}" + (string.IsNullOrEmpty(h.lastVersion) ? "" : $"・Ver{h.lastVersion}");
        }

        static VisualElement RecentCard(string title, string icon, List<(string name, string sub, Action open)> items)
        {
            var card = UIKit.Div("viah-recent");
            var head = UIKit.Row("viah-mb8");
            head.Add(UIKit.Img(icon));
            head.Add(UIKit.Text(title, "viah-bold", "viah-ml6"));
            card.Add(head);
            if (items.Count == 0)
            {
                card.Add(UIKit.Text("まだありません", "viah-faint"));
                return card;
            }
            foreach (var it in items)
            {
                var row = UIKit.Div("viah-list-item");
                row.Add(UIKit.Text(it.name, "viah-list-item-title"));
                row.Add(UIKit.Text(it.sub, "viah-list-item-sub"));
                var open = it.open;
                row.RegisterCallback<ClickEvent>(_ => open());
                card.Add(row);
            }
            return card;
        }
    }
}

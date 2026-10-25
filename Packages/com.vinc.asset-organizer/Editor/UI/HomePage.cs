using System.Linq;
using UnityEngine.UIElements;

namespace VInc.AssetOrganizer.UI
{
    /// <summary>ホーム：はじめての人向けの 3 ステップと、最近のもの。</summary>
    public class HomePage : Page
    {
        public override string Section => "home";

        public override void Build()
        {
            var scroll = UIKit.PageScroll();
            root.Add(scroll);

            var projects = AODatabase.Projects;
            var profiles = AODatabase.Profiles;
            bool exported = AODatabase.State.history.Any(h => !string.IsNullOrEmpty(h.lastExportAt));

            var hero = UIKit.Div("ao-hero");
            hero.Add(UIKit.Text("フォルダー作成から UnityPackage 書き出しまで、ミスなく。", "ao-hero-title"));
            hero.Add(UIKit.Text(
                "案件ごとのフォルダー構成を自動で作り、何を含めるかを「プロファイル」として一度だけ登録。" +
                "あとはボタンひとつで、バージョン・日付・時刻つきの名前で書き出せます。下の 3 ステップの順に進めてください。",
                "ao-hero-text"));
            scroll.Add(hero);

            int next = projects.Count == 0 ? 0 : profiles.Count == 0 ? 1 : exported ? -1 : 2;
            var steps = UIKit.Div("ao-steps-row");
            steps.Add(StepCard(0, next, projects.Count > 0, "プロジェクトを作る",
                "名前を 1 つ入れて、使うフォルダーにチェックするだけ。綴りや Texture/Textures の揺れに悩まず、いつも同じ構成で作れます。タグを付けると日本語で検索できます。",
                projects.Count > 0 ? $"{projects.Count} 件作成済み" : null,
                "新しいプロジェクト", () => window.Show(new ProjectsPage(ProjectsPage.NewKey))));
            steps.Add(StepCard(1, next, profiles.Count > 0, "プロファイルを登録",
                "プレハブをドロップすると、アニメーター（MA Merge Animator なども）からたどって、使っているアニメーション・マテリアル・テクスチャを自動でチェック。含める/含めないを一度決めれば保存されます。",
                profiles.Count > 0 ? $"{profiles.Count} 件登録済み" : null,
                "プロファイルを作る", () => window.Show(new ProfileWizardPage(null))));
            steps.Add(StepCard(2, next, exported, "エクスポート",
                "プロファイルを選んで、バージョン・日付・時刻をクリックで付け外し。複数のプロファイルを順番にまとめて書き出す「シーケンス」もあります。",
                exported ? "書き出し済み" : null,
                "エクスポートへ", () => window.Navigate("export")));
            scroll.Add(steps);

            var recentRow = UIKit.Div("ao-steps-row");
            recentRow.Add(RecentCard("最近のプロジェクト", projects.Count == 0 ? null :
                projects.OrderByDescending(p => p.createdAt).Take(5).Select(p => (p.name, p.RootPath ?? "（フォルダーが見つかりません）", (System.Action)(() => window.Show(new ProjectsPage(p.id))))).ToList()));
            recentRow.Add(RecentCard("最近のプロファイル", profiles.Count == 0 ? null :
                profiles.OrderByDescending(p => p.updatedAt).Take(5).Select(p => (p.name, LastExportText(p), (System.Action)(() => window.Show(new ExportPage(p.id))))).ToList()));
            scroll.Add(recentRow);

            var tips = UIKit.Card("flat");
            tips.Add(UIKit.SectionHead(null, "ちょっとしたコツ"));
            tips.Add(Tip("検索", "上の検索欄に日本語でキーワードを入れると、タグからプロジェクトやプロファイルが見つかります。ひらがな・カタカナ・全角半角の違いは気にしなくて大丈夫です。"));
            tips.Add(Tip("共有", "プリセット・プロファイルは JSON で書き出し/読み込みできます。人からもらった構成をそのまま使えます。"));
            tips.Add(Tip("安全", "書き出すのはプロファイルで選んだファイルだけです。基準フォルダーの外のファイル（アバター本体や他の人の素材）は、自分で選ばない限り入りません。"));
            var resetRow = UIKit.Row("ao-mt8");
            resetRow.Add(UIKit.Btn("閉じたヒントをすべて再表示", () =>
            {
                UIKit.ResetHints();
                window.Toast("ヒントを再表示するようにしました");
            }, "small", "ghost"));
            tips.Add(resetRow);
            scroll.Add(tips);
        }

        string LastExportText(PackageProfile p)
        {
            var h = AODatabase.State.Get(p.id, false);
            if (h == null || string.IsNullOrEmpty(h.lastExportAt)) return "まだ書き出していません";
            return $"前回 {AOStorage.ShortDate(h.lastExportAt)}" + (string.IsNullOrEmpty(h.lastVersion) ? "" : $"・Ver{h.lastVersion}");
        }

        VisualElement StepCard(int index, int next, bool done, string title, string text, string status, string cta, System.Action action)
        {
            var card = UIKit.Div("ao-step-card");
            UIKit.SetClass(card, "is-next", index == next);
            UIKit.SetClass(card, "is-done", done);
            var head = UIKit.Div("ao-step-card-head");
            var num = UIKit.Text(done ? "✓" : (index + 1).ToString(), "ao-section-num");
            UIKit.SetClass(num, "is-done", done);
            head.Add(num);
            head.Add(UIKit.Text(title, "ao-section-title"));
            if (index == next) head.Add(UIKit.Badge("次はここ", "rule"));
            card.Add(head);
            card.Add(UIKit.Text(text, "ao-step-card-text"));
            var foot = UIKit.Row();
            if (status != null) foot.Add(UIKit.Text(status, "ao-faint", "ao-small"));
            foot.Add(UIKit.Spacer());
            foot.Add(UIKit.Btn(cta, action, index == next ? "primary" : ""));
            card.Add(foot);
            return card;
        }

        VisualElement RecentCard(string title, System.Collections.Generic.List<(string name, string sub, System.Action open)> items)
        {
            var card = UIKit.Div("ao-step-card");
            card.Add(UIKit.Text(title, "ao-section-title", "ao-mb8"));
            if (items == null)
            {
                card.Add(UIKit.Text("まだありません", "ao-faint"));
                return card;
            }
            foreach (var it in items)
            {
                var row = UIKit.Div("ao-list-item");
                row.Add(UIKit.Text(it.name, "ao-list-item-title"));
                row.Add(UIKit.Text(it.sub, "ao-list-item-sub"));
                var open = it.open;
                row.RegisterCallback<ClickEvent>(_ => open());
                card.Add(row);
            }
            return card;
        }

        static VisualElement Tip(string label, string text)
        {
            var row = UIKit.Row("ao-mt4");
            row.style.alignItems = Align.FlexStart;
            var b = UIKit.Badge(label, "rule");
            b.style.marginLeft = 0;
            b.style.marginRight = 8;
            b.style.marginTop = 1;
            row.Add(b);
            row.Add(UIKit.Text(text, "ao-dim", "ao-wrap", "ao-grow"));
            return row;
        }
    }
}

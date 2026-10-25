using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AssetOrganizer.UI
{
    /// <summary>
    /// ② プロファイルの登録・編集ウィザード。
    /// 1 登録方法 → 2 プレハブを入れる（自動のみ）→ 3 中身を確認 → 4 名前とタグ
    /// </summary>
    public class ProfileWizardPage : Page
    {
        public override string Section => "profiles";

        const int StepMethod = 0, StepSeeds = 1, StepSelect = 2, StepName = 3, StepDone = 4;
        static readonly string[] StepLabels = { "登録方法", "プレハブを入れる", "中身を確認", "名前とタグ" };

        readonly PackageProfile draft;
        readonly bool isEdit;
        readonly Action<PackageProfile> onFinish;

        string projectId;
        string basePath;
        string mode;
        readonly List<string> seeds = new List<string>();
        SelectionModel model = new SelectionModel();
        ScanResult scan;
        HashSet<string> newPaths = new HashSet<string>();
        bool showAll;
        bool scanTried;
        string scanMessage;
        int step;
        bool packageNameSame = true;
        bool dirty;

        Stepper stepper;
        VisualElement backLink;
        VisualElement body;
        VisualElement footer;

        /// <param name="existing">編集するプロファイル（新規なら null）</param>
        /// <param name="projectIdForNew">新規作成時に紐づけるプロジェクト</param>
        /// <param name="onFinish">保存後に呼ぶ（null なら完了画面を出す）</param>
        public ProfileWizardPage(PackageProfile existing, string projectIdForNew = null, Action<PackageProfile> onFinish = null)
        {
            this.onFinish = onFinish;
            if (existing != null)
            {
                isEdit = true;
                draft = existing.Clone();
                projectId = draft.projectId;
                basePath = ProfileResolver.BaseFolderOf(draft) ?? "Assets";
                mode = draft.mode;
                foreach (var s in draft.seeds)
                {
                    var p = s.Resolve();
                    if (p != null) seeds.Add(p);
                }
                model = SelectionModel.FromProfile(draft);
                packageNameSame = string.IsNullOrWhiteSpace(draft.packageName) || draft.packageName == draft.name;
                step = StepSelect;
            }
            else
            {
                draft = new PackageProfile { id = AOStorage.NewId() };
                draft.Sanitize();
                projectId = projectIdForNew ?? "";
                var proj = AODatabase.FindProject(projectId);
                basePath = proj?.RootPath;
                mode = "auto";
                if (proj != null)
                {
                    draft.tags = new List<string>(proj.tags);
                    var prefabs = FindPrefabFolder(basePath);
                    if (prefabs != null) seeds.Add(prefabs);
                }
                step = StepMethod;
            }
        }

        static string FindPrefabFolder(string root)
        {
            if (string.IsNullOrEmpty(root) || !AssetDatabase.IsValidFolder(root)) return null;
            foreach (var sub in AssetDatabase.GetSubFolders(root))
            {
                var name = Path.GetFileName(sub);
                if (name.EndsWith("Prefabs", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Prefab", StringComparison.OrdinalIgnoreCase))
                    if (DependencyScanner.CountPrefabsIn(sub) > 0) return sub;
            }
            return null;
        }

        public override void Build()
        {
            var page = UIKit.Div();
            page.style.flexGrow = 1;
            root.Add(page);

            var top = UIKit.Div();
            top.style.paddingLeft = 26;
            top.style.paddingRight = 26;
            top.style.paddingTop = 18;
            backLink = UIKit.Row("ao-back");
            backLink.Add(UIKit.Text("‹ キャンセル"));
            UIKit.Clickable(backLink, Cancel);
            top.Add(backLink);
            var titleRow = UIKit.Row("ao-mb8");
            titleRow.Add(UIKit.Text(isEdit ? $"プロファイル「{draft.name}」を編集" : "新しいプロファイル", "ao-page-title"));
            top.Add(titleRow);
            stepper = new Stepper(StepLabels);
            stepper.canGo = CanGo;
            stepper.clicked += i => Go(i);
            top.Add(stepper);
            page.Add(top);

            body = UIKit.Div();
            body.style.flexGrow = 1;
            body.style.flexShrink = 1;
            page.Add(body);

            footer = UIKit.Div("ao-footer-bar");
            page.Add(footer);
            Render();
        }

        bool Skipped(int i) => i == StepSeeds && mode == "manual";

        bool CanGo(int i)
        {
            if (step == StepDone) return false;
            if (Skipped(i)) return false;
            if (i == StepMethod) return true;
            // 自動登録は、プレハブを入れたときに基準フォルダーが決まるので先に進める
            if (i == StepSeeds) return mode == "auto";
            if (!BaseValid()) return false;
            if (i == StepSelect) return mode == "manual" || isEdit || scan != null;
            if (i == StepName) return (mode == "manual" || isEdit || scan != null) && !model.IsEmpty;
            return false;
        }

        bool BaseValid() => !string.IsNullOrEmpty(basePath) && AssetDatabase.IsValidFolder(basePath);

        void Go(int i)
        {
            step = i;
            Render();
        }

        void Render()
        {
            body.Clear();
            footer.Clear();
            UIKit.Show(stepper, step != StepDone);
            UIKit.Show(backLink, step != StepDone);
            stepper.Set(step, Skipped);
            switch (step)
            {
                case StepMethod: RenderMethod(); break;
                case StepSeeds: RenderSeeds(); break;
                case StepSelect: RenderSelect(); break;
                case StepName: RenderName(); break;
                default: RenderDone(); break;
            }
        }

        ScrollView ScrollBody()
        {
            var scroll = UIKit.PageScroll();
            scroll.contentContainer.style.paddingTop = 4;
            body.Add(scroll);
            return scroll;
        }

        void Cancel()
        {
            if (dirty && step != StepDone && !EditorUtility.DisplayDialog("登録をやめますか？", "ここまでの選択は保存されません。", "やめる", "続ける")) return;
            if (isEdit && onFinish != null) onFinish(AODatabase.FindProfile(draft.id) ?? draft);
            else window.Show(new ProfilesPage(isEdit ? draft.id : null));
        }

        // ───────────────────────── 1. 登録方法 ─────────────────────────

        void RenderMethod()
        {
            var c = ScrollBody().contentContainer;
            c.Add(UIKit.Hint("wizard.method", "プロファイルとは",
                "「この unitypackage には何を入れるか」の登録です。一度決めれば、デバッグや修正で何度書き出しても、余計なファイルの混入や入れ忘れが起きません。"));

            var projCard = UIKit.Card();
            projCard.Add(UIKit.SectionHead("1", "どのプロジェクトのプロファイルですか？", "プロジェクトを選ぶと、そのフォルダーが「基準フォルダー」になり、名前やタグも引き継ぎます"));
            var choices = new List<string> { "" };
            choices.AddRange(AODatabase.Projects.Select(p => p.id));
            if (!choices.Contains(projectId)) projectId = "";
            var popup = new PopupField<string>(null, choices, projectId,
                id => string.IsNullOrEmpty(id) ? "（プロジェクトなし）" : AODatabase.FindProject(id)?.name ?? "?",
                id => string.IsNullOrEmpty(id) ? "（プロジェクトなし）" : AODatabase.FindProject(id)?.name + "　—　" + AODatabase.FindProject(id)?.RootPath);
            popup.style.maxWidth = 360;
            popup.RegisterValueChangedCallback(e =>
            {
                projectId = e.newValue;
                var proj = AODatabase.FindProject(projectId);
                if (proj != null)
                {
                    basePath = proj.RootPath;
                    if (!isEdit)
                    {
                        draft.tags = new List<string>(proj.tags);
                        seeds.Clear();
                        var prefabs = FindPrefabFolder(basePath);
                        if (prefabs != null) seeds.Add(prefabs);
                    }
                }
                dirty = true;
                Render();
            });
            projCard.Add(popup);

            var baseRow = UIKit.Row("ao-mt12");
            baseRow.Add(UIKit.Text("基準フォルダー", "ao-kv-key"));
            var pill = UIKit.Div("ao-path-pill");
            pill.Add(UIKit.Img("Folder Icon"));
            pill.Add(UIKit.Text(BaseValid() ? basePath : "未設定", "ao-ml6"));
            baseRow.Add(pill);
            bool linked = AODatabase.FindProject(projectId) != null;
            if (!linked)
            {
                baseRow.Add(UIKit.Btn("変更…", () =>
                {
                    var picked = UIKit.PickAssetsFolder("基準フォルダーを選ぶ", basePath);
                    if (picked == null) return;
                    SetBase(picked);
                }, "ao-ml8"));
                DropZone.AcceptFolderDrop(pill, SetBase);
            }
            projCard.Add(baseRow);
            projCard.Add(UIKit.Text(linked
                    ? "プロジェクトのフォルダーが基準になります。"
                    : "この外にあるファイルは「外部」として扱い、最初はチェックを外します（アバター本体や他の人の素材が混ざるのを防ぎます）。Project ウィンドウからフォルダーをドロップしても選べます。",
                "ao-faint", "ao-small", "ao-wrap", "ao-mt8"));
            c.Add(projCard);

            var methodCard = UIKit.Card();
            methodCard.Add(UIKit.SectionHead("2", "登録のしかたを選んでください"));
            var row = UIKit.Div("ao-choice-row");
            row.Add(Choice("auto", "自動登録（おすすめ）",
                "プレハブやフォルダーをドロップするだけ。アニメーター（MA Merge Animator なども）からたどって、使っているアニメーション・マテリアル・テクスチャなどを自動でチェックします。最後に自分で微調整できます。"));
            row.Add(Choice("manual", "手動登録",
                "ファイルやフォルダーを自分で 1 つずつ選びます。プレハブを使わないデータや、決まったフォルダーを丸ごと入れたいときに。"));
            methodCard.Add(row);
            c.Add(methodCard);

            bool canNext = BaseValid() || mode == "auto";
            footer.Add(UIKit.Text(BaseValid() ? "" : mode == "auto" ? "基準フォルダーは、プレハブを入れると自動で決まります" : "基準フォルダーを選んでください",
                mode == "auto" ? "ao-faint" : "ao-msg-warn", "ao-grow", "ao-wrap"));
            var next = UIKit.Btn("次へ", () => Go(mode == "manual" ? StepSelect : StepSeeds), "primary", "big");
            next.SetEnabled(canNext);
            footer.Add(next);

            void SetBase(string folder)
            {
                basePath = folder;
                dirty = true;
                Render();
            }
        }

        VisualElement Choice(string key, string title, string text)
        {
            var card = UIKit.Div("ao-choice");
            UIKit.SetClass(card, "is-on", mode == key);
            var head = UIKit.Div("ao-choice-head");
            var radio = UIKit.Div("ao-radio");
            radio.Add(UIKit.Div("ao-radio-dot"));
            head.Add(radio);
            head.Add(UIKit.Text(title, "ao-choice-title"));
            card.Add(head);
            card.Add(UIKit.Text(text, "ao-choice-text"));
            card.RegisterCallback<ClickEvent>(e =>
            {
                if (mode != key)
                {
                    mode = key;
                    dirty = true;
                }
                Render();
                if (e.clickCount == 2 && BaseValid()) Go(mode == "manual" ? StepSelect : StepSeeds);
            });
            return card;
        }

        // ───────────────────────── 2. プレハブを入れる ─────────────────────────

        void RenderSeeds()
        {
            var c = ScrollBody().contentContainer;
            c.Add(UIKit.Hint("wizard.drop", "起点になるプレハブを入れてください",
                "ギミックのプレハブ（またはプレハブが入ったフォルダー）をドロップします。中のコンポーネント（MA Merge Animator / Merge Blend Tree / Menu Installer / Material Setter、VRC Avatar Descriptor など）が参照しているものを順にたどって、必要なファイルを自動で見つけます。"));

            var zone = new DropZone("ここにプレハブ または フォルダーをドラッグ＆ドロップ",
                "Project ウィンドウから持ってきてください。フォルダーを入れると、中のプレハブをすべて調べます。");
            zone.dropped += paths =>
            {
                AddSeeds(paths);
                Render();
            };
            c.Add(zone);

            var addRow = UIKit.Row("ao-mt8");
            addRow.Add(UIKit.Btn("Project ウィンドウで選択中のものを追加", () =>
            {
                var paths = Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).Where(p => p.StartsWith("Assets/")).ToList();
                if (paths.Count == 0)
                {
                    window.Toast("Project ウィンドウで何も選択されていません", "warn");
                    return;
                }
                AddSeeds(paths);
                Render();
            }, "small"));
            c.Add(addRow);

            if (seeds.Count > 0)
            {
                c.Add(UIKit.Text($"登録した起点（{seeds.Count}）", "ao-section-title", "ao-mt16"));
                foreach (var s in seeds.ToList())
                {
                    var path = s;
                    var item = UIKit.Row("ao-seed-item");
                    item.Add(UIKit.Img(UIKit.AssetIcon(path)));
                    var col = UIKit.Div("ao-grow", "ao-ml8");
                    col.Add(UIKit.Text(UIKit.AssetName(path), "ao-bold"));
                    bool folder = AssetDatabase.IsValidFolder(path);
                    col.Add(UIKit.Text(folder ? $"フォルダー：中のプレハブ {DependencyScanner.CountPrefabsIn(path)} 個を調べます" : path, "ao-faint", "ao-small"));
                    item.Add(col);
                    if (!ProfileResolver.IsInside(path, basePath)) item.Add(UIKit.Badge("外部", "ext", "基準フォルダーの外にあります"));
                    item.Add(UIKit.Btn("外す", () =>
                    {
                        seeds.Remove(path);
                        dirty = true;
                        Render();
                    }, "small", "ghost", "ao-ml8"));
                    c.Add(item);
                }

                // 基準フォルダー（プロジェクト未選択のときはここで確認・変更できる）
                var baseRow = UIKit.Row("ao-mt12");
                baseRow.Add(UIKit.Text("基準フォルダー", "ao-kv-key"));
                var pill = UIKit.Div("ao-path-pill");
                pill.Add(UIKit.Img("Folder Icon"));
                pill.Add(UIKit.Text(BaseValid() ? basePath : "未設定", "ao-ml6"));
                baseRow.Add(pill);
                if (AODatabase.FindProject(projectId) == null)
                {
                    baseRow.Add(UIKit.Btn("変更…", () =>
                    {
                        var picked = UIKit.PickAssetsFolder("基準フォルダーを選ぶ", basePath);
                        if (picked == null) return;
                        basePath = picked;
                        Render();
                    }, "ao-ml8"));
                    DropZone.AcceptFolderDrop(pill, folder =>
                    {
                        basePath = folder;
                        Render();
                    });
                }
                c.Add(baseRow);
                c.Add(UIKit.Text("この外にあるファイルは「外部」として扱い、最初はチェックを外します。", "ao-faint", "ao-small", "ao-mt4"));
            }

            footer.Add(UIKit.Btn("戻る", () => Go(StepMethod), "ghost"));
            footer.Add(UIKit.Spacer());
            if (scan != null)
            {
                footer.Add(UIKit.Btn("検出し直さずに次へ", () => Go(StepSelect), "ao-mr8"));
            }
            var start = UIKit.Btn("自動検出を開始 ▶", RunScan, "primary", "big");
            start.SetEnabled(seeds.Count > 0 && BaseValid());
            start.tooltip = seeds.Count == 0 ? "まずプレハブかフォルダーを入れてください" : "";
            footer.Add(start);
        }

        void AddSeeds(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                if (seeds.Contains(p)) continue;
                seeds.Add(p);
                dirty = true;
            }
            // プロジェクト未選択なら、ドロップしたファイルからプロジェクトを推測する
            if (AODatabase.FindProject(projectId) == null && seeds.Count > 0)
            {
                var first = seeds[0];
                var proj = AODatabase.Projects.FirstOrDefault(p => p.RootPath != null && ProfileResolver.IsInside(first, p.RootPath));
                if (proj != null)
                {
                    projectId = proj.id;
                    basePath = proj.RootPath;
                    if (draft.tags.Count == 0) draft.tags = new List<string>(proj.tags);
                    window.Toast($"プロジェクト「{proj.name}」のファイルなので、紐づけました");
                }
                else if (!BaseValid())
                {
                    // 基準フォルダーが未設定なら、Assets 直下の親フォルダーにする（あとで変更できる）
                    var parts = first.Split('/');
                    basePath = parts.Length >= 3 ? parts[0] + "/" + parts[1] : "Assets";
                    window.Toast($"基準フォルダーを {basePath} にしました（変更できます）");
                }
            }
        }

        void RunScan()
        {
            scan = DependencyScanner.Scan(DependencyScanner.ExpandSeeds(seeds));
            scanTried = true;
            newPaths = new HashSet<string>();
            int auto = 0, external = 0;

            if (!isEdit)
            {
                // 新規：検出したもののうち基準フォルダー内をチェック。フォルダーで入れた起点はフォルダーごと
                model = new SelectionModel();
                foreach (var s in seeds)
                    if (AssetDatabase.IsValidFolder(s) && ProfileResolver.IsInside(s, basePath)) model.SetFolder(s, true);
                foreach (var p in scan.assets.Keys)
                {
                    if (ProfileResolver.IsInside(p, basePath))
                    {
                        if (!model.IsFileIncluded(p)) model.SetFile(p, true);
                        auto++;
                    }
                    else external++;
                }
                scanMessage = $"自動で {auto} 件をチェックしました。紫の「自動」が付いているのが見つかったファイルです。" +
                              (external > 0 ? $"基準フォルダーの外のファイル {external} 件（オレンジの「外部」）は、念のため外してあります。" : "") +
                              (scan.packageDeps.Count > 0 ? $"\nVRCSDK・MA・lilToon などパッケージ内のファイル {scan.packageDeps.Count} 件は、unitypackage に入れられないため対象外です。" : "");
            }
            else
            {
                // 編集：前回のあとに増えた依存を「新規」として知らせ、基準フォルダー内なら自動でチェック
                var ignored = new HashSet<string>(draft.ignoredDependencies.Select(r => r.Resolve()).Where(x => x != null));
                foreach (var p in scan.assets.Keys)
                {
                    if (model.IsFileIncluded(p) || ignored.Contains(p) || model.IsExplicitlyExcluded(p)) continue;
                    newPaths.Add(p);
                    if (ProfileResolver.IsInside(p, basePath))
                    {
                        model.SetFile(p, true);
                        auto++;
                    }
                    else external++;
                }
                scanMessage = newPaths.Count == 0
                    ? "前回の登録から、新しく使われ始めたファイルはありませんでした。"
                    : $"前回の登録のあとで、新しく使われ始めたファイルが {newPaths.Count} 件あります（緑の「新規」）。基準フォルダー内の {auto} 件は自動でチェックしました。" +
                      (external > 0 ? $"外部の {external} 件は外してあります。" : "");
            }
            dirty = true;
            Go(StepSelect);
        }

        // ───────────────────────── 3. 中身を確認 ─────────────────────────

        void RenderSelect()
        {
            if (!scanTried && scan == null && mode == "auto" && seeds.Count > 0 && isEdit)
            {
                // 編集を開いたときは、今の状態でもう一度たどって「新規」を知らせる
                RunScan();
                return;
            }

            var wrap = UIKit.Div();
            wrap.style.flexGrow = 1;
            wrap.style.paddingLeft = 26;
            wrap.style.paddingRight = 26;
            wrap.style.paddingBottom = 10;
            body.Add(wrap);

            if (!string.IsNullOrEmpty(scanMessage))
                wrap.Add(UIKit.Hint(null, "自動検出の結果", scanMessage, newPaths.Count > 0 ? "warn" : "success", true));
            wrap.Add(UIKit.Hint("wizard.select", "選び方",
                "行をクリックで 1 つずつ切り替え、Shift＋クリックで範囲をまとめて切り替えます。フォルダーのチェックは「中身ぜんぶ（これから増えるファイルも）」の意味です。行にマウスを乗せると「フォルダーごと」ボタンと、Project ウィンドウで表示するボタンが出ます。"));

            var tree = new AssetTreeView();
            var toolbar = UIKit.Div("ao-toolbar");
            var search = new AOTextField("ファイル名で絞り込み");
            search.style.maxWidth = 220;
            search.changed += q => tree.SetQuery(q);
            toolbar.Add(search);
            var seg = new Segmented(new[] { "すべて", "選択中", "未選択", "自動", "外部", "新規" }, 0, i => tree.SetFilter((AssetTreeView.Filter)i), true);
            seg.style.marginLeft = 8;
            toolbar.Add(seg);
            toolbar.Add(UIKit.Spacer());
            toolbar.Add(UIKit.Btn("すべて開く", () => tree.ExpandAll(true), "small", "ghost"));
            toolbar.Add(UIKit.Btn("閉じる", () => tree.ExpandAll(false), "small", "ghost"));
            var all = new Switch("Assets 全体を表示", showAll, "基準フォルダーの外も含めて、Assets のすべてから選びます");
            all.style.marginLeft = 10;
            all.changed += on =>
            {
                showAll = on;
                tree.Setup(model, basePath, showAll, scan, newPaths);
            };
            toolbar.Add(all);
            wrap.Add(toolbar);

            wrap.Add(tree);

            var legend = UIKit.Div("ao-legend");
            legend.Add(Legend("自動", "auto", "プレハブが使っている"));
            legend.Add(Legend("外部", "ext", "基準フォルダーの外"));
            legend.Add(Legend("新規", "new", "前回の登録のあとで使われ始めた"));
            legend.Add(Legend("フォルダーごと", "rule", "今後増えるファイルも入る"));
            wrap.Add(legend);

            var summary = UIKit.Text("", "ao-grow", "ao-wrap");
            footer.Add(UIKit.Btn("戻る", () => Go(mode == "manual" ? StepMethod : StepSeeds), "ghost"));
            footer.Add(UIKit.Div().With(d => d.style.width = 14));
            footer.Add(summary);
            var next = UIKit.Btn("次へ", () => Go(StepName), "primary", "big");
            footer.Add(next);

            void Refresh()
            {
                int count = tree.IncludedCount;
                int ext = tree.AllFiles().Count(n => n.included && n.external);
                int extDetected = tree.AllFiles().Count(n => !n.included && n.external && n.detected);
                var text = $"含める：{count} ファイル";
                if (model.includeFolders.Count > 0) text += $"（フォルダーごと {model.includeFolders.Count}）";
                if (ext > 0) text += $"　・　外部のファイルを {ext} 件含めています";
                if (extDetected > 0) text += $"　・　外部 {extDetected} 件は含めていません";
                summary.text = text;
                next.SetEnabled(!model.IsEmpty);
                next.tooltip = model.IsEmpty ? "1 つ以上選んでください" : "";
            }
            tree.Changed += () =>
            {
                dirty = true;
                Refresh();
            };
            tree.Setup(model, basePath, showAll, scan, newPaths);
            Refresh();
        }

        static VisualElement Legend(string badge, string kind, string text)
        {
            var item = UIKit.Div("ao-legend-item");
            item.Add(UIKit.Badge(badge, kind));
            item.Add(UIKit.Text(text, "ao-faint", "ao-small"));
            return item;
        }

        // ───────────────────────── 4. 名前とタグ ─────────────────────────

        void RenderName()
        {
            var c = ScrollBody().contentContainer;
            if (string.IsNullOrWhiteSpace(draft.name)) draft.name = DefaultName();

            var nameCard = UIKit.Card();
            nameCard.Add(UIKit.SectionHead("1", "名前", "プロファイル名は、そのまま unitypackage の名前になります（例：productC → productC_Ver1.0.0.unitypackage）"));
            var nameField = new AOTextField("例：productC", draft.name, true);
            nameCard.Add(nameField);
            var nameMsg = UIKit.Text("", "ao-field-help", "ao-small");
            nameCard.Add(nameMsg);

            var same = new Switch("unitypackage の名前もプロファイル名と同じにする", packageNameSame);
            same.style.marginTop = 12;
            nameCard.Add(same);
            var pkgField = new AOTextField("unitypackage の名前", packageNameSame ? draft.name : draft.packageName);
            pkgField.style.marginTop = 8;
            pkgField.SetEnabled(!packageNameSame);
            nameCard.Add(pkgField);
            c.Add(nameCard);

            var tagCard = UIKit.Card();
            tagCard.Add(UIKit.SectionHead("2", "タグ（任意）", "あとで名前を忘れても、このキーワードで検索できます"));
            var tags = new TagField(draft.tags);
            tags.changed += t =>
            {
                draft.tags = t;
                dirty = true;
            };
            tagCard.Add(tags);
            c.Add(tagCard);

            var outCard = UIKit.Card();
            outCard.Add(UIKit.SectionHead("3", "書き出し先", "unitypackage を保存するフォルダー。エクスポート画面でも変更できます"));
            Button resetOut = null;
            var outRow = UIKit.Row();
            var pill = UIKit.Div("ao-path-pill");
            pill.Add(UIKit.Img("Folder Icon"));
            var outLabel = UIKit.Text("", "ao-ml6");
            pill.Add(outLabel);
            outRow.Add(pill);
            outRow.Add(UIKit.Btn("変更…", () =>
            {
                var picked = EditorUtility.OpenFolderPanel("書き出し先を選ぶ", PackageExporter.OutputDirOf(draft), "");
                if (string.IsNullOrEmpty(picked)) return;
                draft.outputFolder = picked;
                dirty = true;
                RefreshOut();
            }, "ao-ml8"));
            resetOut = UIKit.Btn("既定に戻す", () =>
            {
                draft.outputFolder = "";
                RefreshOut();
            }, "ghost", "ao-ml4");
            outRow.Add(resetOut);
            outCard.Add(outRow);
            c.Add(outCard);

            var preview = UIKit.Div("ao-filename");
            preview.Add(UIKit.Img("Package Manager"));
            var previewText = UIKit.Text("", "ao-filename-text", "ao-ml8");
            preview.Add(previewText);
            c.Add(preview);

            footer.Add(UIKit.Btn("戻る", () => Go(StepSelect), "ghost"));
            footer.Add(UIKit.Spacer());
            var save = UIKit.Btn(isEdit ? "保存" : "保存して完了", Save, "primary", "big");
            footer.Add(save);

            void RefreshOut()
            {
                outLabel.text = string.IsNullOrWhiteSpace(draft.outputFolder)
                    ? "既定（" + AOStorage.PrettyPath(PackageExporter.OutputDirOf(draft)) + "）"
                    : AOStorage.PrettyPath(draft.outputFolder);
                UIKit.Show(resetOut, !string.IsNullOrWhiteSpace(draft.outputFolder));
            }

            void Refresh()
            {
                var baseName = packageNameSame ? draft.name : draft.packageName;
                var err = string.IsNullOrWhiteSpace(draft.name) ? "名前を入れてください"
                    : AOStorage.SafeFileName(baseName) != (baseName ?? "").Trim() ? "ファイル名に使えない文字は「_」に置き換えて保存されます" : null;
                var dup = AODatabase.Profiles.Any(p => p.id != draft.id && string.Equals(p.name, draft.name, StringComparison.OrdinalIgnoreCase));
                nameMsg.text = err ?? (dup ? "同じ名前のプロファイルがあります（保存はできます）" : "");
                nameMsg.RemoveFromClassList("ao-msg-error");
                nameMsg.RemoveFromClassList("ao-msg-warn");
                if (nameMsg.text.Length > 0) nameMsg.AddToClassList(string.IsNullOrWhiteSpace(draft.name) ? "ao-msg-error" : "ao-msg-warn");
                nameField.SetError(string.IsNullOrWhiteSpace(draft.name));
                save.SetEnabled(!string.IsNullOrWhiteSpace(draft.name) && !string.IsNullOrWhiteSpace(baseName));
                var sample = PackageNaming.Build(string.IsNullOrWhiteSpace(baseName) ? "name" : baseName,
                    new NameOptions { useVersion = true, version = PkgVersion.First, useDate = true }, DateTime.Now);
                previewText.text = $"書き出し例：{sample}.unitypackage";
            }

            nameField.changed += v =>
            {
                draft.name = v.Trim();
                if (packageNameSame) pkgField.value = draft.name;
                dirty = true;
                Refresh();
            };
            same.changed += on =>
            {
                packageNameSame = on;
                pkgField.SetEnabled(!on);
                if (on) pkgField.value = draft.name;
                else if (string.IsNullOrWhiteSpace(draft.packageName)) draft.packageName = draft.name;
                pkgField.value = on ? draft.name : draft.packageName;
                Refresh();
            };
            pkgField.changed += v =>
            {
                draft.packageName = v.Trim();
                dirty = true;
                Refresh();
            };
            RefreshOut();
            Refresh();
        }

        string DefaultName()
        {
            var proj = AODatabase.FindProject(projectId);
            if (proj != null) return proj.name;
            if (seeds.Count > 0) return Path.GetFileNameWithoutExtension(seeds[0]);
            return UIKit.AssetName(basePath);
        }

        void Save()
        {
            draft.projectId = AODatabase.FindProject(projectId) != null ? projectId : "";
            draft.baseFolder = new AssetRef(basePath);
            draft.mode = mode;
            draft.packageName = packageNameSame ? "" : draft.packageName;
            draft.seeds = mode == "auto" ? AssetRef.FromPaths(seeds) : new List<AssetRef>();
            model.WriteTo(draft);
            if (scan != null)
            {
                // 使われているのに含めないと決めたもの → 次から警告しない
                draft.ignoredDependencies = AssetRef.FromPaths(scan.assets.Keys.Where(p => !model.IsFileIncluded(p)).OrderBy(p => p, StringComparer.Ordinal));
            }
            AODatabase.SaveProfile(draft);
            dirty = false;
            if (onFinish != null)
            {
                window.Toast($"プロファイル「{draft.name}」を保存しました");
                onFinish(draft);
                return;
            }
            Go(StepDone);
        }

        // ───────────────────────── 完了 ─────────────────────────

        void RenderDone()
        {
            var c = ScrollBody().contentContainer;
            var card = UIKit.Card("accent");
            card.Add(UIKit.SectionHead("✓", $"プロファイル「{draft.name}」を保存しました",
                $"{SelectionModel.FromProfile(draft).ResolveFiles().Count} ファイルが登録されています。次からは、エクスポート画面でこのプロファイルを選ぶだけで、ノーチェックで書き出せます。"));
            var actions = UIKit.Div("ao-btn-group", "ao-mt8");
            actions.Add(UIKit.Btn("このままエクスポートへ", () => window.Show(new ExportPage(draft.id)), "primary", "big"));
            actions.Add(UIKit.Btn("プロファイル一覧へ", () => window.Show(new ProfilesPage(draft.id))));
            actions.Add(UIKit.Btn("もう 1 つ作る", () => window.Show(new ProfileWizardPage(null, projectId)), "ghost"));
            card.Add(actions);
            c.Add(card);
            c.Add(UIKit.Hint(null, "保存場所", "プロファイルは JSON として " + AOStorage.PrettyPath(draft.FilePath) + " に保存されています。プロファイル画面から書き出して、他の人に渡すこともできます。"));
        }
    }
}

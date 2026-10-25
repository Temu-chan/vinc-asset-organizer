using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AssetOrganizer.UI
{
    /// <summary>① プロジェクト作成：新規作成フォームと、作成済みプロジェクトの一覧・詳細。</summary>
    public class ProjectsPage : Page
    {
        public const string NewKey = "__new__";
        public override string Section => "projects";

        /// <summary>入力途中の内容（プリセット編集から戻ってきても消えないように）。</summary>
        static class Form
        {
            public static string name = "";
            public static string parent = "Assets";
            public static string presetId;
            public static List<string> tags = new List<string>();
            public static HashSet<string> checkedIds;
            public static string checkedFor;

            public static void Reset()
            {
                name = "";
                tags = new List<string>();
                checkedIds = null;
                checkedFor = null;
            }
        }

        string selectedId;
        string filter = "";
        VisualElement listBox;
        VisualElement detail;
        string successFor;
        int successCount;

        public ProjectsPage(string selectId = null)
        {
            selectedId = selectId;
        }

        public override void Build()
        {
            if (selectedId == null) selectedId = AODatabase.Projects.Count == 0 ? NewKey : AODatabase.Projects[0].id;

            var split = UIKit.Div("ao-split");
            root.Add(split);

            var listPane = UIKit.Div("ao-list-pane");
            var head = UIKit.Div("ao-list-pane-head");
            head.Add(UIKit.Text("プロジェクト", "ao-list-title"));
            var add = UIKit.Btn("Toolbar Plus", "新規", () => Select(NewKey), "small", "primary");
            add.tooltip = "新しいプロジェクトのフォルダーを作る";
            head.Add(add);
            listPane.Add(head);
            var search = new AOTextField("名前・タグで絞り込み");
            search.style.flexGrow = 0;
            search.style.marginBottom = 8;
            search.changed += q =>
            {
                filter = q;
                RenderList();
            };
            listPane.Add(search);
            var listScroll = new ScrollView();
            listScroll.style.flexGrow = 1;
            listBox = listScroll.contentContainer;
            listPane.Add(listScroll);
            split.Add(listPane);

            detail = UIKit.Div("ao-detail-pane");
            split.Add(detail);

            RenderList();
            RenderDetail();
        }

        public override void OnDataChanged()
        {
            RenderList();
        }

        void Select(string id)
        {
            selectedId = id;
            successFor = null;
            RenderList();
            RenderDetail();
        }

        void RenderList()
        {
            listBox.Clear();
            var newItem = UIKit.Div("ao-list-item");
            UIKit.SetClass(newItem, "is-active", selectedId == NewKey);
            var nr = UIKit.Row();
            nr.Add(UIKit.Img("Toolbar Plus"));
            nr.Add(UIKit.Text("新しいプロジェクトを作る", "ao-list-item-title", "ao-ml6"));
            newItem.Add(nr);
            newItem.RegisterCallback<ClickEvent>(_ => Select(NewKey));
            listBox.Add(newItem);

            var tokens = SearchUtil.Tokens(filter);
            var items = AODatabase.Projects.Where(p =>
                tokens.Length == 0 || SearchUtil.Contains(SearchUtil.Normalize(p.name + " " + string.Join(" ", p.tags)), tokens)).ToList();
            if (AODatabase.Projects.Count == 0)
            {
                listBox.Add(UIKit.Text("まだプロジェクトはありません。\n上の「新しいプロジェクトを作る」から始めましょう。", "ao-list-empty", "ao-faint"));
                return;
            }
            if (items.Count == 0) listBox.Add(UIKit.Text("一致するプロジェクトはありません", "ao-list-empty", "ao-faint"));
            foreach (var p in items)
            {
                var proj = p;
                var item = UIKit.Div("ao-list-item");
                UIKit.SetClass(item, "is-active", selectedId == p.id);
                item.Add(UIKit.Text(p.name, "ao-list-item-title"));
                var path = p.RootPath;
                item.Add(UIKit.Text(path ?? "（フォルダーが見つかりません）", "ao-list-item-sub"));
                if (p.tags.Count > 0)
                {
                    var tags = TagField.ReadOnly(p.tags, 3);
                    tags.style.marginTop = 4;
                    item.Add(tags);
                }
                item.RegisterCallback<ClickEvent>(_ => Select(proj.id));
                listBox.Add(item);
            }
        }

        void RenderDetail()
        {
            detail.Clear();
            if (selectedId == NewKey)
            {
                BuildNewForm();
                return;
            }
            var p = AODatabase.FindProject(selectedId);
            if (p == null)
            {
                selectedId = NewKey;
                BuildNewForm();
                return;
            }
            BuildProjectDetail(p);
        }

        // ───────────────────────── 新規作成 ─────────────────────────

        void BuildNewForm()
        {
            var scroll = UIKit.PageScroll();
            detail.Add(scroll);
            var c = scroll.contentContainer;

            c.Add(UIKit.Text("新しいプロジェクト", "ao-page-title"));
            c.Add(UIKit.Text("Assets の中に、案件用のフォルダー一式を作ります。", "ao-page-sub"));
            c.Add(UIKit.Hint("projects.intro", "入力するのは「名前」だけ",
                "あとはチェックを付け外しして「フォルダーを作成」を押すだけです。フォルダー名はプリセットで決まるので、綴りを調べたり Texture / Textures の揺れを気にする必要はありません。"));

            var presets = AODatabase.Presets;
            var preset = AODatabase.FindPreset(Form.presetId) ?? presets.FirstOrDefault();
            Form.presetId = preset?.id;

            // ローカル関数から参照するので先に宣言しておく
            Button resetParent = null, create = null;
            Label summary = null;
            FolderTreeView tree = null;

            // ① 名前
            var nameCard = UIKit.Card();
            nameCard.Add(UIKit.SectionHead("1", "名前", "Assets の下に作る、いちばん上のフォルダーの名前です（例：productC、kaisyaB）"));
            var nameField = new AOTextField("例：productC", Form.name, true);
            nameCard.Add(nameField);
            var nameMsg = UIKit.Text("", "ao-field-help", "ao-small");
            nameCard.Add(nameMsg);
            c.Add(nameCard);

            // ② 作成場所
            var whereCard = UIKit.Card();
            whereCard.Add(UIKit.SectionHead("2", "作成場所", "ふつうは Assets のままで OK。会社ごとにまとめたいときは親フォルダーを選びます"));
            var whereRow = UIKit.Row();
            var pill = UIKit.Div("ao-path-pill");
            pill.Add(UIKit.Img("Folder Icon"));
            var pillLabel = UIKit.Text(Form.parent, "ao-ml6");
            pill.Add(pillLabel);
            pill.tooltip = "Project ウィンドウからフォルダーをドラッグ＆ドロップしても選べます";
            whereRow.Add(pill);
            whereRow.Add(UIKit.Btn("変更…", () =>
            {
                var picked = UIKit.PickAssetsFolder("作成場所（親フォルダー）を選ぶ", Form.parent);
                if (picked != null) SetParent(picked);
            }, "ao-ml8"));
            resetParent = UIKit.Btn("Assets に戻す", () => SetParent("Assets"), "ghost", "ao-ml4");
            whereRow.Add(resetParent);
            whereCard.Add(whereRow);
            // フォルダーが消えた・移動されたプロジェクト（RootPath が null）は候補から外す
            var recent = AODatabase.Projects.Select(p => p.RootPath).Where(r => !string.IsNullOrEmpty(r))
                .Select(r => Path.GetDirectoryName(r)?.Replace('\\', '/'))
                .Where(x => !string.IsNullOrEmpty(x) && x != "Assets" && AssetDatabase.IsValidFolder(x)).Distinct().Take(6).ToList();
            if (recent.Count > 0)
            {
                var quick = UIKit.Row("ao-mt8");
                quick.style.flexWrap = Wrap.Wrap;
                quick.Add(UIKit.Text("よく使う場所：", "ao-faint", "ao-small", "ao-mr6"));
                foreach (var r in recent)
                {
                    var path = r;
                    quick.Add(UIKit.Btn(path, () => SetParent(path), "small", "ao-mr6"));
                }
                whereCard.Add(quick);
            }
            DropZone.AcceptFolderDrop(pill, SetParent);
            c.Add(whereCard);

            // ③ フォルダー構成
            var treeCard = UIKit.Card();
            treeCard.Add(UIKit.SectionHead("3", "フォルダー構成", "必要なフォルダーにチェック。モデルを使う案件なら Models に、など。子にチェックすると親も自動で入ります"));
            var presetRow = UIKit.Row("ao-mb8");
            presetRow.Add(UIKit.Text("プリセット", "ao-dim", "ao-mr8", "ao-nowrap"));
            if (preset != null)
            {
                var popup = new PopupField<FolderPreset>(null, presets, preset, p => p.name + (p.numberPrefix ? "（番号付き）" : ""), p => p.name);
                popup.style.width = 280;
                popup.style.flexGrow = 0;
                popup.RegisterValueChangedCallback(e =>
                {
                    Form.presetId = e.newValue.id;
                    Form.checkedIds = null;
                    RenderDetail();
                });
                presetRow.Add(popup);
            }
            var editBtn = UIKit.Btn("editicon.sml", "編集", () => OpenPresetEditor(preset, false), "ao-ml8");
            editBtn.tooltip = "このプリセットのフォルダーを追加・並べ替え・入れ子の変更をする";
            presetRow.Add(editBtn);
            var more = UIKit.Btn("…", null, "ao-ml4");
            more.tooltip = "プリセットの新規作成・複製・読み込み・書き出し・削除";
            more.clicked += () => PresetMenu(preset, more);
            presetRow.Add(more);
            presetRow.Add(UIKit.Spacer());
            treeCard.Add(presetRow);
            if (preset != null && !string.IsNullOrEmpty(preset.description))
                treeCard.Add(UIKit.Text(preset.description, "ao-faint", "ao-small", "ao-wrap", "ao-mb8"));

            if (preset != null)
            {
                tree = new FolderTreeView(FolderTreeView.Mode.Check);
                tree.SetData(preset.Clone().nodes, RootPath(), preset.numberPrefix);
                if (Form.checkedIds != null && Form.checkedFor == preset.id) tree.Checked.UnionWith(Form.checkedIds);
                else tree.CheckDefaults();
                tree.IsExisting = n => IsValidRoot() && AssetDatabase.IsValidFolder(RootPath() + "/" + FolderBuilder.RelativePath(n, tree.Nodes, preset.numberPrefix));
                tree.Render();
                treeCard.Add(tree);
                var treeActions = UIKit.Row("ao-mt8");
                treeActions.Add(UIKit.Btn("すべてチェック", () =>
                {
                    foreach (var n in tree.Nodes) tree.Checked.Add(n.id);
                    tree.Render();
                    OnTreeChanged();
                }, "small", "ghost"));
                treeActions.Add(UIKit.Btn("プリセットの初期状態に戻す", () =>
                {
                    tree.CheckDefaults();
                    tree.Render();
                    OnTreeChanged();
                }, "small", "ghost", "ao-ml4"));
                treeCard.Add(treeActions);
            }
            else
            {
                treeCard.Add(UIKit.Text("プリセットがありません。「…」から新しいプリセットを作ってください。", "ao-msg-warn"));
            }
            c.Add(treeCard);

            // ④ タグ
            var tagCard = UIKit.Card();
            tagCard.Add(UIKit.SectionHead("4", "タグ（任意）", "あとで名前を忘れても、ここに入れたキーワードで検索できます。日本語の呼び名・案件名・依頼者名などがおすすめ（検索ではひらがな・カタカナ・全角半角の違いは区別しないので、どちらか一方を入れておけば見つかります）"));
            var tagField = new TagField(Form.tags);
            tagField.changed += t => Form.tags = t;
            tagCard.Add(tagField);
            c.Add(tagCard);

            // フッター
            var footer = UIKit.Div("ao-footer-bar");
            summary = UIKit.Text("", "ao-dim", "ao-grow", "ao-wrap");
            footer.Add(summary);
            create = UIKit.Btn("フォルダーを作成", CreateProject, "primary", "big", "ao-ml12");
            footer.Add(create);
            detail.Add(footer);

            nameField.changed += v =>
            {
                Form.name = v;
                tree?.SetRootLabel(DisplayRootLabel());
                Refresh();
            };
            nameField.field.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && create.enabledSelf) CreateProject();
            });
            if (tree != null) tree.Changed += OnTreeChanged;
            if (tree != null) tree.SetRootLabel(DisplayRootLabel());
            Refresh();
            nameField.schedule.Execute(() => UIKit.FocusField(nameField.field)).ExecuteLater(50);

            void SetParent(string folder)
            {
                Form.parent = folder;
                pillLabel.text = folder;
                tree?.SetRootLabel(DisplayRootLabel());
                Refresh();
            }

            void OnTreeChanged()
            {
                Form.checkedIds = new HashSet<string>(tree.Checked);
                Form.checkedFor = preset?.id;
                Refresh();
            }

            void Refresh()
            {
                UIKit.Show(resetParent, Form.parent != "Assets");
                var err = FolderBuilder.ValidateName(Form.name);
                var warn = FolderBuilder.WarnName(Form.name);
                nameField.SetError(err != null && Form.name.Length > 0);
                nameMsg.RemoveFromClassList("ao-msg-error");
                nameMsg.RemoveFromClassList("ao-msg-warn");
                nameMsg.RemoveFromClassList("ao-msg-ok");
                var existingRecord = err == null ? AODatabase.FindProjectByRoot(RootPath()) : null;
                if (Form.name.Length == 0)
                {
                    nameMsg.text = "英数字がおすすめです。";
                }
                else if (err != null)
                {
                    nameMsg.text = err;
                    nameMsg.AddToClassList("ao-msg-error");
                }
                else if (existingRecord != null)
                {
                    nameMsg.text = $"このフォルダーはプロジェクト「{existingRecord.name}」として登録済みです。足りないフォルダーだけ追加します。";
                    nameMsg.AddToClassList("ao-msg-warn");
                }
                else if (AssetDatabase.IsValidFolder(RootPath()))
                {
                    nameMsg.text = "このフォルダーは既にあります。中に足りないフォルダーだけを追加し、プロジェクトとして登録します。";
                    nameMsg.AddToClassList("ao-msg-warn");
                }
                else if (warn != null)
                {
                    nameMsg.text = warn;
                    nameMsg.AddToClassList("ao-msg-warn");
                }
                else
                {
                    nameMsg.text = $"{RootPath()} に作成します";
                    nameMsg.AddToClassList("ao-msg-ok");
                }

                int count = 0, existing = 0;
                if (tree != null)
                {
                    foreach (var n in tree.Nodes)
                    {
                        if (!tree.Checked.Contains(n.id)) continue;
                        if (tree.IsExisting(n)) existing++;
                        else count++;
                    }
                    tree.Render();
                }
                bool rootNew = err == null && !AssetDatabase.IsValidFolder(RootPath());
                int total = count + (rootNew ? 1 : 0);
                create.SetEnabled(err == null && preset != null);
                create.text = total > 0 ? $"フォルダーを作成（{total}）" : "プロジェクトとして登録";
                summary.text = err != null
                    ? "名前を入力すると、作られるフォルダーがここに表示されます。"
                    : total > 0
                        ? $"{RootPath()} に {total} 個のフォルダーを作成します" + (existing > 0 ? $"（既にある {existing} 個はそのまま）" : "")
                        : "作成するフォルダーはありません（すべて既にあります）。プロジェクトとして登録だけ行います。";
            }

            void CreateProject()
            {
                if (preset == null || FolderBuilder.ValidateName(Form.name) != null) return;
                var rootPath = RootPath();
                int created = FolderBuilder.Create(rootPath, tree.Nodes, tree.Checked, preset.numberPrefix);
                var rec = AODatabase.FindProjectByRoot(rootPath);
                bool isNew = rec == null;
                if (isNew)
                {
                    rec = new ProjectRecord
                    {
                        id = AOStorage.NewId(),
                        name = Form.name.Trim(),
                        createdAt = AOStorage.Now(),
                        presetName = preset.name,
                        numberPrefix = preset.numberPrefix,
                        structure = tree.Nodes.Select(n => n.Clone()).ToList(),
                    };
                }
                rec.root = new AssetRef(rootPath);
                foreach (var t in Form.tags)
                    if (!rec.tags.Contains(t)) rec.tags.Add(t);
                AODatabase.SaveProject(rec);
                Form.Reset();
                selectedId = rec.id;
                successFor = rec.id;
                successCount = created;
                RenderList();
                RenderDetail();
                UIKit.Reveal(rootPath);
                window.Toast($"「{rec.name}」を作成しました（{created} フォルダー）");
            }
        }

        static string RootPath() => (Form.parent == "Assets" ? "Assets" : Form.parent) + "/" + (Form.name ?? "").Trim();
        static bool IsValidRoot() => FolderBuilder.ValidateName(Form.name) == null;
        static string DisplayRootLabel() => string.IsNullOrWhiteSpace(Form.name) ? "（名前）" : RootPath();

        void OpenPresetEditor(FolderPreset preset, bool asNew)
        {
            if (preset == null && !asNew) return;
            var editing = asNew
                ? new FolderPreset { id = AOStorage.NewId(), name = preset != null ? preset.name + " のコピー" : "新しいプリセット", nodes = preset?.Clone().nodes ?? new List<PresetNode>() }
                : preset.Clone();
            if (asNew)
            {
                editing.numberPrefix = preset?.numberPrefix ?? false;
                editing.description = preset?.description ?? "";
                // 複製は ID を振り直す
                var map = new Dictionary<string, string>();
                foreach (var n in editing.nodes) map[n.id] = AOStorage.NewId();
                foreach (var n in editing.nodes)
                {
                    n.id = map[n.id];
                    if (!string.IsNullOrEmpty(n.parentId) && map.TryGetValue(n.parentId, out var np)) n.parentId = np;
                }
                editing.filePath = null;
            }
            window.Show(PresetEditorPage.ForPreset(editing, asNew, saved =>
            {
                Form.presetId = saved.id;
                Form.checkedIds = null;
                window.Show(new ProjectsPage(NewKey));
            }, () => window.Show(new ProjectsPage(NewKey))));
        }

        void PresetMenu(FolderPreset preset, VisualElement anchor)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("新しいプリセットを作る（空）"), false, () =>
            {
                window.Show(PresetEditorPage.ForPreset(new FolderPreset { id = AOStorage.NewId(), name = "新しいプリセット" }, true, saved =>
                {
                    Form.presetId = saved.id;
                    Form.checkedIds = null;
                    window.Show(new ProjectsPage(NewKey));
                }, () => window.Show(new ProjectsPage(NewKey))));
            });
            if (preset != null)
            {
                menu.AddItem(new GUIContent("このプリセットを複製して編集"), false, () => OpenPresetEditor(preset, true));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("JSON に書き出す（人に渡す）…"), false, () =>
                {
                    var path = EditorUtility.SaveFilePanel("プリセットを書き出す", "", AOStorage.SafeFileName(preset.name) + AOStorage.PresetExt, "json");
                    if (string.IsNullOrEmpty(path)) return;
                    AODatabase.ExportJson(preset, path);
                    window.Toast("書き出しました", "", "場所を開く", () => EditorUtility.RevealInFinder(path));
                });
            }
            menu.AddItem(new GUIContent("JSON から読み込む（もらったものを使う）…"), false, () =>
            {
                var path = EditorUtility.OpenFilePanel("プリセットを読み込む", "", "json");
                if (string.IsNullOrEmpty(path)) return;
                var msg = AODatabase.ImportJson(path, out var imported);
                if (msg == null) return;
                if (imported is FolderPreset fp)
                {
                    Form.presetId = fp.id;
                    Form.checkedIds = null;
                    RenderDetail();
                }
                window.Toast(msg, imported == null ? "warn" : "");
            });
            menu.AddSeparator("");
            if (preset != null)
            {
                menu.AddItem(new GUIContent("このプリセットを削除"), false, () =>
                {
                    if (AODatabase.Presets.Count <= 1)
                    {
                        EditorUtility.DisplayDialog("Asset Organizer", "最後の 1 つは削除できません。", "OK");
                        return;
                    }
                    if (!EditorUtility.DisplayDialog("プリセットを削除", $"「{preset.name}」を削除します。作成済みのフォルダーには影響しません。", "削除", "やめる")) return;
                    AODatabase.Delete(preset);
                    Form.presetId = null;
                    RenderDetail();
                });
            }
            menu.AddItem(new GUIContent("はじめから入っているプリセットを復元"), false, () =>
            {
                AODatabase.RestoreDefaultPresets();
                RenderDetail();
                window.Toast("初期プリセットを復元しました");
            });
            menu.AddItem(new GUIContent("プリセットの保存場所を開く"), false, () =>
            {
                Directory.CreateDirectory(AOStorage.PresetsDir);
                EditorUtility.RevealInFinder(AOStorage.PresetsDir + "/");
            });
            menu.DropDown(anchor.worldBound);
        }

        // ───────────────────────── 作成済みプロジェクト ─────────────────────────

        void BuildProjectDetail(ProjectRecord p)
        {
            var scroll = UIKit.PageScroll();
            detail.Add(scroll);
            var c = scroll.contentContainer;
            var rootPath = p.RootPath;

            if (successFor == p.id)
            {
                var ok = UIKit.Card("accent");
                ok.Add(UIKit.SectionHead("✓", $"「{p.name}」を作成しました", successCount > 0 ? $"{successCount} 個のフォルダーを作りました。次は、書き出す中身を「プロファイル」として登録しましょう。" : "プロジェクトとして登録しました。"));
                var actions = UIKit.Div("ao-btn-group");
                actions.Add(UIKit.Btn("このプロジェクトのプロファイルを作る", () => window.Show(new ProfileWizardPage(null, p.id)), "primary"));
                actions.Add(UIKit.Btn("Project ウィンドウで表示", () => UIKit.Reveal(rootPath)));
                actions.Add(UIKit.Btn("続けてもう 1 つ作る", () => Select(NewKey), "ghost"));
                ok.Add(actions);
                c.Add(ok);
            }

            var head = UIKit.Div("ao-page-head");
            head.Add(UIKit.Img("FolderOpened Icon"));
            head.Add(UIKit.Text(p.name, "ao-page-title", "ao-ml8", "ao-grow"));
            var reveal = UIKit.Btn("Search Icon", "Project で表示", () => UIKit.Reveal(rootPath));
            reveal.SetEnabled(rootPath != null);
            head.Add(reveal);
            var more = UIKit.Btn("…", null, "ao-ml4");
            more.clicked += () =>
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("JSON に書き出す…"), false, () =>
                {
                    var path = EditorUtility.SaveFilePanel("プロジェクト記録を書き出す", "", AOStorage.SafeFileName(p.name) + AOStorage.ProjectExt, "json");
                    if (!string.IsNullOrEmpty(path)) AODatabase.ExportJson(p, path);
                });
                menu.AddItem(new GUIContent("このフォルダー構成をプリセットとして保存"), false, () =>
                {
                    var preset = new FolderPreset
                    {
                        id = AOStorage.NewId(),
                        name = p.name + " の構成",
                        numberPrefix = p.numberPrefix,
                        nodes = p.structure.Select(n => n.Clone()).ToList(),
                    };
                    AODatabase.SavePreset(preset);
                    window.Toast($"プリセット「{preset.name}」を保存しました");
                });
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("記録を削除（フォルダーは消えません）"), false, () =>
                {
                    if (!EditorUtility.DisplayDialog("記録を削除", $"「{p.name}」の記録を一覧から削除します。\nフォルダーやファイルは削除されません。", "削除", "やめる")) return;
                    AODatabase.Delete(p);
                    selectedId = null;
                    window.Show(new ProjectsPage());
                });
                menu.DropDown(more.worldBound);
            };
            head.Add(more);
            c.Add(head);
            c.Add(UIKit.Text(rootPath ?? p.root.path, "ao-page-sub"));

            if (rootPath == null)
                c.Add(UIKit.Hint(null, "フォルダーが見つかりません", $"「{p.root.path}」が削除されたか、別のプロジェクトに移された可能性があります。", "warn"));

            // タグ
            var tagCard = UIKit.Card();
            tagCard.Add(UIKit.SectionHead(null, "タグ", "検索で見つけるためのキーワード。変更はすぐ保存されます"));
            var tagField = new TagField(p.tags);
            tagField.changed += t =>
            {
                p.tags = t;
                AODatabase.SaveProject(p);
            };
            tagCard.Add(tagField);
            var info = UIKit.Div("ao-mt12");
            info.Add(UIKit.KeyValue("作成日", AOStorage.ShortDate(p.createdAt)));
            info.Add(UIKit.KeyValue("使ったプリセット", string.IsNullOrEmpty(p.presetName) ? "—" : p.presetName + (p.numberPrefix ? "（番号付き）" : "")));
            tagCard.Add(info);
            c.Add(tagCard);

            // フォルダー構成（あとから追加）
            var treeCard = UIKit.Card();
            treeCard.Add(UIKit.SectionHead(null, "フォルダー構成", "あとから必要になったフォルダーにチェックを入れて「作成」。既にあるフォルダーはそのままです"));
            var tree = new FolderTreeView(FolderTreeView.Mode.Check);
            tree.SetData(p.structure, rootPath ?? p.root.path, p.numberPrefix);
            tree.IsExisting = n => rootPath != null && AssetDatabase.IsValidFolder(rootPath + "/" + FolderBuilder.RelativePath(n, p.structure, p.numberPrefix));
            tree.Render();
            treeCard.Add(tree);
            var treeFoot = UIKit.Row("ao-mt8");
            var editStructure = UIKit.Btn("editicon.sml", "構成を編集", () =>
            {
                window.Show(PresetEditorPage.ForProject(p, () => window.Show(new ProjectsPage(p.id))));
            }, "small");
            editStructure.tooltip = "この一覧にないフォルダー（例：Shaders）を追加したいときに";
            treeFoot.Add(editStructure);
            treeFoot.Add(UIKit.Spacer());
            var make = UIKit.Btn("フォルダーを作成", null, "primary");
            treeFoot.Add(make);
            treeCard.Add(treeFoot);
            c.Add(treeCard);

            void RefreshMake()
            {
                int n = p.structure.Count(x => tree.Checked.Contains(x.id) && !tree.IsExisting(x));
                make.text = n > 0 ? $"チェックしたフォルダーを作成（{n}）" : "作成するフォルダーはありません";
                make.SetEnabled(n > 0 && rootPath != null);
            }
            tree.Changed += RefreshMake;
            make.clicked += () =>
            {
                int created = FolderBuilder.Create(rootPath, p.structure, tree.Checked, p.numberPrefix);
                tree.Checked.Clear();
                tree.Render();
                RefreshMake();
                window.Toast($"{created} 個のフォルダーを作成しました");
            };
            RefreshMake();

            // プロファイル
            var profCard = UIKit.Card();
            profCard.Add(UIKit.SectionHead(null, "このプロジェクトのプロファイル", "書き出す中身の登録。ここから作ると、このフォルダーが基準になります"));
            var profiles = AODatabase.ProfilesOf(p);
            if (profiles.Count == 0) profCard.Add(UIKit.Text("まだありません", "ao-faint", "ao-mb8"));
            foreach (var prof in profiles)
            {
                var pr = prof;
                var item = UIKit.Div("ao-list-item");
                var row = UIKit.Row();
                row.Add(UIKit.Img("Prefab Icon"));
                row.Add(UIKit.Text(pr.name, "ao-list-item-title", "ao-ml6", "ao-grow"));
                var h = AODatabase.State.Get(pr.id, false);
                row.Add(UIKit.Text(h != null && !string.IsNullOrEmpty(h.lastVersion) ? "前回 Ver" + h.lastVersion : "未書き出し", "ao-faint", "ao-small"));
                item.Add(row);
                item.RegisterCallback<ClickEvent>(_ => window.Show(new ExportPage(pr.id)));
                profCard.Add(item);
            }
            profCard.Add(UIKit.Btn("Toolbar Plus", "このプロジェクトのプロファイルを作る", () => window.Show(new ProfileWizardPage(null, p.id)), "ao-mt8"));
            c.Add(profCard);
        }
    }
}

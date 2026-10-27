using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
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
            /// <summary>複製元のプロジェクト（その構成を使う）。空ならプリセットを使う。</summary>
            public static string sourceProjectId = "";

            public static void Reset()
            {
                name = "";
                tags = new List<string>();
                checkedIds = null;
                checkedFor = null;
                sourceProjectId = "";
            }
        }

        string selectedId;
        string filter = "";
        VisualElement listBox;
        VisualElement detail;
        string successFor;
        int successCount;
        /// <summary>作成済みプロジェクトの「あとから作るフォルダー」のチェック。</summary>
        HashSet<string> detailChecked = new HashSet<string>();

        public ProjectsPage(string selectId = null)
        {
            selectedId = selectId;
        }

        public override void Build()
        {
            if (selectedId == null) selectedId = ViahDatabase.Projects.Count == 0 ? NewKey : ViahDatabase.Projects[0].id;

            var split = UIKit.Div("viah-split");
            root.Add(split);

            var listPane = UIKit.Div("viah-list-pane");
            var head = UIKit.Div("viah-list-pane-head");
            head.Add(UIKit.Text("プロジェクト", "viah-list-title"));
            var import = UIKit.IconBtn("Import", "JSON から読み込む（人からもらったプロジェクトを使う）", ImportProject, "small", "ghost");
            import.name = "btn-project-import";
            head.Add(import);
            var add = UIKit.Btn("Toolbar Plus", "新規", () => StartNew(), "small", "primary", "viah-ml4");
            add.name = "btn-project-new";
            add.tooltip = "新しいプロジェクトのフォルダーを作る";
            head.Add(add);
            listPane.Add(head);
            var search = new ViahTextField("名前・タグで絞り込み");
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

            detail = UIKit.Div("viah-detail-pane");
            split.Add(detail);

            RenderList();
            RenderDetail();
        }

        public override void OnDataChanged()
        {
            RenderList();
        }

        // ───────────── Undo（入力途中の状態） ─────────────

        [Serializable]
        class UiState
        {
            public string selectedId;
            public string name;
            public string parent;
            public string presetId;
            public List<string> tags = new List<string>();
            public bool hasChecked;
            public List<string> checkedIds = new List<string>();
            public string checkedFor;
            public string sourceProjectId;
            public List<string> detailChecked = new List<string>();
        }

        public override string CaptureState()
        {
            var s = new UiState
            {
                selectedId = selectedId,
                name = Form.name,
                parent = Form.parent,
                presetId = Form.presetId,
                tags = new List<string>(Form.tags),
                hasChecked = Form.checkedIds != null,
                checkedIds = Form.checkedIds != null ? Form.checkedIds.OrderBy(x => x).ToList() : new List<string>(),
                checkedFor = Form.checkedFor,
                sourceProjectId = Form.sourceProjectId,
                detailChecked = detailChecked.OrderBy(x => x).ToList(),
            };
            return JsonUtility.ToJson(s);
        }

        public override void RestoreState(string state)
        {
            var s = JsonUtility.FromJson<UiState>(state);
            selectedId = s.selectedId;
            Form.name = s.name ?? "";
            Form.parent = string.IsNullOrEmpty(s.parent) ? "Assets" : s.parent;
            Form.presetId = s.presetId;
            Form.tags = s.tags ?? new List<string>();
            Form.checkedIds = s.hasChecked ? new HashSet<string>(s.checkedIds) : null;
            Form.checkedFor = s.checkedFor;
            Form.sourceProjectId = s.sourceProjectId ?? "";
            detailChecked = new HashSet<string>(s.detailChecked ?? new List<string>());
            successFor = null;
            RenderList();
            RenderDetail();
        }

        /// <summary>一覧のクリックなど（Undo には積まない）。基準だけ更新する。</summary>
        void Select(string id)
        {
            selectedId = id;
            successFor = null;
            detailChecked.Clear();
            RenderList();
            RenderDetail();
            Committed = CaptureState();
        }

        void StartNew()
        {
            Select(NewKey);
        }

        void ImportProject()
        {
            var path = EditorUtility.OpenFilePanel("プロジェクトを読み込む", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            var msg = ViahDatabase.ImportJson(path, out var imported);
            if (msg == null) return;
            if (imported is ProjectRecord pr) Select(pr.id);
            else if (imported is FolderPreset fp)
            {
                Form.presetId = fp.id;
                Form.checkedIds = null;
                Form.sourceProjectId = "";
                Select(NewKey);
            }
            window.Toast(msg, imported == null ? "warn" : "");
        }

        void RenderList()
        {
            listBox.Clear();
            var tokens = SearchUtil.Tokens(filter);
            var items = ViahDatabase.Projects.Where(p =>
                tokens.Length == 0 || SearchUtil.Contains(SearchUtil.Normalize(p.name + " " + string.Join(" ", p.tags)), tokens)).ToList();
            if (ViahDatabase.Projects.Count == 0)
            {
                listBox.Add(UIKit.Text("まだプロジェクトはありません。\n右上の「＋ 新規」から作れます。\n人からもらったものは「読み込み」ボタンから。", "viah-list-empty", "viah-faint"));
                return;
            }
            if (items.Count == 0) listBox.Add(UIKit.Text("一致するプロジェクトはありません", "viah-list-empty", "viah-faint"));
            foreach (var p in items)
            {
                var proj = p;
                var item = UIKit.Div("viah-list-item");
                UIKit.SetClass(item, "is-active", selectedId == p.id);
                item.Add(UIKit.Text(p.name, "viah-list-item-title"));
                var path = p.RootPath;
                if (path != null) item.Add(UIKit.Text(path, "viah-list-item-sub"));
                else
                {
                    var r = UIKit.Row("viah-mt4");
                    r.Add(UIKit.Badge(string.IsNullOrEmpty(p.unityProject) || p.unityProject == ViahStorage.UnityProjectName
                        ? "フォルダーなし" : "別のUnityプロジェクト", "warn",
                        string.IsNullOrEmpty(p.unityProject) ? "フォルダーが見つかりません" : "作成元：" + p.unityProject));
                    r.Children().First().style.marginLeft = 0;
                    item.Add(r);
                }
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
            var p = ViahDatabase.FindProject(selectedId);
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

            var source = ViahDatabase.FindProject(Form.sourceProjectId);
            if (source == null) Form.sourceProjectId = "";

            c.Add(UIKit.Text(source != null ? $"「{source.name}」を複製して新しいプロジェクトを作る" : "新しいプロジェクト", "viah-page-title"));
            c.Add(UIKit.Text("Assets の中に、案件用のフォルダー一式を作ります。", "viah-page-sub"));
            c.Add(UIKit.Hint("projects.intro", "入力するのは「名前」だけ",
                "あとはチェックを付け外しして「フォルダーを作成」を押すだけです。フォルダー名はプリセットで決まるので、綴りを調べたり Texture / Textures の揺れを気にする必要はありません。"));

            var presets = ViahDatabase.Presets;
            var preset = ViahDatabase.FindPreset(Form.presetId) ?? presets.FirstOrDefault();
            Form.presetId = preset?.id;
            // 構成の元：複製元のプロジェクト or プリセット
            var nodes = source != null ? source.structure : preset?.nodes;
            bool numberPrefix = source != null ? source.numberPrefix : preset != null && preset.numberPrefix;
            var structureKey = source != null ? "project:" + source.id : preset?.id;

            // ローカル関数から参照するので先に宣言しておく
            Button resetParent = null, create = null;
            Label summary = null;
            FolderTreeView tree = null;

            // ① 名前
            var nameCard = UIKit.Card();
            nameCard.name = "card-name";
            nameCard.Add(UIKit.SectionHead("1", "名前", "Assets の下に作る、いちばん上のフォルダーの名前です（例：productC、kaisyaB）"));
            var nameField = new ViahTextField("例：productC", Form.name, true);
            nameField.name = "field-project-name";
            nameCard.Add(nameField);
            var nameMsg = UIKit.Text("", "viah-field-help", "viah-small");
            nameCard.Add(nameMsg);
            c.Add(nameCard);

            // ② 作成場所
            var whereCard = UIKit.Card();
            whereCard.name = "card-where";
            whereCard.Add(UIKit.SectionHead("2", "作成場所", "ふつうは Assets のままで OK。会社ごとにまとめたいときは親フォルダーを選びます"));
            var whereRow = UIKit.Row();
            var pill = UIKit.Div("viah-path-pill");
            pill.Add(UIKit.Img("Folder Icon"));
            var pillLabel = UIKit.Text(Form.parent, "viah-ml6");
            pill.Add(pillLabel);
            pill.tooltip = "Project ウィンドウからフォルダーをドラッグ＆ドロップしても選べます";
            whereRow.Add(pill);
            whereRow.Add(UIKit.Btn("変更…", () =>
            {
                var picked = UIKit.PickAssetsFolder("作成場所（親フォルダー）を選ぶ", Form.parent);
                if (picked != null) SetParent(picked);
            }, "viah-ml8"));
            resetParent = UIKit.Btn("Assets に戻す", () => SetParent("Assets"), "ghost", "viah-ml4");
            whereRow.Add(resetParent);
            whereCard.Add(whereRow);
            // フォルダーが消えた・移動されたプロジェクト（RootPath が null）は候補から外す
            var recent = ViahDatabase.Projects.Select(p => p.RootPath).Where(r => !string.IsNullOrEmpty(r))
                .Select(r => Path.GetDirectoryName(r)?.Replace('\\', '/'))
                .Where(x => !string.IsNullOrEmpty(x) && x != "Assets" && AssetDatabase.IsValidFolder(x)).Distinct().Take(6).ToList();
            if (recent.Count > 0)
            {
                var quick = UIKit.Row("viah-mt8");
                quick.style.flexWrap = Wrap.Wrap;
                quick.Add(UIKit.Text("よく使う場所：", "viah-faint", "viah-small", "viah-mr6"));
                foreach (var r in recent)
                {
                    var path = r;
                    quick.Add(UIKit.Btn(path, () => SetParent(path), "small", "viah-mr6"));
                }
                whereCard.Add(quick);
            }
            DropZone.AcceptFolderDrop(pill, SetParent);
            c.Add(whereCard);

            // ③ フォルダー構成
            var treeCard = UIKit.Card();
            treeCard.name = "card-structure";
            treeCard.Add(UIKit.SectionHead("3", "フォルダー構成", "必要なフォルダーにチェック。モデルを使う案件なら Models に、など。子にチェックすると親も自動で入ります"));
            if (source != null)
            {
                var srcRow = UIKit.Row("viah-mb8");
                srcRow.Add(UIKit.Badge("複製元", "rule"));
                srcRow.Children().First().style.marginLeft = 0;
                srcRow.Add(UIKit.Text($"「{source.name}」のフォルダー構成を使っています", "viah-dim", "viah-ml8", "viah-grow"));
                srcRow.Add(UIKit.Btn("プリセットから選び直す", () =>
                {
                    Form.sourceProjectId = "";
                    Form.checkedIds = null;
                    Commit("複製元をやめる");
                    RenderDetail();
                }, "small", "ghost"));
                treeCard.Add(srcRow);
            }
            else
            {
                var presetRow = UIKit.Row("viah-mb8");
                presetRow.Add(UIKit.Text("プリセット", "viah-dim", "viah-mr8", "viah-nowrap"));
                if (preset != null)
                {
                    var popup = new PopupField<FolderPreset>(null, presets, preset, p => p.name + (p.numberPrefix ? "（番号付き）" : ""), p => p.name);
                    popup.name = "popup-preset";
                    popup.style.width = 280;
                    popup.style.flexGrow = 0;
                    popup.RegisterValueChangedCallback(e =>
                    {
                        Form.presetId = e.newValue.id;
                        Form.checkedIds = null;
                        Commit("プリセットを変更");
                        RenderDetail();
                    });
                    presetRow.Add(popup);
                }
                var editBtn = UIKit.Btn("editicon.sml", "編集", () => OpenPresetEditor(preset, false), "viah-ml8");
                editBtn.name = "btn-preset-edit";
                editBtn.tooltip = "このプリセットのフォルダーを追加・並べ替え・入れ子の変更をする";
                presetRow.Add(editBtn);
                var more = UIKit.Btn("…", null, "viah-ml4");
                more.name = "btn-preset-menu";
                more.tooltip = "プリセットの新規作成・複製・読み込み・書き出し・削除";
                more.clicked += () => PresetMenu(preset, more);
                presetRow.Add(more);
                presetRow.Add(UIKit.Spacer());
                treeCard.Add(presetRow);
                if (preset != null && !string.IsNullOrEmpty(preset.description))
                    treeCard.Add(UIKit.Text(preset.description, "viah-faint", "viah-small", "viah-wrap", "viah-mb8"));
            }

            if (nodes != null)
            {
                tree = new FolderTreeView(FolderTreeView.Mode.Check);
                tree.name = "tree-structure";
                tree.SetData(nodes.Select(n => n.Clone()).ToList(), RootPath(), numberPrefix);
                if (Form.checkedIds != null && Form.checkedFor == structureKey) tree.Checked.UnionWith(Form.checkedIds);
                else tree.CheckDefaults();
                tree.IsExisting = n => IsValidRoot() && AssetDatabase.IsValidFolder(RootPath() + "/" + FolderBuilder.RelativePath(n, tree.Nodes, numberPrefix));
                tree.Render();
                treeCard.Add(tree);
                var treeActions = UIKit.Row("viah-mt8");
                treeActions.Add(UIKit.Btn("すべてチェック", () =>
                {
                    foreach (var n in tree.Nodes) tree.Checked.Add(n.id);
                    tree.Render();
                    OnTreeChanged("すべてチェック");
                }, "small", "ghost"));
                treeActions.Add(UIKit.Btn(source != null ? "初期状態に戻す" : "プリセットの初期状態に戻す", () =>
                {
                    tree.CheckDefaults();
                    tree.Render();
                    OnTreeChanged("チェックを初期状態に戻す");
                }, "small", "ghost", "viah-ml4"));
                treeCard.Add(treeActions);
            }
            else
            {
                treeCard.Add(UIKit.Text("プリセットがありません。「…」から新しいプリセットを作ってください。", "viah-msg-warn"));
            }
            c.Add(treeCard);

            // ④ タグ
            var tagCard = UIKit.Card();
            tagCard.name = "card-tags";
            tagCard.Add(UIKit.SectionHead("4", "タグ（任意）", "あとで名前を忘れても、ここに入れたキーワードで検索できます。日本語の呼び名・案件名・依頼者名などがおすすめ（検索ではひらがな・カタカナ・全角半角の違いは区別しないので、どちらか一方を入れておけば見つかります）"));
            var tagField = new TagField(Form.tags);
            tagField.changed += t =>
            {
                Form.tags = t;
                Commit("タグを変更");
            };
            tagCard.Add(tagField);
            c.Add(tagCard);

            // フッター
            var footer = UIKit.Div("viah-footer-bar");
            summary = UIKit.Text("", "viah-dim", "viah-grow", "viah-wrap");
            footer.Add(summary);
            create = UIKit.Btn("フォルダーを作成", CreateProject, "primary", "big", "viah-ml12");
            create.name = "btn-project-create";
            footer.Add(create);
            detail.Add(footer);

            nameField.changed += v =>
            {
                Form.name = v;
                Commit("名前を入力", "project-name");
                tree?.SetRootLabel(DisplayRootLabel());
                Refresh();
            };
            nameField.field.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && create.enabledSelf) CreateProject();
            });
            if (tree != null) tree.Changed += () => OnTreeChanged("フォルダーのチェックを変更");
            if (tree != null) tree.SetRootLabel(DisplayRootLabel());
            Refresh();
            nameField.schedule.Execute(() => UIKit.FocusField(nameField.field)).ExecuteLater(50);

            void SetParent(string folder)
            {
                Form.parent = folder;
                pillLabel.text = folder;
                Commit("作成場所を変更");
                tree?.SetRootLabel(DisplayRootLabel());
                Refresh();
            }

            void OnTreeChanged(string label)
            {
                Form.checkedIds = new HashSet<string>(tree.Checked);
                Form.checkedFor = structureKey;
                Commit(label);
                Refresh();
            }

            void Refresh()
            {
                UIKit.Show(resetParent, Form.parent != "Assets");
                var err = FolderBuilder.ValidateName(Form.name);
                var warn = FolderBuilder.WarnName(Form.name);
                nameField.SetError(err != null && Form.name.Length > 0);
                nameMsg.RemoveFromClassList("viah-msg-error");
                nameMsg.RemoveFromClassList("viah-msg-warn");
                nameMsg.RemoveFromClassList("viah-msg-ok");
                var existingRecord = err == null ? ViahDatabase.FindProjectByRoot(RootPath()) : null;
                if (Form.name.Length == 0)
                {
                    nameMsg.text = "英数字がおすすめです。";
                }
                else if (err != null)
                {
                    nameMsg.text = err;
                    nameMsg.AddToClassList("viah-msg-error");
                }
                else if (existingRecord != null)
                {
                    nameMsg.text = $"このフォルダーはプロジェクト「{existingRecord.name}」として登録済みです。足りないフォルダーだけ追加します。";
                    nameMsg.AddToClassList("viah-msg-warn");
                }
                else if (AssetDatabase.IsValidFolder(RootPath()))
                {
                    nameMsg.text = "このフォルダーは既にあります。中に足りないフォルダーだけを追加し、プロジェクトとして登録します。";
                    nameMsg.AddToClassList("viah-msg-warn");
                }
                else if (warn != null)
                {
                    nameMsg.text = warn;
                    nameMsg.AddToClassList("viah-msg-warn");
                }
                else
                {
                    nameMsg.text = $"{RootPath()} に作成します";
                    nameMsg.AddToClassList("viah-msg-ok");
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
                create.SetEnabled(err == null && nodes != null);
                create.text = total > 0 ? $"フォルダーを作成（{total}）" : "プロジェクトとして登録";
                summary.text = err != null
                    ? "名前を入力すると、作られるフォルダーがここに表示されます。"
                    : total > 0
                        ? $"{RootPath()} に {total} 個のフォルダーを作成します" + (existing > 0 ? $"（既にある {existing} 個はそのまま）" : "")
                        : "作成するフォルダーはありません（すべて既にあります）。プロジェクトとして登録だけ行います。";
            }

            void CreateProject()
            {
                if (nodes == null || FolderBuilder.ValidateName(Form.name) != null) return;
                var rootPath = RootPath();
                int created = 0;
                ProjectRecord rec = null;
                UndoHub.Data($"「{Form.name.Trim()}」を作成", () =>
                {
                    created = FolderBuilder.Create(rootPath, tree.Nodes, tree.Checked, numberPrefix);
                    rec = ViahDatabase.FindProjectByRoot(rootPath);
                    if (rec == null)
                    {
                        rec = new ProjectRecord
                        {
                            id = ViahStorage.NewId(),
                            name = Form.name.Trim(),
                            createdAt = ViahStorage.Now(),
                            presetName = source != null ? source.presetName : preset.name,
                            numberPrefix = numberPrefix,
                            structure = tree.Nodes.Select(n => n.Clone()).ToList(),
                            unityProject = ViahStorage.UnityProjectName,
                        };
                    }
                    rec.root = new AssetRef(rootPath);
                    foreach (var t in Form.tags)
                        if (!rec.tags.Contains(t)) rec.tags.Add(t);
                    ViahDatabase.SaveProject(rec);
                });
                Form.Reset();
                selectedId = rec.id;
                successFor = rec.id;
                successCount = created;
                detailChecked.Clear();
                Commit("プロジェクトを作成");
                RenderList();
                RenderDetail();
                UIKit.Reveal(rootPath);
                window.Toast($"「{rec.name}」を作成しました（{created} フォルダー）。Ctrl+Z で取り消せます");
            }
        }

        static string RootPath() => (Form.parent == "Assets" ? "Assets" : Form.parent) + "/" + (Form.name ?? "").Trim();
        static bool IsValidRoot() => FolderBuilder.ValidateName(Form.name) == null;
        static string DisplayRootLabel() => string.IsNullOrWhiteSpace(Form.name) ? "（名前）" : RootPath();

        void OpenPresetEditor(FolderPreset preset, bool asNew)
        {
            if (preset == null && !asNew) return;
            var editing = asNew
                ? new FolderPreset { id = ViahStorage.NewId(), name = preset != null ? preset.name + " のコピー" : "新しいプリセット", nodes = preset?.Clone().nodes ?? new List<PresetNode>() }
                : preset.Clone();
            if (asNew)
            {
                editing.numberPrefix = preset?.numberPrefix ?? false;
                editing.description = preset?.description ?? "";
                // 複製は ID を振り直す
                var map = new Dictionary<string, string>();
                foreach (var n in editing.nodes) map[n.id] = ViahStorage.NewId();
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
                Form.sourceProjectId = "";
                window.Show(new ProjectsPage(NewKey));
            }, () => window.Show(new ProjectsPage(NewKey))));
        }

        void PresetMenu(FolderPreset preset, VisualElement anchor)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("新しいプリセットを作る（空）"), false, () =>
            {
                window.Show(PresetEditorPage.ForPreset(new FolderPreset { id = ViahStorage.NewId(), name = "新しいプリセット" }, true, saved =>
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
                    var path = EditorUtility.SaveFilePanel("プリセットを書き出す", "", ViahStorage.SafeFileName(preset.name) + ViahStorage.PresetExt, "json");
                    if (string.IsNullOrEmpty(path)) return;
                    ViahDatabase.ExportJson(preset, path);
                    window.Toast("書き出しました", "", "場所を開く", () => ViahStorage.RevealFile(path));
                });
            }
            menu.AddItem(new GUIContent("JSON から読み込む（もらったものを使う）…"), false, () =>
            {
                var path = EditorUtility.OpenFilePanel("プリセットを読み込む", "", "json");
                if (string.IsNullOrEmpty(path)) return;
                var msg = ViahDatabase.ImportJson(path, out var imported);
                if (msg == null) return;
                if (imported is FolderPreset fp)
                {
                    Form.presetId = fp.id;
                    Form.checkedIds = null;
                    RenderDetail();
                    Committed = CaptureState();
                }
                window.Toast(msg, imported == null ? "warn" : "");
            });
            menu.AddSeparator("");
            if (preset != null)
            {
                menu.AddItem(new GUIContent("このプリセットを削除"), false, () =>
                {
                    if (ViahDatabase.Presets.Count <= 1)
                    {
                        EditorUtility.DisplayDialog("Avatar Hub", "最後の 1 つは削除できません。", "OK");
                        return;
                    }
                    if (!EditorUtility.DisplayDialog("プリセットを削除", $"「{preset.name}」を削除します。作成済みのフォルダーには影響しません。\n（Ctrl+Z で元に戻せます）", "削除", "やめる")) return;
                    ViahDatabase.Delete(preset);
                    Form.presetId = null;
                    RenderDetail();
                    Committed = CaptureState();
                });
            }
            menu.AddItem(new GUIContent("はじめから入っているプリセットを復元"), false, () =>
            {
                ViahDatabase.RestoreDefaultPresets();
                RenderDetail();
                window.Toast("初期プリセットを復元しました");
            });
            menu.AddItem(new GUIContent("プリセットの保存場所を開く"), false, () => ViahStorage.OpenFolder(ViahStorage.PresetsDir));
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
                var actions = UIKit.Div("viah-btn-group");
                actions.Add(UIKit.Btn("このプロジェクトのプロファイルを作る", () => window.Show(new ProfileWizardPage(null, p.id)), "primary"));
                actions.Add(UIKit.Btn("Project ウィンドウで表示", () => UIKit.Reveal(rootPath)));
                actions.Add(UIKit.Btn("続けてもう 1 つ作る", () => StartNew(), "ghost"));
                ok.Add(actions);
                c.Add(ok);
            }

            var head = UIKit.Div("viah-page-head");
            head.Add(UIKit.Img("FolderOpened Icon"));
            head.Add(UIKit.Text(p.name, "viah-page-title", "viah-ml8", "viah-grow"));
            var tools = UIKit.Row("viah-page-head-tools");
            head.Add(tools);
            var reveal = UIKit.Btn("Search Icon", "Project で表示", () => UIKit.Reveal(rootPath));
            reveal.SetEnabled(rootPath != null);
            tools.Add(reveal);
            var share = UIKit.Btn("SaveAs", "JSONで渡す", () => ShareProject(p), "viah-ml4");
            share.name = "btn-project-share";
            share.tooltip = "このプロジェクト（フォルダー構成とタグ）を JSON に書き出して、人に渡せるようにします";
            tools.Add(share);
            var more = UIKit.Btn("…", null, "viah-ml4");
            more.name = "btn-project-menu";
            more.clicked += () =>
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("複製して新しいプロジェクトを作る"), false, () => Duplicate(p));
                menu.AddItem(new GUIContent("このフォルダー構成をプリセットとして保存"), false, () =>
                {
                    var preset = new FolderPreset
                    {
                        id = ViahStorage.NewId(),
                        name = p.name + " の構成",
                        numberPrefix = p.numberPrefix,
                        nodes = p.structure.Select(n => n.Clone()).ToList(),
                    };
                    ViahDatabase.SavePreset(preset, $"プリセット「{preset.name}」を保存");
                    window.Toast($"プリセット「{preset.name}」を保存しました");
                });
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("記録を削除（フォルダーは消えません）"), false, () =>
                {
                    if (!EditorUtility.DisplayDialog("記録を削除", $"「{p.name}」の記録を一覧から削除します。\nフォルダーやファイルは削除されません。（Ctrl+Z で元に戻せます）", "削除", "やめる")) return;
                    ViahDatabase.Delete(p, $"プロジェクト「{p.name}」の記録を削除");
                    Select(ViahDatabase.Projects.Count == 0 ? NewKey : ViahDatabase.Projects[0].id);
                });
                menu.DropDown(more.worldBound);
            };
            tools.Add(more);
            c.Add(head);
            c.Add(UIKit.Text(rootPath ?? p.root.path, "viah-page-sub"));

            if (rootPath == null)
            {
                bool other = !string.IsNullOrEmpty(p.unityProject) && p.unityProject != ViahStorage.UnityProjectName;
                var box = UIKit.Card("warn-card");
                box.Add(UIKit.SectionHead(null, other ? $"このプロジェクトは別の Unity プロジェクト（{p.unityProject}）で作られました" : "フォルダーが見つかりません",
                    other ? "この Unity プロジェクトにはまだフォルダーがありません。同じ構成で作成できます。" : $"「{p.root.path}」が削除されたか、移動された可能性があります。"));
                var row = UIKit.Div("viah-btn-group");
                var here = UIKit.Btn("Toolbar Plus", $"{p.root.path} に作成", () => CreateHere(p, p.root.path), "primary");
                here.name = "btn-project-create-here";
                here.SetEnabled(!string.IsNullOrEmpty(p.root.path) && p.root.path.StartsWith("Assets/"));
                row.Add(here);
                row.Add(UIKit.Btn("場所を選んで作成…", () =>
                {
                    var parent = UIKit.PickAssetsFolder("作成場所（親フォルダー）を選ぶ", "Assets");
                    if (parent != null) CreateHere(p, parent + "/" + p.name);
                }));
                box.Add(row);
                c.Add(box);
            }

            // タグ
            var tagCard = UIKit.Card();
            tagCard.name = "card-detail-tags";
            tagCard.Add(UIKit.SectionHead(null, "タグ", "検索で見つけるためのキーワード。変更はすぐ保存されます"));
            var tagField = new TagField(p.tags);
            tagField.changed += t =>
            {
                p.tags = t;
                ViahDatabase.SaveProject(p, "タグを変更", "tags:" + p.id);
            };
            tagCard.Add(tagField);
            var info = UIKit.Div("viah-mt12");
            info.Add(UIKit.KeyValue("作成日", ViahStorage.ShortDate(p.createdAt)));
            info.Add(UIKit.KeyValue("作成した場所", string.IsNullOrEmpty(p.unityProject) ? "—" : p.unityProject));
            info.Add(UIKit.KeyValue("使ったプリセット", string.IsNullOrEmpty(p.presetName) ? "—" : p.presetName + (p.numberPrefix ? "（番号付き）" : "")));
            tagCard.Add(info);
            c.Add(tagCard);

            // フォルダー構成（あとから追加）
            var treeCard = UIKit.Card();
            treeCard.name = "card-detail-structure";
            treeCard.Add(UIKit.SectionHead(null, "フォルダー構成", "あとから必要になったフォルダーにチェックを入れて「作成」。既にあるフォルダーはそのままです"));
            var tree = new FolderTreeView(FolderTreeView.Mode.Check);
            tree.name = "tree-detail";
            tree.SetData(p.structure, rootPath ?? p.root.path, p.numberPrefix);
            tree.IsExisting = n => rootPath != null && AssetDatabase.IsValidFolder(rootPath + "/" + FolderBuilder.RelativePath(n, p.structure, p.numberPrefix));
            tree.Checked.UnionWith(detailChecked);
            tree.Render();
            treeCard.Add(tree);
            var treeFoot = UIKit.Row("viah-mt8");
            var editStructure = UIKit.Btn("editicon.sml", "構成を編集", () =>
            {
                window.Show(PresetEditorPage.ForProject(p, () => window.Show(new ProjectsPage(p.id))));
            }, "small");
            editStructure.name = "btn-project-edit-structure";
            editStructure.tooltip = "この一覧にないフォルダー（例：Shaders）を追加したいときに";
            treeFoot.Add(editStructure);
            treeFoot.Add(UIKit.Spacer());
            var make = UIKit.Btn("フォルダーを作成", null, "primary");
            make.name = "btn-project-make";
            treeFoot.Add(make);
            treeCard.Add(treeFoot);
            c.Add(treeCard);

            void RefreshMake()
            {
                int n = p.structure.Count(x => tree.Checked.Contains(x.id) && !tree.IsExisting(x));
                make.text = n > 0 ? $"チェックしたフォルダーを作成（{n}）" : "作成するフォルダーはありません";
                make.SetEnabled(n > 0 && rootPath != null);
            }
            tree.Changed += () =>
            {
                detailChecked = new HashSet<string>(tree.Checked);
                Commit("作るフォルダーのチェックを変更");
                RefreshMake();
            };
            make.clicked += () =>
            {
                int created = 0;
                UndoHub.Data("フォルダーを作成", () => created = FolderBuilder.Create(rootPath, p.structure, tree.Checked, p.numberPrefix));
                tree.Checked.Clear();
                detailChecked.Clear();
                Commit("フォルダーを作成");
                tree.Render();
                RefreshMake();
                window.Toast($"{created} 個のフォルダーを作成しました。Ctrl+Z で取り消せます");
            };
            RefreshMake();

            // プロファイル
            var profCard = UIKit.Card();
            profCard.Add(UIKit.SectionHead(null, "このプロジェクトのプロファイル", "書き出す中身の登録。ここから作ると、このフォルダーが基準になります"));
            var profiles = ViahDatabase.ProfilesOf(p);
            if (profiles.Count == 0) profCard.Add(UIKit.Text("まだありません", "viah-faint", "viah-mb8"));
            foreach (var prof in profiles)
            {
                var pr = prof;
                var item = UIKit.Div("viah-list-item");
                var row = UIKit.Row();
                row.Add(UIKit.Img("Prefab Icon"));
                row.Add(UIKit.Text(pr.name, "viah-list-item-title", "viah-ml6", "viah-grow"));
                var h = ViahDatabase.History.Get(pr.id, false);
                row.Add(UIKit.Text(h != null && !string.IsNullOrEmpty(h.lastVersion) ? "前回 Ver" + h.lastVersion : "未書き出し", "viah-faint", "viah-small"));
                item.Add(row);
                item.RegisterCallback<ClickEvent>(_ => window.Show(new ExportPage(pr.id)));
                profCard.Add(item);
            }
            profCard.Add(UIKit.Btn("Toolbar Plus", "このプロジェクトのプロファイルを作る", () => window.Show(new ProfileWizardPage(null, p.id)), "viah-mt8"));
            c.Add(profCard);
        }

        void ShareProject(ProjectRecord p)
        {
            var path = EditorUtility.SaveFilePanel("プロジェクトを JSON に書き出す", "", ViahStorage.SafeFileName(p.name) + ViahStorage.ProjectExt, "json");
            if (string.IsNullOrEmpty(path)) return;
            ViahDatabase.ExportJson(p, path);
            window.Toast("書き出しました。このファイルを相手に渡し、相手は「プロジェクト作成 → 読み込み」で取り込みます", "", "場所を開く", () => ViahStorage.RevealFile(path));
        }

        /// <summary>このプロジェクトの構成とタグを引き継いで、新規作成フォームを開く。</summary>
        void Duplicate(ProjectRecord p)
        {
            Form.Reset();
            Form.sourceProjectId = p.id;
            Form.tags = new List<string>(p.tags);
            var parent = Path.GetDirectoryName(p.RootPath ?? p.root.path ?? "")?.Replace('\\', '/');
            Form.parent = !string.IsNullOrEmpty(parent) && AssetDatabase.IsValidFolder(parent) ? parent : "Assets";
            Select(NewKey);
            window.Toast($"「{p.name}」の構成とタグを引き継ぎました。新しい名前を入れてください");
        }

        /// <summary>別の Unity プロジェクトで作られたプロジェクトのフォルダーを、ここに作る。</summary>
        void CreateHere(ProjectRecord p, string rootPath)
        {
            if (FolderBuilder.ValidateName(Path.GetFileName(rootPath)) != null) return;
            int created = 0;
            UndoHub.Data($"「{p.name}」のフォルダーを作成", () =>
            {
                var on = p.structure.Where(n => n.defaultOn).Select(n => n.id).ToList();
                created = FolderBuilder.Create(rootPath, p.structure, on, p.numberPrefix);
                p.root = new AssetRef(rootPath);
                ViahDatabase.SaveProject(p);
            });
            RenderList();
            RenderDetail();
            UIKit.Reveal(rootPath);
            window.Toast($"{created} 個のフォルダーを作成しました。Ctrl+Z で取り消せます");
        }
    }
}

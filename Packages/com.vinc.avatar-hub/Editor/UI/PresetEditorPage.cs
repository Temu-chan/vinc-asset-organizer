using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>
    /// フォルダー構成のエディットモード。プリセット、または作成済みプロジェクトの構成を編集する。
    /// </summary>
    public class PresetEditorPage : Page
    {
        public override string Section => "projects";

        FolderPreset preset;
        ProjectRecord project;
        bool isNew;
        Action<FolderPreset> onSavedPreset;
        Action onDone;
        Action onCancel;

        FolderTreeView tree;
        ViahTextField nameField;
        Button saveBtn;
        Button[] selectionButtons;
        bool dirty;

        public static PresetEditorPage ForPreset(FolderPreset editing, bool isNew, Action<FolderPreset> onSaved, Action onCancel)
        {
            return new PresetEditorPage { preset = editing, isNew = isNew, onSavedPreset = onSaved, onCancel = onCancel };
        }

        public static PresetEditorPage ForProject(ProjectRecord rec, Action onDone)
        {
            var copy = new FolderPreset
            {
                id = rec.id,
                name = rec.name,
                numberPrefix = rec.numberPrefix,
                nodes = rec.structure.Select(n => n.Clone()).ToList(),
            };
            return new PresetEditorPage { preset = copy, project = rec, onDone = onDone, onCancel = onDone };
        }

        public override void Build()
        {
            var scroll = UIKit.PageScroll();
            root.Add(scroll);
            var c = scroll.contentContainer;

            var back = UIKit.Row("viah-back");
            back.Add(UIKit.Text("‹ 戻る"));
            UIKit.Clickable(back, Cancel);
            c.Add(back);

            c.Add(UIKit.Text(project != null ? $"「{project.name}」のフォルダー構成を編集" : isNew ? "新しいプリセット" : "プリセットを編集", "viah-page-title"));
            c.Add(UIKit.Text(project != null
                ? "このプロジェクトで使える（あとから作成できる）フォルダーの一覧を変更します。実際のフォルダーは、保存後にチェックして作成します。"
                : "フォルダーの追加・名前の変更・並べ替え・入れ子の変更ができます。保存すると、次からこの構成でプロジェクトを作れます。", "viah-page-sub"));
            c.Add(UIKit.Hint("preset.edit", "操作のしかた",
                "行の左の「⋮」をドラッグすると移動できます（行の上下＝前後へ、行の真ん中＝そのフォルダーの中へ）。ボタンの ↑↓ で順番、← → で階層を変えられます。ダブルクリック / F2 で名前を変更、Delete で削除。"));

            // 設定
            if (project == null)
            {
                var settings = UIKit.Card();
                settings.Add(UIKit.SectionHead(null, "プリセットの設定"));
                var row1 = UIKit.Row("viah-mb8");
                var l1 = UIKit.Text("名前", "viah-kv-key");
                row1.Add(l1);
                nameField = new ViahTextField("例：VRChatギミック標準", preset.name);
                nameField.changed += v =>
                {
                    preset.name = v;
                    MarkDirty();
                    Commit("プリセット名を入力", "preset-name");
                };
                row1.Add(nameField);
                settings.Add(row1);
                var row2 = UIKit.Row("viah-mb8");
                row2.Add(UIKit.Text("説明", "viah-kv-key"));
                var desc = new ViahTextField("どんなときに使う構成か（任意）", preset.description);
                desc.changed += v =>
                {
                    preset.description = v;
                    MarkDirty();
                    Commit("説明を入力", "preset-desc");
                };
                row2.Add(desc);
                settings.Add(row2);
                settings.Add(NumberSwitch());
                c.Add(settings);
            }
            else
            {
                var settings = UIKit.Card();
                settings.Add(NumberSwitch());
                c.Add(settings);
            }

            // ツリー
            var treeCard = UIKit.Card();
            var toolbar = UIKit.Div("viah-toolbar");
            toolbar.Add(UIKit.Btn("Toolbar Plus", "フォルダーを追加", () => tree.AddChild(), "primary", "small").With(b => b.tooltip = "選択中のフォルダーの中に追加（いちばん上の行を選んでいれば、いちばん上の階層に追加）"));
            toolbar.Add(UIKit.Btn("同じ階層に追加", () => tree.AddSibling(), "small", "viah-ml4").With(b => b.tooltip = "選択中のフォルダーのすぐ下に、同じ階層で追加"));
            toolbar.Add(UIKit.Div().With(d => d.style.width = 12));
            var up = UIKit.Btn("↑", () => tree.MoveSelected(-1), "small").With(b => b.tooltip = "上へ（Alt+↑）");
            var down = UIKit.Btn("↓", () => tree.MoveSelected(+1), "small", "viah-ml4").With(b => b.tooltip = "下へ（Alt+↓）");
            var outdent = UIKit.Btn("←", () => tree.Outdent(), "small", "viah-ml4").With(b => b.tooltip = "階層を上げる（親フォルダーの外に出す）");
            var indent = UIKit.Btn("→", () => tree.Indent(), "small", "viah-ml4").With(b => b.tooltip = "階層を下げる（すぐ上のフォルダーの中に入れる）");
            toolbar.Add(up);
            toolbar.Add(down);
            toolbar.Add(outdent);
            toolbar.Add(indent);
            toolbar.Add(UIKit.Div().With(d => d.style.width = 12));
            var rename = UIKit.Btn("名前を変更", () => tree.StartRename(tree.SelectedId), "small").With(b => b.tooltip = "F2 / ダブルクリックでも変更できます");
            var toggle = UIKit.Btn("初期チェック切替", () => tree.ToggleDefaultSelected(), "small", "viah-ml4").With(b => b.tooltip = "プロジェクト作成画面で、最初からチェックを入れておくか");
            var delete = UIKit.Btn("削除", () => tree.DeleteSelected(), "small", "danger", "viah-ml4");
            toolbar.Add(rename);
            toolbar.Add(toggle);
            toolbar.Add(delete);
            treeCard.Add(toolbar);
            selectionButtons = new[] { up, down, outdent, indent, rename, toggle, delete };

            tree = new FolderTreeView(FolderTreeView.Mode.Edit);
            tree.SetData(preset.nodes, project != null ? project.name : "（プロジェクト名）", preset.numberPrefix);
            tree.Changed += () =>
            {
                MarkDirty();
                Commit("フォルダー構成を編集");
            };
            tree.SelectionChanged += RefreshButtons;
            treeCard.Add(tree);
            c.Add(treeCard);

            var footer = UIKit.Div("viah-footer-bar");
            footer.Add(UIKit.Text(project != null ? "保存しても、実際のフォルダーは変更・削除されません。" : "保存先：" + ViahStorage.PresetsDir, "viah-faint", "viah-small", "viah-grow", "viah-ellipsis"));
            footer.Add(UIKit.Btn("キャンセル", Cancel, "ghost"));
            if (project == null && !isNew)
                footer.Add(UIKit.Btn("別のプリセットとして保存", () => Save(true), "viah-ml8"));
            saveBtn = UIKit.Btn("保存", () => Save(false), "primary", "viah-ml8");
            footer.Add(saveBtn);
            root.Add(footer);
            RefreshButtons();
            if (isNew) MarkDirty();
            if (!string.IsNullOrEmpty(restoreSelection)) tree.Select(restoreSelection);
            restoreSelection = null;
        }

        // ───────────── Undo（編集途中の状態） ─────────────

        [Serializable]
        class UiState
        {
            public string name;
            public string description;
            public bool numberPrefix;
            public bool dirty;
            public string selectedId;
            public List<PresetNode> nodes = new List<PresetNode>();
        }

        string restoreSelection;

        public override string CaptureState()
        {
            if (tree == null) return null;
            var s = new UiState
            {
                name = preset.name,
                description = preset.description,
                numberPrefix = preset.numberPrefix,
                dirty = dirty,
                selectedId = tree.SelectedId,
                nodes = tree.Nodes.Select(n => n.Clone()).ToList(),
            };
            return JsonUtility.ToJson(s);
        }

        public override void RestoreState(string state)
        {
            var s = JsonUtility.FromJson<UiState>(state);
            preset.name = s.name;
            preset.description = s.description;
            preset.numberPrefix = s.numberPrefix;
            preset.nodes = s.nodes ?? new List<PresetNode>();
            dirty = s.dirty;
            restoreSelection = s.selectedId;
            root.Clear();
            Build();
        }

        VisualElement NumberSwitch()
        {
            var box = UIKit.Div();
            var sw = new Switch("フォルダー名に番号を付ける（01_Prefabs のように）", preset.numberPrefix);
            sw.changed += on =>
            {
                preset.numberPrefix = on;
                tree.SetNumberPrefix(on);
                MarkDirty();
                Commit("番号の有無を切り替え");
            };
            box.Add(sw);
            box.Add(UIKit.Text("Unity の Project ウィンドウはフォルダーを名前順に並べます。ここで決めた順番どおりに並べたいときは ON にします。", "viah-faint", "viah-small", "viah-wrap", "viah-mt4"));
            return box;
        }

        void MarkDirty()
        {
            dirty = true;
            RefreshButtons();
        }

        void RefreshButtons()
        {
            if (tree == null || saveBtn == null) return;
            bool sel = tree.HasSelection;
            selectionButtons[0].SetEnabled(tree.CanMoveUp);
            selectionButtons[1].SetEnabled(tree.CanMoveDown);
            selectionButtons[2].SetEnabled(tree.CanOutdent);
            selectionButtons[3].SetEnabled(tree.CanIndent);
            for (int i = 4; i < selectionButtons.Length; i++) selectionButtons[i].SetEnabled(sel);
            bool errors = tree.HasErrors;
            bool nameOk = project != null || !string.IsNullOrWhiteSpace(preset.name);
            saveBtn.SetEnabled(dirty && !errors && nameOk);
            saveBtn.tooltip = errors ? "名前に問題のあるフォルダーがあります（赤い「要修正」）" : !nameOk ? "プリセットの名前を入れてください" : "";
        }

        void Save(bool asCopy)
        {
            preset.nodes = tree.Nodes;
            if (project != null)
            {
                project.structure = preset.nodes.Select(n => n.Clone()).ToList();
                project.numberPrefix = preset.numberPrefix;
                ViahDatabase.SaveProject(project, $"「{project.name}」のフォルダー構成を保存");
                window.Toast("フォルダー構成を保存しました");
                onDone?.Invoke();
                return;
            }
            if (asCopy)
            {
                preset.id = ViahStorage.NewId();
                preset.filePath = null;
                if (ViahDatabase.Presets.Any(p => p.name == preset.name)) preset.name += " のコピー";
            }
            ViahDatabase.SavePreset(preset, $"プリセット「{preset.name}」を保存");
            window.Toast($"プリセット「{preset.name}」を保存しました");
            onSavedPreset?.Invoke(preset);
        }

        void Cancel()
        {
            if (dirty && !EditorUtility.DisplayDialog("編集をやめますか？", "保存していない変更は失われます。", "やめる", "編集を続ける")) return;
            onCancel?.Invoke();
        }
    }
}

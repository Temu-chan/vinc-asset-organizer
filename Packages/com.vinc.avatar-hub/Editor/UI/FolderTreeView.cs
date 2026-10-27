using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>
    /// フォルダー構成のツリー。
    /// Check モード：作るフォルダーにチェックを入れる（子をチェックすると親も自動でチェック）。
    /// Edit モード：追加・名前変更・削除・並べ替え・入れ子の変更（ドラッグ＆ドロップ / ボタン）。
    /// </summary>
    public class FolderTreeView : VisualElement
    {
        public enum Mode { Check, Edit }

        class Node
        {
            public PresetNode data;
            public Node parent;
            public readonly List<Node> children = new List<Node>();
            public bool expanded = true;
        }

        enum Drop { None, Before, After, Inside }

        readonly Mode mode;
        List<PresetNode> nodes = new List<PresetNode>();
        Node root = new Node();
        readonly Dictionary<string, Node> byId = new Dictionary<string, Node>();
        readonly List<(VisualElement row, Node node)> rows = new List<(VisualElement, Node)>();
        readonly HashSet<string> collapsed = new HashSet<string>();

        string rootLabel = "";
        bool numberPrefix;

        /// <summary>Check モードでチェックされているノード ID。</summary>
        public readonly HashSet<string> Checked = new HashSet<string>();
        /// <summary>Check モードで「既にある」ノード（チェックを外せない）。</summary>
        public Func<PresetNode, bool> IsExisting;

        public string SelectedId { get; private set; } = "";
        public event Action Changed;
        public event Action SelectionChanged;

        string renamingId;
        Node dragNode;
        VisualElement dropRow;
        Node dropTarget;
        Drop dropKind;

        public FolderTreeView(Mode mode)
        {
            this.mode = mode;
            AddToClassList("viah-tree");
            focusable = mode == Mode.Edit;
            if (mode == Mode.Edit) RegisterCallback<KeyDownEvent>(OnKey);
        }

        public List<PresetNode> Nodes => nodes;
        public bool HasErrors => nodes.Any(n => ErrorOf(byId[n.id]) != null);

        public void SetRootLabel(string label)
        {
            rootLabel = label;
            Render();
        }

        public void SetNumberPrefix(bool on)
        {
            numberPrefix = on;
            Render();
        }

        public void SetData(List<PresetNode> list, string label, bool prefix)
        {
            nodes = list ?? new List<PresetNode>();
            rootLabel = label;
            numberPrefix = prefix;
            Build();
            Render();
        }

        public void CheckDefaults()
        {
            Checked.Clear();
            foreach (var n in nodes)
                if (n.defaultOn) CheckWithAncestors(n);
        }

        // ───────────── 構築 ─────────────

        void Build()
        {
            byId.Clear();
            root = new Node();
            foreach (var n in nodes) byId[n.id] = new Node { data = n, expanded = !collapsed.Contains(n.id) };
            foreach (var n in nodes)
            {
                var node = byId[n.id];
                var parent = !string.IsNullOrEmpty(n.parentId) && byId.TryGetValue(n.parentId, out var p) ? p : root;
                node.parent = parent;
                parent.children.Add(node);
            }
        }

        /// <summary>ツリーの並びを nodes（前順）に書き戻す。</summary>
        void Flatten()
        {
            nodes.Clear();
            void Walk(Node n)
            {
                foreach (var c in n.children)
                {
                    c.data.parentId = n == root ? "" : n.data.id;
                    nodes.Add(c.data);
                    Walk(c);
                }
            }
            Walk(root);
        }

        // ───────────── 描画 ─────────────

        public void Render()
        {
            Clear();
            rows.Clear();
            Add(MakeRootRow());
            foreach (var c in root.children) RenderNode(c, 1);
        }

        VisualElement MakeRootRow()
        {
            var row = UIKit.Row("viah-tree-row", "is-root");
            if (mode == Mode.Edit)
            {
                row.Add(new VisualElement { style = { width = 18 } });
                row.AddToClassList("is-click");
                UIKit.SetClass(row, "is-selected", SelectedId == "");
                row.RegisterCallback<ClickEvent>(_ => Select(""));
            }
            var arrow = new Arrow();
            arrow.Set(false, true);
            row.Add(arrow);
            if (mode == Mode.Check)
            {
                var cb = new CheckBox();
                cb.Set(1, true);
                cb.tooltip = "いちばん上のフォルダーは必ず作られます";
                row.Add(cb);
            }
            row.Add(UIKit.Img("FolderOpened Icon"));
            row.Add(UIKit.Text(string.IsNullOrEmpty(rootLabel) ? "（プロジェクト名）" : rootLabel, "viah-tree-name"));
            if (mode == Mode.Edit) row.Add(UIKit.Text("　← ここには作成時に入力する名前が入ります", "viah-tree-prefix"));
            rows.Add((row, root));
            return row;
        }

        void RenderNode(Node n, int depth)
        {
            var row = mode == Mode.Check ? MakeCheckRow(n, depth) : MakeEditRow(n, depth);
            Add(row);
            rows.Add((row, n));
            if (!n.expanded) return;
            foreach (var c in n.children) RenderNode(c, depth + 1);
        }

        VisualElement Indent(int depth) => new VisualElement { style = { width = depth * 18, flexShrink = 0 } };

        Arrow MakeArrow(Node n)
        {
            var arrow = new Arrow();
            arrow.Set(n.children.Count > 0, n.expanded);
            arrow.RegisterCallback<ClickEvent>(e =>
            {
                n.expanded = !n.expanded;
                if (n.expanded) collapsed.Remove(n.data.id);
                else collapsed.Add(n.data.id);
                Render();
                e.StopPropagation();
            });
            return arrow;
        }

        VisualElement MakeCheckRow(Node n, int depth)
        {
            var row = UIKit.Row("viah-tree-row", "is-click");
            bool existing = IsExisting != null && IsExisting(n.data);
            bool on = existing || Checked.Contains(n.data.id);
            UIKit.SetClass(row, "is-off", !on);
            row.Add(Indent(depth));
            row.Add(MakeArrow(n));
            var cb = new CheckBox();
            cb.Set(on ? 1 : 0, existing);
            row.Add(cb);
            row.Add(UIKit.Img(on ? "Folder Icon" : "FolderEmpty Icon"));
            row.Add(UIKit.Text(FolderBuilder.FolderName(n.data, nodes, numberPrefix), "viah-tree-name"));
            if (existing) row.Add(UIKit.Badge("作成済み", "done", "このフォルダーはもうあります"));
            row.tooltip = existing
                ? "既にあるフォルダーです"
                : on ? "クリックで作成しない（中のフォルダーも外れます）" : "クリックで作成する（親フォルダーも自動でチェックされます）";
            if (!existing) row.RegisterCallback<ClickEvent>(_ => ToggleCheck(n));
            return row;
        }

        VisualElement MakeEditRow(Node n, int depth)
        {
            var row = UIKit.Row("viah-tree-row", "is-click");
            UIKit.SetClass(row, "is-selected", SelectedId == n.data.id);

            var handle = UIKit.Div("viah-tree-handle");
            handle.Add(UIKit.Div("viah-tree-handle-dots"));
            handle.tooltip = "ドラッグして移動（行の上下＝前後に移動、行の真ん中＝そのフォルダーの中へ）";
            SetupDrag(handle, row, n);
            row.Add(handle);

            row.Add(Indent(depth - 1));
            row.Add(MakeArrow(n));
            row.Add(UIKit.Img(n.data.defaultOn ? "Folder Icon" : "FolderEmpty Icon"));

            if (renamingId == n.data.id)
            {
                var tf = new TextField { value = n.data.name };
                tf.AddToClassList("viah-tree-rename");
                bool done = false;
                void Commit(bool apply)
                {
                    if (done) return;
                    done = true;
                    renamingId = null;
                    if (apply)
                    {
                        var v = (tf.value ?? "").Trim();
                        if (v.Length > 0) n.data.name = v;
                    }
                    Render();
                    Changed?.Invoke();
                    Focus();
                }
                tf.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Commit(true); e.StopPropagation(); }
                    else if (e.keyCode == KeyCode.Escape) { Commit(false); e.StopPropagation(); }
                }, TrickleDown.TrickleDown);
                tf.RegisterCallback<FocusOutEvent>(_ => Commit(true));
                row.Add(tf);
                schedule.Execute(() =>
                {
                    UIKit.FocusField(tf);
                    tf.SelectAll();
                });
            }
            else
            {
                if (numberPrefix)
                {
                    var prefix = FolderBuilder.FolderName(n.data, nodes, true);
                    row.Add(UIKit.Text(prefix.Substring(0, 3), "viah-tree-prefix"));
                }
                var name = UIKit.Text(string.IsNullOrEmpty(n.data.name) ? "（名前なし）" : n.data.name, "viah-tree-name");
                row.Add(name);
                var err = ErrorOf(n);
                if (err != null) row.Add(UIKit.Badge("要修正", "danger", err));
            }

            row.Add(UIKit.Spacer());
            var def = UIKit.Badge(n.data.defaultOn ? "初期チェック ON" : "初期チェック OFF", n.data.defaultOn ? "rule" : "",
                "プロジェクト作成画面で、最初からチェックを入れておくか。クリックで切り替え");
            UIKit.Clickable(def, () =>
            {
                n.data.defaultOn = !n.data.defaultOn;
                Render();
                Changed?.Invoke();
            });
            row.Add(def);

            var actions = UIKit.Row("viah-row-actions");
            actions.Add(UIKit.Btn("＋", () => { Select(n.data.id); AddChild(); }, "small", "ghost").With(b => b.tooltip = "この中にフォルダーを追加"));
            actions.Add(UIKit.Btn("名前変更", () => StartRename(n.data.id), "small", "ghost"));
            actions.Add(UIKit.Btn("削除", () => { Select(n.data.id); DeleteSelected(); }, "small", "ghost"));
            foreach (var b in actions.Children()) b.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            row.Add(actions);

            row.RegisterCallback<ClickEvent>(e =>
            {
                Select(n.data.id);
                if (e.clickCount == 2) StartRename(n.data.id);
                Focus();
            });
            return row;
        }

        string ErrorOf(Node n)
        {
            var err = FolderBuilder.ValidateName(n.data.name);
            if (err != null) return err;
            var siblings = (n.parent ?? root).children;
            if (siblings.Count(s => string.Equals(s.data.name, n.data.name, StringComparison.OrdinalIgnoreCase)) > 1)
                return "同じ階層に同じ名前のフォルダーがあります";
            return null;
        }

        // ───────────── Check モード ─────────────

        void ToggleCheck(Node n)
        {
            if (Checked.Contains(n.data.id))
            {
                void Uncheck(Node x)
                {
                    Checked.Remove(x.data.id);
                    foreach (var c in x.children) Uncheck(c);
                }
                Uncheck(n);
            }
            else
            {
                CheckWithAncestors(n.data);
            }
            Render();
            Changed?.Invoke();
        }

        void CheckWithAncestors(PresetNode n)
        {
            Checked.Add(n.id);
            foreach (var a in FolderBuilder.AncestorIds(n, nodes)) Checked.Add(a);
        }

        // ───────────── Edit モード ─────────────

        public void Select(string id)
        {
            if (SelectedId == id) return;
            SelectedId = id ?? "";
            foreach (var (row, node) in rows)
                UIKit.SetClass(row, "is-selected", (node == root && SelectedId == "") || (node.data != null && node.data.id == SelectedId));
            SelectionChanged?.Invoke();
        }

        Node SelectedNode => string.IsNullOrEmpty(SelectedId) ? root : byId.TryGetValue(SelectedId, out var n) ? n : root;

        public bool CanMoveUp => SelectedNode != root && SelectedNode.parent.children.IndexOf(SelectedNode) > 0;
        public bool CanMoveDown => SelectedNode != root && SelectedNode.parent.children.IndexOf(SelectedNode) < SelectedNode.parent.children.Count - 1;
        public bool CanIndent => CanMoveUp;
        public bool CanOutdent => SelectedNode != root && SelectedNode.parent != root;
        public bool HasSelection => SelectedNode != root;

        public void StartRename(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            renamingId = id;
            SelectedId = id;
            Render();
        }

        string UniqueName(Node parent, string baseName)
        {
            var name = baseName;
            int i = 2;
            while (parent.children.Any(c => string.Equals(c.data.name, name, StringComparison.OrdinalIgnoreCase)))
                name = baseName + i++;
            return name;
        }

        /// <summary>選択中のフォルダーの中に追加（ルート選択中ならいちばん上の階層に追加）。</summary>
        public void AddChild()
        {
            var parent = SelectedNode;
            var node = new Node
            {
                data = new PresetNode { id = ViahStorage.NewId(), name = UniqueName(parent, "NewFolder"), defaultOn = true },
                parent = parent,
            };
            parent.children.Add(node);
            parent.expanded = true;
            if (parent.data != null) collapsed.Remove(parent.data.id);
            byId[node.data.id] = node;
            Flatten();
            StartRename(node.data.id);
            Changed?.Invoke();
            SelectionChanged?.Invoke();
        }

        /// <summary>選択中のフォルダーと同じ階層（すぐ下）に追加。</summary>
        public void AddSibling()
        {
            var sel = SelectedNode;
            if (sel == root)
            {
                AddChild();
                return;
            }
            var parent = sel.parent;
            var node = new Node
            {
                data = new PresetNode { id = ViahStorage.NewId(), name = UniqueName(parent, "NewFolder"), defaultOn = true },
                parent = parent,
            };
            parent.children.Insert(parent.children.IndexOf(sel) + 1, node);
            byId[node.data.id] = node;
            Flatten();
            StartRename(node.data.id);
            Changed?.Invoke();
            SelectionChanged?.Invoke();
        }

        public void MoveSelected(int dir)
        {
            var n = SelectedNode;
            if (n == root) return;
            var list = n.parent.children;
            int i = list.IndexOf(n);
            int j = i + dir;
            if (j < 0 || j >= list.Count) return;
            list.RemoveAt(i);
            list.Insert(j, n);
            Commit();
        }

        /// <summary>→：ひとつ上のフォルダーの中に入れる。</summary>
        public void Indent()
        {
            var n = SelectedNode;
            if (!CanIndent) return;
            var list = n.parent.children;
            var prev = list[list.IndexOf(n) - 1];
            list.Remove(n);
            prev.children.Add(n);
            n.parent = prev;
            prev.expanded = true;
            collapsed.Remove(prev.data.id);
            Commit();
        }

        /// <summary>←：親フォルダーの外（親のすぐ下）に出す。</summary>
        public void Outdent()
        {
            var n = SelectedNode;
            if (!CanOutdent) return;
            var parent = n.parent;
            var grand = parent.parent ?? root;
            parent.children.Remove(n);
            grand.children.Insert(grand.children.IndexOf(parent) + 1, n);
            n.parent = grand;
            Commit();
        }

        public void DeleteSelected()
        {
            var n = SelectedNode;
            if (n == root) return;
            if (n.children.Count > 0 &&
                !EditorUtility.DisplayDialog("フォルダーを削除", $"「{n.data.name}」と、その中の {CountDescendants(n)} 個のフォルダーをプリセットから削除します。", "削除", "やめる"))
                return;
            var list = n.parent.children;
            int idx = list.IndexOf(n);
            list.Remove(n);
            SelectedId = list.Count == 0 ? (n.parent == root ? "" : n.parent.data.id) : list[Math.Min(idx, list.Count - 1)].data.id;
            Commit();
            SelectionChanged?.Invoke();
        }

        public void ToggleDefaultSelected()
        {
            var n = SelectedNode;
            if (n == root) return;
            n.data.defaultOn = !n.data.defaultOn;
            Render();
            Changed?.Invoke();
        }

        static int CountDescendants(Node n) => n.children.Sum(c => 1 + CountDescendants(c));

        void Commit()
        {
            Flatten();
            Build();
            Render();
            Changed?.Invoke();
            SelectionChanged?.Invoke();
        }

        void OnKey(KeyDownEvent e)
        {
            if (renamingId != null) return;
            switch (e.keyCode)
            {
                case KeyCode.F2:
                    StartRename(SelectedId);
                    e.StopPropagation();
                    break;
                case KeyCode.Delete:
                    DeleteSelected();
                    e.StopPropagation();
                    break;
                case KeyCode.UpArrow when e.altKey:
                    MoveSelected(-1);
                    e.StopPropagation();
                    break;
                case KeyCode.DownArrow when e.altKey:
                    MoveSelected(+1);
                    e.StopPropagation();
                    break;
            }
        }

        // ───────────── ドラッグ＆ドロップ ─────────────

        void SetupDrag(VisualElement handle, VisualElement row, Node n)
        {
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                dragNode = n;
                Select(n.data.id);
                row.AddToClassList("is-dragging");
                handle.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (dragNode == null || !handle.HasPointerCapture(e.pointerId)) return;
                UpdateDrop(e.position);
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (dragNode == null) return;
                if (handle.HasPointerCapture(e.pointerId)) handle.ReleasePointer(e.pointerId);
                var target = dropTarget;
                var kind = dropKind;
                var moving = dragNode;
                ClearDrop();
                dragNode = null;
                row.RemoveFromClassList("is-dragging");
                if (target != null && kind != Drop.None) MoveNode(moving, target, kind);
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                ClearDrop();
                dragNode = null;
                row.RemoveFromClassList("is-dragging");
            });
        }

        void UpdateDrop(Vector2 pos)
        {
            ClearDrop();
            foreach (var (row, node) in rows)
            {
                var wb = row.worldBound;
                if (pos.y < wb.yMin || pos.y >= wb.yMax) continue;
                Drop kind;
                if (node == root) kind = Drop.Inside;
                else
                {
                    float t = (pos.y - wb.yMin) / Mathf.Max(1, wb.height);
                    kind = t < 0.3f ? Drop.Before : t > 0.7f ? Drop.After : Drop.Inside;
                }
                if (node == dragNode || IsDescendant(node, dragNode)) return;
                dropRow = row;
                dropTarget = node;
                dropKind = kind;
                row.AddToClassList(kind == Drop.Before ? "drop-before" : kind == Drop.After ? "drop-after" : "drop-inside");
                return;
            }
        }

        void ClearDrop()
        {
            if (dropRow != null)
            {
                dropRow.RemoveFromClassList("drop-before");
                dropRow.RemoveFromClassList("drop-after");
                dropRow.RemoveFromClassList("drop-inside");
            }
            dropRow = null;
            dropTarget = null;
            dropKind = Drop.None;
        }

        static bool IsDescendant(Node maybeChild, Node ancestor)
        {
            var p = maybeChild?.parent;
            while (p != null)
            {
                if (p == ancestor) return true;
                p = p.parent;
            }
            return false;
        }

        void MoveNode(Node n, Node target, Drop kind)
        {
            n.parent.children.Remove(n);
            if (kind == Drop.Inside)
            {
                target.children.Add(n);
                n.parent = target;
                target.expanded = true;
                if (target.data != null) collapsed.Remove(target.data.id);
            }
            else
            {
                var parent = target.parent ?? root;
                int idx = parent.children.IndexOf(target);
                parent.children.Insert(kind == Drop.Before ? idx : idx + 1, n);
                n.parent = parent;
            }
            SelectedId = n.data.id;
            Commit();
        }
    }

    public static class VisualElementExtensions
    {
        public static T With<T>(this T e, Action<T> action) where T : VisualElement
        {
            action(e);
            return e;
        }
    }
}

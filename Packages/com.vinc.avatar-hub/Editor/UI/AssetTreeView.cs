using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>
    /// 含めるファイルを選ぶツリー（大量のファイルでも軽いように仮想化リストで表示）。
    /// クリック＝1 つ切り替え / Shift+クリック＝範囲をまとめて切り替え /
    /// フォルダーのチェック＝中身ぜんぶ（今後増えるファイルも）。
    /// </summary>
    public class AssetTreeView : VisualElement
    {
        public class Node
        {
            public string path;
            public string name;
            public string sub;
            public bool isFolder;
            public bool isGroup;
            /// <summary>中身の一部しか表示していないフォルダー（フォルダールールは使わない）。</summary>
            public bool partial;
            public int depth;
            public Node parent;
            public readonly List<Node> children = new List<Node>();
            public bool expanded;

            public bool included;
            public int total;
            public int inc;
            public bool ruleHere;
            public bool viaRule;

            public bool detected;
            public bool external;
            public bool isNew;
            public bool isSeed;
            public string reason;
            public string norm;
            public Texture icon;
        }

        public enum Filter { All, Selected, Unselected, Detected, External, New }

        class RowRefs
        {
            public int index;
            public VisualElement indent;
            public Arrow arrow;
            public CheckBox check;
            public Image icon;
            public Label name;
            public Label sub;
            public VisualElement badges;
            public Label reason;
            public Button folderBtn;
        }

        SelectionModel model;
        string basePath;
        ScanResult scan;
        HashSet<string> newPaths = new HashSet<string>();
        readonly List<Node> roots = new List<Node>();
        readonly List<Node> visible = new List<Node>();
        readonly ListView list;
        Node anchor;
        Filter filter = Filter.All;
        string[] tokens = Array.Empty<string>();

        public event Action Changed;

        public AssetTreeView()
        {
            AddToClassList("viah-asset-list");
            style.flexGrow = 1;
            list = new ListView
            {
                itemsSource = visible,
                fixedItemHeight = 24,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            list.style.flexGrow = 1;
            Add(list);
        }

        // ───────────── データ ─────────────

        public void Setup(SelectionModel model, string basePath, bool showAll, ScanResult scan, HashSet<string> newPaths)
        {
            this.model = model;
            this.basePath = string.IsNullOrEmpty(basePath) ? "Assets" : basePath;
            this.scan = scan;
            this.newPaths = newPaths ?? new HashSet<string>();
            roots.Clear();

            bool wholeAssets = showAll || this.basePath == "Assets";
            if (wholeAssets)
            {
                roots.Add(BuildFolder("Assets", 0));
            }
            else
            {
                roots.Add(BuildFolder(this.basePath, 0));
                var group = BuildExternalGroup();
                if (group != null) roots.Add(group);
            }

            LoadIcons(wholeAssets);
            Compute();
            foreach (var r in roots) InitExpand(r);
            RebuildVisible();
        }

        /// <summary>
        /// アイコンは選び始める前にまとめて読む（スクロール中に読み込みが走らないように）。
        /// 読むのは表示する範囲（基準フォルダーと、その外で使われているファイル）だけ。
        /// Assets 全体の表示は範囲を絞れないので、サムネイルは読まず種類のアイコンにする。
        /// </summary>
        void LoadIcons(bool typeOnly)
        {
            var nodes = new List<Node>();
            void Walk(Node n)
            {
                nodes.Add(n);
                foreach (var c in n.children) Walk(c);
            }
            foreach (var r in roots) Walk(r);

            bool progress = nodes.Count > 300;
            try
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    var n = nodes[i];
                    if (progress && i % 50 == 0)
                        EditorUtility.DisplayProgressBar("Avatar Hub", $"アイコンを読み込んでいます… ({i}/{nodes.Count})", (float)i / nodes.Count);
                    if (n.isGroup) n.icon = UIKit.Icon("console.warnicon.sml");
                    else if (n.isFolder) n.icon = UIKit.Icon("Folder Icon");
                    else n.icon = UIKit.AssetIcon(n.path, !typeOnly);
                }
            }
            finally
            {
                if (progress) EditorUtility.ClearProgressBar();
            }
        }

        Node BuildFolder(string folder, int depth)
        {
            var rootNode = MakeNode(folder, true, depth, null);
            rootNode.name = folder;
            var map = new Dictionary<string, Node> { [folder] = rootNode };
            var paths = AssetDatabase.FindAssets("", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && p.StartsWith(folder + "/", StringComparison.Ordinal))
                .Distinct()
                .ToList();
            paths.Sort(StringComparer.Ordinal);
            foreach (var p in paths) Ensure(p, map, folder);
            SortChildren(rootNode);
            return rootNode;
        }

        Node Ensure(string path, Dictionary<string, Node> map, string top)
        {
            if (map.TryGetValue(path, out var n)) return n;
            int slash = path.LastIndexOf('/');
            var parentPath = path.Substring(0, slash);
            var parent = parentPath.Length < top.Length ? map[top] : Ensure(parentPath, map, top);
            n = MakeNode(path, AssetDatabase.IsValidFolder(path), parent.depth + 1, parent);
            parent.children.Add(n);
            map[path] = n;
            return n;
        }

        Node MakeNode(string path, bool isFolder, int depth, Node parent)
        {
            var n = new Node
            {
                path = path,
                name = UIKit.AssetName(path),
                isFolder = isFolder,
                depth = depth,
                parent = parent,
            };
            n.norm = SearchUtil.Normalize(n.name);
            if (scan != null && scan.assets.TryGetValue(path, out var d))
            {
                n.detected = true;
                n.isSeed = d.isSeed;
                n.reason = scan.ReasonOf(path);
            }
            n.isNew = newPaths.Contains(path);
            n.external = !ProfileResolver.IsInside(path, basePath);
            return n;
        }

        /// <summary>基準フォルダーの外で、検出された・登録されているもの。</summary>
        Node BuildExternalGroup()
        {
            var outside = new HashSet<string>(StringComparer.Ordinal);
            if (scan != null)
                foreach (var p in scan.assets.Keys)
                    if (!ProfileResolver.IsInside(p, basePath)) outside.Add(p);
            foreach (var p in model.includeFiles)
                if (!ProfileResolver.IsInside(p, basePath)) outside.Add(p);
            var folders = model.includeFolders.Where(p => !ProfileResolver.IsInside(p, basePath)).ToList();
            if (outside.Count == 0 && folders.Count == 0) return null;

            var group = new Node
            {
                path = "",
                name = "基準フォルダーの外にあるファイル",
                isGroup = true,
                partial = true,
                isFolder = true,
                depth = 0,
                external = true,
            };
            foreach (var f in folders.OrderBy(x => x, StringComparer.Ordinal))
            {
                var folderNode = BuildFolder(f, 1);
                folderNode.parent = group;
                folderNode.external = true;
                MarkExternal(folderNode);
                group.children.Add(folderNode);
            }
            foreach (var p in outside.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (folders.Any(f => p.StartsWith(f + "/", StringComparison.Ordinal))) continue;
                if (AssetDatabase.IsValidFolder(p)) continue;
                var n = MakeNode(p, false, 1, group);
                n.sub = System.IO.Path.GetDirectoryName(p)?.Replace('\\', '/');
                group.children.Add(n);
            }
            return group;
        }

        static void MarkExternal(Node n)
        {
            n.external = true;
            foreach (var c in n.children) MarkExternal(c);
        }

        static void SortChildren(Node n)
        {
            n.children.Sort((a, b) =>
            {
                if (a.isFolder != b.isFolder) return a.isFolder ? -1 : 1;
                return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
            });
            foreach (var c in n.children)
                if (c.isFolder) SortChildren(c);
        }

        void InitExpand(Node n)
        {
            n.expanded = n.depth == 0 || n.isGroup || (n.isFolder && (n.inc > 0 || ContainsFlag(n)));
            foreach (var c in n.children)
                if (c.isFolder) InitExpand(c);
        }

        static bool ContainsFlag(Node n)
        {
            foreach (var c in n.children)
            {
                if (!c.isFolder && (c.detected || c.isNew)) return true;
                if (c.isFolder && ContainsFlag(c)) return true;
            }
            return false;
        }

        // ───────────── 状態の計算 ─────────────

        void Compute()
        {
            foreach (var r in roots)
            {
                if (r.isGroup) ComputeGroup(r);
                else ComputeNode(r, model.Inherited(r.path));
            }
        }

        void ComputeGroup(Node g)
        {
            g.total = g.inc = 0;
            foreach (var c in g.children)
            {
                ComputeNode(c, model.Inherited(c.path));
                Accumulate(g, c);
            }
            g.included = g.total > 0 && g.inc == g.total;
        }

        void ComputeNode(Node n, bool inherited)
        {
            if (!n.isFolder)
            {
                n.included = model.includeFiles.Contains(n.path) || (!model.excludes.Contains(n.path) && inherited);
                return;
            }
            n.ruleHere = model.includeFolders.Contains(n.path);
            bool state = n.ruleHere || (!model.excludes.Contains(n.path) && inherited);
            n.viaRule = state;
            n.total = n.inc = 0;
            foreach (var c in n.children)
            {
                ComputeNode(c, state);
                Accumulate(n, c);
            }
            n.included = n.total > 0 ? n.inc == n.total : state;
        }

        static void Accumulate(Node parent, Node c)
        {
            if (c.isFolder)
            {
                parent.total += c.total;
                parent.inc += c.inc;
            }
            else
            {
                parent.total++;
                if (c.included) parent.inc++;
            }
        }

        public int IncludedCount => roots.Sum(r => r.inc);

        public IEnumerable<Node> AllFiles()
        {
            var stack = new Stack<Node>(roots);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (!n.isFolder) yield return n;
                foreach (var c in n.children) stack.Push(c);
            }
        }

        // ───────────── 表示 ─────────────

        public void SetFilter(Filter f)
        {
            filter = f;
            RebuildVisible();
        }

        public void SetQuery(string q)
        {
            tokens = SearchUtil.Tokens(q);
            RebuildVisible();
        }

        public void ExpandAll(bool open)
        {
            void Walk(Node n)
            {
                if (n.isFolder) n.expanded = open || n.depth == 0 || n.isGroup;
                foreach (var c in n.children) Walk(c);
            }
            foreach (var r in roots) Walk(r);
            RebuildVisible();
        }

        bool Filtering => filter != Filter.All || tokens.Length > 0;

        bool Matches(Node n)
        {
            if (tokens.Length > 0 && !SearchUtil.Contains(n.norm ?? "", tokens)) return false;
            if (n.isFolder) return filter == Filter.All;
            switch (filter)
            {
                case Filter.Selected: return n.included;
                case Filter.Unselected: return !n.included;
                case Filter.Detected: return n.detected;
                case Filter.External: return n.external;
                case Filter.New: return n.isNew;
                default: return true;
            }
        }

        void RebuildVisible()
        {
            visible.Clear();
            if (!Filtering)
            {
                void Walk(Node n)
                {
                    visible.Add(n);
                    if (!n.expanded) return;
                    foreach (var c in n.children) Walk(c);
                }
                foreach (var r in roots) Walk(r);
            }
            else
            {
                bool Collect(Node n, List<Node> acc)
                {
                    var mine = new List<Node>();
                    bool any = false;
                    foreach (var c in n.children)
                        if (Collect(c, mine)) any = true;
                    bool self = Matches(n);
                    if (!self && !any && n.depth > 0 && !n.isGroup) return false;
                    acc.Add(n);
                    acc.AddRange(mine);
                    return self || any;
                }
                foreach (var r in roots)
                {
                    var acc = new List<Node>();
                    if (Collect(r, acc) || r.depth == 0) visible.AddRange(acc);
                }
            }
            list.RefreshItems();
        }

        VisualElement MakeRow()
        {
            var row = UIKit.Row("viah-asset-row");
            var refs = new RowRefs
            {
                indent = new VisualElement { style = { flexShrink = 0 } },
                arrow = new Arrow(),
                check = new CheckBox(),
                icon = UIKit.Img((Texture)null),
                name = UIKit.Text("", "viah-tree-name"),
                sub = UIKit.Text("", "viah-tree-prefix", "viah-ml6"),
                badges = UIKit.Row(),
                reason = UIKit.Text("", "viah-tree-reason"),
            };
            refs.folderBtn = UIKit.Btn("フォルダーごと", () => OnFolderRule(refs.index), "small", "ghost", "viah-ping");
            refs.folderBtn.tooltip = "このフォルダーの中身を、今後増えるファイルも含めてすべて入れます";
            var ping = UIKit.IconBtn("Search Icon", "Project ウィンドウで表示", () => OnPing(refs.index), "small", "ghost", "viah-ping");
            // ボタンのクリックで行のチェックが切り替わらないように止める
            refs.folderBtn.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            ping.RegisterCallback<ClickEvent>(e => e.StopPropagation());

            row.Add(refs.indent);
            row.Add(refs.arrow);
            row.Add(refs.check);
            row.Add(refs.icon);
            row.Add(refs.name);
            row.Add(refs.sub);
            row.Add(refs.badges);
            row.Add(UIKit.Spacer());
            row.Add(refs.reason);
            row.Add(refs.folderBtn);
            row.Add(ping);
            row.userData = refs;

            refs.arrow.RegisterCallback<ClickEvent>(e =>
            {
                ToggleExpand(refs.index, e.altKey);
                e.StopPropagation();
            });
            row.RegisterCallback<ClickEvent>(e => OnRowClick(refs.index, e.shiftKey));
            return row;
        }

        void BindRow(VisualElement row, int index)
        {
            var r = (RowRefs)row.userData;
            r.index = index;
            if (index < 0 || index >= visible.Count) return;
            var n = visible[index];

            r.indent.style.width = n.depth * 16;
            r.arrow.Set(n.isFolder && n.children.Count > 0, n.expanded || Filtering);

            int state;
            if (n.isFolder)
            {
                if (n.total == 0) state = n.viaRule ? 1 : 0;
                else state = n.inc == n.total ? 1 : n.inc > 0 ? 2 : 0;
            }
            else state = n.included ? 1 : 0;
            r.check.Set(state);

            r.icon.image = n.icon;
            r.name.text = n.isGroup ? $"{n.name}（{n.children.Count}）" : n.name;
            r.sub.text = n.sub ?? "";
            UIKit.Show(r.sub, !string.IsNullOrEmpty(n.sub));
            UIKit.SetClass(row, "is-off", state == 0);
            UIKit.SetClass(row, "is-group", n.isGroup || n.depth == 0);
            UIKit.SetClass(row, "is-anchor", anchor == n);

            r.badges.Clear();
            if (n.isFolder && n.ruleHere)
                r.badges.Add(UIKit.Badge("フォルダーごと", "rule", "このフォルダーの中身は、今後増えるファイルも含めてすべて入ります"));
            if (n.isSeed) r.badges.Add(UIKit.Badge("起点", "auto", "自動検出の起点として登録したアセット"));
            else if (n.detected) r.badges.Add(UIKit.Badge("自動", "auto", "プレハブが使っているため自動で見つかりました\n" + n.reason));
            if (n.isNew) r.badges.Add(UIKit.Badge("新規", "new", "前回の登録のあとで、新しく使われ始めたファイルです"));
            if (n.external && !n.isGroup && !n.isFolder)
                r.badges.Add(UIKit.Badge("外部", "ext", "基準フォルダーの外にあります。アバター本体や他の人の素材なら含めないでください"));

            r.reason.text = n.detected && !n.isSeed ? n.reason : "";
            r.folderBtn.style.display = n.isFolder && !n.partial && !n.isGroup && !n.ruleHere && n.depth > 0 ? StyleKeyword.Null : DisplayStyle.None;
            row.tooltip = n.isGroup
                ? "基準フォルダーの外で使われているファイルです。必要なものだけチェックしてください"
                : n.isFolder
                    ? (state == 1 ? "クリックで中身をすべて外す" : "クリックで中身をすべて含める（今後増えるファイルも含む）") + "\n" + n.path
                    : n.path;
        }

        // ───────────── 操作 ─────────────

        void ToggleExpand(int index, bool recursive)
        {
            if (index < 0 || index >= visible.Count || Filtering) return;
            var n = visible[index];
            bool open = !n.expanded;
            void Set(Node x)
            {
                x.expanded = open;
                if (recursive)
                    foreach (var c in x.children)
                        if (c.isFolder) Set(c);
            }
            Set(n);
            RebuildVisible();
        }

        void OnRowClick(int index, bool shift)
        {
            if (index < 0 || index >= visible.Count) return;
            var n = visible[index];
            bool target = !IsOn(n);

            int anchorIndex = anchor != null ? visible.IndexOf(anchor) : -1;
            if (shift && anchorIndex >= 0 && anchorIndex != index)
            {
                int a = Math.Min(anchorIndex, index), b = Math.Max(anchorIndex, index);
                var range = visible.GetRange(a, b - a + 1);
                // 範囲の起点（最初にクリックした行）の状態にそろえる
                target = IsOn(anchor);
                var inRange = new HashSet<Node>(range);
                bool Covered(Node f)
                {
                    foreach (var c in f.children)
                    {
                        if (!inRange.Contains(c)) return false;
                        if (c.isFolder && c.expanded && !Covered(c)) return false;
                    }
                    return true;
                }
                foreach (var x in range)
                {
                    // 開いているフォルダーは、中身が全部範囲に入っているときだけフォルダーごと切り替える
                    // （一部だけ範囲に入ったときに、範囲外のファイルまで変わらないように）
                    if (x.isFolder && x.children.Count > 0 && (x.expanded || Filtering) && !Covered(x)) continue;
                    Apply(x, target);
                }
            }
            else
            {
                Apply(n, target);
                anchor = n;
            }
            Compute();
            list.RefreshItems();
            Changed?.Invoke();
        }

        static bool IsOn(Node n)
        {
            if (!n.isFolder) return n.included;
            return n.total > 0 ? n.inc == n.total : n.viaRule;
        }

        void Apply(Node n, bool on)
        {
            if (!n.isFolder)
            {
                model.SetFile(n.path, on);
                return;
            }
            if (n.isGroup || n.partial)
            {
                foreach (var c in n.children) Apply(c, on);
                return;
            }
            model.SetFolder(n.path, on);
        }

        void OnFolderRule(int index)
        {
            if (index < 0 || index >= visible.Count) return;
            var n = visible[index];
            model.SetFolder(n.path, true);
            anchor = n;
            Compute();
            list.RefreshItems();
            Changed?.Invoke();
        }

        void OnPing(int index)
        {
            if (index < 0 || index >= visible.Count) return;
            var n = visible[index];
            if (!n.isGroup) UIKit.Reveal(n.path);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>
    /// ⑥ アセットSearcher：選んだアセットを使っているもの（テクスチャ → マテリアル、アニメーション → コントローラーなど）を探す。
    /// 探す場所は Assets 全体・フォルダーの中・プレハブの中。ふだんは直接使っているものだけ、スイッチでその先までたどる。
    /// 検索はボタンを押したときだけ（作業中に勝手に読み込まない）。
    /// </summary>
    public class SearcherPage : Page
    {
        public override string Section => "searcher";

        const int ScopeAssets = 0, ScopeFolder = 1, ScopePrefab = 2;
        static readonly string[] ScopeLabels = { "Assets 全体", "フォルダーの中", "プレハブの中" };

        // 画面を移っても続きから使えるよう、検索の条件は覚えておく
        static string target = "";
        static int scope;
        static string folder = "";
        static string prefab = "";
        static bool indirect;

        List<ReferenceHit> results;
        string resultsFor;
        VisualElement targetBox;
        VisualElement scopeBox;
        VisualElement resultBox;
        VisualElement footer;

        // ───────────── Undo（入力途中の状態） ─────────────

        [Serializable]
        class UiState
        {
            public string target, folder, prefab;
            public int scope;
            public bool indirect;
        }

        public override string CaptureState() =>
            JsonUtility.ToJson(new UiState { target = target, folder = folder, prefab = prefab, scope = scope, indirect = indirect });

        public override void RestoreState(string state)
        {
            var s = JsonUtility.FromJson<UiState>(state);
            target = s.target;
            folder = s.folder;
            prefab = s.prefab;
            scope = s.scope;
            indirect = s.indirect;
            RenderInputs();
        }

        // ───────────── 画面 ─────────────

        public override void Build()
        {
            var page = UIKit.Div();
            page.style.flexGrow = 1;
            root.Add(page);
            var scroll = UIKit.PageScroll();
            page.Add(scroll);
            var c = scroll.contentContainer;

            c.Add(UIKit.Text("アセットSearcher", "viah-page-title"));
            c.Add(UIKit.Text("選んだアセットを使っているもの（テクスチャ → マテリアル、アニメーション → コントローラーなど）を探します。", "viah-page-sub"));

            var targetCard = UIKit.Card();
            targetCard.name = "card-search-target";
            targetCard.Add(UIKit.SectionHead("1", "探すアセット", "Project ウィンドウからドロップするか、選んでから「選択中のものを使う」を押します"));
            targetBox = UIKit.Div();
            targetCard.Add(targetBox);
            c.Add(targetCard);

            var scopeCard = UIKit.Card();
            scopeCard.name = "card-search-scope";
            scopeCard.Add(UIKit.SectionHead("2", "探す場所"));
            scopeBox = UIKit.Div();
            scopeCard.Add(scopeBox);
            c.Add(scopeCard);

            resultBox = UIKit.Div();
            c.Add(resultBox);

            footer = UIKit.Div("viah-footer-bar");
            page.Add(footer);
            RenderInputs();
        }

        void Changed(string label)
        {
            results = null;
            Commit(label);
            RenderInputs();
        }

        void RenderInputs()
        {
            if (targetBox == null) return;
            RenderTarget();
            RenderScope();
            RenderResults();
            RenderFooter();
        }

        static string Selected(Func<string, bool> ok) =>
            Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => !string.IsNullOrEmpty(p) && ok(p));

        void RenderTarget()
        {
            targetBox.Clear();
            if (AssetRef.Exists(target) && !AssetDatabase.IsValidFolder(target))
            {
                targetBox.Add(PathPill(target, () => Changed("探すアセットを外す"), "外す"));
            }
            else
            {
                var zone = new DropZone("ここに探したいアセットをドラッグ＆ドロップ", "テクスチャ・マテリアル・アニメーション・メッシュ・プレハブなど");
                zone.dropped += paths =>
                {
                    var p = paths.FirstOrDefault(x => !AssetDatabase.IsValidFolder(x));
                    if (p == null) return;
                    target = p;
                    Changed("探すアセットを選ぶ");
                };
                targetBox.Add(zone);
            }
            var row = UIKit.Row("viah-mt8");
            row.Add(UIKit.Btn("選択中のものを使う", () =>
            {
                var p = Selected(x => !AssetDatabase.IsValidFolder(x));
                if (p == null)
                {
                    window.Toast("Project ウィンドウでアセットを選んでください", "warn");
                    return;
                }
                target = p;
                Changed("探すアセットを選ぶ");
            }, "small"));
            targetBox.Add(row);
        }

        void RenderScope()
        {
            scopeBox.Clear();
            var seg = new Segmented(ScopeLabels, scope, i =>
            {
                scope = i;
                Changed("探す場所を変更");
            });
            seg.style.alignSelf = Align.FlexStart;
            scopeBox.Add(seg);

            if (scope == ScopeFolder)
            {
                var row = UIKit.Row("viah-mt12");
                row.Add(UIKit.Text("フォルダー", "viah-kv-key"));
                var pill = UIKit.Div("viah-path-pill");
                pill.Add(UIKit.Img("Folder Icon"));
                pill.Add(UIKit.Text(AssetDatabase.IsValidFolder(folder) ? folder : "未設定（ドロップでも選べます）", "viah-ml6"));
                row.Add(pill);
                DropZone.AcceptFolderDrop(pill, f =>
                {
                    folder = f;
                    Changed("探すフォルダーを選ぶ");
                });
                row.Add(UIKit.Btn("変更…", () =>
                {
                    var f = UIKit.PickAssetsFolder("探すフォルダーを選ぶ", folder);
                    if (f == null) return;
                    folder = f;
                    Changed("探すフォルダーを選ぶ");
                }, "viah-ml8"));
                scopeBox.Add(row);
                scopeBox.Add(UIKit.Text("このフォルダーの中のアセットだけを調べます（その先をたどるときも、フォルダーの中だけ）。", "viah-faint", "viah-small", "viah-mt4", "viah-wrap"));
            }
            else if (scope == ScopePrefab)
            {
                var row = UIKit.Row("viah-mt12");
                row.Add(UIKit.Text("プレハブ", "viah-kv-key"));
                if (IsPrefab(prefab))
                {
                    row.Add(PathPill(prefab, null, null));
                }
                else
                {
                    var pill = UIKit.Div("viah-path-pill");
                    pill.Add(UIKit.Img("Prefab Icon"));
                    pill.Add(UIKit.Text("未設定（ドロップでも選べます）", "viah-ml6"));
                    row.Add(pill);
                }
                row.RegisterCallback<DragUpdatedEvent>(_ => DragAndDrop.visualMode = DropZone.DraggedPaths().Any(IsPrefab) ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected);
                row.RegisterCallback<DragPerformEvent>(_ =>
                {
                    var p = DropZone.DraggedPaths().FirstOrDefault(IsPrefab);
                    if (p == null) return;
                    DragAndDrop.AcceptDrag();
                    prefab = p;
                    Changed("調べるプレハブを選ぶ");
                });
                row.Add(UIKit.Btn("選択中のプレハブ", () =>
                {
                    var p = Selected(IsPrefab);
                    if (p == null)
                    {
                        window.Toast("Project ウィンドウでプレハブを選んでください", "warn");
                        return;
                    }
                    prefab = p;
                    Changed("調べるプレハブを選ぶ");
                }, "viah-ml8"));
                scopeBox.Add(row);
                scopeBox.Add(UIKit.Text("プレハブの中の、どのオブジェクトの、どのコンポーネントの、どの欄が使っているかを表示します。", "viah-faint", "viah-small", "viah-mt4", "viah-wrap"));
            }
            else
            {
                scopeBox.Add(UIKit.Text("Assets の中のすべてのアセットから探します（アセットは読み込まず、Unity の依存関係の情報だけを使います）。", "viah-faint", "viah-small", "viah-mt8", "viah-wrap"));
            }

            var sw = new Switch("その先もたどる（経由も表示）", indirect,
                scope == ScopePrefab ? "コンポーネントが使っているアセット（コントローラーなど）の中で使われている場合も出します" : "テクスチャ → マテリアル → プレハブのように、使っているものを使っているものも出します");
            sw.style.marginTop = 12;
            sw.changed += on =>
            {
                indirect = on;
                Changed(on ? "その先もたどる" : "直接使っているものだけ");
            };
            scopeBox.Add(sw);
        }

        static bool IsPrefab(string p) => !string.IsNullOrEmpty(p) && p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) && AssetRef.Exists(p);

        VisualElement PathPill(string path, Action onClear, string clearLabel)
        {
            var row = UIKit.Row("viah-search-target");
            row.Add(UIKit.Img(UIKit.AssetIcon(path)));
            var col = UIKit.Div("viah-grow", "viah-ml8");
            col.Add(UIKit.Text(Path.GetFileName(path), "viah-bold", "viah-ellipsis"));
            col.Add(UIKit.Text(DependencyScanner.KindOf(path) + "　" + path, "viah-faint", "viah-small", "viah-ellipsis"));
            row.Add(col);
            row.Add(UIKit.IconBtn("Search Icon", "Project ウィンドウで表示", () => ProjectTabs.Jump(path), "small", "ghost"));
            if (onClear != null) row.Add(UIKit.Btn(clearLabel, onClear, "small", "ghost", "viah-ml4"));
            return row;
        }

        string CannotRun()
        {
            if (!AssetRef.Exists(target) || AssetDatabase.IsValidFolder(target)) return "探すアセットを選んでください";
            if (scope == ScopeFolder && !AssetDatabase.IsValidFolder(folder)) return "探すフォルダーを選んでください";
            if (scope == ScopePrefab && !IsPrefab(prefab)) return "調べるプレハブを選んでください";
            if (scope == ScopePrefab && prefab == target) return "探すアセットとプレハブが同じです";
            return null;
        }

        void RenderFooter()
        {
            footer.Clear();
            var why = CannotRun();
            footer.Add(UIKit.Text(why ?? (scope == ScopeAssets ? "Assets 全体を調べるので、少し時間がかかることがあります。" : ""), why != null ? "viah-msg-warn" : "viah-faint", "viah-grow", "viah-wrap"));
            var run = UIKit.Btn("検索 ▶", Run, "primary", "big");
            run.name = "btn-search-run";
            run.SetEnabled(why == null);
            footer.Add(run);
        }

        void Run()
        {
            if (CannotRun() != null) return;
            var started = EditorApplication.timeSinceStartup;
            if (scope == ScopePrefab)
            {
                results = ReferenceFinder.FindInPrefab(prefab, target, indirect);
            }
            else
            {
                results = ReferenceFinder.FindUsers(target, scope == ScopeFolder ? folder : "Assets", indirect);
                if (results == null)
                {
                    window.Toast("検索をやめました", "warn");
                    return;
                }
            }
            resultsFor = target;
            RenderResults();
            ViahSound.Done("search", EditorApplication.timeSinceStartup - started);
        }

        // ───────────── 結果 ─────────────

        void RenderResults()
        {
            resultBox.Clear();
            if (results == null || resultsFor != target) return;
            var card = UIKit.Card();
            card.name = "card-search-results";
            var where = scope == ScopePrefab ? $"「{Path.GetFileName(prefab)}」の中" : scope == ScopeFolder ? folder : "Assets 全体";
            card.Add(UIKit.SectionHead(null, $"「{Path.GetFileName(target)}」を使っているもの（{results.Count} 件）", "探した場所：" + where));
            if (results.Count == 0)
            {
                card.Add(UIKit.Text(indirect ? "見つかりませんでした。" : "直接使っているものは見つかりませんでした。「その先もたどる」をオンにすると、間に別のアセットがはさまっているものも探せます。",
                    "viah-faint", "viah-wrap", "viah-mt4"));
                resultBox.Add(card);
                return;
            }
            var direct = results.Where(r => r.Direct).ToList();
            var further = results.Where(r => !r.Direct).ToList();
            if (direct.Count > 0)
            {
                card.Add(UIKit.Text($"直接使っている（{direct.Count}）", "viah-bold", "viah-mt8"));
                foreach (var r in direct) card.Add(scope == ScopePrefab ? PrefabRow(r) : AssetRow(r));
            }
            if (further.Count > 0)
            {
                card.Add(UIKit.Text($"その先で使っている（{further.Count}）", "viah-bold", "viah-mt12"));
                foreach (var r in further) card.Add(scope == ScopePrefab ? PrefabRow(r) : AssetRow(r));
            }
            resultBox.Add(card);
        }

        VisualElement AssetRow(ReferenceHit r)
        {
            var row = UIKit.Row("viah-search-hit");
            row.Add(UIKit.Img(UIKit.AssetIcon(r.path, false)));
            var col = UIKit.Div("viah-grow", "viah-ml8");
            var title = UIKit.Row();
            title.Add(UIKit.Text(Path.GetFileName(r.path), "viah-bold", "viah-ellipsis"));
            title.Add(UIKit.Badge(DependencyScanner.KindOf(r.path), ""));
            col.Add(title);
            col.Add(UIKit.Text(r.path, "viah-faint", "viah-small", "viah-ellipsis"));
            if (!r.Direct) col.Add(UIKit.Text("経由：" + string.Join(" › ", r.via.Select(Path.GetFileName)), "viah-small", "viah-search-via", "viah-wrap"));
            row.Add(col);
            row.Add(UIKit.Btn("Project で表示", () => ProjectTabs.Jump(r.path), "small", "ghost"));
            return row;
        }

        VisualElement PrefabRow(ReferenceHit r)
        {
            var row = UIKit.Row("viah-search-hit");
            row.Add(UIKit.Img(AssetPreview.GetMiniTypeThumbnail(typeof(GameObject))));
            var col = UIKit.Div("viah-grow", "viah-ml8");
            col.Add(UIKit.Text($"{r.objectPath} › {r.component} › {r.property}", "viah-bold", "viah-wrap"));
            if (!r.Direct)
                col.Add(UIKit.Text($"「{Path.GetFileName(r.path)}」の中で使われています　経由：" + string.Join(" › ", r.via.Select(Path.GetFileName)), "viah-small", "viah-search-via", "viah-wrap"));
            row.Add(col);
            var open = UIKit.Btn("プレハブで選択", () => OpenInPrefab(r), "small", "ghost");
            open.tooltip = "プレハブを開いて、このオブジェクトを選択します";
            row.Add(open);
            return row;
        }

        void OpenInPrefab(ReferenceHit r)
        {
            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(prefab));
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null) return;
            var root = stage.prefabContentsRoot.transform;
            // objectPath は「ルート名/子/孫」なので、ルート名を外して探す
            var rel = r.objectPath.Contains("/") ? r.objectPath.Substring(r.objectPath.IndexOf('/') + 1) : "";
            var t = string.IsNullOrEmpty(rel) ? root : root.Find(rel);
            if (t == null) return;
            Selection.activeGameObject = t.gameObject;
            EditorGUIUtility.PingObject(t.gameObject);
        }
    }
}

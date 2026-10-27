using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VInc.AvatarHub.UI
{
    /// <summary>② プロファイル：一覧と詳細（作成・編集はウィザード）。</summary>
    public class ProfilesPage : Page
    {
        public override string Section => "profiles";

        string selectedId;
        string filter = "";
        VisualElement listBox;
        VisualElement detail;

        public ProfilesPage(string selectId = null)
        {
            selectedId = selectId;
        }

        public override void Build()
        {
            if (selectedId == null && ViahDatabase.Profiles.Count > 0) selectedId = ViahDatabase.Profiles[0].id;

            var split = UIKit.Div("viah-split");
            root.Add(split);

            var listPane = UIKit.Div("viah-list-pane");
            var head = UIKit.Div("viah-list-pane-head");
            head.Add(UIKit.Text("プロファイル", "viah-list-title"));
            var import = UIKit.IconBtn("Import", "JSON から読み込む（人からもらったプロファイルを使う）", Import, "small", "ghost");
            head.Add(import);
            var add = UIKit.Btn("Toolbar Plus", "新規", () => window.Show(new ProfileWizardPage(null)), "small", "primary", "viah-ml4");
            add.tooltip = "新しいプロファイルを作る";
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
            // 詳細はタグ入力中にフォーカスが外れないよう作り直さない
            RenderList();
        }

        void Import()
        {
            var path = EditorUtility.OpenFilePanel("プロファイルを読み込む", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            var msg = ViahDatabase.ImportJson(path, out var imported);
            if (msg == null) return;
            if (imported is PackageProfile p) selectedId = p.id;
            RenderList();
            RenderDetail();
            window.Toast(msg, imported == null ? "warn" : "");
        }

        void RenderList()
        {
            listBox.Clear();
            var all = ViahDatabase.Profiles;
            if (all.Count == 0)
            {
                listBox.Add(UIKit.Text("まだプロファイルはありません。\n「新規」から、書き出す中身を登録しましょう。", "viah-list-empty", "viah-faint"));
                return;
            }
            var tokens = SearchUtil.Tokens(filter);
            var items = all.Where(p => tokens.Length == 0 ||
                                       SearchUtil.Contains(SearchUtil.Normalize(p.name + " " + p.packageName + " " + string.Join(" ", p.tags)), tokens)).ToList();
            if (items.Count == 0) listBox.Add(UIKit.Text("一致するプロファイルはありません", "viah-list-empty", "viah-faint"));
            foreach (var p in items)
            {
                var prof = p;
                var item = UIKit.Div("viah-list-item");
                UIKit.SetClass(item, "is-active", selectedId == p.id);
                item.Add(UIKit.Text(p.name, "viah-list-item-title"));
                var proj = ViahDatabase.FindProject(p.projectId);
                item.Add(UIKit.Text(proj != null ? "プロジェクト：" + proj.name : "プロジェクトなし", "viah-list-item-sub"));
                if (p.tags.Count > 0)
                {
                    var tags = TagField.ReadOnly(p.tags, 3);
                    tags.style.marginTop = 4;
                    item.Add(tags);
                }
                item.RegisterCallback<ClickEvent>(_ =>
                {
                    selectedId = prof.id;
                    RenderList();
                    RenderDetail();
                });
                listBox.Add(item);
            }
        }

        void RenderDetail()
        {
            detail.Clear();
            var p = ViahDatabase.FindProfile(selectedId);
            if (p == null)
            {
                var empty = UIKit.Div("viah-page");
                empty.Add(UIKit.Text("プロファイル", "viah-page-title"));
                empty.Add(UIKit.Text("「何を unitypackage に含めるか」を登録したものです。一度登録すれば、修正のたびにチェックし直す必要はありません。", "viah-page-sub"));
                empty.Add(UIKit.Btn("Toolbar Plus", "最初のプロファイルを作る", () => window.Show(new ProfileWizardPage(null)), "primary", "big"));
                detail.Add(empty);
                return;
            }

            var scroll = UIKit.PageScroll();
            detail.Add(scroll);
            var c = scroll.contentContainer;

            var head = UIKit.Div("viah-page-head");
            head.Add(UIKit.Img("Prefab Icon"));
            head.Add(UIKit.Text(p.name, "viah-page-title", "viah-ml8", "viah-grow"));
            var tools = UIKit.Row("viah-page-head-tools");
            tools.Add(UIKit.Btn("SaveAs", "エクスポートへ", () => window.Show(new ExportPage(p.id)), "primary"));
            head.Add(tools);
            c.Add(head);
            c.Add(UIKit.Text($"パッケージ名：{p.PackageBaseName}", "viah-page-sub"));

            var actions = UIKit.Div("viah-btn-group", "viah-mb8");
            actions.Add(UIKit.Btn("editicon.sml", "中身を編集", () => window.Show(new ProfileWizardPage(p, null, saved => window.Show(new ProfilesPage(saved.id))))));
            actions.Add(UIKit.Btn("複製", () =>
            {
                var copy = p.Clone();
                copy.id = ViahStorage.NewId();
                copy.filePath = null;
                copy.name = p.name + "_copy";
                copy.createdAt = "";
                ViahDatabase.SaveProfile(copy, $"「{p.name}」を複製");
                selectedId = copy.id;
                RenderList();
                RenderDetail();
                window.Toast($"「{copy.name}」として複製しました");
            }));
            actions.Add(UIKit.Btn("JSON に書き出す…", () =>
            {
                var path = EditorUtility.SaveFilePanel("プロファイルを書き出す", "", ViahStorage.SafeFileName(p.name) + ViahStorage.ProfileExt, "json");
                if (string.IsNullOrEmpty(path)) return;
                ViahDatabase.ExportJson(p, path);
                window.Toast("書き出しました", "", "場所を開く", () => ViahStorage.RevealFile(path));
            }));
            actions.Add(UIKit.Spacer());
            actions.Add(UIKit.Btn("削除", () =>
            {
                if (!EditorUtility.DisplayDialog("プロファイルを削除", $"「{p.name}」を削除します。ファイルやフォルダーには影響しません。", "削除", "やめる")) return;
                ViahDatabase.Delete(p);
                selectedId = null;
                window.Show(new ProfilesPage());
            }, "danger"));
            c.Add(actions);

            // タグ
            var tagCard = UIKit.Card();
            tagCard.Add(UIKit.SectionHead(null, "タグ", "検索用のキーワード。変更はすぐ保存されます"));
            var tagField = new TagField(p.tags);
            tagField.changed += t =>
            {
                p.tags = t;
                ViahDatabase.SaveProfile(p, "タグを変更", "tags:" + p.id);
            };
            tagCard.Add(tagField);
            c.Add(tagCard);

            // 概要
            var info = UIKit.Card();
            info.Add(UIKit.SectionHead(null, "概要"));
            var proj = ViahDatabase.FindProject(p.projectId);
            info.Add(UIKit.KeyValue("プロジェクト", proj != null ? proj.name : "なし"));
            info.Add(UIKit.KeyValue("基準フォルダー", ProfileResolver.BaseFolderOf(p) ?? "（見つかりません）"));
            info.Add(UIKit.KeyValue("登録方法", p.mode == "manual" ? "手動" : "自動（" + string.Join("、", p.seeds.Select(s => UIKit.AssetName(s.Resolve() ?? s.path))) + "）"));
            info.Add(UIKit.KeyValue("出力先", ViahStorage.PrettyPath(PackageExporter.OutputDirOf(p))));
            info.Add(UIKit.KeyValue("ルール", $"フォルダーごと {p.includeFolders.Count} ・ ファイル {p.includeFiles.Count} ・ 除外 {p.excludes.Count}"));
            var h = ViahDatabase.History.Get(p.id, false);
            info.Add(UIKit.KeyValue("前回の書き出し", h == null || string.IsNullOrEmpty(h.lastExportAt) ? "まだありません" : $"{h.lastExportAt}　{h.lastFile}"));
            info.Add(UIKit.KeyValue("更新日", p.updatedAt));
            c.Add(info);

            // 中身
            var files = UIKit.Card();
            files.Add(UIKit.SectionHead(null, "含まれるファイル", "いまの状態で書き出したときに入るファイルです"));
            var holder = UIKit.Div();
            files.Add(holder);
            var show = UIKit.Btn("一覧を表示", null, "small");
            show.clicked += () =>
            {
                show.RemoveFromHierarchy();
                var model = SelectionModel.FromProfile(p);
                holder.Add(FileList.Build(model.ResolveFiles(), ProfileResolver.BaseFolderOf(p)));
            };
            files.Add(show);
            c.Add(files);
        }
    }

    /// <summary>ファイル一覧（フォルダーごとにまとめて表示）。</summary>
    public static class FileList
    {
        public static VisualElement Build(List<string> files, string baseFolder, int max = 400)
        {
            var box = UIKit.Div();
            if (files.Count == 0)
            {
                box.Add(UIKit.Text("含まれるファイルはありません", "viah-msg-warn"));
                return box;
            }
            box.Add(UIKit.Text($"{files.Count} ファイル", "viah-dim", "viah-small"));
            int shown = 0;
            foreach (var group in files.GroupBy(f => Path.GetDirectoryName(f)?.Replace('\\', '/')))
            {
                if (shown >= max) break;
                box.Add(UIKit.Text(group.Key, "viah-file-group"));
                foreach (var f in group)
                {
                    if (shown++ >= max) break;
                    var row = UIKit.Row("viah-file-row");
                    row.Add(UIKit.Img(UIKit.AssetIcon(f)));
                    row.Add(UIKit.Text(Path.GetFileName(f), "viah-ellipsis"));
                    row.Add(UIKit.Text(DependencyScanner.KindOf(f), "viah-faint", "viah-small", "viah-ml8"));
                    if (!ProfileResolver.IsInside(f, baseFolder)) row.Add(UIKit.Badge("外部", "ext", "基準フォルダーの外のファイルです"));
                    var path = f;
                    row.RegisterCallback<ClickEvent>(e =>
                    {
                        if (e.clickCount == 2) UIKit.Reveal(path);
                    });
                    row.tooltip = f + "\nダブルクリックで Project ウィンドウに表示";
                    box.Add(row);
                }
            }
            if (files.Count > max) box.Add(UIKit.Text($"…ほか {files.Count - max} ファイル", "viah-faint", "viah-mt4"));
            return box;
        }
    }
}

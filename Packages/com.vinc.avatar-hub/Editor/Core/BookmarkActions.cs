using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VInc.AvatarHub
{
    /// <summary>プロジェクトブックマーカーの操作（⑤の画面と小窓で共通）。変更はすべて Undo できる。</summary>
    public static class BookmarkActions
    {
        public static List<Bookmark> Items => ViahDatabase.Bookmarks.items;

        /// <summary>いまの Unity プロジェクトでのパス。無ければ null（別のプロジェクトで登録した場所など）。</summary>
        public static string PathOf(Bookmark b) => b.target?.Resolve();

        static bool Has(string path)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            return Items.Any(b => (!string.IsNullOrEmpty(guid) && b.target.guid == guid) || string.Equals(b.target.path, path, StringComparison.Ordinal));
        }

        /// <summary>追加する（登録済みのものは飛ばす）。追加した数を返す。</summary>
        public static int Add(IEnumerable<string> paths)
        {
            var added = new List<Bookmark>();
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p) || !p.StartsWith("Assets", StringComparison.Ordinal) || Has(p)) continue;
                var b = new Bookmark { id = ViahStorage.NewId(), target = new AssetRef(p) };
                Items.Add(b);
                added.Add(b);
            }
            if (added.Count > 0)
                ViahDatabase.SaveBookmarks(added.Count == 1 ? $"ブックマーク「{added[0].DisplayLabel}」を追加" : $"ブックマークを {added.Count} 件追加");
            return added.Count;
        }

        public static void Remove(Bookmark b)
        {
            Items.Remove(b);
            ViahDatabase.SaveBookmarks($"ブックマーク「{b.DisplayLabel}」を削除");
        }

        public static void Rename(Bookmark b, string label)
        {
            label = (label ?? "").Trim();
            // フォルダー名と同じなら空にしておく（フォルダー名が変わったときに追いかける）
            if (label == Path.GetFileName(b.target.path)) label = "";
            if (label == b.label) return;
            b.label = label;
            ViahDatabase.SaveBookmarks("ブックマークの名前を変更");
        }

        public static void Move(int index, int dir)
        {
            int to = index + dir;
            if (index < 0 || index >= Items.Count || to < 0 || to >= Items.Count) return;
            var b = Items[index];
            Items.RemoveAt(index);
            Items.Insert(to, b);
            ViahDatabase.SaveBookmarks("ブックマークを並べ替え");
        }

        /// <summary>Project タブをその場所へ切り替える。飛べなかったときは理由を返す。</summary>
        public static string Jump(Bookmark b)
        {
            var path = PathOf(b);
            return path == null ? $"「{b.target.path}」はこの Unity プロジェクトにありません" : ProjectTabs.Jump(path);
        }

        /// <summary>JSON に書き出す（人に渡す・別のプロジェクトで使う）。</summary>
        public static void Export(string file)
        {
            var list = new BookmarkList { items = Items.Select(b => new Bookmark { id = b.id, label = b.label, target = new AssetRef { guid = b.target.guid, path = PathOf(b) ?? b.target.path } }).ToList() };
            ViahStorage.Write(file, list);
        }

        /// <summary>JSON から読み込んで足す（同じ場所のものは飛ばす）。結果のメッセージを返す。</summary>
        public static string Import(string file)
        {
            BookmarkList list;
            try { list = JsonUtility.FromJson<BookmarkList>(File.ReadAllText(file)); }
            catch (Exception e) { return "ファイルを読めませんでした: " + e.Message; }
            if (list == null || list.kind != "AvatarHub.Bookmarks") return "プロジェクトブックマーカーの JSON ではないようです。";
            list.Sanitize();
            int added = 0, same = 0, missing = 0;
            foreach (var b in list.items)
            {
                var path = b.target.Resolve();
                if (Items.Any(x => (!string.IsNullOrEmpty(b.target.guid) && x.target.guid == b.target.guid) || string.Equals(x.target.path, b.target.path, StringComparison.Ordinal)))
                {
                    same++;
                    continue;
                }
                if (path == null) missing++;
                Items.Add(new Bookmark { id = ViahStorage.NewId(), label = b.label, target = b.target });
                added++;
            }
            if (added > 0) ViahDatabase.SaveBookmarks($"ブックマークを {added} 件読み込み");
            var msg = $"{added} 件を読み込みました。";
            if (same > 0) msg += $"同じ場所の {same} 件は登録済みなので飛ばしました。";
            if (missing > 0) msg += $"この Unity プロジェクトに無い場所が {missing} 件あります（同じ場所があるプロジェクトで使えます）。";
            return msg;
        }
    }
}

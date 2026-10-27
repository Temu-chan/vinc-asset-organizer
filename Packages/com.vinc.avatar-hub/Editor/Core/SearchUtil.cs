using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VInc.AvatarHub
{
    public class SearchHit
    {
        public ProjectRecord project;
        public PackageProfile profile;
        public string title;
        public string detail;
        public int score;
    }

    /// <summary>
    /// 日本語のうろ覚え検索用。ひらがな/カタカナ・全角/半角・大文字/小文字・区切り記号の違いを無視して部分一致させる。
    /// </summary>
    public static class SearchUtil
    {
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == ' ' || c == '_' || c == '-' || c == '.' || c == '・' || c == '　') continue;
                if (c >= 'ァ' && c <= 'ヶ') sb.Append((char)(c - 0x60)); // カタカナ → ひらがな
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string[] Tokens(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();
            return query.Split(new[] { ' ', '　', '\t', ',', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .Where(t => t.Length > 0)
                .ToArray();
        }

        public static bool Contains(string normalizedTarget, string[] tokens)
        {
            foreach (var t in tokens)
                if (!normalizedTarget.Contains(t)) return false;
            return true;
        }

        public static List<SearchHit> Search(string query, int max = 12)
        {
            var tokens = Tokens(query);
            var hits = new List<SearchHit>();
            if (tokens.Length == 0) return hits;

            foreach (var p in ViahDatabase.Projects)
            {
                var hit = Match(tokens, p.name, p.tags, p.RootPath ?? p.root.path);
                if (hit == null) continue;
                hit.project = p;
                hit.title = p.name;
                hits.Add(hit);
            }
            foreach (var p in ViahDatabase.Profiles)
            {
                var proj = ViahDatabase.FindProject(p.projectId);
                var hit = Match(tokens, p.name + " " + p.packageName, p.tags, proj?.name ?? "");
                if (hit == null) continue;
                hit.profile = p;
                hit.title = p.name;
                hits.Add(hit);
            }
            return hits.OrderByDescending(h => h.score).ThenBy(h => h.title).Take(max).ToList();
        }

        static SearchHit Match(string[] tokens, string name, List<string> tags, string extra)
        {
            var nName = Normalize(name);
            var nTags = tags.Select(Normalize).ToList();
            var nExtra = Normalize(extra);
            int score = 0;
            var matchedTags = new List<string>();
            foreach (var t in tokens)
            {
                bool any = false;
                if (nName.Contains(t)) { score += 3; any = true; }
                for (int i = 0; i < nTags.Count; i++)
                {
                    if (!nTags[i].Contains(t)) continue;
                    score += 2;
                    any = true;
                    if (!matchedTags.Contains(tags[i])) matchedTags.Add(tags[i]);
                }
                if (!any && nExtra.Contains(t)) { score += 1; any = true; }
                if (!any) return null;
            }
            return new SearchHit
            {
                score = score,
                detail = matchedTags.Count > 0 ? "タグ: " + string.Join("、", matchedTags) : extra,
            };
        }
    }
}

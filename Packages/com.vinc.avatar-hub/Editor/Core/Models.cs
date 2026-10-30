using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VInc.AvatarHub
{
    /// <summary>JSON として保存されるデータの共通部分。</summary>
    public interface IStored
    {
        string FilePath { get; set; }
        string DisplayName { get; }
        string Id { get; set; }
        void Sanitize();
    }

    /// <summary>GUID とパスの両方で覚えておくアセット参照。移動・リネームされても GUID で追いかける。</summary>
    [Serializable]
    public class AssetRef
    {
        public string guid = "";
        public string path = "";

        public AssetRef() { }

        public AssetRef(string assetPath)
        {
            path = assetPath ?? "";
            guid = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
        }

        public bool IsEmpty => string.IsNullOrEmpty(guid) && string.IsNullOrEmpty(path);

        /// <summary>いまのパスを返す。見つからなければ null。</summary>
        public string Resolve()
        {
            if (!string.IsNullOrEmpty(guid))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (Exists(p))
                {
                    path = p;
                    return p;
                }
            }
            if (Exists(path))
            {
                guid = AssetDatabase.AssetPathToGUID(path);
                return path;
            }
            return null;
        }

        public static bool Exists(string p) => !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p));

        public static List<AssetRef> FromPaths(IEnumerable<string> paths)
        {
            var list = new List<AssetRef>();
            foreach (var p in paths) list.Add(new AssetRef(p));
            return list;
        }
    }

    // ───────────────────────── フォルダー構成プリセット ─────────────────────────

    [Serializable]
    public class PresetNode
    {
        public string id;
        /// <summary>空ならルート（ユーザーが入力した名前のフォルダー）の直下。</summary>
        public string parentId = "";
        public string name;
        /// <summary>新規作成時に最初からチェックを入れておくか。</summary>
        public bool defaultOn = true;

        public PresetNode Clone() => (PresetNode)MemberwiseClone();
    }

    [Serializable]
    public class FolderPreset : IStored
    {
        public string kind = "AvatarHub.Preset";
        public int schema = 1;
        public string id;
        public string name;
        public string description = "";
        public bool numberPrefix;
        public List<PresetNode> nodes = new List<PresetNode>();

        [NonSerialized] public string filePath;
        public string FilePath { get => filePath; set => filePath = value; }
        public string DisplayName => name;
        public string Id { get => id; set => id = value; }

        public void Sanitize()
        {
            if (string.IsNullOrEmpty(id)) id = ViahStorage.NewId();
            if (string.IsNullOrEmpty(name)) name = "名称未設定";
            description ??= "";
            nodes ??= new List<PresetNode>();
            foreach (var n in nodes)
            {
                if (string.IsNullOrEmpty(n.id)) n.id = ViahStorage.NewId();
                n.parentId ??= "";
                n.name ??= "";
            }
        }

        public FolderPreset Clone()
        {
            var c = JsonUtility.FromJson<FolderPreset>(JsonUtility.ToJson(this));
            c.filePath = filePath;
            return c;
        }
    }

    // ───────────────────────── 作成したプロジェクトの記録 ─────────────────────────

    [Serializable]
    public class ProjectRecord : IStored
    {
        public string kind = "AvatarHub.Project";
        public int schema = 1;
        public string id;
        public string name;
        public AssetRef root = new AssetRef();
        public List<string> tags = new List<string>();
        public string presetName = "";
        public bool numberPrefix;
        /// <summary>作成に使ったフォルダー構成。あとからフォルダーを追加するときに使う。</summary>
        public List<PresetNode> structure = new List<PresetNode>();
        public string createdAt = "";
        /// <summary>作成した Unity プロジェクトのフォルダー名（別のプロジェクトで開いたときの表示用）。</summary>
        public string unityProject = "";

        [NonSerialized] public string filePath;
        public string FilePath { get => filePath; set => filePath = value; }
        public string DisplayName => name;
        public string Id { get => id; set => id = value; }

        public string RootPath => root?.Resolve();

        public void Sanitize()
        {
            if (string.IsNullOrEmpty(id)) id = ViahStorage.NewId();
            name ??= "";
            root ??= new AssetRef();
            tags ??= new List<string>();
            structure ??= new List<PresetNode>();
            presetName ??= "";
            createdAt ??= "";
            unityProject ??= "";
        }
    }

    // ───────────────────────── パッケージのプロファイル ─────────────────────────

    [Serializable]
    public class PackageProfile : IStored
    {
        public string kind = "AvatarHub.Profile";
        public int schema = 1;
        public string id;
        public string name;
        public string packageName;
        public List<string> tags = new List<string>();
        public string projectId = "";
        /// <summary>この外にある依存は「外部」として扱う。</summary>
        public AssetRef baseFolder = new AssetRef();
        /// <summary>空なら既定の出力先。</summary>
        public string outputFolder = "";
        /// <summary>"auto" または "manual"。</summary>
        public string mode = "auto";
        /// <summary>自動検出の起点にしたプレハブ・フォルダー。</summary>
        public List<AssetRef> seeds = new List<AssetRef>();
        /// <summary>中身ぜんぶ（今後増えるファイルも）を含めるフォルダー。</summary>
        public List<AssetRef> includeFolders = new List<AssetRef>();
        public List<AssetRef> includeFiles = new List<AssetRef>();
        /// <summary>含めないファイル・フォルダー（フォルダールールの例外）。</summary>
        public List<AssetRef> excludes = new List<AssetRef>();
        /// <summary>使われているが、確認したうえで含めないことにした依存。警告を出さない。</summary>
        public List<AssetRef> ignoredDependencies = new List<AssetRef>();
        public string createdAt = "";
        public string updatedAt = "";
        /// <summary>作成した Unity プロジェクトのフォルダー名（別のプロジェクトで開いたときの表示用）。</summary>
        public string unityProject = "";

        [NonSerialized] public string filePath;
        public string FilePath { get => filePath; set => filePath = value; }
        public string DisplayName => name;
        public string Id { get => id; set => id = value; }

        public string PackageBaseName => string.IsNullOrWhiteSpace(packageName) ? name : packageName;

        public void Sanitize()
        {
            if (string.IsNullOrEmpty(id)) id = ViahStorage.NewId();
            name ??= "";
            packageName ??= "";
            tags ??= new List<string>();
            projectId ??= "";
            baseFolder ??= new AssetRef();
            outputFolder ??= "";
            if (mode != "manual") mode = "auto";
            seeds ??= new List<AssetRef>();
            includeFolders ??= new List<AssetRef>();
            includeFiles ??= new List<AssetRef>();
            excludes ??= new List<AssetRef>();
            ignoredDependencies ??= new List<AssetRef>();
            createdAt ??= "";
            updatedAt ??= "";
            unityProject ??= "";
        }

        public PackageProfile Clone()
        {
            var c = JsonUtility.FromJson<PackageProfile>(JsonUtility.ToJson(this));
            c.filePath = filePath;
            c.Sanitize();
            return c;
        }
    }

    // ───────────────────────── 設定（PC 共通） ─────────────────────────

    /// <summary>プロファイルごとに覚えておく、名前に付ける要素の ON/OFF。</summary>
    [Serializable]
    public class ProfilePrefs
    {
        public string profileId;
        public bool useVersion = true;
        public bool useDate = true;
        public bool useTime;
        public bool useDebug;
    }

    [Serializable]
    public class ViahSettings
    {
        public string kind = "AvatarHub.Settings";
        public int schema = 2;
        /// <summary>"unity"（Unity のグレー）/ "dark" / "light"</summary>
        public string theme = "dark";
        /// <summary>"blue" / "cyan" / "green" / "orange" / "red" / "purple" / "pink"</summary>
        public string accent = "blue";
        /// <summary>"float"（独立したウィンドウ・おすすめ）/ "tab"（Unity のタブとして開く）</summary>
        public string windowMode = "float";
        /// <summary>サイドバーに表示しないワークフローのキー。</summary>
        public List<string> hiddenWorkflows = new List<string>();
        public string debugLabel = "Debug";
        /// <summary>空なら &lt;Unityプロジェクト&gt;/Exports。</summary>
        public string defaultOutputFolder = "";
        public bool seqUseVersion = true;
        public bool seqUseDate = true;
        public bool seqUseTime;
        public bool seqUseDebug;
        public List<ProfilePrefs> profilePrefs = new List<ProfilePrefs>();

        // 時間のかかる作業が終わったとき・確認待ちで止まったときの効果音
        public bool soundOn = true;
        /// <summary>0〜1</summary>
        public float soundVolume = 0.6f;
        /// <summary>内蔵の音のキー（ViahSound.Presets）、または "file"（Sounds フォルダーの soundDoneFile）</summary>
        public string soundDone = "pirorin";
        public string soundDoneFile = "";
        public string soundAttention = "pinpon";
        public string soundAttentionFile = "";
        /// <summary>鳴らさない作業のキー（ViahSound.Events）。</summary>
        public List<string> soundMuted = new List<string>();
        /// <summary>すぐ終わったとき（3 秒未満）は鳴らさない。</summary>
        public bool soundSkipShort = true;

        public ProfilePrefs Prefs(string profileId, bool create)
        {
            profilePrefs ??= new List<ProfilePrefs>();
            var r = profilePrefs.Find(h => h.profileId == profileId);
            if (r == null && create)
            {
                r = new ProfilePrefs { profileId = profileId };
                profilePrefs.Add(r);
            }
            return r;
        }

        public void Sanitize()
        {
            if (theme != "unity" && theme != "light") theme = "dark";
            if (string.IsNullOrEmpty(accent)) accent = "blue";
            // schema 1 は「タブ」が既定だった。自分で選んだわけではないので、新しい既定（独立ウィンドウ）にする
            if (schema < 2)
            {
                windowMode = "float";
                schema = 2;
            }
            if (windowMode != "tab") windowMode = "float";
            hiddenWorkflows ??= new List<string>();
            if (string.IsNullOrWhiteSpace(debugLabel)) debugLabel = "Debug";
            defaultOutputFolder ??= "";
            profilePrefs ??= new List<ProfilePrefs>();
            soundVolume = Mathf.Clamp01(soundVolume);
            if (string.IsNullOrEmpty(soundDone)) soundDone = "pirorin";
            if (string.IsNullOrEmpty(soundAttention)) soundAttention = "pinpon";
            soundDoneFile ??= "";
            soundAttentionFile ??= "";
            soundMuted ??= new List<string>();
        }
    }

    // ───────────────────────── プロジェクトブックマーカー ─────────────────────────

    /// <summary>Project ウィンドウで飛びたいフォルダー（またはアセット）。</summary>
    [Serializable]
    public class Bookmark
    {
        public string id;
        /// <summary>表示名（空ならフォルダー名）。</summary>
        public string label = "";
        /// <summary>
        /// GUID とパス。別の Unity プロジェクトでは GUID が違うので、同じパスがあるときだけ使える。
        /// </summary>
        public AssetRef target = new AssetRef();

        public string DisplayLabel => !string.IsNullOrWhiteSpace(label) ? label : Path.GetFileName(target?.path ?? "");
    }

    /// <summary>ブックマークの一覧（PC 共通・JSON で人に渡せる）。</summary>
    [Serializable]
    public class BookmarkList
    {
        public string kind = "AvatarHub.Bookmarks";
        public int schema = 1;
        public List<Bookmark> items = new List<Bookmark>();

        public void Sanitize()
        {
            items ??= new List<Bookmark>();
            items.RemoveAll(b => b == null);
            foreach (var b in items)
            {
                if (string.IsNullOrEmpty(b.id)) b.id = ViahStorage.NewId();
                b.label ??= "";
                b.target ??= new AssetRef();
            }
        }
    }

    /// <summary>独立ウィンドウの前回の位置と大きさ（Undo の対象外）。</summary>
    [Serializable]
    public class WindowState
    {
        public string kind = "AvatarHub.Window";
        public bool has;
        public float x, y, width, height;
    }

    // ───────────────────────── 書き出し履歴（Undo の対象外） ─────────────────────────

    [Serializable]
    public class HistoryEntry
    {
        public string profileId;
        public string lastVersion = "";
        public string lastExportAt = "";
        public string lastFile = "";
    }

    [Serializable]
    public class ExportHistory
    {
        public string kind = "AvatarHub.History";
        public List<HistoryEntry> entries = new List<HistoryEntry>();

        public HistoryEntry Get(string profileId, bool create)
        {
            entries ??= new List<HistoryEntry>();
            var r = entries.Find(h => h.profileId == profileId);
            if (r == null && create)
            {
                r = new HistoryEntry { profileId = profileId };
                entries.Add(r);
            }
            return r;
        }
    }

    // ───────────────────────── 旧形式（移行用） ─────────────────────────

    [Serializable]
    class LegacyExportRecord
    {
        public string profileId;
        public string lastVersion = "";
        public string lastExportAt = "";
        public string lastFile = "";
        public bool useVersion = true;
        public bool useDate = true;
        public bool useTime;
        public bool useDebug;
    }

    [Serializable]
    class LegacyState
    {
        public string debugLabel = "Debug";
        public string defaultOutputFolder = "";
        public bool seqUseVersion = true;
        public bool seqUseDate = true;
        public bool seqUseTime;
        public bool seqUseDebug;
        public List<LegacyExportRecord> history = new List<LegacyExportRecord>();
    }
}
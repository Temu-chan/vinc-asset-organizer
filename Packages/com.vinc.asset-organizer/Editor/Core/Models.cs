using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VInc.AssetOrganizer
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
        public string kind = "AssetOrganizer.Preset";
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
            if (string.IsNullOrEmpty(id)) id = AOStorage.NewId();
            if (string.IsNullOrEmpty(name)) name = "名称未設定";
            description ??= "";
            nodes ??= new List<PresetNode>();
            foreach (var n in nodes)
            {
                if (string.IsNullOrEmpty(n.id)) n.id = AOStorage.NewId();
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
        public string kind = "AssetOrganizer.Project";
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

        [NonSerialized] public string filePath;
        public string FilePath { get => filePath; set => filePath = value; }
        public string DisplayName => name;
        public string Id { get => id; set => id = value; }

        public string RootPath => root?.Resolve();

        public void Sanitize()
        {
            if (string.IsNullOrEmpty(id)) id = AOStorage.NewId();
            name ??= "";
            root ??= new AssetRef();
            tags ??= new List<string>();
            structure ??= new List<PresetNode>();
            presetName ??= "";
            createdAt ??= "";
        }
    }

    // ───────────────────────── パッケージのプロファイル ─────────────────────────

    [Serializable]
    public class PackageProfile : IStored
    {
        public string kind = "AssetOrganizer.Profile";
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

        [NonSerialized] public string filePath;
        public string FilePath { get => filePath; set => filePath = value; }
        public string DisplayName => name;
        public string Id { get => id; set => id = value; }

        public string PackageBaseName => string.IsNullOrWhiteSpace(packageName) ? name : packageName;

        public void Sanitize()
        {
            if (string.IsNullOrEmpty(id)) id = AOStorage.NewId();
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
        }

        public PackageProfile Clone()
        {
            var c = JsonUtility.FromJson<PackageProfile>(JsonUtility.ToJson(this));
            c.filePath = filePath;
            c.Sanitize();
            return c;
        }
    }

    // ───────────────────────── エクスポート履歴・設定 ─────────────────────────

    [Serializable]
    public class ExportRecord
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
    public class OrganizerState
    {
        public string kind = "AssetOrganizer.State";
        public string debugLabel = "Debug";
        /// <summary>空なら &lt;プロジェクト&gt;/Exports。</summary>
        public string defaultOutputFolder = "";
        public bool seqUseVersion = true;
        public bool seqUseDate = true;
        public bool seqUseTime;
        public bool seqUseDebug;
        public List<ExportRecord> history = new List<ExportRecord>();

        public ExportRecord Get(string profileId, bool create)
        {
            history ??= new List<ExportRecord>();
            var r = history.Find(h => h.profileId == profileId);
            if (r == null && create)
            {
                r = new ExportRecord { profileId = profileId };
                history.Add(r);
            }
            return r;
        }
    }
}

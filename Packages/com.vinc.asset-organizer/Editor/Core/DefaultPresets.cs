using System.Collections.Generic;

namespace VInc.AssetOrganizer
{
    /// <summary>はじめて起動したときに用意するプリセット。</summary>
    public static class DefaultPresets
    {
        public static List<FolderPreset> Create()
        {
            return new List<FolderPreset> { Standard(), Minimal() };
        }

        public static FolderPreset Standard()
        {
            var p = new FolderPreset
            {
                id = AOStorage.NewId(),
                name = "VRChatギミック標準",
                description = "VRChat 向けギミックでよく使うフォルダー一式。必要なものにチェックを入れて使います。",
            };
            var b = new Builder(p);
            b.Add("Prefabs");
            var anim = b.Add("Animations");
            b.Add("Controllers", anim);
            b.Add("Materials");
            b.Add("Textures");
            b.Add("Models", null, false);
            b.Add("Shaders", null, false);
            b.Add("Expressions");
            b.Add("Audio", null, false);
            var scripts = b.Add("Scripts", null, false);
            b.Add("Editor", scripts, false);
            b.Add("Scenes", null, false);
            return p;
        }

        public static FolderPreset Minimal()
        {
            var p = new FolderPreset
            {
                id = AOStorage.NewId(),
                name = "最小構成",
                description = "よく使う 4 フォルダーだけのシンプルな構成。",
            };
            var b = new Builder(p);
            b.Add("Prefabs");
            b.Add("Animations");
            b.Add("Materials");
            b.Add("Textures");
            return p;
        }

        class Builder
        {
            readonly FolderPreset preset;
            public Builder(FolderPreset preset) { this.preset = preset; }

            public PresetNode Add(string name, PresetNode parent = null, bool on = true)
            {
                var n = new PresetNode
                {
                    id = AOStorage.NewId(),
                    parentId = parent?.id ?? "",
                    name = name,
                    defaultOn = on,
                };
                preset.nodes.Add(n);
                return n;
            }
        }
    }
}

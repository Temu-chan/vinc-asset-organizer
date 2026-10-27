using UnityEditor;

namespace VInc.AvatarHub
{
    /// <summary>ツール内の画像（ロゴ・ガイドのスクリーンショット）を、ぼやけない設定で読み込む。</summary>
    class ViahTextureImport : AssetPostprocessor
    {
        const string Root = "Packages/com.vinc.avatar-hub/Editor/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.GUI;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.maxTextureSize = 4096;
        }
    }
}

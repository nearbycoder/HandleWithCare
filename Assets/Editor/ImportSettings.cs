using UnityEditor;
using UnityEngine;

namespace HWC.EditorTools
{
    /// <summary>Import settings for the Blender FBX models, generated textures and synthesized audio.</summary>
    public class ImportSettings : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Contains("/Resources/Models/")) return;
            var m = (ModelImporter)assetImporter;
            m.globalScale = 1f;
            m.useFileScale = true;
            m.bakeAxisConversion = false;
            m.importAnimation = false;
            m.animationType = ModelImporterAnimationType.None;
            m.importCameras = false;
            m.importLights = false;
            m.importBlendShapes = false;
            m.importVisibility = false;
            m.addCollider = false;
            m.isReadable = false;
            m.preserveHierarchy = false;
            m.importNormals = ModelImporterNormals.Import;
            m.importTangents = ModelImporterTangents.CalculateMikk;
            m.meshCompression = ModelImporterMeshCompression.Off;
            m.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/Textures/")) return;
            var t = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            t.mipmapEnabled = true;
            t.anisoLevel = 8;
            t.textureCompression = TextureImporterCompression.CompressedHQ;
            if (file.EndsWith("_n"))
            {
                t.textureType = TextureImporterType.NormalMap;
                t.sRGBTexture = false;
            }
            else
            {
                t.textureType = TextureImporterType.Default;
                t.sRGBTexture = !file.EndsWith("_mask");
            }
            if (assetPath.Contains("/Textures/Baked/")) t.maxTextureSize = 2048;
            bool clamp = file.StartsWith("decal_") || file == "label";
            t.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            bool foliage = file.StartsWith("foliage_") && !file.EndsWith("_n");
            t.alphaIsTransparency = file.StartsWith("decal_") || file == "clouds" || foliage;
            // keep alpha-tested leaf cards from thinning out with distance
            t.mipMapsPreserveCoverage = foliage;
            if (foliage) t.alphaTestReferenceValue = 0.45f;
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.Contains("/Resources/Audio/")) return;
            var a = (AudioImporter)assetImporter;
            var s = a.defaultSampleSettings;
            bool music = assetPath.Contains("/Music/");
            a.forceToMono = !music;
            a.loadInBackground = music;
            s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = music ? 0.75f : 0.85f;
            s.preloadAudioData = !music;
            a.defaultSampleSettings = s;
        }
    }
}

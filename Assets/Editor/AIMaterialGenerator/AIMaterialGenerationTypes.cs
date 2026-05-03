using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIMaterialGenerator
{
    public enum AIMaterialBackend
    {
        ComfyUI,
        OnlineImageApi
    }

    public enum AIMaterialGenerationMode
    {
        TextToBaseColor,
        TextToBaseColorNormal,
        TextToPbr,
        ImageToNormal,
        ImageToPbr,
        EditImageToBaseColor,
        EditImageToBaseColorNormal,
        EditImageToPbr
    }

    public enum AIMaterialTargetAction
    {
        PreviewOnly,
        ApplyToSelectedRenderer,
        ReplaceSelectedMaterial
    }

    public enum AIMaterialMapType
    {
        BaseColor,
        Normal,
        Roughness,
        Height,
        Metallic,
        Specular,
        Occlusion,
        MaskMap
    }

    public enum AIMaterialSizeSource
    {
        Default,
        SourceTexture,
        TargetMaterial,
        UserInput
    }

    [Serializable]
    public sealed class AIMaterialGenerationRequest
    {
        public AIMaterialBackend backend;
        public AIMaterialGenerationMode mode;
        public AIMaterialTargetAction targetAction;
        public string prompt;
        public string negativePrompt;
        public int width = 512;
        public int height = 512;
        public long seed;
        public bool randomSeed = true;
        public string slug = "material";
        public string sourceTexturePath;
        public Material targetMaterial;
        public Renderer targetRenderer;
        public AIMaterialSizeSource sizeSource;
    }

    [Serializable]
    public sealed class AIMaterialTextureResult
    {
        public AIMaterialMapType mapType;
        public string assetPath;
        public Texture2D texture;

        public AIMaterialTextureResult(AIMaterialMapType mapType, string assetPath)
        {
            this.mapType = mapType;
            this.assetPath = assetPath;
        }
    }

    [Serializable]
    public sealed class AIMaterialGenerationResult
    {
        public string runName;
        public string runDirectory;
        public string materialPath;
        public Material material;
        public readonly List<AIMaterialTextureResult> textures = new List<AIMaterialTextureResult>();

        public Texture2D GetTexture(AIMaterialMapType mapType)
        {
            foreach (var result in textures)
            {
                if (result.mapType == mapType)
                    return result.texture;
            }

            return null;
        }

        public string GetTexturePath(AIMaterialMapType mapType)
        {
            foreach (var result in textures)
            {
                if (result.mapType == mapType)
                    return result.assetPath;
            }

            return string.Empty;
        }
    }
}

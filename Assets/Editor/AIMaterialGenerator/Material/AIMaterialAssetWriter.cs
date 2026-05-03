using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AIMaterialGenerator
{
    static class AIMaterialAssetWriter
    {
        public static AIMaterialGenerationResult WriteResult(AIMaterialGenerationRequest request, IReadOnlyList<Comfy.ComfyDownloadedImage> images, Texture2D sourceBaseColor)
        {
            var settings = AIMaterialGenerationSettings.instance;
            var outputDirectory = settings.SafeAiOutputDirectory;
            AIMaterialTextureUtility.EnsureDirectory(outputDirectory);

            var result = new AIMaterialGenerationResult();
            var slug = Comfy.ComfyWorkflowMapper.SanitizeFileName(request.slug);
            result.runName = "AI_" + slug + "_" + DateTime.Now.ToString("HHmmss");
            result.runDirectory = outputDirectory;

            foreach (var image in images)
            {
                var mapName = MapName(image.mapType);
                var path = AssetDatabase.GenerateUniqueAssetPath($"{outputDirectory}/{result.runName}_{mapName}.png");
                File.WriteAllBytes(Path.GetFullPath(path), image.bytes);
                AIMaterialTextureUtility.ConfigureImporter(path, image.mapType);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                result.textures.Add(new AIMaterialTextureResult(image.mapType, path) { texture = texture });
            }

            if (sourceBaseColor != null && string.IsNullOrEmpty(result.GetTexturePath(AIMaterialMapType.BaseColor)))
                result.textures.Add(new AIMaterialTextureResult(AIMaterialMapType.BaseColor, AssetDatabase.GetAssetPath(sourceBaseColor)) { texture = sourceBaseColor });

            var maskPath = CreateMaskMapIfPossible(result);
            if (!string.IsNullOrEmpty(maskPath))
            {
                var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
                result.textures.Add(new AIMaterialTextureResult(AIMaterialMapType.MaskMap, maskPath) { texture = mask });
            }

            var specGlossPath = CreateSpecGlossMapIfPossible(result);
            if (!string.IsNullOrEmpty(specGlossPath))
            {
                var specGloss = AssetDatabase.LoadAssetAtPath<Texture2D>(specGlossPath);
                result.textures.Add(new AIMaterialTextureResult(AIMaterialMapType.Specular, specGlossPath) { texture = specGloss });
            }

            var material = CreateMaterial(request.targetMaterial);
            URPMaterialMapAssigner.AssignMaps(material, result);
            result.materialPath = AssetDatabase.GenerateUniqueAssetPath($"{outputDirectory}/{result.runName}.mat");
            AssetDatabase.CreateAsset(material, result.materialPath);
            result.material = AssetDatabase.LoadAssetAtPath<Material>(result.materialPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return result;
        }

        public static void MoveResultToDirectory(AIMaterialGenerationResult result, string targetDirectory)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            AIMaterialTextureUtility.EnsureDirectory(targetDirectory);
            var aiTempDirectory = AIMaterialGenerationSettings.instance.SafeAiOutputDirectory.TrimEnd('/').Replace('\\', '/');

            foreach (var texture in result.textures)
            {
                if (string.IsNullOrEmpty(texture.assetPath) || !texture.assetPath.StartsWith("Assets/"))
                    continue;

                var current = texture.assetPath;
                if (!current.Replace('\\', '/').StartsWith(aiTempDirectory + "/", StringComparison.Ordinal))
                    continue;

                if (Path.GetDirectoryName(current)?.Replace('\\', '/') == targetDirectory)
                    continue;

                var target = AssetDatabase.GenerateUniqueAssetPath(targetDirectory.TrimEnd('/') + "/" + Path.GetFileName(current));
                var error = AssetDatabase.MoveAsset(current, target);
                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException(error);

                texture.assetPath = target;
                texture.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(target);
            }

            if (!string.IsNullOrEmpty(result.materialPath) && result.materialPath.StartsWith("Assets/"))
            {
                var target = AssetDatabase.GenerateUniqueAssetPath(targetDirectory.TrimEnd('/') + "/" + Path.GetFileName(result.materialPath));
                var error = AssetDatabase.MoveAsset(result.materialPath, target);
                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException(error);

                result.materialPath = target;
                result.material = AssetDatabase.LoadAssetAtPath<Material>(target);
                URPMaterialMapAssigner.AssignMaps(result.material, result);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void CopySelectedTexturesToMaterialDirectory(
            Material targetMaterial,
            AIMaterialGenerationResult result,
            bool baseColor,
            bool normal,
            bool workflowMap,
            bool height,
            bool occlusion)
        {
            if (targetMaterial == null)
                throw new ArgumentNullException(nameof(targetMaterial));
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var materialPath = AssetDatabase.GetAssetPath(targetMaterial);
            if (string.IsNullOrEmpty(materialPath) || !materialPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("目标材质必须是 Assets 下的材质资源，才能复制贴图到同目录。");

            var directory = Path.GetDirectoryName(materialPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("无法确定目标材质目录。");

            var materialName = Comfy.ComfyWorkflowMapper.SanitizeFileName(Path.GetFileNameWithoutExtension(materialPath));

            if (baseColor)
                CopyMap(result, AIMaterialMapType.BaseColor, directory, materialName, "basecolor");
            if (normal)
                CopyMap(result, AIMaterialMapType.Normal, directory, materialName, "normal");
            if (height)
                CopyMap(result, AIMaterialMapType.Height, directory, materialName, "height");
            if (occlusion)
                CopyMap(result, AIMaterialMapType.Occlusion, directory, materialName, "ao");

            if (workflowMap)
            {
                if (IsSpecularWorkflow(targetMaterial))
                    CopyMap(result, AIMaterialMapType.Specular, directory, materialName, "specgloss");
                else
                    CopyMap(result, AIMaterialMapType.MaskMap, directory, materialName, "maskmap");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void CopyMap(AIMaterialGenerationResult result, AIMaterialMapType mapType, string directory, string materialName, string suffix)
        {
            foreach (var texture in result.textures)
            {
                if (texture.mapType != mapType || string.IsNullOrEmpty(texture.assetPath))
                    continue;

                var source = texture.assetPath.Replace('\\', '/');
                if (!source.StartsWith("Assets/", StringComparison.Ordinal))
                    return;

                var extension = Path.GetExtension(source);
                if (string.IsNullOrEmpty(extension))
                    extension = ".png";

                var target = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{materialName}_{suffix}{extension}");
                if (!AssetDatabase.CopyAsset(source, target))
                    throw new InvalidOperationException($"复制贴图失败：{source} -> {target}");

                AIMaterialTextureUtility.ConfigureImporter(target, mapType);
                texture.assetPath = target;
                texture.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(target);
                return;
            }
        }

        static bool IsSpecularWorkflow(Material material)
        {
            if (material.HasProperty("_WorkflowMode"))
                return Mathf.Approximately(material.GetFloat("_WorkflowMode"), 0f);

            return material.HasProperty("_SpecGlossMap") && !material.HasProperty("_MetallicGlossMap");
        }

        static Material CreateMaterial(Material template)
        {
            if (template != null)
                return new Material(template);

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            return new Material(shader);
        }

        static string CreateMaskMapIfPossible(AIMaterialGenerationResult result)
        {
            var roughness = result.GetTexture(AIMaterialMapType.Roughness);
            var metallic = result.GetTexture(AIMaterialMapType.Metallic);
            var occlusion = result.GetTexture(AIMaterialMapType.Occlusion);
            if (roughness == null && metallic == null && occlusion == null)
                return string.Empty;

            var reference = roughness != null ? roughness : metallic != null ? metallic : occlusion;
            var width = reference.width;
            var height = reference.height;
            var mask = new Texture2D(width, height, TextureFormat.RGBA32, false, true);

            var roughPixels = ReadPixelsOrDefault(roughness, width, height, Color.black);
            var metallicPixels = ReadPixelsOrDefault(metallic, width, height, Color.black);
            var occlusionPixels = ReadPixelsOrDefault(occlusion, width, height, Color.white);
            var output = new Color[width * height];

            for (var i = 0; i < output.Length; i++)
            {
                var metal = metallicPixels[i].grayscale;
                var ao = occlusionPixels[i].grayscale;
                var smoothness = 1f - roughPixels[i].grayscale;
                output[i] = new Color(metal, ao, 0f, smoothness);
            }

            mask.SetPixels(output);
            mask.Apply();

            var path = AssetDatabase.GenerateUniqueAssetPath($"{result.runDirectory}/{result.runName}_maskmap.png");
            File.WriteAllBytes(Path.GetFullPath(path), mask.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(mask);
            AIMaterialTextureUtility.ConfigureImporter(path, AIMaterialMapType.MaskMap);
            return path;
        }

        static string CreateSpecGlossMapIfPossible(AIMaterialGenerationResult result)
        {
            var roughness = result.GetTexture(AIMaterialMapType.Roughness);
            var metallic = result.GetTexture(AIMaterialMapType.Metallic);
            var specular = result.GetTexture(AIMaterialMapType.Specular);
            if (roughness == null && metallic == null && specular == null)
                return string.Empty;

            var reference = roughness != null ? roughness : specular != null ? specular : metallic;
            var width = reference.width;
            var height = reference.height;
            var output = new Color[width * height];
            var roughPixels = ReadPixelsOrDefault(roughness, width, height, Color.black);
            var specPixels = ReadPixelsOrDefault(specular, width, height, Color.clear);
            var metalPixels = ReadPixelsOrDefault(metallic, width, height, new Color(0.2f, 0.2f, 0.2f, 1f));
            var hasSpecular = specular != null;

            for (var i = 0; i < output.Length; i++)
            {
                var smoothness = 1f - roughPixels[i].grayscale;
                var spec = hasSpecular ? specPixels[i] : new Color(metalPixels[i].grayscale, metalPixels[i].grayscale, metalPixels[i].grayscale, 1f);
                output[i] = new Color(spec.r, spec.g, spec.b, smoothness);
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.SetPixels(output);
            texture.Apply();

            var path = AssetDatabase.GenerateUniqueAssetPath($"{result.runDirectory}/{result.runName}_specgloss.png");
            File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AIMaterialTextureUtility.ConfigureImporter(path, AIMaterialMapType.Specular);
            return path;
        }

        static Color[] ReadPixelsOrDefault(Texture2D texture, int width, int height, Color fallback)
        {
            if (texture == null)
            {
                var colors = new Color[width * height];
                for (var i = 0; i < colors.Length; i++)
                    colors[i] = fallback;
                return colors;
            }

            var previous = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply();
                var pixels = readable.GetPixels();
                UnityEngine.Object.DestroyImmediate(readable);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static string MapName(AIMaterialMapType mapType)
        {
            switch (mapType)
            {
                case AIMaterialMapType.BaseColor: return "basecolor";
                case AIMaterialMapType.Normal: return "normal";
                case AIMaterialMapType.Roughness: return "roughness";
                case AIMaterialMapType.Height: return "height";
                case AIMaterialMapType.Metallic: return "metallic";
                case AIMaterialMapType.Specular: return "specular";
                case AIMaterialMapType.Occlusion: return "ao";
                case AIMaterialMapType.MaskMap: return "maskmap";
                default: return "texture";
            }
        }
    }
}

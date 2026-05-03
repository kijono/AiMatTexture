using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AIMaterialGenerator
{
    static class AIMaterialTextureUtility
    {
        public static bool TryGetTextureSize(Texture texture, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (texture == null)
                return false;

            width = texture.width;
            height = texture.height;
            return width > 0 && height > 0;
        }

        public static string ExportTextureForUpload(string textureAssetPath, string outputDirectory, string runPrefix)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(textureAssetPath);
            if (texture == null)
                throw new InvalidOperationException("Source texture not found: " + textureAssetPath);

            EnsureDirectory(outputDirectory);
            var targetPath = AssetDatabase.GenerateUniqueAssetPath($"{outputDirectory}/{runPrefix}_source.png");
            var bytes = EncodeTextureToPng(texture);
            File.WriteAllBytes(Path.GetFullPath(targetPath), bytes);
            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
            return targetPath;
        }

        public static byte[] EncodeTextureToPng(Texture2D source)
        {
            var previous = RenderTexture.active;
            var renderTexture = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

            try
            {
                Graphics.Blit(source, renderTexture);
                RenderTexture.active = renderTexture;
                var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false);
                readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                readable.Apply();
                var bytes = readable.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(readable);
                return bytes;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        public static void EnsureDirectory(string assetDirectory)
        {
            if (string.IsNullOrWhiteSpace(assetDirectory))
                throw new InvalidOperationException("Output directory is empty.");

            var normalized = assetDirectory.Replace('\\', '/').Trim('/');
            if (!normalized.StartsWith("Assets", StringComparison.Ordinal))
                throw new InvalidOperationException("Output directory must be under Assets: " + assetDirectory);

            var parts = normalized.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        public static void ConfigureImporter(string assetPath, AIMaterialMapType mapType)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
                return;

            importer.textureType = mapType == AIMaterialMapType.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = mapType == AIMaterialMapType.BaseColor;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
    }
}

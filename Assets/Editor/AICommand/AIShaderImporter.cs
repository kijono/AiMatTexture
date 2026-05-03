using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;
using System.IO;

namespace AIShader
{

    [ScriptedImporter(1, Extension)]
    sealed class AIShaderImporter : ScriptedImporter
    {
        public const string Extension = "aishader";

#pragma warning disable CS0414

        [SerializeField, TextArea(3, 20)]
        string _prompt = "Simple solid fill shader. The color is exposed as a property.";

        [SerializeField, TextArea(3, 20)]
        string _cached = null;

#pragma warning restore CS0414

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var shader = ShaderUtil.CreateShaderAsset(ctx, _cached, false);
            ctx.AddObjectToAsset("MainAsset", shader);
            ctx.SetMainObject(shader);
        }

        [MenuItem("Assets/Create/AI Tools/AI Shader")]
        static void CreateNewAsset()
        {
            var folder = "Assets";
            var selected = Selection.activeObject;
            if (selected != null)
            {
                var path = AssetDatabase.GetAssetPath(selected);
                if (!string.IsNullOrEmpty(path))
                    folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            }

            if (string.IsNullOrEmpty(folder)) folder = "Assets";
            var assetPath = AssetDatabase.GenerateUniqueAssetPath
              (Path.Combine(folder, "New AI Shader." + Extension).Replace('\\', '/'));

            File.WriteAllText(assetPath, "");
            AssetDatabase.ImportAsset(assetPath);
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(assetPath);
        }
    }

} // namespace AIShader

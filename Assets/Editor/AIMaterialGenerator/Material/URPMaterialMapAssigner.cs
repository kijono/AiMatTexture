using UnityEditor;
using UnityEngine;

namespace AIMaterialGenerator
{
    static class URPMaterialMapAssigner
    {
        public static void AssignMaps(Material material, AIMaterialGenerationResult result)
        {
            if (material == null || result == null)
                return;

            SetTextureIfPresent(material, "_BaseMap", result.GetTexture(AIMaterialMapType.BaseColor));
            SetTextureIfPresent(material, "_MainTex", result.GetTexture(AIMaterialMapType.BaseColor));
            SetTextureIfPresent(material, "_BumpMap", result.GetTexture(AIMaterialMapType.Normal));
            SetTextureIfPresent(material, "_ParallaxMap", result.GetTexture(AIMaterialMapType.Height));
            SetTextureIfPresent(material, "_OcclusionMap", result.GetTexture(AIMaterialMapType.Occlusion));

            AssignWorkflowMap(material, result);

            if (result.GetTexture(AIMaterialMapType.Normal) != null)
                SetKeyword(material, "_NORMALMAP", true);
            if (result.GetTexture(AIMaterialMapType.Height) != null)
                SetKeyword(material, "_PARALLAXMAP", true);
            if (result.GetTexture(AIMaterialMapType.Occlusion) != null)
                SetKeyword(material, "_OCCLUSIONMAP", true);

            EditorUtility.SetDirty(material);
        }

        public static void ApplySelectedMaps(Material target, AIMaterialGenerationResult result, bool baseColor, bool normal, bool maskMap, bool height, bool occlusion)
        {
            if (target == null || result == null)
                return;

            Undo.RecordObject(target, "Apply AI Material Maps");

            if (baseColor)
            {
                SetTextureIfPresent(target, "_BaseMap", result.GetTexture(AIMaterialMapType.BaseColor));
                SetTextureIfPresent(target, "_MainTex", result.GetTexture(AIMaterialMapType.BaseColor));
            }

            if (normal)
            {
                SetTextureIfPresent(target, "_BumpMap", result.GetTexture(AIMaterialMapType.Normal));
                SetKeyword(target, "_NORMALMAP", result.GetTexture(AIMaterialMapType.Normal) != null);
            }

            if (maskMap)
            {
                AssignWorkflowMap(target, result);
            }

            if (height)
            {
                SetTextureIfPresent(target, "_ParallaxMap", result.GetTexture(AIMaterialMapType.Height));
                SetKeyword(target, "_PARALLAXMAP", result.GetTexture(AIMaterialMapType.Height) != null);
            }

            if (occlusion)
            {
                SetTextureIfPresent(target, "_OcclusionMap", result.GetTexture(AIMaterialMapType.Occlusion));
                SetKeyword(target, "_OCCLUSIONMAP", result.GetTexture(AIMaterialMapType.Occlusion) != null);
            }

            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
        }

        static void SetTextureIfPresent(Material material, string property, Texture texture)
        {
            if (texture != null && material.HasProperty(property))
                material.SetTexture(property, texture);
        }

        static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }

        static void AssignWorkflowMap(Material material, AIMaterialGenerationResult result)
        {
            if (IsSpecularWorkflow(material))
            {
                var specGloss = result.GetTexture(AIMaterialMapType.Specular);
                if (specGloss != null)
                {
                    SetTextureIfPresent(material, "_SpecGlossMap", specGloss);
                    SetFloatIfPresent(material, "_Smoothness", 1f);
                    SetKeyword(material, "_SPECGLOSSMAP", true);
                    SetKeyword(material, "_METALLICSPECGLOSSMAP", false);
                }
                return;
            }

            var maskMap = result.GetTexture(AIMaterialMapType.MaskMap);
            if (maskMap != null)
            {
                SetTextureIfPresent(material, "_MetallicGlossMap", maskMap);
                SetFloatIfPresent(material, "_Metallic", 1f);
                SetFloatIfPresent(material, "_Smoothness", 1f);
                SetKeyword(material, "_METALLICSPECGLOSSMAP", true);
                SetKeyword(material, "_SPECGLOSSMAP", false);
            }
        }

        static bool IsSpecularWorkflow(Material material)
        {
            if (material.HasProperty("_WorkflowMode"))
                return Mathf.Approximately(material.GetFloat("_WorkflowMode"), 0f);

            return material.HasProperty("_SpecGlossMap") && !material.HasProperty("_MetallicGlossMap");
        }

        static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }
    }
}

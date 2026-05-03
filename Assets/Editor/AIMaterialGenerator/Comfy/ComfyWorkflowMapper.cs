using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AIMaterialGenerator.Comfy
{
    static class ComfyWorkflowMapper
    {
        const string WorkflowRoot = "Assets/ComfyApi";

        public static string GetWorkflowPath(AIMaterialGenerationMode mode)
        {
            switch (mode)
            {
                case AIMaterialGenerationMode.TextToBaseColor:
                    return $"{WorkflowRoot}/txt2image.json";
                case AIMaterialGenerationMode.TextToBaseColorNormal:
                    return $"{WorkflowRoot}/txt2img+normal.json";
                case AIMaterialGenerationMode.TextToPbr:
                    return $"{WorkflowRoot}/txt2pbr.json";
                case AIMaterialGenerationMode.ImageToNormal:
                    return $"{WorkflowRoot}/imggetnormal.json";
                case AIMaterialGenerationMode.ImageToPbr:
                    return $"{WorkflowRoot}/imggetpbr.json";
                case AIMaterialGenerationMode.EditImageToBaseColor:
                    return $"{WorkflowRoot}/edit2image.json";
                case AIMaterialGenerationMode.EditImageToBaseColorNormal:
                    return $"{WorkflowRoot}/edit2img+nor.json";
                case AIMaterialGenerationMode.EditImageToPbr:
                    return $"{WorkflowRoot}/edit2imagepbr.json";
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        public static JObject BuildWorkflow(AIMaterialGenerationRequest request, string comfyImageName, string outputPrefix)
        {
            var path = GetWorkflowPath(request.mode);
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (json == null)
                throw new FileNotFoundException($"ComfyUI workflow not found: {path}");

            var workflow = JObject.Parse(json.text);
            PatchWorkflow(workflow, request, comfyImageName, outputPrefix);
            return workflow;
        }

        static void PatchWorkflow(JObject workflow, AIMaterialGenerationRequest request, string comfyImageName, string outputPrefix)
        {
            var hasTextNode = false;
            var hasDirectSize = false;
            var hasImageSizeLink = false;

            foreach (var property in workflow.Properties())
            {
                if (property.Value is not JObject node)
                    continue;

                var classType = node.Value<string>("class_type") ?? string.Empty;
                var inputs = node["inputs"] as JObject;
                if (inputs == null)
                    continue;

                if (classType == "CLIPTextEncode" && inputs["text"] != null)
                {
                    inputs["text"] = BuildPromptWithSize(request);
                    hasTextNode = true;
                }

                if (inputs["seed"] != null)
                    inputs["seed"] = request.seed;

                if (classType == "LoadImage" && !string.IsNullOrEmpty(comfyImageName))
                    inputs["image"] = comfyImageName;

                if (classType == "SaveImage" && inputs["filename_prefix"] != null)
                    inputs["filename_prefix"] = BuildSavePrefix(outputPrefix, inputs.Value<string>("filename_prefix"));

                if (SetNumericInputIfDirect(inputs, "width", request.width))
                    hasDirectSize = true;
                if (SetNumericInputIfDirect(inputs, "height", request.height))
                    hasDirectSize = true;
                if (SetNumericInputIfDirect(inputs, "target_width", request.width))
                    hasDirectSize = true;
                if (SetNumericInputIfDirect(inputs, "target_height", request.height))
                    hasDirectSize = true;

                if (inputs["width"] is JArray || inputs["height"] is JArray || inputs["target_width"] is JArray || inputs["target_height"] is JArray)
                    hasImageSizeLink = true;

                if (classType == "ImageScaleToMaxDimension" && inputs["largest_size"] != null)
                    inputs["largest_size"] = Mathf.Max(request.width, request.height);
            }

            if (!hasTextNode && !hasDirectSize && !hasImageSizeLink)
                Debug.LogWarning("AI Material Generator: workflow has no obvious prompt or size node; size can only be implied by the selected workflow.");
        }

        static bool SetNumericInputIfDirect(JObject inputs, string key, int value)
        {
            var token = inputs[key];
            if (token == null || token is JArray)
                return false;

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                inputs[key] = value;
                return true;
            }

            return false;
        }

        static string BuildPromptWithSize(AIMaterialGenerationRequest request)
        {
            var prompt = string.IsNullOrWhiteSpace(request.prompt) ? "tileable material texture" : request.prompt.Trim();
            return $"{prompt}\nTexture resolution: {request.width}x{request.height}. Seamless tileable material texture, top-down orthographic view.";
        }

        static string BuildSavePrefix(string runPrefix, string originalPrefix)
        {
            var normalized = NormalizeMapPrefix(originalPrefix);
            return string.IsNullOrEmpty(normalized) ? runPrefix : $"{runPrefix}_{normalized}";
        }

        public static AIMaterialMapType GuessMapType(string filename)
        {
            var name = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();
            if (name.Contains("normal"))
                return AIMaterialMapType.Normal;
            if (name.Contains("rough"))
                return AIMaterialMapType.Roughness;
            if (name.Contains("height") || name.Contains("depth"))
                return AIMaterialMapType.Height;
            if (name.Contains("metal"))
                return AIMaterialMapType.Metallic;
            if (name.Contains("spec") || name.Contains("gloss"))
                return AIMaterialMapType.Specular;
            if (name.Contains("ao") || name.Contains("occlusion"))
                return AIMaterialMapType.Occlusion;
            return AIMaterialMapType.BaseColor;
        }

        public static bool ModeNeedsSourceImage(AIMaterialGenerationMode mode)
        {
            return mode == AIMaterialGenerationMode.ImageToNormal ||
                   mode == AIMaterialGenerationMode.ImageToPbr ||
                   mode == AIMaterialGenerationMode.EditImageToBaseColor ||
                   mode == AIMaterialGenerationMode.EditImageToBaseColorNormal ||
                   mode == AIMaterialGenerationMode.EditImageToPbr;
        }

        public static bool ModeOutputsBaseColor(AIMaterialGenerationMode mode)
        {
            return mode == AIMaterialGenerationMode.TextToBaseColor ||
                   mode == AIMaterialGenerationMode.TextToBaseColorNormal ||
                   mode == AIMaterialGenerationMode.TextToPbr ||
                   mode == AIMaterialGenerationMode.EditImageToBaseColor ||
                   mode == AIMaterialGenerationMode.EditImageToBaseColorNormal ||
                   mode == AIMaterialGenerationMode.EditImageToPbr;
        }

        static string NormalizeMapPrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                return string.Empty;

            var lower = prefix.ToLowerInvariant();
            if (lower.Contains("base") || lower.Contains("texture") || lower.Contains("z-image"))
                return "basecolor";
            if (lower.Contains("normal"))
                return "normal";
            if (lower.Contains("rough"))
                return "roughness";
            if (lower.Contains("height"))
                return "height";
            if (lower.Contains("metal"))
                return "metallic";
            return SanitizeFileName(prefix);
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "material";

            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            return name.Trim().Replace(' ', '_');
        }
    }
}

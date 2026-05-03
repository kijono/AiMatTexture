using UnityEditor;
using UnityEngine;

namespace AIMaterialGenerator
{
    [FilePath("UserSettings/AIMaterialGeneratorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class AIMaterialGenerationSettings : ScriptableSingleton<AIMaterialGenerationSettings>
    {
        public string comfyBaseUrl = "http://127.0.0.1:8188";
        public int timeoutSeconds = 180;
        public string aiOutputDirectory = "Assets/Editor/AI_MaterialGenerated";

        public bool IsComfyReady => !string.IsNullOrWhiteSpace(comfyBaseUrl);

        public bool IsOnlineReady => AIShader.AIToolSettings.instance.IsReady;

        public string NormalizedComfyBaseUrl => NormalizeBaseUrl(comfyBaseUrl);
        public string SafeAiOutputDirectory
        {
            get
            {
                var normalized = string.IsNullOrWhiteSpace(aiOutputDirectory)
                    ? "Assets/Editor/AI_MaterialGenerated"
                    : aiOutputDirectory.Replace('\\', '/').TrimEnd('/');
                return normalized.StartsWith("Assets/Editor/") ? normalized : "Assets/Editor/AI_MaterialGenerated";
            }
        }

        public int GetTimeoutForResolution(int width, int height)
        {
            var maxSide = Mathf.Max(1, Mathf.Max(width, height));
            var multiplier = 1;
            if (maxSide > 512)
                multiplier = 2;
            if (maxSide > 1024)
                multiplier = 4;

            return Mathf.Max(1, timeoutSeconds) * multiplier;
        }

        public void SaveSettings() => Save(true);

        static string NormalizeBaseUrl(string url)
        {
            return string.IsNullOrWhiteSpace(url) ? string.Empty : url.Trim().TrimEnd('/');
        }
    }

    sealed class AIMaterialGenerationSettingsProvider : SettingsProvider
    {
        AIMaterialGenerationSettingsProvider()
          : base("Project/AI Material Generator", SettingsScope.Project) {}

        public override void OnGUI(string searchContext)
        {
            var settings = AIMaterialGenerationSettings.instance;

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Local ComfyUI", EditorStyles.boldLabel);
            settings.comfyBaseUrl = EditorGUILayout.TextField("Base URL", settings.comfyBaseUrl);
            settings.timeoutSeconds = Mathf.Max(1, EditorGUILayout.IntField("512 Timeout (seconds)", settings.timeoutSeconds));
            settings.aiOutputDirectory = EditorGUILayout.TextField("AI Temp Output", settings.aiOutputDirectory);
            if (!settings.aiOutputDirectory.Replace('\\', '/').StartsWith("Assets/Editor/"))
                EditorGUILayout.HelpBox("建议将 AI 临时生成目录放在 Assets/Editor 下，防止临时材质/贴图被误用于运行时资源。", MessageType.Warning);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Online Image API", EditorStyles.boldLabel);
            var aiTools = AIShader.AIToolSettings.instance;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Provider", aiTools.provider.ToString());
                EditorGUILayout.TextField("Base URL", aiTools.BaseUrl);
                EditorGUILayout.TextField("Model", aiTools.Model);
            }

            if (GUILayout.Button("Open AI Tools API Settings"))
                SettingsService.OpenProjectSettings("Project/AI Tools");

            EditorGUILayout.HelpBox("在线图片生成复用 Project/AI Tools 中的 API Key、Base URL 和 Model，并按 OpenAI-compatible /images/generations 协议请求。请将 AI Tools 的 Custom Model 配置为供应商支持的图片生成模型。", MessageType.Info);

            if (EditorGUI.EndChangeCheck())
                settings.SaveSettings();
        }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new AIMaterialGenerationSettingsProvider();
        }
    }
}

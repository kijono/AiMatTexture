using UnityEngine;
using UnityEditor;

namespace AIShader
{
    public enum AIProvider
    {
        DeepSeek,
        OpenAI,
        Qwen,
        Moonshot,
        Zhipu,
        Custom
    }

    [FilePath("UserSettings/AIToolSettings.asset",
              FilePathAttribute.Location.ProjectFolder)]
    public sealed class AIToolSettings : ScriptableSingleton<AIToolSettings>
    {
        public AIProvider provider = AIProvider.DeepSeek;
        public string apiKey = "";
        public string customBaseUrl = "";
        public string customModel = "";
        public int timeout = 60;

        public string BaseUrl => GetBaseUrl(provider, customBaseUrl);
        public string Model => GetModel(provider, customModel);

        public bool IsReady
          => !string.IsNullOrWhiteSpace(apiKey) &&
             !string.IsNullOrWhiteSpace(BaseUrl) &&
             !string.IsNullOrWhiteSpace(Model);

        public string ChatCompletionsUrl
        {
            get
            {
                var url = BaseUrl.TrimEnd('/');
                return url.EndsWith("/chat/completions") ? url : url + "/chat/completions";
            }
        }

        public static string GetBaseUrl(AIProvider provider, string customBaseUrl)
        {
            switch (provider)
            {
                case AIProvider.DeepSeek: return "https://api.deepseek.com";
                case AIProvider.OpenAI: return "https://api.openai.com/v1";
                case AIProvider.Qwen: return "https://dashscope.aliyuncs.com/compatible-mode/v1";
                case AIProvider.Moonshot: return "https://api.moonshot.cn/v1";
                case AIProvider.Zhipu: return "https://open.bigmodel.cn/api/paas/v4";
                default: return customBaseUrl;
            }
        }

        public static string GetModel(AIProvider provider, string customModel)
        {
            switch (provider)
            {
                case AIProvider.DeepSeek: return "deepseek-v4-flash";
                case AIProvider.OpenAI: return "gpt-4o-mini";
                case AIProvider.Qwen: return "qwen-plus";
                case AIProvider.Moonshot: return "moonshot-v1-8k";
                case AIProvider.Zhipu: return "glm-4-flash";
                default: return customModel;
            }
        }

        public void Save() => Save(true);
        void OnDisable() => Save();
    }

    sealed class AIToolSettingsProvider : SettingsProvider
    {
        public AIToolSettingsProvider()
          : base("Project/AI Tools", SettingsScope.Project) {}

        public override void OnGUI(string search)
        {
            var settings = AIToolSettings.instance;
            var provider = settings.provider;
            var key = settings.apiKey;
            var customBaseUrl = settings.customBaseUrl;
            var customModel = settings.customModel;
            var timeout = settings.timeout;

            EditorGUI.BeginChangeCheck();

            provider = (AIProvider)EditorGUILayout.EnumPopup("Provider", provider);
            key = EditorGUILayout.PasswordField("API Key", key);
            timeout = EditorGUILayout.IntField("Timeout (seconds)", timeout);

            if (provider == AIProvider.Custom)
            {
                customBaseUrl = EditorGUILayout.TextField("Custom Base URL", customBaseUrl);
                customModel = EditorGUILayout.TextField("Custom Model", customModel);
            }

            EditorGUILayout.LabelField("Base URL", AIToolSettings.GetBaseUrl(provider, customBaseUrl));
            EditorGUILayout.LabelField("Model", AIToolSettings.GetModel(provider, customModel));

            if (EditorGUI.EndChangeCheck())
            {
                settings.provider = provider;
                settings.apiKey = key;
                settings.customBaseUrl = customBaseUrl;
                settings.customModel = customModel;
                settings.timeout = Mathf.Max(0, timeout);
                settings.Save();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox
              ("AI Shader and AI Command share this OpenAI-compatible chat API setting. " +
               "Use DeepSeek with model deepseek-v4-flash by default, or choose Custom for any compatible provider.",
               MessageType.Info);
        }

        [SettingsProvider]
        public static SettingsProvider CreateCustomSettingsProvider()
          => new AIToolSettingsProvider();
    }

    static class AIToolMenus
    {
        [MenuItem("Window/AI Tools/Settings")]
        static void OpenSettings()
          => SettingsService.OpenProjectSettings("Project/AI Tools");
    }

} // namespace AIShader

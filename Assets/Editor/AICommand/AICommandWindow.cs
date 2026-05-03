using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;

namespace AIShader
{

    public sealed class AICommandWindow : EditorWindow
    {
        #region Temporary script file operations

        const string TempFilePath = "Assets/AICommandTemp.cs";

        bool TempFileExists => System.IO.File.Exists(TempFilePath);

        void CreateScriptAsset(string code)
        {
            var fullPath = Path.GetFullPath(TempFilePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(fullPath, StripCodeFence(code), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(TempFilePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
        }

        static string StripCodeFence(string code)
        {
            code = code.Trim();
            if (!code.StartsWith("```")) return code;

            var firstLineEnd = code.IndexOf('\n');
            var lastFence = code.LastIndexOf("```");
            if (firstLineEnd < 0 || lastFence <= firstLineEnd) return code;

            return code.Substring(firstLineEnd + 1, lastFence - firstLineEnd - 1).Trim();
        }

        #endregion

        #region Script generator

        static string WrapPrompt(string input)
          => "Write a Unity Editor script.\n" +
             " - It provides its functionality as a menu item placed \"Edit\" > \"Do Task\".\n" +
             " - It doesn’t provide any editor window. It immediately does the task when the menu item is invoked.\n" +
             " - Don’t use GameObject.FindGameObjectsWithTag.\n" +
             " - There is no selected object. Find game objects manually.\n" +
             " - I only need the script body. Don’t add explanations or code fences.\n" +
             "The task is described as follows:\n" + input;

        void RunGenerator()
        {
            try
            {
                var code = OpenAIUtil.InvokeChat(WrapPrompt(_prompt), "AI Command");
                Debug.Log("AI command script:" + code);
                CreateScriptAsset(code);
            }
            catch (System.Exception e)
            {
                Debug.LogError(e);
                EditorUtility.DisplayDialog("AI Command", e.Message, "OK");
            }
        }

        #endregion

        #region Editor GUI

        string _prompt = "Create 100 cubes at random points.";

        const string ApiKeyErrorText =
          "AI API settings are incomplete. Please check the project settings " +
          "(Edit > Project Settings > AI Tools).";

        bool IsApiKeyOk
          => AIToolSettings.instance.IsReady;

        [MenuItem("Window/AI Tools/Command")]
        static void Init() => GetWindow<AICommandWindow>(true, "AI Command");

        void OnGUI()
        {
            if (IsApiKeyOk)
            {
                _prompt = EditorGUILayout.TextArea(_prompt, GUILayout.ExpandHeight(true));
                if (GUILayout.Button("Run")) RunGenerator();
            }
            else
            {
                EditorGUILayout.HelpBox(ApiKeyErrorText, MessageType.Error);
            }
        }

        #endregion

        #region Script lifecycle

        void OnEnable()
          => AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;

        void OnDisable()
          => AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;

        void OnAfterAssemblyReload()
        {
            if (!TempFileExists) return;
            EditorApplication.ExecuteMenuItem("Edit/Do Task");
            AssetDatabase.DeleteAsset(TempFilePath);
        }

        #endregion
    }

} // namespace AICommand

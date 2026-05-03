using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;

namespace AIShader
{

    [CustomEditor(typeof(AIShaderImporter))]
    sealed class AIShaderImporterEditor : ScriptedImporterEditor
    {
        #region Private members

        SerializedProperty _prompt;
        SerializedProperty _cached;

        bool IsApiKeyOk
          => AIToolSettings.instance.IsReady;

        const string ApiKeyErrorText =
          "AI API settings are incomplete. Please check the project settings " +
          "(Edit > Project Settings > AI Tools).";

        static string WrapPrompt(string input)
          => "Create an unlit shader for Unity. " + input +
             " Don't include any note nor explanation in the response" +
             " I only need the code body. no ``` and ```hlsl ";

        void Regenerate()
        {
            try
            {
                _cached.stringValue = OpenAIUtil.InvokeChat(WrapPrompt(_prompt.stringValue), "AI Shader");
            }
            catch (System.Exception e)
            {
                Debug.LogError(e);
                EditorUtility.DisplayDialog("AI Shader", e.Message, "OK");
            }
        }

        #endregion

        #region ScriptedImporterEditor overrides

        public override void OnEnable()
        {
            base.OnEnable();
            _prompt = serializedObject.FindProperty("_prompt");
            _cached = serializedObject.FindProperty("_cached");
        }

        public override void OnInspectorGUI()
        {
            // Intro
            serializedObject.Update();

            // Prompt text area
            EditorGUILayout.PropertyField(_prompt);

            // "Generate" button
            EditorGUI.BeginDisabledGroup(!IsApiKeyOk);
            if (GUILayout.Button("Generate")) Regenerate();
            EditorGUI.EndDisabledGroup();

            // Missing API key error
            if (!IsApiKeyOk) EditorGUILayout.HelpBox(ApiKeyErrorText, MessageType.Error);

            // Cached code text area
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_cached);

            // Outro
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }

        #endregion
    }

} // namespace AIShader

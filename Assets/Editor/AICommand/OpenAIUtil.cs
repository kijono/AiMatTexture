using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System;
using System.Text;

namespace AIShader
{

    static class OpenAIUtil
    {
        static string CreateChatRequestBody(string prompt)
        {
            var msg = new OpenAI.RequestMessage();
            msg.role = "user";
            msg.content = prompt;

            var req = new OpenAI.Request();
            req.model = AIToolSettings.instance.Model;
            req.messages = new[] { msg };
            req.stream = false;

            return JsonUtility.ToJson(req);
        }

        public static string InvokeChat(string prompt, string progressTitle)
        {
            var settings = AIToolSettings.instance;
            if (!settings.IsReady)
                throw new InvalidOperationException("AI Tools API settings are incomplete.");

            // POST
            using var post = new UnityWebRequest(settings.ChatCompletionsUrl, UnityWebRequest.kHttpVerbPOST);
            post.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(CreateChatRequestBody(prompt)));
            post.downloadHandler = new DownloadHandlerBuffer();

            // API key authorization
            post.SetRequestHeader("Authorization", "Bearer " + settings.apiKey);
            post.SetRequestHeader("Content-Type", "application/json");
            post.SetRequestHeader("Accept", "application/json");
            post.timeout = settings.timeout;

            // Request start
            var req = post.SendWebRequest();

            // Progress bar (Totally fake! Don't try this at home.)
            for (var progress = 0.0f; !req.isDone; progress += 0.01f)
            {
                EditorUtility.DisplayProgressBar
                  (progressTitle, "Generating...", progress);
                System.Threading.Thread.Sleep(100);
                progress += 0.01f;
            }
            EditorUtility.ClearProgressBar();

            // Response extraction
            var json = post.downloadHandler.text;
            if (post.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException(post.error + "\n" + json);

            Debug.Log(json);
            var data = JsonUtility.FromJson<OpenAI.Response>(json);
            if (data.choices == null || data.choices.Length == 0)
                throw new InvalidOperationException("AI response did not contain any choices.\n" + json);

            return data.choices[0].message.content;
        }
    }

} // namespace AIShader

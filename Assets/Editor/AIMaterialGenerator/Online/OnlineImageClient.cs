using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace AIMaterialGenerator.Online
{
    sealed class OnlineImageClient
    {
        readonly AIMaterialGenerationSettings _settings;

        public OnlineImageClient(AIMaterialGenerationSettings settings)
        {
            _settings = settings;
        }

        public async Task<AIMaterialGenerationResult> GenerateAsync(AIMaterialGenerationRequest request, string runPrefix, Action<string, float> progress)
        {
            var aiTools = AIShader.AIToolSettings.instance;
            if (!aiTools.IsReady)
                throw new InvalidOperationException("AI Tools API settings are incomplete. Configure Project/AI Tools first.");

            var prompt = $"{request.prompt}\nTexture resolution: {request.width}x{request.height}. Seamless tileable basecolor material texture, top-down orthographic view.";
            var body = new JObject
            {
                ["model"] = aiTools.Model,
                ["prompt"] = prompt,
                ["size"] = $"{request.width}x{request.height}",
                ["n"] = 1,
                ["response_format"] = "b64_json"
            };

            progress?.Invoke("Submitting online image request...", 0.2f);
            using var webRequest = new UnityWebRequest(GetImageGenerationsUrl(aiTools), UnityWebRequest.kHttpVerbPOST);
            webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString()));
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", "Bearer " + aiTools.apiKey);
            webRequest.timeout = Mathf.Max(1, _settings.GetTimeoutForResolution(request.width, request.height));
            await SendAsync(webRequest);

            var bytes = await ExtractImageBytes(webRequest.downloadHandler.text);
            var image = new Comfy.ComfyDownloadedImage
            {
                fileName = runPrefix + "_basecolor.png",
                bytes = bytes,
                mapType = AIMaterialMapType.BaseColor
            };

            return AIMaterialAssetWriter.WriteResult(request, new[] { image }, null);
        }

        static string GetImageGenerationsUrl(AIShader.AIToolSettings aiTools)
        {
            var url = aiTools.BaseUrl.Trim().TrimEnd('/');
            if (url.EndsWith("/images/generations", StringComparison.OrdinalIgnoreCase))
                return url;
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return url.Substring(0, url.Length - "/chat/completions".Length) + "/images/generations";
            return url + "/images/generations";
        }

        static async Task<byte[]> ExtractImageBytes(string jsonText)
        {
            var json = JObject.Parse(jsonText);
            var first = json["data"] is JArray data && data.Count > 0 ? data[0] : json;
            var b64 = first.Value<string>("b64_json") ?? first.Value<string>("base64");
            if (!string.IsNullOrEmpty(b64))
                return Convert.FromBase64String(b64);

            var url = first.Value<string>("url") ?? first.Value<string>("image_url");
            if (!string.IsNullOrEmpty(url))
            {
                using var get = UnityWebRequest.Get(url);
                await SendAsync(get);
                return get.downloadHandler.data;
            }

            throw new InvalidOperationException("Online image API response did not contain b64_json/base64/url.\n" + jsonText);
        }

        static async Task SendAsync(UnityWebRequest request)
        {
            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Delay(50);

            if (request.result != UnityWebRequest.Result.Success)
            {
                var body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                throw new InvalidOperationException(request.error + "\n" + body);
            }
        }
    }
}

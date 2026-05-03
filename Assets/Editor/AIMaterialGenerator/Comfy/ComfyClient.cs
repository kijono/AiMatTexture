using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace AIMaterialGenerator.Comfy
{
    sealed class ComfyDownloadedImage
    {
        public string fileName;
        public byte[] bytes;
        public AIMaterialMapType mapType;
    }

    sealed class ComfyClient
    {
        readonly AIMaterialGenerationSettings _settings;
        int _activeTimeoutSeconds;

        public ComfyClient(AIMaterialGenerationSettings settings)
        {
            _settings = settings;
        }

        public async Task<List<ComfyDownloadedImage>> GenerateAsync(AIMaterialGenerationRequest request, string runPrefix, Action<string, float> progress)
        {
            if (!_settings.IsComfyReady)
                throw new InvalidOperationException("ComfyUI URL is not configured.");

            _activeTimeoutSeconds = _settings.GetTimeoutForResolution(request.width, request.height);
            var comfyImageName = string.Empty;
            if (ComfyWorkflowMapper.ModeNeedsSourceImage(request.mode))
            {
                if (string.IsNullOrEmpty(request.sourceTexturePath))
                    throw new InvalidOperationException("This mode requires a source texture.");

                var exportedPath = AIMaterialTextureUtility.ExportTextureForUpload(request.sourceTexturePath, _settings.SafeAiOutputDirectory, runPrefix);
                comfyImageName = await UploadImageAsync(exportedPath, progress);
            }

            progress?.Invoke("Preparing workflow...", 0.1f);
            var workflow = ComfyWorkflowMapper.BuildWorkflow(request, comfyImageName, runPrefix);
            var promptId = await QueuePromptAsync(workflow, progress);
            var history = await WaitForHistoryAsync(promptId, progress);
            return await DownloadOutputsAsync(history, promptId, progress);
        }

        async Task<string> QueuePromptAsync(JObject workflow, Action<string, float> progress)
        {
            var body = new JObject
            {
                ["prompt"] = workflow,
                ["client_id"] = Guid.NewGuid().ToString("N")
            };

            progress?.Invoke("Submitting ComfyUI prompt...", 0.2f);
            using var request = CreateRequest("/prompt", UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString()));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            await SendAsync(request);
            var json = JObject.Parse(request.downloadHandler.text);
            var promptId = json.Value<string>("prompt_id");
            if (string.IsNullOrEmpty(promptId))
                throw new InvalidOperationException("ComfyUI did not return prompt_id.\n" + request.downloadHandler.text);

            return promptId;
        }

        async Task<JObject> WaitForHistoryAsync(string promptId, Action<string, float> progress)
        {
            var started = DateTime.UtcNow;
            while ((DateTime.UtcNow - started).TotalSeconds < Mathf.Max(1, _activeTimeoutSeconds))
            {
                progress?.Invoke("Waiting for ComfyUI output...", 0.35f);
                using var request = CreateRequest("/history/" + UnityWebRequest.EscapeURL(promptId), UnityWebRequest.kHttpVerbGET);
                request.downloadHandler = new DownloadHandlerBuffer();
                await SendAsync(request);

                var root = JObject.Parse(request.downloadHandler.text);
                if (root[promptId] is JObject history)
                    return history;

                await Task.Delay(700);
            }

            throw new TimeoutException("Timed out waiting for ComfyUI history: " + promptId);
        }

        async Task<List<ComfyDownloadedImage>> DownloadOutputsAsync(JObject history, string promptId, Action<string, float> progress)
        {
            var images = new List<ComfyDownloadedImage>();
            var outputs = history["outputs"] as JObject;
            if (outputs == null)
                throw new InvalidOperationException("ComfyUI history has no outputs for prompt_id: " + promptId);

            foreach (var node in outputs.Properties())
            {
                if (node.Value["images"] is not JArray imageArray)
                    continue;

                foreach (var imageToken in imageArray)
                {
                    var filename = imageToken.Value<string>("filename");
                    if (string.IsNullOrEmpty(filename))
                        continue;

                    var subfolder = imageToken.Value<string>("subfolder") ?? string.Empty;
                    var type = imageToken.Value<string>("type") ?? "output";
                    progress?.Invoke("Downloading " + filename, 0.75f);
                    var bytes = await DownloadImageAsync(filename, subfolder, type);
                    images.Add(new ComfyDownloadedImage
                    {
                        fileName = filename,
                        bytes = bytes,
                        mapType = ComfyWorkflowMapper.GuessMapType(filename)
                    });
                }
            }

            if (images.Count == 0)
                throw new InvalidOperationException("ComfyUI finished but no output images were found for prompt_id: " + promptId);

            return images;
        }

        async Task<byte[]> DownloadImageAsync(string filename, string subfolder, string type)
        {
            var url = $"{_settings.NormalizedComfyBaseUrl}/view?filename={UnityWebRequest.EscapeURL(filename)}&subfolder={UnityWebRequest.EscapeURL(subfolder)}&type={UnityWebRequest.EscapeURL(type)}";
            using var request = UnityWebRequest.Get(url);
            request.timeout = Mathf.Max(1, _activeTimeoutSeconds);
            await SendAsync(request);
            return request.downloadHandler.data;
        }

        async Task<string> UploadImageAsync(string assetPath, Action<string, float> progress)
        {
            progress?.Invoke("Uploading source image...", 0.05f);
            var fullPath = Path.GetFullPath(assetPath);
            var fileName = Path.GetFileName(fullPath);
            var bytes = File.ReadAllBytes(fullPath);

            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("image", bytes, fileName, "image/png"),
                new MultipartFormDataSection("overwrite", "true")
            };

            using var request = UnityWebRequest.Post(_settings.NormalizedComfyBaseUrl + "/upload/image", form);
            request.timeout = Mathf.Max(1, _activeTimeoutSeconds);
            await SendAsync(request);

            var response = JObject.Parse(request.downloadHandler.text);
            return response.Value<string>("name") ?? fileName;
        }

        UnityWebRequest CreateRequest(string path, string method)
        {
            var request = new UnityWebRequest(_settings.NormalizedComfyBaseUrl + path, method);
            request.timeout = Mathf.Max(1, _activeTimeoutSeconds);
            return request;
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

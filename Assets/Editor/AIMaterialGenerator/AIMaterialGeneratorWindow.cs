using System;
using System.IO;
using System.Threading.Tasks;
using AIMaterialGenerator.Comfy;
using AIMaterialGenerator.Online;
using UnityEditor;
using UnityEngine;

namespace AIMaterialGenerator
{
    public sealed class AIMaterialGeneratorWindow : EditorWindow
    {
        AIMaterialBackend _backend = AIMaterialBackend.ComfyUI;
        AIMaterialGenerationMode _mode = AIMaterialGenerationMode.TextToBaseColor;
        AIMaterialTargetAction _targetAction = AIMaterialTargetAction.PreviewOnly;
        Material _targetMaterial;
        Renderer _targetRenderer;
        Texture2D _sourceTexture;
        string _prompt = "写实的石板地面材质，俯视视角，无缝贴图";
        string _slug = "Material";
        int _width = 512;
        int _height = 512;
        long _seed = 0;
        bool _randomSeed = true;
        bool _isGenerating;
        string _status = "Ready";
        float _progress;
        AIMaterialGenerationResult _lastResult;
        GameObject _previewObject;
        bool _applyBaseColor = true;
        bool _applyNormal = true;
        bool _applyMaskMap = true;
        bool _applyHeight = true;
        bool _applyOcclusion = true;

        [MenuItem("Window/AI Tools/Main")]
        public static void Open()
        {
            var window = GetWindow<AIMaterialGeneratorWindow>();
            window.titleContent = new GUIContent("AI Tools");
            window.minSize = new Vector2(360f, 520f);
            window.Show();
        }

        void OnEnable()
        {
            RefreshSelection();
            Selection.selectionChanged += Repaint;
        }

        void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
        }

        void OnGUI()
        {
            DrawHeader();
            EditorGUILayout.Space();
            DrawTargetSection();
            EditorGUILayout.Space();
            DrawGenerationSection();
            EditorGUILayout.Space();
            DrawActions();
            EditorGUILayout.Space();
            DrawResultSection();
        }

        void DrawHeader()
        {
            var settings = AIMaterialGenerationSettings.instance;
            EditorGUILayout.LabelField("AI Tools - 材质生成", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _backend = (AIMaterialBackend)EditorGUILayout.EnumPopup("后端", _backend);
                if (GUILayout.Button("设置", GUILayout.Width(64)))
                    SettingsService.OpenProjectSettings("Project/AI Material Generator");
            }

            if (_backend == AIMaterialBackend.ComfyUI)
                EditorGUILayout.LabelField("ComfyUI", settings.NormalizedComfyBaseUrl);
            else
                EditorGUILayout.LabelField("Online Image API", AIShader.AIToolSettings.instance.IsReady ? AIShader.AIToolSettings.instance.BaseUrl : "AI Tools 未配置");

            EditorGUILayout.LabelField("AI 临时目录", settings.SafeAiOutputDirectory);
            EditorGUILayout.LabelField("当前超时", settings.GetTimeoutForResolution(_width, _height) + " 秒");
        }

        void DrawTargetSection()
        {
            EditorGUILayout.LabelField("目标", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("读取当前选择", GUILayout.Width(110)))
                    RefreshSelection();
                _targetAction = (AIMaterialTargetAction)EditorGUILayout.EnumPopup(_targetAction);
            }

            _targetRenderer = (Renderer)EditorGUILayout.ObjectField("目标 Renderer", _targetRenderer, typeof(Renderer), true);
            _targetMaterial = (Material)EditorGUILayout.ObjectField("目标材质", _targetMaterial, typeof(Material), false);
            _sourceTexture = (Texture2D)EditorGUILayout.ObjectField("源贴图", _sourceTexture, typeof(Texture2D), false);

            if (GUILayout.Button("从目标/源贴图读取宽高"))
                InitializeSizeFromTarget();
        }

        void DrawGenerationSection()
        {
            EditorGUILayout.LabelField("生成", EditorStyles.boldLabel);
            _mode = (AIMaterialGenerationMode)EditorGUILayout.EnumPopup("模式", _mode);
            _slug = EditorGUILayout.TextField("名称", _slug);
            _prompt = EditorGUILayout.TextArea(_prompt, GUILayout.MinHeight(70));

            using (new EditorGUILayout.HorizontalScope())
            {
                _width = Mathf.Max(1, EditorGUILayout.IntField("宽", _width));
                _height = Mathf.Max(1, EditorGUILayout.IntField("高", _height));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("512")) { _width = 512; _height = 512; }
                if (GUILayout.Button("1024")) { _width = 1024; _height = 1024; }
                if (GUILayout.Button("2048")) { _width = 2048; _height = 2048; }
            }

            _randomSeed = EditorGUILayout.Toggle("随机 Seed", _randomSeed);
            using (new EditorGUI.DisabledScope(_randomSeed))
                _seed = EditorGUILayout.LongField("Seed", _seed);

            if (_backend == AIMaterialBackend.OnlineImageApi && _mode != AIMaterialGenerationMode.TextToBaseColor)
                EditorGUILayout.HelpBox("在线 API 第一版只支持 basecolor 文生图。请选择 TextToBaseColor，或使用 ComfyUI 后端。", MessageType.Warning);

            if (ComfyWorkflowMapper.ModeNeedsSourceImage(_mode) && _sourceTexture == null)
                EditorGUILayout.HelpBox("当前模式需要源贴图。", MessageType.Warning);
        }

        void DrawActions()
        {
            using (new EditorGUI.DisabledScope(_isGenerating || !CanGenerate()))
            {
                if (GUILayout.Button("生成预览", GUILayout.Height(32)))
                    _ = GenerateAsync();
            }

            if (_isGenerating)
                EditorGUILayout.Slider(_status, _progress, 0f, 1f);
            else
                EditorGUILayout.LabelField("状态", _status);

            if (_lastResult == null)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("应用槽位", EditorStyles.boldLabel);
            _applyBaseColor = EditorGUILayout.ToggleLeft("BaseColor", _applyBaseColor);
            _applyNormal = EditorGUILayout.ToggleLeft("Normal", _applyNormal);
            _applyMaskMap = EditorGUILayout.ToggleLeft("Metallic Mask Map / Specular Gloss Map", _applyMaskMap);
            _applyHeight = EditorGUILayout.ToggleLeft("Height", _applyHeight);
            _applyOcclusion = EditorGUILayout.ToggleLeft("Occlusion", _applyOcclusion);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("创建/刷新预览物体"))
                    CreatePreviewObject();
                using (new EditorGUI.DisabledScope(_targetRenderer == null))
                {
                    if (GUILayout.Button("应用到选中物体"))
                        ApplyToRenderer();
                }
                using (new EditorGUI.DisabledScope(_targetMaterial == null))
                {
                    if (GUILayout.Button("替换材质槽位"))
                        ReplaceMaterialSlots();
                }
            }

            if (GUILayout.Button("确认并移动到正式目录"))
                MoveResultToFormalDirectory();
        }

        void DrawResultSection()
        {
            if (_lastResult == null)
                return;

            EditorGUILayout.LabelField("结果", EditorStyles.boldLabel);
            EditorGUILayout.ObjectField("材质", _lastResult.material, typeof(Material), false);
            foreach (var texture in _lastResult.textures)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(texture.mapType.ToString(), texture.texture, typeof(Texture2D), false);
                    if (GUILayout.Button("选中", GUILayout.Width(56)))
                        Selection.activeObject = texture.texture;
                }
            }
        }

        bool CanGenerate()
        {
            if (_backend == AIMaterialBackend.OnlineImageApi && _mode != AIMaterialGenerationMode.TextToBaseColor)
                return false;
            if (string.IsNullOrWhiteSpace(_prompt))
                return false;
            if (ComfyWorkflowMapper.ModeNeedsSourceImage(_mode) && _sourceTexture == null)
                return false;
            return true;
        }

        async Task GenerateAsync()
        {
            _isGenerating = true;
            _progress = 0f;
            _status = "Starting...";
            Repaint();

            try
            {
                var request = BuildRequest();
                var runPrefix = "AI_" + ComfyWorkflowMapper.SanitizeFileName(request.slug) + "_" + DateTime.Now.ToString("HHmmss");
                if (_backend == AIMaterialBackend.ComfyUI)
                {
                    var client = new ComfyClient(AIMaterialGenerationSettings.instance);
                    var images = await client.GenerateAsync(request, runPrefix, SetProgress);
                    var sourceBaseColor = ShouldUseSourceBaseColor(request.mode) ? _sourceTexture : null;
                    _lastResult = AIMaterialAssetWriter.WriteResult(request, images, sourceBaseColor);
                }
                else
                {
                    var client = new OnlineImageClient(AIMaterialGenerationSettings.instance);
                    _lastResult = await client.GenerateAsync(request, runPrefix, SetProgress);
                }

                _status = "生成完成";
                _progress = 1f;
                Selection.activeObject = _lastResult.material;
            }
            catch (Exception ex)
            {
                _status = "失败: " + ex.Message;
                Debug.LogException(ex);
            }
            finally
            {
                _isGenerating = false;
                Repaint();
            }
        }

        AIMaterialGenerationRequest BuildRequest()
        {
            return new AIMaterialGenerationRequest
            {
                backend = _backend,
                mode = _mode,
                targetAction = _targetAction,
                prompt = _prompt,
                width = _width,
                height = _height,
                seed = _randomSeed ? GenerateSeed() : _seed,
                randomSeed = _randomSeed,
                slug = string.IsNullOrWhiteSpace(_slug) ? "Material" : _slug,
                sourceTexturePath = _sourceTexture != null ? AssetDatabase.GetAssetPath(_sourceTexture) : string.Empty,
                targetMaterial = _targetMaterial,
                targetRenderer = _targetRenderer
            };
        }

        void SetProgress(string status, float progress)
        {
            _status = status;
            _progress = progress;
            Repaint();
        }

        static long GenerateSeed()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            return Math.Abs(BitConverter.ToInt64(bytes, 0)) % 999999999999999L;
        }

        void RefreshSelection()
        {
            if (Selection.activeObject is Material material)
                _targetMaterial = material;
            else if (Selection.activeGameObject != null)
            {
                _targetRenderer = Selection.activeGameObject.GetComponent<Renderer>();
                if (_targetRenderer != null)
                    _targetMaterial = _targetRenderer.sharedMaterial;
            }

            if (_targetMaterial != null && _sourceTexture == null)
                _sourceTexture = GetBaseColorTexture(_targetMaterial);

            InitializeSizeFromTarget();
        }

        void InitializeSizeFromTarget()
        {
            var texture = _sourceTexture != null ? _sourceTexture : _targetMaterial != null ? GetBaseColorTexture(_targetMaterial) : null;
            if (AIMaterialTextureUtility.TryGetTextureSize(texture, out var width, out var height))
            {
                _width = width;
                _height = height;
            }
            else
            {
                _width = 512;
                _height = 512;
            }
        }

        static Texture2D GetBaseColorTexture(Material material)
        {
            if (material == null)
                return null;
            if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") is Texture2D baseMap)
                return baseMap;
            if (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") is Texture2D mainTex)
                return mainTex;
            return null;
        }

        static bool ShouldUseSourceBaseColor(AIMaterialGenerationMode mode)
        {
            return mode == AIMaterialGenerationMode.ImageToNormal || mode == AIMaterialGenerationMode.ImageToPbr;
        }

        void CreatePreviewObject()
        {
            if (_lastResult?.material == null)
                return;

            if (_previewObject == null)
                _previewObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            _previewObject.name = "AI_Material_Preview";
            var renderer = _previewObject.GetComponent<Renderer>();
            renderer.sharedMaterial = _lastResult.material;
            Selection.activeGameObject = _previewObject;
        }

        void ApplyToRenderer()
        {
            if (_targetRenderer == null || _lastResult?.material == null)
                return;

            Undo.RecordObject(_targetRenderer, "Apply AI Material");
            _targetRenderer.sharedMaterial = _lastResult.material;
            EditorUtility.SetDirty(_targetRenderer);
        }

        void ReplaceMaterialSlots()
        {
            if (_targetMaterial == null || _lastResult == null)
                return;

            if (!EditorUtility.DisplayDialog("替换材质槽位", "将按勾选项替换目标材质贴图槽位。此操作会修改目标材质资源。", "确认替换", "取消"))
                return;

            try
            {
                AIMaterialAssetWriter.CopySelectedTexturesToMaterialDirectory(_targetMaterial, _lastResult, _applyBaseColor, _applyNormal, _applyMaskMap, _applyHeight, _applyOcclusion);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("复制贴图失败", ex.Message, "OK");
                return;
            }

            URPMaterialMapAssigner.ApplySelectedMaps(_targetMaterial, _lastResult, _applyBaseColor, _applyNormal, _applyMaskMap, _applyHeight, _applyOcclusion);
            Selection.activeObject = _targetMaterial;
            _status = "已复制贴图并替换材质槽位";
        }

        void MoveResultToFormalDirectory()
        {
            if (_lastResult == null)
                return;

            var defaultDirectory = GetDefaultFormalDirectory();
            var fullDefault = Path.GetFullPath(defaultDirectory);
            var selected = EditorUtility.OpenFolderPanel("选择正式资源目录", fullDefault, "");
            if (string.IsNullOrEmpty(selected))
                return;

            var projectRoot = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/');
            selected = selected.Replace('\\', '/');
            if (!selected.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("目录无效", "正式目录必须位于当前 Unity 项目的 Assets 下。", "OK");
                return;
            }

            var assetDirectory = selected.Substring(projectRoot.Length + 1);
            if (!assetDirectory.StartsWith("Assets/", StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("目录无效", "请选择 Assets 下的目录。", "OK");
                return;
            }

            try
            {
                AIMaterialAssetWriter.MoveResultToDirectory(_lastResult, assetDirectory);
                _status = "已移动到正式目录";
                Selection.activeObject = _lastResult.material;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("移动失败", ex.Message, "OK");
            }
        }

        string GetDefaultFormalDirectory()
        {
            if (_targetMaterial != null)
            {
                var materialPath = AssetDatabase.GetAssetPath(_targetMaterial);
                var directory = Path.GetDirectoryName(materialPath);
                if (!string.IsNullOrEmpty(directory))
                    return directory;
            }

            return "Assets";
        }
    }
}

using lilToon.URP.Extensions.CharacterShadow;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterShadow
{
    [CustomEditor(typeof(HoCharacterShadowRendererFeature))]
    public sealed class HoCharacterShadowRendererFeatureEditor : UnityEditor.Editor
    {
        // Palette (Ho-UI 风格规范 §1): 运行 / 名称·声明 / 调试 / 高级 / RendererFeature 设置。
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color SoftShadowColor = new Color(0.42f, 0.72f, 0.58f);
        private static readonly Color DeclarationColor = new Color(0.80f, 0.55f, 0.85f);
        private static readonly Color DebugColor = new Color(0.86f, 0.62f, 0.38f);
        private static readonly Color AdvancedColor = new Color(0.62f, 0.58f, 0.78f);
        private static readonly Color StatusColor = new Color(0.45f, 0.64f, 0.96f);

        private static bool showRuntime = true;
        private static bool showSoftShadow = true;
        private static bool showDeclaration;
        private static bool showDebug;
        private static bool showAdvanced;
        private static bool showStatus;

        private SerializedProperty settingsProperty;

        private void OnEnable()
        {
            settingsProperty = serializedObject.FindProperty("settings");
            SerializedProperty shader = Find("debugShader");
            if (shader != null && shader.objectReferenceValue == null)
            {
                shader.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(
                    "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterShadow/Shaders/HoCharacterShadowDebug.shader");
                serializedObject.ApplyModifiedProperties();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (settingsProperty == null)
            {
                DrawDefaultInspector();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.HelpBox(
                "接收对象由场景里的 Ho-CharacterShadow 组件声明；这里是兜底默认值，Ho-CharacterShadow Volume 覆盖优先。",
                MessageType.Info);

            DrawRuntime();
            DrawSoftShadow();
            DrawDeclaration();
            DrawDebug();
            DrawAdvanced();
            DrawRuntimeStatus();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRuntime()
        {
            string summary = LilUrpEditorSectionGui.BoolSummary(Find("enabled"))
                + " / " + LilUrpEditorSectionGui.EnumName(Find("resolution"))
                + " / " + LilUrpEditorSectionGui.FloatSummary(Find("softnessRadius"))
                + " / " + LilUrpEditorSectionGui.IntSummary(Find("maxCharacters"), " 域");
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("enabled", "启用");
                DrawProperty("resolution", "单角色分辨率");
                DrawProperty("softnessRadius", "阴影软边（米）");
                DrawProperty("maxCharacters", "同时接收域上限");
                DrawProperty("maxAtlasSize", "图集边长上限");
                DrawProperty("depthBias", "深度偏移");
                DrawProperty("normalBias", "法线偏移");
            }
        }

        private void DrawSoftShadow()
        {
            string summary = LilUrpEditorSectionGui.BoolSummary(Find("pcssEnabled"))
                + " / " + LilUrpEditorSectionGui.EnumName(Find("pcssQuality"))
                + " / " + LilUrpEditorSectionGui.FloatSummary(Find("pcssSoftness"));
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSoftShadow, "软阴影（PCSS）", summary, SoftShadowColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("pcssEnabled", "启用 PCSS");
                DrawProperty("pcssQuality", "质量档");
                DrawProperty("pcssSoftness", "半影放大");
                DrawProperty("pcssBlockerSearchRadius", "Blocker 搜索半径（米）");
                DrawProperty("pcssMaxPenumbraRadius", "半影半径上限（米）");
                DrawProperty("pcssDepthBias", "Blocker 深度偏移");

            }
        }

        private void DrawDeclaration()
        {
            HoCharacterShadow[] subjects = Object.FindObjectsByType<HoCharacterShadow>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int allocated = 0;
            foreach (HoCharacterShadow subject in subjects)
            {
                if (subject.atlasSlice >= 0) allocated++;
            }

            string summary = subjects.Length + " 个组件 / tile " + allocated + "/" + GetTileCapacity();
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showDeclaration, "声明（只读汇总）", summary, DeclarationColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("已声明接收域", subjects.Length + " 个（场景中的 Ho-CharacterShadow 组件）");
                EditorGUILayout.LabelField("图集容量", GetTileCapacity() + " tile");
                EditorGUILayout.LabelField("当前已分配", allocated + " tile");

                if (subjects.Length == 0)
                {
                    EditorGUILayout.HelpBox(
                        "场景里没有 Ho-CharacterShadow 组件，CS 不会产生任何接收域。在角色预制件根节点加 "
                        + "Rendering/Ho-CharacterShadow，并指定该角色的 Ho-ObjectBuffer Group。",
                        MessageType.Info);
                    return;
                }

                EditorGUILayout.Space(3.0f);
                for (int i = 0; i < subjects.Length; i++)
                {
                    HoCharacterShadow subject = subjects[i];
                    string name = subject.objectGroup != null ? "组 " + subject.objectGroup.groupId : "未指定组";
                    string tile = subject.atlasSlice >= 0 ? "tile " + subject.atlasSlice : "未分配";
                    EditorGUILayout.LabelField(
                        (i + 1) + ". " + subject.name + " → " + name,
                        tile + " / 盒 " + subject.size.x.ToString("0.00") + "×" + subject.size.y.ToString("0.00")
                        + "×" + subject.size.z.ToString("0.00") + " / " + subject.status);
                }


            }
        }

        private int GetTileCapacity()
        {
            SerializedProperty resolution = Find("resolution");
            int size = resolution != null ? resolution.intValue : 2048;
            SerializedProperty maxAtlas = Find("maxAtlasSize");
            int maxAtlasSize = Mathf.Min(maxAtlas != null ? maxAtlas.intValue : 8192, SystemInfo.maxTextureSize);
            int columns = Mathf.Max(1, maxAtlasSize / Mathf.Max(1, size));
            SerializedProperty maxCharacters = Find("maxCharacters");
            return Mathf.Min(maxCharacters != null ? maxCharacters.intValue : 16, columns * columns);
        }

        private void DrawDebug()
        {
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showDebug, "调试", "在 Volume", DebugColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(
                    "调试模式与视图开关已移至 Ho-CharacterShadow Volume 的「调试」分组。",
                    MessageType.None);
            }
        }

        private void DrawAdvanced()
        {
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showAdvanced, "高级", "时机 / Shader", AdvancedColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("渲染时机", "BeforeRenderingShadows（固定）");
                EditorGUILayout.HelpBox(
                    "时机固定，改 pass 顺序前请先跑 ValidateSceneShadows 与 ValidateDistanceRendering。",
                    MessageType.None);

                DrawProperty("debugShader", "调试 Shader");

                EditorGUILayout.LabelField("图集", "feature 持有的持久 RenderTexture（Point / Clamp）");
                EditorGUILayout.LabelField("剔除", "每个接收域一份 ShadowSplitData（6 平面 + cullingSphere）");
                EditorGUILayout.LabelField("剔除光源", "feature 自己的隐藏方向光（不参与场景光照，不占相机灯光名额）");
            }
        }

        private void DrawRuntimeStatus()
        {
            string status = HoCharacterShadowRendererFeature.LastCullStatus;
            string summary = string.IsNullOrEmpty(status) ? "暂无帧" : status;
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showStatus, "运行状态", summary, StatusColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (string.IsNullOrEmpty(status))
                {
                    EditorGUILayout.HelpBox(
                        "还没有记录到 CS 运行帧。进入 Play Mode，或让使用该 RendererFeature 的 Scene/Game camera 渲染一帧。",
                        MessageType.Info);
                    return;
                }

                EditorGUILayout.LabelField("最近一次 AddRenderPasses", status);
                EditorGUILayout.HelpBox(
                    "token：disabled=被关掉 / camType=相机不支持 / light(...)=主光条件不满足 / noSlices=没有有效接收域 / "
                    + "noLocalLight=隐藏光没进剔除 / lists=n/m=建了 n 份列表。",
                    MessageType.None);
            }
        }

        private void DrawProperty(string relativeName, string label)
        {
            SerializedProperty property = Find(relativeName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
            }
        }

        private SerializedProperty Find(string relativeName)
        {
            return settingsProperty != null ? settingsProperty.FindPropertyRelative(relativeName) : null;
        }
    }
}

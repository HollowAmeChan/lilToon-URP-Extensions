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
                "CS 只替换 URP 主方向光的角色局部阴影。接收对象由场景里的 Ho-CharacterShadow 组件声明（一个 OB 组一个组件），"
                + "pass 固定排在 URP 相机阴影之前。这里是兜底默认值与结构设置；启用、单角色分辨率与调试按相机由 "
                + "Ho-CharacterShadow Volume 覆盖。",
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
                + " / " + LilUrpEditorSectionGui.IntSummary(Find("maxCharacters"), " 域");
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("enabled", "启用");
                DrawProperty("resolution", "单角色分辨率");
                DrawProperty("maxCharacters", "同时接收域上限");
                DrawProperty("maxAtlasSize", "图集边长上限");
                DrawProperty("depthBias", "深度偏移");
                DrawProperty("normalBias", "法线偏移");

                EditorGUILayout.HelpBox(
                    "启用与单角色分辨率是兜底值：Ho-CharacterShadow Volume 覆盖了就用 Volume 的（Volume 未覆盖时用这里的值）。"
                    + "容量按「图集边长上限 / 单角色分辨率」换算成可容纳的 tile 数，再与「同时接收域上限」取小；"
                    + "容量不足时不降低分辨率，多出来的接收域回退普通天光投影（在组件 Inspector 上说明）。",
                    MessageType.None);
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
                DrawProperty("softnessRadius", "最低软度（米）");
                DrawProperty("pcssQuality", "质量档");
                DrawProperty("pcssSoftness", "半影放大");
                DrawProperty("pcssBlockerSearchRadius", "Blocker 搜索半径（米）");
                DrawProperty("pcssMaxPenumbraRadius", "半影半径上限（米）");
                DrawProperty("pcssDepthBias", "Blocker 深度偏移");

                EditorGUILayout.HelpBox(
                    "PCSS：blocker search → 用平均遮挡深度估半影宽度 → 按该宽度做可变半径滤波，所以离遮挡物越远边缘越软。"
                    + "关掉、或半影放大为 0 时回退「最低软度」那个固定半径的旋转盘 PCF（降级即回退，不是另一套 shader）。"
                    + "质量档只决定采样数（上限 " + HoCharacterShadowShaderContract.PcssBlockerSamples + "/"
                    + HoCharacterShadowShaderContract.PcssFilterSamples + "，与 HLSL 里的宏一致，由 Validate() 校验）。",
                    MessageType.None);

                EditorGUILayout.HelpBox(
                    "软阴影半径一律是**米（世界单位）**，这样换分辨率不用重调；tile 越细，同样半径吃掉的 texel 越多、"
                    + "同样采样数铺开越稀，超出采样预算时半径会被收窄以免出颗粒。想更软又不想出噪点：把「单角色分辨率」"
                    + "降到 1024/2048（让 1 texel 接近 1 像素），或提高质量档。",
                    MessageType.None);

                EditorGUILayout.HelpBox(
                    "这一节是兜底值：Ho-CharacterShadow Volume 的「软阴影（PCSS）」覆盖了就用 Volume 的（Volume 未覆盖时用这里的值）。",
                    MessageType.None);
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

                EditorGUILayout.HelpBox(
                    "分配结果是上一帧渲染留下的：进入 Play Mode 或让使用该 feature 的相机渲染一帧后再看。"
                    + "tile 编号就是 Character 调试模式要填的编号。",
                    MessageType.None);
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
                    "调试模式、Debug In Scene View / Debug In Game View 与单角色 tile 已移至 Ho-CharacterShadow Volume 的「调试」分组。"
                    + "这里不再保留第二份开关，避免两份真值。",
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
                    "CS 用**自己的隐藏方向光**做局部剔除与绘制（跟着主光的方向/剔除层，但 color 黑、强度极小，"
                    + "只在相机剔除之后到本 pass 之间开着），因此不会碰 URP 的相机阴影图。"
                    + "早于相机阴影阶段的时机与最初实现一致；改这一段之前请跑 "
                    + "HoCharacterShadowValidation.ValidateSceneShadows 与 ValidateDistanceRendering。",
                    MessageType.None);

                DrawProperty("debugShader", "调试 Shader");
                EditorGUILayout.HelpBox(
                    "调试直出用。留空时用包内的 Hidden/Ho-CharacterShadow/Debug（Atlas 整图 / Character 单 tile 的深度视图）。",
                    MessageType.None);

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
                    "add: 之后为空表示这一帧排入了 CS；noSettings / disabled / camType / light(...) / noSlices "
                    + "分别表示 feature 缺设置、被 Volume 或兜底关掉、相机类型不支持、主方向光条件不满足、没有有效接收域。"
                    + "ok(...) 与 lists=n/m 只有拿到有效接收域时才追加；noLocalLight(...) 表示 CS 自己的隐藏光没进剔除结果。"
                    + "camLights/add 是相机看到的灯光数，正常情况下它和 CS 无关（CS 的灯只在相机剔除之后才开）。",
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

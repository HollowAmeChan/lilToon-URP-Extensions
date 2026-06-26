using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private static readonly string[] ParticleDirectionModeNames = { "屏幕直线", "透视放射" };
        private static readonly string[] ParticleBlendModeNames = { "正常", "加亮", "滤色", "柔光" };
        private static readonly string[] ParticleTextureModeNames = { "自动", "Alpha 透明", "黑白遮罩" };
        private static readonly string[] ParticleGlobalFadeModeNames = { "方向渐隐", "中心渐隐" };
        private static bool particleTextureFoldout = true;
        private static bool particleGlobalFadeFoldout = true;
        private static bool particleEmissionFoldout = true;
        private static bool particleMotionFoldout = true;
        private static bool particleAppearanceFoldout = true;
        private static bool particleDepthFoldout = true;

        private static int GetParticleLineCount(SerializedProperty element)
        {
            int count = 6;
            if (particleTextureFoldout)
            {
                count += 5;
            }

            if (particleGlobalFadeFoldout)
            {
                count += 7;
            }

            if (particleEmissionFoldout)
            {
                count += 9;
            }

            if (particleMotionFoldout)
            {
                count += 8;
            }

            if (particleAppearanceFoldout)
            {
                count += 11;
            }

            if (particleDepthFoldout)
            {
                count += 4;
            }

            return count;
        }

        private void DrawParticleElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            SerializedProperty parameters5 = element.FindPropertyRelative("parameters5");
            SerializedProperty parameters6 = element.FindPropertyRelative("parameters6");
            SerializedProperty parameters7 = element.FindPropertyRelative("parameters7");
            SerializedProperty parameters8 = element.FindPropertyRelative("parameters8");
            SerializedProperty parameters9 = element.FindPropertyRelative("parameters9");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureParticleDefaults(parameters0, parameters1, parameters2, parameters3, parameters4, parameters5, parameters6, parameters7, parameters8, parameters9);
            EnsureDefaultFeatherParticleTextures(element);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                if (IsImageProcessParticleViewControlActive(element))
                {
                    ImageProcessParticleViewControl.Stop();
                }

                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(
                rect.x,
                y,
                rect.width,
                element,
                includeBlendMode: false,
                includeColor: false,
                includeTexture: false,
                includePassIndex: false,
                includeMaterialOverride: false,
                includeParameters: false,
                showAdvancedFields: showAdvancedSettings);

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 p3 = parameters3.vector4Value;
            Vector4 p4 = parameters4.vector4Value;
            Vector4 p5 = parameters5.vector4Value;
            Vector4 p6 = parameters6.vector4Value;
            Vector4 p7 = parameters7.vector4Value;
            Vector4 p8 = parameters8.vector4Value;
            Vector4 p9 = parameters9.vector4Value;
            if (p8.w < 1.5f)
            {
                p8.z = p8.z <= 0.0001f ? 1.0f : Mathf.Clamp(p8.z, 0.0f, 3.0f);
                p8.w = 2.0f;
            }
            if (p8.w < 2.5f)
            {
                p6.z = p6.z <= 0.0001f ? 0.65f : Mathf.Clamp01(p6.z);
                p8.w = 3.0f;
            }

            y = DrawParticleSectionFoldout(rect, y, "羽毛贴图", ref particleTextureFoldout);
            if (particleTextureFoldout)
            {
                EditorGUI.indentLevel++;
                int textureMode = Mathf.Clamp(Mathf.RoundToInt(p8.x), 0, ParticleTextureModeNames.Length - 1);
                textureMode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "贴图模式", textureMode, ParticleTextureModeNames);
                p8.x = textureMode;
                y += LineHeight + LineSpacing;

                for (int i = 0; i < 4; i++)
                {
                    y = DrawPropertyLine(rect.x, y, rect.width, element.FindPropertyRelative($"logoTexture{i}"), $"羽毛贴图 {i + 1}");
                }

                EditorGUI.indentLevel--;
            }

            y = DrawParticleSectionFoldout(rect, y, "整体渐隐", ref particleGlobalFadeFoldout);
            if (particleGlobalFadeFoldout)
            {
                EditorGUI.indentLevel++;
                y = DrawImageProcessParticleViewControlButton(rect, y, element, ParticleViewControlMode.Fade, "可视化调整整体渐隐");

                int globalFadeMode = Mathf.Clamp(Mathf.RoundToInt(p9.x), 0, ParticleGlobalFadeModeNames.Length - 1);
                globalFadeMode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "渐隐模式", globalFadeMode, ParticleGlobalFadeModeNames);
                p9.x = globalFadeMode;
                y += LineHeight + LineSpacing;

                EditorGUI.BeginDisabledGroup(globalFadeMode != 0);
                p9.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "方向角度", Mathf.Clamp(p9.y, -180.0f, 180.0f), -180.0f, 180.0f);
                EditorGUI.EndDisabledGroup();
                y += LineHeight + LineSpacing;

                p9.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "渐隐强度", Mathf.Clamp01(p9.z), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                p9.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "渐隐范围", Mathf.Clamp(p9.w, 0.05f, 1.5f), 0.05f, 1.5f);
                y += LineHeight + LineSpacing;
                p8.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "渐隐柔化", Mathf.Clamp(p8.z, 0.0f, 3.0f), 0.0f, 3.0f);
                y += LineHeight + LineSpacing;
                p8.y = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "渐隐反相", p8.y > 0.5f) ? 1.0f : 0.0f;
                y += LineHeight + LineSpacing;
                EditorGUI.indentLevel--;
            }

            y = DrawParticleSectionFoldout(rect, y, "生成与方向", ref particleEmissionFoldout);
            if (particleEmissionFoldout)
            {
                EditorGUI.indentLevel++;
                y = DrawImageProcessParticleViewControlButton(rect, y, element, ParticleViewControlMode.Spawn, "可视化调整方向中心与方向");

                p1.x = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "方向模式", Mathf.Clamp(Mathf.RoundToInt(p1.x), 0, ParticleDirectionModeNames.Length - 1), ParticleDirectionModeNames);
                y += LineHeight + LineSpacing;
                p1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "直线角度", Mathf.Clamp(p1.y, -180.0f, 180.0f), -180.0f, 180.0f);
                y += LineHeight + LineSpacing;
                p1.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "方向强度", Mathf.Clamp(p1.w, 0.0f, 2.0f), 0.0f, 2.0f);
                y += LineHeight + LineSpacing;
                p2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "方向中心 X", Mathf.Clamp(p2.x, -0.5f, 1.5f), -0.5f, 1.5f);
                y += LineHeight + LineSpacing;
                p2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "方向中心 Y", Mathf.Clamp(p2.y, -0.5f, 1.5f), -0.5f, 1.5f);
                y += LineHeight + LineSpacing;
                p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "羽毛数量", Mathf.Clamp(p0.w, 1.0f, 32.0f), 1.0f, 32.0f);
                y += LineHeight + LineSpacing;
                p0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "生成率", Mathf.Clamp01(p0.x), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", Mathf.Clamp01(p0.y), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                EditorGUI.indentLevel--;
            }

            y = DrawParticleSectionFoldout(rect, y, "缓降运动", ref particleMotionFoldout);
            if (particleMotionFoldout)
            {
                EditorGUI.indentLevel++;
                p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "消失速度", Mathf.Clamp01(p0.z), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "运动速度", Mathf.Clamp(p1.z, 0.0f, 1.0f), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                p3.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "横向飘移", Mathf.Clamp(p3.x, 0.0f, 2.0f), 0.0f, 2.0f);
                y += LineHeight + LineSpacing;
                p3.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "摆动频率", Mathf.Clamp(p3.y, 0.0f, 4.0f), 0.0f, 4.0f);
                y += LineHeight + LineSpacing;
                p3.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "湍流强度", Mathf.Clamp(p3.z, 0.0f, 2.0f), 0.0f, 2.0f);
                y += LineHeight + LineSpacing;
                p3.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "随机性", Mathf.Clamp(p3.w, 0.0f, 2.0f), 0.0f, 2.0f);
                y += LineHeight + LineSpacing;
                p6.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "运动噪波", Mathf.Clamp(p6.x, 0.0f, 1.5f), 0.0f, 1.5f);
                y += LineHeight + LineSpacing;
                p6.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "噪波频率", Mathf.Clamp(p6.y, 0.1f, 8.0f), 0.1f, 8.0f);
                y += LineHeight + LineSpacing;
                EditorGUI.indentLevel--;
            }

            y = DrawParticleSectionFoldout(rect, y, "外观与颜色", ref particleAppearanceFoldout);
            if (particleAppearanceFoldout)
            {
                EditorGUI.indentLevel++;
                color.colorValue = EditorGUI.ColorField(new Rect(rect.x, y, rect.width, LineHeight), new GUIContent("叠加颜色"), color.colorValue, true, true, true);
                y += LineHeight + LineSpacing;
                p4.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "基础尺寸", Mathf.Clamp(p4.x, 0.02f, 0.6f), 0.02f, 0.6f);
                y += LineHeight + LineSpacing;
                p4.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "贴图旋转", Mathf.Clamp(p4.y, -180.0f, 180.0f), -180.0f, 180.0f);
                y += LineHeight + LineSpacing;
                p4.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "尺寸随机", Mathf.Clamp01(p4.z), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                p4.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "旋转随机", Mathf.Clamp(p4.w, 0.0f, 1.0f), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                p6.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "3D 旋转", Mathf.Clamp01(p6.z), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                p5.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "随机色相", Mathf.Clamp01(p5.x), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                p5.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "饱和度", Mathf.Clamp(p5.y, 0.0f, 2.0f), 0.0f, 2.0f);
                y += LineHeight + LineSpacing;
                p5.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "明度", Mathf.Clamp(p5.z, 0.0f, 2.0f), 0.0f, 2.0f);
                y += LineHeight + LineSpacing;
                p5.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "随机明暗", Mathf.Clamp01(p5.w), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                int blendMode = Mathf.Clamp(Mathf.RoundToInt(p6.w), 0, ParticleBlendModeNames.Length - 1);
                blendMode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "叠加模式", blendMode, ParticleBlendModeNames);
                p6.w = blendMode;
                y += LineHeight + LineSpacing;
                EditorGUI.indentLevel--;
            }

            y = DrawParticleSectionFoldout(rect, y, "假景深", ref particleDepthFoldout);
            if (particleDepthFoldout)
            {
                EditorGUI.indentLevel++;
                p7.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "深度层次", Mathf.Clamp(p7.x, 0.0f, 3.0f), 0.0f, 3.0f);
                y += LineHeight + LineSpacing;
                p7.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "前景放大", Mathf.Clamp(p7.y, 0.25f, 3.0f), 0.25f, 3.0f);
                y += LineHeight + LineSpacing;
                p7.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "前景虚化", Mathf.Clamp(p7.z, 0.0f, 2.0f), 0.0f, 2.0f);
                y += LineHeight + LineSpacing;
                p7.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "背景虚化", Mathf.Clamp(p7.w, 0.0f, 2.0f), 0.0f, 2.0f);
                y += LineHeight + LineSpacing;
                EditorGUI.indentLevel--;
            }

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            p2.z = 0.0f;
            p2.w = 0.0f;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = p3;
            parameters4.vector4Value = p4;
            parameters5.vector4Value = p5;
            parameters6.vector4Value = p6;
            parameters7.vector4Value = p7;
            p8.w = 3.0f;
            parameters8.vector4Value = p8;
            parameters9.vector4Value = p9;
            EditorGUI.indentLevel--;
        }

        private static float DrawParticleSectionFoldout(Rect rect, float y, string label, ref bool foldout)
        {
            foldout = EditorGUI.Foldout(new Rect(rect.x, y, rect.width, LineHeight), foldout, label, true);
            return y + LineHeight + LineSpacing;
        }

        private static void EnsureParticleDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3,
            SerializedProperty parameters4,
            SerializedProperty parameters5,
            SerializedProperty parameters6,
            SerializedProperty parameters7,
            SerializedProperty parameters8,
            SerializedProperty parameters9)
        {
            SetDefaultVector4(parameters0, new Vector4(0.85f, 0.85f, 0.85f, 13.0f));
            SetDefaultVector4(parameters1, new Vector4(0.0f, -90.0f, 0.16f, 0.85f));
            SetDefaultVector4(parameters2, new Vector4(0.5f, 0.58f, 0.0f, 0.0f));
            SetDefaultVector4(parameters3, new Vector4(0.62f, 0.85f, 0.35f, 2.0f));
            SetDefaultVector4(parameters4, new Vector4(0.16f, 0.0f, 0.55f, 0.34f));
            SetDefaultVector4(parameters5, new Vector4(0.0f, 1.0f, 1.0f, 0.22f));
            SetDefaultVector4(parameters6, new Vector4(0.13f, 2.4f, 0.65f, 0.58f));
            SetDefaultVector4(parameters7, new Vector4(1.15f, 1.45f, 0.75f, 1.25f));
            SetDefaultParticleTextureMode(parameters8);
            SetDefaultVector4(parameters9, new Vector4(0.0f, -90.0f, 0.0f, 0.75f));
        }

        private static void SetDefaultVector4(SerializedProperty property, Vector4 value)
        {
            if (property != null && property.propertyType == SerializedPropertyType.Vector4 && property.vector4Value.sqrMagnitude <= 0.000001f)
            {
                property.vector4Value = value;
            }
        }

        private static void SetDefaultParticleTextureMode(SerializedProperty property)
        {
            if (property != null && property.propertyType == SerializedPropertyType.Vector4 && property.vector4Value.sqrMagnitude <= 0.000001f)
            {
                property.vector4Value = new Vector4(2.0f, 0.0f, 1.0f, 3.0f);
            }
        }

        private static void EnsureDefaultFeatherParticleTextures(SerializedProperty element)
        {
            SerializedProperty large = element.FindPropertyRelative("logoTexture0");
            if (large != null && large.propertyType == SerializedPropertyType.ObjectReference && large.objectReferenceValue == null)
            {
                large.objectReferenceValue = LoadDefaultFeatherParticleTexture(0);
            }

            SerializedProperty small = element.FindPropertyRelative("logoTexture1");
            if (small != null && small.propertyType == SerializedPropertyType.ObjectReference && small.objectReferenceValue == null)
            {
                small.objectReferenceValue = LoadDefaultFeatherParticleTexture(1);
            }
        }

        private static void SetDefaultFeatherParticleTextures(SerializedProperty element)
        {
            SetObjectReference(element, "logoTexture0", LoadDefaultFeatherParticleTexture(0));
            SetObjectReference(element, "logoTexture1", LoadDefaultFeatherParticleTexture(1));
        }

        private static Texture2D LoadDefaultFeatherParticleTexture(int index)
        {
            string guid = index == 0 ? DefaultFeatherParticleLargeTextureGuid : DefaultFeatherParticleSmallTextureGuid;
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                return AssetDatabase.LoadAssetAtPath<Texture2D>(
                    $"{PackageAssetRoot}/Runtime/ImageProcess/Textures/{(index == 0 ? "ImageProcessFeatherParticleLarge" : "ImageProcessFeatherParticleSmall")}.png");
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}

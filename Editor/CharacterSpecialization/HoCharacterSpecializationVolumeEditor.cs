using lilToon.URP.Extensions.CharacterSpecialization;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    [CustomEditor(typeof(HoCharacterSpecializationVolume))]
    internal sealed class HoCharacterSpecializationVolumeEditor : VolumeComponentEditor
    {
        private SerializedDataParameter enable;
        private SerializedDataParameter showInSceneView;
        private SerializedDataParameter layerMask;
        private SerializedDataParameter minRenderQueue;
        private SerializedDataParameter maxRenderQueue;
        private SerializedDataParameter passEvent;
        private SerializedDataParameter renderScale;
        private SerializedDataParameter eyeRevealEnabled;
        private SerializedDataParameter eyeRevealStrength;
        private SerializedDataParameter eyeRevealFeatherPixels;
        private SerializedDataParameter eyeRevealDilationPixels;
        private SerializedDataParameter eyeRevealDepthBias;
        private SerializedDataParameter useEyeRevealArea;
        private SerializedDataParameter sameCharacterOnly;
        private SerializedDataParameter hairDropShadowEnabled;
        private SerializedDataParameter hairShadowColor;
        private SerializedDataParameter hairShadowOpacity;
        private SerializedDataParameter hairShadowDistancePixels;
        private SerializedDataParameter hairShadowDistancePerspectiveStrength;
        private SerializedDataParameter hairShadowDistanceReferenceDepth;
        private SerializedDataParameter hairShadowDistanceMinScale;
        private SerializedDataParameter hairShadowAngleDegrees;
        private SerializedDataParameter hairShadowSoftnessPixels;
        private SerializedDataParameter hairShadowSpreadPixels;
        private SerializedDataParameter hairShadowKeepOffHair;
        private SerializedDataParameter hairShadowBlendMode;
        private SerializedDataParameter debugMode;

        public override void OnEnable()
        {
            PropertyFetcher<HoCharacterSpecializationVolume> fetcher = new PropertyFetcher<HoCharacterSpecializationVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.Enable));
            showInSceneView = Unpack(fetcher.Find(x => x.ShowInSceneView));
            layerMask = Unpack(fetcher.Find(x => x.LayerMask));
            minRenderQueue = Unpack(fetcher.Find(x => x.MinRenderQueue));
            maxRenderQueue = Unpack(fetcher.Find(x => x.MaxRenderQueue));
            passEvent = Unpack(fetcher.Find(x => x.PassEvent));
            renderScale = Unpack(fetcher.Find(x => x.RenderScale));
            eyeRevealEnabled = Unpack(fetcher.Find(x => x.EyeRevealEnabled));
            eyeRevealStrength = Unpack(fetcher.Find(x => x.EyeRevealStrength));
            eyeRevealFeatherPixels = Unpack(fetcher.Find(x => x.EyeRevealFeatherPixels));
            eyeRevealDilationPixels = Unpack(fetcher.Find(x => x.EyeRevealDilationPixels));
            eyeRevealDepthBias = Unpack(fetcher.Find(x => x.EyeRevealDepthBias));
            useEyeRevealArea = Unpack(fetcher.Find(x => x.UseEyeRevealArea));
            sameCharacterOnly = Unpack(fetcher.Find(x => x.SameCharacterOnly));
            hairDropShadowEnabled = Unpack(fetcher.Find(x => x.HairDropShadowEnabled));
            hairShadowColor = Unpack(fetcher.Find(x => x.HairShadowColor));
            hairShadowOpacity = Unpack(fetcher.Find(x => x.HairShadowOpacity));
            hairShadowDistancePixels = Unpack(fetcher.Find(x => x.HairShadowDistancePixels));
            hairShadowDistancePerspectiveStrength = Unpack(fetcher.Find(x => x.HairShadowDistancePerspectiveStrength));
            hairShadowDistanceReferenceDepth = Unpack(fetcher.Find(x => x.HairShadowDistanceReferenceDepth));
            hairShadowDistanceMinScale = Unpack(fetcher.Find(x => x.HairShadowDistanceMinScale));
            hairShadowAngleDegrees = Unpack(fetcher.Find(x => x.HairShadowAngleDegrees));
            hairShadowSoftnessPixels = Unpack(fetcher.Find(x => x.HairShadowSoftnessPixels));
            hairShadowSpreadPixels = Unpack(fetcher.Find(x => x.HairShadowSpreadPixels));
            hairShadowKeepOffHair = Unpack(fetcher.Find(x => x.HairShadowKeepOffHair));
            hairShadowBlendMode = Unpack(fetcher.Find(x => x.HairShadowBlendMode));
            debugMode = Unpack(fetcher.Find(x => x.DebugMode));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "使用方式：Renderer Data 里先添加 HoCharacter Specialization RendererFeature；然后在全局或局部 Volume 里添加本组件并启用。Face、FrontHair、Eye、EyeRevealArea 需要由 HoAovGroup/RSUV 或材质 fallback 标记提供。",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "眼睛透过：Eye 标记提供眼睛颜色/深度/Alpha；FrontHair 标记作为遮挡物；EyeRevealArea 标记可选，用来限制只在指定区域透出。这里的“透过强度、羽化、扩张、深度偏移、仅同角色”只影响眼睛透过。",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "前发投影：FrontHair 标记作为投影源，Face 标记作为接收面。这里的“投影颜色、不透明度、距离、角度、柔化、扩散、避开前发、混合模式”只影响 DropShadow。它和眼睛透过可以使用不同区域，不必完全一致。",
                MessageType.Info);

            PropertyField(enable, new GUIContent("启用"));
            PropertyField(showInSceneView, new GUIContent("场景视图"));

            EditorGUILayout.Space(4.0f);
            EditorGUILayout.LabelField("捕获范围", EditorStyles.boldLabel);
            PropertyField(layerMask, new GUIContent("图层遮罩"));
            PropertyField(minRenderQueue, new GUIContent("最小渲染队列"));
            PropertyField(maxRenderQueue, new GUIContent("最大渲染队列"));
            PropertyField(passEvent, new GUIContent("渲染时机"));
            PropertyField(renderScale, new GUIContent("渲染缩放"));

            EditorGUILayout.Space(4.0f);
            EditorGUILayout.LabelField("眼睛透过", EditorStyles.boldLabel);
            PropertyField(eyeRevealEnabled, new GUIContent("启用眼睛透过"));
            PropertyField(eyeRevealStrength, new GUIContent("透过强度"));
            PropertyField(eyeRevealFeatherPixels, new GUIContent("羽化像素"));
            PropertyField(eyeRevealDilationPixels, new GUIContent("扩张像素"));
            PropertyField(eyeRevealDepthBias, new GUIContent("深度偏移"));
            PropertyField(useEyeRevealArea, new GUIContent("使用眼透区域"));
            PropertyField(sameCharacterOnly, new GUIContent("仅同角色"));

            EditorGUILayout.Space(4.0f);
            EditorGUILayout.LabelField("前发投影 DropShadow", EditorStyles.boldLabel);
            PropertyField(hairDropShadowEnabled, new GUIContent("启用前发投影"));
            PropertyField(hairShadowColor, new GUIContent("投影颜色"));
            PropertyField(hairShadowOpacity, new GUIContent("投影不透明度"));
            PropertyField(hairShadowDistancePixels, new GUIContent("投影距离像素"));
            PropertyField(hairShadowDistancePerspectiveStrength, new GUIContent("投影距离透视衰减"));
            PropertyField(hairShadowDistanceReferenceDepth, new GUIContent("投影距离参考深度"));
            PropertyField(hairShadowDistanceMinScale, new GUIContent("投影距离最小倍率"));
            PropertyField(hairShadowAngleDegrees, new GUIContent("投影角度"));
            PropertyField(hairShadowSoftnessPixels, new GUIContent("柔化像素"));
            PropertyField(hairShadowSpreadPixels, new GUIContent("扩散像素"));
            PropertyField(hairShadowKeepOffHair, new GUIContent("避开前发"));
            PropertyField(hairShadowBlendMode, new GUIContent("混合模式"));

            EditorGUILayout.Space(4.0f);
            EditorGUILayout.LabelField("调试", EditorStyles.boldLabel);
            PropertyField(debugMode, new GUIContent("调试模式"));

            serializedObject.ApplyModifiedProperties();
        }
    }
}

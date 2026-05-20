using lilToon.URP.Extensions.PlanarReflection;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PlanarReflection
{
    [CustomEditor(typeof(LILPlanarReflectionSurface))]
    internal sealed class LILPlanarReflectionSurfaceEditor : UnityEditor.Editor
    {
        private SerializedProperty targetRenderer;
        private SerializedProperty planeTransform;
        private SerializedProperty useTargetRendererBoundsCenter;
        private SerializedProperty reflectionMask;
        private SerializedProperty resolution;
        private SerializedProperty clipPlaneOffset;
        private SerializedProperty useObliqueClipPlane;
        private SerializedProperty minClipPlaneDistance;
        private SerializedProperty reflectionNearClipPlane;
        private SerializedProperty hideSurfaceInReflection;
        private SerializedProperty overrideMaterialToggle;
        private SerializedProperty copyCameraClearFlags;
        private SerializedProperty fallbackClearFlags;
        private SerializedProperty fallbackBackgroundColor;
        private SerializedProperty reflectSceneView;
        private SerializedProperty frameInterval;

        private static readonly GUIContent TargetRendererLabel = new GUIContent("目标渲染器", "接收平面反射纹理和材质参数的 Renderer。留空时会自动使用当前物体上的 Renderer。");
        private static readonly GUIContent PlaneTransformLabel = new GUIContent("反射平面锚点", "指定反射平面的位置和法线。留空时位置优先使用目标渲染器包围盒中心，法线使用目标渲染器 Transform.up。");
        private static readonly GUIContent UseTargetRendererBoundsCenterLabel = new GUIContent("使用渲染器中心", "未指定反射平面锚点时，用目标渲染器 bounds.center 作为平面位置，避免组件挂载物体的 Transform 偏移影响反射平面。");
        private static readonly GUIContent ReflectionMaskLabel = new GUIContent("反射层遮罩", "只有这些层会被渲染进平面反射。把镜面本身和不需要反射的遮挡物排除，可避免反射相机被实心物体挡住。");
        private static readonly GUIContent ResolutionLabel = new GUIContent("反射分辨率", "反射纹理的宽度。高度会按源相机宽高比自动计算。");
        private static readonly GUIContent ClipPlaneOffsetLabel = new GUIContent("裁剪平面偏移", "把反射裁剪面沿反射平面法线偏移。0 表示精确贴合反射平面。");
        private static readonly GUIContent UseObliqueClipPlaneLabel = new GUIContent("使用平面裁剪", "开启后用反射平面作为斜裁剪面，避免镜面背后的物体进入反射。关闭后使用普通相机裁剪，适合排查裁剪导致的反射消失。");
        private static readonly GUIContent MinClipPlaneDistanceLabel = new GUIContent("最小裁剪距离", "源相机太贴近反射平面时，把裁剪面略微后退，避免斜裁剪面贴到反射相机导致整张反射消失。");
        private static readonly GUIContent ReflectionNearClipPlaneLabel = new GUIContent("反射相机近裁剪", "反射相机的最小 near clip。调小可减少贴近镜面时的裁切，调大可减少近处穿模噪声。");
        private static readonly GUIContent HideSurfaceInReflectionLabel = new GUIContent("反射中隐藏本体", "渲染反射时临时隐藏目标渲染器，避免平面自己递归反射。");
        private static readonly GUIContent OverrideMaterialToggleLabel = new GUIContent("自动启用材质开关", "通过 MaterialPropertyBlock 自动写入 _UsePlanarReflection。关闭后需要在材质上手动启用平面反射。");
        private static readonly GUIContent CopyCameraClearFlagsLabel = new GUIContent("复制相机清屏设置", "开启时使用源相机的清屏方式和背景色；关闭时使用下面的备用设置。");
        private static readonly GUIContent FallbackClearFlagsLabel = new GUIContent("备用清屏方式");
        private static readonly GUIContent FallbackBackgroundColorLabel = new GUIContent("备用背景色");
        private static readonly GUIContent ReflectSceneViewLabel = new GUIContent("反射场景视图", "在 Scene View 相机中也渲染平面反射，方便编辑时预览。");
        private static readonly GUIContent FrameIntervalLabel = new GUIContent("更新帧间隔", "每隔多少帧更新一次反射。1 表示每帧更新。");

        private void OnEnable()
        {
            targetRenderer = serializedObject.FindProperty("targetRenderer");
            planeTransform = serializedObject.FindProperty("planeTransform");
            useTargetRendererBoundsCenter = serializedObject.FindProperty("useTargetRendererBoundsCenter");
            reflectionMask = serializedObject.FindProperty("reflectionMask");
            resolution = serializedObject.FindProperty("resolution");
            clipPlaneOffset = serializedObject.FindProperty("clipPlaneOffset");
            useObliqueClipPlane = serializedObject.FindProperty("useObliqueClipPlane");
            minClipPlaneDistance = serializedObject.FindProperty("minClipPlaneDistance");
            reflectionNearClipPlane = serializedObject.FindProperty("reflectionNearClipPlane");
            hideSurfaceInReflection = serializedObject.FindProperty("hideSurfaceInReflection");
            overrideMaterialToggle = serializedObject.FindProperty("overrideMaterialToggle");
            copyCameraClearFlags = serializedObject.FindProperty("copyCameraClearFlags");
            fallbackClearFlags = serializedObject.FindProperty("fallbackClearFlags");
            fallbackBackgroundColor = serializedObject.FindProperty("fallbackBackgroundColor");
            reflectSceneView = serializedObject.FindProperty("reflectSceneView");
            frameInterval = serializedObject.FindProperty("frameInterval");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("平面反射表面", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(targetRenderer, TargetRendererLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("反射平面", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(planeTransform, PlaneTransformLabel);
            using (new EditorGUI.DisabledScope(planeTransform.objectReferenceValue != null))
            {
                EditorGUILayout.PropertyField(useTargetRendererBoundsCenter, UseTargetRendererBoundsCenterLabel);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("反射渲染", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(reflectionMask, ReflectionMaskLabel);
            EditorGUILayout.PropertyField(resolution, ResolutionLabel);
            EditorGUILayout.PropertyField(hideSurfaceInReflection, HideSurfaceInReflectionLabel);
            EditorGUILayout.PropertyField(overrideMaterialToggle, OverrideMaterialToggleLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("裁剪", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(useObliqueClipPlane, UseObliqueClipPlaneLabel);
            using (new EditorGUI.DisabledScope(!useObliqueClipPlane.boolValue))
            {
                EditorGUILayout.PropertyField(clipPlaneOffset, ClipPlaneOffsetLabel);
                EditorGUILayout.PropertyField(minClipPlaneDistance, MinClipPlaneDistanceLabel);
            }
            EditorGUILayout.PropertyField(reflectionNearClipPlane, ReflectionNearClipPlaneLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("背景", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(copyCameraClearFlags, CopyCameraClearFlagsLabel);
            using (new EditorGUI.DisabledScope(copyCameraClearFlags.boolValue))
            {
                EditorGUILayout.PropertyField(fallbackClearFlags, FallbackClearFlagsLabel);
                EditorGUILayout.PropertyField(fallbackBackgroundColor, FallbackBackgroundColorLabel);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("调试与性能", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(reflectSceneView, ReflectSceneViewLabel);
            EditorGUILayout.PropertyField(frameInterval, FrameIntervalLabel);

            serializedObject.ApplyModifiedProperties();
        }
    }
}

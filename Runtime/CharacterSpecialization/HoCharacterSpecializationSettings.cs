using System;
using lilToon.URP.Extensions.MetadataBuffer;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    public enum HoCharacterShadowBlendMode
    {
        [InspectorName("正片叠底")]
        Multiply = 0,
        [InspectorName("线性混合")]
        Lerp = 1
    }

    public enum HoCharacterSpecializationDebugMode
    {
        [InspectorName("关闭")]
        Off = 0,
        [InspectorName("眼睛颜色")]
        EyeColor = 1,
        [InspectorName("眼睛 Alpha")]
        EyeAlpha = 2,
        [InspectorName("眼睛透过遮罩")]
        EyeRevealMask = 3,
        [InspectorName("前发投影遮罩")]
        HairShadowMask = 4
    }

    [Serializable]
    public sealed class HoCharacterSpecializationSettings
    {
        [InspectorName("启用")]
        [Tooltip("跳过或启用整个角色特化合成。推荐通过 Volume 控制。")]
        public bool enabled = true;

        [InspectorName("图层遮罩")]
        [Tooltip("参与角色捕获的对象图层。")]
        public LayerMask layerMask = -1;

        [InspectorName("最小渲染队列")]
        [Tooltip("角色捕获包含的最低 render queue。")]
        public int minRenderQueue = 0;

        [InspectorName("最大渲染队列")]
        [Tooltip("角色捕获包含的最高 render queue。")]
        public int maxRenderQueue = (int)RenderQueue.Overlay - 1;

        [InspectorName("渲染时机")]
        [Tooltip("角色特化捕获和合成的执行时机，通常放在透明物之后。")]
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingTransparents;

        [InspectorName("渲染缩放")]
        [Tooltip("捕获 RT 的分辨率。降低分辨率会省带宽，但会影响边缘质量。")]
        public HoCharacterRenderScale renderScale = HoCharacterRenderScale.Full;

        [InspectorName("合成 Shader")]
        [Tooltip("为空时自动使用 Hidden/lilToon-HoCharacter/URP/Composite。一般不需要改。")]
        public Shader compositeShader;

        [Header("眼睛透过")]
        [InspectorName("启用眼睛透过")]
        [Tooltip("让被前发遮挡的眼睛按眼睛捕获结果透出。")]
        public bool eyeRevealEnabled = true;

        [InspectorName("透过强度")]
        [Tooltip("眼睛透过的总强度。材质侧还可以用 Character Capture Opacity 调每个眼睛材质的捕获 alpha。")]
        [Range(0.0f, 1.0f)]
        public float eyeRevealStrength = 0.05f;

        [InspectorName("羽化像素")]
        [Tooltip("眼睛透过遮罩的边缘羽化。")]
        [Min(0.0f)]
        public float eyeRevealFeatherPixels = 1.0f;

        [InspectorName("扩张像素")]
        [Tooltip("眼睛透过遮罩向外扩张的像素数。")]
        [Min(0.0f)]
        public float eyeRevealDilationPixels = 2.0f;

        [InspectorName("深度偏移")]
        [Tooltip("判断前发是否在眼睛前方时使用的深度偏移。")]
        [Min(0.0f)]
        public float eyeRevealDepthBias = 0.01f;

        [InspectorName("使用眼透区域")]
        [Tooltip("启用后，眼睛透过还会受 ObjectCustom4 / 眼透区域限制。")]
        public bool useEyeRevealArea = true;

        [InspectorName("仅同角色")]
        [Tooltip("启用后，只允许同 Character ID 的前发影响同角色的眼睛/脸。")]
        public bool sameCharacterOnly = true;

        [Header("前发投影")]
        [InspectorName("启用前发投影")]
        [Tooltip("用 FrontHair 标记向 Face 标记投射屏幕空间阴影。")]
        public bool hairDropShadowEnabled = true;

        [InspectorName("投影颜色")]
        [Tooltip("前发投影颜色。")]
        public Color hairShadowColor = new Color(0.78f, 0.68f, 0.72f, 1.0f);

        [InspectorName("投影不透明度")]
        [Tooltip("前发投影强度。")]
        [Range(0.0f, 1.0f)]
        public float hairShadowOpacity = 1.0f;

        [InspectorName("投影距离像素")]
        [Tooltip("投影沿角度方向偏移的屏幕像素距离。")]
        [Min(0.0f)]
        public float hairShadowDistancePixels = 15.0f;

        [InspectorName("投影距离透视衰减")]
        [Tooltip("按 GeometryBuffer 线性深度压缩远处的投影偏移，避免角色离镜头较远时固定像素距离显得过大。0 为关闭，1 为完全按深度衰减。")]
        [Range(0.0f, 1.0f)]
        public float hairShadowDistancePerspectiveStrength = 1.0f;

        [InspectorName("投影距离参考深度")]
        [Tooltip("线性深度小于或等于此值时保持原始像素距离；更远处会按参考深度 / 当前深度缩短。单位通常近似为米。")]
        [Min(0.0f)]
        public float hairShadowDistanceReferenceDepth = 2.0f;

        [InspectorName("投影距离最小倍率")]
        [Tooltip("远处投影距离衰减的下限，避免阴影在远景完全贴回前发。")]
        [Range(0.0f, 1.0f)]
        public float hairShadowDistanceMinScale = 0.25f;

        [InspectorName("投影角度")]
        [Tooltip("投影方向，单位为角度。")]
        public float hairShadowAngleDegrees = 240.0f;

        [InspectorName("柔化像素")]
        [Tooltip("前发投影边缘柔化范围。")]
        [Min(0.0f)]
        public float hairShadowSoftnessPixels = 2.0f;

        [InspectorName("扩散像素")]
        [Tooltip("前发投影遮罩扩张范围。")]
        [Min(0.0f)]
        public float hairShadowSpreadPixels = 0.0f;

        [InspectorName("避开前发")]
        [Tooltip("避免投影重新盖回前发自身的强度。")]
        [Range(0.0f, 1.0f)]
        public float hairShadowKeepOffHair = 1.0f;

        [InspectorName("混合模式")]
        [Tooltip("前发投影与画面的混合方式。")]
        public HoCharacterShadowBlendMode hairShadowBlendMode = HoCharacterShadowBlendMode.Multiply;

        [Header("未来模块")]
        [InspectorName("远平面阴影预留")]
        public bool farPlaneShadowReserved;

        [InspectorName("反射空间预留")]
        public bool reflectionSpaceReserved;

        [InspectorName("调试模式")]
        [Tooltip("把角色特化的中间结果写回当前视图，便于检查眼睛透过和前发投影。")]
        public HoCharacterSpecializationDebugMode debugMode = HoCharacterSpecializationDebugMode.Off;

        public void CopyFrom(HoCharacterSpecializationSettings source)
        {
            if (source == null)
            {
                return;
            }

            enabled = source.enabled;
            layerMask = source.layerMask;
            minRenderQueue = source.minRenderQueue;
            maxRenderQueue = source.maxRenderQueue;
            passEvent = source.passEvent;
            renderScale = source.renderScale;
            compositeShader = source.compositeShader;
            eyeRevealEnabled = source.eyeRevealEnabled;
            eyeRevealStrength = source.eyeRevealStrength;
            eyeRevealFeatherPixels = source.eyeRevealFeatherPixels;
            eyeRevealDilationPixels = source.eyeRevealDilationPixels;
            eyeRevealDepthBias = source.eyeRevealDepthBias;
            useEyeRevealArea = source.useEyeRevealArea;
            sameCharacterOnly = source.sameCharacterOnly;
            hairDropShadowEnabled = source.hairDropShadowEnabled;
            hairShadowColor = source.hairShadowColor;
            hairShadowOpacity = source.hairShadowOpacity;
            hairShadowDistancePixels = source.hairShadowDistancePixels;
            hairShadowDistancePerspectiveStrength = source.hairShadowDistancePerspectiveStrength;
            hairShadowDistanceReferenceDepth = source.hairShadowDistanceReferenceDepth;
            hairShadowDistanceMinScale = source.hairShadowDistanceMinScale;
            hairShadowAngleDegrees = source.hairShadowAngleDegrees;
            hairShadowSoftnessPixels = source.hairShadowSoftnessPixels;
            hairShadowSpreadPixels = source.hairShadowSpreadPixels;
            hairShadowKeepOffHair = source.hairShadowKeepOffHair;
            hairShadowBlendMode = source.hairShadowBlendMode;
            farPlaneShadowReserved = source.farPlaneShadowReserved;
            reflectionSpaceReserved = source.reflectionSpaceReserved;
            debugMode = source.debugMode;
        }
    }
}

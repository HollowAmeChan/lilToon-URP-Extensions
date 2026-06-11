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

    public enum HoCharacterFaceHairDiffuseBlendMode
    {
        [InspectorName("线性混合")]
        Lerp = 0,
        [InspectorName("加色")]
        Additive = 1,
        [InspectorName("滤色")]
        Screen = 2
    }

    public enum HoCharacterSubjectOutlineFillMode
    {
        [InspectorName("纯色描边")]
        SolidColor = 0,
        [InspectorName("彩色流光描边")]
        NormalColor = 1,
        [InspectorName("柔化雾气")]
        SoftFog = 2
    }

    public enum HoCharacterSubjectOutlineHeightFadeMode
    {
        [InspectorName("关闭")]
        Off = 0,
        [InspectorName("靠近地面变浅")]
        FadeNearGround = 1,
        [InspectorName("远离地面变浅")]
        FadeFarFromGround = 2
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
        HairShadowMask = 4,
        [InspectorName("脸色扩散源遮罩")]
        FaceHairDiffuseSourceMask = 5,
        [InspectorName("脸色扩散模糊遮罩")]
        FaceHairDiffuseBlurMask = 6,
        [InspectorName("脸色扩散模糊颜色")]
        FaceHairDiffuseBlurColor = 7,
        [InspectorName("脸色扩散最终遮罩")]
        FaceHairDiffuseMask = 8,
        [InspectorName("主体轮廓源遮罩")]
        SubjectOutlineSourceMask = 9,
        [InspectorName("主体轮廓模糊遮罩")]
        SubjectOutlineBlurMask = 10,
        [InspectorName("主体轮廓最终遮罩")]
        SubjectOutlineMask = 11,
        [InspectorName("主体轮廓方向")]
        SubjectOutlineNormal = 12
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
        [Tooltip("为空时自动使用 Hidden/lilToon-HoCharacterSpecialization/URP/Composite。一般不需要改。")]
        public Shader compositeShader;

        [InspectorName("脸色扩散 Shader")]
        [Tooltip("为空时自动使用 Hidden/lilToon-HoCharacterSpecialization/URP/FaceHairDiffuse。一般不需要改。")]
        public Shader faceHairDiffuseShader;

        [InspectorName("主体轮廓 Shader")]
        [Tooltip("为空时自动使用 Hidden/lilToon-HoCharacterSpecialization/URP/SubjectOutline。一般不需要改。")]
        public Shader subjectOutlineShader;

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

        [Header("脸色扩散到前发")]
        [InspectorName("启用脸色扩散")]
        [Tooltip("把 Face 的 SurfaceColor 大范围模糊后叠到 FrontHair 上，用于近景前发的脸色透感。")]
        public bool faceHairDiffuseEnabled = false;

        [InspectorName("扩散强度")]
        [Tooltip("脸色扩散叠到前发上的总强度。")]
        [Range(0.0f, 1.0f)]
        public float faceHairDiffuseStrength = 0.35f;

        [InspectorName("模糊半径像素")]
        [Tooltip("Face 颜色扩散的屏幕空间模糊半径。")]
        [Min(0.0f)]
        public float faceHairDiffuseRadiusPixels = 48.0f;

        [InspectorName("深度容差")]
        [Tooltip("当前 FrontHair 与模糊 Face 深度之间允许的线性深度差。")]
        [Min(0.0f)]
        public float faceHairDiffuseDepthTolerance = 0.25f;

        [InspectorName("色阶黑场")]
        [Tooltip("模糊遮罩低于该值时压到 0。")]
        [Range(0.0f, 1.0f)]
        public float faceHairDiffuseLevelBlack = 0.02f;

        [InspectorName("色阶白场")]
        [Tooltip("模糊遮罩高于该值时推到 1。")]
        [Range(0.0f, 1.0f)]
        public float faceHairDiffuseLevelWhite = 0.45f;

        [InspectorName("颜色乘")]
        [Tooltip("叠到前发前乘到 Face SurfaceColor 上的颜色。Alpha 也会乘到最终强度。")]
        public Color faceHairDiffuseTintColor = new Color(1.0f, 0.78f, 0.72f, 1.0f);

        [InspectorName("混合模式")]
        [Tooltip("脸色扩散与当前前发颜色的混合方式。")]
        public HoCharacterFaceHairDiffuseBlendMode faceHairDiffuseBlendMode = HoCharacterFaceHairDiffuseBlendMode.Additive;

        [Header("主体轮廓")]
        [InspectorName("启用主体轮廓")]
        [Tooltip("读取 ObjectCustom0.r 的主体遮罩，生成高精度外扩轮廓。")]
        public bool subjectOutlineEnabled = false;

        [InspectorName("轮廓强度")]
        [Tooltip("主体轮廓叠到画面上的总强度。")]
        [Range(0.0f, 1.0f)]
        public float subjectOutlineStrength = 1.0f;

        [InspectorName("外扩半径像素")]
        [Tooltip("主体轮廓向外扩张和圆润化的屏幕空间半径。")]
        [Min(0.0f)]
        public float subjectOutlineRadiusPixels = 6.0f;

        [InspectorName("边缘黑场")]
        [Tooltip("模糊遮罩低于该值时压到 0。")]
        [Range(0.0f, 1.0f)]
        public float subjectOutlineLevelBlack = 0.02f;

        [InspectorName("边缘白场")]
        [Tooltip("模糊遮罩高于该值时推到 1。")]
        [Range(0.0f, 1.0f)]
        public float subjectOutlineLevelWhite = 0.35f;

        [InspectorName("轮廓颜色")]
        [Tooltip("主体外扩轮廓颜色。Alpha 也会乘到最终强度。")]
        public Color subjectOutlineColor = Color.white;

        [InspectorName("风格模式")]
        [Tooltip("纯色描边使用轮廓颜色；彩色流光描边会把边缘梯度方向映射为 HSV 色相；柔化雾气会把原图底色乘雾气颜色后做 HSV 变换并加色叠加。")]
        public HoCharacterSubjectOutlineFillMode subjectOutlineFillMode = HoCharacterSubjectOutlineFillMode.SolidColor;

        [InspectorName("法线旋转")]
        [Tooltip("彩色流光描边模式下，对整圈外扩方向做统一旋转。")]
        public float subjectOutlineNormalRotationDegrees = 0.0f;

        [InspectorName("法线流动速度")]
        [Tooltip("彩色流光描边模式下，方向随时间旋转的速度，单位为度/秒。")]
        public float subjectOutlineNormalFlowDegreesPerSecond = 0.0f;

        [InspectorName("雾气颜色")]
        [Tooltip("柔化雾气模式下，先乘到原图底色上的颜色。Alpha 也会乘到最终雾气强度。")]
        public Color subjectOutlineFogColor = new Color(1.0f, 0.85f, 0.65f, 1.0f);

        [InspectorName("雾气色相偏移")]
        [Tooltip("柔化雾气模式下，对乘色后的 HSV 色相做偏移，单位为度。")]
        public float subjectOutlineFogHueShiftDegrees = 0.0f;

        [InspectorName("雾气饱和度")]
        [Tooltip("柔化雾气模式下，对乘色后的 HSV 饱和度做倍率调整。")]
        [Range(0.0f, 4.0f)]
        public float subjectOutlineFogSaturation = 1.0f;

        [InspectorName("雾气亮度")]
        [Tooltip("柔化雾气模式下，对乘色后的 HSV 明度做倍率调整。")]
        [Range(0.0f, 4.0f)]
        public float subjectOutlineFogValue = 1.0f;

        [InspectorName("雾气柔化")]
        [Tooltip("柔化雾气模式下，控制外扩 SDF 边缘的软硬。低值更雾化，高值更锐。")]
        [Range(0.05f, 4.0f)]
        public float subjectOutlineFogSoftness = 0.55f;

        [InspectorName("高度渐隐")]
        [Tooltip("按主体轮廓来源点的世界高度减弱轮廓。")]
        public HoCharacterSubjectOutlineHeightFadeMode subjectOutlineHeightFadeMode = HoCharacterSubjectOutlineHeightFadeMode.Off;

        [InspectorName("地面高度")]
        [Tooltip("高度渐隐的地面世界 Y。")]
        public float subjectOutlineHeightFadeGroundY = 0.0f;

        [InspectorName("渐隐开始距离")]
        [Tooltip("距离地面小于等于该值时进入渐隐区间。")]
        [Min(0.0f)]
        public float subjectOutlineHeightFadeStart = 0.0f;

        [InspectorName("渐隐结束距离")]
        [Tooltip("距离地面大于等于该值时结束渐隐区间。")]
        [Min(0.0f)]
        public float subjectOutlineHeightFadeEnd = 1.0f;

        [InspectorName("渐隐硬度")]
        [Tooltip("高度渐隐过渡曲线的硬度。1 为标准平滑过渡，数值越大越硬，越小越柔。")]
        [Range(0.1f, 8.0f)]
        public float subjectOutlineHeightFadeHardness = 1.0f;

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
            faceHairDiffuseShader = source.faceHairDiffuseShader;
            subjectOutlineShader = source.subjectOutlineShader;
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
            faceHairDiffuseEnabled = source.faceHairDiffuseEnabled;
            faceHairDiffuseStrength = source.faceHairDiffuseStrength;
            faceHairDiffuseRadiusPixels = source.faceHairDiffuseRadiusPixels;
            faceHairDiffuseDepthTolerance = source.faceHairDiffuseDepthTolerance;
            faceHairDiffuseLevelBlack = source.faceHairDiffuseLevelBlack;
            faceHairDiffuseLevelWhite = source.faceHairDiffuseLevelWhite;
            faceHairDiffuseTintColor = source.faceHairDiffuseTintColor;
            faceHairDiffuseBlendMode = source.faceHairDiffuseBlendMode;
            subjectOutlineEnabled = source.subjectOutlineEnabled;
            subjectOutlineStrength = source.subjectOutlineStrength;
            subjectOutlineRadiusPixels = source.subjectOutlineRadiusPixels;
            subjectOutlineLevelBlack = source.subjectOutlineLevelBlack;
            subjectOutlineLevelWhite = source.subjectOutlineLevelWhite;
            subjectOutlineColor = source.subjectOutlineColor;
            subjectOutlineFillMode = source.subjectOutlineFillMode;
            subjectOutlineNormalRotationDegrees = source.subjectOutlineNormalRotationDegrees;
            subjectOutlineNormalFlowDegreesPerSecond = source.subjectOutlineNormalFlowDegreesPerSecond;
            subjectOutlineFogColor = source.subjectOutlineFogColor;
            subjectOutlineFogHueShiftDegrees = source.subjectOutlineFogHueShiftDegrees;
            subjectOutlineFogSaturation = source.subjectOutlineFogSaturation;
            subjectOutlineFogValue = source.subjectOutlineFogValue;
            subjectOutlineFogSoftness = source.subjectOutlineFogSoftness;
            subjectOutlineHeightFadeMode = source.subjectOutlineHeightFadeMode;
            subjectOutlineHeightFadeGroundY = source.subjectOutlineHeightFadeGroundY;
            subjectOutlineHeightFadeStart = source.subjectOutlineHeightFadeStart;
            subjectOutlineHeightFadeEnd = source.subjectOutlineHeightFadeEnd;
            subjectOutlineHeightFadeHardness = source.subjectOutlineHeightFadeHardness;
            farPlaneShadowReserved = source.farPlaneShadowReserved;
            reflectionSpaceReserved = source.reflectionSpaceReserved;
            debugMode = source.debugMode;
        }
    }
}

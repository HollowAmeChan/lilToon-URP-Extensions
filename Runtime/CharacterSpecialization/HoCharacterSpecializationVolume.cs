using System;
using lilToon.URP.Extensions.MetadataBuffer;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    [Serializable]
    public sealed class HoCharacterLayerMaskParameter : VolumeParameter<LayerMask>
    {
        public HoCharacterLayerMaskParameter(LayerMask value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(LayerMask from, LayerMask to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterRenderPassEventParameter : VolumeParameter<RenderPassEvent>
    {
        public HoCharacterRenderPassEventParameter(RenderPassEvent value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(RenderPassEvent from, RenderPassEvent to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterRenderScaleParameter : VolumeParameter<HoCharacterRenderScale>
    {
        public HoCharacterRenderScaleParameter(HoCharacterRenderScale value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterRenderScale from, HoCharacterRenderScale to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterShadowBlendModeParameter : VolumeParameter<HoCharacterShadowBlendMode>
    {
        public HoCharacterShadowBlendModeParameter(HoCharacterShadowBlendMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterShadowBlendMode from, HoCharacterShadowBlendMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterFaceHairDiffuseBlendModeParameter : VolumeParameter<HoCharacterFaceHairDiffuseBlendMode>
    {
        public HoCharacterFaceHairDiffuseBlendModeParameter(HoCharacterFaceHairDiffuseBlendMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterFaceHairDiffuseBlendMode from, HoCharacterFaceHairDiffuseBlendMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterSubjectOutlineFillModeParameter : VolumeParameter<HoCharacterSubjectOutlineFillMode>
    {
        public HoCharacterSubjectOutlineFillModeParameter(HoCharacterSubjectOutlineFillMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterSubjectOutlineFillMode from, HoCharacterSubjectOutlineFillMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterSubjectOutlineHeightFadeModeParameter : VolumeParameter<HoCharacterSubjectOutlineHeightFadeMode>
    {
        public HoCharacterSubjectOutlineHeightFadeModeParameter(HoCharacterSubjectOutlineHeightFadeMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterSubjectOutlineHeightFadeMode from, HoCharacterSubjectOutlineHeightFadeMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterObjectCustomChannelParameter : VolumeParameter<HoCharacterObjectCustomChannel>
    {
        public HoCharacterObjectCustomChannelParameter(HoCharacterObjectCustomChannel value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterObjectCustomChannel from, HoCharacterObjectCustomChannel to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

#if UNITY_2023_1_OR_NEWER
    [VolumeComponentMenu("Post-processing/Ho-CharacterSpecialization/角色特化"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#else
    [VolumeComponentMenuForRenderPipeline("Post-processing/Ho-CharacterSpecialization/角色特化", typeof(UniversalRenderPipeline))]
#endif
#if UNITY_2023_3_OR_NEWER
    [VolumeRequiresRendererFeatures(typeof(HoCharacterSpecializationRendererFeature))]
#endif
    [Serializable]
    public sealed class HoCharacterSpecializationVolume : VolumeComponent, IPostProcessComponent
    {
        public HoCharacterSpecializationVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = "Ho-CharacterSpecialization 角色特化";
#endif
            Enable.overrideState = true;
            EyeRevealEnabled.overrideState = true;
            EyeRevealStrength.overrideState = true;
            EyeRevealAngleEnabled.overrideState = true;
            EyeRevealAngleStrength.overrideState = true;
            EyeRevealAngleYawRangeDegrees.overrideState = true;
            EyeRevealAnglePitchRangeDegrees.overrideState = true;
            EyeRevealAngleSoftnessDegrees.overrideState = true;
            HairDropShadowEnabled.overrideState = true;
            HairShadowColor.overrideState = true;
            HairShadowOpacity.overrideState = true;
            HairShadowDistancePixels.overrideState = true;
            HairShadowDistancePerspectiveStrength.overrideState = true;
            HairShadowDistanceReferenceDepth.overrideState = true;
            HairShadowDistanceMinScale.overrideState = true;
            HairShadowAngleDegrees.overrideState = true;
            FaceHairDiffuseEnabled.overrideState = true;
            FaceHairDiffuseStrength.overrideState = true;
            FaceHairDiffuseRadiusPixels.overrideState = true;
            FaceHairDiffuseDepthTolerance.overrideState = true;
            FaceHairDiffuseLevelBlack.overrideState = true;
            FaceHairDiffuseLevelWhite.overrideState = true;
            FaceHairDiffuseTintColor.overrideState = true;
            FaceHairDiffuseBlendMode.overrideState = true;
            SubjectOutlineEnabled.overrideState = true;
            SubjectOutlineStrength.overrideState = true;
            SubjectOutlineRadiusPixels.overrideState = true;
            SubjectOutlineLevelBlack.overrideState = true;
            SubjectOutlineLevelWhite.overrideState = true;
            SubjectOutlineColor.overrideState = true;
            SubjectOutlineFillMode.overrideState = true;
            SubjectOutlineNormalRotationDegrees.overrideState = true;
            SubjectOutlineNormalFlowDegreesPerSecond.overrideState = true;
            SubjectOutlineFogColor.overrideState = true;
            SubjectOutlineFogHueShiftDegrees.overrideState = true;
            SubjectOutlineFogSaturation.overrideState = true;
            SubjectOutlineFogValue.overrideState = true;
            SubjectOutlineFogSoftness.overrideState = true;
            SubjectOutlineHeightFadeMode.overrideState = true;
            SubjectOutlineHeightFadeGroundY.overrideState = true;
            SubjectOutlineHeightFadeStart.overrideState = true;
            SubjectOutlineHeightFadeEnd.overrideState = true;
            SubjectOutlineHeightFadeHardness.overrideState = true;
            EnhancedOutlineEnabled.overrideState = true;
            EnhancedOutlineSourceChannel.overrideState = true;
            EnhancedOutlineStrength.overrideState = true;
            EnhancedOutlineRadiusPixels.overrideState = true;
            EnhancedOutlineFogColor.overrideState = true;
            EnhancedOutlineFogHueShiftDegrees.overrideState = true;
            EnhancedOutlineFogSaturation.overrideState = true;
            EnhancedOutlineFogValue.overrideState = true;
            EnhancedOutlineFogSoftness.overrideState = true;
            EnhancedOutlineHeightFadeMode.overrideState = true;
            EnhancedOutlineHeightFadeGroundY.overrideState = true;
            EnhancedOutlineHeightFadeStart.overrideState = true;
            EnhancedOutlineHeightFadeEnd.overrideState = true;
            EnhancedOutlineHeightFadeHardness.overrideState = true;
        }

        [InspectorName("启用"), Tooltip("启用角色特化眼睛透过和前发投影。")]
        public BoolParameter Enable = new BoolParameter(true, BoolParameter.DisplayType.EnumPopup);

        [InspectorName("场景视图"), Tooltip("是否在 Scene View 里显示这个 Volume 的角色特化结果。")]
        public BoolParameter ShowInSceneView = new BoolParameter(true, false);

        [InspectorName("图层遮罩"), Tooltip("参与角色捕获的对象图层。")]
        public HoCharacterLayerMaskParameter LayerMask = new HoCharacterLayerMaskParameter(new LayerMask { value = -1 });

        [InspectorName("最小渲染队列"), Tooltip("角色捕获包含的最低 render queue。")]
        public IntParameter MinRenderQueue = new IntParameter(0);

        [InspectorName("最大渲染队列"), Tooltip("角色捕获包含的最高 render queue。")]
        public IntParameter MaxRenderQueue = new IntParameter((int)RenderQueue.Overlay - 1);

        [InspectorName("渲染时机"), Tooltip("角色特化捕获和合成的执行时机。通常保持透明物之后。")]
        public HoCharacterRenderPassEventParameter PassEvent = new HoCharacterRenderPassEventParameter(RenderPassEvent.AfterRenderingTransparents);

        [InspectorName("渲染缩放"), Tooltip("捕获 RT 的分辨率。降低分辨率会省带宽，但会影响边缘质量。")]
        public HoCharacterRenderScaleParameter RenderScale = new HoCharacterRenderScaleParameter(HoCharacterRenderScale.Full);

        [InspectorName("启用眼睛透过"), Tooltip("让被前发遮挡的眼睛按眼睛捕获结果透出。")]
        public BoolParameter EyeRevealEnabled = new BoolParameter(true);

        [InspectorName("透过强度"), Tooltip("眼睛透过的总强度。材质侧 Character Capture Opacity 可调单个眼睛材质。")]
        public ClampedFloatParameter EyeRevealStrength = new ClampedFloatParameter(0.05f, 0.0f, 1.0f);

        [InspectorName("羽化像素"), Tooltip("眼睛透过遮罩的边缘羽化。")]
        public FloatParameter EyeRevealFeatherPixels = new FloatParameter(1.0f);

        [InspectorName("扩张像素"), Tooltip("眼睛透过遮罩向外扩张的像素数。")]
        public FloatParameter EyeRevealDilationPixels = new FloatParameter(2.0f);

        [InspectorName("深度偏移"), Tooltip("判断前发是否在眼睛前方时使用的深度偏移。")]
        public FloatParameter EyeRevealDepthBias = new FloatParameter(0.01f);

        [InspectorName("使用眼透区域"), Tooltip("启用后，眼睛透过还会受 ObjectCustom4 / 眼透区域限制。")]
        public BoolParameter UseEyeRevealArea = new BoolParameter(true);

        [InspectorName("仅同角色"), Tooltip("启用后，只允许同 Character ID 的前发影响同角色的眼睛/脸。")]
        public BoolParameter SameCharacterOnly = new BoolParameter(true);

        [InspectorName("启用相机角度修正"), Tooltip("开启后，眼睛透过会按相机与角色面部朝向的夹角衰减。角色面部朝向由 HoMetadataBufferGroup 上的“面部朝向”提供（Transform，骨骼或空物体均可）。")]
        public BoolParameter EyeRevealAngleEnabled = new BoolParameter(false);

        [InspectorName("角度修正强度"), Tooltip("相机偏离正脸时眼睛透过衰减的总强度。1 表示超出角度范围完全关闭眼睛透过。")]
        public ClampedFloatParameter EyeRevealAngleStrength = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [InspectorName("平转半角范围"), Tooltip("相机绕角色竖直轴的水平转动半角范围，单位为度。")]
        public ClampedFloatParameter EyeRevealAngleYawRangeDegrees = new ClampedFloatParameter(90.0f, 0.0f, 360.0f);

        [InspectorName("俯仰半角范围"), Tooltip("相机相对角色脸的俯仰半角范围，单位为度。")]
        public ClampedFloatParameter EyeRevealAnglePitchRangeDegrees = new ClampedFloatParameter(60.0f, 0.0f, 360.0f);

        [InspectorName("角度柔化"), Tooltip("角度衰减边缘的柔化范围，单位为度。")]
        public ClampedFloatParameter EyeRevealAngleSoftnessDegrees = new ClampedFloatParameter(40.0f, 0.0f, 180.0f);

        [InspectorName("启用前发投影"), Tooltip("用 FrontHair 标记向 Face 标记投射屏幕空间阴影。")]
        public BoolParameter HairDropShadowEnabled = new BoolParameter(true);

        [InspectorName("投影颜色"), Tooltip("前发投影颜色。")]
        public ColorParameter HairShadowColor = new ColorParameter(new Color(0.78f, 0.68f, 0.72f, 1.0f));

        [InspectorName("投影不透明度"), Tooltip("前发投影强度。")]
        public ClampedFloatParameter HairShadowOpacity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [InspectorName("投影距离像素"), Tooltip("投影沿角度方向偏移的屏幕像素距离。")]
        public FloatParameter HairShadowDistancePixels = new FloatParameter(15.0f);

        [InspectorName("投影距离透视衰减"), Tooltip("按 GeometryBuffer 线性深度压缩远处的投影偏移。0 为关闭，1 为完全按深度衰减。")]
        public ClampedFloatParameter HairShadowDistancePerspectiveStrength = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [InspectorName("投影距离参考深度"), Tooltip("线性深度小于或等于此值时保持原始像素距离；更远处会按参考深度 / 当前深度缩短。")]
        public FloatParameter HairShadowDistanceReferenceDepth = new FloatParameter(2.0f);

        [InspectorName("投影距离最小倍率"), Tooltip("远处投影距离衰减的下限。")]
        public ClampedFloatParameter HairShadowDistanceMinScale = new ClampedFloatParameter(0.25f, 0.0f, 1.0f);

        [InspectorName("投影角度"), Tooltip("投影方向，单位为角度。")]
        public FloatParameter HairShadowAngleDegrees = new FloatParameter(240.0f);

        [InspectorName("柔化像素"), Tooltip("前发投影边缘柔化范围。")]
        public FloatParameter HairShadowSoftnessPixels = new FloatParameter(2.0f);

        [InspectorName("扩散像素"), Tooltip("前发投影遮罩扩张范围。")]
        public FloatParameter HairShadowSpreadPixels = new FloatParameter(0.0f);

        [InspectorName("避开前发"), Tooltip("避免投影重新盖回前发自身的强度。")]
        public ClampedFloatParameter HairShadowKeepOffHair = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [InspectorName("混合模式"), Tooltip("前发投影与画面的混合方式。")]
        public HoCharacterShadowBlendModeParameter HairShadowBlendMode = new HoCharacterShadowBlendModeParameter(HoCharacterShadowBlendMode.Multiply);

        [InspectorName("启用脸色扩散"), Tooltip("把 Face 的 SurfaceColor 大范围模糊后叠到 FrontHair 上。")]
        public BoolParameter FaceHairDiffuseEnabled = new BoolParameter(false);

        [InspectorName("扩散强度"), Tooltip("脸色扩散叠到前发上的总强度。")]
        public ClampedFloatParameter FaceHairDiffuseStrength = new ClampedFloatParameter(0.35f, 0.0f, 1.0f);

        [InspectorName("模糊半径像素"), Tooltip("Face 颜色扩散的屏幕空间模糊半径。")]
        public FloatParameter FaceHairDiffuseRadiusPixels = new FloatParameter(48.0f);

        [InspectorName("深度容差"), Tooltip("当前 FrontHair 与模糊 Face 深度之间允许的线性深度差。")]
        public FloatParameter FaceHairDiffuseDepthTolerance = new FloatParameter(0.25f);

        [InspectorName("色阶黑场"), Tooltip("模糊遮罩低于该值时压到 0。")]
        public ClampedFloatParameter FaceHairDiffuseLevelBlack = new ClampedFloatParameter(0.02f, 0.0f, 1.0f);

        [InspectorName("色阶白场"), Tooltip("模糊遮罩高于该值时推到 1。")]
        public ClampedFloatParameter FaceHairDiffuseLevelWhite = new ClampedFloatParameter(0.45f, 0.0f, 1.0f);

        [InspectorName("颜色乘"), Tooltip("叠到前发前乘到 Face SurfaceColor 上的颜色。Alpha 也会乘到最终强度。")]
        public ColorParameter FaceHairDiffuseTintColor = new ColorParameter(new Color(1.0f, 0.78f, 0.72f, 1.0f));

        [InspectorName("混合模式"), Tooltip("脸色扩散与当前前发颜色的混合方式。")]
        public HoCharacterFaceHairDiffuseBlendModeParameter FaceHairDiffuseBlendMode = new HoCharacterFaceHairDiffuseBlendModeParameter(HoCharacterFaceHairDiffuseBlendMode.Additive);

        [InspectorName("启用主体轮廓"), Tooltip("读取 ObjectCustom0.r / CharacterFull 的遮罩，生成高精度外扩轮廓。")]
        public BoolParameter SubjectOutlineEnabled = new BoolParameter(false);

        [InspectorName("轮廓强度"), Tooltip("主体轮廓叠到画面上的总强度。")]
        public ClampedFloatParameter SubjectOutlineStrength = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [InspectorName("外扩半径像素"), Tooltip("主体轮廓向外扩张和圆润化的屏幕空间半径。")]
        public FloatParameter SubjectOutlineRadiusPixels = new FloatParameter(6.0f);

        [InspectorName("边缘黑场"), Tooltip("模糊遮罩低于该值时压到 0。")]
        public ClampedFloatParameter SubjectOutlineLevelBlack = new ClampedFloatParameter(0.02f, 0.0f, 1.0f);

        [InspectorName("边缘白场"), Tooltip("模糊遮罩高于该值时推到 1。")]
        public ClampedFloatParameter SubjectOutlineLevelWhite = new ClampedFloatParameter(0.35f, 0.0f, 1.0f);

        [InspectorName("轮廓颜色"), Tooltip("主体外扩轮廓颜色。Alpha 也会乘到最终强度。")]
        public ColorParameter SubjectOutlineColor = new ColorParameter(Color.white);

        [InspectorName("风格模式"), Tooltip("纯色描边使用轮廓颜色；彩色流光描边会把边缘梯度方向映射为 HSV 色相；柔化雾气会把原图底色乘雾气颜色后做 HSV 变换并加色叠加。")]
        public HoCharacterSubjectOutlineFillModeParameter SubjectOutlineFillMode = new HoCharacterSubjectOutlineFillModeParameter(HoCharacterSubjectOutlineFillMode.SolidColor);

        [InspectorName("法线旋转"), Tooltip("彩色流光描边模式下，对整圈外扩方向做统一旋转。")]
        public FloatParameter SubjectOutlineNormalRotationDegrees = new FloatParameter(0.0f);

        [InspectorName("法线流动速度"), Tooltip("彩色流光描边模式下，方向随时间旋转的速度，单位为度/秒。")]
        public FloatParameter SubjectOutlineNormalFlowDegreesPerSecond = new FloatParameter(0.0f);

        [InspectorName("雾气颜色"), Tooltip("柔化雾气模式下，先乘到原图底色上的颜色。Alpha 也会乘到最终雾气强度。")]
        public ColorParameter SubjectOutlineFogColor = new ColorParameter(new Color(1.0f, 0.85f, 0.65f, 1.0f));

        [InspectorName("雾气色相偏移"), Tooltip("柔化雾气模式下，对乘色后的 HSV 色相做偏移，单位为度。")]
        public FloatParameter SubjectOutlineFogHueShiftDegrees = new FloatParameter(0.0f);

        [InspectorName("雾气饱和度"), Tooltip("柔化雾气模式下，对乘色后的 HSV 饱和度做倍率调整。")]
        public ClampedFloatParameter SubjectOutlineFogSaturation = new ClampedFloatParameter(1.0f, 0.0f, 4.0f);

        [InspectorName("雾气亮度"), Tooltip("柔化雾气模式下，对乘色后的 HSV 明度做倍率调整。")]
        public ClampedFloatParameter SubjectOutlineFogValue = new ClampedFloatParameter(1.0f, 0.0f, 4.0f);

        [InspectorName("雾气柔化"), Tooltip("柔化雾气模式下，控制外扩 SDF 边缘的软硬。低值更雾化，高值更锐。")]
        public ClampedFloatParameter SubjectOutlineFogSoftness = new ClampedFloatParameter(0.55f, 0.05f, 4.0f);

        [InspectorName("高度渐隐"), Tooltip("按主体轮廓来源点的世界高度减弱轮廓。")]
        public HoCharacterSubjectOutlineHeightFadeModeParameter SubjectOutlineHeightFadeMode = new HoCharacterSubjectOutlineHeightFadeModeParameter(HoCharacterSubjectOutlineHeightFadeMode.Off);

        [InspectorName("地面高度"), Tooltip("高度渐隐的地面世界 Y。")]
        public FloatParameter SubjectOutlineHeightFadeGroundY = new FloatParameter(0.0f);

        [InspectorName("渐隐开始距离"), Tooltip("距离地面小于等于该值时进入渐隐区间。")]
        public FloatParameter SubjectOutlineHeightFadeStart = new FloatParameter(0.0f);

        [InspectorName("渐隐结束距离"), Tooltip("距离地面大于等于该值时结束渐隐区间。")]
        public FloatParameter SubjectOutlineHeightFadeEnd = new FloatParameter(1.0f);

        [InspectorName("渐隐硬度"), Tooltip("高度渐隐过渡曲线的硬度。1 为标准平滑过渡，数值越大越硬，越小越柔。")]
        public ClampedFloatParameter SubjectOutlineHeightFadeHardness = new ClampedFloatParameter(1.0f, 0.1f, 8.0f);

        [InspectorName("启用增强轮廓"), Tooltip("从指定 ObjectCustom 分量生成柔化外扩雾气。")]
        public BoolParameter EnhancedOutlineEnabled = new BoolParameter(false);

        [InspectorName("来源通道"), Tooltip("增强轮廓读取的 RSUV / ObjectCustom 分量。默认使用 CharacterBody / ObjectCustom6。")]
        public HoCharacterObjectCustomChannelParameter EnhancedOutlineSourceChannel = new HoCharacterObjectCustomChannelParameter(HoCharacterObjectCustomChannel.CharacterBody);

        [InspectorName("雾气强度"), Tooltip("增强轮廓雾气叠到画面上的总强度。")]
        public ClampedFloatParameter EnhancedOutlineStrength = new ClampedFloatParameter(0.65f, 0.0f, 1.0f);

        [InspectorName("外扩半径像素"), Tooltip("增强轮廓雾气向外扩散的屏幕空间半径。")]
        public FloatParameter EnhancedOutlineRadiusPixels = new FloatParameter(18.0f);

        [InspectorName("雾气颜色"), Tooltip("先乘到原图底色上的颜色。Alpha 也会乘到最终雾气强度。")]
        public ColorParameter EnhancedOutlineFogColor = new ColorParameter(new Color(1.0f, 0.76f, 0.55f, 1.0f));

        [InspectorName("雾气色相偏移"), Tooltip("对乘色后的 HSV 色相做偏移，单位为度。")]
        public FloatParameter EnhancedOutlineFogHueShiftDegrees = new FloatParameter(0.0f);

        [InspectorName("雾气饱和度"), Tooltip("对乘色后的 HSV 饱和度做倍率调整。")]
        public ClampedFloatParameter EnhancedOutlineFogSaturation = new ClampedFloatParameter(1.0f, 0.0f, 4.0f);

        [InspectorName("雾气亮度"), Tooltip("对乘色后的 HSV 明度做倍率调整。")]
        public ClampedFloatParameter EnhancedOutlineFogValue = new ClampedFloatParameter(1.0f, 0.0f, 4.0f);

        [InspectorName("雾气柔化"), Tooltip("控制外扩 SDF 边缘的软硬。低值更雾化，高值更锐。")]
        public ClampedFloatParameter EnhancedOutlineFogSoftness = new ClampedFloatParameter(0.45f, 0.05f, 4.0f);

        [InspectorName("高度渐隐"), Tooltip("按增强轮廓来源点的世界高度减弱雾气。")]
        public HoCharacterSubjectOutlineHeightFadeModeParameter EnhancedOutlineHeightFadeMode = new HoCharacterSubjectOutlineHeightFadeModeParameter(HoCharacterSubjectOutlineHeightFadeMode.Off);

        [InspectorName("地面高度"), Tooltip("高度渐隐的地面世界 Y。")]
        public FloatParameter EnhancedOutlineHeightFadeGroundY = new FloatParameter(0.0f);

        [InspectorName("渐隐开始距离"), Tooltip("距离地面小于等于该值时进入渐隐区间。")]
        public FloatParameter EnhancedOutlineHeightFadeStart = new FloatParameter(0.0f);

        [InspectorName("渐隐结束距离"), Tooltip("距离地面大于等于该值时结束渐隐区间。")]
        public FloatParameter EnhancedOutlineHeightFadeEnd = new FloatParameter(1.0f);

        [InspectorName("渐隐硬度"), Tooltip("高度渐隐过渡曲线的硬度。1 为标准平滑过渡，数值越大越硬，越小越柔。")]
        public ClampedFloatParameter EnhancedOutlineHeightFadeHardness = new ClampedFloatParameter(1.0f, 0.1f, 8.0f);

        public bool IsActive()
        {
            return active;
        }

        public bool IsActiveForCamera(CameraType cameraType)
        {
            if (!IsActive())
            {
                return false;
            }

            if (cameraType == CameraType.SceneView)
            {
                return true;
            }

            return cameraType == CameraType.Game;
        }

        public bool IsTileCompatible()
        {
            return false;
        }

        public void ApplyTo(HoCharacterSpecializationSettings target)
        {
            if (target == null)
            {
                return;
            }

            target.eyeRevealEnabled = EyeRevealEnabled.value;
            target.eyeRevealStrength = EyeRevealStrength.value;
            target.eyeRevealFeatherPixels = EyeRevealFeatherPixels.value;
            target.eyeRevealDilationPixels = EyeRevealDilationPixels.value;
            target.eyeRevealDepthBias = EyeRevealDepthBias.value;
            target.useEyeRevealArea = UseEyeRevealArea.value;
            target.sameCharacterOnly = SameCharacterOnly.value;
            target.eyeRevealAngleEnabled = EyeRevealAngleEnabled.value;
            target.eyeRevealAngleStrength = EyeRevealAngleStrength.value;
            target.eyeRevealAngleYawRangeDegrees = EyeRevealAngleYawRangeDegrees.value;
            target.eyeRevealAnglePitchRangeDegrees = EyeRevealAnglePitchRangeDegrees.value;
            target.eyeRevealAngleSoftnessDegrees = EyeRevealAngleSoftnessDegrees.value;
            target.hairDropShadowEnabled = HairDropShadowEnabled.value;
            target.hairShadowColor = HairShadowColor.value;
            target.hairShadowOpacity = HairShadowOpacity.value;
            target.hairShadowDistancePixels = HairShadowDistancePixels.value;
            target.hairShadowDistancePerspectiveStrength = HairShadowDistancePerspectiveStrength.value;
            target.hairShadowDistanceReferenceDepth = HairShadowDistanceReferenceDepth.value;
            target.hairShadowDistanceMinScale = HairShadowDistanceMinScale.value;
            target.hairShadowAngleDegrees = HairShadowAngleDegrees.value;
            target.hairShadowSoftnessPixels = HairShadowSoftnessPixels.value;
            target.hairShadowSpreadPixels = HairShadowSpreadPixels.value;
            target.hairShadowKeepOffHair = HairShadowKeepOffHair.value;
            target.hairShadowBlendMode = HairShadowBlendMode.value;
            target.faceHairDiffuseEnabled = FaceHairDiffuseEnabled.value;
            target.faceHairDiffuseStrength = FaceHairDiffuseStrength.value;
            target.faceHairDiffuseRadiusPixels = FaceHairDiffuseRadiusPixels.value;
            target.faceHairDiffuseDepthTolerance = FaceHairDiffuseDepthTolerance.value;
            target.faceHairDiffuseLevelBlack = FaceHairDiffuseLevelBlack.value;
            target.faceHairDiffuseLevelWhite = FaceHairDiffuseLevelWhite.value;
            target.faceHairDiffuseTintColor = FaceHairDiffuseTintColor.value;
            target.faceHairDiffuseBlendMode = FaceHairDiffuseBlendMode.value;
            target.subjectOutlineEnabled = SubjectOutlineEnabled.value;
            target.subjectOutlineStrength = SubjectOutlineStrength.value;
            target.subjectOutlineRadiusPixels = SubjectOutlineRadiusPixels.value;
            target.subjectOutlineLevelBlack = SubjectOutlineLevelBlack.value;
            target.subjectOutlineLevelWhite = SubjectOutlineLevelWhite.value;
            target.subjectOutlineColor = SubjectOutlineColor.value;
            target.subjectOutlineFillMode = SubjectOutlineFillMode.value;
            target.subjectOutlineNormalRotationDegrees = SubjectOutlineNormalRotationDegrees.value;
            target.subjectOutlineNormalFlowDegreesPerSecond = SubjectOutlineNormalFlowDegreesPerSecond.value;
            target.subjectOutlineFogColor = SubjectOutlineFogColor.value;
            target.subjectOutlineFogHueShiftDegrees = SubjectOutlineFogHueShiftDegrees.value;
            target.subjectOutlineFogSaturation = SubjectOutlineFogSaturation.value;
            target.subjectOutlineFogValue = SubjectOutlineFogValue.value;
            target.subjectOutlineFogSoftness = SubjectOutlineFogSoftness.value;
            target.subjectOutlineHeightFadeMode = SubjectOutlineHeightFadeMode.value;
            target.subjectOutlineHeightFadeGroundY = SubjectOutlineHeightFadeGroundY.value;
            target.subjectOutlineHeightFadeStart = SubjectOutlineHeightFadeStart.value;
            target.subjectOutlineHeightFadeEnd = SubjectOutlineHeightFadeEnd.value;
            target.subjectOutlineHeightFadeHardness = SubjectOutlineHeightFadeHardness.value;
            target.enhancedOutlineEnabled = EnhancedOutlineEnabled.value;
            target.enhancedOutlineSourceChannel = EnhancedOutlineSourceChannel.value;
            target.enhancedOutlineStrength = EnhancedOutlineStrength.value;
            target.enhancedOutlineRadiusPixels = EnhancedOutlineRadiusPixels.value;
            target.enhancedOutlineFogColor = EnhancedOutlineFogColor.value;
            target.enhancedOutlineFogHueShiftDegrees = EnhancedOutlineFogHueShiftDegrees.value;
            target.enhancedOutlineFogSaturation = EnhancedOutlineFogSaturation.value;
            target.enhancedOutlineFogValue = EnhancedOutlineFogValue.value;
            target.enhancedOutlineFogSoftness = EnhancedOutlineFogSoftness.value;
            target.enhancedOutlineHeightFadeMode = EnhancedOutlineHeightFadeMode.value;
            target.enhancedOutlineHeightFadeGroundY = EnhancedOutlineHeightFadeGroundY.value;
            target.enhancedOutlineHeightFadeStart = EnhancedOutlineHeightFadeStart.value;
            target.enhancedOutlineHeightFadeEnd = EnhancedOutlineHeightFadeEnd.value;
            target.enhancedOutlineHeightFadeHardness = EnhancedOutlineHeightFadeHardness.value;
        }
    }
}

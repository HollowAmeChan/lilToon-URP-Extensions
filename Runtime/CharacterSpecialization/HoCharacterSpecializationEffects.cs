using System;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    /// <summary>
    /// 角色特化的效果参数（普通字段，没有逐参数 override）。
    /// </summary>
    /// <remarks>
    /// 这些参数由 <c>HoCharacterSpecializationVolume.Effects</c> 单一提供：Volume 是唯一来源，
    /// 不再有 Settings 资产的"保底值 + Volume 逐参数覆盖"（那套里 ApplyTo 无条件覆盖，
    /// overrideState 其实是摆设）。运行时把它们复制进 <c>HoCharacterSpecializationSettings</c> 的载体字段。
    /// </remarks>
    [Serializable]
    public sealed class HoCharacterSpecializationEffects
    {
        [InspectorName("抗锯齿宽度"), Tooltip("掩码抗锯齿的模糊半径，单位为 MetadataBuffer 的 texel。低于 1 像素没有抗锯齿收益（单采样 0/1 场必须摊到至少一个 texel），内部按 1 像素下限处理；越宽边缘越软、台阶越小。每个效果在自己分区里用「读取抗锯齿掩码」勾选是否读取，任一勾选就会产出这份副本，全不勾选则不跑。")]
        public float semanticMaskBlurRadiusPixels = 1.0f;

        [InspectorName("读取抗锯齿掩码"), Tooltip("前发投影读取掩码抗锯齿版：接收面的裁剪边（发际线）与眼透区域，以及半影的取样源。不勾选则读原始 bit。")]
        public bool semanticMaskBlurHairShadow = true;

        [InspectorName("读取抗锯齿掩码"), Tooltip("脸色扩散读取掩码抗锯齿版：前发接收区域的边界。不勾选则读原始 bit。")]
        public bool semanticMaskBlurFaceHairDiffuse = true;

        [InspectorName("读取抗锯齿掩码"), Tooltip("眼睛透过读取掩码抗锯齿版：遮挡前发与眼透区域的边界。不勾选则读原始 bit。")]
        public bool semanticMaskBlurEyeReveal = true;

        [InspectorName("读取抗锯齿掩码"), Tooltip("主体轮廓的语义源读取掩码抗锯齿版。默认不读（读原始 bit）。")]
        public bool semanticMaskBlurSubjectOutline = false;

        [InspectorName("读取抗锯齿掩码"), Tooltip("增强轮廓的语义源读取掩码抗锯齿版。默认不读（读原始 bit）。")]
        public bool semanticMaskBlurEnhancedOutline = false;

        [InspectorName("启用眼睛透过"), Tooltip("让被前发遮挡的眼睛按眼睛捕获结果透出。")]
        public bool eyeRevealEnabled = true;

        [InspectorName("透过强度"), Tooltip("眼睛透过的总强度。材质侧 Character Capture Opacity 可调单个眼睛材质。")]
        [Range(0.0f, 1.0f)]
        public float eyeRevealStrength = 0.05f;

        [InspectorName("羽化像素"), Tooltip("眼睛透过遮罩的边缘羽化。")]
        public float eyeRevealFeatherPixels = 1.0f;

        [InspectorName("扩张像素"), Tooltip("眼睛透过遮罩向外扩张的像素数。")]
        public float eyeRevealDilationPixels = 2.0f;

        [InspectorName("深度偏移"), Tooltip("判断前发是否在眼睛前方时使用的深度偏移。")]
        public float eyeRevealDepthBias = 0.01f;

        [InspectorName("使用眼透区域"), Tooltip("启用后，眼睛透过还会受 ObjectCustom4 / 眼透区域限制。")]
        public bool useEyeRevealArea = true;

        [InspectorName("仅同角色"), Tooltip("启用后，只允许同 Character ID 的前发影响同角色的眼睛/脸。")]
        public bool sameCharacterOnly = true;

        [InspectorName("启用相机角度修正"), Tooltip("开启后，眼睛透过会按相机与角色面部朝向的夹角衰减。角色面部朝向由 HoMetadataBufferGroup 上的“面部朝向”提供（Transform，骨骼或空物体均可）。")]
        public bool eyeRevealAngleEnabled = false;

        [InspectorName("角度修正强度"), Tooltip("相机偏离正脸时眼睛透过衰减的总强度。1 表示超出角度范围完全关闭眼睛透过。")]
        [Range(0.0f, 1.0f)]
        public float eyeRevealAngleStrength = 1.0f;

        [InspectorName("平转半角范围"), Tooltip("相机绕角色竖直轴的水平转动半角范围，单位为度。")]
        [Range(0.0f, 360.0f)]
        public float eyeRevealAngleYawRangeDegrees = 90.0f;

        [InspectorName("俯仰半角范围"), Tooltip("相机相对角色脸的俯仰半角范围，单位为度。")]
        [Range(0.0f, 360.0f)]
        public float eyeRevealAnglePitchRangeDegrees = 60.0f;

        [InspectorName("角度柔化"), Tooltip("角度衰减边缘的柔化范围，单位为度。")]
        [Range(0.0f, 180.0f)]
        public float eyeRevealAngleSoftnessDegrees = 40.0f;

        [InspectorName("启用前发投影"), Tooltip("用 FrontHair 标记向 Face 标记投射屏幕空间阴影。")]
        public bool hairDropShadowEnabled = true;

        [InspectorName("投影颜色"), Tooltip("前发投影颜色。")]
        public Color hairShadowColor = new Color(0.78f, 0.68f, 0.72f, 1.0f);

        [InspectorName("投影不透明度"), Tooltip("前发投影强度。")]
        [Range(0.0f, 1.0f)]
        public float hairShadowOpacity = 1.0f;

        [InspectorName("投影距离像素"), Tooltip("投影沿角度方向偏移的屏幕像素距离。")]
        public float hairShadowDistancePixels = 15.0f;

        [InspectorName("投影距离透视衰减"), Tooltip("按 GeometryBuffer 线性深度压缩远处的投影偏移。0 为关闭，1 为完全按深度衰减。")]
        [Range(0.0f, 1.0f)]
        public float hairShadowDistancePerspectiveStrength = 1.0f;

        [InspectorName("投影距离参考深度"), Tooltip("线性深度小于或等于此值时保持原始像素距离；更远处会按参考深度 / 当前深度缩短。")]
        public float hairShadowDistanceReferenceDepth = 2.0f;

        [InspectorName("投影距离最小倍率"), Tooltip("远处投影距离衰减的下限。")]
        [Range(0.0f, 1.0f)]
        public float hairShadowDistanceMinScale = 0.25f;

        [InspectorName("投影角度"), Tooltip("投影方向，单位为角度。")]
        public float hairShadowAngleDegrees = 240.0f;

        [InspectorName("柔化像素"), Tooltip("前发投影边缘柔化范围。掩码读入时始终保留 1 像素的抗锯齿模糊，这里是在此之上的额外柔化。")]
        public float hairShadowSoftnessPixels = 2.0f;

        [InspectorName("扩散像素"), Tooltip("前发投影遮罩扩张范围。")]
        public float hairShadowSpreadPixels = 0.0f;

        [InspectorName("混合模式"), Tooltip("前发投影与画面的混合方式。")]
        public HoCharacterShadowBlendMode hairShadowBlendMode = HoCharacterShadowBlendMode.Multiply;

        [InspectorName("启用脸色扩散"), Tooltip("把前发后面的受光脸（强制脸捕获的颜色）大范围模糊后乘上「颜色乘」，叠到 FrontHair 上。")]
        public bool faceHairDiffuseEnabled = false;

        [InspectorName("扩散强度"), Tooltip("脸色扩散叠到前发上的总强度。")]
        [Range(0.0f, 1.0f)]
        public float faceHairDiffuseStrength = 0.35f;

        [InspectorName("模糊半径像素"), Tooltip("Face 颜色扩散的屏幕空间模糊半径。")]
        public float faceHairDiffuseRadiusPixels = 48.0f;

        [InspectorName("深度容差"), Tooltip("当前 FrontHair 与模糊 Face 深度之间允许的线性深度差。")]
        public float faceHairDiffuseDepthTolerance = 0.25f;

        [InspectorName("色阶黑场"), Tooltip("模糊遮罩低于该值时压到 0。")]
        [Range(0.0f, 1.0f)]
        public float faceHairDiffuseLevelBlack = 0.02f;

        [InspectorName("色阶白场"), Tooltip("模糊遮罩高于该值时推到 1。")]
        [Range(0.0f, 1.0f)]
        public float faceHairDiffuseLevelWhite = 0.45f;

        [InspectorName("颜色乘"), Tooltip("乘到模糊后的受光脸上的颜色（在模糊之后相乘，不是替换成这个颜色）。Alpha 也会乘到最终强度。")]
        public Color faceHairDiffuseTintColor = new Color(1.0f, 0.78f, 0.72f, 1.0f);

        [InspectorName("混合模式"), Tooltip("脸色扩散与当前前发颜色的混合方式。")]
        public HoCharacterFaceHairDiffuseBlendMode faceHairDiffuseBlendMode = HoCharacterFaceHairDiffuseBlendMode.Additive;

        [InspectorName("启用主体轮廓"), Tooltip("读取 ObjectCustom0.r / CharacterFull 的遮罩，生成高精度外扩轮廓。")]
        public bool subjectOutlineEnabled = false;

        [InspectorName("轮廓强度"), Tooltip("主体轮廓叠到画面上的总强度。")]
        [Range(0.0f, 1.0f)]
        public float subjectOutlineStrength = 1.0f;

        [InspectorName("外扩半径像素"), Tooltip("主体轮廓向外扩张和圆润化的屏幕空间半径。")]
        public float subjectOutlineRadiusPixels = 6.0f;

        [InspectorName("边缘黑场"), Tooltip("模糊遮罩低于该值时压到 0。")]
        [Range(0.0f, 1.0f)]
        public float subjectOutlineLevelBlack = 0.02f;

        [InspectorName("边缘白场"), Tooltip("模糊遮罩高于该值时推到 1。")]
        [Range(0.0f, 1.0f)]
        public float subjectOutlineLevelWhite = 0.35f;

        [InspectorName("轮廓颜色"), Tooltip("主体外扩轮廓颜色。Alpha 也会乘到最终强度。")]
        public Color subjectOutlineColor = Color.white;

        [InspectorName("风格模式"), Tooltip("纯色描边使用轮廓颜色；彩色流光描边会把边缘梯度方向映射为 HSV 色相；柔化雾气会把原图底色乘雾气颜色后做 HSV 变换并加色叠加。")]
        public HoCharacterSubjectOutlineFillMode subjectOutlineFillMode = HoCharacterSubjectOutlineFillMode.SolidColor;

        [InspectorName("法线旋转"), Tooltip("彩色流光描边模式下，对整圈外扩方向做统一旋转。")]
        public float subjectOutlineNormalRotationDegrees = 0.0f;

        [InspectorName("法线流动速度"), Tooltip("彩色流光描边模式下，方向随时间旋转的速度，单位为度/秒。")]
        public float subjectOutlineNormalFlowDegreesPerSecond = 0.0f;

        [InspectorName("雾气颜色"), Tooltip("柔化雾气模式下，先乘到原图底色上的颜色。Alpha 也会乘到最终雾气强度。")]
        public Color subjectOutlineFogColor = new Color(1.0f, 0.85f, 0.65f, 1.0f);

        [InspectorName("雾气色相偏移"), Tooltip("柔化雾气模式下，对乘色后的 HSV 色相做偏移，单位为度。")]
        public float subjectOutlineFogHueShiftDegrees = 0.0f;

        [InspectorName("雾气饱和度"), Tooltip("柔化雾气模式下，对乘色后的 HSV 饱和度做倍率调整。")]
        [Range(0.0f, 4.0f)]
        public float subjectOutlineFogSaturation = 1.0f;

        [InspectorName("雾气亮度"), Tooltip("柔化雾气模式下，对乘色后的 HSV 明度做倍率调整。")]
        [Range(0.0f, 4.0f)]
        public float subjectOutlineFogValue = 1.0f;

        [InspectorName("雾气柔化"), Tooltip("柔化雾气模式下，控制外扩 SDF 边缘的软硬。低值更雾化，高值更锐。")]
        [Range(0.05f, 4.0f)]
        public float subjectOutlineFogSoftness = 0.55f;

        [InspectorName("高度渐隐"), Tooltip("按主体轮廓来源点的世界高度减弱轮廓。")]
        public HoCharacterSubjectOutlineHeightFadeMode subjectOutlineHeightFadeMode = HoCharacterSubjectOutlineHeightFadeMode.Off;

        [InspectorName("地面高度"), Tooltip("高度渐隐的地面世界 Y。")]
        public float subjectOutlineHeightFadeGroundY = 0.0f;

        [InspectorName("渐隐开始距离"), Tooltip("距离地面小于等于该值时进入渐隐区间。")]
        public float subjectOutlineHeightFadeStart = 0.0f;

        [InspectorName("渐隐结束距离"), Tooltip("距离地面大于等于该值时结束渐隐区间。")]
        public float subjectOutlineHeightFadeEnd = 1.0f;

        [InspectorName("渐隐硬度"), Tooltip("高度渐隐过渡曲线的硬度。1 为标准平滑过渡，数值越大越硬，越小越柔。")]
        [Range(0.1f, 8.0f)]
        public float subjectOutlineHeightFadeHardness = 1.0f;

        [InspectorName("启用增强轮廓"), Tooltip("从指定 ObjectCustom 分量生成柔化外扩雾气。")]
        public bool enhancedOutlineEnabled = false;

        [InspectorName("来源通道"), Tooltip("增强轮廓读取的 RSUV / ObjectCustom 分量。默认使用 CharacterBody / ObjectCustom6。")]
        public HoCharacterObjectCustomChannel enhancedOutlineSourceChannel = HoCharacterObjectCustomChannel.CharacterBody;

        [InspectorName("雾气强度"), Tooltip("增强轮廓雾气叠到画面上的总强度。")]
        [Range(0.0f, 1.0f)]
        public float enhancedOutlineStrength = 0.65f;

        [InspectorName("外扩半径像素"), Tooltip("增强轮廓雾气向外扩散的屏幕空间半径。")]
        public float enhancedOutlineRadiusPixels = 18.0f;

        [InspectorName("雾气颜色"), Tooltip("先乘到原图底色上的颜色。Alpha 也会乘到最终雾气强度。")]
        public Color enhancedOutlineFogColor = new Color(1.0f, 0.76f, 0.55f, 1.0f);

        [InspectorName("雾气色相偏移"), Tooltip("对乘色后的 HSV 色相做偏移，单位为度。")]
        public float enhancedOutlineFogHueShiftDegrees = 0.0f;

        [InspectorName("雾气饱和度"), Tooltip("对乘色后的 HSV 饱和度做倍率调整。")]
        [Range(0.0f, 4.0f)]
        public float enhancedOutlineFogSaturation = 1.0f;

        [InspectorName("雾气亮度"), Tooltip("对乘色后的 HSV 明度做倍率调整。")]
        [Range(0.0f, 4.0f)]
        public float enhancedOutlineFogValue = 1.0f;

        [InspectorName("雾气柔化"), Tooltip("控制外扩 SDF 边缘的软硬。低值更雾化，高值更锐。")]
        [Range(0.05f, 4.0f)]
        public float enhancedOutlineFogSoftness = 0.45f;

        [InspectorName("高度渐隐"), Tooltip("按增强轮廓来源点的世界高度减弱雾气。")]
        public HoCharacterSubjectOutlineHeightFadeMode enhancedOutlineHeightFadeMode = HoCharacterSubjectOutlineHeightFadeMode.Off;

        [InspectorName("地面高度"), Tooltip("高度渐隐的地面世界 Y。")]
        public float enhancedOutlineHeightFadeGroundY = 0.0f;

        [InspectorName("渐隐开始距离"), Tooltip("距离地面小于等于该值时进入渐隐区间。")]
        public float enhancedOutlineHeightFadeStart = 0.0f;

        [InspectorName("渐隐结束距离"), Tooltip("距离地面大于等于该值时结束渐隐区间。")]
        public float enhancedOutlineHeightFadeEnd = 1.0f;

        [InspectorName("渐隐硬度"), Tooltip("高度渐隐过渡曲线的硬度。1 为标准平滑过渡，数值越大越硬，越小越柔。")]
        [Range(0.1f, 8.0f)]
        public float enhancedOutlineHeightFadeHardness = 1.0f;
    }
}

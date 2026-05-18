using System;
using lilToon.URP.Extensions.AOV;
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
    public sealed class HoCharacterRenderScaleParameter : VolumeParameter<HoAovRenderScale>
    {
        public HoCharacterRenderScaleParameter(HoAovRenderScale value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoAovRenderScale from, HoAovRenderScale to, float t)
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
    public sealed class HoCharacterDebugModeParameter : VolumeParameter<HoCharacterSpecializationDebugMode>
    {
        public HoCharacterDebugModeParameter(HoCharacterSpecializationDebugMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterSpecializationDebugMode from, HoCharacterSpecializationDebugMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

#if UNITY_2023_1_OR_NEWER
    [VolumeComponentMenu("Post-processing/lilToon-HoCharacter/角色特化"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#else
    [VolumeComponentMenuForRenderPipeline("Post-processing/lilToon-HoCharacter/角色特化", typeof(UniversalRenderPipeline))]
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
            displayName = "lilToon-HoCharacter 角色特化";
#endif
            Enable.overrideState = true;
            EyeRevealEnabled.overrideState = true;
            EyeRevealStrength.overrideState = true;
            HairDropShadowEnabled.overrideState = true;
            HairShadowColor.overrideState = true;
            HairShadowOpacity.overrideState = true;
            HairShadowDistancePixels.overrideState = true;
            HairShadowAngleDegrees.overrideState = true;
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
        public HoCharacterRenderScaleParameter RenderScale = new HoCharacterRenderScaleParameter(HoAovRenderScale.Full);

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

        [InspectorName("启用前发投影"), Tooltip("用 FrontHair 标记向 Face 标记投射屏幕空间阴影。")]
        public BoolParameter HairDropShadowEnabled = new BoolParameter(true);

        [InspectorName("投影颜色"), Tooltip("前发投影颜色。")]
        public ColorParameter HairShadowColor = new ColorParameter(new Color(0.78f, 0.68f, 0.72f, 1.0f));

        [InspectorName("投影不透明度"), Tooltip("前发投影强度。")]
        public ClampedFloatParameter HairShadowOpacity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [InspectorName("投影距离像素"), Tooltip("投影沿角度方向偏移的屏幕像素距离。")]
        public FloatParameter HairShadowDistancePixels = new FloatParameter(15.0f);

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

        [InspectorName("调试模式"), Tooltip("把角色特化的中间结果写回当前视图，便于检查眼睛透过和前发投影。")]
        public HoCharacterDebugModeParameter DebugMode = new HoCharacterDebugModeParameter(HoCharacterSpecializationDebugMode.Off);

        public bool IsActive()
        {
            return active
                && Enable.value
                && (EyeRevealEnabled.value || HairDropShadowEnabled.value || DebugMode.value != HoCharacterSpecializationDebugMode.Off);
        }

        public bool IsActiveForCamera(CameraType cameraType)
        {
            if (!IsActive())
            {
                return false;
            }

            if (cameraType == CameraType.SceneView)
            {
                return ShowInSceneView.value;
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

            target.enabled = Enable.value;
            target.layerMask = LayerMask.value;
            target.minRenderQueue = MinRenderQueue.value;
            target.maxRenderQueue = MaxRenderQueue.value;
            target.passEvent = PassEvent.value;
            target.renderScale = RenderScale.value;
            target.eyeRevealEnabled = EyeRevealEnabled.value;
            target.eyeRevealStrength = EyeRevealStrength.value;
            target.eyeRevealFeatherPixels = EyeRevealFeatherPixels.value;
            target.eyeRevealDilationPixels = EyeRevealDilationPixels.value;
            target.eyeRevealDepthBias = EyeRevealDepthBias.value;
            target.useEyeRevealArea = UseEyeRevealArea.value;
            target.sameCharacterOnly = SameCharacterOnly.value;
            target.hairDropShadowEnabled = HairDropShadowEnabled.value;
            target.hairShadowColor = HairShadowColor.value;
            target.hairShadowOpacity = HairShadowOpacity.value;
            target.hairShadowDistancePixels = HairShadowDistancePixels.value;
            target.hairShadowAngleDegrees = HairShadowAngleDegrees.value;
            target.hairShadowSoftnessPixels = HairShadowSoftnessPixels.value;
            target.hairShadowSpreadPixels = HairShadowSpreadPixels.value;
            target.hairShadowKeepOffHair = HairShadowKeepOffHair.value;
            target.hairShadowBlendMode = HairShadowBlendMode.value;
            target.debugMode = DebugMode.value;
        }
    }
}

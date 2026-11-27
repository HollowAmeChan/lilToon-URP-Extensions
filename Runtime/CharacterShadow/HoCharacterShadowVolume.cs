using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterShadow
{
    [Serializable]
    public sealed class HoCharacterShadowResolutionParameter : VolumeParameter<HoCharacterShadowResolution>
    {
        public HoCharacterShadowResolutionParameter(HoCharacterShadowResolution value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterShadowResolution from, HoCharacterShadowResolution to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterShadowDebugModeParameter : VolumeParameter<HoCharacterShadowDebugMode>
    {
        public HoCharacterShadowDebugModeParameter(HoCharacterShadowDebugMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterShadowDebugMode from, HoCharacterShadowDebugMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    /// <summary>
    /// Per-camera overrides for Ho-CharacterShadow: enable, per-receiver resolution and the debug view.
    /// The RendererFeature keeps the fallback values (used when a field here is not overridden) and the
    /// structural settings (timing, shader); receiver declarations stay on the HoCharacterShadow component.
    /// </summary>
    [Serializable]
    [VolumeComponentMenu(HoVolumeNames.CharacterShadow)]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = HoVolumeNames.CharacterShadow)]
#endif
    public sealed class HoCharacterShadowVolume : VolumeComponent, IPostProcessComponent
    {
        public HoCharacterShadowVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = HoVolumeNames.CharacterShadow;
#endif
        }

        [InspectorName("启用"), Tooltip("该相机是否使用局部高精度天光投影。未勾选覆盖时用 Ho-CharacterShadow RendererFeature 的兜底值。\n\n逐相机覆盖；未覆盖字段使用 Feature 兜底值，接收对象由场景组件声明。")]
        public BoolParameter enable = new BoolParameter(true);

        [InspectorName("单角色分辨率"), Tooltip("每个接收域的深度图分辨率。可容纳 tile 数 = (图集边长上限/本值)²。容量不足的接收域使用普通天光投影。")]
        public HoCharacterShadowResolutionParameter resolution = new HoCharacterShadowResolutionParameter(HoCharacterShadowResolution.R2048);

        [InspectorName("阴影软边"), Tooltip("独立角色阴影的固定 PCF 请求半径（米）。按当前 tile 换算成 1 到 2 个 texel。")]
        public ClampedFloatParameter softnessRadius = new ClampedFloatParameter(0.005f, 0.0f, 0.2f);

        [InspectorName("调试模式"), Tooltip("直出替换画面，不改材质输出。Off 无输出；Atlas 看整张图集；Character 按编号放大单个接收域的 tile。")]
        public HoCharacterShadowDebugModeParameter debugMode = new HoCharacterShadowDebugModeParameter(HoCharacterShadowDebugMode.Off);

        [InspectorName("场景视图"), Tooltip("是否在 Scene View 中显示调试画面。")]
        public BoolParameter debugInSceneView = new BoolParameter(true);

        [InspectorName("游戏视图"), Tooltip("是否在 Game View 中显示调试画面。会替换最终画面，默认关闭。")]
        public BoolParameter debugInGameView = new BoolParameter(false);

        [InspectorName("单角色 tile"), Tooltip("Character 模式下要放大的接收域编号，编号见 Ho-CharacterShadow 组件 Inspector 的「图集 Tile」。")]
        public ClampedIntParameter debugCharacter = new ClampedIntParameter(0, 0, 15);

        public bool IsActive()
        {
            return enable.value;
        }

        public bool IsTileCompatible()
        {
            return false;
        }

        /// <summary>
        /// The volume component of the camera currently being rendered, or null when no volume stack
        /// exists. Fields whose overrideState is false must fall back to the RendererFeature settings.
        /// </summary>
        public static HoCharacterShadowVolume Resolve()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return stack != null ? stack.GetComponent<HoCharacterShadowVolume>() : null;
        }
    }
}

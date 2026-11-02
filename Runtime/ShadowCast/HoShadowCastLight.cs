using UnityEngine;

namespace lilToon.URP.Extensions.ShadowCast
{
    [DisallowMultipleComponent, RequireComponent(typeof(Light))]
    [AddComponentMenu("Rendering/Ho-ShadowCast Light")]
    public sealed class HoShadowCastLight : MonoBehaviour
    {
        [InspectorName("加入主阴影")]
        [Tooltip("将这盏附加灯的 HoShadowCast 投影加入材质的「主阴影」模式。全阴影仍包含它。此组件不会开启 Light 的阴影、改变灯光颜色或突破图集预算；Light 仍须为 Realtime/Mixed 且启用阴影。")]
        public bool includeInMainShadow = true;
    }
}

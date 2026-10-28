using System.Collections.Generic;
using UnityEngine;
namespace lilToon.URP.Extensions.RTShadow
{
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Light)), AddComponentMenu("Rendering/Ho-RTShadow Light")]
    public sealed class HoRTShadowLight : MonoBehaviour
    {
        internal static readonly List<HoRTShadowLight> Active = new();
        [InspectorName("方向光角直径（度）"), Range(0, 8)] public float angularDiameter = .5f;
        public Light Light => GetComponent<Light>();
        private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        private void OnDisable() { Active.Remove(this); }
    }
}

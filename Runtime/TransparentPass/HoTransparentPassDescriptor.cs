using System;
using UnityEngine;

namespace lilToon.URP.Extensions.TransparentPass
{
    [Serializable]
    public sealed class HoTransparentPassDescriptor
    {
        [InspectorName("Enabled")]
        public bool enabled = true;

        [InspectorName("Light Mode")]
        public string lightMode = string.Empty;

        [InspectorName("Profiler Name")]
        public string profilerName = string.Empty;

        public bool IsValid => enabled && !string.IsNullOrWhiteSpace(lightMode);

        public static HoTransparentPassDescriptor Backface()
        {
            return new HoTransparentPassDescriptor
            {
                enabled = true,
                lightMode = HoTransparentShaderConstants.BackfacePassName,
                profilerName = "Ho-Transparent Backface"
            };
        }

        public static HoTransparentPassDescriptor Frontface()
        {
            return new HoTransparentPassDescriptor
            {
                enabled = true,
                lightMode = HoTransparentShaderConstants.FrontfacePassName,
                profilerName = "Ho-Transparent Frontface"
            };
        }
    }
}

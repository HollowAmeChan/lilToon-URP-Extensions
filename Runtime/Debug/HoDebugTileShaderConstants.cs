using UnityEngine;

namespace lilToon.URP.Extensions.Debugging
{
    public static class HoDebugTileShaderConstants
    {
        public const string ShaderName = "Hidden/lilToon/URP/Debug/DebugTile";
        public const string ShaderAssetPath = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/Debug/Shaders/HoDebugTile.shader";
        public static readonly int ModeId = Shader.PropertyToID("_HoDebugTileMode");
    }
}

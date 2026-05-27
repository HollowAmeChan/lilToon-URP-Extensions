using UnityEngine;

namespace lilToon.URP.Extensions.Debugging
{
    public static class HoDebugTileShaderConstants
    {
        public const string ShaderName = "Hidden/lilToon/URP/Debug/DebugTile";
        public const string ShaderAssetPath = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/Debug/Shaders/HoDebugTile.shader";

        public static readonly int RenderKindId = Shader.PropertyToID("_HoDebugTileRenderKind");
        public static readonly int ModeId = Shader.PropertyToID("_HoDebugTileMode");
        public static readonly int RectId = Shader.PropertyToID("_HoDebugTileRect");
        public static readonly int GridId = Shader.PropertyToID("_HoDebugTileGrid");
        public static readonly int Label0Id = Shader.PropertyToID("_HoDebugTileLabel0");
        public static readonly int Label1Id = Shader.PropertyToID("_HoDebugTileLabel1");
        public static readonly int Label2Id = Shader.PropertyToID("_HoDebugTileLabel2");
        public static readonly int Label3Id = Shader.PropertyToID("_HoDebugTileLabel3");
        public static readonly int GeometryDepthParamsId = Shader.PropertyToID("_HoDebugTileGeometryDepthParams");
        public static readonly int PlanarReflectionDebugInputStatusId = Shader.PropertyToID("_HoPlanarReflectionDebugInputStatus");
    }
}

using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    internal static class HoSurfaceBufferShaderConstants
    {
        public const string ShaderPassName = "HoSurfaceBuffer";
        public const string DebugShaderName = "Hidden/lilToon/URP/SurfaceBuffer/DebugView";

        public const string ActiveName = "_HoSurfaceBufferActive";
        public const string ColorTextureName = "_HoSurfaceBufferColorTexture";
        public const string NormalTextureName = "_HoSurfaceBufferNormalTexture";
        public const string MaterialTextureName = "_HoSurfaceBufferMaterialTexture";
        public const string ReflectionTextureName = "_HoSurfaceBufferReflectionTexture";
        public const string ClassificationTextureName = "_HoSurfaceBufferClassificationTexture";
        /// <summary>internal：这个像素上的前表面是谁（16-bit IdentityId；0 = 没有 writer）。</summary>
        public const string OwnerTextureName = "_HoSurfaceBufferOwnerTexture";
        public const string DebugModeName = "_HoSurfaceBufferDebugMode";

        /// <summary>数值附件的 MRT 序：0..4 是五张数值图，5 是 owner。</summary>
        public const int ColorAttachment = 0;
        public const int NormalAttachment = 1;
        public const int MaterialAttachment = 2;
        public const int ReflectionAttachment = 3;
        public const int ClassificationAttachment = 4;
        public const int OwnerAttachment = 5;
        public const int ValueAttachmentCount = 6;

        public static readonly int ActiveId = Shader.PropertyToID(ActiveName);
        public static readonly int ColorTextureId = Shader.PropertyToID(ColorTextureName);
        public static readonly int NormalTextureId = Shader.PropertyToID(NormalTextureName);
        public static readonly int MaterialTextureId = Shader.PropertyToID(MaterialTextureName);
        public static readonly int ReflectionTextureId = Shader.PropertyToID(ReflectionTextureName);
        public static readonly int ClassificationTextureId = Shader.PropertyToID(ClassificationTextureName);
        public static readonly int OwnerTextureId = Shader.PropertyToID(OwnerTextureName);
        public static readonly int DebugModeId = Shader.PropertyToID(DebugModeName);

        public static readonly ShaderTagId ShaderTagId = new ShaderTagId(ShaderPassName);
    }
}

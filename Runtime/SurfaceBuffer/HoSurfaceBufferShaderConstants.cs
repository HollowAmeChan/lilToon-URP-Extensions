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

        // ---------------------------------------------------------------- 语义 lane（单采样，规划 §0.4 / §0.3.7）

        /// <summary>语义 pass 的 LightMode：**与数值 pass 分开**（附件格式 / 数量都不同，一趟最多 8 个 MRT）。</summary>
        public const string SemanticShaderPassName = "HoSurfaceSemantic";
        public const string SemanticActiveName = "_HoSurfaceSemanticActive";
        /// <summary>逐像素的 owner（16-bit IdentityId，两个字节；与数值 owner 同一个值）。</summary>
        public const string SemanticOwnerTextureName = "_HoSurfaceSemanticOwnerTexture";
        /// <summary>4 张 RGBA8，每张两条 `(SemanticId, value)`：lane 0/1、2/3、4/5、6/7。</summary>
        public const string SemanticLaneTextureNameFormat = "_HoSurfaceSemanticLane{0}Texture";
        /// <summary>lane j 的 SemanticId（来自 `HoSemanticSchema`；OB/SB/AC 共用一份声明）。</summary>
        public const string SemanticLaneIdsNameFormat = "_HoSemanticLaneIds{0}";
        /// <summary>lane j 的物体位掩码（0 = 这条 lane 没有物体位 ⇒ SB 不写它）。</summary>
        public const string SemanticLaneTagMaskNameFormat = "_HoSemanticLaneTagMasks{0}";

        /// <summary>4 张 RGBA8 = 8 条 lane；词表变宽（&gt; 8 位）时再上 16-lane 分批。</summary>
        public const int SemanticLaneTextureCount = 4;
        public const int SemanticLaneCount = SemanticLaneTextureCount * 2;
        public const int SemanticLaneCountPerTexture = 2;

        /// <summary>语义附件的 MRT 序：0 是 owner，1..4 是四张 lane 图。</summary>
        public const int SemanticOwnerAttachment = 0;
        public const int SemanticLaneAttachmentBase = 1;
        public const int SemanticAttachmentCount = SemanticLaneAttachmentBase + SemanticLaneTextureCount;

        public static readonly int SemanticActiveId = Shader.PropertyToID(SemanticActiveName);
        public static readonly int SemanticOwnerTextureId = Shader.PropertyToID(SemanticOwnerTextureName);
        public static readonly int SemanticLaneIdsId0 = Shader.PropertyToID(string.Format(SemanticLaneIdsNameFormat, 0));
        public static readonly int SemanticLaneIdsId1 = Shader.PropertyToID(string.Format(SemanticLaneIdsNameFormat, 1));
        public static readonly int SemanticLaneTagMasksId0 = Shader.PropertyToID(string.Format(SemanticLaneTagMaskNameFormat, 0));
        public static readonly int SemanticLaneTagMasksId1 = Shader.PropertyToID(string.Format(SemanticLaneTagMaskNameFormat, 1));

        public static readonly ShaderTagId SemanticShaderTagId = new ShaderTagId(SemanticShaderPassName);

        public static int GetSemanticLaneTextureId(int index)
        {
            return Shader.PropertyToID(string.Format(SemanticLaneTextureNameFormat, index));
        }

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

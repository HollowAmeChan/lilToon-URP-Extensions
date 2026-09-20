using UnityEngine;

namespace lilToon.URP.Extensions.AttributeComposite
{
    internal static class HoAttributeCompositeShaderConstants
    {
        public const string ResolveShaderName = "Hidden/lilToon/URP/AttributeComposite/SelectionResolve";
        public const string DebugShaderName = "Hidden/lilToon/URP/AttributeComposite/DebugView";

        public const string ActiveName = "_HoACActive";
        public const string LaneCountName = "_HoACLaneCount";
        public const string LaneBufferName = "_HoACLanes";
        public const string DebugModeName = "_HoACDebugMode";

        /// <summary>Selection 池：`_HoACSelection{0..7}Texture`，每张 RGBA8 装 2 条 `(SemanticId, coverage)`。</summary>
        public const string SelectionTextureFormat = "_HoACSelection{0}Texture";

        public const int SelectionTexturesPerResolve = 4;

        /// <summary>
        /// 有 SB 语义 lane 时打开：Selection 池的每条 lane 按 catalog 的 `sourceMode` 与 SB 合成。
        /// 关掉就是纯物体位（`ObjectOnly`）——没有 SB / 平台不支持时 AC 不会假装有 surface 来源。
        /// <para>
        /// 单采样：lane 是**逐像素**的（`SAMPLE_TEXTURE2D_X` 普通采样）。逐 sample 细分重新上时，
        /// 由 SB 自己 resolve 出单采样 lane 再发布，这个关键字仍然只表示"有没有 surface 来源"。
        /// </para>
        /// </summary>
        public const string SurfaceKeyword = "_HO_SURFACE_SEMANTIC";

        public static readonly int ActiveId = Shader.PropertyToID(ActiveName);
        public static readonly int LaneCountId = Shader.PropertyToID(LaneCountName);
        public static readonly int LaneBufferId = Shader.PropertyToID(LaneBufferName);
        public static readonly int DebugModeId = Shader.PropertyToID(DebugModeName);

        public static readonly int[] SelectionTextureIds = BuildSelectionTextureIds();

        private static int[] BuildSelectionTextureIds()
        {
            var ids = new int[SelectionTexturesPerResolve];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = Shader.PropertyToID(string.Format(SelectionTextureFormat, i));
            }

            return ids;
        }
    }
}

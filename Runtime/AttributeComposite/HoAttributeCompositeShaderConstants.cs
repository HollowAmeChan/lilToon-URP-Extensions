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
        /// 有 SB 语义 lane 时打开：Selection 池的每条 lane 走 `SurfaceOverride`（逐 sample 与 SB 合成）。
        /// 两个都关就是纯物体位解压（`ObjectOnly`）——没有 SB / 平台不支持时 AC 不会假装有 surface 来源。
        /// 与 OB 的 resolve 同一套：采样数由关键字给出（声明 `Texture2DMS&lt;T, N&gt;` 要用到它）。
        /// </summary>
        public const string SurfaceMsaa2Keyword = "_HO_SURFACE_SEMANTIC_MSAA_2";

        public const string SurfaceMsaa4Keyword = "_HO_SURFACE_SEMANTIC_MSAA_4";

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

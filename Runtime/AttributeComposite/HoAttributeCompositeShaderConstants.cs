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

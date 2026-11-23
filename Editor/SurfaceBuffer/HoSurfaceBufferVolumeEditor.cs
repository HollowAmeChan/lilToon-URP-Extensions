using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.SurfaceBuffer
{
    /// <summary>
    /// SB 的调试入口面板（SB 架构 §14：调试在 Volume，feature 只放高级设置 + 兜底默认值）。
    /// 下拉只显示视图名，当前视图的通道含义放在控件的悬停提示中。
    /// </summary>
    [CustomEditor(typeof(HoSurfaceBufferVolume))]
    internal sealed class HoSurfaceBufferVolumeEditor : HoVolumeComponentEditor
    {
        private SerializedDataParameter enable;
        private SerializedDataParameter debugMode;
        private SerializedDataParameter debugInSceneView;
        private SerializedDataParameter debugInGameView;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<HoSurfaceBufferVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.enable));
            debugMode = Unpack(fetcher.Find(x => x.debugMode));
            debugInSceneView = Unpack(fetcher.Find(x => x.debugInSceneView));
            debugInGameView = Unpack(fetcher.Find(x => x.debugInGameView));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(enable);
            string description = DescribeDebugMode((HoSurfaceBufferDebugMode)debugMode.value.intValue);
            PropertyField(debugMode, new GUIContent("调试模式", description));

            PropertyField(debugInSceneView);
            PropertyField(debugInGameView);
        }

        /// <summary>通道含义与取景范围照 `Shaders/Debug/HoSurfaceBufferDebug.shader` 写，改 shader 就改这里。</summary>
        private static string DescribeDebugMode(HoSurfaceBufferDebugMode mode)
        {
            switch (mode)
            {
                case HoSurfaceBufferDebugMode.All:
                    return "平铺所有 SB 视图，沿用各单视图的解码与颜色；在最终后处理之后显示。";
                case HoSurfaceBufferDebugMode.Color:
                    return "写入的着色色：lilToon 的最终颜色，未过后处理。";
                case HoSurfaceBufferDebugMode.Emission:
                    return "独立自发光组分：单层线性 HDR 贡献，不含其他发亮效果；未经过曝光、Bloom 或色调映射。";
                case HoSurfaceBufferDebugMode.Depth:
                    return "SB 自己的线性眼空间深度，按 depth/(1+depth) 显示；暗红为无 writer。";
                case HoSurfaceBufferDebugMode.GeometryAssociation:
                    return "绿 = 深度匹配，橙 = 深度错配，红 = 无 SB writer，蓝 = 无 GB 几何，黄 = 分辨率或取样域不同，洋红 = 缺少输入。匹配不等于 primitive 身份证明。";
                case HoSurfaceBufferDebugMode.Normal:
                    return "世界空间法线：octa 解出的方向，已映射到 0..1。";
                case HoSurfaceBufferDebugMode.GeometryNormal:
                    return "未经过 normal map 的世界空间网格法线；映射到 0..1，洋红表示 writer 未提供该组分。";
                case HoSurfaceBufferDebugMode.Material:
                    return "R = 1-粗糙度（越白越光滑），G = 金属度，B = 厚度。";
                case HoSurfaceBufferDebugMode.Reflection:
                    return "R = 反射率，G = PLR 强度。";
                case HoSurfaceBufferDebugMode.Classification:
                    return "R = 材质档位（÷8 显示），G = 曲率，B = 透射提示。三通道全 0 是合法结果（档位 0 且曲率/透射为 0）。";
                case HoSurfaceBufferDebugMode.Owner:
                    return "绿 = 与 OB 层 0 一致，橙 = 写了但对不上，红 = 没人写（SB 没画到这个像素），洋红 = OB 没产出。";
                case HoSurfaceBufferDebugMode.ClassId:
                    return "R = 材质类（÷32 显示）。";
                case HoSurfaceBufferDebugMode.SemanticOwner:
                    return "语义 lane 的 owner：绿 = 与 OB 层 0 一致，橙 = 对不上，红 = 没人写，洋红 = OB 没产出。";
                case HoSurfaceBufferDebugMode.SemanticLanes:
                    return "8 条语义 lane 铺成 4×2 网格（左到右 lane 0..3 / 4..7）：通道 = (SemanticId÷255, value, 写了没有)；未写是暗红。";
                default:
                    // Off：没有要解释的东西就不画那一行。
                    return string.Empty;
            }
        }
    }
}

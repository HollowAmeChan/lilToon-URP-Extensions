using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    /// <summary>
    /// 调试用的一次性回读：把**已经 resolve 完**的层图拷回 CPU，在 Console 里打一张"实际字节直方图"。
    /// <para>
    /// 为什么需要它：调试视图只能给出"看起来是什么颜色"，而"洋红 = 未注册行"和"洋红 = 拿到垃圾数据"
    /// 是同一个颜色，靠肉眼分不开（这一轮就是被这件事拖住的）。这里直接给出
    /// <c>0xRRGGBBAA × 像素数</c>，把"写没写进去 / 写进去的是什么"一次性钉死。
    /// </para>
    /// <para>
    /// 只在 debug 视图打开时工作：这条路径给每帧加两次全屏 CopyTexture，正常渲染不该付这个成本。
    /// </para>
    /// </summary>
    internal static class HoObjectBufferReadback
    {
        private const float IntervalSeconds = 1.0f;
        private static float nextRequestTime;
        private static bool inFlight;
        private static int logCount;

        /// <summary>回读的开关：调用方按"调试视图是否开着"决定。</summary>
        public static bool Enabled { get; set; }

        /// <summary>拷贝用的持久目标（不属于 RenderGraph，所以不会被别名复用）。</summary>
        public static void Request(HoObjectBufferRenderTargets targets)
        {
            if (!Enabled || inFlight || targets == null || targets.Id0Texture == null)
            {
                return;
            }

            if (Time.realtimeSinceStartup < nextRequestTime)
            {
                return;
            }

            RenderTexture id0 = targets.Id0Texture.rt;
            RenderTexture coverage = targets.CoverageTexture != null ? targets.CoverageTexture.rt : null;
            if (id0 == null || coverage == null || !SystemInfo.supportsAsyncGPUReadback)
            {
                return;
            }

            nextRequestTime = Time.realtimeSinceStartup + IntervalSeconds;
            inFlight = true;
            string size = $"{id0.width}x{id0.height} {id0.graphicsFormat}";
            AsyncGPUReadback.Request(id0, 0, request =>
            {
                LogHistogram($"Id0(组0,槽0,组1,槽1) [{size}]", request);
                AsyncGPUReadback.Request(coverage, 0, coverageRequest =>
                {
                    LogHistogram($"Coverage(层0..层3) [{size}]", coverageRequest);
                    inFlight = false;
                });
            });
        }

        private static void LogHistogram(string label, AsyncGPUReadbackRequest request)
        {
            if (request.hasError)
            {
                Debug.LogWarning($"[Ho-ObjectBuffer][READBACK] {label}：回读失败（hasError）。");
                return;
            }

            NativeArray<Color32> pixels = request.GetData<Color32>(0);
            if (!pixels.IsCreated || pixels.Length == 0)
            {
                Debug.LogWarning($"[Ho-ObjectBuffer][READBACK] {label}：没有数据。");
                return;
            }

            var counts = new Dictionary<uint, int>(16);
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                uint key = ((uint)p.r << 24) | ((uint)p.g << 16) | ((uint)p.b << 8) | p.a;
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;
            }

            var sorted = new List<KeyValuePair<uint, int>>(counts);
            sorted.Sort(static (a, b) => b.Value.CompareTo(a.Value));

            var text = new System.Text.StringBuilder();
            text.Append($"[Ho-ObjectBuffer][READBACK #{++logCount}] {label}：像素={pixels.Length} 唯一值={sorted.Count}");
            int shown = Mathf.Min(6, sorted.Count);
            for (int i = 0; i < shown; i++)
            {
                uint key = sorted[i].Key;
                int count = sorted[i].Value;
                float percent = 100.0f * count / pixels.Length;
                text.Append($"\n    {(key >> 24) & 0xFFu:X2}{(key >> 16) & 0xFFu:X2}{(key >> 8) & 0xFFu:X2}{key & 0xFFu:X2} × {count} ({percent:F2}%)");
                if (label.StartsWith("Id0"))
                {
                    text.Append($"  => 层0 组={(key >> 24) & 0xFFu} 槽={(key >> 16) & 0xFFu} / 层1 组={(key >> 8) & 0xFFu} 槽={key & 0xFFu}");
                }
                else
                {
                    text.Append($"  => 覆盖率 {((key >> 24) & 0xFFu) / 255.0f:F2} / {((key >> 16) & 0xFFu) / 255.0f:F2} / {((key >> 8) & 0xFFu) / 255.0f:F2} / {(key & 0xFFu) / 255.0f:F2}");
                }
            }

            Debug.Log(text.ToString());
        }
    }
}

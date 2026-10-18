# Ho-RTDI 独立Player GPU计时审计

> 状态：有日期/协议边界的审计证据。现行完成度与使用入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)；历史失败仍按原门槛保留。

第43步交付隔离的计时构建/诊断入口，当前环境没有得到有效GPU时长，因此GPU预算仍未证明。运行时DI/GI、Shader、native和普通质量门槛不变；失败的计时结果不包装为性能验收。

## 依据与测量边界

核对现有Cutout GPU sampler、Core `ProfilingSampler`的Development/Recorder实现，以及Unity 6 [Recorder GPU API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Profiling.Recorder-gpuElapsedNanoseconds.html)、[FrameTimingManager](https://docs.unity3d.com/6000.0/Documentation/Manual/frame-timing-manager.html)和[CustomSampler GPU数据开关](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Profiling.CustomSampler.Create.html)。GPU Recorder数据延迟三个应用帧；FrameTimingManager会有记录开销，整帧数据不能替代单pass测量。

`BuildGpuTiming/BuildGpuTimingCompatibility`生成独立Development目录，不覆盖普通Release二进制。普通Prepare复制新增测试sidecar；普通Run/Build行为保持。`--gpu-timing`默认每个应用帧提交一次渲染请求、yield到下一帧，512²测试DI关闭、opaque DI+NRD、移动Cutout DI+NRD三种状态，每状态160帧、舍弃前36帧。Capture feature停用，使用已有FinalVisibility/VisibilityGradients标记，记录CPU/GPU样本数，不新增产品profiling模块或资源。

记录FrameTiming可用/新时间戳条数、有效正GPU时长条数、实际endCameraRendering次数、CPU标记帧数与动态gradient调度帧数。帧时间戳去重，缺失/零GPU时长不算有效；至少32个GPU样本且实际相机/gradient调度有效才成立。所有不足项用-1，report.pass=false并exit1；协程异常会保存错误、清理订阅/recorders并退出。Release误用计时参数也明确失败，不挂起。

这里的全帧包括URP/GB/SB/DI等，DI启用还可能改变输入需求，所以即使今后能取得frame差值，也不能直接当作纯DI shader成本。动态/静态场景差异也不是纯gradient差值；单独marker才是相应阶段的度量。本步简单Quad/单caster不是古堡代表性性能场景。

## 当前机器的实测缺失

RTX 4060、D3D12、Development=true、FrameTimingEnabled=true、GPU计时频率1GHz。最终手动请求三种状态分别有160次相机渲染事件、124条可用且新鲜的FrameTiming记录，但有效GPU帧时长为0条，GPU标记也0条：

| 状态 | 相机事件 | 新FrameTiming | GPU时长样本 | visibility/gradient CPU标记帧 | gradient调度帧 |
| --- | --- | --- | --- | --- | --- |
| base | 160 | 124 | 0 | 0 / 0 | 0 |
| opaque | 160 | 124 | 0 | 0 / 0 | 0 |
| movingCutout | 160 | 124 | 0 | 124 / 124 | 124 |

GPU字段都为-1，正式false/exit1。相机事件/CPU标记/调度标志证明请求路径执行，不能替代GPU完成或耗时证明。频率和API启用也不代表有有效duration。

同一Development EXE隐藏非batch窗口的手动请求对照仍没有有效GPU时间；`--gpu-auto`自动相机对照相机事件0、调度0，亦失败。保留这些不同运行条件，不据此推断唯一原因、宣称GPU/API不支持，或再用CPU wall time充当GPU时间。自动相机问题与先前motion源未验证同样需要实际帧/呈现证据。

此前RenderDoc捕获没有得到文件，本步未重复盲目启动该失败捕获；没有修改用户Unity会话、古堡资产或旁边仓库。

## 验证与下一项

Development及两个普通Win64 Mono Release builds errors0。最终计时EXE按缺失数据实际exit1；Release误用参数按Development要求exit1并输出JSON。普通实际D3D12 RG/compat完整质量suite和D3D11 inactive通过。Roslyn和提交前检查通过，无产品Shader变更，不重复无共享变更的GI。

独立计时入口为后续现场采集提供可复现条件，但不解决GPU预算；下一步继续扩大分辨率质量，同时评估能取得真实应用帧/GPU query的采集方式。不要将0样本当0成本或据此优化采样数量；保留原DI正确性、残影和材质门槛，不扩张最终合成/通用PT。

## 第81步：实际Runtime sampler复查

当前Core包`com.unity.render-pipelines.core@648b47f96bf1/Runtime/Debugging/ProfilingScope.cs`显示，ProfilingSampler创建GPU-enabled CustomSampler并持有自己的Recorder。计时sidecar原先另建同名CustomSampler；本步改为读取feature实际的FinalVisibilityProfiling/VisibilityGradientsProfiling对象，显式enableRecording，通过其gpuElapsedTime/gpuSampleCount统计。报告新增samplerSource，避免仅凭同名字符串假定测到了实际对象。

新的Development构建errors0，实际D3D12隐藏batch Player完整执行并exit1。RTX4060、1GHz、FrameTimingEnabled=true，三状态仍各160次camera、124个新鲜frame records；moving Cutout实际CPU标记/gradient调度各124。GPU frame/两marker样本依然0，全部时长-1、pass=false。此变化排除了一项instrumentation疑点，没有解决GPU计时，也没有将现有结果改判通过。报告`rtdi-timing81-runtime-samplers.json`。

当前安装RenderDoc1.45，MCP报告无已打开capture；没有可分析的rdc，也没有重复之前未产出文件的同条件捕获。[RenderDoc v1.36官方说明](https://github.com/baldurk/renderdoc/releases/tag/v1.36)已允许保存含标准raytracing调用的捕获，但raytracing内部仍不提供shader/binding/AS调试。当前MCP统计接口提供draw/dispatch/resource统计，未提供GPU duration查询；不能据其“statistics”名称宣称已测成本。Shell的renderdoc-cli不在PATH，未绕开技能改用其它批处理抓帧工具。

本步仅改独立计时sidecar，生产DI/Shader/native/常规Player质量逻辑未改。参考与实际接口边界已确认，后续GPU预算需要能产出有效GPU timestamp/capture的测量条件；这不是当前DI质量/场景支持工作的整体阻塞。

# Ho-RTGI 当前交接

更新：2026-10-19，步骤000文档提交准备；步骤001参考对照进行中。目标状态 active，未完成，未暂停。

## 目标与不可丢失的用户要求

沿本机 UnityPathTracing 成熟链路推进 Ho-RTGI v0.1：实跑参考 GI、同场景对照，完善一跳 diffuse、次级天空、独立降噪、场景合成与实际验收。每步必须事前评审、事后 review 正确性/收益/退化、过滤提交、更新过程与交接；长期无收益路线搁置并写踩坑/禁止重复条件。不要回到无边界调参。

完整规范：[规划强制交付条件](../计划/Ho-RTGI-v0.1-实施规划.md)。活动 goal 已引用规划；当前 goal 工具无法改 objective，只能读/创建/改状态，所以这些新增约束保存在该引用文档中，禁止通过虚假完成旧目标来改写字符串。

## 工作区与并行边界

- 当前目录 `D:/Unity_Fork/lilToon-URP-Extensions`，共享 checkout。最近已存在 RTDI 提交 `94c9128`，不是本任务提交。
- RTDI 另一任务仍在修改 Runtime/Editor/RTDI 的 Volume、Settings、RendererFeature、Player/GUI 等文件，有未跟踪 ViewSettings/VolumeControlsValidation；**保留，不暂存、不提交、不回退**。每次提交前重新看 `git status` / index，列表会变化。
- 本任务自有文件：本目录四份文档、`Documentation~/计划/Ho-RTGI-v0.1-实施规划.md`、`Tests~/RTGI/Reference/HoReferenceGIProbe.cs`。步骤000只提交文档，探针留给步骤001完整复审后提交。
- 尚未修改任何 GI Runtime/Editor 或用户场景。

## 工程与运行状态

- 参考副本 `.codex-research/UnityPathTracing-6000.3.15`；native 源码 `.codex-research/UnityPathTracing/RenderingPlugin`。
- Unity `C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe`，RTX4060 / D3D12，参考当前 Linear。
- 参考启动对子进程 PATH 增加 `Assets/Plugins/x86_64` 与 `Packages/top.kuanmi.native-rendering/Plugins/x86_64`，不改系统 PATH。隐藏启动 `-batchmode -force-d3d12`，不使用 `-nographics`，同工程一次一进程。
- `HoReferenceGIProbe.RunUnityRaw` 的 PID 18940 已完成、退出0。父 exec session 63997 已收取完成结果，没有本任务仍运行的 Unity 测试。用户 BREAK_URP Unity 和 AssetImportWorker 是其他进程，不操作。
- 探针源码已复制到参考 `Assets/Editor/HoReferenceGIProbe.cs`；同目录原 DI probe/stats 未改。既有 NRI/Native 修复按 `Tests~/RTDI/Reference/README.md` 保留。
- `.codex-research/RTInputValidation` 是 DI 既有测试工程，后续 Ho GI 对照应优先建立自己的隔离工程，避免与并行 DI 运行冲突。

## 最新证据与限制

参考 Unity raw 实际报告：`.codex-research/UnityPathTracing-6000.3.15/ho-reference-gi-v01-unity-raw/report.json`；日志 `.codex-research/rtgi-v01-reference-unity-raw.log`；同目录有 direct-only 和第1/4/8/16/32/64/128/256 GI raw EXR/PNG。

报告 `pass=true`、264次手动请求，direct-only 基线差0，中心32² ROI GI raw 增量 RGB约 `(0.072124, 0.002533, 0.001618)`。这是单灯白接收面/离屏红墙的真实 GI 存在性诊断，尚无 Ho 同场景对照、无偏参考或重建收益结论。读取包含同步 GPU readback，非性能测试。

参考退出日志存在 licensing 握手/令牌提示（后续仍实际渲染并退出0），以及 persistent allocation / d3d12 referenced resource 释放提示；仍需归类/记录，不能静默声称无诊断。当前筛查未见本轮新增 shader 编译错误，正式步骤001仍需完整核对。

## 下一动作（按顺序）

1. 完成步骤000文档的精确路径 review、链接检查、过滤提交；记录提交号。不要把探针或 RTDI 并行文件混进去。
2. 复审 `HoReferenceGIProbe`：baseline 差应按每通道绝对值，标明 DI+GI 合成增量与重建 combined 的区别；核对 IOR/F0、粗糙度、采样参数实际消费和 shader 诊断。
3. 运行 `RunUnityNoReuse`、`RunUnityRR`、`RunNativeRelax`（顺序、每项检查实际退出/新报告），观察参考 GI 原始/重建路径。如有失败先定位，不重复修已解决的 NRI/DLL 问题。
4. 建 Ho 自用验证工程与相同场景，先匹配 diffuse 支持域与 raw 能量/时间方差，再决定优先补次级天空还是重建。
5. 步骤001有充分结果后，将探针、必要报告快照/哈希、过程/禁试/交接更新作为一组过滤提交；之后继续 M1/M2。每次压缩前保证本文件反映真实未完成工作和运行进程。

# Ho-RTGI 当前交接

更新：2026-10-19，步骤000已提交，步骤001诊断矩阵与复审完成、准备过滤提交。目标状态 active，未完成，未暂停。M0仍需共同响应域的必要能量对照。

## 目标与不可丢失的用户要求

沿本机 UnityPathTracing 成熟链路推进 Ho-RTGI v0.1：实跑参考 GI、同场景对照，完善一跳 diffuse、次级天空、独立降噪、场景合成与实际验收。每步必须事前评审、事后 review 正确性/收益/退化、过滤提交、更新过程与交接；长期无收益路线搁置并写踩坑/禁止重复条件。不要回到无边界调参。

完整规范：[规划强制交付条件](../计划/Ho-RTGI-v0.1-实施规划.md)。活动 goal 已引用规划；当前 goal 工具无法改 objective，只能读/创建/改状态，所以这些新增约束保存在该引用文档中，禁止通过虚假完成旧目标来改写字符串。

## 工作区与并行边界

- 当前目录 `D:/Unity_Fork/lilToon-URP-Extensions`，共享 checkout。本任务步骤000提交 `7ece0fe`。RTDI已新增 `5c404cd`，不是本任务提交。
- RTDI另一任务继续修改 Runtime/Editor/RTDI，最新见过 Context/RendererFeature、ProjectionSample、VolumeLiveAudit 和根README；**保留，不暂存、不提交、不回退**。每次提交前重新看 `git status` / index，列表会变化。
- 本任务自有文件：本目录四份文档、规划、`Tests~/RTGI/Reference/`、`Editor/RTGI/HoRTGIMatchedReferenceValidation.cs`及meta。步骤001仅过滤提交这些相关诊断/证据/文档；提交号可用 `git log -1 -- Tests~/RTGI/Reference` 查询，下步骤再记入过程记录。
- 尚未修改 GI Runtime 或用户场景；Editor仅增加隔离诊断入口。

## 工程与运行状态

- 参考副本 `.codex-research/UnityPathTracing-6000.3.15`；native 源码 `.codex-research/UnityPathTracing/RenderingPlugin`。
- Unity `C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe`，RTX4060 / D3D12，参考当前 Linear。
- 参考启动对子进程 PATH 增加 `Assets/Plugins/x86_64` 与 `Packages/top.kuanmi.native-rendering/Plugins/x86_64`，不改系统 PATH。隐藏启动 `-batchmode -force-d3d12`，不使用 `-nographics`，同工程一次一进程。
- 步骤001全部测试已结束，相关 exec session 63997/67520/68162/21469/72296/73483/75477/57623均已收取完成结果，没有本任务仍运行的Unity测试。最后Ho canonical PID43156/40808/11744均exit0。用户BREAK_URP与其AssetImportWorker等是其他进程，不操作。
- 探针源码已复制到参考 `Assets/Editor/HoReferenceGIProbe.cs`；同目录原 DI probe/stats 未改。既有 NRI/Native 修复按 `Tests~/RTDI/Reference/README.md` 保留。
- 已建立 `.codex-research/RTGIValidation`：从既有验证工程复制ProjectSettings，显式设Linear，manifest引用本地HoURP/lilToon/Extensions，移除与本测试无关的GLTF两项依赖。RTInputValidation留给并行DI，不占用。

## 最新证据与限制

正式证据快照与可复现说明：[Tests~/RTGI/Reference/README.md](../../Tests~/RTGI/Reference/README.md)及其results目录。`run.json`记录四个参考真实exit0/fresh/pass和source/report SHA；`ho-run.json`记录三个Ho入口同样信息。保留Native首版8帧协议失败的报告，不能删除。全量EXR/PNG仍在隔离项目的对应输出目录，选定PNG随步骤提交。

参考Unity无复用/复用raw/RR、NativeGI+RELAX四项均264手动请求；Ho无复用/RG复用/compat复用三项均256请求。最后128帧时间方差：参考 `.0107172 → .000590190`，RR实际combined `.00000752505`；Ho `.00993115 → .0000167614`，RG/compat一致。不同响应域与输出语义下不能用这些值排名画质或断言无偏。未做真实自动帧motion、GPU预算、Player或目标场景验收。

关键发现：参考设置IOR1并未关闭二次F0，实际packet `FF3B3B3B`约.04；Ho为`FF000000`。参考Unity还合并primary specular，Nativedebug则为demodulated diff+spec。两边主色按各自codec接近，但当前不是严格diffuse-only能量匹配。Ho初次元数据reader曾误用参考48B布局，已按真实HLSL纠正并重跑三项；正式结果均为修正版本。

七份canonical日志无shader编译error/warning或C#编译错误；仍存在licensing启动提示和reference退出persistent/reference-resource提示，未声称参考完整生命周期通过。诊断与暂缓项见过程记录及禁试F08～F10。

## 下一动作（按顺序）

1. 完成步骤001最终hash/链接/diff与精确路径过滤提交。步骤000已结束，七个诊断已完成，不要无新理由重跑。
2. 新步骤先评审必要的响应域对齐：参考次级F0约.04，primary最终输出含specular；用独立diffuse输出/已选reservoir的参考求值或其他有证据的最小方案完成共同域对照。不要为了追求逐位相同改参考采样算法，不凭表面IOR字段判断响应。
3. 已知Ho ReSTIR在当前夹具有效，优先补用户可见缺口：沿既有参考/SDK适配次级天空、GI独立合成与重建，按实测选择顺序；不重写通用PT或重新做DI滤波试值。
4. 对每个新步骤执行事前评审、实现、必要验证、正确性/收益/退化review、文档/禁试/交接更新和过滤提交。只在真实目标交付后完成goal，当前保持active。

# Ho-RTGI 当前交接

更新：2026-10-19，步骤001已提交，步骤002共同响应域/独立积分已验证并review、准备过滤提交。目标active，未完成，未暂停。M0本组对照结束，下一项生产次级天空照明。

## 目标与不可丢失的用户要求

沿本机 UnityPathTracing 成熟链路推进 Ho-RTGI v0.1：实跑参考 GI、同场景对照，完善一跳 diffuse、次级天空、独立降噪、场景合成与实际验收。每步必须事前评审、事后 review 正确性/收益/退化、过滤提交、更新过程与交接；长期无收益路线搁置并写踩坑/禁止重复条件。不要回到无边界调参。

完整规范：[规划强制交付条件](../计划/Ho-RTGI-v0.1-实施规划.md)。活动 goal 已引用规划；当前 goal 工具无法改 objective，只能读/创建/改状态，所以这些新增约束保存在该引用文档中，禁止通过虚假完成旧目标来改写字符串。

## 工作区与并行边界

- 当前目录 `D:/Unity_Fork/lilToon-URP-Extensions`，共享 checkout。本任务步骤000 `7ece0fe`，步骤001 `6e90d21`。RTDI提交不是本任务提交，每次操作重新检查。
- RTDI另一任务继续修改 Runtime/Editor/RTDI，最新见过 Context/RendererFeature、ProjectionSample、VolumeLiveAudit 和根README；**保留，不暂存、不提交、不回退**。每次提交前重新看 `git status` / index，列表会变化。
- 本任务自有文件：本目录四份文档、规划、`Tests~/RTGI/Reference/`、`Editor/RTGI/HoRTGIMatchedReferenceValidation.cs`及meta。步骤002只过滤提交CommonDiffuse及对应夹具/文档；提交号用 `git log -1 -- Tests~/RTGI/Reference/CommonDiffuse` 查询，下步骤记入过程。
- 尚未修改 GI Runtime 或用户场景；Editor仅增加隔离诊断入口。

## 工程与运行状态

- 参考副本 `.codex-research/UnityPathTracing-6000.3.15`；native 源码 `.codex-research/UnityPathTracing/RenderingPlugin`。
- Unity `C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe`，RTX4060 / D3D12，参考当前 Linear。
- 参考启动对子进程 PATH 增加 `Assets/Plugins/x86_64` 与 `Packages/top.kuanmi.native-rendering/Plugins/x86_64`，不改系统 PATH。隐藏启动 `-batchmode -force-d3d12`，不使用 `-nographics`，同工程一次一进程。
- 步骤001及002全部测试均已结束；步骤002最终reference PID26616/47888、Ho PID42440/40176均exit0，exec session54591/46272已收取结束结果。没有本任务仍运行的Unity测试。用户BREAK_URP和并行DI进程不操作；检查进程时只输出PID/工程路径，不输出完整命令行（可能带登录令牌）。
- 探针源码已复制到参考 `Assets/Editor/HoReferenceGIProbe.cs`；同目录原 DI probe/stats 未改。既有 NRI/Native 修复按 `Tests~/RTDI/Reference/README.md` 保留。
- 已建立 `.codex-research/RTGIValidation`：从既有验证工程复制ProjectSettings，显式设Linear，manifest引用本地HoURP/lilToon/Extensions，移除与本测试无关的GLTF两项依赖。RTInputValidation留给并行DI，不占用。

## 最新证据与限制

步骤002正式说明与证据：[CommonDiffuse/README.md](../../Tests~/RTGI/Reference/CommonDiffuse/README.md)。参考仅用独立最终求值Compute选diffuse，原采样/SDK未改。Ho夹具显式线性F0=.04（需Gamma字段转换），实际打包`FF0A0A0A`；reference为`FF3B3B3B`约.03995。

四个实际渲染入口exit0；独立墙面积分48²/96²变化<.01%。Ho无复用RGB误差`-.146%/-.412%/+1.201%`、复用`+.220%/+1.759%/+3.608%`均在原5%内。参考无复用约`-7.37%/-9.73%/-9.55%`、复用约`-5.41%/-16.91%/-22.45%`仍失败。因此 `integrate_fixture.py` 实际exit1、overallPass=false、hoQualityPass=true。保留失败，暂缓追参考唯一根因，不反向调Ho增益或阈值。步骤002无Runtime改动，M0这组有限验证足以支持推进实际缺口。

正式证据快照与可复现说明：[Tests~/RTGI/Reference/README.md](../../Tests~/RTGI/Reference/README.md)及其results目录。`run.json`记录四个参考真实exit0/fresh/pass和source/report SHA；`ho-run.json`记录三个Ho入口同样信息。保留Native首版8帧协议失败的报告，不能删除。全量EXR/PNG仍在隔离项目的对应输出目录，选定PNG随步骤提交。

参考Unity无复用/复用raw/RR、NativeGI+RELAX四项均264手动请求；Ho无复用/RG复用/compat复用三项均256请求。最后128帧时间方差：参考 `.0107172 → .000590190`，RR实际combined `.00000752505`；Ho `.00993115 → .0000167614`，RG/compat一致。不同响应域与输出语义下不能用这些值排名画质或断言无偏。未做真实自动帧motion、GPU预算、Player或目标场景验收。

关键发现：参考设置IOR1并未关闭二次F0，实际packet `FF3B3B3B`约.04；Ho为`FF000000`。参考Unity还合并primary specular，Nativedebug则为demodulated diff+spec。两边主色按各自codec接近，但当前不是严格diffuse-only能量匹配。Ho初次元数据reader曾误用参考48B布局，已按真实HLSL纠正并重跑三项；正式结果均为修正版本。

七份canonical日志无shader编译error/warning或C#编译错误；仍存在licensing启动提示和reference退出persistent/reference-resource提示，未声称参考完整生命周期通过。诊断与暂缓项见过程记录及禁试F08～F10。

## 下一动作（按顺序）

1. 完成步骤002源码/报告hash、链接/diff和精确路径过滤提交。步骤001已结束，不因源码扩展要求把旧矩阵全部重跑；历史Ho source hash按6e90d21核对。
2. 步骤003先记录review，然后实现次级天空：沿现有DI环境bridge与固定SDK的环境PDF/RIS/初始MIS，GI拥有私有小型环境资源；不在并行DI文件做重构。明确主表面天空miss与次级天空为独立开关，避免DI重复计光。
3. 同步补GI pass数值快照（现持有可变Settings引用），正确声明RG与兼容路径环境绑定/历史失效。测试零解析灯也可次级天空、恒定天空能量/遮挡、主天空归属、开关和sky revision；已有GI完整链路用专用工程回归。
4. 接GI独立场景合成/NRD重建，优先用户可见收益。每步都事前review、实测、复审、文档/禁试/交接更新并过滤提交。参考侧差异调查暂缓，重开条件见F12。不用较小任务替代完整goal，当前保持active。

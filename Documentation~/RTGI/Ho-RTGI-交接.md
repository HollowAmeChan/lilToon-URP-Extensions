# Ho-RTGI 当前交接

更新：2026-10-19，步骤004已提交82df540；步骤005独立GI RELAX_DIFFUSE已完成正式矩阵/回归与复审，准备过滤提交。目标active，未完成；下一项正常自动帧动态/空间质量、目标场景/性能与Player。

## 目标与不可丢失的用户要求

沿本机 UnityPathTracing 成熟链路推进 Ho-RTGI v0.1：实跑参考 GI、同场景对照，完善一跳 diffuse、次级天空、独立降噪、场景合成与实际验收。每步必须事前评审、事后 review 正确性/收益/退化、过滤提交、更新过程与交接；长期无收益路线搁置并写踩坑/禁止重复条件。不要回到无边界调参。

完整规范：[规划强制交付条件](../计划/Ho-RTGI-v0.1-实施规划.md)。活动 goal 已引用规划；当前 goal 工具无法改 objective，只能读/创建/改状态，所以这些新增约束保存在该引用文档中，禁止通过虚假完成旧目标来改写字符串。

## 工作区与并行边界

- 当前目录 `D:/Unity_Fork/lilToon-URP-Extensions`，共享 checkout。本任务步骤000 `7ece0fe`、001 `6e90d21`、002 `f6278de`、003 `a0a89f6`、004 `82df540`。其他提交不属本任务，每次操作重新检查工作区/暂存区。
- RTDI另一任务继续修改 Runtime/Editor/RTDI，最新见过 Context/RendererFeature、ProjectionSample、VolumeLiveAudit 和根README；**保留，不暂存、不提交、不回退**。每次提交前重新看 `git status` / index，列表会变化。
- 步骤004自有改动：新Runtime/RTGI/CompositePass和shader及meta、GI Settings/Volume/Feature接入与Editor UI，新CompositeValidation、legacyGI测试显式DI启用、Tests~/RTGI/Composite，以及RTGI文档。提交号用`git log -1 -- Runtime/RTGI/HoRTGICompositePass.cs`查，下步记入过程。任何并行文件不纳入。
- 没有改用户场景。次级天空、普通相机加法及独立NRD已有实际结果；Player、正常自动帧动态/复杂空间质量和目标场景性能仍未交付。

## 工程与运行状态

- 参考副本 `.codex-research/UnityPathTracing-6000.3.15`；native 源码 `.codex-research/UnityPathTracing/RenderingPlugin`。
- Unity `C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe`，RTX4060 / D3D12，参考当前 Linear。
- 参考启动对子进程 PATH 增加 `Assets/Plugins/x86_64` 与 `Packages/top.kuanmi.native-rendering/Plugins/x86_64`，不改系统 PATH。隐藏启动 `-batchmode -force-d3d12`，不使用 `-nographics`，同工程一次一进程。
- 步骤001及002全部测试均已结束；步骤002最终reference PID26616/47888、Ho PID42440/40176均exit0，exec session54591/46272已收取结束结果。没有本任务仍运行的Unity测试。用户BREAK_URP和并行DI进程不操作；检查进程时只输出PID/工程路径，不输出完整命令行（可能带登录令牌）。
- 探针源码已复制到参考 `Assets/Editor/HoReferenceGIProbe.cs`；同目录原 DI probe/stats 未改。既有 NRI/Native 修复按 `Tests~/RTDI/Reference/README.md` 保留。
- 已建立 `.codex-research/RTGIValidation`：从既有验证工程复制ProjectSettings，显式设Linear，manifest引用本地HoURP/lilToon/Extensions，移除与本测试无关的GLTF两项依赖。RTInputValidation留给并行DI，不占用。

### 步骤003当前验证进程

正式runner为 `Tests~/RTGI/Environment/run_validation.ps1`，exec session71171已完成并收取终态；run.json为11 entries、activeProcess=null，11项均实际exit0、passMarker=true、shaderCsDiagnostics=0。专用工程已恢复Linear。当前没有本任务测试活进程；后续若tool session失效，先检查run.json中的PID与exit记录，不能仅因失联重启。

初次矩阵的Linear环境两路径、legacy两路径和secondary五项exit0；Gamma新探针第一请求receiver无效而exit1，已保留`run-initial-first-frame-failure.json`和`Gamma-rejected-first-frame.json`（其旧log路径/哈希为历史，当时log后被正式运行覆盖）。另跑既有三次editor warmup的Gamma legacy实际exit0。新探针采用同样的三次有界warmup，之后仍严格检查全部有效性/有限性；新增混合MIS 5%检查与远挡板后，最终11项均通过。

四组环境报告数值一致，每组138请求/3warmup/23checks；sky-only bounce约`(.357808,.179135,.088113)`，analytic-only`(.217788,.216423,.214741)`，mixed`(.572891,.393576,.301248)`，相对独立项之和约-.47%～-.53%。说明与真实报告在 `Tests~/RTGI/Environment/`。没有GPU毫秒/Player或完整动态质量结论。

生产内容：私有128²环境PDF、16384 uint2 RIS，主直接天空默认不含；次级shader从stored DI reservoir重建选中样本后测可见性，pass使用不可变数值快照；直接天空有限bounce范围miss时补到100000世界单位的远处遮挡查询。`Ho-RTGI.md`已补设置/默认行为变化与资源成本说明。

### 步骤004验证终态

`Tests~/RTGI/Composite/run_validation.ps1`已完成，exec session9815终态已收取；`results/run.json`为6项实际exit0、pass/fresh检查成功、shaderCsDiagnostics=0、activeProcess=null。四组Composite每组62请求/33checks，两项旧链路回归。当前无本任务测试活进程，专用项目已恢复原设置。

已验证同帧GI加法、0/.25/1强度、主色/emission不重复、fog/透明顺序、背景alpha、真实前景depth、输入域拒绝、局部viewport绕过、DI后GI连续累加、Volume覆盖/不回写及两台实际相机独立gain/context；实际离屏红墙的次级天空反弹进入普通camera color，PNG在隔离项目`ho-gi-composite-{rg,compat}`。

保留失败：并行DI改为Volume显式启用后，联合夹具仅SetActive无法生产DI；现明确加DI Volume enable并验证真实context，legacyGI联合夹具也同步更新。没有改DI生产策略。见Composite/results/rejected-di-opt-in.json及禁试F15。

生产接口：合成默认false、强度.25，Volume可覆盖；启用时生产与合成同在BeforeRenderingTransparents并按入队顺序执行，未启用保留原生产时序。合成输入已含BRDF/albedo；主材质emission不再加，Gamma源先解码，雾只衰减新增光，alpha保留。数据契约仍Mono/全viewport/单采样，raw/debug未删除。raw画面仍有噪声，本步没有NRD、自动运动帧、GPU毫秒或Player结论。

## 最新证据与限制

步骤002正式说明与证据：[CommonDiffuse/README.md](../../Tests~/RTGI/Reference/CommonDiffuse/README.md)。参考仅用独立最终求值Compute选diffuse，原采样/SDK未改。Ho夹具显式线性F0=.04（需Gamma字段转换），实际打包`FF0A0A0A`；reference为`FF3B3B3B`约.03995。

四个实际渲染入口exit0；独立墙面积分48²/96²变化<.01%。Ho无复用RGB误差`-.146%/-.412%/+1.201%`、复用`+.220%/+1.759%/+3.608%`均在原5%内。参考无复用约`-7.37%/-9.73%/-9.55%`、复用约`-5.41%/-16.91%/-22.45%`仍失败。因此 `integrate_fixture.py` 实际exit1、overallPass=false、hoQualityPass=true。保留失败，暂缓追参考唯一根因，不反向调Ho增益或阈值。步骤002无Runtime改动，M0这组有限验证足以支持推进实际缺口。

正式证据快照与可复现说明：[Tests~/RTGI/Reference/README.md](../../Tests~/RTGI/Reference/README.md)及其results目录。`run.json`记录四个参考真实exit0/fresh/pass和source/report SHA；`ho-run.json`记录三个Ho入口同样信息。保留Native首版8帧协议失败的报告，不能删除。全量EXR/PNG仍在隔离项目的对应输出目录，选定PNG随步骤提交。

参考Unity无复用/复用raw/RR、NativeGI+RELAX四项均264手动请求；Ho无复用/RG复用/compat复用三项均256请求。最后128帧时间方差：参考 `.0107172 → .000590190`，RR实际combined `.00000752505`；Ho `.00993115 → .0000167614`，RG/compat一致。不同响应域与输出语义下不能用这些值排名画质或断言无偏。未做真实自动帧motion、GPU预算、Player或目标场景验收。

关键发现：参考设置IOR1并未关闭二次F0，实际packet `FF3B3B3B`约.04；Ho为`FF000000`。参考Unity还合并primary specular，Nativedebug则为demodulated diff+spec。两边主色按各自codec接近，但当前不是严格diffuse-only能量匹配。Ho初次元数据reader曾误用参考48B布局，已按真实HLSL纠正并重跑三项；正式结果均为修正版本。

七份canonical日志无shader编译error/warning或C#编译错误；仍存在licensing启动提示和reference退出persistent/reference-resource提示，未声称参考完整生命周期通过。诊断与暂缓项见过程记录及禁试F08～F10。

## 步骤005最新状态

独立原版RELAX_DIFFUSE：15个DIFF-only wrapper、6 permanent/4 transient池，每相机独立实例/guide/history。共用DI ABI/dispatch只加可选类型/manifest/settings，DI默认调用不变。乘albedo前直接生成demodulated信号，NRD前后成对除/乘64，保留raw，新RawDiffuse调试/global/RG发布，合成读取PublishedDiffuse。Settings/Volume denoise默认false。

Tests~/RTGI/NRD/results/run.json汇总7项实际exit0：四组Linear/Gamma×RG/compat各243请求/41checks，DI RunDenoise完整能量/噪声/生命周期门槛、raw GI合成和Gamma环境compat回归。均值(.360382,.180422,.088747)→(.364358,.182411,.089711)，红通道逐像素时间方差.1141095684→.00000390791。HDR天空1000均值差约+.7%；零albedo、首暗帧reset、相机矩阵移动、invalid、Volume、两相机独立与resize均通过。新增1080p约156.4MiB，未测GPU毫秒。

exec session79307（四组GI通过，DI marker误配runner失败）、75531（正确三项回归）终态均已收取。当前本任务没有活Unity测试，active.json/run.json均activeProcess=null，禁止重启旧PID。

共享VolumeLight施工编译错误，未改他人文件；专用工程manifest现指向.codex-research/RTGIValidationPackage：base82df540加95份GI/API覆盖。snapshot-matrix.json与snapshot.json源码SHA逐项相同，已核对当前工作树。改GI后必须先snapshot_validation.ps1同步，不能将该隔离包当实时源码。git archive的旧ShadowCast合同CRLF导致宏parser误报，仅快照转LF并记录最终SHA，生产未改。不能扩称当前共享工作区整体验收通过。

保留失败：33位GUID导入失败、缺using（已修正）；快照CRLF；DI RunConvergence实际exit0但错误marker拒绝，换已有RunDenoise严格门槛通过。部分历史同名log被成功后续运行覆盖，其旧SHA只能作历史；报告/Convergence capture均保留。后续runner已加时间戳。见Tests~/RTGI/NRD/README.md与F16～18。

复审接纳为可选原版基线；正常自动帧动态、复杂空间细节/薄遮挡、任意HDR、运动角色、实际场景性能/Player均未完成，不能由均匀夹具噪声收益替代。

## 下一动作（按顺序）

1. 步骤005最终过滤提交：仅Runtime/RTGI、Editor/RTGI、两份共享NRD API和Tests~/RTGI/NRD/RTGI文档；先看git/index，所有VolumeLight等并行变动排除。
2. 步骤006先review正常自动帧动态与空间质量：真实应用frameCount推进，验证静态世界camera重投影、移动几何/天空/材质变化首帧reset、薄边/非均匀间接光的细节。不能将同帧多RenderRequest标为动态帧；无阶段证据不调NRD参数。
3. 后续实际场景补DI已有天空壳排除、基础Cutout/骨骼遮挡等GI支持；分段GPU/显存与Mono Player全部shader/native/合成的引用及打包可达性必须实测，不能据Editor加载证明Player。
4. 每步事前review、实测、复审正确性/收益/退化、过程/禁试/交接更新、过滤提交。参考侧偏暗差异调查暂缓F12；完整目标保持active。UI目标看似丢失时先get_goal，不虚假完成重建。

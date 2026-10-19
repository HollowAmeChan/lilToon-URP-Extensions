# Ho-RTGI 当前交接

更新：2026-10-19，步骤003次级天空代码与正式矩阵已完成review，准备过滤提交；目标active，未完成，未暂停。下一步正常相机GI合成，后续独立NRD/场景/性能/Player仍须交付。

## 目标与不可丢失的用户要求

沿本机 UnityPathTracing 成熟链路推进 Ho-RTGI v0.1：实跑参考 GI、同场景对照，完善一跳 diffuse、次级天空、独立降噪、场景合成与实际验收。每步必须事前评审、事后 review 正确性/收益/退化、过滤提交、更新过程与交接；长期无收益路线搁置并写踩坑/禁止重复条件。不要回到无边界调参。

完整规范：[规划强制交付条件](../计划/Ho-RTGI-v0.1-实施规划.md)。活动 goal 已引用规划；当前 goal 工具无法改 objective，只能读/创建/改状态，所以这些新增约束保存在该引用文档中，禁止通过虚假完成旧目标来改写字符串。

## 工作区与并行边界

- 当前目录 `D:/Unity_Fork/lilToon-URP-Extensions`，共享 checkout。本任务步骤000 `7ece0fe`、001 `6e90d21`、002 `f6278de`。RTDI/SSS等并行提交不是本任务提交，每次操作重新检查。
- RTDI另一任务继续修改 Runtime/Editor/RTDI，最新见过 Context/RendererFeature、ProjectionSample、VolumeLiveAudit 和根README；**保留，不暂存、不提交、不回退**。每次提交前重新看 `git status` / index，列表会变化。
- 步骤003未提交的自有改动：Runtime/RTGI的Settings、Context、RendererFeature、bridge/compute/raytrace，Editor/RTGI的FeatureEditor、legacyValidation显式天空设置和新EnvironmentValidation，Tests~/RTGI/Environment启动脚本/结果，以及RTGI文档。任何并行文件都不纳入提交。
- 没有改用户场景。次级天空已接生产代码，但普通相机合成、独立NRD、Player/目标场景仍未交付。

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

## 最新证据与限制

步骤002正式说明与证据：[CommonDiffuse/README.md](../../Tests~/RTGI/Reference/CommonDiffuse/README.md)。参考仅用独立最终求值Compute选diffuse，原采样/SDK未改。Ho夹具显式线性F0=.04（需Gamma字段转换），实际打包`FF0A0A0A`；reference为`FF3B3B3B`约.03995。

四个实际渲染入口exit0；独立墙面积分48²/96²变化<.01%。Ho无复用RGB误差`-.146%/-.412%/+1.201%`、复用`+.220%/+1.759%/+3.608%`均在原5%内。参考无复用约`-7.37%/-9.73%/-9.55%`、复用约`-5.41%/-16.91%/-22.45%`仍失败。因此 `integrate_fixture.py` 实际exit1、overallPass=false、hoQualityPass=true。保留失败，暂缓追参考唯一根因，不反向调Ho增益或阈值。步骤002无Runtime改动，M0这组有限验证足以支持推进实际缺口。

正式证据快照与可复现说明：[Tests~/RTGI/Reference/README.md](../../Tests~/RTGI/Reference/README.md)及其results目录。`run.json`记录四个参考真实exit0/fresh/pass和source/report SHA；`ho-run.json`记录三个Ho入口同样信息。保留Native首版8帧协议失败的报告，不能删除。全量EXR/PNG仍在隔离项目的对应输出目录，选定PNG随步骤提交。

参考Unity无复用/复用raw/RR、NativeGI+RELAX四项均264手动请求；Ho无复用/RG复用/compat复用三项均256请求。最后128帧时间方差：参考 `.0107172 → .000590190`，RR实际combined `.00000752505`；Ho `.00993115 → .0000167614`，RG/compat一致。不同响应域与输出语义下不能用这些值排名画质或断言无偏。未做真实自动帧motion、GPU预算、Player或目标场景验收。

关键发现：参考设置IOR1并未关闭二次F0，实际packet `FF3B3B3B`约.04；Ho为`FF000000`。参考Unity还合并primary specular，Nativedebug则为demodulated diff+spec。两边主色按各自codec接近，但当前不是严格diffuse-only能量匹配。Ho初次元数据reader曾误用参考48B布局，已按真实HLSL纠正并重跑三项；正式结果均为修正版本。

七份canonical日志无shader编译error/warning或C#编译错误；仍存在licensing启动提示和reference退出persistent/reference-resource提示，未声称参考完整生命周期通过。诊断与暂缓项见过程记录及禁试F08～F10。

## 下一动作（按顺序）

1. 完成步骤003最终源码/报告hash及精确路径提交；本步已经review并正式11项通过，不再无新理由重复矩阵。提交号用 `git log -1 -- Runtime/RTGI` 查询，下步写入过程。
2. 步骤004先review然后接正常相机GI加法合成/强度，沿DI已有色彩空间、雾、receiver/深度门控与透明物顺序；GI输出已含接收BRDF，不能再乘albedo/emission，不能对有效黑面生成假亮光。改注入时序须确认GB/SB已就绪且在透明物前完成。
3. 独立NRD接入要保留raw，核对真实GI命中距离、原版固定RELAX同输入基线和独立历史，不把DI private策略或Native prior-DI距离直接套用。
4. 后续实际场景还须核查DI已有天空壳材质排除、基础Cutout/骨骼遮挡等GI支持；并完成正常帧动态响应、GPU分段/显存和Mono Player。不要把新环境/合成开关当成整个goal已交付。
5. 每步都事前review、实测、复审、文档/禁试/交接更新并过滤提交。参考侧差异调查暂缓，重开条件见F12。完整目标保持active。

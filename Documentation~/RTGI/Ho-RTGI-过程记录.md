# Ho-RTGI 持续推进过程记录

开始日期：2026-10-19。当前目标与用户约束见[规划](../计划/Ho-RTGI-v0.1-实施规划.md)，最新操作状态见[交接](Ho-RTGI-交接.md)。每一步都要有事前评审、事后 review 与精确路径提交；本文“下一步”只表示写作时判断。

## 000：目标约束、参考调查与交接入口

状态：接纳文档与工作流程；无 Runtime 改动，不计画质收益。

**事前评审：** 用户先要求规划 RTGI，随后要求多看本机参考工程及 DI 过程文档，开启目标持续推进，并明确每步 review/过滤提交、无收益路线搁置/禁试记录和多次压缩交接。需要先固定这些约束，避免后续只继承功能清单。

**调查：** 阅读 DI 可行性调查 §15～17、第108步参考收敛审计、NRD、motion、距离、参数快照、环境和相关失败文档；对照本机参考 GI 五阶段、Unity RR、Native RELAX 的实际 host / shader 路径。参考副本 `.codex-research/UnityPathTracing-6000.3.15`，native 源码 `.codex-research/UnityPathTracing/RenderingPlugin`。

**判断：** 先实跑参考 GI，再按同场景差距移植。DI 的 raw/重建分离方法可复用；DI 已通过的参数、距离和派生滤波不能直接推广至 GI。详见规划文末的具体映射表。

**产出：** 规划、RTGI 导航、过程记录、踩坑/禁试和交接文档。活动 goal 的 objective 已引用规划；工具不支持修改活动 objective，因此将新要求写入该引用文档，不虚假完成再重建目标。

**验证：** 检查规划的本地 Markdown 引用全部存在；`git diff --check` 无 whitespace 错误（并行任务文件可能输出 CRLF 提示，非本步改动）。提交前再次只对本步文档核对路径。文档修改不执行 Unity 回归。

**事后 review：** 内容覆盖用户的逐步提交、正确性/收益复审、无收益搁置、禁试证据与压缩交接要求；没有扩大 PT/SSS/DLSS 产品范围，没有操作用户场景。收益是后续任务可追溯与可接手；GI 质量/功能仍未交付。接纳。

**过滤提交：** 仅 `Documentation~/计划/Ho-RTGI-v0.1-实施规划.md` 和 `Documentation~/RTGI/` 下本步四个文档。新参考探针尚在 M0 验证，不混入本步；RTDI 并行修改不纳入。

步骤000过滤提交：`7ece0fe`（仅上述五份文档）。

## 001：参考 GI 真实运行与 Ho 诊断基线

**事前评审：** 既有 DI 探针显式关闭 GI；需要新入口实际启用 GI 并读实际输出。先建立单解析灯、白色接收面与离屏红墙，禁用环境/jitter/额外次级 DI 复用，减小混杂。不得以合并 RR 画面直接代表 GI raw。

**当前实现：** 新参考探针 `Tests~/RTGI/Reference/HoReferenceGIProbe.cs`，复制至隔离参考的 `Assets/Editor/`。不替换采样 shader。先渲染 8 次无 GI 的单灯基线，再启用 GI 256 次，读取真实 raw 和可用重建输出。此协议含同步 readback，仅用于诊断，非 GPU timing 或正常应用帧运动验收。

**初步证据：** `HoReferenceGIProbe.RunUnityRaw` 实际进程 PID 18940 退出0；D3D12 / RTX4060 / Linear / 264请求。ROI raw 增量 RGB约 `(0.072124, 0.002533, 0.001618)`，direct-only基线差0。随后补齐 canonical 矩阵、真实材质捕获与哈希快照；初次报告不单独充当正式结论。

**实现：** 增加独立参考探针和顺序启动/新报告/退出码检查脚本，借用现有 NRI/依赖修复；不替换采样 shader。Native 通过额外诊断 Feature 开启已有 indirect debug 常量。Ho 新增独立 Editor 验证入口，在 `.codex-research/RTGIValidation` 跑相同几何，未占用并行 DI 测试工程。每个路径捕获实际输出和 secondary 材质打包，输出统计与 EXR/PNG。

**正式验证：** 参考 Unity无复用/复用raw/RR、Native GI+RELAX四项均实际exit0、fresh=true、reportPass=true；Ho无复用/RG复用/compat复用三项同样通过。参考每项264次请求，Ho每项256次。Canonical源码/报告SHA与退出记录见 `Tests~/RTGI/Reference/results/{run,ho-run}.json`。七个日志无 shader error/warning 或 C# 编译错误；资源退出提示另记，未据退出0称完整生命周期通过。

**实测：** 最后GI129～256窗口时间方差：参考Unity无复用 `.0107172`、复用 `.000590190`、RR实际combined `.00000752505`；Native debug `.00163686`、RELAX combined `.00000643314`。Ho无复用 `.00993115`，RG/compat复用均 `.0000167614`。Ho已存在的SDK重采样在此夹具显著降低波动，无需先重写采样器；数字不能跨不同响应域直接排名。

**失败与修正：** Native首版从DI输入相减，8帧基线不稳定，exit1/报告false，拒绝该协议并保留报告；改用已有真实GI调试输出，没有放宽断言。复审又发现Ho的48B secondary布局不同，修正元数据读取后重跑三条canonical；渲染算法没有变化。新增禁试F08～F10。

**事后 review：** 探针证明参考 GI、RR、RELAX 与 Ho GI 的真实执行及时间方差行为，定位收益明确，接纳诊断工具和证据。实际secondary捕获证明参考F0约.04，Ho夹具F0=0，参考还合并primary specular；两边主色按各自codec接近，但当前仍不是严格diffuse能量对齐。RR/RELAX输出标为combined，手动请求/readback不标为GPU预算或正常运动验收。没有生产画质改动，不声称M0全部结束或GI产品完成。

**过滤提交范围：** 新 `Editor/RTGI/HoRTGIMatchedReferenceValidation.cs` 及meta、`Tests~/RTGI/Reference/` 的探针/启动脚本/README/结果与选定截图、本目录过程/禁试/交接更新。排除 Runtime、RTDI并行文件及整个隔离工程/巨量日志/EXR。下一步先按已定位的真实响应域完成必要对照，再沿参考补次级环境与场景合成/重建。

步骤001过滤提交：`6e90d21`。

## 002：共同 diffuse 响应域对照与独立能量验证

**事前评审：** 步骤001实际捕获确认参考次级F0约.04，Ho夹具为0，且参考最终含primary specular。继续对比原图逐通道均值会将响应域差异误判成采样错误，须用有界的共同域对照结束M0。

**最小方案与预期收益：** 保留参考的初始射线、次级灯光、temporal/spatial和SDK原算法；仅用独立Editor诊断Compute在最后求值选取同一个reservoir的diffuse分量，调用参考已有BRDF、visibility与StoreShadingOutput。Ho夹具显式设reflectance=.04；分别跑无复用和复用。此适配只用于对照，不迁移到产品、不引入specular GI、不改原参考文件。若此有限对照不能证明对应能量，就保留失败并定位输入，不扩大为新采样器。

**实现与输入复核：** 新增CommonDiffuse适配器，包装既有参考探针，仅替换内存feature的最终求值Compute；不修改原参考文件或采样。Ho新增两个显式入口，使用Gamma-tagged Reflectance的对应值表达线性F0=.04，并断言GPU次级打包为`FF0A0A0A`。参考实际打包`FF3B3B3B`按其Gamma codec解码约.03995，Ho量化为10/255。

**实际验证：** 参考无复用/复用、Ho无复用/复用四个修正后实际进程均exit0；参考264请求、Ho256请求。初次参考适配缺DI reservoir include导致shader编译失败、exit1，补齐依赖后重跑。初次Ho直接给Gamma字段写.04，presence诊断虽过但物理F0不对，拒绝该夹具并保留报告；没有放宽物理断言。

**独立能量依据：** 有限平面夹具可直接做墙面积分。新增NumPy确定性积分，不调用GI/SDK或reservoir，按实际量化主色/F0、roughness1 GGX/Smith、面积到立体角Jacobian及相机ROI积分。48²→96²墙面划分变化小于.01%。Ho无复用相对误差RGB为`-.146%/-.412%/+1.201%`，复用为`+.220%/+1.759%/+3.608%`，均通过原5%门槛。

**保留失败与暂缓：** 参考diffuse无复用误差`-7.373%/-9.731%/-9.554%`，复用`-5.413%/-16.911%/-22.446%`。独立比较脚本真实exit1、overallPass=false；不能因四个渲染进程exit0就称整组quality通过。弱通道还受combined FP16相减误差影响，当前未证明唯一根因。参考侧偏差调查暂缓，不反向调Ho增益去贴齐，不改原门槛，不铺通用PT。

**事后 review：** Ho当前一跳diffuse在该静态共同域有独立能量支持，已有重采样具备可用基础，接纳诊断和证据，结束M0本组对照。定位收益明确，生产画面未改。参考不是无条件ground truth，保留它的成熟架构/接口参考价值和质量失败边界。下一步转向实际功能缺口，先次级天空照明、独立贡献归属与可见画面。完整证据见 `Tests~/RTGI/Reference/CommonDiffuse/`。

**过滤提交范围：** 仅CommonDiffuse探针/Compute/积分与报告、Ho Editor夹具新增入口、Reference README历史版本说明和RTGI过程/禁试/交接。Runtime及RTDI并行工作不纳入。

步骤002过滤提交：`f6278de`。

## 003：次级天空照明与贡献归属

**事前评审：** 当前GI命中次级表面后只求解析灯与emission，纯天光场景缺少天空→墙面→接收面的光。已完成M0的独立能量验证，继续重复采样夹具不增加用户可见功能，应推进该缺口。

**方案：** 沿已验证DI环境bridge及固定SDK的128²等立体角PDF、RIS预采样与统一初始MIS，为GI增加私有小型环境资源与shader-local绑定；不重构并行DI代码。将主表面直接天空miss与次级天空候选独立控制，默认GI不重复DI的直接天空。新增采样设置同时采用不可变dispatch数值快照，避免记录后设置变化破坏资源/签名一致性。

**必要正确性继承：** DI已有证据表明Unity DXC下SDK返回的LightSample可能与stored reservoir不一致；GI次级着色应从已选reservoir重建样本，再做一次可见性，不能先凭返回的另一个候选丢样本。原SDK不改。

**预期验收：** 零解析灯仍有次级天空贡献，恒定天光/遮挡/归属开关、天空revision及设置变化能正确更新或失效；RG/compat均验证，新输入下的raw保持有效，旧GI完整链路回归。结果未通过前不提交生产接纳结论。

**实现：** GI私有128² R32F全mip PDF与16384 uint2 RIS，沿固定SDK预采样/初始MIS；零解析灯也可进入次级天空着色。新增secondaryEnvironmentSamples默认4、includeDirectEnvironment默认false，useEnvironment仍为总开关。启用主直接天空时，有限bounce范围miss增加按需远查询，阻止范围外几何被当作天空。绑定在GI shader局部，RG声明环境/PDF/RIS依赖，compat借用当前相机快照。PassSettings为数值快照，采样设置与Sky revision一并进入历史签名。

**测试与修正：** 首次Linear环境与既有链路五项exit0；Gamma新探针第一请求receiver无效而exit1，保留失败。既有GI三次editor warmup的Gamma实测exit0；新探针采用同样有界warmup，之后仍逐样本严格检查receiver/有限性，没有放宽阈值。补充解析灯+天空混合MIS检查，避免只验证两个独立域。

**正式验证：** `Tests~/RTGI/Environment/run_validation.ps1`：Linear/Gamma × 环境RG/compat四项、legacyGI RG/compat四项、secondary TraceRay两项、DX11 inactive一项，11项实际exit0，pass marker/fresh report核对通过，所有日志shader/C#编译诊断为0。四份环境报告各138请求（3次warmup）、23checks，数值一致；包含零灯、RGB比例、混合MIS、近挡板、1500单位远挡板、天空归属、revision、历史失效和实际Snapshot检查。runner的PID/exit逐项落盘，避免上下文切换丢失tool session后猜测进程状态。

**收益：** 非自发光次级表面在零解析灯时，原实现输出黑；现在ROI bounce约`(.357808,.179135,.088113)`。解析灯单独`(.217788,.216423,.214741)`，混合`(.572891,.393576,.301248)`，相对独立项之和约-.47%～-.53%，通过原5%门槛。阻挡后归零、天空翻倍与两条路径/色彩空间一致；新增用户可用的真实天光反弹，非只增诊断。

**事后review：** 接纳生产接入；原SDK/Donut/DI代码未改，zero outcomes计数、线性HDR/alpha validity及独立输出契约保留。资源增加约.208MiB/相机，RIS每帧更新、天空候选与可见性及可选远查询都有成本；尚无GPU毫秒结论。默认直接天空关闭属于有意的归属变化，已在主文档/Inspector说明。未交付正常相机合成、独立降噪、Player/目标场景或完整动态角色；不将本步11项通过扩称目标完成。下一步接可开关的正常相机GI合成。

**过滤提交：** 仅Runtime/RTGI与Editor/RTGI本步文件、Tests~/RTGI/Environment、Ho-RTGI与RTGI过程/禁试/交接。排除并行RTDI/SSS修改及隔离工程与全量日志/EXR。

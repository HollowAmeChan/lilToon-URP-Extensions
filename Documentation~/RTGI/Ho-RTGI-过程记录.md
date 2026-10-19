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

步骤003过滤提交：`a0a89f6`。

## 004：正常相机GI加法合成

**事前review：** 独立GI与次级天空已验证，正常相机仍看不到GI。应交付可开关、可调强度的场景消费，而非继续只看调试纹理。沿DI已经实测的pre-transparent注入、primary/depth门控、Gamma线性相加和只衰减新增光的fog路径，GI只加其独立diffuse，不添加primary emission、不再次乘albedo。

**范围与预期收益：** Feature保存未覆盖默认值，Volume提供逐相机合成开关/强度；默认关闭合成以保留已有资产行为，启用时直接显示GI。强度只影响合成，不改变采样/历史。先支持既有Mono、全视口、单采样输入；与DI同时启用按Feature顺序分别累加。raw/debug仍保留，后续NRD发布可接同一消费接口。

**必要验证：** 实际camera color与同帧GI输出对照，覆盖开关/0强度/倍数、Volume、主色不重复相乘/emission不重复添加、背景/不匹配receiver/前景depth、fog和透明物顺序；Linear/Gamma × RG/compat。保持独立输出和旧环境链路回归，不能仅凭shader编译宣称合成已交付。

**实现：** 新GI专用加法CompositePass与shader，沿DI既有深度/primary读取、Gamma/fog契约；RG创建当前camera color的后继纹理，compat用scratch避免读写同一目标。启用时把生产提前到同一BeforeRenderingTransparents事件，按入队顺序生产→合成；关闭则保留旧生产时序。Feature保存默认开关/强度，Volume可逐相机覆盖，外观设置不进入采样签名。

**正式验证：** `Tests~/RTGI/Composite/run_validation.ps1`，Linear/Gamma×RG/compat四组各62请求/33checks，外加legacy GI和未合成环境回归两项，6个实际进程全部exit0，fresh/report/marker与shader诊断核对通过。包含两台真实相机独立Volume gain与context、透明混合在GI之后、局部视口绕过、DI之后GI连续消费当前camera color，实际离屏红墙天光反弹已在普通相机PNG可见。

**保留的失败：** 并行DI在本步期间改为Volume显式启用；初次正式联合夹具沿旧默认未启用DI，LastContext=null而失败。保留报告并更新测试显式DI Volume enable及诊断信息，旧GI联合验证同样补明确启用；没有改DI生产策略，也没有以丢弃联合检查规避。新的正式矩阵全部通过。

**事后review与收益：** 接纳。用户现在可在Volume开关/调节GI合成，既有原画面、材质emission与fog保留；新实测camera color符合独立贡献，采样历史不因强度变化清零。阴影/透明归属和输入拒绝有实际覆盖，非仅调试输出。默认opt-in保留已有独立消费者。合成新增一次全屏光栅与目标，compat另有拷贝；没有GPU毫秒结论。当前raw仍有噪声，自动帧动态、NRD/Player/目标场景验收仍待推进。

**过滤提交范围：** GI Runtime/Editor本步合成与Volume/GUI、legacyGI测试显式DI启用、Tests~/RTGI/Composite及RTGI文档；排除所有并行DI改动。下一项独立GI降噪与实际信号/命中距离合同。

步骤004过滤提交：`82df540`。

## 005：GI独立RELAX降噪

**事前review：** 场景合成已验证，但raw反弹仍有噪声。采用固定NRD4.17.3的原版RELAX_DIFFUSE，先查询本机CPU库真实instance/pipeline与资源布局，生成对应Unity wrappers；不复制DI private shader/body或默认为BOTH浪费specular资源。复用已核对的native ABI与通用dispatch/pool实现时保持DI默认行为不变。

**信号与验收原则：** Final在乘primary albedo前直接生成demodulated diffuse，保留独立raw，不反除零通道；距离来自实际GI lobe/次级命中并区分远处天空与invalid。每相机独立NRD实例/池/历史，正确绑定原版normal/roughness/viewZ与静态世界motion契约；RG/compat及正常合成都消费filtered publication。必须有原版同输入基线、均值/颜色、时间噪声及变化响应证据，再接纳生产；不以进程启动或原型布线当画质收益。

**实现：** 固定DLL实查RELAX_DIFFUSE type10，生成15个原版DIFF-only wrappers、6 permanent/4 transient池manifest；共用ABI/dispatch只增加可选类型/资源表/显式参数，DI默认路径不变。每相机独立实例、signal/normal/viewZ/output/filtered；Final直接生成乘primary albedo之前的信号，真实GI命中距离、原版signed-oct normal/roughness与正viewZ。固定64单位转换减小FP16 moments压力；按非缩放时间换算原版历史预算，静态世界使用相机矩阵/零world motion，保守签名变更reset。Settings/Volume denoise默认false，raw保留并新增RawDiffuse调试/RG/global，正常合成使用当前publication。

**真实验证：** `Tests~/RTGI/NRD/results/run.json`汇总7项全部实际exit0/marker通过/编译诊断0：Linear/Gamma×RG/compat四组各243请求/41checks，DI既有RunDenoise完整门槛及raw GI合成/环境三项回归。四组数值一致：raw RGB(.360382,.180422,.088747)→filtered(.364358,.182411,.089711)，红通道逐像素时间方差.1141095684→.00000390791；HDR天空1000的均值差约+.7%。实际覆盖零albedo不丢incoming通道、首暗帧清旧亮、camera矩阵移动、独立相机、invalid、Volume/开关资源与resize。PNG/报告及源hash见该目录README。

**保留失败/隔离依据：** 首轮33位GUID导致Context不导入、CS0246；修正后发现缺URP using（修正）及并行VolumeLight施工编译错误。未改他人文件，改用base82df540加95份GI/API明确覆盖的隔离package；两轮snapshot SHA逐项一致并与当前源码核对。git archive的旧ShadowCast合同CRLF触发既有validator，只有隔离副本转LF并记SHA；report保留。DI RunConvergence进程实际exit0但runner误配通用PASS marker而拒绝，保留run/capture，改跑已有RunDenoise严格门槛通过。旧同名smoke/Convergence log后来覆盖，旧SHA不可对当前同名文件验真；后续runner已增加时间戳，禁止隐去该限制。

**事后review与收益：** 接纳为可选原版GI降噪基线，均匀天光反弹夹具方差降低约99.9966%，均值差约+1.1%，raw可直接对照，DI回归未退化。新增约79B/像素+tile+192KiB，1080p约156.4MiB；尚无实际GPU毫秒。均匀夹具不是复杂空间质量，手动请求不是自动帧动态；任意HDR、运动角色、目标场景/Player与当前共享工作区整体验收仍未完成。后续优先补真实自动帧/空间质量与预算，不直接用更多历史或DI私有参数继续压噪。

**过滤提交范围：** Runtime/RTGI、Editor/RTGI、本步两份共享NRD API文件、Tests~/RTGI/NRD、Ho-RTGI说明及RTGI过程/禁试/交接。所有VolumeLight及其他并行文件、隔离package/工程和全量EXR/log排除。


步骤005过滤提交：`95120a4`。

## 006：真实应用帧与空间质量 / Player前置夹具（进行中）

**事前review：** 步骤005在均匀反弹和受控RenderRequest时序中确认降噪基线；尚不能证明真实自动帧、非均匀空间细节或Player资源可达。优先补证据，不预设需要改变参数。复用DI历史中真实frameCount/Time、实际end-of-frame读取和序列化Shader引用验收原则。

**实施边界与预期收益：** 在专用GI工程创建独立测试scene/pipeline/assets和runtime probe，以自动camera渲染；使用离屏红/蓝emission面产生空间渐变，测raw/filtered同帧均值图与时间方差，验证camera运动、emission关闭、几何移动、Volume与denoise/resize响应。runtime probe只放Tests~源并复制专用工程，不加入产品runtime流程。尽量复用同场景进入独立Mono D3D12 Player，以实际加载/dispatch证明serializedShader/NRD/native/composite可达。全部入口记录真实frameCount/时间/进程/来源。

**判定与搁置条件：** 同一应用帧不能重复算时间样本；先用同次raw时间均值作降噪形状对照，明确有限采样限制。保守几何revision每移动帧reset是当前策略，不能伪装为动态表面motion；记录其收敛代价。有空间退化先定位实际输入/阶段，不延长历史放宽门槛。Player失败保留并按具体资源/ABI处理，不改系统D3D12或重编已有固定库。

**DI过程补充与验证模式：** 已读DI Player Composite说明与hidden-host失败：隐藏/批处理宿主会推进Update但自动camera可能零事件，不能重试该条件充作通过。本轮先在Mono Player每个真实Update仅一次SubmitRenderRequest，frameCount/time严格递增；这可验应用帧历史和打包，但不声称正常enabled-camera自动呈现，后者仍独立待验。

**首轮Player失败与定位（2026-10-20）：** 隔离包首build实际exit0/errors0，Player却有m_Script缺失/布局不符；探针Feature为null，清理二次NRE未退出，核对PID34808与本任务EXE绝对路径后结束，真实exit=-1，报告保留。脚本审计确认GI/GB/SB/Volume在快照中MonoScript.GetClass/FromScriptableObject均为空，单次全脚本强制重导入仍为空。并行VolumeLight此时已过滤提交76210e7/a525c36，专用工程改回实际工作区；同四类脚本映射正常、编译无error。暂缓快照导入唯一根因，禁止原样再build；构建helper加入脚本绑定强制检查，runtime probe清理null安全/首帧依赖检查，并加入camera color真实filtered合成断言后重建。隔离工程manifest当前已回实时工作区，步骤005历史snapshot证据未改。

**夹具修正与拒绝证据：** 白色unlit raster实际为.2158203，原“必为1”的假设导致首个有效Player合成门槛失败；改为前一真实未合成帧基准，不改产品增益。下一轮空间/响应至268帧已通过，Volume off失败；沿DI既有Player Render helper补disabled-camera请求前的VolumeManager.Update，生产Volume未改。两份report和旧run SHA保留；重复字节报告去重并有别名映射。去除red后raw/filtered实际均0；LogLuv的近黑通道按绝对误差，不据相对绿通道差异声称物理退化。

**正式验证与事后review：** Tests~/RTGI/Dynamic/formal-run.json，两套Win64 Mono release build和两个实际D3D12 Player全部exit0。Linear RG/compat各304次camera request、299条有效真实应用帧记录、16checks；绝不把它称enabled-camera自动呈现。静态非均匀emission反弹的时间均值空间图L1分别.103438/.103524，raw方差.0724847→filtered .00007721/.00007768（降约99.89%）；红/蓝均值比约.970/.970，原5%门槛通过，绿近黑按绝对<.001。camera移动59帧保留矩阵重投影，关闭red首帧0，移动几何24帧全reset，Volume/denoise实际切换通过，camera color对实际raster+filtered误差约.000073。

**接纳/成本/限制：** 接纳本步为部署/应用帧证据，没有新增产品算法参数或Runtime改动。实际源码MonoScript绑定/serialized shader/15个wrapper/native DLL、正常合成的数据路径已在Player运行；完整payload SHA、来源源码SHA与每帧时钟均核对。原版降噪在空间不均匀信号下仍有明显收益，约3%均值差与10.35%形状L1需保留，不能据此称无偏或复杂细节已达标。正常自动camera、薄墙/小高亮源、骨骼/Cutout、GPU预算和完整Player矩阵仍待验收。构建三条非GI shader警告及Player GB fallback/resolve不可达、退出4条GraphicsBuffer GC清理警告均记录，不能声称全部域/完整生命周期通过。快照MonoScript唯一根因在一次重导入无收益后搁置F19，实际源码可推进。

**过滤提交范围：** Editor/RTGI/HoRTGIPlayerValidation.cs/meta、Tests~/RTGI/Dynamic及RTGI说明/实施规划/过程/禁试/交接。排除Atmosphere/VolumeLight并行变化、隔离工程/Player二进制/全量log。后续优先实际场景输入支持与GPU预算，避免继续增加同类均匀夹具压噪。

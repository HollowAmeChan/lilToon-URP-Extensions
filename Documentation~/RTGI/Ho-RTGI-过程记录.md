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


步骤006过滤提交：`035b9f9`。

## 007：GI按材质排除合并天空壳（进行中）

**事前review：** 已读DI第80步材质排除专题和实际古堡支持记录：天空壳与主体共用Renderer，按layer排掉整个对象会损失主体；DI已有Material对象身份列表及共享HoRTScene逐submesh过滤，GI仍未传入。该缺口可直接影响已有场景，不需要改变采样/降噪。沿现有接口增加GI独立默认空列表，高级Inspector可配，保留源mesh/material/raster。

**Cutout边界复核：** GI当前native secondary closest-hit没有any-hit；共享inline查询显式CULL_NON_OPAQUE，DI另有native最终visibility。不能只把SupportsCutout传给GI registry就宣称接入，否则secondary/重连/次级灯可见性路径不一致、可能漏遮挡。Cutout需后续单独完整路线，本步不半接。

**验收与预期收益：** 一个真实合并双submesh包含前遮挡壳和后emission面，验证只排壳后raw/filtered/正常camera增加反弹，保留面仍存在；同名材质按身份区分，恢复/空/重复null列表、retained identity、history reset与源资产不变；完全排除skin不Bake仅作为registry覆盖，不冒称骨骼GI材质全部已验。Linear/Gamma×RG/compat实际路径，沿已有Composite夹具专门入口，不泛增新的Feature或NRD参数。

**实现与验证：** GI独立默认空Material[]，高级Inspector编辑，并传已有scene.Synchronize第三参数；共享registry/SDK/NRD/HLSL未改。正式四组各12请求/21checks、完整空列表Composite和Gamma NRD compatibility两回归，6实际进程全exit0、fresh/marker与编译诊断0。合并前壳令GI为0，只排壳后raw约(1,0,.001)，filtered/正常camera同步增加；恢复首帧清除旧红。同名不同引用、null/重复、保留identity与恢复新identity、完全排除skin不Bake、源对象不变均通过，说明见Tests~/RTGI/MaterialExclusion。

**拒绝与修正：** 首轮含空间重采样，却把单像素值当常量解析期望，.928而非1导致exit1；报告保留。专门夹具关闭空间复用、保留temporal/NRD，.03门槛未放宽，生产算法/默认/增益没改。空间统计质量继续由独立步骤006负责。

**事后review：** 接纳。用户现在可在GI单独过滤合并天空壳，不丢同Renderer主体，也不改raster/源资产。代价为共享已有逐帧身份过滤/HashSet逻辑，空列表不建HashSet；部分排除仍不缩小合并skin snapshot，不能从少实例推断GPU/VRAM节省。过滤变更需要清历史，等价列表保持历史有实测。没有重新验证非空GI Player列表或完整古堡/GPU预算，边界保留；Cutout完整路线仍需后续，不能半接predicate。

**过滤提交：** 只含GI Settings/Feature/Inspector、Composite专门入口和新增partial测试、复用runner的可选MaterialExclusion模式、该步报告及RTGI文档；不改共享DI/Scene和其他任务文件。


步骤007过滤提交：`20449c3`。

## 008：基础Cutout完整GI命中与遮挡（进行中）

**事前review：** 规划M3明确要求基础lilToon Cutout，DI实际古堡16个submesh中12个为Cutout；仅完成天空壳过滤还不能称实际支持域充分。已逐项核对：GenerateSecondary及远天空查询FORCE_OPAQUE，ShadeSecondary的灯/天空visibility使用inline保守跳nonopaque，Final重连也用该保守query。必须同时补全三处实际alpha语义，不能只开registry predicate。

**实施路线与边界：** 沿DI现有基础Cutout classifier/静态UV0+Main alpha/AlphaMask0～4/背面UV规则；GI私有any-hit拒绝孔洞，native初始命中移除FORCE_OPAQUE。有Cutout时用同一份次级采样求值函数的native ray-generation分支测试次级灯/天空，新增最终native visibility/distance记录后由原Final/NRD消费。无Cutout保留现有compute照明/inline Final，不复制SDK/NRD算法或调参数。visibility-only payload采用私有输入状态标记，closest-hit只回遮挡/距离，不做完整lilToon材质求值；公开payload输出/secondary48B/validity协议不变。

**验收：** 初始alpha0/1及纹理孔洞/UV/mask、次级解析灯和天空被cutout挡/透、重连最终可见性（包括NRD距离）、远天空范围、恢复/texture update/history、无Cutout资源释放和opaque回归；两路径/两颜色空间及必要Player。只有完整路径可运行且能量/遮挡门槛通过才接纳；失败保留，不能用保守inline假装实际alpha。骨骼vertex fetch随后独立验收，不据registry登记完成。

008实现复核补充：时间/空间bias correction有Cutout时也使用同一份原SDK函数的native wrapper，避免其保守inline跳alpha与初始proposal不一致；opaque仍原compute。共享改动仅抽出原样HoRTSceneHit类型，以及native BuildAndBind补同等AS state向量（原来只绑定AS），不改registry/DI算法。首轮缺AlphaMask数值uniform编译失败已修，opaque材质过滤21checks回归过；单射线Cutout/mask/UV/backface/visibility-payload测试在修正由opaque转换材质的RenderType标签后实际exit0，尚未验证完整GI照明/复用链路。

**正式Editor与实际Player：** 10个Editor进程全部exit0/marker/fresh通过、shader/C#诊断0：两颜色空间单射线、四组完整链路（每组284请求/17checks）及opaque环境两路径/opaque材质/DI native Cutout回归。1024条冻结有效后墙点的独立CPU线段/实际mesh UV/nearest判断与最终buffer及NRD距离全一致，512条遮挡raw无漏光；冻结注入不参与后续能量测量并清历史。恢复正常原SDK复用后，半开孔洞raw四组均.4869144，对独立.5积分误差-2.62%，原5%通过；filtered约.4873～.4891。次级天空/方向光透明时匹配参考，实体时归零；远1500挡板与孔洞正确，原型无额外gain/clamp。

两个Linear D3D12 Win64 Mono release build与实际EXE全部exit0，各309请求/304有效真实应用帧/22checks，serialized非空天空材质排除、native Cutout、首帧alpha变化、NRD清历史/恢复、最后Cutout资源释放、opaque回切和原动态/合成检查通过。每Update一请求仍不称enabled-camera自动呈现。完整输入/报告/payload SHA与GUID经Cutout/verify_validation.py核对。

**保留失败：** 黑albedo/F0=0的初始Cutout在天空下约.0013并非miss；GPU包确认flags1、z=-.5、diffuse/spec RGB0、emission0，原GGX的Schlick擦边项仍在。原黑色必须零断言错误，替换为所有中央射线真实命中/位置断言，没有改模型或放宽shadow能量阈值。并行Dust有编译错误那轮没有fresh report，未当新GPU结果；Editor正式用base82df540加124覆盖的不可变快照，Player在实际base6be5bd9工作区构建，MonoScript绑定已检查。两类来源有各自hash，不冒称相同整个工程。复用runner旧step006标签保留raw metadata，canonical只修正步骤编号，未改报告/退出。

**事后review与收益：** 接纳基础Cutout完整路径。初始、次级灯/天空、重采样bias、最终真实alpha一致，避免只登记非opaque后各阶段漏遮挡。共用同一份应用/原SDK估计器与重采样函数，vendor/NRD参数未改；opaque维持原compute/inline。可见性专用payload输入sentinel只跳过昂贵材质求值，普通输出结构、secondary48B和公开RGBA validity不变。新增有Cutout时8B/像素（1080p约15.82MiB）buffer和一次dispatch，hit-group/alpha取样与原生复用成本尚无GPU毫秒。静态基础域接纳，不扩成动画alpha、MPB、多主色/dither/dissolve/视差；骨骼GI命中、实际古堡、正常自动camera及GPU预算仍需交付。既有非GI/GB/退出GC警告保留，不称完整生命周期通过。

**过滤提交：** 仅GI Runtime/Editor、本步共享HoRTScene state绑定及原样Hit类型拆分、Cutout证据、复用Dynamic/NRD runner扩展与RTGI文档。当前Atmosphere/VolumeLight提交和改动保留，不纳入。下一项优先实际骨骼vertex fetch与场景、有效GPU预算，不继续单纯堆静态降噪门槛。


步骤008过滤提交：`6ebe065`。提交后复核只包含本步GI/必要共享接口/测试/文档，其他模块未纳入；Cutout verifier仍通过，当前所有测试进程结束，目标继续active。


## 009：GI骨骼快照真实材质命中与反弹（进行中）

**事前review：** DI/共享registry已证明骨骼、BlendShape和非均匀/镜像距离；BakeMesh(true)有既有实测依据，不能凭API名字反改。GI还需证明当前姿态mesh的UV0/UV1、normal/tangent、emission实际vertex fetch正确，以及遮挡/反弹和姿态清历史。先补独立线性蒙皮/变形几何对照，不先改生产代码。

**边界与门槛：** 单/双骨、混合权重、刚性骨骼旋转/平移、BlendShape、renderer非均匀/镜像变换；用独立矩阵、原始mesh参数与光线目标推导距离/法线/UV颜色。再以离屏skinned emitter/caster的实际GI、Cutout/NRD history和源mesh不变验证。稳定姿态不得反复Bake；动作变化保守reset不标称动态表面motion。两颜色空间/路径与必要Player，故障保留再定位，不能只凭进入AS勾完成。

**正式证据：** 六个Editor入口全exit0/marker/fresh/SHA成功、shader/C#诊断0。Linear/Gamma材质命中各140checks，包含独立矩阵、混合骨权、Morph、负行列式镜像、UV0主色/UV1 emission及切线normal map；完整四组各187请求/25checks。离屏蒙皮emission约(1,0,.001)，Bone/Morph移动后首帧raw/filtered=0、恢复重亮；opaque/Cutout skin roof的真实次级天空遮挡、非readable源、缓存/无效姿态移除均过。

两个Linear D3D12 Mono Player实际build/EXE全exit0，各315请求/310有效应用帧/30checks，序列化非readable skinned emitter/caster、Bone/BlendShape/invalid/restore首帧响应及已有Cutout/材质过滤/动态/合成继续通过。每真实Update一次请求，不标称自动camera。来源为独立base fa64cc6+128覆盖的Editor快照及实际工作区Player，各自hash/payload/帧号经Skinned/verify_validation.py核对。

**事后review与收益：** 接纳已有生产能力的支持域确认，没有改Runtime/共享BakeMesh/Shader/SDK/NRD，不为测试改参数。与DI已有true-scale实测一致，GI实际属性读取没有新缺陷；稳定pose复用、源mesh未改。代价/限制仍为CPU Bake、姿态变更保守reset，未测真实GPU成本，也不等于完整角色/场景/动态接收通过。没有新的有效生产失败；已保留构建/GB/退出GC既有警告。下一步转实际场景与有效GPU记录，避免继续堆同类小夹具。

**后续取证准备：** 本机RenderDoc技能已读，CLI在D:/Unity_Fork/renderdoc-mcp/bin，可通过仅子进程PATH调用；当前help/MCP没有duration接口，统计数量不当耗时。DI此前隐藏自动camera零事件、GPU时长零样本、无capture记录已读，不同条件不足前不盲重试。只读定位RTInputValidation的血月古堡依赖副本，此前观察到PID49064在使用该工程；后续须查当前状态，不操作别的任务进程/资产。

**过滤提交：** 仅Editor/RTGI骨骼验证/Player fixture扩展、Tests~/RTGI/Skinned、复用Cutout/Dynamic/NRD runner及RTGI文档。其他Atmosphere/VolumeLight未提交改动保留，生产Runtime未变。


步骤009过滤提交：`4ccd02b`。提交后verifier复核通过，生产Runtime无diff；真实场景/GPU阶段继续，目标active。


## 010：实际血月古堡画面与GPU取证准备（进行中）

**事前review：** 008/009已验证基础Cutout/骨骼真实路径，不能以小夹具替代实际场景。DI第86步已有复制古堡、天空材质过滤、SceneSkybox、克隆源相机的成熟读取流程；只读复用其依赖副本，在本任务RTGIValidation项目导入，不占用或改写活跃RTInputValidation。先展示真实scene的GI off/raw/filtered/加法及支持/拒绝清单；同一支持域/曝光/相机，不把画面不同归因于收敛。

**部署与取证：** glTF依赖通过GUID/URI闭包复制到自有工程，保留meta和哈希；必要UnityGLTF包复制到自有Packages，源侧只读。只创建临时pipeline/相机/Volume，不保存源场景。有效GPU时长仍单列，RenderDoc统计不当duration、CPU不代GPU；正常自动camera/呈现需要实际事件验证，不复刻已失败隐藏条件。先完成可审查的场景结果与采集夹具，再决定必要的交互运行条件。

**实际接入：** GUID/URI闭包54文件49,941,388 bytes及私有UnityGLTF2.19.5复制，源/目标/包SHA完整；运行后两端均未变。128²/256²×RG/compat四项正式actual exit0/marker/fresh通过、编译诊断0，15 RT实例/12Cutout/1pose、18灯；唯一排除天空，无材质拒绝。128/256分别全部16384/65536像素有效，15340/61390像素GI非零，默认.25合成变化11692/46793像素。32预热后static无额外Bake，原相机/SceneSkybox已取图。只接入通过，不是质量通过。

**发现质量失败：** 增加逐通道时间均值门槛（原5%）后，64额外预热/64窗filtered约低9%，实际exit1。256额外预热/256窗仍(-5.95%,-6.54%,-7.03%)。源输入还原红均值.001492365对raw .001492383基本保持；Prepass .001478197、Temporal .001384652、Fix .001463385、Clamp .001383324、末Atrous .001353189。明确不以max-channel单帧均值当RGB能量，也不把PNG显示曝光×100当实际增益。

**两轮有界假说/搁置：** 据DI稀疏可见样本色盒经验，仅在Editor已有ValidationSettings测试fastSigma4/6，长窗分别(-4.41%,-5.18%,-5.68%)和(-4.13%,-4.95%,-5.42%)，两次实际exit1。仍未满足原门槛、边际收益小且噪声增加，搁置更宽盒，不再试8/盲加history/补gain。Runtime原版fastSigma2与所有Shader/NRD参数未改，qualityQualified=false。五份被拒绝报告及实际退出/SHA都保留；不是已接纳优化。

**事后review：** 接纳真实场景接入/取证工具和失败发现，拒绝filtered画质接纳。它改变后续行动：不再以小夹具稳定性代表目标场景，质量缺口不能掩盖。原模型/NPR/源资产保持，实际贡献弱需按数据判断；GPU毫秒仍缺失。CLI/MCP只给数量统计，需新的官方D3D12 timestamp条件，先以fence/频率/空区间/已知负载验证，不能复刻DI零样本或用CPU耗时。正常自动camera/场景漫游仍独立待验。

**过滤提交：** 仅Editor/RTGI/HoRTGICastleValidation.cs/meta，Tests~/RTGI/Scene、RTGI说明/计划/过程/禁试/交接；用户场景/依赖/二进制只在隔离工程不入仓，其他模块不纳入。下一阶段优先取得有效GPU分段与显存/分辨率成本，同时保留这项真实质量失败，整体goal不完成。


步骤010过滤提交：`010b8cd`。提交后verify仍确认四项功能真实通过、五项质量真实拒绝，qualityQualified=false；无产品Runtime修改。整体目标active，后续GPU取证不得掩盖质量缺口。


## 011：独立D3D12 GPU timestamp资格验证（进行中）

**事前review：** 目标仍缺有效GPU成本。DI已证明隐藏请求/FrameTiming与实际sampler可零GPU duration，RenderDoc接口无duration；不复刻该条件。Unity6000.3官方PluginAPI提供当前commandList、queue频率及frame fence，可在同一队列记录真实timestamp。先做隔离test-only桥，不创建device、不接NRI/DLSS、不改NRD二进制/系统或产品依赖。

**合同：** ConfigureEvent明确queue Allow与command-recording DontCare线程域；query写入当前Unity commandList，GPU Resolve到READBACK，只有结束事件+非零目标fence已完成、时钟频率有效、begin/end新鲜且有序才接受。先空区间与不同已知Compute负载资格验证，失败用真实缺失/exit1，不CPU代替。资源/指针待GPU完成后释放，限定自有GI工程Plugins。资格过后才挂GI阶段，记录instrumentation空区间/开销，不把单pass差值或回放统计当总帧成本。

**实现与资格：** 自有test-only C++插件使用官方IUnityGraphicsD3D12v7当前commandList，不创device/截descriptor/NRI。Native Resolve延后camera外，避免raster render pass内ResolveQueryData。24样本空/128/2048迭代Compute，空median0、短约.006ms、长约.041ms，频率1GHz、每slot非零起点/有序/fence完成。初版严格end>begin拒绝合法empty，真实exit1报告保留；修正为只empty允许量化零，非空仍正，正式exit0。最初DXGI include/UTF8编译问题修正，官方header未改。

**实际分段：** Runtime只加UNITY_EDITOR条件测试回调与标记，Player调用编译去除。资格、256RG/compat、512RG/compat、256Total-only、强制AS/PDF重建7个实际进程全exit0/marker/fresh/SHA/编译诊断0。每普通case40帧/marker40 fenced样本。生产Total（含NRD不含composite）median分别1.18784/1.360896/3.991552/3.926016ms，composite .016384/.019456/.023552/.03072ms；Total-only1.191936ms对全marker1.18784接近。稳定AS无build，marker可量化零；强制重建frame0 AS2.694144ms、PDF.016384ms。p95/完整tick与fence都保留。

**隔离复核与Release：** 发现默认AnyPlatform导入且Linux/OSX仍enabled，配置明确禁全部Standalone、仅Windows Editor/x64。独立Win64 Mono release（不覆盖历史Player目录）build0、实际315请求/原Skinned/Cutout/动态/合成全部gate过，payload不含HoGIGpuProbe.dll；额外DLL不进产品仓库目录，HoRTDINRD未改。Editor配置/Release日志有实际完成证明，既有GB/退出GC/非GI警告不遮盖。

**事后review：** 接纳GPU取证能力，取代先前缺失数据；不是采样/NRD优化，不改变010质量失败。测得空间复用、secondary、Generate为主要GPU段，512相对256显著增长；带插桩Editor graphics-queue区间而非完整Player presented-frame，不把阶段median相加或CPU扫描/Bake算进Total。显存/高分辨率/全域半GI重建/正常自动camera与原质量5%仍缺失。空标记开销不盲扣，host其它GPU活动和p95波动保留。下一步按实测成本推进分辨率/显存及实际应用条件，不重启宽色盒路线。

**过滤提交：** 仅Runtime/RTGI三文件Editor-only hook、Editor/RTGI计时sidecar/Shader/古堡测量与隔离Release入口、Tests~/RTGI/GpuTiming源码与实测报告、RTGI文档。没有产品nativeDLL、NRD/vendor/header更改或用户资产；其他任务文件保留。


步骤011过滤提交：`63db17e`。提交后GPU verifier再次通过；产品算法/Shader/NRD不改，Editor-only hook与独立probe已隔离Release。GPU成本取证接纳，010质量失败保留，目标active。

## 012：实际资源清单与1080p成本（进行中）

**事前review：** 011只有256/512整相机尺寸，不能据此宣称1080p或半GI已交付。先在同一古堡独立工程读取实际GraphicsBuffer count/stride、已创建RTHandle的格式/尺寸/mip、RELAX permanent/transient/constants、场景实例映射、AS的Unity GetSize（若不可用明确unknown）和自有pose mesh的Unity运行时内存。对象去重，输出逐资源清单；描述符payload与Unity统计不称物理驻留VRAM，不含借用GB/SB、源mesh/材质/纹理、driver heap或临时AS scratch。

**验证与边界：** 新独立输出目录保留011历史；256/512及1920×1080 RG/compat分别实跑，沿用已通过资格的fence/timestamp桥，仍40样本。核对像素有效性、真实Cutout/骨骼/合成、稳定pose无额外Bake、源码/日志/报告SHA和实际exit。1080p使用正常16:9投影，与旧方形视野不同，不能把差值只归因分辨率。没有产品参数/算法变更，不做质量成功宣称；010能量失败及F27搁置继续保留。

**后续决策门槛：** 只在拿到清单与实测后设计半GI，必须包含全分辨率GB/SB代表像素、深度/法线/validity边缘重建、历史/屏幕坐标映射及原能量/动态/细节门槛。仅缩dispatch或相机不接受。正常enabled-camera与完整Player预算仍独立待验。

**实际证据及拒绝：** 新Editor审计器读取28个去重对象。256/512两个进程52360/56712实际exit0、编译诊断0、40 GPU samples/marker及fence通过；自有GI+NRD+instance payload16.588/65.151 MiB，AS Unity GetSize均16.523 MiB，pose Mesh Unity运行时统计28.539 MiB（不称GPU驻留）。NRD实际79B/pixel+tile+192KiB constants。1080p同布局推算513.997 MiB，其中reservoir191.25、NRD156.421；没有HD成功分配/呈现结论。

1080 RG PID44476在GPU clock同步出现连续Unity D3D12Fence::Wait；核对仅自有GI projectPath/PID后终止，真实exit−1，无fresh report，未跑compat。最初审计器读GraphicsBuffer.name的setter-only导致编译exit1，修正为owner field标识，日志及失败源SHA保留。步骤012的run/host observations/rejected compilation均在Tests~/RTGI/Resources；verifier明确actual1080Qualified=false、stableGpuBudgetQualified=false、filteredQualityQualified=false。

**事后review：** 接纳逐资源取证和分辨率需求确认，不接纳HD/稳定成本。256/512本轮Total median24.969/117.351ms、p95 138.807/125.692ms，显著异于011；当时整卡约7.3～7.6/8GiB，但瞬时观测不足证明唯一根因。Runtime/Shader/NRD相对011完全相同，不伪称算法退化或以此线性估1080时间。保持其它应用/进程，未改驱动/TDR。F31明确新条件前不原样重试；runner后加仅自身fence中止/15分钟上限、HD显式记录新条件，只有语法复核，不冒称新控制实跑。

**收益与下一阶段：** 清单把主开销定位为3份reservoir和NRD；可以据此设计半GI，但没有生产优化收益。新设计文档记录参考render/rect/output及前帧scale合同、DI尺寸验收边界，以及2×2代表full pixel与GI中心相差.5 full pixel的关键错误风险。下一步先独立验证映射/历史/NRD投影，再接重建，默认full不改。010能量失败、正常自动camera和完整Player预算继续。仅Editor取证改动，无理由重复相同Player构建。

**过滤提交：** Editor/RTGI/HoRTGIResourceAudit.cs/meta与Castle矩形尺寸/新入口；Tests~/RTGI/Resources（含原运行脚本归档和失败证据）；RTGI说明/计划/过程/禁试/交接/设计。无Runtime、native binary、vendor、源场景或其他任务文件。所有本任务进程结束。

步骤012过滤提交：`ca3797f`。提交后Resources verifier再次通过、Runtime无变更、index空，仅保留其他任务未跟踪文件。HD/稳定预算/filtered质量仍false，半GI未实现；下一项013先实际验证输入/历史/NRD映射，goal继续active。

## 013：半GI输入网格和投影合同（进行中）

**事前review：** 012确认资源需求，但当前RAB、Final history、Temporal与NRD仍把GI pixel当full primary pixel。2×2固定代表点必须与规则grid中心补偿一致；没有这项证据直接缩dispatch会读错误surface/旧worldPos。先实现GI私有可复用网格helper（full默认、偶数尺寸2倍、奇数/过小明确full回退），不改DI公共语义，不暴露尚无重建的半GI产品开关。

**实际验证范围：** 在自有RTGIValidation运行小尺寸真实GB/SB和GPU Compute，读取映射后的实际primary及设备深度；CPU用独立ViewportPointToRay与两块变换平面求交，验证worldPos、normal和代表UV。当前/前帧真实深度重建、Temporal投影和NRD补偿后的投影往返独立比较；覆盖透视、正交、非中心投影、相机平移/旋转、full/half与尺寸变化、奇数和边界。低分辨率避免012 HD/fence条件，若再出现GPU fence错误立即中止自身进程。该项不是NRD质量或半GI重建验收；通过后才将同helper接GI桥和后续全域重建。

**实现和正式验证：** Runtime新增私有HoRTGIGrid及HLSL helper，固定fullPixel=s×g+floor(s/2)、UV偏移(s−1)/2 full pixel；逻辑projection row0/row1减row3×2offset/fullSize。偶数且至少2×2才scale2，其他明确full，输入尺寸/scale变则历史拒绝。尚未接生产GI，不暴露半GI开关。Editor新GB/SB Compute审计，RGBA矩形解析quad双层求交，不用GI输出做真值。

最终exec24368终态已收：RG37868、compat50528实际exit0/编译诊断0/fresh/marker/SHA通过；每条12受控请求、18190receiver/5961previous depth/1443foreground。最大world .001916（8×1 full极端宽视野，half <1e−4）、normal .006913、depth .001686、primaryUV8.43e−8、gridUV3.21e−7、previous物理/NRD world7.15e−7/7.54e−7、current→previous UV1.48e−5，全过原门槛。漏NRD投影补偿的负对照各half最大.052～.108world误差；漏primary映射3.75～4.09，确认原.002门槛能检出。Full零偏移保持一致。

**失败与夹具复核：** 首轮52740和诊断轮40264各actual exit1；唯一错误是最后full移动像素(18,31)CPU选back、GPU选foreground。独立double求交得foreground local x−.5000122，距投影边缘.000192px，小于1/256。最终只在该窄轮廓带允许两个独立ray/plane候选，仍测全部位置/深度/法线，不跳过或放宽.002。每条仅2点在带内，其他18188点保持唯一候选。两次失败run/report/logSHA和全部执行源码逐字节归档，edge-analysis.json及F32保留，不伪称修复生产映射bug。

**事后review与收益：** 接纳坐标合同和可复用helper，使下一步半GI不靠猜中心偏移；没有生产质量/性能收益，不能当半GI完成。受控请求不是正常自动camera/实际应用帧motion；只验证NRD投影合同，没有运行NRD过滤器。无AS/native计时桥/重建/生产采样参数变更，不原样重跑012HD。Grid verifier已核对两正式run、两失败、源/log/report/hash/门槛/负对照，生产half=false、NRD质量=false、HD=false。

**下一动作与过滤提交：** 同helper接GI私有RAB（保留DI原语义）、初始native、Final history、Temporal和NRD投影，然后全域depth/normal/validity重建与原能量/细节/动态门槛。默认full；奇数full回退和尺度变更reset必须实际走生产验证。仅提交Runtime这两个新helper/meta、Editor Grid审计/meta、Tests~/RTGI/Grid及RTGI过程/禁试/设计/计划/交接。010实际能量失败、012HD/稳定预算和完整Player/正常camera仍待交付。

步骤013过滤提交：`4950433`。提交后Grid verifier再次通过，012资源verifier仍过。所有进程结束、index空；下一项014转生产GI适配与完整输出重建，半GI功能/画质尚未交付，goal继续active。

## 014：GI实际网格适配与全域重建实验（进行中）

**事前review：** 013已证明映射数学，必须转实际GI而不是继续纯夹具。先以仅Editor非序列化实验入口选择half，产品默认与Player仍full；同一Runtime链接Generate、GI私有RAB、Final depth/history、Temporal和NRD projection，DI公共代码及NRD原参数不变。full发布raw/filtered合同不变，half内部raw保留demodulated，独立全域输出按目标full albedo乘一次。

**重建假说/有界门槛：** 四邻居按深度/共面/几何与shading normal筛选，重建demodulated后乘目标full albedo。无兼容代表样本时保守零贡献、保留receiver validity，绝不能拿背景填前景；这只是明确实验行为，薄结构缺失即质量失败。先实际uniform/跨材质、前后景薄边和孔洞对照full raw及独立解析期望、原5%能量/动态首帧reset，测资源收益；不把小夹具等同实际古堡画质。两轮若无可接纳收益则搁置half，不把实验入口当产品交付，回到目标质量/正常相机/完整预算。012HD保持禁试，010能量失败独立保留。

**实际实现与执行：** 七个Runtime文件实现上述实验完整GI适配：013 helper贯穿初始ray、GI独立RAB（原DI函数仅rename后不使用，DI源未改）、低网格history stride/full depth UV、Temporal UV减偏移、NRD逻辑projection补偿；空间半径按scale换GI单位。half内部raw为demodulated，RawDiffuse/PublishedDiffuse为额外full outputs，depth/normal四邻居重建后按目标albedo乘一次。默认full，half仅Editor非序列化入口，没有产品Inspector/Volume/Player开关。

初轮48012重复F23的夹具错误：空间复用单帧当逐像素能量，full raw最大1.996提前exit1，报告/source/log保留rejected-single-frame。只在夹具关闭spatial、保留temporal/NRD隔离解析信号，没有改生产参数或.05门槛。正式exec92640已收终态，RG10316/compat32560各169请求实际exit1/编译诊断0/fresh/marker/SHA，无fence错误；functionalPass=true但qualityPass=false。

**收益与画质失败：** 平面跨红蓝albedo raw最大误差.00781，两路径filtered full .00879/half .00928；历史切换reset、stable reuse、full发布与.25合成、65×63 odd full回退/释放half输出通过。实际payload 1227104→521556B，减少57.497%（含双full输出，不是总VRAM/GPUms）。但偶数列x8、半个full像素宽的独立白色前景在低网格无任何代表点，30个内部receiver full raw≈.9921875、filtered≈.991927，half二者全0。validity保留、depth拒绝正确，却明显丢失真实GI，原5%/局部最小值gate失败。图片已实际查看，full白线变half黑线，没有忽略/放宽薄边。

**事后review/过滤撤回：** 拒绝当前half方案；不是可交付画质。扩大邻域仍找不到该表面采样，不能背景填充或加gain冒充重建。按F33搁置，重开必须有实际full receiver缺样补偿和动态/噪声/完整成本证据。七个自有Runtime改动逐字节恢复到e7fcb40（SHA与012既有测量一致），新Editor入口/meta移到Tests~归档，产品只保留013已验证helper。Unity恢复编译57944 actual exit0、C#0/fence0；旧Grid/Resources verifier仍过，无需重复相同Player。原型patch、所有运行源码/失败/图片/reversion都保留，root runner加缺入口保护不原样再跑；该保护未声称实跑。

**下一动作：** 回到010实际原版RELAX质量。DI NRD文档已有亮暗moment/尺度历史经验；GI当前SignalScale64将微弱输入缩小，是否损失FP16平方矩精度是新待验证线索，本地NRD原shader确有moment计算。下一项015先读真实stage moment/局部输入及尺度敏感性，再决定单位策略；这不是认定根因，不重启sigma8/盲history/gain，不更改原5%。010质量、012HD/稳定预算、正常camera和完整Player仍不完成。

**本步过滤范围：** 仅Tests~/RTGI/HalfGrid的真实实验/撤回证据与RTGI过程/禁试/设计/计划/交接文档；不提交被拒绝的Runtime/Editor实现，不碰DI/vendor/native/源场景或他人Ho-GD plan。全部本任务进程结束，goal active。

步骤014过滤提交：`ee962e4`。提交后HalfGrid verifier再次过，Runtime/Editor零diff，原型仅在Tests~归档而未产品化。下一项015回到原版GI低信号moment实际证据；已失败half按F33搁置，完整goal继续active。

## 015：实际古堡低信号moment精度取证（进行中）

**事前review：** 014 half已撤回/搁置，不重开。DI NRD步骤14/15有亮暗moment与尺度历史实测；GI固定SignalScale=64，真实微弱信号平方可能落入FP16低精度区，但目前只是新假说。先保留原尺度/参数，扩既有Castle stage audit采集slow diffuse真实二阶矩。已核对共享dispatcher只按descriptorType=UAV且binding=gOut_Diff选择输出；固定manifest4=Temporal、5=HistoryFix、6=Clamping，其RGBA alpha经原shader确为moment。PrePass alpha是距离，Atrous阶段alpha是variance，不混当二阶矩；不改DI/vendor或复制fast alpha。

**验证门槛：** 同010长窗256 settle/256观察、同stride4 receiver/原逐RGB5%门槛，读取4/5/6阶段RGB/alpha、正信号而moment为0比例、mean²与moment的FP16量化亏损及局部raw/filtered/input记录。先baseline actual exit/源码/log/report绑定，再据证据决定可逆单位缩放实验；物理raw/最终输出及history量纲必须一致，不能输入加gain或改门槛。012HD仍禁试，半GI保持撤回，目标继续active。

**口径更正：** 上段事前review对5的判断错误。HistoryFix正文虽然写float4，但实际CPU把gOut_Diff绑到Temporal fast pong；稳定history早退后alpha仍fast0，不能一律统计slow m2。baseline原数据/错误标签保留，baseline-interpretation明确只采用4/6。另开真实DLL描述符探针16272 actual exit0：Temporal slow=transient0、fast=1；Fix读0/条件写1；Clamp slow读0/fast读1，slow写permanent0/fast写1。不是只凭函数名或固定pool猜测；F34禁止混用。

**正式数据：** baseline exec1794/PID42056、单位1 exec39224/PID46844、单位.125 exec44355/PID37392，三个Unity均actual exit1、audit完成、原5%仍失败、编译诊断0/无fence。每条256额外预热/256观察，4/6各1048576样本。原64 Temporal/Clamp正亮m2=0约98.458/98.519%；单位1降.704/.729%；.125降.00315/.00315%。Temporal明显m2亏损样本188211→69685→90；该采样网格无饱和。baseline平均物理m2=7.208e−5低于平均mean²=8.624e−5；单位1 m2≈2.320e−4、mean²≈8.638e−5，统计精度问题得到实际支持。

**单位对照及拒绝：** 只加Editor ValidationSignalScale，NRD前除/后乘同值、变单位清独立NRD历史；Player仍原64，Shader/vendor/DI/native/采样全不改。三组256条raw frame均值逐条完全相同。RGB误差原(-5.946,-6.535,-7.028)%→单位1(-6.220,-5.317,-4.699)%→.125(-6.167,-5.252,-4.603)%，都未全过。filtered方差2.307e−8→约1.484e−8有改善，但不能用降噪收益或绿蓝改善掩盖红/绿失败。换单位亦改变epsilon/舍入相对量，不声称moment是唯一根因。两个有界单位试验后停止缩尺度，F35不再盲试.0625/.015625、按通道调scale或直接生产化；HDR/动态/历史仍需以后整体策略。

单位1实际报告完整，但初runner错用marker“HoGICastle”未匹配异常，脚本也exit1。原run保留marker=false；verifier另按实际失败异常/fresh/moment完整性/退出核对，不修改历史为true。后续marker修正后完成.125与绑定探针。所有时刻精确源/报告/logSHA均独立归档；原baseline5错误统计不覆盖，显式排除。

**事后review/收益：** 接纳实际slow moment/资源别名取证与仅Editor诊断能力；不接纳生产单位调整、HDR全域安全或RGB质量通过。没有GPU资源/Shader/NRD参数变更；Runtime两处只是Editor单位探针，默认/Player仍64，不声称新Player回归。Moments verifier核对4个实际进程、源/log/report、原逐帧raw一致、分桶/局部单位、真正4/6和3个原5%失败。014half搁置、010质量/012HD与正常camera/完整预算继续。

**下一动作/过滤提交：** 016先补同一窗口full图RGB及16种stride4相位/局部阶段误差，区分网格取样与全图偏差、保留原0相位失败，不挑相位/改门槛。这将决定后续沿DI颜色裁剪/信号域的有界候选，而非继续盲调units。只提交GI两个Runtime文件Editor诊断hook、Castle入口/新MomentAudit/meta、Tests~/RTGI/Moments、RTGI过程/禁试/计划/交接。其他模块/他人Ho-GD plan不碰，全部进程结束，goal active。

步骤015过滤提交：`419e4ef`。提交后Moments verifier再次通过；矩精度问题有真实证据，但3个RGB门槛继续失败，生产默认/Player仍64。index空、进程结束；下一项016按上述完整域/相位取证，不重复单位调参，goal active。

## 016：完整图像、16相位与局部阶段误差（进行中）

**事前review：** 015只修矩精度并不能过RGB，不能继续缩scale或将矩正确冒称质量正确。现有ReadPixels已读full256²，原能量只用stride4相位(0,0)。新增CPU审计同时累积全部receiver、全部16相位、同像素时间均值/方差与raw→input/PrePass/Temporal/fast/Clamp/Atrous→filtered，保持原32+256预热/256窗、原5%与原相位失败。单位只比较原64与已测.125，不引入新数字/产品默认变更。

**接纳边界：** 相位/全图仅帮助判断全局偏差和局部损失，不能挑好看相位、删旧失败或以新统计静默替换原门槛。HistoryFix只标fast/条件fix RGB，不再当slow moment；A-trous只读取RGB。确认问题分布后才决定是否沿DI已验证颜色凸裁剪做有界候选，先不改vendor/采样/NRD参数。半GI与012HD继续搁置，正常相机/Player预算仍独立未验。

**阶段证据及候选事前review：** 同窗全图scale64 RGB约(-7.25,-8.42,-8.78)%、.125约(-7.56,-7.34,-6.55)%，16相位两组全部失败，不能解释成原相位假阳性。局部热点(87,165)raw红均值.02472、input/prepass相近，Temporal约.01186、最终约.00363；矩精度改善并未消除压低。原Clamping出现少量负RGB，DI第82～84步有同box fast→slow线段裁剪成熟证据，可隔离测试其GI收益，不先认定能修均值。

只在自有项目生成原vendor HistoryClamping的独立候选，替换diffuse轴向clip为DI同标量线段相交，保持box/sigma/预算/acceleration/reset/moment修正/其它body与资源布局；用已测.125单位和原.125对照，不新增单位调参。仅当前GI NRD实例shaders[6]临时替换并finally恢复，default/Player/vendor/DI源不改。若无质量收益则拒绝该路线，不能因“来自DI”就直接产品化；原stride0与全图5%都保留。

**正式完整域证据：** exec36028已收：scale64 PID56596、.125 PID8564均actual exit1、编译诊断0/fresh/marker/完整audit、无fence。每组full16777216样本/16相位各1048576；原相位与015一致。全图64 RGB(-7.2477,-8.4152,-8.7755)%、.125(-7.5641,-7.3400,-6.5499)%，16相位全部失败。全图time-mean L1 .19121/.21131；方差filtered2.788e−8/1.774e−8。不是取样相位假阳性，不删旧门槛或挑相位。

**局部阶段：** 全图64 raw/input红.00154717/.00154716，Pre .00153321，Temporal slow .00146992，fast/条件Fix .00152301，Clamp .00146839，末Atrous .00143554，published .00143504。最差(87,165) raw红均值.02472、Temporal约.01186、最终约.00363～.00372；raw方差.00257远高于filtered约3e−5。已定位阶段/位置，但有限窗raw高波动还不是独立高样本真值，不能直接把全部差值都定性物理能量损失。

**候选真实失败与过滤：** exec62237/PID37456 actual exit1，只有原vendor一个diffuse clip块改成DI同box线段剪裁；原source SHA、DI参考/生成body/wrapper SHA入报告，compile0，当前GI实例数组[6]scope替换并finally restoredOriginal=true。fullRGB(-8.3104,-7.8915,-6.9069)%、原相位(-6.9120,-5.7901,-4.9390)%、L1 .21378都劣于.125对照；负分量计数352→4不能抵消整体退化。三组full raw256帧均值逐条完全相同。拒绝该GI能量修复路线，F36搁置，不否认DI已有独立保色收益。

候选Editor入口/helper已精确归档并从产品移除，Castle恢复baseline全图审计版本；Runtime/Shader/DI/vendor/native没有diff。只保留SpatialAudit及Castle接口/Primary内部可访问。过滤后编译exec17362/PID41164 actual exit0、C#0/fence0。原型body/wrapper/完整执行源/报告在Tests~/RTGI/Spatial，验证器已核对全域/16相位守恒、旧相位、真实失败/源码/本体替换/恢复与编译。没有GPUms/Player/正常camera新验收，014half与012HD仍搁置。

**事后review与下一动作：** 接纳完整域诊断，否定“只是取样相位”的解释，拒绝凸裁剪候选。下一项017先复核本机RTX/现有Reference一跳估计，再给热点与安静控制点做高样本无复用参考及批次收敛；匹配原full primary/material/light/sky/Cutout域，区分有限窗ReSTIR异常与filter偏差。此为一跳漫反射验证，不扩通用PT/多跳，不以子集冒充完整验收。先有真值证据再改降噪；原5%与所有失败保留，不盲gain/历史/sigma/更小单位。

**过滤提交：** 仅Editor SpatialAudit/meta、Castle完整域接口（无候选）、Tests~/RTGI/Spatial及RTGI README/过程/禁试/交接/计划。拒绝候选只在Tests~可review归档，不进入Runtime/Editor产品入口；他人Ho-GD plan不碰，所有进程结束，goal active。

步骤016过滤提交：`c8a83dc`。提交后Spatial verifier再过，Runtime零diff、index空、进程结束。下一项017建立同域高样本无复用一跳参考/收敛，不能把有限窗高方差raw或子集当完整真值；原5%和全部失败继续，goal active。

## 017：同域高样本无复用一跳参考（进行中）

**事前review：** 已重读M0/CommonDiffuse独立墙积分及本机RTX BrdfRayTracing/ShadeSecondarySurfaces。参考支持混合lobe/完整PT域，本步仍只匹配既有Ho一跳漫反射，不能抄完整PT或以旧reference偏暗反调Ho。016热点raw波动很大，需要比256窗高得多的无复用采样，并按独立批次看收敛；子集参考不等于全图/完整画质验收。

**实现/门槛：** Editor专用Feature在同一full256 GB/SB之后，展开sampleID×receiverID，仅包装原primary loader映射到选择的full pixel；复用已验证Generate→次级SDK RIS→真实alpha final visibility→Final，无Temporal/Spatial/NRD。借同一场景/排除、18活灯及sky/RIS，确认不把容量32当活灯数。GPU probe对比原full primary、RAB与主GI实际history包，先小批有效性再递增独立seed批次。黑事件算样本，参考输出FP32防额外截断，保留与产品FP16差异边界；采样预算/参数不改main GI。F31仍禁原HD条件，fence错误只中止自身任务。原5%及全部失败保留。

**AS前置错误及修复事前review（2026-10-21）：** 小批/1M采样有过functional pass和14/16统计收敛，但两次长批device removal（887a0006）后启用Unity官方D3D12 debug层，发现14条BuildRaytracingAccelerationStructure vertex buffer范围越界。Unity退出0和旧runner debugErrors=0均非合格（匹配式漏掉“validation”）；既有dense数值先取消真值接纳。日志明确not out of Local memory，不能归因显存不足。

CPU-only实际layout确认：1_mesh为251321顶点、位置stride40的skinned mesh，14个非天空submesh都用非零baseVertex、局部indices；driver请求跨度10052812=(N−1)×40+12，却从baseVertex偏移处起读，超过10052840B整VB。拟只在已有自有BakeMesh快照上把index重写为absolute、baseVertex/firstVertex归零，保持顶点/UV/法线/材质/submesh顺序和原raster mesh不动，再用同场景debug层判定是否消除14条错误。小骨骼/非readable/Cutout/DI共享路径要回归；静态非零baseVertex未由本场景覆盖，不扩大修复结论。不凭数值“看起来对”接纳非法AS，先修几何再重跑参考。

**实际修复/证明：** 新共享HoRTRayMeshIndices在自有pose上按applyBaseVertex取得absolute indices并重写index buffer，保留submesh/topology/bounds、base0；UInt16足够则保留，否则UInt32。仅在HoRTSkinnedMesh.BakeMesh(true)后调用，source/raster不动、stable pose无额外Bake。实际Unity重算firstVertex为minIndex，不能称返回firstVertex全0；合法AS范围依据base0。canonical-layout PID28044 actual exit0：vertex流SHA不变、absolute三角元组相同、14处base规范化。索引724416/约2.90MB，O(index) CPU与临时数组成本未计入GPU收益，动画成本后续测。

同场景debug小批13720及最终复核29476 actual exit0、API错误14→0。shared scene非零base RG12436/compat58844各145checks、GI native属性57560共141checks全exit0/validation0，覆盖骨骼/Morph/镜像/缓存/非readable及真实UV0/UV1（unused UV poison避免错误索引也通过）。两份新Mono Release RG build54680/Player52168、compat build45800/Player58604全exit0，各315实际请求/35checks；serialized nonreadable source base4保持、三个owned snapshot base0且absolute，原Cutout/动态/启停/合成保留。payload/日志/报告SHA完整，无GPU计时DLL，不称正常自动camera。

**参考与故障完整保留：** 分配早于URP初始化/compute include pragma未导出Final先后实际exit1，修后小512×2 functional通过。原4096×256同帧长循环与512×16384跨update旧AS长循环两次device removal，均actual exit−1无fresh终态；后者仅terminal=false的1024批检查点。日志887a0006、not out of Local memory，不能甩锅显存。恢复小负载57032 actual exit0，24fenced known-load资格过，但不当后续大场景稳定保证。首debug3432 exit0/runner漏匹配validation计0，实际14条；更正regex并撤销旧参考真值接纳。源/日志/错误结果不改写，F37/F38记录。

**合法AS后的参考：** fixed-reference44048 actual exit1仅因旧原5%失败，debug0、场景域稳定，每点512×2048=1048576样本，14/16严格收敛。两个暗控制点fixed-controls29832再512×16384=8388608样本，约438.5秒、无device/fence/API错误，新增(240,32)收敛；(16,16) green batch-SE2.316%仍大于2%，不放宽、不无限采。最终15/16选点可信于同模型与当前精度，不当全场景/绝对物理真值。raygen/compute/full history primary误差0或微小量、18灯15实例12Cutout、8/4候选与sky/ray域一致。修复后数值与旧未定义范围数据恰好相同也不能恢复旧数据资格，依赖新的合法实跑。

热点(87,165) reference红约.001500，原256窗raw为16.48倍、filtered2.48倍；另一点filtered仅reference .49倍。选点有事后选择偏差，结论是有限窗raw不可一律作真值、不能整体补gain；不是16点代表全图。postfix full审计21152仍RGB(-7.25,-8.42,-8.78)%、原5%失败，矩/降噪/完整应用验收不由AS修复代替。

**事后review/过滤：** 接纳共享owned-skin AS规范化、实际回归/Player和受控参考工具；不接纳旧非法AS真值、剩余暗控制点、全图质量或“device removal唯一原因已证实”。静态MeshRenderer非零baseVertex仍须自有镜像，不能泛化skin修复。所有尝试源按SHA去重保存，当前root runner仅小debug复核，移除旧不安全长循环入口。下一项先补静态base安全及只读源，再用合法AS独立seed评估复用模式/过滤；原F27/F31/F33/F35/F36和全部5%失败继续，正常camera/1080/完整预算未完，goal active。

步骤017过滤提交：`5456782`。提交后DenseReference verifier再次过，owned-skin AS错误14→0、Editor/两Release Player证据保留；index空、进程结束。下一项018静态非零base与只读源的安全镜像，不泛化已修范围，完整goal active。

## 018：静态非零baseVertex安全镜像（进行中）

**事前review：** 017只修自有skin快照；静态MeshRenderer仍直接注册源Mesh，非零base同样可能形成非法AS。必须保留该几何支持域，不能跳过它制造漏光，也不修改源/raster。计划按source Mesh缓存自有canonical镜像，base0继续零拷贝；所有renderer共享同一source时复用镜像，变换/材质/过滤/identity沿原合同，MarkGeometryDirty/释放正确清理。

**非readable路径：** 先实际核对Unity readonly clone/index写入能力，避免假定Editor可读。若不能原样clone后改index，GPU只读回拷完整vertex streams/index再创建owned readable镜像；验证格式/offset/stride、顶点原字节、absolute三角元组、UV/法线/alpha、source可读性与内容不变。回拷只在创建/显式dirty，稳定帧不能阻塞GPU或重复复制。先小fixture和debug层0，再完整DI/GI/Player与生命周期/成本；原质量/预算未完，goal active。

### 018事后review（2026-10-21）：候选拒绝，原生增长问题复现

实现了按source缓存的owned静态镜像，重用绝对索引规范化；readonly源只回读indices，clone顶点不动。共享缓存/稳定不复制/source dirty/empty等157功能checks实际完成，但首次3→5实例增长出现D3D12 scratch范围错误，整个候选不验收。没有完成native UV/Cutout、nonreadable Player与成本资格，不把功能报告pass当合格支持。早期clone capability原run元数据被覆盖，报告保留并明确不具独立验收资格。

**有界失败与停止：** 原Ho未加mirror的共享/独立源增长同样各1条错误；初始5个干净，分Editor tick增长仍失败。Static mode、重建AS、PreferFastBuild、MinimizeMemory、跳过新增UpdateInstanceGeometry依次无收益，全部撤回。FastBuild错误范围2432 B，其余2560 B；没有继续盲调容量或参数。运行元数据/debugErrors原样保留，暂时移除debug拒绝的早期runner不被追认为通过。

**最后定位收益：** 新Editor/RayTracing/HoRTAccelerationGrowthProbe直接使用Unity API，不调用HoRTScene或GI。Unity6000.3.15f1/RTX4060/D3D12 debug真实PID19372初始5个build1次exit0/API0；PID41084先3后5/build2次exit0但scratch2560 B错误1。root runner恢复严格debug拒绝，实际脚本exit1。定位到“不依赖Ho镜像的原生API增长复现”，尚未证明具体引擎/驱动根因；没有光线命中或画质/性能结论。

**过滤/成本/接纳：** Runtime HoRTScene/HoRTRayMeshIndices和共享Editor场景测试仅本步骤自有修改按6ed9b41恢复；新mirror/partial/copyProbe移出产品。仅接纳独立Editor最小复现、Tests~/RTGI/StaticMesh证据与文档。rejected-candidate.patch对基线git apply --check通过；138份去重精确源码、15个完整实际进程日志约2.06MB，source/log index与review绑定。verify_validation.py通过表示失败/恢复证据自洽，不是static支持通过。所有本任务进程结束，run.activeProcess=null；无新Player或完整画面回归声明，017蒙皮修复保留。F39搁置静态镜像及无收益AS调参，静态非零base支持仍未修复。

**下一步019：** 使用017已确认合法AS的原古堡域，先核对本机RTX和现有M0对T/S/none的seed/权重合同，再做有界独立seed的main raw误差对照；使用已经收敛的参考子集，保留暗点不确定、全图5%失败及selection bias，不能据选点接纳全图。先区分有限窗口方差/系统偏差，再决定生产采样或降噪改动。不重开F27/31/33/35/36/37/39；正常camera/1080/完整预算继续，goal active。所有用户要求继续约束后续步骤。

018已按白名单过滤提交`ad900e7`，StaticMesh verifier提交后再次通过。生产Runtime/共享场景测试无diff、index空、全部本任务进程结束，仅他人Ho-GD plan未跟踪。下一轮直接019合法AS古堡的独立seed/模式误差对照，不重复F39。目标仍active，未complete。

## 019：独立seed与复用模式质量对照（进行中）

**事前review：** 017已证明固定256窗raw在局部可偏离同模型高样本参考十余倍，不能继续把所有filtered/raw差值当物理损失。018无收益AS增长路线F39搁置，使用017debug合法的原古堡域（15instance/12Cutout/18灯，static Sphere base0）。复读本机BrdfRayTracing/Secondary/FinalShading：各阶段RNG由pixel/frame/独立常数初始化；FinalMIS含主表面镜面BRDF，当前不移植、不改权重。DI过程审计要求功能/画质/预算分开，保留原5%失败。

**预先固定协议：** 仅Editor诊断，无复用/T/ S/TS四模式，seed序列0及104729+i×1048576（i=0..6），每段重置GI/NRD历史，64帧warm+256帧观察；8seed×4×320=10240请求，实际Editor tick分散，15分钟上限/fence只停止自有进程。先2seed×4×(16+16)小资格，不把它当收敛。保留full RGB/原phase0/方差、16参考点每段均值/方差/32帧块均值；核对主表面映射/历史、fresh secondary几何和RIS hash、灯/sky/AS域。15个收敛参考和暗点不确定/事后选点偏差继续，不能子集接纳全图。没有生产默认、NRD、Shader/SDK或Player变更；若统计仍不足则明确不确定，不无限增加样本。

### 019事后review（2026-10-21）：TS均值偏移定位，画质仍未通过

**实际执行：** 新Editor/RTGI/HoRTGISeedAudit.cs/meta，Castle改partial并接独立入口/报告。Runtime/Shader/SDK/NRD/Player不改。RG古堡256²，none/T/S/TS×固定8seed×(64warm+256观察)，分Editor tick。最终PID6104实际exit0，debug/编译/源漂移0，共32段10240矩阵请求/含初始与末尾合成功能检查10274请求。每段全图及原phase0、16点、方差/32帧块均值俱全，same primary/light/sky/Cutout/actual T/frame约束过；同seed端点secondary与RIS最终逐字节匹配。参考仍15/16，暗点与选择偏差保留，不是独立全图真值。

**结果与收益：** none full rawRGB(.001228918,.000130884,.000120137)；TS(.001544720,.000159953,.000142659)，相对none +25.698/+22.209/+18.747%，配对seed均值差SE约.418/.354/.333%。T和S单独约+2.4～3.0%，TS组合偏移大得多。none/T/S/TS filtered相对本模式raw分别约(−15.59,−17.40,−17.75)/(−13.11,−14.91,−15.53)/(−10.68,−12.70,−13.20)/(−7.96,−8.70,−8.99)%，full32/32仍原5%失败。TS filtered比none raw仍高15.70/11.58/8.08%，不能一律补gain。(128,128)红TS/reference=2.1819、seedSE/reference=.1456；T约.9707±.0739，none .9202±.1111。旧热点(87,165)新TS约1.0156±.1631，017固定窗16.48倍不能当长期真值。这些SE来自8个seed均值，非相关帧/像素iid或正式置信概率。定位收益是下一步优先查跨帧空间反馈，不称画质修复。

**失败/精度复审：** PID45524首资格exit1，byte gate失败但没字段记录，具体差异未证实；11552加记录资格exit0。原矩阵43444实际exit1，32段完成后byte gate拒绝，精确保留。独立逐字段复核504组position末位差异：实体最大1.22177e−5、100000单位合成miss端点.008789；18组normal差1个oct16分量单位。flags/material/emission/PDF/geometric/RIS严格相同。只改后置比较为原世界界限.0005、miss单位方向/FP32半径、normal一个量化步，primary和所有能量门槛不变；patch证明不改采样/渲染。最终完整重跑6104实际0且端点全bitwise相同，不推断比较器修正导致GPU数据相同，不补写具体引擎/调度根因。F40保留协议边界。

**接纳/成本/过滤：** 接纳Editor诊断、四attempt/完整日志/239源hash（97共享017 blob）、逐字节gzip原JSON及可重算分析、README/过程/禁试/交接/计划。约3.44MB，verifier四实际进程/536870912 receiver样本计数/逐字段SHA/旧失败/全部5%失败通过。没有新Player、compat或产品GPU成本证明，CPU ReadPixels耗时不作预算。当前Runtime无diff，全部本任务进程结束、activeProcess=null，别人的Ho-GD plan不纳入。

**下一项020候选（尚未实施）：** 参考ReSTIRGIContext.cs已核对，其TS也反馈空间输出，Ho不是因此被证明索引集成错误。以019组合偏移为依据，Editor私有compute仅把Final写history0的值换为pre-spatial buffer2（当前帧照旧读1做T/S/最终可见性/NRD）。在Final写避免Temporal读0/写0竞态，保留当前T/S功能，不新增reservoir；有一次32 B/receiver额外读取要实测。先隔离生成original body单行候选/同seed配对，验证raw/reference、filtered、方差/细节与GPU成本，再决定是否可进入Runtime及双路径/Player/动态回归。禁止直接关闭T/S当完整交付，或同时改gain/sigma/Jacobian；F27/31/33/35/36/37/39继续。原质量、static base、HDR/正常camera/1080/完整预算未完，goal active。

019已按白名单过滤提交`4019bd7`，Seeds verifier提交后再次通过；Runtime无diff、index空、四个本任务进程均已结束，仅他人Ho-GD plan未跟踪。下一轮直接020仅Editor的pre-spatial history候选，不重跑19矩阵凑样本；原5%/static/HDR/正常camera/1080/完整预算继续未完成，goal active。

## 020：打包历史复制与pre-spatial反馈候选（进行中）

**事前review：** 019的TS组合raw偏移支持检查反馈链。复查Pinned GI/Reservoir.hlsl与参考Context：Ho Final的load1→StoreGIReservoir0会额外decode/encode LogLuv/normal；参考TS直接保留packed Spatial1。不能先假定codec幂等，也不能把pre-S和避免重编码的收益混在一起。因此有界比较legacy、packed Spatial1→history0、packed Temporal2→history0三项；后者S关闭时仍复制1。当前帧T/S/可见性/NRD、gain/sigma/Jacobian/SDK不改，不新增reservoir。

仅在自有项目Assets生成独立compute副本，include路径等义规范化，只替换Final历史store，reflection临时替换后恢复。先四模式×2seed×16warm/16观察小资格，检查真实绑定、全pixel history0与所选packed源一致、域/生成proposal；再TS×8既定seed×64warm/256观察，保留full/phase0/点/方差及同曝光mean images。依收益再做有效GPU成本/回归，不先进入生产；无收益撤回并记禁试。所有原5%/参考15点及不确定/静态base/正常camera/1080预算边界继续，goal active。

### 020事后review（2026-10-21）：pre-spatial历史有实测收益，准备生产接入

**三项隔离：** 复查参考TS保留packed Spatial，而Ho Final再编码历史，因此拆legacy、packed Spatial1→0、packed Temporal2→0，后者S关闭时复制1；当前帧T/S/final visibility/NRD不改。自有项目生成compute，只等义规范化三个include并替换一个store，8kernels/实际pass绑定/恢复后实际绑定均验证。Runtime/SDK/Shader/NRD参数未改。

**真实执行：** 40296小资格三项×四模式×2seed×(16warm+16观察)，24段/803总请求；10500完整三项×TS×8既定seed×(64warm+256观察)，24段/7715总请求；GPU已知负载30436与成本35936。四Unity实际exit0，debug/compile/source drift全0。两质量组96个端点、6291456个pixel packet比较无差；legacy也与Spatial packed一致，三项同seed端点secondary/RIS全bitwise同，18灯/15instances/12Cutout/sky/primary/actualT/frame域过。小资格none/T/S的每帧fullRGB和选点记录三项完全相同。codec额外重编码在此域没有误差，拒绝其为本次偏亮根因，不泛化到任意HDR；packed Spatial无画质收益。

**画质/噪声收益：** legacy full raw相对019 none约+25.70/+22.21/+18.75%，pre-S候选降至+7.73/+6.77/+6.28%；filtered相对none由+15.70/+11.58/+8.08%降为+1.22/−1.87/−3.28%。raw平均时间方差5.00366e−6→4.54467e−6，filtered2.83890e−8→1.27640e−8，约−9.17%/−55.04%。选择的15收敛点45分量非加权相对L1 raw .41785→.13950、filtered .65422→.46454（有选择偏差和暗分量放大，非全图验收）。(128,128)红raw/ref2.1819→1.1333，filtered1.7208→1.0944；(185,180)filtered仍约.520，局部未修好。所有24段full及原phase0的filtered/raw5%仍失败，不换掉旧门槛。参考15/16/暗点/共用模型边界继续。

**成本：** 当前GPU资格empty/128/2048 median0/.006144/.039936ms，24真实interval全部完成fence。三项各40帧、12markers，共1440有效interval；legacy/pre-S Final median .027648/.046080ms，增加.018432ms；GI.Total median1.043456/1.018880ms，但p95达1.611776/1.552384且固定顺序，不声称总性能提升。无新reservoir allocation，多一次其它buffer的32 B/receiver读取；不说实际DRAM/VRAM、不推算1080合格。计时DLL仍Editor-only，无新Player/payload证明。

**复审/接纳：** 接纳pre-S作为下一生产接入候选，未接纳完整画质/预算。12个原物理时均EXR/×100PNG均保留SHA，已查看原版/候选filtered预览，只说明形状/细节保留与亮度变化，不当普通相机合成。Tests~/RTGI/HistoryFeedback有四run/完整日志/246源hash（234共用旧blob）、生成源码与单处patch、gzip原始JSON/图片/可重算analysis/verifier，约7.03MB。verifier过含反事实控制、原版与019逐帧一致、packet/输入/计数、GPU1440区间及原5%失败。所有本任务进程结束、active=null，Runtime无diff，别人的Ho-GD plan不纳入。F41记录codec假说及禁止把subset/成本噪声当全面通过。

**下一项021立即实施：** Runtime HoRTGI.compute Final仅在spatial flag开时直接packed-copy Temporal2→history0，S关保留旧store。不要改T/S当前输出、SDK/gain/NRD单位或加产品实验开关；Final后写避免Temporal读写0竞态。实际生产加history源断言，双路径、环境/材质/Cutout/蒙皮/真实应用帧响应及两独立Release Player验证，包含S开启的新历史路径，不能只沿旧S关闭的解析夹具。Win64 payload无GPU探针，旧fixture/default门槛保留，实测硬退化则撤回。020工具Generate依赖旧legacy store，生产改变后不可原样重跑错标baseline，须用本步历史版本或明确适配。原全5%/static F39/HDR/正常camera/1080/完整预算继续，goal active。

020已按白名单过滤提交`3b6937e`，HistoryFeedback verifier提交后再次通过；Runtime无diff、index空、四个本任务进程均已结束，仅他人Ho-GD plan未跟踪。下一轮直接021生产pre-S packed history与实际S开路径/RG/compat/动态/两Release Player回归，不再重复20隔离矩阵；旧质量与预算缺口继续，goal active。

## 021：生产pre-spatial历史接入与回归（进行中）

**事前review：** 020三项配对控制已否定当前域codec误差；pre-S改善全图相对finite MC偏移、选点与时间方差，Final成本约+.018ms，原5%仍失败。只接入S开启时packed Temporal2→history0；S关保持原store，当前帧Spatial1求值/visibility/NRD、SDK/参数/资源不变。Final后写避免Temporal跨线程读写0竞态。

实际生产检查须证明Runtime shader被绑定、S开全viewport history0与2一致且1/2确实不同，兼顾S关原路径，不能只测C#开关。保留原环境/材质/Cutout/蒙皮双路径门槛；在真实Update Player默认TS开启序列加入history检查，两独立输出/资产前缀、不覆盖017 payload。原生产probe本就默认TS开启，并非S关闭；本步补资源合同与反事实见证，不改旧能量/响应门槛。若硬退化则撤回，全部原质量/static/HDR/正常camera/1080/预算缺口继续。

### 021事后review（2026-10-21）：生产接入、Player通过；核心AS与原画质仍失败

**生产改动/正确性：** Runtime仅HoRTGI.compute Final历史写7行：S开原样packed2→0，S关原store；当前帧Spatial1求值、visibility、NRD、SDK、参数及分配均不变。Final后写无Temporal跨线程读0竞态。实际Runtime compute绑定，RG19188/compat30080各226请求/8全viewport history检查，S开0=2且1/2有约5.8～6.5万不同packet，S关旧路径保留，全部exit0/API0/compile0/源漂移0。不是仅候选shader或C#开关检查。

**回归与失败诚实保留：** 原次级环境47824/12240各138帧23checks、Cutout45552/25412各284帧17checks、蒙皮base/UV/normal/tangent/morph5316共141checks均exit0/debug0。首核心23244/40360实际1，旧夹具读history0判断SpatialM>1已不合新缓存语义；只改读实际Spatial1，M>1与其它门槛全不变。两次失败/源码保留。修正核心12884/48892功能全过actual0，但各两条2304 B AS scratch越界，严格runner拒绝。2260实际绑定020冻结legacy Final，仍功能过/actual0并同样两错误，说明并非新历史策略引入，具体引擎/driver根因未证，完整核心debug资格仍失败。F39不再盲调。拆runner时一个尾逗号解析失败发生在Unity启动前，已修正，不伪称实际实验。

**Player构建警告：** RG build17284实际成功0/errors0，但包装器将5条warning全部拒绝，未launch。核对017两build原log/SHA，逐字相同五条（lilToon/Cutout duplicate keywords、既有SolarHD d3d11 Shadows.hlsl除零）后，只加精确行白名单，新warning/error仍拒绝，未动其它模块。源内容未变，RG复用该成功build并保留原run/SHA，compat47996新build。没有把5改0。独立资产/输出前缀，旧17 payload未覆盖。

**真实应用：** Player11072/36020各315实际Update请求、310观察记录、38checks，exit0/debug0/runtime diagnostics0/source drift0；两完整payload SHA，无HoGIGpuProbe.dll。各23次/94208 pixel history0=2，sample52 Temporal/Spatial不同且实际T开启，覆盖静止/相机移动/灯光/几何/启停/native Cutout→opaque/skin/morph/invalid pose恢复，实际生产compute HoRTGI。原能量/响应/15%空间L1/60%降噪门槛不改：L1约.10792/.10794，方差比.001053/.001064，59相机重投影帧、24几何reset；nonreadable source base4、三owned snapshot base0的原合同继续过。仍为每Update SubmitRequest，不算正常enabled-camera。旧截图通用名在compat覆盖RG，所以只保留compat两PNG，不补造RG图片。

**生产质量：** 实际Default古堡47984为256观察帧全域/16相位/stage审计，actualexit1仅原5%失败，debug/compile/drift0。full raw(.001328435,.000140335,.000128173)，filtered(.001250704,.000129084,.000116730)，filtered相对既有finite none约+1.773/−1.375/−2.835%，隔离改善保留；原filtered/rawRGB−5.851/−8.017/−8.927%，全部16phase也失败。保留原门槛、失败和局部误差，不换更方便比较器complete。当前stage红input .00132840→Pre .00133402→Temporal slow .00127925→Clamp .00127787→LastAtrous .00125113→published .00125070；HistoryFix5仍条件fast，不误标m2。

**收益/取舍/提交：** 接纳实际生产质量/静态方差改善，并有真实默认TS的响应/Player回归；没有新策略回归被观察到。核心AS硬缺口为legacy相同错误，未放行或修好；完整原画质/预算未接纳。020局部GPU成本+.018ms Final仍适用为同策略局部参考，不冒称新生产/Player完整成本。Tests~/RTGI/ProductionHistory保存所有原/修正/legacy/构建拒绝/Player/质量元数据、19完整日志（含017warning对照）、源/图片/payload SHA、analysis/verifier。1191源hash中240旧blob、897基线Git对象+精确行尾重建、54新blob，避免复制大量无关未改代码；约5.48MB。verifier过表示证据完整且失败保留，不是核心/画质全部过。全部本任务进程结束、active=null；他人Ho-GD plan不纳入。

**下一项022：** 原剩余损失现在有新的实际生产stage。先读015真实Temporal4/Clamping6的slow moment/descriptor别名，核对当前数据范围和各moment/variance消费者，再选一项有依据且维持HDR/历史语义的精度修复；生产64不盲改、F35不重启纯单位扫描、不补gain/宽sigma。优先用GI私有Editor资源/阶段试验验证，必须报告局部/全域/动态与额外资源成本，无益撤回。F39旧flags/重建/延迟不重试；本机Unity RTX参考Automatic/direct Build差异仅可读核对，有新原生API/分配机制才重开有界control。正常enabled-camera和实际1080/完整预算仍需继续，goal active。

021已按白名单过滤提交`a93d4dd`，ProductionHistory verifier提交后再次通过（明确保留core debug与原5%失败）。实际生产Shader改动已保留、两315请求Release Player通过；index空、所有本任务进程结束，仅他人Ho-GD plan未跟踪。下一轮022从当前生产NRD stage/真实moment绑定处理余损，不重复20隔离或F39旧调参；正常camera/HDR/局部/static/1080完整预算继续，goal active。

## 022：GI私有NRD池精度上界对照（进行中）

**事前review：** 当前生产pre-S保留，021full仍原5%失败。015已证明scaled slow moment下溢，F35禁止纯单位扫描；当前source还确认AtrousSmem由m2求variance、后续Atrous继续读写alpha，且固定luma floor1e−4会破坏任意单位不变性。不能只改一个alpha存储又漏消费者，也不先改epsilon。

本轮仅Editor对照baseline与GI实例所有RGBA16F pool→RGBA32F，默认scale64、原NRD/Shader/epsilon/settings/历史预算不变，external signal/output仍原格式。以高精度上界分离storage误差；没有收益就不扩为全量生产资源。用Editor-only instance allocation selector，默认null、DI与Player行为不变；格式变更重启GI自己的NRD历史，统计真实pool格式/bytes/Native绑定。阶段Capture不能再CopyTexture到R16：新增float32 Load/Store copy、先tiny-value位一致self-test，再保留完整RGB、原phase0、slow4/6 moment与15点边界。先小资格再两组固定长窗，仍严格D3D12/源漂移/退出/原5%，不拓宽HD或调单位/gain/sigma。

### 022事后review（2026-10-21）：池精度显著改善，生产格式仍16F

核对Native binding与消费者：Temporal4 slow→transient0、Clamp6 slow→permanent0，AtrousSmem由alpha m2计算variance、后续Atrous继续读写；固定luma floor1e−4也存在，故不能假定任意单位不变。没有改单位/epsilon/Shader/NRD设置。本轮仅GI实例RGBA16F池→RGBA32F上界，external signal/output仍16F、scale64。共享core增加UNITY_EDITOR-only allocator selector，默认null；未改变DI或Player生产行为。独立float32 copy shader先以64分量（含1e−12）GPU seed/load/store/readback位一致验真。

**失败保留：** 首资格47656真实exit1，策略刚设置未经过Prepare导致实际格式未换，补一次真实render；长窗采样序列不变。48792小资格exit0，4池实际32F、copy64bit一致、API/compile/drift0。首长9360/24456虽actual1/0、pool与raw有效，阶段捕获目标漏UAV，Unity绑定报错且RGB/m2读回全0；旧regex漏该类错误，原diagnostics0保留但两组明确拒绝。仅加目标enableRandomWrite、Copy入口guard和绑定错误计数后重跑，不能把zero输出finite当capture有效。F43记录。

**正式证据：** baseline37676 actualexit1原5%失败，rgba32 26252 actualexit0原stride0通过；两者API/compile/binding/source drift0，256settle+256观察（前32warm）。256条raw frame RGB均值逐条完全相同。各slow阶段1048576 samples：基线Temporal/Clamp正均值m2=0为972674/982725(98.9772%)、972865/982725(98.9967%)；32F为256/984788(.025995%)、256/984784(.025996%)，未见本采样网格m2饱和。不称消灭全部moment缺陷，severe旧half容差不当32F严格界。

**收益/边界：** full filtered/rawRGB由(−5.8515,−8.0171,−8.9279)%改善至(−4.5586,−5.2075,−4.9433)%，原stride0过但fullgreen仍失败，16phase仍8失败（基线16），不挑phase或改5%。full filtered时间方差1.261775e−8→5.534688e−9(−56.14%)。实际改变permanent0/1、transient0/1四池，256² pool2818304→4915456 B，+2MiB；1080同布局投影+63.28125MiB仅payload，非实际VRAM/HD/性能证据。没有新HDR/动态/Player/GPU成本合格声明。

**复审接纳：** 接纳精度候选进入HDR/生命周期/成本/动态资格，未改生产默认格式。Tests~/RTGI/PoolPrecision约.98MB，六实际进程/全部失败、精确源/日志/原JSON gzip/真实格式/绑定/统计/analysis/verifier均保留；250source hash仅9新blob。verifier过只表示证据及剩余失败准确。所有本任务进程结束，active=null；他人Ho-GD plan不纳入。021生产pre-S策略仍保留，F39不重开盲调。

**下一项023具体动作：** 为GI-only RGBA32F pool建立真实HDR/动态/Player与GPU成本资格，scale64不变、DI默认16F不动。若需要生产构造参数，shared core defaultfalse，只有GI实例显式启用；Editor baseline callback须优先按native format重建baseline，防新默认下误标。先解析发光面低信号~1e−4至20000物理辐射；scale64下高段slow m2可超65504，实测32F有限且无溢出/残留；S关解析能量与默认TS状态分开验证、门槛不放宽。加resolution/启停/释放/资源格式、有效GPU已知负载与40sample成本、两真实Player的32F pool/历史/动态回归；若代价或HDR退化不接纳。原fullgreen/局部、AS/F39、normal enabled-camera、1080/完整预算继续，goal active。

022已按白名单过滤提交`189fcd9`，PoolPrecision verifier提交后再次通过；生产池格式/单位仍16F/64，021pre-S保留，index空，六个本任务进程结束，仅他人Ho-GD plan未跟踪。下一轮023直接HDR/生命周期/GPU成本/真实Player资格，再决定GI-only高精度默认，不重跑纯格式/单位矩阵；fullgreen/其它phase/局部与所有原缺口继续，goal active。

## 023：GI-only 32F池HDR/生命周期/成本/Player资格（进行中）

**事前review：** 022精度与噪声收益明确，但full green仍5.2075%失败且未量GPU/HDR。先以实例构造策略实现暂定GI-only高精度，shared core默认false，DI默认16F不动；Editor selector从native格式显式覆盖baseline。scale64/external16F/算法/参数不变。后续HDR、生命周期、GPU与两Player有硬退化就撤回策略，不把stride0过当全部完成。

解析发光平面范围1e−4/1/20000，验证原能量/重置、slow m2高于half上限仍有限；S关的解析与默认TS另验，格式96→64/启停/释放/多camera与默认ctor控制。再已知负载/实际40sample GPU和两个独立Release Player的pool32+原315应用帧/23history。内存2MiB@256/63.28125MiB@1080为布局，F31禁止据此重开原HD；原局部/full/AS/正常camera与完整预算继续。

### 023事后review（2026-10-21～22）：GI精度生产接入，HDR/Player通过，full与预算仍缺

**实现/隔离：** shared NRD core增加实例highPrecisionColorPool构造参数默认false，GI显式true；只native RGBA16F颜色池转32F，DI默认不变。Editor selector从native format优先重建baseline。scale64/NRD Shader/算法/参数不变。初策略四pool32而external output16时低信号失败，定位output同时承载PrePass/最终信号会重复half量化，最终仅再将GI output转32；signal/normal/physical filtered保留原格式。阶段捕获自动production32模式以float copy保精度，不更改分配策略。

**原失败/对照不删：** HDR夹具48476/17312实际1，因为Color.white×amp缩放了alpha，lilToon emissionBlend还乘alpha；显式RGBA(...,1)，未改算法/阈值。34708/12596输入改正后仍actual1，低1e−4 raw通过但filtered蓝低约7.7%，真实失败。47580/37696延迟能量失败记录完整HDR/生命周期后仍1，13764 legacy16F低信号也失败且更差；pass0仅测量完成。数据支持pool-only仍不足，未用历史失败放行新范围。仅output32新试验38732低/中/HDR全部过，最终compat36228也过，scope未扩其它格式。22真实进程全部源/原JSON/log保留。

**范围/生命周期：** 幅度1e−4/1/20000，原5% raw解析与filtered/raw均过，首变化帧reset；high slow m2约97653、低约2.388e−12，实际有限，明确范围非任意HDR。四pool32/default ctor control仍四pool16、96→64、停用全部oldpool handle释放、重建新instance、原NRD二camera/32尺寸/相机/方差/合成检查继续，RG/compat debug/compile/binding/drift0。S关T开做解析避免F23，默认TS由Player额外验真。

**真实Player：** pool-only6252/3340的315请求记录与原资产/payload保留；它们不是最终output32证明。最终独立PrecisionOutput build42680/43124、Player2932/17740 all0，defaultGI实际32池/16signal/32output，315真实Update请求、39checks、23pool/23history含NRDoff270 release、骨骼/morph/Cutout/base/能量/启停/相机/合成原门槛均过。最终空间L1约.105689/.105668<原.15。完整payload SHA、无HoGIGpuProbe.dll；五条精确旧build warning审查沿021，不改其它模块。截图按backend独立保存避免覆盖。仍请求式，不是enabled-camera自动呈现。

**成本/资源：** 32200已知负载资格有效，43588/19896 pool-only各40区间有效但Total median47.46/38.37ms、NRD p95 25.12/12.91ms尖峰，不能稳定比较。事后GPU100%、6296/8188MiB快照仅观测，不证明原因，不干预其它进程。最终8372实际production40区间合格，Total median1.042432/p95 1.891328ms，NRD .129024/.131072ms，不跨不同负载宣称加速，也不称目标预算。最终+四pool及output共2.5MiB@256、1080+79.10156MiB只逻辑payload投影，非物理VRAM/实际HD。F31原条件未重跑。

**实际全域/取舍：** final16612 actual0、stride0过，但fullRGB−4.49450/−5.05869/−4.72749%，fullgreen与7相位仍失败，不因只差.05869就放宽阈值。接纳精度与低信号/HDR范围改善和真实功能回归为生产增量，不接纳完整质量/稳定预算。当前无新增硬退化被观察到；AS/F39/normal camera/局部/HD仍开。Tests~/RTGI/PrecisionQualification约6.14MB，1197精确source SHA仅25新blob，其余Git/旧blob、22实际logs、完整失败/反事实/最终metadata、gzip原报告、两套独立payload与analysis/verify过。所有本任务进程结束/active=null，仅他人Ho-GD plan未跟踪。

**下一项024优先可用性证据：** 不继续围绕5.05869%调参/格式/单位或挑phase。先读DI关于hidden-host正常camera的真实记录/启动参数与当前运行条件，再设计一个有具体差异的enabled-camera连续呈现实验；必须Camera.enabled=true、无SubmitRequest/Render替代、真实endCameraRendering/帧/读回与GI开关/Volume/时域响应，不能把既有315请求当完成。可先核对无-batchmode但Hidden窗口/targetRT与OnDemandRendering是否已尝试，旧相同条件不盲试；开发约束不允许未经用户明确要求启动可见交互窗口，若确需可见UI先准备好reviewable结果再说明来源请求。其它质量用独立seed与局部/原5%完整保留，F39不重启旧参数，实际1080预算须新host/驻留机制证据，goal active。

023已按白名单过滤提交`e5ff691`，PrecisionQualification verifier提交后再次通过；最终GI-only四32F池/output32、单位64/DI默认不变，原失败保留。两个最终315请求Player与HDR/生命周期通过，fullgreen/7phase与稳定目标预算仍未过；index空、22个本任务进程结束，仅他人Ho-GD plan未跟踪。下一轮024优先normal enabled-camera连续呈现，先读DI原hidden条件，有新机制才试；goal active。

## 024：正常enabled-camera连续呈现验证（进行中）

**事前review：** DI MotionProtocol/Timing实际已试hidden batch及hidden普通启动，自动事件0，不能重复只去掉batch。代码显示先序列化disabled camera，运行后late-enable并请求预热。新机制仅一次：从场景加载就serialized enabled camera+targetRT，不做任何手动预热/Render/SubmitRequest/Volume.Update，显式background/OnDemand interval1；观察原生endCameraRendering并在下一Update读前帧数据。隐藏窗口约束保持，缺事件限时失败。

先独立Player/资产前缀，真实自动frame/相机/输出/开关/Volume/相机移动证据，不借旧315请求充数。如果相同隐藏条件仍无自动渲染，保留失败并搁置同条件，不绕过可见窗口授权；场景/可执行已具体可review后再问必要的显示窗口许可，独立质量/预算工作仍可继续。原fullgreen/7phase/AS/F39与HD仍开，goal active。

### 024事后review（2026-10-22）：真实自动相机通过，屏幕呈现另验

**实施与原失败：** 独立Automatic资产/输出与HoRTGIAutomaticProbe，场景序列化Camera.enabled=true及64²target；Start设background/interval1，不调用Render/SubmitRequest/Volume.Update/手动预热。RG第一build46000/Player7000实际0/1，180Update/0事件且target未created；显式Create只分配资源后第二build0/Player1，targetCreated=true仍0事件。两次原报告/源码/log/payload清单完整，不再重复同一hidden条件，不把无事件归因具体Unity/驱动缺陷。

**用户授权后的单变量对照：** 用户明确允许短暂可见窗口。同一份RG executable/完整payload不变，仅Hidden→Normal，Player43576在8.15秒自行exit0；独立compat build44364/Player45248约8.30秒exit0。两个Linear D3D12 Mono Release Player各241Update、240连续endCameraRendering、240前帧读回、10checks，camera始终enabled，无请求式渲染。RG/compat报告明确false/true；runtime debug/compile/绑定0/source drift0；build各五条精确历史warning继续记录。

**收益/正确性/成本review：** 32raster、64quarter、32one、32Volume-off、20re-enabled、30moving、30emission-off构成240记录；读取applicationFrame=renderFrame+1且renderFrame连续。0.25/1强度RGB最大误差RG8.795e-5/6.855e-5、compat8.798e-5/6.711e-5，小于原.015。Volume-off全部32帧恢复raster，re-enable首帧reset；移动30帧全部保留有效历史；发光关闭首帧reset且raw/filtered红0。证明真实enabled-camera自动调度/合成/动态链路，补齐此前请求式证据缺口。没有生产改动、运行成本结论或视觉QA。**camera仍输出到targetRT，窗口可见不等于backbuffer已验**；025再做target=null屏幕呈现，不能扩大本步支持域。fullgreen5.05869%/7phase、static/F39与HD预算继续。

**证据/提交review：** Tests~/RTGI/AutomaticCamera含两hidden失败/两visible成功、7完整logs、1163精确source对象（5新小文本，其余已有证据/Git，nativeDLL复用Git）、完整最终payload无HoGIGpuProbe.dll。首旧binary被重建覆盖仅保留manifest；不冒称其仍可执行。verify过逐条检查失败/源SHA/事件/全部RGB/响应；源码静态review确认无手动请求。全部本任务进程结束、active=null；Runtime无diff。白名单只Builder、新probe、新证据和RTGI六文档；他人Ho-GD计划不纳入。目标active，下一025默认屏幕目标连续呈现。

024已按白名单过滤提交`0d80f85`，AutomaticCamera verifier提交后再次通过。两visible Player各240真实自动targetRT渲染，两个hidden失败保留，全部本任务进程结束、index空，仅他人Ho-GD计划未跟踪。下一025直接默认backbuffer呈现/色域/捕获时序资格，短暂visible授权已获；原质量/F39/HD缺口与目标active继续。

## 025：默认屏幕目标与最终呈现（进行中）

**事前review：** 024只证明自动targetRT相机。本步独立Backbuffer资产/输出，serialized-enabled且targetTexture=null，不改生产。已查本机Unity6000.3.15f1 ScreenCaptureModule XML，CaptureScreenshotAsTexture读取GameView；在WaitForEndOfFrame捕获已完成的屏幕，下一Update对齐真实callback/捕获帧。先固定sRGB→linear解码并以024同材质raster约.21582作独立色域资格，不按加法结果挑映射。保持原.015加法/Volume/移动/发光门槛，实际screen大小与GI尺寸明确，保存屏幕PNG并检查。

两路径短暂可见测试沿用用户明确授权，各限15秒；不显示Editor、不干预他人进程。失败保留源/日志/图，不用截图触发Render/SubmitRequest、不把捕获成功等同正确画质。512窗口不是HD预算，fullgreen/7phase/F39/完整预算继续，目标active。

### 025事后review（2026-10-22）：默认屏幕目标自动呈现通过

**实现/资格：** 独立Backbuffer资产/Player/probe，Camera加载即enabled且target=null，512²屏幕与512²GI publication。WaitForEndOfFrame后CaptureScreenshotAsTexture，下一Update验证captureFrame=实际前帧endCameraRendering，不触发Render/SubmitRequest/Volume.Update。固定逐像素sRGB→linear，raster .215823859吻合024线性targetRT .215820313，再按原门槛检合成。首次build39064 actual1因隔离项目缺ScreenCapture模块，CS0103三条，原源码/manifest/log保留；仅隔离manifest启用本机内置1.0.0，产品依赖不动。

**真实结果/收益review：** RG build44940/Player13180、compat build38344/Player44248 all actual0；各241Update/240连续原生callback/240前帧读回/11checks，测量跨度4.514/4.576s，用户短暂visible授权沿用且15s上限。两路径runtime API/Shader/Property/UAV/fence0、drift0，build五条旧warning保留。两条沿用024的未用GeometryBuffer fallback/MSAA缺省shader提示原样保留，不宣称那些路径支持。全部RGB .25/1加法误差RG .00471446/.00641033、compat .00471386/.00643197<原.015；Volume-off32帧恢复raster、re-enable首帧reset、移动30帧全保留history、发光off首帧raw/filtered红0+reset。

**画面/成本边界：** 10张backend独立实际屏幕PNG，已查看RG raster/one和compat emission-off，灰基线/红蓝反弹渐变/红off后蓝保留符合夹具；这是纯接收面功能图，不是复杂古堡画质验收。512²不是1080，截图有屏幕输出量化，不作HDR精密能量/稳定GPU预算证据。本步Runtime无改动、没有新生产资源成本；补齐默认screen自动呈现缺口，不解除fullgreen5.05869%/7phase/局部/参考偏差/F39/static/HD预算。

**过滤/交接review：** Tests~/RTGI/Backbuffer有1164source对象（6新小文本、其余旧证据/Git）、5完整logs、缺模块失败、最终两payload/SHA无HoGIGpuProbe.dll、10原PNG、环境XML/package与verifier。归档与verify过，所有本任务进程结束、run active=null。白名单Builder、新Backbuffer probe/证据、RTGI六文档；他人Ho-GD计划排除。026先当前生产默认独立seed的全域/局部质量边界，不继续纯单位/格式/宽sigma扫描或挑phase过关，目标active。

025已按白名单过滤提交`ba76e3b`，Backbuffer verifier提交后再次通过，环境XML及source blob已核对Git暂存原字节SHA。默认screen两Player各240自动帧与10张屏幕图完成，首次缺模块失败保留；所有本任务进程结束、Runtime无diff、index空，仅他人Ho-GD计划未跟踪。下一026当前生产独立seed全域/局部质量边界，保留原5%/7phase/F39/HD缺口，目标active。

## 026：当前生产默认独立seed全域/局部边界（进行中）

**事前review：** 025完成普通screen呈现后回质量。019四模式矩阵使用pre-S/32F前源码，不能拿其波动直接解释023的fullgreen−5.05869%/7phase；现SeedAudit只落盘full/phase0，虽然内部SpatialAudit算16phase却未保留。本轮只当前默认TS，不重复四模式大矩阵，不改算法/参数/阈值/生产资源。固定沿用8seed，逐段GI/NRD reset、64warm/256观察，18灯/15实例/12Cutout/天空及原primary支持域合同不变。先2seed×16warm/16观察资格再8seed正式；短资格只测夹具，不作画质。

报告补全部16phase和全图每像素raw/filtered均值（CPU double累加后float32存档，非GPU格式修改），按seed均值估计经验SE，不把相关帧/像素当iid。固定8×8网格局部域全部报告；first4seed探索top12loss位置，last4seed验证同位置，不能拿探索选点接纳质量。旧15/16同模型参考与选择偏差保留，不作全图真值；原5%和旧失败完整。结果用于决定后续算法定位，若稳定损失不由seed解释再追阶段/局部结构，不宽sigma/gain/单位/格式扫描。Editor隐藏、15分钟/fence只限自身进程，Runtime无改动，F39/HD预算继续。

### 026事后review（2026-10-22）：默认8seed质量边界与holdout局部结构

**实现/资格：** Castle增加seedProduction flag；SeedAudit仅显式选TS且保留全部16phase、实际publication map SHA；SpatialAudit导出CPU double累计后的float32每像素raw/filtered均值。生产Runtime/算法/参数/GPU资源完全不改。38940资格actual0、39616正式actual0；正式32初预热+8×(64warm+256观察)+2合成=2594真实Editor请求，primary最大误差约1.9e−6、18灯/15实例/12Cutout/天空/alpha域与原合同通过，API/编译/绑定/fence/drift0。短资格98请求只测夹具，不当质量；report.pass/functional只代表测量/功能完成。

**编码review/修复：** 复核diff发现脚本默认Windows编码将含中文Castle源从UTF8写成GBK，实际编译/场景与域均成功但不能原样提交。保留两个原run精确GBK字节，显式UTF8规范化，Unicode文本严格相同；44052实际98请求资格0，两个seed的全图publication map与修正前逐字节相同。前后source SHA/编码/图SHA均记encoding-repair。以后Path读写显式encoding，不用控制台显示代替字节审查。

**结果/收益review：** 8seed full raw RGB(.00132385418,.000139750312,.000127679479)，filtered(.00126142420,.000132541894,.000121521042)；误差−4.71577/−5.15807/−4.82336%，配对差seed经验SE/raw .05379/.11936/.08255%。3/8full过，合并full不通过；逐seed phase失败7/11/11/8/8/9/11/7，合并9/16phase、32/64固定局部块不通过。5%绿边界仅.158个百分点约1.32经验SE，不能过度确定或挑seed；均值差不等于相对独立真值的物理损失。当前filtered时间方差5.805e−9/raw4.545e−6，非新GPU预算。

**局部定位/禁止扩大：** predeclared first4探索top12、last4验证同点、全部8×8局部块报告。探索首位(221,184)亮度差.01454在holdout只.000127±.000172 SE，不围绕单窗极值调参。6个x86、y206/227/255/252/204/226的holdout差.00159～.00252，约大于各自SE4.5倍，RGB差约−35%～−66%，支持后续调查该结构，但未证明raw是真值或NRD错误。旧01715/16参考/选择偏差不解除；新坐标须独立同模型无复用高样本参考与guide，不能直接补gain/宽sigma/挑phase。

**成本/过滤/交接：** CPU导出与读回是诊断开销，不进入产品也不作GPU成本；Tests~/RTGI/ProductionSeeds约12.6MiB，239source仅5新blob、3完整log、15原capture（3JSON+12maps，2同map归档复用）、所有原门槛/分析/encoding修复/verify均过。Runtime无diff、全部自有进程已结束、active=null。白名单三Editor审计helper、新证据与RTGI六文档；他人Ho-GD计划不纳入。027先新局部坐标同域无复用高样本参考+primary/guide核对，再决定算法，不能拿定位证据当画质修复。原全域/phase/F39/static/复杂动态/HD完整预算继续，目标active。

026最终静态review另修进度JSON标签：Snapshot(false)传显式modes，避免TS-only进度仍显示旧四模式；final Snapshot(true,modes)/GPU调用/实际report未改。reporting-review绑定一行精确字节替换，verifier区分历史实跑源与该纯报告修正；复用已编译签名/局部变量，不重跑GPU矩阵、不声称原run使用新字节。

026已按白名单过滤提交`ecd9c6a`，ProductionSeeds verifier提交后再次通过（保留原pooled质量失败，3个实际进程均结束）。最终progress标签纯报告修正另有精确字节review，未冒称GPU重跑。index空、Runtime无diff，仅他人Ho-GD计划未跟踪。027直接新x86六位置及相邻控制的同域无复用高样本/primary/guide，不把raw当真值，不重复本轮完整8seed或旧四模式，不调门槛；原质量/F39/复杂动态/HD预算与目标active继续。

## 027：holdout结构的同支持域无复用参考与NRD输入（进行中）

**事前review：** 026的6个x86局部差跨seed保留，但有限预算TS raw不是真值；017参考不覆盖这些坐标。固定y=204/206/226/227/252/255，每行x85/86/87，共18点，包含6目标和12相邻控制；完整报告，不依据新参考挑点。沿017原一跳估计器/primary loader lattice/真实alpha final visibility，无T/S/NRD；相同256²古堡、18灯/15实例/12Cutout/天空、8 local/4 environment、bias .001/distance1000、FP32 reference和黑样本计入。512样本×2048独立批=每点1048576，原SE≤2%/split≤5%/低信号与positive gate不变。先2批实际资格，再完整固定预算，未收敛保留未定，不无限加样本。

首次/末次额外读取同一主相机NRD normal/roughness、viewZ、scaled signal、raw/filtered；记录格式与scale，并按本机NRDConfig编码2/rough1验证normal pack/decoded/world primary、depth和half输入还原。只Editor读回/报告，不改生产或过滤参数。对比026全部8seed及heldout4seed同坐标均值和新同模型参考，保留选择偏差，不能外推全图真值。源码显式UTF8，hidden Editor、15分钟/fence只停自有进程；原全域5%/phase/F39/static/复杂动态/HD完整预算继续active。

### 027事后review（2026-10-22）：18点参考收敛，确认局部欠亮但尚未修复

**实际实现/资格：** 固定6行y204/206/226/227/252/255×x85/86/87=18点；Castle新增local入口/receiver选择，DenseFeature暴露实际mainContext给Editor审计；DenseAudit仅增首尾guide读回/报告。参考原一跳sample lattice，无T/S/NRD，主相机仍默认TS+NRD；相同256²/18活灯/15实例/12Cutout/次级天空、8 local/4 environment/.001/1000，黑事件计入、reference FP32。18160初资格actual0、22748正式actual0，36/2082真实Editor请求，API/Shader/Property/UAV/fence/drift0。

**收敛/原门槛：** 每点2048×512=1048576，共18874368样本，18/18原SE≤2%/split≤5%/positive/低信号门槛过；最大相对SE .84477%、最大split2.97214%、最少positive250448。primary/full/history最大差1.89e−6、ray/compute0，整个batch域稳定。仍同材质/light模型且选点来自旧探索，非独立绝对着色真值或全图验收。mainFrames0，因此不拿旧mainRaw/mainFiltered零字段当本轮均值。

**实际收益review：** 对026 8seed同坐标真实map，中心6点raw/reference normalized L1 .067544、filtered .494028；邻12 .046567/.423096；全部18 .056952/.458213。中心RGB总量raw相对reference +1.490/−1.242/−.994%，filtered −42.161/−59.933/−62.224%，逐点filtered仅reference .280～.651。此次证据排除了“这里只是raw单窗极值”的解释，支持有真实局部欠亮；不以18点替代全图质量/误差门槛。全部点/heldout后4seed/参考误差均保留，不挑点通过。

**输入定位/边界：** 首/末批的主NRD normal/signal/raw/filtered16F、viewZ32F、scale64；按NRDConfig2/rough1独立核验pack最大误差.000228882、normal解码.000324177、viewZ0；signal remod在max(5e−6,.002raw)精度界内，max abs约1.077e−5。前4行x85/86相邻viewZ差约.155、法线接近平行、albedo约.02→.4，后2行左邻已在后平面。这说明实际几何/材质边界而非guide坐标错位；没有证明具体filter阶段/跨边界根因。仅读首尾，不外推所有帧/HDR。

**最终防护与统计review：** 复核后增加normal/depth显式finite guard，36548重编译/36请求资格actual0。正式参考原源在guard前，唯一diff与实际guide全部finite由verifier独立核对，不伪称完整参考重跑。首次verifier按decimal64重算split不符，定位既有LINQ Average(float selector)返回float再赋double的数值语义；按原IEEE float32批均值及平均精度重建后全部原门槛一致，未改原结果/阈值。此修正不涉及渲染。

**成本/交接/过滤：** 只Editor首尾CPU readback/新报告，无产品资源/参数/SDK/Shader改动，无新GPU预算/画质修复结论。Tests~/RTGI/LocalReference约2.5MiB，242source/9新文本含场景与NRD编码原文、3完整logs、3原JSON gzip、026 map输入SHA、analysis/review/verify全过。全部自有进程已终态、active=null。白名单Castle/DenseFeature/DenseAudit、新证据与RTGI六文档；他人Ho-GD计划排除。028先同18点4个预声明seed0/104729/5347609/6396185的实际局部stage及新参考L1，保留原全域/16phase门槛，用真实绑定和32F typed copy/F43 guard，再按主损失阶段选机制；不重跑本轮MC、gain/宽sigma/单位格式扫描或复活F36候选。原F39/static/复杂动态/HD预算继续，目标active。

027已按白名单过滤提交`d099dff`，LocalReference verifier提交后再次通过；18/18新局部参考原收敛过，主guide输入匹配，局部filtered欠亮有同模型参考支持但未修复/未定位阶段。3个实际进程终态，index空、Runtime无diff，仅他人Ho-GD计划未跟踪。028直接同18点/4个预声明seed的实际32F stage与新参考误差，不重采本轮MC或重复0268seed；原全域/phase/F39/static/复杂动态/HD完整预算继续，目标active。旧verifier涉及current-source SHA时用所属历史提交，不回滚当前代码凑旧检查。

## 028：局部真实阶段归因（进行中）

**事前review：** 02718点同模型参考全部原收敛过，026 TS raw较接近而filtered严重欠亮，guide首尾匹配；还未归因具体阶段。本轮固定同18点、seed0/104729/5347609/6396185，每64warm/256观察，先2seed×16warm/16观察资格。复用027原参考，不重采MC或重跑026完整8seed，不改生产/参数/单位/格式。

只Editor使用既有ValidationDiffuseStage，production32F typed-copy self-test及UAV/format/binding guard；逐帧记录真实pipeline3/4/5/6/9/10写入计数，存原始local RGB流（raw/input/6阶段/publish）并按每帧实际history albedo×64重调制。5标条件HistoryFix/fast，不误作slow m2；10是末次Atrous。每seed同时保留full/全部16phase/旧5%和全帧RGB均值，以026同seed证明observer没有改原raw。primary/light/sky/alpha/scene域与新参考坐标核对，actual binding记录包含外部与pool资源。

根据真实主损失阶段与reference L1决定后续一个有机制候选，未取证前不关阶段/宽sigma/gain/单位格式扫描，不复活F36同box线段候选。额外copy/readback只是诊断成本，不当产品GPU预算。隐藏Editor、自有进程15分钟/fence上限；原F39/static/复杂动态/1080完整预算继续active。

### 028事后review（2026-10-22）：主要局部差在首轮之后的Atrous，尚无生产改动

**实现/实际资格：** 新Editor HoRTGILocalStageAudit/meta，Castle仅加flag/入口/报告。固定同18点与4seed0/104729/5347609/6396185，实际primary/full/history/ray/reference误差≤.0005，18灯/15实例/12Cutout/sky/alpha域与原参数不变。43892资格actual0、14436正式actual0；98/1314真实Editor请求。API/Shader/Property/UAV/fence/drift0，F32 copy64分量逐bit过，所有capture UAV/格式guard与实际bindings/count已核验。每观察帧3/4/5/6/9各1写，10四写；9+10四次共5个Atrous，10只保留末次。

**数据/正确性review：** HGS1存每帧shader/application序号、6真实writer计数、raw/input/3/4/5/6/9/10/pub九阶段18点物理RGB。实际每帧history albedo×64重调制，CPU double累计后float32落盘；负值保留/计数。3写external，4 transient0 slow、5 transient1条件fast/Fix、6 permanent0 slow、9 transient0、10 transient1/0/1/external，不能把5当slow moment。全部四seed raw与filtered全图逐帧RGB均值均与026同seed逐条同；18点raw/filtered均值也逐bit同，不扩大为所有纹理每帧逐byte证明。

**实际收益：** 同18点4seed相对027合格同模型参考的L1：raw .094054、input .094075、PrePass .094619、Temporal slow .112661、条件fast/Fix .097161、Clamp slow .113427、first Atrous .130687、last Atrous .462370、physical pub .462545。首Atrous RGB相对参考−12.987/−12.023/−12.743%，末−38.709/−57.173/−60.188%。主要差出在首轮之后四次Atrous，不能继续盲追输入编码/单位/宽sigma。未单存10中间轮，不宣称某一轮单独被证错误；仅局部因果定位，非全图真值/画质修复。

**原失败/成本：** 4seed full2/4过，phase7/11/11/7失败，pooled full−4.71818/−5.10748/−4.80745%仍失败、8phase失败，原阈值不动。全域平均时间方差raw4.26391e−6/filtered5.60620e−9，后续须核噪声取舍。4seed raw L1 .094不同于0278seed .057是预声明子集，不是代码回归。额外copy/readback/文件成本不作产品GPU预算。

**参考/下一机制：** 复读本机参考NRD Relax.cpp，iteration clamp后gStepSize=1<<i；runtime Atrous shader与该参考按LF逐byte同，step>4有随机offset；CPU上下文/Shader原文和SHA已归档，不声称已反编译当前DLL常量。029只Editor GI私有atrous5→3，移去最后两大步长，其它setting/采样/单位/格式不改；相同4seed阶段/全部18点（含暗邻）/full/phase/noise与真实count对照。无收益或硬退化即搁置、不扫2/4；有收益再完整8seed/旧合格参考/动态/HDR/两Player/GPU资格，不现在接生产。

**过滤/交接：** Tests~/RTGI/LocalStages约2.3MiB，246source/7新小文本、2完整logs、2原JSON+6streams gzip、code-reference/输入SHA/analysis/verify通过。当前Runtime无diff、全部自有进程结束/active=null。白名单新helper/meta、Castle、新证据和RTGI六文档，他人Ho-GD计划不纳入。原full/phase/F39/static/复杂动态/1080完整预算继续active。

028已按白名单过滤提交`90fc233`，LocalStages verifier提交后再次通过：copy/真实binding与writer count/全部stage原流/原质量失败保留，raw与pub观察一致性均通过。两个自有进程已结束、index空、Runtime无diff，仅他人Ho-GD计划未跟踪。029直接GI私有atrous5→3单Editor候选，先原18点/4seed/full/phase/暗邻/噪声取舍，有收益再8seed/旧参考/动态/HDR/Player/GPU，不扫其它迭代数、不提前生产化；全目标和原缺口继续active。

## 029：GI私有Atrous5→3单候选（进行中）

**事前review：** 028实际18点/4seed stage误差first .131→last .462，raw/pub未被observer改动，参考CPU步长1<<i且step>4有offset。只Editor GI实例ValidationSettings将atrous5改3，其他148B settings字节必须相同，记录真实AppliedSettings与writer count、finally hook恢复和之后正常render回5。保留原GI T/S/单位64/32F策略、时间预算、shader/SDK/DI不改，候选不直接生产化。

先2seed短资格，再同4seed（0/104729/5347609/6396185）、64warm/256观察；全部18点、暗邻、reference L1、full/16phase/原5%、局部/全域方差及raw一致性同时报告。收益判断不只加权L1；原参考收敛/全图边界保持。无实质收益或有硬退化就搁置且不扫2/4迭代；有收益再完整8seed/旧合格点/动态/HDR/两Player/GPU资格后决定生产。隐藏Editor，自有进程15分钟/fence上限，所有旧F39/HD缺口继续active。

### 029事后review（2026-10-22）：3轮候选改善局部与全图边界，噪声代价明确

**实现/隔离：** 仅Editor scope在GI core.ValidationSettings中将atrous5→3；每次校验148字节settings除offset92 uint外完全一致，记录实际Applied3、原hook/finally恢复与之后正常render Native回5，capture释放。单位/生产32F/时间历史/采样/其余settings、Runtime/DI/Shader/SDK不改。资格8792/正式632 actual0，98/1314请求，API/Shader/Property/UAV/fence/drift0；实际每观察帧9一次+10两次=3轮，typed copy64分量继续过。

**预声明取舍：** 正式前review-contract固定18点L1≥20%改善、原full/phase通过项不退化、逐点逐通道误差增加超过max(5e−6,.05ref,2×组合配对seed/reference经验SE)判硬退化，方差filtered/raw<.15仅作本场景筛选代理。经验SE非正式CI，代理不替代原解析/动态夹具，原5%/其它门槛不放宽。不以加权L1掩盖暗邻，候选不直接生产化。

**实际收益/成本review：** 同4seed全18点reference L1 .462545→.307095（−33.6076%）；full通过2→3/4，pooled RGB误差−4.538/−4.545/−4.091%过原full，phase每seed7/11/11/7→6/4/11/5，pooled8→6失败，原phase无pass→fail。暗邻部分通道小幅增加，逐点/通道全部报告，未触发硬退化界。局部仍明显欠亮、一个seed full与6pooled phase仍失败，不能称质量修复完成。方差均值5.60620e−9→7.76781e−9（+38.56%，各seed约34～44%），candidate/raw .001822，必须后续动态/真实Player再评估，不把少两dispatch直接称实测GPU节省。

**正确性：** raw全帧RGB均值/局部raw全部样本对028逐bit一致；input/Pre/Temporal/条件fast/Clamp/first Atrous局部原流逐bit同。改变只影响后续空间滤波结果，参考/选点限制不解除。设置唯一4字节变化和真实Native绑定/count/恢复均可从原报告及stream重建。

**过滤/交接：** Tests~/RTGI/AtrousCandidate约2.2MiB，246source仅3新小文本、2完整logs、2JSON+6streams gzip、review-contract/输入SHA/analysis/verify过；两个自有进程终态active=null，Runtime无diff，他人Ho-GD计划排除。保留有收益的Editor候选进入030完整8seed/旧合格参考/暗控制/full/phase/noise资格，不扫描其它迭代数；进一步有收益才动态/HDR/两Player/GPU与生产取舍。原F39/static/复杂动态/HD完整预算与目标active继续。

029已按白名单过滤提交`e53709e`，AtrousCandidate verifier提交后再次通过。固定3轮局部L1−33.6%、pooled full过但6phase仍失败、方差+38.6%，只进入更广资格。Native设置只改4字节、真实3轮与回5已证；两个自有进程终态，index空/Runtime无diff，仅他人Ho-GD计划未跟踪。030直接固定3的8seed/旧合格参考与暗控制，再动态/HDR/Player/GPU；不要先改Runtime，不扫描2/4，原所有缺口/目标active继续。旧source-SHA verifier用所属提交，不回滚当前代码凑绿。

## 030：固定3轮完整8seed/旧参考控制资格（进行中）

**事前review：** 0294seed有局部收益但未覆盖旧16点/完整8seed，方差增38.6%且6phase仍失败；Runtime仍5。本轮固定同一3，不扫迭代数，不改Shader/采样/单位/格式/时域策略。复用SeedAudit原legacy16点真实probe与每seed full map，原8seed各64warm/256观察；先2seed×16/16资格。设置148B只允许offset92 uint5→3，观察中实际Applied3/Native9+10总3，finally恢复hook且后续正常render回5。

比较026同8seed原raw全帧均值/全图map、029重合4seed18点；新18参考+旧16（15合格/1暗点未定）全部报告，不把未定点改绿。沿029预声明hard-regression/噪声筛选、保留full/全部16phase/原5%门槛与SE限制，不只看加权L1。无收益或硬退化搁置固定候选；有收益再动态/HDR/两Release Player/GPU后生产取舍，当前不改Runtime。源码显式UTF8，隐藏Editor、自有进程15分钟/fence上限，F39/static/复杂动态/1080完整预算继续active。

### 030事后review（2026-10-22）：完整8seed发现旧控制硬退化，固定3轮搁置

**实际资格：** 19396短资格actual0/98请求，39616完整8seed actual0/2594请求，原列表各64warm/256观察，原legacy16 probe及full map/全部16phase保留。148B仅offset92 uint5→3、所有观察实际Applied3/Native3、hook恢复且后续render回5，API/compile/Property/UAV/fence/drift0；原18灯/15实例/12Cutout/天空/alpha/primary合同继续。旧16参考15合格+1未定原样保留，结合新18共34点/33合格全部报告。

**收益与反例：** 新18 L1 .458213→.300045（−34.52%）；旧15 L1 .306745→.293238（−4.40%）；full3→7/8、pooled RGB−4.53312/−4.59497/−4.11332%过原full，pooled phase9→8失败、原phase无pass→fail。方差5.80513e−9→8.17309e−9（+40.79%）。但旧(141,157)G/B按预声明界硬退化：G filtered/ref .911986→.847986、abs error增加2.42011e−5>1.89071e−5；B .996998→.908287，增加8.56060e−6>5e−6；R也变暗但未越界。不能让加权平均收益掩盖反例，筛选条件不放宽。

**正确性/取舍：** 所有8张raw全图均值图与逐帧RGB均值和026逐bit同；重合4seed新18点均值和029逐bit同。退化不是换采样预算造成，候选不具备更广资格，固定3轮搁置；不扫2/4，不继续该候选HDR/Player/生产部署。默认5保留，Runtime/Shader/SDK/DI一直未改。此轮是拒绝一个不充分策略的收益，不是画质修复。

**过滤与实跑：** 三Editor文件Castle/LocalStageAudit/SeedAudit精确恢复90fc233默认5诊断版，移除candidate入口和覆盖；原实跑源SHA/字节完整归档，不伪称当前源生成原候选。19648过滤后真实98请求资格actual0，64分量copy bitexact，Native9一次+10四次=5、API/compile/绑定/fence/drift0。filter-review绑定前后源与Git字节，三自有进程终态active=null。

**交接/范围：** Tests~/RTGI/AtrousQualification约12.8MiB，250source/8新文本、3完整logs、3JSON+12map/streams gzip、原输入/34点/分析/预声明contract/过滤review/verify通过。白名单两Editor回撤文件（SeedAudit恢复后无最终diff）、新证据与RTGI六文档；他人Ho-GD计划排除。031保持5轮，为新18和反例141157及相邻控制取得逐轮/邻域传播证据，解释同一全局滤波有利有害的结构差异后再定局部机制，不再全局cap3/迭代扫描、重采既有MC或宽sigma/gain/单位格式扫描。原full/phase/F39/static/复杂动态/1080完整预算继续active。

030已按白名单过滤提交`6e8e6a1`，AtrousQualification verifier提交后再次通过。完整8seed/34点发现旧141157 G/B硬退化，固定3轮已搁置，Editor入口与override撤回默认5并实跑资格过；Runtime一直未变。三个自有进程均结束、index空，仅他人Ho-GD计划未跟踪。SeedAudit工作树与HEAD原字节SHA相同，已刷新索引stat，不增加伪diff。031保持5，为亮边和反例取逐轮/邻域传播新证据，不扫全局迭代数；全部原目标/缺口active继续。

## 031：默认5轮逐轮与邻域权重观察（进行中）

**事前review：** 030全局3轮因旧141157 G/B硬退化已拒绝，不能扫描迭代数。保持默认5，固定新18点+反例中心141157+其3×3其余8邻点=27点；19点有既有合格同模型参考，8邻点只作输入/几何传播观察，不伪造参考。固定4seed0/104729/5347609/6396185、64warm/256观察，先2seed16/16资格。

按pipeline10真实ordinal保存4轮输出（步长2/4/8/16），并在观察帧0/32/64/96/128/160/192/224/255读取实际center/8tap/geometry-normal-material-luminance权重及sumW/output；短资格只0/last。仅在隔离Assets生成stock shader的debug-UAV附加版，原算式不改、只GI实例shader10临时替换，finally恢复。必须验证raw/pub全帧均值及全图时间均值图对既有默认5一致，权重能重建原输出、debug shader去附加块后与stock一致。额外读取不作为性能。

不重采MC，不跑完整8seed，不改Runtime/设置/单位/格式/DI；若观察本身改变结果就保留失败并修夹具，不能据无效权重定机制。基于两类位置相反传播证据再定局部候选，F51全局cap3继续搁置。隐藏Editor，自有15分钟/fence上限；原所有质量/F39/static/动态/HD完整预算继续active。

### 031事后review（2026-10-22）：原版逐轮与debug权重分离，得到下限假说

**实现/资格：** 新Editor HoRTGIAtrousTrace/meta与LocalStageAudit的propagation分支，Castle选27点（新18+反例141157+8邻），19有参考/8仅guide，默认5/单位64/32F不变。ordinal10四轮分存；stock/debug分离。16584初debug98请求actual0但未满足exact observer，均值图差max2.38e−7原样保留；6360 stock短control98请求0且全raw/pub均值图对旧短资格逐bit同。9188 stock正式1314请求0；40664最终debug资格98请求0，45656 debug正式1314请求0。五进程API/Shader/Property/UAV/fence/drift0、typed copy64 bitexact，writer9×1+10×4。

**正确性review：** stock4seed全raw/pub均值图对026逐bit同，作为唯一质量stage记录。debug shader只是原算式+UAV记录，去附加块/恢复include后非空行与stock逐行同，但compiled浮点差存在：正式全图mapmax1.49e−8～2.98e−8，局部intermediate约3.26e−9、个别pub样本3.81e−6；不称bit-exact/no-op通过或据其放宽质量门槛。debug实际权重重建其输出差≤7.34e−11、sumW≤1.20e−7，单独用于传播数值解释。当前GI实例shader10/kernel finally恢复，Runtime/DI/vendor原文件未改。

**实际阶段收益：** 新18 L1从firstAtrous .130687，经step2/4/8/16为.215150/.306872/.386896/.462370。反例Clamp .04152→first .14924，step2/4/8/16 .14305/.15325/.12229/.11055，蓝/reference从first .8259至末.9967；后两轮确实恢复其首轮部分损失，不能全局删迭代。原full/phase失败未解除，F51继续搁置。

**权重/机制线索：** 权重采样0/32/.../224/255（短资格0/last），非全部帧。每点每轮center/var/normalZ/offset/sumW/history/output与8tap坐标/权重/颜色方差/geometry-normal-material-luma因子/guide均存。6亮边中心step16平均亮度变化约−3.744e−4、低于center半亮donor占8.16%总权；反例step8/16约+4.620e−5/+2.220e−5，该类暗donor权0。单位域假说：phi2×sigma在亮边step8/16低于1e−4比例78.7/97.2%，反例13.9/69.4%；step16中位数4.85e−5/7.88e−5，实际luma因子反推inverse约10000，吻合stock分母floor。scale64下最小阈值未作物理单位对齐值得单独实验，不据此直接称bug或修复。

**成本/过滤/交接：** 255source/12新小文本、5完整logs、47原JSON/bin gzip、生成源码SHA、observer-review/analysis/verify约24.4MiB。extra shader/copy/readback非产品性能，stock/debug边界明确，所有自有进程终态active=null。白名单新trace/meta、LocalStageAudit/Castle、新证据和RTGI六文档；Ho-GD计划排除。032只GI-private Atrous diffuse分母floor物理单位对齐1e−4/64，保持5和其它算式/参数/单位/格式，先scope/source/qual再同两组参考/暗控制/full/phase/noise，不扫迭代/单位。原F39/static/动态/HD完整预算继续active。

031已按白名单过滤提交`99f4fac`，AtrousPropagation verifier提交后再次通过。stock全图时间均值图逐bit相同、逐轮两类相反作用已证；debug非exact，独立数值边界/重建与初失败保留。五自有进程终态、index空、Runtime无diff，仅他人Ho-GD计划未跟踪。032仅GI私有diffuse luminance分母floor随scale64对齐的单候选，默认5/其它算式与参数不动，先新旧控制/full/phase/noise再资格，不能跳过141157或重开迭代/纯单位扫描；全目标和所有原缺口继续active。

## 032：GI私有Atrous diffuse分母floor物理单位候选（进行中）

**事前review：** 031原版逐轮与独立权重显示scale64下后期phi×sigma常落入1e−4分母下限，亮边与反例激活程度不同；不是修复结论。只当前GI实例临时替换shader9/10，将精确diffusePhiLIlluminationInv分母floor 1e−4改1e−4/64（实际AppliedScale必须64），保持5轮/phi/variance/信号单位/格式/所有settings与其它算式；不碰tap cutoff/specular/vendor原文件/DI。生成体逐行仅一个token差、wrapper保持，finally恢复原shader/kernel。

先2seed短资格与实际source/Native5确认，再同4seed新18+旧反例/8邻点/full/16phase/noise，必要完整8seed旧15/未定暗点；沿前述hard-regression与原门槛，不只报加权L1，不改阈值凑绿。有硬退化或无实质收益即撤回/搁置，不扫其它epsilon/单位/迭代数。Runtime保持stock。所有自有hidden Editor、15分钟/fence上限；全目标与F39/static/复杂动态/1080完整预算继续active。

### 032事后review（2026-10-22）：固定diffuse分母floor/64候选失败，已过滤撤回

**实际实现/资格：** 只Editor当前GI实例临时替换9/10，精确diffusePhiLIlluminationInv的1e−4→1e−4/64，每体只有一个token差，tap cutoff/specular/原math/settings/5轮/phi/variance/信号单位64/格式不改。21760资格98请求、632正式1314请求 all0；实际Native9×1+10×4，shader scope恢复，API/compile/Property/UAV/fence/drift0，F32 copy64过。031 stock27点模式，不用debug-UAV代替质量数据。

**结果/收益review：** 新18 L1 .462545→.444431，仅改善3.916%，未达预声明20%；旧141157 G/B变化很小，G/reference .9155→.9134、B .9963→.9937。原full2→0/4过，phase7/11/11/7→9/14/12/12，新增11个原pass→fail；pooledRGB−5.02872/−5.56743/−5.33700%仍失败且更差。无19点局部hard-regression不等于整体可接纳。方差各seed约−.27～+.18%近似不变，不能报显著降噪收益。raw局部所有样本及preAtrous流逐bit同，非输入/预算变化。

**取舍/过滤：** 固定floor/64策略拒绝/搁置，不否认031floor激活证据，但它不是主要解法。不得扫其它epsilon/单位/迭代。实际候选源/生成体/所有失败保存后，Castle恢复99f4fac，新增LumaFloorAudit与meta移除，Editor入口/覆盖不保留；Runtime/DI/vendor始终未改。43936过滤后stock control实际98请求0、Native5/typed-copy64/原math scope验证过，API/compile/绑定/fence/drift0。

**交接/证据：** Tests~/RTGI/LumaFloor含254source、3完整logs、27原JSON/bin gzip、生成体SHA、预声明contract/analysis/filter-review/verify全部过，只代表证据自洽与撤回验证，不改绿质量失败。自有进程全终态active=null；白名单只新证据/RTGI六文档，Editor恢复后无最终源码diff，Ho-GD计划排除。033复用031center/sample variance/实际权重离线核对双向权重与归一化传播，先区别两类结构的机制再GPU单候选；静态输入重放不冒称迭代渲染或修复。原F39/static/复杂动态/HD完整预算及目标active继续。

032已按白名单过滤提交`d9d4459`，LumaFloor verifier提交后再次通过。固定floor/64候选局部仅−3.9%、full2→0和11phase退化已拒绝，原候选源/生成体/失败完整；新增类/meta/入口已移除，stock5过滤后实跑过。三个自有进程终态、index空、Editor/Runtime无最终diff，仅Ho-GD计划未跟踪。033先复用031权重/两侧variance做离线双向归一化机制核对，不重跑旧GPU矩阵/MC、不扫常量，原全部目标/缺口active继续。

## 033：已有权重的双向/归一化离线核对（进行中）

**事前review：** F51全局3轮和F53floor/64已拒绝，不能继续扫描。复用031精确stock逐轮输出和分离debug权重，不新开Unity/MC矩阵。先独立重建forward luma、weighted sum、从27实际world/depth拟合透视几何核对native binary geometry；再仅作luma反向方差/无新参数几何均值对称化的冻结输入反事实。只额外检查031已固定factor2亮度分箱的双向对称支持，不能试多个阈值或把静态重放称GPU递归结果。

原27点（19合格参考/8仅guide）/4seed/9采样帧保持，分seed/分组/分轮全部报告；source/input SHA验证，所有原质量失败继续。若只能微小预测收益不重开对称方差GPU候选；有能区分亮边与反例的新机制才安排单个有界GPU实验，仍须旧点/full/phase/noise资格。Runtime/Editor均不改，目标active。

### 033事后review（2026-10-23）：离线排除简单方差对称化，限定支持域单候选

复用031原数据，无新Unity/GPU/MC、无Editor/Runtime改动。原forward luma重建max3.013e−7、输出max7.34e−11；27实际world/depth拟合透视基rank4/cond4169.7，max world残差3.93e−6，31,104个安全tap几何判定0不符，不再盲追坐标/geometry错误（仅采样域一致，不泛称全几何证明）。

固定原输入单轮重放：luma正反向因子几何均值对新18 L1变化step2/4/8/16仅−.00635/−.00446/−.000779/−.0000875，后期很小，不值得新GPU试验。031既有[.5,2]亮度分箱双向支持域对新18为−.01649/−.02065/−.01907/−.01795；反例step2略恶化+.00206，step8/16仅约1.4e−8/1.0e−9（tap全保留）。因此只支持一个大步长局部guard假说，不支持全局guard/迭代减少；没有调多个阈值。

此是冻结每轮原输入、仅luma项反向，不是实际reverse geometry/jitter/完整转移矩阵，也没有递归传播/动态/全域质量结论，不能把单轮改善相加报最终收益。Tests~/RTGI/PairwiseWeights含原输入SHA/原公式/全部27点输出变化/分seed分轮/analysis/verify过。白名单仅新证据与RTGI六文档，Ho-GD计划排除。034只GI私有shader10 step>4双向factor2支持域、保持5/其它参数/单位/格式，先源码与实际scope资格再4seed全19参考/邻点/full/phase/noise，有收益再旧15完整8seed等资格；失败撤回不扫ratio。全部原质量/F39/static/复杂动态/HD预算继续active。

033已过滤提交`0955bd9`，PairwiseWeights verifier提交后再次通过。无新GPU/产品代码改动，原输入/公式/几何核对与冻结反事实范围明确。后期luma对称化预测太小搁置，034仅既有factor2大步长双向支持单候选，实际资格/新旧点/full/phase/noise前不称修复；Ho-GD计划排除，目标active。

## 034：仅大步长双向factor2支持域候选（进行中）

**事前review：** 033冻结输入中factor2支持在亮边有定向变化、反例仅step2微退化而step8/16保留全部tap，最多支持单GPU资格。只GI实例shader10在gStepSize>4且donor不在center[.5,2]时将原权重归零，双零保留；5轮/phi/variance/geometry-normal-material/单位64/格式/其它原式不改，非单向增益/迭代扫描。生成体只有一块guard新增，scope恢复。

先2seed16/16资格再同4seed64/256原27点/19参考/8邻/full/16phase/noise，沿既有20%实质收益/无hard-regression筛选与原5%门槛；不能因冻结模型可能有益就降低标准。失败撤回搁置不扫ratio，有益再旧15完整8seed/后续实际资格。所有hidden Editor自有进程15分钟/fence约束，Runtime先不改，目标active。

### 034事后review（2026-10-23）：支持域候选撤回，发现后两seed raw配对问题

39032资格actual0/98请求、31792正式actual0/1314请求，5轮/其它settings/单位64/格式保持；只GI实例shader10 step>4新增对称factor2 guard，去一行后source恢复stock，scope恢复、F32copy64、API/compile/Property/UAV/fence/drift0。

名义新18 L1 .462545→.421666（−8.84%）未达预声明20%；旧反例近似不变/无19点hard，full仍2/4、pooled−4.69053/−5.02999/−4.71627%，phase7/11/11/7→7/10/11/7、无pass→fail，方差约+4～11%。但最终严格pair核对发现seed5347609/6396185 raw不逐bit同（前两个同）；全帧raw均值max差2.21911e−6/1.84949e−6、各767/768 RGB分量不同，local单样本max9.155e−5/.001495。不能把8.84%当受控因果收益；comparisonQualified=false与原数据保留，根因未知不归因engine/driver/场景/NRD。初Verifier据raw应同失败后，改为明确核验并保留该未满足条件，而非改绿质量/配对资格。

候选搁置，不扫ratio。原所有源/生成体/SHA归档后Castle恢复99f4fac、新ContrastSupportAudit/meta删除，49216 stock control过滤后真实98请求actual0、Native5/copy64/scope过、API/compile/绑定/fence/drift0。Runtime/DI/vendor始终未改。新证据与RTGI文档白名单、Ho-GD计划排除。035先原版4seed重现性/no-op clone对照，记录frame/light/sky/RIS/secondary实际状态，恢复严格pair前停止算法候选；不重采MC或重启旧AS调参。原所有质量/F39/static/复杂动态/HD预算继续active。

034已按白名单过滤提交`5095ddc`，ContrastSupport verifier提交后再次通过，明确保留comparisonQualified=false及后两seed raw不匹配。名义收益不足、不接纳因果质量；候选类/meta/入口已撤回，stock5实跑过，Runtime/Editor无最终diff。三自有进程终态/index空，仅Ho-GD计划未跟踪。035先stock重复/no-op clone及实际输入状态定位，严格pair恢复前停新滤波候选；所有原目标/质量/F39/动态/HD预算继续active。

## 035：默认5与no-op clone配对重现性（进行中）

**事前review：** 034后两seed raw配对失败，因果比较未资格，停止算法候选。本轮固定原27点/4seed/64warm256观察，在stock重复A/B与不改stock算式的shader10 clone之间比较，不改Runtime/参数/预算，不重采MC/跑AS flags。先短资格确认新增只读快照有效。

每segment首/尾保存真实shader/app frame、数值18灯、sky参数、完整RIS记录及hash、全secondary hash+27选中逐字段、完整primary history hash+选中raw包/实际probe，支持定位而非只报hash。GetData只读，不新增采样策略；保留raw/pub完整mean maps和逐轮流，先核旧031/026是否重现再比较A/B/clone。源/log/失败完整归档，未知原因不归因engine/driver；严格pair恢复前不碰滤波。默认5和所有原质量/F39/static/动态/HD预算继续active。

### 035事后review（2026-10-23）：当前A/B/no-op严格重现，历史遗漏/TDR/误排保留

**输入记录与首失败：** 新Editor InputStateAudit/meta；DenseFeature保存数值sky，Stage可选首尾输入快照；Trace可选literal-stock no-op clone，默认路径不启用。28112/31692 actual0但JSON漏未Serializable的AnalyticLight[]/Primary[]，灯hash/其它字段保留，数值资格未过；不能用actual0冒称完整记录。修正为72Vector4/135uint，保留原源/报告。

**异常与编排review：** 53896修正后资格actual1073741845、无fresh report，首快照前D3D12 DEVICE_HUNG/removal+Unmap，debug11；不能称JSON造成。外部Python失败未停止PowerShell，7312 stock意外排起，核PID/路径/启动时间后仅停本任务该子进程actual−1，partial与abort-review保留。后续显式LASTEXITCODE/EAP检查，runner加numericSnapshotQualified字段及失败门槛。未改driver/TDR/杀他人进程，7302MiB/util50%只观测非原因。

**恢复资格：** 6968 empty scene/no-AS已知小负载24sample/fence actual0/debug0，中位empty0/short.006144/long.039936ms；20912真实JsonUtility无渲染往返actual0（72vectors/135words/16384RIS）。显存降低后44040仅98请求/2分钟短资格actual0，真实数值字段和hash重建过；小负载不保证大场景，所有旧异常不改绿。

**正式有效结果：** 当前stockA53112、stockB50368、no-op50088 all0，各1314请求，原4seed64warm/256观察、27点/支持域/5轮不变，API/compile/Property/UAV/fence/drift0。三者全raw/pub时间均值map对026同seed逐bit同；A/B/no-op端点灯/sky/RIS/完整secondary+primary hash、选中逐字段相同。clone body除include定位与stock相同、无debug/guard，scope恢复。只证明当前重现，不证明所有机器/时间，不能给034原配对失败补发资格。

**历史差异解释界限：** 初stock对当前A的灯/sky/RIS/primary同、full secondary hash不同；selected差只position/normal：hitmax9.26e−6、miss方向7.82e−8/radius.01324、normal1oct16步，符合019既有界限。尚未证明其导致raw/TDR，不归因engine/driver/场景；端点非所有帧全部输入。

**收益/成本/交接：** 本步修复记录缺字段与编排失败停止，恢复新鲜stock严格pair前置，无画质/产品GPU预算改善。255source/8场景完整log+2辅助log/77capture gzip/当前和历史全部状态/analysis/verify，Runtime/DI/vendor无改动，所有自有进程已终态。白名单InputStateAudit/meta、3Editor增强、Castle、新证据与RTGI六文档，Ho-GD计划排除。036先同源GI输入双独立NRD baseline/no-op分支资格，后续实验只动一分支，避免跨进程ray末位差混入；旧候选不重开，全部原质量/F39/static/复杂动态/HD预算继续active。

035已按白名单过滤提交`bff5850`，Reproducibility verifier提交后再次通过。当前stockA/B/no-op对旧全raw/pub均值图逐bit同、数值端点一致；历史漏序列化/devicehung/显式abort与partial均保留，不确认历史唯一根因。十个自有进程全终态、index空、Runtime无diff，仅Ho-GD计划未跟踪。036先同一GI输入双独立NRD baseline/no-op分支资格，保持host/短资格/失败停止约束，旧F51/F53/F55不自动重开，原全部目标/质量/F39/动态/HD预算继续active。

## 036：同源GI输入双NRD基线/no-op资格（进行中）

**事前review：** 035当前stock/no-op重现恢复，但历史跨进程raw差因果未定，后续重建比较改为同一GI输入分支。新增仅UNITY_EDITOR的GI NRD record-input观察hook，直接传实际common/settings/只读external；两个独立RELAX_DIFFUSE native实例、独立pool/输出/constant buffer只共享IN资源，原baseline及所有产品参数/Shader不改。回调前录制两个branch，baseline照常记录，Native描述符在各core中即时拷贝不共享指针；scope释放恢复。

先64²空场景/no-AS确定性信号64帧：baseline/branch0/no-op branch1正常段输出逐bit相同，单独reset branch1必须只影响该分支，统一reset后应重新相同。再短实际GI资格、固定有限4seed27点/全图raw/pub与旧基线/每帧branch输出/输入绑定及时间重置等价；不直接开HD/新算法/旧候选。复制/三分支诊断开销不作产品GPU预算，hook在Player编译排除。host/超时/fence/失败停止约束保持，所有原质量/F39/static/动态/1080完整预算继续active。

### 036事后review（2026-10-23）：实际同源双NRD逐bit等价与历史隔离资格通过

**实现/边界：** GI NrdContext只加UNITY_EDITOR RecordInputs观察hook，传实际common/settings/只读external，Player字段和调用排除。Editor SharedInputAudit创建2个额外RELAX_DIFFUSE，IN标识全同，OUT/pool/constants独立；branch0原版、branch1原body no-op10，原baseline照常。Native每core立即拷贝descriptor/constants，无跨调用pointer复用。所有产品参数/Signal64/32F/Shader/DI不变，scope释放。

**首失败原样：** 54932小fixture在URP pool未初始化时Prepare失败、0比较帧；wrapper空生成路径Copy又异常，实际子进程退出码未落盘，只确认wrapper1，不根据源码补actual1。initial-allocation/源/报告/日志/review保留。修为空相机64²三个request先初始化URP（无GI/AS），先落盘actual exit再读取可选生成路径。

**小资格：** 41304 actual0，64²/64合成输入帧，branch0对baseline所有帧RGBA逐bit同；frame24只reset branch1，24～47仅该支不同且每帧至少14596分量（不只是alpha）；frame48统一reset后再次逐bit同，证明历史独立、baseline未受污染，hook/shader/资源恢复。此不作GI物理质量参考。

**实际GI：** 38552短98request actual0/32比较帧，54328正式1314request actual0/1024比较帧，4seed64warm256观察；每帧262144分量 baseline/branch0/no-op全逐bit同，含warm1280次真实shared callback。原完整raw/pub时间均值图和逐帧raw对026同seed逐bit同。有效进程API/compile/Property/UAV/fence/drift0，时间/矩阵/重置由同一actual common value-copy，非另造时钟。原质量门槛不因observer成功改变。

**成本/参考：** 加2NRD+读回为诊断；diagnosticLogicalBytes只external outputs/validation，不含pools/constants/VRAM总量。已读DI技术DiffuseExchange和实际派生body，三项关联策略（首轮temporal-var w²传播、后续pair/RGB bandwidth、固定Gaussian rejected mass留center）有DI收益及增噪/动态限制，不能自动当GI最佳；原文/SHA冻结。037可只branch1评估GI-private diffuse bundle、5轮/时间/单位/格式不变，排除spec/F0身份guide，先精确差异与小资格再新旧控制/full/phase/noise。

**过滤/交接：** SharedInput约11MiB，259source/9新文本、4完整logs/22capture gzip/全部初失败和成功/输入/生成体/analysis/verify通过。所有自有进程终态/active=null；白名单Editor工具/Stage/Castle、Runtime仅Editor hook、新证据和RTGI六文档，Ho-GD计划排除。没有生产画质修复/预算改善，原质量/F39/static/动态/1080完整预算继续active。

036已按白名单过滤提交`dfa3dd4`，SharedInput verifier提交后再次通过：64²历史reset隔离、实际GI1024观察帧全RGBA逐bit等价、旧baseline全图均值不变、初allocation/wrapper失败完整保留。四自有进程终态、index空，Runtime只有UNITY_EDITOR hook/Player预处理排除，Ho-GD计划未跟踪不纳入。037基于已冻结DI DiffuseExchange实际派生，仅branch1 GI-private diffuse bundle同源对照，先exact source/小资格再新旧控制/full/phase/noise，原全部目标/质量/F39/动态/HD预算继续active。


### 037预审（2026-10-23）：同源GI分支评估DI diffuse bundle

上一轮036是有效进展：同源输入、独立历史/资源、原版与no-op逐帧RGBA相等，跨进程配对不再是本轮因果依据。全目标仍active，原生产stock5保持。只branch1的9/10改DI成熟diffuse策略：Smem邻域估计只平均temporal variance、首轮w²传播；后续min pair variance/RGB contrast、固定Gaussian mass留center。不带specular/F0/material guide，不改设置/单位64/32F/资源/5轮/采样。原vendor/DI不改。

先检查逐段源码可重建/DI对应/仅diffuse，再64²小负载、实际16warm16观察短资格；确认branch0始终与main逐bit同、候选作用、scope恢复、无D3D12/绑定/编译错误。物理输出使用与生产相同量化albedo/scale/clamp及RGBA16F GPU resolve，逐帧branch0对生产发布校准；失败不能进入质量结论。通过才固定4seed64warm256观察，读取完整候选均值/方差及27局部逐帧样本。新18 MC reference L1至少改善20%才值得进一步资格；旧full/16phase不能pass→fail，旧141157和所有已有合格点逐通道误差增幅不得超过max(5e-6,参考5%,两倍配对seed/参考SE)，filtered/raw方差比<.15为静态初筛（不能代替真实动态）。原dark不合格参考保持不算通过。

若候选不合格，停止扩展8seed/HDR/Player，保留失败源码/原数据，撤回实验入口/覆盖，写禁止重复尝试与下一步交接；不扫描phi/epsilon/迭代/ratio或移植spec。若显著获益，再完整8seed/旧15+新18、动态/HDR/Player/GPU后才考虑生产化。新增诊断驻留/读回不作产品预算。每步先审后测再审，白名单提交，排除他人Ho-GD计划。


### 037终态（2026-10-23，待过滤提交）：DI diffuse同源实测有总体收益、旧控制硬退化拒绝

**实际范围：** 按预审只Editor branch1 pipeline9/10的一个固定DI mature diffuse bundle（Smem预模糊temporal variance、w²传播；后续pair min variance/RGB contrast、拒绝Gaussian质量留center）。原stock构造两个DIFF body，逐anchor/DI片段/SHA可重建，无spec/F0/guide/参数/5轮/单位64/格式/Runtime/vendor/DI改变。主baseline与branch0原版逐帧RGBA bitexact，branch1有实际变化。

**验证：** 四自有进程29048小64²/64帧、17236真实短98请求/32比较帧、31812正式1314请求/1024观察帧、13432过滤stock98请求/32比较帧，均actual0。有效过程API/compile/Property/UAV/fence/drift0、hook/Shader/kernel/pool/OUT恢复释放。正式1280含warm实际callback，Native9×1280+10×5120保持5轮。同源settings/common/time value-copy，物理RGBA16F GPU resolve先用branch0对生产发布逐帧校准1024次0差；stock raw/pub均值图与026逐byte同，候选raw均值及逐帧raw实际同，comparisonQualified=true。

**收益与失败：** 固定4seed64warm256，新18 reference L1 .462545→.266596（−42.36%），旧15 .304146→.208852（−31.33%），full2→4/4过，pooledRGB−4.167/−4.035/−3.671%，phase失败7/11/11/7→3/3/8/2，无pass→fail，但pooled仍3失败。方差×2.94/3.55/8.49/3.17，filtered/raw .00496～.00744过静态筛选，未验证动态。旧196141 B误差增6.299e−6>界5.913e−6；141157 G增2.171e−5>1.891e−5、B增8.084e−6>5e−6。旧B/reference .9435→.8732、.9963→.9126。原硬门槛不放宽，eligible=false，拒绝生产接纳，不继续8seed/HDR/Player/GPU。

**过滤/成本：** 原候选所有源字节/2patch/5Shader/meta/工具、40真实capture lossless gzip、4完整logs、272source对象/SHA和全部34点/相位/方差分析保留Tests~/RTGI/DiffusePolicy约20.6MiB，verify通过。新增类/meta/5shader/meta/覆盖入口已移除，Castle/LocalStage/SharedInput精确恢复HEAD源；过滤后stock/no-op32帧RGBA bitexact。当前build只写Tests重建目录，历史runner拒绝缺入口；不能回滚产品凑旧SHA。额外2NRD/多RT/读回只诊断，不叫产品GPU预算。四进程终态/active=null，白名单只证据和六RTGI文档，Ho-GD计划排除。

**后审：** 是有因果资格的机制收益与失败边界，未交付生产修复。F58禁止因聚合改善直接重开该完整bundle或放宽旧控制；不扫描phi/epsilon/迭代/ratio、不按坐标修复。038先保持同源两个独立分支，用原27+196141至少28点实际Pre/Temporal/Clamp/首Atrous/各ordinal的RGB/variance观察定位旧退化；observer先不改037原版和候选输出，才有限新采证据。新机制前不定算法变体，不重采MC/无新目的完整矩阵。所有原质量/F39/static/复杂动态/1080完整预算和总目标active继续。


037已按白名单过滤提交`520935e`，DiffusePolicy verifier提交后再次通过。四个自有进程actual0/全部终态，1024观察帧同源stock与物理GPU校准bitexact；固定DI diffuse bundle新18/旧15总体收益42%/31%、full4/4，但旧196141 B和141157 G/B三通道硬退化，拒绝接入生产，候选覆盖/新增类/Shader/入口均移除并stock复核过。40真实capture/4logs/272源对象和全部收益/失败/取舍留存，Runtime/Editor无最终diff。提交后仅校正JSON属性变更留下的索引行尾缓存，HEAD与worktree原字节相同，没有重写证据。index空、仅他人Ho-GD计划未跟踪。

038继续同源stage观察：原27点加196141至少28点，对stock与固定037策略读取实际Pre/Temporal/Clamp/首Atrous及每个后续ordinal的RGB/variance；先短资格证明observer不改037两分支原输出/物理resolve，再有限seed定位旧退化与帮助传播，新机制后才定候选。不得放宽硬门槛或扫描参数/坐标特判，不重采旧MC/无目的大矩阵；F39/static/复杂动态/1080完整预算、原全部目标和每步review/过滤提交/踩坑/交接约束继续active。


### 038预审（2026-10-23）：同源stock/固定037策略的逐轮RGB/variance证据

上一轮是有效进展：同源比较真实42%/31%总体收益与3旧通道硬退化改变下一步，原生产默认和完整目标保持。只新增Editor观察，不引入新算法/参数，不重新接纳F58候选。复用037归档的exact9/10 DIFF body/wrapper/物理resolve，在隔离项目Assets生成、仅branch1使用；两branch Native输入/时间/settings/OUT隔离继续。对实际Pre/Temporal/HistoryFix/Clamp/首Atrous/4后续ordinal用单独GPU gather读取原RGBA，不给滤波shader注入debug UAV。原27点保持，加196141为28点，新增参考与full/RAB/history/ray guide需验证。

先16warm16观察×2seed短资格：gather tiny/large F32拷贝112分量bitexact，原baseline/候选完整physical mean map与037同模式逐byte同、branch0对main demod输出逐帧同、物理resolve对发布逐帧同，所有实际Native writer数/观察恢复/编译绑定debug资格齐全。若失败先修观察，不能解读质量。通过后只同4个固定seed64warm256观察的新阶段证据（非新算法质量接受），原全部28点/34参考/full/phase/noise比较保留且不得改原门槛；全域发布对037应继续exact。候选本轮仍拒绝生产，不跑新MC/8seed/HDR/Player/GPU。

读前后RGB/variance以定位旧196141 B、141157 G/B退化在哪阶段出现、原有后期恢复是否被抑制，同时对照18亮边，不能只挑退化点或把final alpha(historyLength)当variance。方差保留原NRD单位（physical variance不是逐通道乘albedo平方），最后pass alpha独立标记。若无可区分机制证据，不开新候选；不得扫phi/epsilon/迭代/ratio/坐标特判。每步前后审查/保留失败/白名单提交和交接继续，Ho-GD计划排除，全质量/F39/static/复杂动态/1080完整预算与目标active。


### 038终态（2026-10-23，待过滤提交）：同源逐轮RGB/variance资格过，旧反例在后四轮恢复不足

**范围/资格：** 新Editor HoRTGIPairedStageAudit/meta、独立Gather shader/meta及Castle/LocalStage/SharedInput可选接线。原生产stock5/scale64/GI32F、Runtime/vendor/DI未改，037固定策略只在隔离Assets按5份原字节/SHA重建，用于观察，不接纳F58。保留原27点+196141=28点；两branch共享实际IN/common/settings/time，独立历史/OUT/pools/constants；只在原dispatch之后gather真实RGBA，不给滤波body注入debug UAV。112 tiny/negative/large F32分量自检bitexact，9stage原27点稀疏读取重调制对main F32整图copy逐帧bitexact。

1108短98请求/32观察帧、18596正式1314请求/1024观察帧 actual0，原4seed64warm256；API/compile/Property/UAV/fence/drift0，自有进程全终态/active=null。新增196141 full/RAB/history/ray/reference guide max9.336e−7。两branch完整raw/pub mean maps、候选原27点逐帧物理pub对037同模式逐bit同；branch0对main每帧demod RGBA同，物理GPU发布校准1024次0差。此资格允许解释阶段，非策略接纳。

**发现：** 28点Pre/Temporal/HistoryFix/Clamp全部RGBA两支相同；第一Atrous新18 L1 stock.130687/DI.130145近似同，后4轮stock .215150/.306872/.386896/.462370，DI .159167/.192195/.225485/.266351。旧141157 G/B首轮近似同，B/ref .82590/.82572，末.99666/.91288；196141 B首轮.75986/.75822，末.94380/.87350。原三通道物理pub硬退化精确同037，拒绝不变。收益与旧反例损失主要都在后4轮，不能归因早期history或只看整体误差。

首轮实际variance141157 stock7.276e−8/DI7.588e−9、196141 3.004e−7/8.064e−8；但196141 step4/8 DI反而约3.3/5.6倍stock仍恢复弱，排除“所有位置都只是variance太小”的笼统修复。141157 Clamp9邻真实moments离线重构preblur原/DI mean7.456e−8/6.673e−8、phi2带宽mean .0005026/.0004760、floor均0，首轮RGB几乎不变；恢复首轮blur不足以解释完整退化。尚不能把w²/min-pair/RGB/exchange各自因果分开。

**语义/边界：** Pre alpha hitdistance；Temporal/Clamp alpha二阶矩（离线variance近似，不冒充GPU直出）；本轮settled HistoryFix fast/responsive alpha全0，不当variance；9/101/102/103实际variance；104 historyLength禁止当variance。不逐通道乘albedo平方换variance。图表末stage是NRD RGB重调制、非物理RGBA16F最后量化值。4seed经验/同模型参考不是正式CI/独立真值/全图MC。没有新算法/MC/8seed/HDR/Player/GPU资格。

**过滤/成本：** Tests~/RTGI/PairedStages保留264原源对象、2完整logs、38 lossless captures、HGB1两支9stage28点RGBA、observer/analysis/figure/contract/filter-review/verify，全部原SHA与不变性核过。合格Editor观察工具保留，只测试入口开启、默认stock/noop路径不变、finally恢复；新增values8064B+coords224B只是sparse payload，外加2NRD/RT/读回不作产品VRAM/性能预算。可选Matplotlib图初默认/内置Python缺库，pip user与target冲突后显式--no-user隔离target装，仅plot进程路径使用，原环境/Unity不动，失败与版本/图SHA保留。白名单只4新Editor文件/3既有Editor接线、本轮证据和六RTGI文档，Ho-GD计划排除。

**039直接动作：** 现有实际证据允许一个单算子消融：仅把后4轮的固定center留重恢复stock行归一化，其余037 Smem temporal-var/w²、min-pair/RGB/settings/5轮/单位/格式不动。检验固定交换是否压制有益后期恢复，不预称修复。保持mainstock，把两独立branch设固定037参考/单算子变体以same input比较；物理校准改用main输出，不能沿用branch0==stock断言。先短资格两支stage9 RGB/variance bitexact、第一后轮同输入处出现算子差，再固定4seed旧15+新18/暗控制/full/phase/noise原门槛；失败保留消融并撤回，不扫描常量/迭代/坐标特判，不额外seed救回F58。所有原质量/F39/static/复杂动态/1080完整预算与总目标active继续。


038已按白名单过滤提交`c0ad3e2`，PairedStages verifier提交后再次通过。两自有Unity实际0且终态、112 F32 gather分量/1024物理校准/原27点九阶段copy逐帧bitexact、两支完整mean map和候选原27逐帧pub对037逐bit同；新增196141 guide通过。Editor观察工具合格保留、只专用入口激活，Runtime/vendor/DI/生产默认未改，F58仍拒绝。264源/38captures/2logs/阶段图与失败依赖记录完整，index空，仅他人Ho-GD计划未跟踪。

039以新阶段证据做单交换算子消融：固定037参考与只恢复后轮stock行归一化的变体，保留first Smem temporal-var/w²、min-pair/RGB、5轮/scale64/格式/参数。mainstock继续；两branch改参考/变体时务必同步物理校准和观察断言，不得沿用branch0==stock，优先同源三输出避免历史配对。短资格stage9两实验支RGBA相同、stage101同输入出现唯一算子差，再原四seed/旧15+新18/暗控制/full/phase/noise原门槛；不能称已证明exchange唯一原因。失败撤回保留，不以额外seed、常量扫描或位置特判救回，合格才完整部署资格。全目标与原质量/F39/static/复杂动态/1080完整预算、每步前后review/过滤提交/踩坑/交接持续active。


### 039预审（2026-10-23）：同源固定DI参考与后轮归一化单算子消融

038为有效进展：原28点九阶段资格、滤波前完全同/首轮颜色近同、旧反例后轮恢复不足，允许有目的的算子消融但未证明唯一原因。保持main stock5，把两独立branch设固定037参考/只恢复stock行归一化的变体。只替换后4轮diffuse output交换算子一个block，variance传播随同原表达式恢复；其它Smem temporal-var/w²、pair/RGB、phi/历史/5轮/单位64/格式/采样不改，Runtime/DI/vendor不动。物理GPU校准用main输出独立第三RT，不沿用branch0==stock断言。原038观察路径保持。

先2seed16warm16观察短资格：冻结源SHA/精确单block逆替换回参考、112F32 gather、两实验支3/4/5/6/9全部RGBA逐bit同、101出现唯一算子差；reference全NRD输出逐帧SHA及物理mean map对038固定DI同，main亦不变；各Native5轮/guide/校准/恢复/无debug绑定编译错。通过才固定4seed64warm256原旧15+新18/暗控制/full/16phase/noise，三种输出same actual GI input/common/settings，避免跨进程推测。

机制预测：恢复归一化可能增强旧141157 G/B、196141 B的后轮修正；记录相对固定DI的误差恢复量，至少收回原stock→DI差的一半才支持该方向，但不能替代产品门槛。沿原new18 L1改善>=20%、旧合格点误差增幅<=max(5e-6,参考5%,2倍配对seed/参考SE)、full/16phase不得pass→fail、filtered/raw variance<.15静态筛选；旧dark未合格仍不算过。若不合格，保留因果消融/源码/全部失败，移除新模式和类、恢复038观察/实跑过滤资格，不追加seed或常量/迭代扫描救回；有资格再完整8seed/动态/HDR/Player/GPU。目标active及F39/static/复杂动态/1080完整预算、前后review/过滤提交/踩坑/交接继续。

039资格补强：初34400 actual0基础校准/28点读取完成，原源/结果保留initial-local-qualification，不将其补写成整图资格。为消融第一后轮归因新增两个F32 RT拷首Atrous输出，每观察帧完整RGBA262144分量逐bit比较/双SHA，再开正式矩阵；不是重跑失败或参数扫描。


### 039终态（2026-10-23，待过滤提交）：归一化消融确认取舍，拒绝并暂搁置局部公式试验线

**设计与验证：** mainstock、branch0固定037 DI参考、branch1只将后4轮diffuse交换block恢复stock行归一化，RGB/variance一起沿原表达式，Smem temporal-var/w²、pair/RGB/其它参数/5轮/64/32F不动。源码逆替换逐byte同、wrapper仅include名变；三输出同实际IN/common/settings/time和独立资源。物理校准独立main输出，不误用branch0==stock。

四自有进程34400初局部资格98请求/32帧、19860补强98请求/32帧、39728正式1314请求/1024帧、25112过滤038模式98请求/32帧 actual0、API/compile/Property/UAV/fence/drift0、全部终态/active=null。初资格不补写整图证明；补强两个F32 RT每帧比较首轮整图262144RGBA，正式268435456分量/1024帧全bitexact，差异从首个后轮算子开始。112F32 gather过；main与DI参考完整NRD逐帧SHA、参考28点九stage/物理mean map对038同，guide max9.336e−7。只归一化干预含后续variance反馈，不夸成唯一因素。

**机制收益/质量失败：** 旧141157G/B、196141B收回DI损失89.386/91.025/98.145%，原一半恢复预测过，无新硬退化。但新18 L1 .462545→.447479仅−3.257%（DI为.266596），旧15 .304146→.284750仅−6.377%（DI.208852）。full仍2/4、DI4/4；pooledRGB−4.483/−4.809/−4.548%、phase7/11/11/7→7/8/10/6无pass→fail但pooled6失败，方差×1.77/2.34/7.17/2.01、filtered/raw .00299～.00628。原20%new18门槛未过，eligible=false，拒绝生产，不继续8/HDR/Player/GPU；机制成立不能代替足够产品收益。

**过滤/交接：** Tests~/RTGI/ExchangeOperator约41.3MiB，272源、4logs、72lossless captures、初/补强/正式源与生成体/全部34点与full/phase/noise/图/contract/analysis/filter-review/verify保留。新增ExchangeAudit/meta与3个既有Editor exchange接线/入口移除、精确恢复1cb030f，过滤038实际资格过；Runtime/DI/vendor一直未改。诊断额外2F32copy/第三physicalRT/2NRD/读回不作产品预算。只证据和六RTGI文档白名单，Ho-GD计划排除。

**按用户规则搁置：** 029以来局部公式方向虽有机制认识、始终无可保留生产改进，暂搁置归一化/DI局部公式试验线，不再排插值/phi/epsilon/迭代/ratio或位置特判。质量缺口不关闭，未来新的结构性机制/参考链路证据再开。F58/F60继续拒绝，不把单一消融成功算目标完成。

**040直接动作：** 转原实际交付缺口。先核F39最小原生增长probe、018失败原源和当前引擎/host/官方诊断是否提供新机制；静态owned mirror/base0已在018做过，原Ho/纯Unity3→5也scratch错而initial5干净，不能重新抄017称新方案。禁止盲试flags/Static/recreate/delay/capacity或原样重跑15矩阵。无可行动新证据则保留static缺口，推进固定实例复杂动态Player/全帧成本独立验证（不能冒称F39解决），原质量/F39/static/复杂动态/1080完整预算与总目标仍active。


039已按白名单过滤提交`8b376bc`，ExchangeOperator verifier提交后再次通过。四自有进程actual0/终态，正式1024帧整图首轮RGBA全同、main/DI参考对038全NRD输出逐帧SHA同；单归一化干预收回旧点89～98%损失却只留新18约3.26%改善，eligible=false，变体类/meta/模式/入口撤回，038观察恢复实跑过。272源/72captures/4logs/全部收益失败/算子图/初局部与补强资格完整留存，Runtime/Editor无最终diff，index空，仅Ho-GD计划未跟踪。

按用户长期无可保留收益需搁置要求，局部归一化/DI公式试验线暂搁置，不接着扫插值/参数/迭代或依据现有两端数据再排公式。040改推进其它实际交付缺口：先读F39 StaticMesh README/归档源/原生HoRTAccelerationGrowthProbe与当前引擎/host/官方诊断，旧mirror/base0和原Ho/纯Unity3→5均scratch错、初始5干净，不能再抄canonical helper当新方案或原样重跑flags/recreate/delay/capacity。只有新的可行动证据才重开AS实验；否则显式保留static缺口，推进固定实例复杂动态Player和完整帧成本独立验证，不冒称F39解决。完整画质/静态/动态/1080预算及总目标和每步review/过滤提交/踩坑/交接规则继续active。


### 040预审（2026-10-23）：F39无新行动依据，固定实例复杂动态enabled-camera Player

039完成单算子取舍为有效进展，局部滤波公式线继续搁置。现Unity6000.3.15f1/RTX4060/driver617.42与018相同，已读最小probe确实Add后Build/同队列读回；Unity/API/Microsoft scratch/Unity内存文章及本版release notes有限核查未定位对应修复，不能据此宣称没有上游fix。native-audit保留URL/观察/界限；不重跑mirror/flags/3→5，F39仍缺口。

本轮另补固定初始实例动态Player，不冒称静态增长资格。原backbuffer夹具增加多可见接收面、连续骨骼+blendshape发光面运动、移动primary接收面和ray-only遮挡；保持原source base4/owned AS base0、Cutout、材质排除、RGB开关、Volume重新启用。新独立probe，真实serialized enabled camera/target=null/512屏幕、原生endCameraRendering与WaitForEndOfFrame截图对应，禁止手动render/SubmitRenderRequest/Volume.Update。两后端Release各<=10秒可见窗口（沿用户授权），build隐藏有界；先RG再compat，失败停止。

240连续帧：32静态raster，32GI合成，48连续几何运动，32恢复静止，16Cutout封闭，16蓝发光关闭，16红关闭蓝恢复，16Volume关，32重新启用/强度.5。每帧记录3ROI实际raw/filtered/screen RGB、frame/reset/temporal、scene/structure revision/instances/skin bakes、运动数值；固定初始实例数量，不增删网格或triangle数。连续运动按当前保守策略应reset/禁GI temporal>=46/48，并显式记录为收敛限制，不能说全场景时域质量已过。稳定阶段骨骼不重复Bake。运动蓝响应范围>max(.002,静态蓝10%)仅响应筛选，非动态MC/MSE。

保持旧screen sRGB->linear固定decode、每RGB合成误差<.015、封闭Cutout和发光关闭首帧raw/filtered消除对应旧颜色<.005、再启用reset及光照恢复。Screen同帧capture校准、连续camera、有效instances/cutout/skin、无D3D12/编译/绑定/UAV/fence/source drift、独立payload/PNG和精确旧warning才资格。复杂动测覆盖扩大但非真实大场景/环境混合/全图MC/1080完整预算；不把截图读回时间当产品成本。先审后测/后审收益限制/踩坑/交接/白名单提交继续，全目标active。


### 040终态（2026-10-23，待过滤提交）：两后端多接收面动态Player通过，证实48/48全局reset限制

**实际范围/收益：** 新ComplexMotion独立probe和Player builder可选入口（旧默认不变），全Runtime/Shader/DI/vendor/生产参数无改动。固定8初始ray instance、3owned skin snapshot、1Cutout；额外倾斜/移动primary接收面、持续bone+blendshape发光面、移动ray-only遮挡，末段Cutout/红蓝开关/Volume关开/强度.5。复用017已验证sourcebase4构建/ownedbase0代码，本步未重新逐mesh枚举该metadata；不是F39增长资格或复杂大场景MC画质。

RG build36184/Player10356、compat build35500/Player40688 all actual0，serialized enabled camera/target=null/512屏幕，240原生连续camera callback与同帧WaitForEndOfFrame截图，无人工render/request/Volume.Update。各240观察含Volume-off16，实际224 GI更新连续；生命周期4.384/4.331秒不是全process/GPU预算，外部10秒可见界限沿用户授权。每后端22checks、instances8/skins3/cutout1全程；48motion实际Bake48、StructureRevision恒2、停姿Bake0并恢复temporal。

三ROI全部RGB合成误差强度1 max.007227/.007213、.5均.005216<原.015；Volume-off屏幕恢复raster误差0。Cutout封闭首帧RGB raw/filtered0；蓝关首帧旧蓝max.0002688<.005、红关首帧旧红0，另一色保留，Volume重开首帧reset。RG/compat raw三ROI逐帧float相同，filtered最大差.0006831/screen.0004029真实保留。build仅原五条精确warning，runtimeD3D12/compile/Property/UAV/fence/drift0；原GB fallback缺省提示仍在、夹具全lilToon路径不证明fallback支持。18真实PNG已看RG合成和compat运动，受控色块/多表面/位移符合预期。

**新瓶颈/参考核对：** 两持续运动段均48/48 NRD reset、GI raw temporal0，当前全scene.Revision令NRD无时间累积，必须记为收敛限制，不能因响应checks通过称动态画质完成。已读并冻DI31/32过程、VisibilityConfidence/currentDI、PrimaryMotion与固定RTXDI FullSample NRD：DI直接忽略world reset虽降噪，阶跃变化像素L1 .5709>.35失败；后续真实同reservoir/light采样当前TLAS重求confidence才保留NRD。GI现在黑MV/白confidence且无同类动态保护。不能直接换StructureRevision/强设CONTINUE，GI reservoir保守失效继续；下一步先审同GI历史采样重求的元数据/RNG/primary对应与独立oracle可行性，再限域生命周期候选，不重开局部滤波参数线。

**F39有限审计：** 当前Unity6000.3.15f1/RTX4060/617.42同018。最小probe确实Add后Build，旧mirror/base0已做、原Ho/纯Unity3→5也scratch错、initial5干净。有限核UnityAPI/Microsoft scratch契约/Unity内存文章/本版notes未识别可行动新fix，不推断所有上游/后续版本均无fix，URL/局限native-audit保留；未重跑旧flags/mirror/growth矩阵，F39仍开。

**过滤/失败保留：** 1163去重精确source对象、4完整build/player logs、两套全payload逐文件SHA/无HoGIGpuProbe.dll、2原report lossless gzip、18PNG/analysis/contract/filter-review/reference/verify完整。初RG wrapper同ordered记录persist后再append成双entry，原JSON与源码保留initial-metadata，去重为一次真实build/Player；compat wrapper已修、无重跑RG。offline verifier最初把Unity空error字符串误当null，按无error文本修、原报告SHA不变。只保留Editor可选构建/本轮证据/六RTGI文档白名单，Ho-GD计划排除。截图/读回/反射JSON开销不叫产品全帧GPU成本，原全质量/F39/static/复杂动态MC/环境混合/1080完整预算及总目标active。


040已按白名单过滤提交`b4e8b65`，ComplexMotion verifier提交后再次通过。RG36184/10356、compat35500/40688四自有进程actual0/终态，各240真实camera帧/224GI更新、22checks、8固定instances/3skin/1Cutout，18真实PNG及完整两payload核SHA；Runtime/Shader/DI未改，Editor仅可选构建入口/场景。1163原源/4logs/初RG双entry元数据与修正保留，当前source看compat，旧RG按原snapshot。index空，仅Ho-GD计划未跟踪。F39同host/有限主源核查没有新行动依据，未重跑旧矩阵，不能称已解决。

041先沿用户要求看DI过程/固定参考而非再扫滤波：已冻DI31/32持续记录片段、DynamicVisibility/PrimaryMotion/VisibilityConfidence与代码；直接保NRD平均噪声虽降但阶跃changedL1 .5709失败，生产依赖同旧采样当前TLAS重求confidence。GI motion实测48/48 reset/raw temporal0是新收敛瓶颈，但当前GI黑MV/白confidence且未接相同保护，禁止一行改StructureRevision/CONTINUE。先逐字段核GI历史packet/secondary transport/来源pixel-frame/RNG、primary匹配、灯/环境域，建立同历史GI采样重求和独立oracle/同源比较所需契约；缺元数据明确记录，不能新采样充当confidence。保留GI reservoir保守失效、旧颜色/Volume硬重置，正确性和变化像素残影/噪声证据后才限域生命周期候选。归一化/phi/迭代公式线继续搁置，原全质量/F39/static/复杂动态MC/环境混合/1080完整预算及全目标与review/过滤提交/踩坑/交接规则active。


### 041预审（2026-10-23）：GI采样重求契约、真实SDK信息损失与CPU诊断ABI修正

040是有效进展：两实际Player证实持续运动48/48清历史，DI已有无confidence保历史的残影失败，不能盲改reset。已逐字段读实际GI secondary48B、GI32B Pack/Unpack/Combine、ShadeSecondary与T/S/Final。次级DI选中light/UV/invPdf只存局部变量，GI只留位置/法线/聚合辐射/权重/M/age；age不是来源pixel，SDK普通Store将misc/unused清零，T/S和final选样也未保留DI选择来源。因此即使当前secondary缓存可读，也不能从final GI包完整反推同次级DI采样；必须同NRD实际Spatial1 Shade样本对应，不把pre-S历史0当最终样本。

审计同时发现CPU HistoryFeedback/RuntimeProbe Packet字段标签顺序与SDK不符：SDK字节12=misc,16=LogLuv radiance,20=weight,24=normal,28=unused；旧CPU标签12=normal,16=misc,24=radiance。原32字节全包Equal仍覆盖全部word、复制/画质判据不因此失效，但旧normal/radiance分类计数和JSON标签不能按字面解释。先修未来诊断布局并加schema，历史源/报告不改。

本轮只做小GPU合约资格，不改Runtime/vendor/NRD/采样/重置：用实际原SDK Pack/Unpack/Combine验证32B与两CPU布局offset、保留misc的显式API和默认丢失、reserved word丢失；64固定quantile选择验证selection bool的外置ledger。另用独立双点灯Lambert+矩形遮挡CPU oracle构造旧GI字节完全相同却当前旧选择重求辐射不同的碰撞，以及换新采样会制造/漏掉变化的负对照；这是信息必要性/诊断模型，不是实际场景GI重求或动态confidence/AS验证。只D3D12小compute/no-AS，120秒有界隐藏Editor；源/日志/二进制/初失败保留，先后review/过滤提交/踩坑/交接继续。

产出需要明确可用/缺失字段、至少选择lightData+UV+invPdf+source-viewDir与sample-kind的可重建设计、空间/时间选择继承和实际final sample捕获位置、必要内存预算；不能简单写unused/misc后假设SDK自动继承，也不能新RNG冒充同采样。当前生产reset和GI reservoir保守失效保持，全部质量/F39/static/动态MC/环境/1080预算与完整目标active。


### 041终态（2026-10-23，待过滤提交）：同采样信息必要性/SDK GPU契约通过，CPU诊断字段标签修正

**源码结论：** 实际secondary48B不跟随GI选样，选中DI light/UV/invPdf未存；GI32只位置/normal/LogLuv聚合radiance/GI weight/M/age，age非sourcepixel。NRD来自Spatial1真实Shade，history0却pre-S2，不能互换。不能由位置/颜色反查来源，也不必重生成旧RNG/RIS候选：先保存原选中选择和DI invPdf（非GI weight），还需原secondary BRDF的source-viewDir/sample-kind、T/S来源继承与实际lastShade捕获、当前secondary匹配和两段visibility。详contract，尚非生产实现。

**GPU与独立oracle：** 20788初actual1，HLSL保留字point造成3 compile error，未写buffer后的CPU错不代表ABI失败；原源/log/report/binary保留。修名lightPosition、同步/后dispatch检查。45904小D3D12 compute/no-AS actual0、compile/Property/UAV/fence/debug/drift0。原SDK Pack/Unpack/Combine直接执行，64案例：32旧GI32字节碰撞而当前旧选择响应不同；64默认roundtrip应用misc/unused丢失；64quantile实际选B48次。opaque aux四word按select bool复制，独立sentinel不作transport权重，未证明真实Shade/高层T/S关联。双点灯Lambert GPU AABB与独立CPU中平面oracle max3.674e−8<1e−6；64同选择静态bitexact，换另一选择负对照32静态假变化、碰撞中16漏真实变化/16假变化。非真实TLAS/alpha/area/env/spec replay或confidence资格。

**修一个诊断实错：** SDK GI32偏移0pos/12misc/16LogLuv/20weight/24normal/28unused。旧HistoryFeedback.Packet与RuntimeProbe.Packet把12标normal、16标misc、24标radiance；未来已校正并加GI32-v2。两个CPU类型经实际GPU sentinel/Marshal offset核对，1024次逐word翻转保证Equal/Same仍覆盖32B。旧全包byte-copy判据和texture质量/能量结论保留；旧normalMismatch实misc、旧radianceMismatch实normal，未另记全局true radiance mismatch，只有全包0才可推全部字段同。原报告/源不改，两历史README加显式标签提醒，映射legacy-layout-review。

**过滤/成本/边界：** 保留新Editor helper/shader/meta、两个CPU诊断布局/schema修正、本轮证据及文档，不改Runtime/vendor/DI/NRD/重置。ReplayContract有262源对象/2完整logs/4lossless报告binary、初失败/独立oracle/源映射/contract/filter-review/verify，全部SHA核过。两个GraphicsBuffer逻辑payload75,776B不是总GPU分配；假设16B×3pool+48B lastShade在256²6MiB、1080约190.55MiB/相机，未含gradient/对齐/原生分配，16B是否充分仍待证，不直接申请1080。无Player/AS重跑，生产motion48/48 reset仍未修。Ho-GD计划排除，原所有质量/F39/static/复杂动态/环境/1080预算和总目标active。

**042直接动作：** 低分辨率真实GI T/S/Final来源继承观察资格，而非继续toy或放开reset。评审GI私有helper寄存器source-pointer/sidecar在Load/选中/Store传播，原SDK/DI不改、GI32字段/原结果须对照；覆盖初始、temporal所有分支/fallback、空间选样、S-off、pre-S packed history copy与Spatial1 lastShade，碰撞不能靠float匹配。证明真实关联与主输出无影响后才当前TLAS同旧选择重求/独立oracle/变化像素响应。保留GI reservoir保守失效和旧色/Volume硬重置，confidence正确性与成本前不改NRD生命周期；局部phi/归一化/迭代线继续搁置。

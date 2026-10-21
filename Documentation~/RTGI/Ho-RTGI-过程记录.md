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

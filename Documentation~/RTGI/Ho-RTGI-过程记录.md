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

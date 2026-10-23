# Ho-RTGI v0.1 实施规划

日期：2026-10-19。状态：源码调查后的实施建议；本轮仅规划，没有修改渲染代码，没有重新执行 Unity / GPU 验证。本文不把历史通过项视为当前画质或性能已验收。

同日补充：按用户要求复查 DI 过程文档与本机 UnityPathTracing 参考源码。**开工前置改为实跑参考 GI 并建立同场景对照，再按实际差距移植。** 具体参考入口、已识别差异和 DI 经验见文末“本机参考与 DI 过程复核”。已有 DI 对照关闭了 GI，不能作为 GI 已跑通的证据。

## 持续目标的强制交付条件（用户于 2026-10-19 补充）

目标模式持续推进时，以下条件与功能目标一起验收，后续压缩、自动续跑和交接不得省略：

1. **每一步过滤提交。** 先检查工作区和暂存区，只提交本步骤相关、自有且已评审的文件；保留并行 RTDI 与其他任务改动。不用 `git add .`，提交前复查精确 diff、路径和验证结果。
2. **每一步先评审再做，完成后再 review。** 记录必要性、参考依据、预期收益、实现是否正确、实测收益/退化、支持边界、成本和接纳/拒绝决定。诊断成功只计定位收益，不能写成画质改进或功能交付。
3. **持续维护过程文档。** 使用 [RTGI 过程记录](../RTGI/Ho-RTGI-过程记录.md)，每个步骤记录基线、修改、命令/入口、实际退出码、原始证据、复审与过滤提交。失败与已有门槛保留，旧“下一步”不是自动执行队列。
4. **长期无收益路线必须搁置。** 在 [踩坑与禁止重复尝试](../RTGI/Ho-RTGI-踩坑与禁止重复尝试.md) 记录失败条件、禁止重复的做法和允许重开所需的新证据。不能靠改阈值、换名字或更换统计口径重复同一实验。
5. **为多次上下文压缩及后续接手写交接。** 每步及重要状态变化同步 [交接文档](../RTGI/Ho-RTGI-交接.md)：目标/用户约束、已提交与未提交工作、最新有效证据、运行进程、并行改动、当前阻塞和下一项具体动作。接手先读 [RTGI 入口](../RTGI/README.md)，再读交接与禁试记录。
6. **目标完成必须同时交付功能与上述证据。** 不因为单个诊断通过、研究步骤很多或预算/上下文不足就标为完成；不在交接时丢弃未验收项。

目标工具的现有接口只支持读取、创建以及完成/暂停/阻塞状态更新，不能修改活动目标的 objective 字符串。当前活动目标已明确引用本文，因此本节作为该目标的持久化补充约束；后续不得为改写字符串虚假完成或替换活动目标。

## 目标与首版边界

把已有 Ho-RTGI 原型推进为可在真实场景中开关、观察、调节的一跳漫反射 GI：灯或天空照亮墙面后形成颜色反弹；屏幕外的墙和发光物仍参与照明；遮挡由世界空间求交决定。与 Ho-RTDI 共同工作，并保留 GI 单独运行的能力。

继续使用现有 GB/SB 与 Ho-Atmosphere，沿用当前 RTXDI SDK GI 实现和 UnityPathTracing-6000.3.15 参考。以独立 Feature、独立输出、独立历史推进。首版以 Windows / D3D12、静态 opaque 场景、可移动相机为正式验收范围，补基础 Cutout 遮挡作为后续实用性阶段。

首版不扩展为通用路径追踪器，不引入 DDGI / NRC / SHaRC，不做历史反馈多跳，不同时承担 specular GI、镜面反射、焦散、透明折射或 SSS。动态角色参与遮挡与动态角色稳定接收 GI 分开验收。

## 源码现状与差距

| 项目 | 已有事实 | 本次推进需要完成的部分 |
| --- | --- | --- |
| GI 链路 | `HoRTGI.raytrace` + `HoRTGI.compute` 已有 GenerateSecondary → ShadeSecondary → Temporal → Spatial → Final | 恢复当前基线，建立最终画面和收敛基准 |
| 主表面 | 直接读取共享 GB/SB，输出含接收材质 diffuse albedo / Lambert 响应 | 保持语义，避免合成重复乘主色；低分辨率时明确表面对应 |
| 二次表面 | 真正 TraceRay，读取三角形、UV、normal map、基础材质与两组 emission | 补生产支持矩阵、纹理过滤和高方差材质约束 |
| 次级照明 | 解析灯初始 RIS + visibility + emission | 缺二次命中点的天空直接照明 |
| 主表面天空 | miss 已经作为远处样本进入 GI reservoir | 与 RTDI 天空路径明确分工，防止重复计光 |
| 重采样 | SDK temporal / spatial 与 ray-traced bias correction 已接通 | 测量重连遮挡、Jacobian、能量、相关噪声与历史失效 |
| 最终输出 | 独立 RGBA HDR diffuse，A 为 receiver validity | 增加 raw / filtered、GI 独立合成、更多诊断视图 |
| 动态处理 | geometry Revision 或灯/材质签名变化会阻断历史；只做静态世界相机重投影 | 不能把清历史当作动态物体运动向量或次级样本跟踪 |
| 场景支持 | 共享 HoRTScene 已加入 skinned snapshot；GI 构造没有传入 Cutout 适配器 | 旧 GI 文档支持域已经落后；骨骼命中材质求值需实际验证，不能直接继承 DI 验收结论 |
| 降噪 | RTDI 已有固定版本的 NRD 集成 | GI 尚未接入；需独立信号、距离、历史和设置 |
| 资源 | 三份 32 B reservoir、48 B secondary、共享历史现为 20 B、RGBA16F 输出 | 1080p 核心资源含 block padding 约 342 MiB，未含 AS、输入和 NRD；旧文档的 16 B history / 332 MiB 需更新 |

主要代码入口：`Runtime/RTGI/HoRTGIRendererFeature.cs`、`HoRTGIContext.cs`、`HoRTGISecondaryMaterials.cs`、`Shaders/HoRTGIApplicationBridge.hlsl`。已有验证入口：`Editor/RTGI/HoRTGIValidation.cs`、`HoRTGISecondaryValidation.cs`。

## 先确定光照贡献的归属

建议增加明确的 GI 环境策略，而不是根据 RTDI 的开关在背后改变光照含义：

- **与 RTDI 配合：** RTDI 负责主表面解析灯和直接天空；RTGI 负责经次级表面反射回来的光，以及当前 RTDI 没有采样的发光网格直接命中贡献。GI 主表面天空 miss 作为零贡献样本计入，不能删掉黑样本再重新归一化。
- **GI 独立使用：** 可显式启用主表面天空 miss。它和“次级表面是否接收天空”是两个独立选项，不能用现有单个 `useEnvironment` 一起切掉。
- **二次表面天空：** 在世界空间命中点采样同一 Ho-Atmosphere HDR 和 PDF，带可见性计算其出射光。它属于天空 → 墙面 → 接收面的路径，不与主表面直接天空重复。
- **发光网格：** 首版保留 BRDF 射线命中真实 emission；小面积强发光体的重要性采样是明确的后续增强。将来 RTDI 支持 mesh light NEE 时再统一 MIS，避免两边重复统计同一路径。
- **强度：** GI intensity 属于显示/艺术控制；建议另有明确的环境照明倍率。RTDI 和 RTGI 若使用不同倍率，应让用户知道两类路径被分别缩放。默认使用同一环境能量基准。

不能把 RTDI 的屏幕结果贴回二次命中点；屏幕外仍须完整求值。次级解析灯只需要初始灯采样与遮挡，不需要为每个次级命中点再建立一套 DI 时空 reservoir。

## 实施顺序与每阶段交付

### M0：恢复可比较基线，固定贡献划分

1. 先在现有隔离参考 `.codex-research/UnityPathTracing-6000.3.15` 上实跑 GI；沿用 DI 已验证的启动依赖与 NRI 兼容处理，不重建无关组件。分别确认 Unity raw / RR 与 Native raw / RELAX 能实际启用哪些 GI 环节。然后在 Ho 的独立验证工程跑现有 secondary、GI RenderGraph / compatibility、DI+GI 同开入口；保留当前 SDK 和依赖版本。
2. 建立参考/Ho 的同场景、同分辨率、同曝光、同采样预算对照，增加无复用模式和离线高样本参考，区分有限采样噪声与系统偏差。先匹配 diffuse 支持域，再单独观察参考完整 GI。真实自动渲染帧另测；手动 Render 多次不能替代正常运动/时序验证。
3. 做小型白盒场景：白墙、红/蓝反弹墙、挡板、离屏反弹面、可控天空与 emission。保留最终画面、raw、validity、命中类型和简单分段时间。
4. 实现显式环境贡献策略。纯天空无反弹物时，组合模式开启 GI 不应再次增加主表面天空。

交付：参考 GI 的实际链路/输出证据、参考与 Ho 的可比较截图及差异表、当前基线报告、环境贡献开关与输出契约。单独记录初始采样、ReSTIR 后 raw、filtered、最终合成；参考启动或 GI 不工作时明确记录阻塞，不能仅用 Ho 自测代替参考对照。发现不兼容先修复阻塞，不重新开启 DI 画质研究。

### M1：补齐一跳光照，尽早获得可见结果

1. 补次级天空 NEE，尽可能复用现有环境采样/PDF；为 GI 单独绑定资源和参数。检查解析灯与天空候选的权重，不能直接将两套 reservoir 的权重相加。
2. 保持 cosine 初始射线 + SDK GI resampling 作为基线。先用漫反射次级材质建立能量参考，再决定是否保留次级高光；基础 reservoir 的 radiance 不携带完整方向性，尖锐次级高光必须限制支持域或粗糙度，并用对照实验验证。
3. 核查薄墙、背面、几何/着色法线不一致、自交偏移和重连 visibility。unsupported opaque 继续遮挡并返回黑贡献。
4. 核查有限 `maxDistance`：在有限距离内未命中不等于无限远没有遮挡。天空查询须有明确范围语义，避免远墙被当成天空漏光。
5. 接最小 GI 加法合成开关，沿用 DI 已验证的线性色彩转换、雾衰减、深度/接收域和透明物顺序。GI 不添加主表面 emission，不重复乘 albedo。

交付：真实场景里可直接切换的 raw 一跳 GI；天空经墙面反弹、彩色反弹、离屏 emission 和遮挡均有对照。最小合成只服务观察，后续再接 filtered。

### M2：接 GI 专用降噪，固定收敛质量

1. 复用 NRD 的版本、资源编排、部署与 shader 生成工具；为 GI 分配独立 denoiser instance、pool、history、reset 和参数。
   先保留固定版本原版 RELAX/defaults 在相同 GI 输入上的对照，再评估 DI private 派生 shader 与静止/变化历史策略；不把 DI 的预设或派生修复自动当作 GI 最佳配置。
2. 以已集成的 RELAX 路线优先做 diffuse-only 接入可行性核对；核实当前固定版本的 native wrapper / manifest 是否覆盖需要的 denoiser，而不是直接复制 DI 的 manifest。
3. 输入保留 GI raw，另输出符合 NRD 协议的 demodulated radiance + hit distance、normal/roughness、viewZ 和 motion。公开 RGBA 的 A 仍为 receiver validity，不能改成距离再悄悄破坏消费者。
4. ReSTIR 选中样本可能来自邻域或历史；距离应有明确定义与代表性验证。环境 miss 距离、黑样本、无效 receiver、零 albedo、HDR emission、薄几何和新显露区域分别验证。
5. 对比 raw / filtered / reference、时间响应和颜色漂移。ReSTIR 历史预算与 NRD 累积帧数独立控制，不靠无限加长历史制造静态截图的稳定性。

交付：raw 和 filtered 并存，可切换降噪；静态收敛、移动相机、新显露区域与关灯响应均可查看。合成改为选择 raw / filtered，而不是把二者相加。

### M3：实际场景质量与支持域

1. 先补基础 lilToon Cutout 的 any-hit alpha test，并使初始求交、次级照明 shadow query、GI 重连使用一致的遮挡语义；当前 FORCE_OPAQUE 路径需要同步调整。
2. 检查骨骼 snapshot 对命中点 vertex fetch、UV/法线、BlendShape 和变换的实际一致性。先验收其遮挡和反弹，并明确姿态变化会清历史；动态接收的稳定性另列能力。
3. 按真实场景使用频度补 Main2nd/Main3rd、MPB 等材质项；unsupported 计数、对象和原因应可定位，避免用户把黑反弹误判为收敛慢。
4. 核查双面/薄片、非均匀缩放、镜像、法线贴图与显式 LOD 0 的纹理闪烁。按实际问题决定 ray-cone / mip 过滤优先级。
5. 加诊断视图：初始/最终 GI、命中与 miss、次级材质支持状态、sample age、历史拒绝/重连可见性、raw/filtered 差异。

交付：一个用户实际场景的静态与漫游结果，已支持/已排除材质清单，含残余问题。不得把 skinned 进入 AS 等同于动态 GI 已完成。

### M4：性能、分辨率与 Player 交付

性能记录从 M0 开始；本阶段集中优化已定位的瓶颈。先量 AS 更新、TraceRay、次级灯采样/天空查询、temporal、spatial/final、NRD、合成和显存；使用真实 GPU timing，CPU 等待或 readback 时长只作辅助。

- 比较全分辨率与每边 1/2 分辨率。后者射线像素约为 1/4；核心 GI 资源按现有布局约 85 MiB，新增 NRD/重建资源另计，速度不承诺同比提高。
- 低分辨率不能只缩 dispatch：需要明确从全分辨率 GB/SB 选择代表表面，motion 与 spatial radius 的坐标单位，以及边缘/材质/validity 感知的重建。
- 测量每帧全场景扫描、全材质属性 hash 和 DI/GI 各持一份 AS 的成本。若成本显著再抽共享 registry / AS；DI 的 visibility material 与 GI 的 closest-hit material 不同，不能直接互换或强制共用同一 AS。
- 用实测决定 TraceRay 数、次级灯候选数、天空候选数、空间邻域、最大历史和分辨率预设。先修最大瓶颈，不并行扩展采样方案。
- 保留已有 Mono Player 路线，完成 RG / compatibility、Gamma / Linear、启停/多相机/分辨率变化、场景切换与资源销毁；D3D11 不支持分支应稳定关闭。IL2CPP / XR 作为后续单独验收项。

交付：目标机器上的 GPU 分段结果、显存、质量预设、实际 Player 漫游与可复现设置。具体毫秒预算在首次实测后确定，不能用 DI 的耗时推算 GI 达标。

## 最小验收矩阵与建议质量门槛

| 场景 | 要证明什么 |
| --- | --- |
| 纯天空 + 白色接收面 | DI+GI 没有重复天空；独立天空模式积分正确 |
| 有天光的封闭/开窗室内 | 次级天空反弹存在；挡住窗口后相应能量下降 |
| 红蓝墙 + 白色地面 | 颜色反弹方向和强度正确；filtered 不串色 |
| 离屏墙与离屏 emission | 屏幕外几何仍有效；相机转走不等于光源消失 |
| 薄墙、转角、近接触、远处挡板 | visibility、bias、范围语义不会造成明显漏光 |
| 小面积高亮 emission | 测出当前 BRDF 采样的噪声边界，不以 clamp 后变暗视为通过 |
| 移动相机、快速转向、新显露区域 | 不把旧亮斑长期拖到新表面 |
| 开关灯、换天空、改材质、移动遮挡物 | 历史及时失效；GI 和 denoiser 不保留不再存在的能量 |
| DI+GI+原画面、DI 替换+GI | 合成项可解释，不重复 emission / albedo / 主表面天空 |

建议在 M0 固定 ROI、相机轨迹、曝光、预热和参考样本预算后冻结门槛：

- 长序列均值：正常亮度 ROI 的每通道相对误差以 5% 为初始目标，同时给出 Monte Carlo 置信区间；接近黑色的 ROI 使用绝对误差。噪声过大导致无法区分偏差时增加参考精度，不能直接宣布无偏。
- 收敛：记录第 1 / 4 / 8 / 16 / 32 帧，报告误差与时间方差。以关闭复用/降噪作对照，建议第 16 帧明显降低噪声（ROI 时间标准差至少降低 50%），同时保持颜色和均值门槛；这是建议目标，尚未实测。
- 响应：以阶跃关灯或移除反弹面测量残留能量，建议 16 帧内衰减至少 90%；新显露区域优先拒绝错误历史。对缓慢物理变化另测平滑性。
- 性能：必须有有效 GPU 样本、画面分辨率、硬件和设置；未取得 GPU 样本属于未验收。不得仅凭一张静态截图宣称 RTGI 与 RTDI 同样快速收敛。

## 推进与停止规则

每阶段交付可见结果、最小必要报告和明确剩余问题。下一阶段围绕剩余的用户可见缺口展开，不以增加测试入口数量代替交付。

单个质量假说最多做两轮有对照的尝试：没有稳定收益就保留问题、退回基线并继续独立可交付项。只有能量错误、漏光或资源/生命周期错误等阻塞问题需要停下修复。小发光源重要性采样、次级方向性、多跳缓存和完整动态路径跟踪各自另立阶段，不自动膨胀为 v0.1 的前置条件。

建议实际开工顺序：**参考 GI 实跑与同场景差距定位（M0）→ 补齐对应链路并接画面（M1）→ 验证信号重建（M2）**，再根据实际场景表现推进 M3 / M4。第一阶段交付参考/Ho 的反弹光开关对照，随后形成用户场景里可用的效果。

## 本机参考与 DI 过程复核（2026-10-19 补充）

### 固定参考位置与实施原则

- 可运行参考副本：`.codex-research/UnityPathTracing-6000.3.15`。
- 对应 native / SDK 源码：`.codex-research/UnityPathTracing/RenderingPlugin`。它和上面的 Unity HLSL 分支是两条不同集成路径，不能只根据同名 pass 互换输入输出。
- 启动/既有补丁与基准复现：[Tests~/RTDI/Reference/README.md](../../Tests~/RTDI/Reference/README.md)。沿用进程级 DLL 搜索目录、现有 NRI 设备接口兼容补丁、NativeRayTracingTarget 注册和 WITH_NRD 合成设置。先保留参考工程已有本地修复与二进制，不为 GI 调查重新搭建 native / DLSS 框架。
- 早期用户已明确“优先移植提供的 UnityPathTracing 成熟链路，只补应用适配”，见[可行性调查 §15～17](../RTDI/归档/Ho-RTDI-RTGI-可行性调查.md)。这应继续作为 GI 的主路线。

### DI 过程中的具体经验

| 已读记录 | 实际结论 | GI 采取的动作 |
| --- | --- | --- |
| [参考收敛审计，第108步](../RTDI/审计/Ho-RTDI-ReferenceConvergence.md) | 同一受控 DI 夹具 raw 波动接近；Ho 原短历史是明显差距之一，静止历史调整后方差降到旧版约 1/19 | GI 先分开测 raw 与重建，原版 RELAX 同输入对照提前；不能默认剩余噪声都靠改 ReSTIR 解决 |
| [持续推进记录](../RTDI/归档/Ho-RTDI-持续推进记录.md)与[完成度审计](../RTDI/审计/Ho-RTDI-CompletionAudit-2026-10-15.md) | 多轮滤波实验出现静态小改善、动态退化或没有生产收益 | 先查实际阶段与参考差异；不直接重跑固定交换系数、颜色 guide、VNDF 概率等已拒绝试值 |
| [NRD 接入专题](../RTDI/技术/Ho-RTDI-NRD.md) | 核心 CPU 库/固定 shader 可以复用；Ho 已有 DI private 派生和特殊输入约定 | 保留原版与 Ho 派生的区别，优先复用资源/调度而不混入 DI 专用采样或降噪策略 |
| [距离输入审计，第95步](../RTDI/归档/Ho-RTDI-GuideDistanceAudit.md) | 组合光照 RGB 与 primary 距离脱节；“参考这么写”不等于距离符合当前 lobe | GI 逐项标明初始命中、重用后连接、环境及组合信号的距离来源；先捕获实际 alpha 再判断 |
| [运动协议审计](../RTDI/审计/Ho-RTDI-MotionProtocol.md) | 同应用帧多次请求与正常应用帧运动历史不等价，运动符号/深度语义也有差别 | GI 先验静态世界相机重投影；物体运动源在真实自动帧另验，不直接把 URP 2D MV 接进去 |
| [dispatch 参数快照，第85步](../RTDI/技术/Ho-RTDI-SettingsSnapshot.md) | 记录后继续引用可变 settings 会造成参数、signature 与资源不一致 | GI 当前仍保存 settings 引用，接入时补最小不可变数值快照，沿用 DI 的已验证做法 |
| [灯域历史失效](../RTDI/技术/Ho-RTDI-LightDomainHistoryReset.md)与[颜色凸裁剪](../RTDI/技术/Ho-RTDI-DiffuseConvexClamping.md) | raw 归零后 filtered 仍可能由历史/裁剪生成残色，重置也不一定解决所有环节 | 对天空、emission 与彩色反弹分别看 raw/Temporal/Clamping/输出；不将残色一概归因于采样 |
| [环境采样专题](../RTDI/技术/Ho-RTDI-Environment.md)与[辅助分层](../RTDI/技术/Ho-RTDI-StratifiedAuxiliary.md) | PDF 单位、灯域权重、黑事件和随机分层影响能量；DI 的改进有明确适用域 | 次级环境优先沿 SDK 既有候选域接入；不把 DI TripleAnalytic 辅助估计或其权重直接移到 GI |

第108步的参考对照明确关闭 GI、环境、jitter 与 PT accumulation。其成功证明本机参考 DI/RELAX/RR 可以实际工作，没有证明 GI 的完整链路、画质或对应距离输入已经正确。

### 已对照的参考 GI 源码与差异

下表路径相对于可运行参考副本；Native 的 `.computeshader` 包装与外部 SDK 文件依赖不同，后续应捕获实际编译依赖和执行设置，而不只读目录。

| 环节 | 参考入口与已读内容 | 相对 Ho 的判断 |
| --- | --- | --- |
| 调度与帧输入 | `Assets/RTXDI/Scripts/UnityRtxdiFeature.cs`、`NativeRtxdiFeature.cs` | 两边都有完整 GI 五阶段；Unity 后接 RR，Native 调度 RELAX 与合成。Host 的实际状态、开关和写入纹理必须分别核对 |
| 初始射线 | `Assets/Shaders/RayTracing/GI/BrdfRayTracing.hlsl` | 参考含 diffuse/GGX lobe 选择、混合方向 PDF、delta 旁路和独立 throughput；Ho 当前固定 diffuse。先匹配 diffuse 对照，不因参考支持高光就扩大 v0.1 |
| 次级照明 | 同目录 `ShadeSecondarySurfaces.hlsl` | 参考通过统一 `RTXDI_SampleLightsForSurface` 参数支持局部/无限/天空候选，还可从投影到当前 GB 的次级表面复用 DI reservoir。Ho 当前仅设局部候选；优先补灯域适配，屏幕内 DI reservoir 再复用列为可选优化 |
| 次级粗糙度与 firefly | `BrdfRayTracing.hlsl`、`ShadeSecondarySurfaces.hlsl` 和 `Assets/RTXDI/RtxdiSettingPreset.asset` | 参考有 `minSecondaryRoughness`、radiance firefly clamp；Ho 保留原 HDR 且没有该 clamp。参考更稳可能包含信号改变，应分别控制比较，不直接以变暗作为改进 |
| 时间重采样 | 同目录 `TemporalResampling.hlsl` | 参考从 motion texture 读输入，并将 maxReservoirAge 乘以 0.5～1 的随机数以错开过期；Ho 由静态世界矩阵推 motion，年龄统一。先对比旧样本死亡/新显露区噪声，再判断是否移植年龄策略 |
| GI 参数 | `RtxdiSettingPreset.asset` 的一组参数 | 可见 fallback/permutation、历史20/age30、空间2样本/半径64、final visibility/MIS开关。它是资产记录而非已确认的运行时配置；Ho 不能照抄阈值与半径后宣称对齐 |
| 最终着色 | 同目录 `FinalShading.hlsl` | 参考可保留初始样本并做 final MIS，同时输出 diffuse/specular；Ho 是 diffuse、无 final MIS。先量化对应 diffuse 结果与初始样本成本，镜面用途后续再做 |
| Unity 输出 | `Assets/Shaders/RayTracing/ShadingHelpers.hlsl` 的 `StoreShadingOutput` | 写合并后的 `gOut_DirectLighting`，Unity feature 随后将其送 RR。只截最终纹理无法分清 GI、DI 和 RR 的各自收益 |
| Native 输出 | 外部 SDK `Samples/FullSample/Shaders/LightingPasses/GI/FinalShading.hlsl`、`ShadingHelpers.hlsli` | 最终 GI 调用 StoreShadingOutput 时 lightDistance=0，helper 会读取 prior diffuse/specular 并沿用对应距离，最后按 WITH_NRD 打包；并非天然提供“GI 独立几何命中距离”。必须检查实际编译版本及运行绑定，不把它无条件复制到独立 GI denoiser |

这些是本轮源码与文档调查结论；尚未执行 GI 模式的新 GPU 对照。尤其参考的部分机制改变了支持域或能量分布，需要把实际收益与代价分开记录。

### M0 参考对照的具体做法

1. 扩展独立参考探针，显式开启 GI，固定 ReSTIR、denoiser、jitter、曝光、分辨率和实际渲染帧；复用既有 DI 参考测试的启动办法，不覆盖原 DI 报告。
2. 从单灯照亮彩墙、彩墙反弹到白色接收面开始。双方匹配几何、法线、材质、光源和相机；先关闭环境、emission clamp、高光 GI 及额外次级 DI 复用以减少混杂，再逐项恢复参考功能。
3. 对照 BRDF 初始样本、ReSTIR raw、filtered、最终图。若参考只输出 DI+GI 合并结果，在最小探针里于阶段边界捕获增量/独立分量，不能把整个参考画面当作 HoGI 的参照。
4. 同时记录 GI 时空模式、age、初始样本 PDF、实际次级候选预算、NRD/RR 开关和历史时长、reset 次数。文件中有选项但 GPU 未实际消费，不算对齐。
5. 分两类比较：在共同 diffuse 支持域上定位实现差异；在各自原生完整配置上观察最终效果与成本。参考完整 specular GI + RR 与 Ho diffuse-only raw 不作同质量结论。
6. 根据测得的主要差距再更新 M1/M2 顺序。如果主要差距已经在重建，原版 RELAX 同输入对照先做；若缺次级天空/灯域，则先移植对应既有采样接口。暂不为每个参考选项新增产品设置。

## 2026-10-20 实施状态更新

开工前调查表中的“Ho当前”描述为当时状态，现按过程步骤更新：

- M0：参考GI Unity/Native已实跑，同支持域及独立墙面积分已做。Ho通过初始5%门槛，参考仍有偏暗差异，F12保留失败并暂缓唯一根因调查，禁止反调Ho增益贴齐。
- M1：次级天空和显式直接天空归属、可开关/调强度的正常相机加法合成已接入；Editor多颜色空间/路径有实际证据。
- M2：步骤005 `95120a4`原版RELAX_DIFFUSE独立实例/信号/历史与raw对照已有收益；步骤006两个Linear D3D12 Mono Player各304次请求覆盖真实应用帧、非均匀反弹、相机/几何/emission响应、Volume和启停。红/蓝均值约-3%、空间L1约10.35%、方差降低约99.89%，细节见Tests~/RTGI/Dynamic。
- 本轮Player为每个真实Update一次请求，**不等同正常enabled-camera自动呈现**；动态表面motion未实现，几何变化仍保守reset。M2复杂薄边/小高亮源、M3实际场景支持、M4 GPU预算与完整Player矩阵仍未完成。
- M3进展：步骤007独立天空壳材质过滤已交付；步骤008基础Cutout的初始/照明/复用bias/最终真实alpha与NRD距离、半开孔洞能量已有10项Editor及两个Mono Player证据。仍缺实际骨骼命中/漫游、完整目标场景和有效GPU预算，M3/M4不整体勾完成。
- M3后续：步骤009已有独立矩阵对应的骨骼材质hit、UV/normal map/BlendShape及非readable source反弹/遮挡，六项Editor/两个315请求Mono Player通过。生产Runtime未因测试修改；复杂角色动态接收、实际场景漫游和M4有效GPU预算仍待交付。
- 实际场景：步骤010古堡四组接入和GI开关图通过，但逐RGB时间均值filtered质量门槛失败；长窗原版约低6～7%，sigma4/6两轮仍失败，明确搁置更宽色盒，原生产设置不改。该项未验收，不能以有效像素/变化像素重定义完成；有效GPU取证与质量缺口继续。
- M4取证进展：步骤011严格GPU timestamp资格和古堡分段实测过，256/512两路径、AS/PDF重建与Total-only对照有有效fence/tick；仅限定Editor graphics-queue区间，未据此完成显存/1080p/半GI重建/正常自动camera或完整Player预算。010质量失败继续，不修改阶段成功定义。
- M4资源：步骤012实际256/512去重payload16.588/65.151MiB、AS API16.523MiB；1080布局推算513.997MiB。HD连续Unity fence错误后仅终止本任务进程，未取得HD报告、compat未执行，稳定性能预算不接纳。F31保留条件与禁止原样重试；[半GI输入/历史/重建设计](../RTGI/Ho-RTGI-半分辨率设计.md)已写，尚未实现或验收。过程/失败/交接和过滤提交要求继续。
- 半GI前置：步骤013的私有网格helper已实际GPU验证输入/历史/NRD投影和负对照，RG/compat各12case。尚未接入生产GI或重建，不算半GI功能/性能接纳；下一步按同合同接GI私有桥与全域输出，010质量和012HD失败不解除。
- 半GI实验结论：步骤014真实实现后RG/compat各169请求，平面/历史/full发布及57.497%payload收益过，但无代表点薄前景full约.992、half全0，原画质gate失败。原型已过滤撤回，F33搁置，未产品化。当前回到010质量和正常相机/完整预算，先调查低信号moment精度证据；不原样重复half路线，不解除010/012失败。
- 质量取证015：真实slow moment默认64出现约98%正亮而m2=0；两个成对单位对照改善矩精度但RGB仍原5%失败，生产/Player不改64。实际DLL别名确认HistoryFix为条件fast写，F34/35保留错误及停止纯单位调参。下一项补full图/16相位/局部阶段数据，原stride0失败不替换，整体未验收。
- 完整域016：full256²与16相位均原5%失败；DI同box凸裁剪候选虽减少负分量，却恶化RGB/L1，已移出产品、F36搁置。局部高波动提示下一项先做同支持域高样本无复用一跳参考/收敛，不能有限窗raw或子集代替完整质量验收。Runtime/默认NRD/Player未变，010/012与正常应用条件继续。
- 几何/参考017：调试层发现14处skinned非零base导致AS顶点声明越界，自有快照索引规范化令14→0、顶点/三角语义保持，Editor/两Release Player回归通过。合法AS后15/16选点参考收敛，暗控制点仍不确定，旧非法AS数据不接纳真值；full5%仍失败。静态非零base安全镜像、独立seed误差评估及正常camera/1080预算继续，F37/38保留。

每步review/过滤提交/失败保留/交接约束继续执行；目标保持active。阶段局部通过不能替代最终场景/GPU验收。

### 参考文献

- [当前 Ho-RTGI 原型说明](../Ho-RTGI.md)：包含历史 GPU 验证记录；支持域和内存描述需与当前共享代码重新核对。
- 本地主要参考：`.codex-research/UnityPathTracing-6000.3.15/Assets/Shaders/RayTracing/GI/`，以及仓库固定的 RTXDI / NRD 源码与 provenance。
- [NVIDIA RTXDI ReSTIR GI 集成说明](https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/RestirGI.md)：次级表面、初始方向 PDF、radiance、重采样与方向性限制。网页用于核对原理，不自动升级本地固定 SDK。
- [NVIDIA NRD 集成说明](https://github.com/NVIDIA-RTX/NRD/blob/master/README.md)：信号 demodulation、hit distance 与各 denoiser 的输入约定。具体实现以本地固定版本为准。

步骤018补充：静态镜像157功能checks后仍有AS scratch增长错误，候选拒绝并从产品撤回。直接Unity API也复现初始5 debug0、先3后5 debug1，尚未定位具体上游根因；Static/重建/构建flags/延迟/update调参均无收益，F39搁置。static非零base支持仍缺，下一项先在017合法古堡AS域做独立seed与T/S/none模式质量对照，不能用诊断通过替代功能/画质/预算交付。原目标和六项强制要求不变，active。

步骤019补充：RG古堡256²四模式×8seed×(64warm+256观察)真实exit0、API/compile/source drift0；TS raw相对none fullRGB+25.70/+22.21/+18.75%，T/S单独约+2.4～3.0%，旧filtered/raw5%全32段仍失败。参考15/16/选择偏差不解除，未修改生产渲染。下一项有界Editor pre-spatial history候选保留同帧T/S，必须以质量/方差/成本和完整回归取舍；原参考也反馈S，不先称索引bug。F40保留两次byte gate失败及精度更正，原5%、static base/HDR/正常camera/1080/完整预算继续未完成，目标active。

步骤020补充：三项×8seed TS实测pre-S packed history将filtered相对finite none的RGB偏移从+15.70/+11.58/+8.08%降至+1.22/−1.87/−3.28%，方差约−55%；原5%/局部质量仍失败。codec额外编码在测试域无差，否定其根因假说。GPU Final约+.018ms，Total相近但波动大，不作总性能/HD预算结论。当前仅Editor隔离候选，021直接生产接入和真实S开路径/RG/compat/动态/两Release Player回归；全目标/所有用户强制要求继续active，不能由局部收益complete。

步骤021：生产Final已在S开时packed Temporal2→history0，S关原store；当前T/S/NRD/资源不改。实际RG/compat合同、环境/Cutout/蒙皮与两个315-request Release Player过，新23状态资源合同覆盖真实S开。生产long相对finite none改善约+1.77/−1.37/−2.84%，原filtered/raw5%与16phase仍失败；core功能断言过但2304B scratch错误在legacy同样复现，debug资格未过。F42保留旧读点/构建警告与失败，下一步据实际生产stage处理余损/精度，正常camera/HDR/局部/static/F39/1080完整预算继续未完成，目标active。

步骤022：Editor-only GI四颜色池32F对照保持scale64/原算法，正式slow m2=0约99%→.026%、filtered方差−56%；stride0过但fullgreen仍−5.2075%、8/16phase失败。生产默认16F未改，额外2MiB@256/63.28125MiB@1080只payload投影。F43保留实际分配/capture错误与重跑，023先HDR/格式生命周期/GPU成本/两真实Player资格，再决定GI-only默认；DI默认不动，不重启F35纯单位扫描。全部未完成项与用户强制要求继续，active。

步骤023：GI-only pool+共用PrePass/output32F已接入，shared默认false保DI16F、单位64。原alpha输入/低signal失败与旧16F控制全保留；最终1e−4..20000、格式生命周期/两315请求Release Player通过。full绿色仍5.05869%及7phase失败，不放宽；额外79.10MiB@1080是投影，40样本非稳定目标预算。F44记录范围/成本边界，024优先正常enabled-camera连续呈现，不能请求式换名；全部原缺口/用户强制条件与目标active继续。

步骤024：两个真实serialized-enabled相机Player各240连续自动渲染，通过合成/Volume/重投影/发光重置，无手动请求。两hidden失败与用户明确授权后的短暂visible单变量成功都保留；Runtime无改动。targetRT输出不冒称backbuffer完成，025补默认屏幕目标与捕获色域/位置证据；full质量/7phase/static/F39/HD预算及全部用户强制要求继续active。

步骤025：默认screen目标已实际验证，两个512²Player各240自动渲染/截图对齐/固定色域/原强度与动态门槛通过，10张真实屏幕图保留。隔离模块缺失原失败保留，产品Runtime/依赖无改。屏幕功能可用性不替代复杂画质、原5%/7phase/F39/static/1080完整预算；026回当前生产独立seed全域/局部边界，全部用户强制要求与目标active继续。

步骤026：当前TS默认8seed实测与全部phase/固定64局部块/探索holdout map已完成，full绿色−5.15807%（配对seed经验SE/raw .11936%），合并门槛仍失败；6个x86结构跨seed保留差但raw不是真值。仅Editor测量增强，无画质修复。027先新坐标与相邻控制的同支持域无复用高样本一跳参考/guide，再定算法；所有原5%/phase/static/F39/复杂动态/1080完整预算与用户强制要求继续active。

步骤027：固定18新坐标每点1M同模型无复用一跳参考18/18原收敛过，确认中心filtered/reference约.28～.65；主guide/单位/depth匹配，具体NRD阶段未归因。只Editor取证，无画质修复/新GPU预算。028先同18点4预声明seed的实际NRD阶段与新参考L1，保留原全域/phase门槛，不将局部验证替代全目标；所有static/F39/复杂动态/1080完整预算与用户强制要求继续active。

步骤028：同18点4seed真实绑定/32F stage显示主要局部误差增加在首轮后的四次Atrous，首L1 .131→末.462；raw/pub与026同seed全图帧均值及局部均值完全匹配，原full/phase失败保留。仅Editor观察，无修复。029先一个有机制的GI私有atrous5→3候选，完整点/暗邻/全域/相位/噪声取舍，有收益再生产资格，无收益搁置不扫2/4；原全部范围/预算/用户强制条件继续active。

步骤029：单Editor atrous5→3候选，唯一settings4字节及真实Native3轮/恢复5已证；4seed新18点L1−33.6%、pooled full过、phase8→6失败，无预声明暗邻硬退化，方差+38.6%。仅进入更广资格，非生产/完整质量完成。030同固定3的完整8seed/旧合格参考与暗控制，再动态/HDR/两Player/GPU取舍；Runtime保持5、原所有范围/预算及用户强制要求继续active。

步骤030：固定3轮完整8seed虽新18 L1−34.5%、旧15加权L1−4.4%、pooled full过，但旧控制(141,157)G/B硬退化，候选拒绝/搁置；Editor入口已过滤撤回默认5且实际资格重跑0，Runtime未变。031先两类位置逐轮/邻域传播证据，不扫描全局迭代数/以子集收益换整体目标；原full/phase/static/F39/复杂动态/1080完整预算及所有用户强制要求继续active。

步骤031：原版27点逐轮与独立debug权重分离，确认两类位置相反传播，F51全局3继续搁置；发现后期luma floor激活差异作为新机制线索，未修复生产。032仅GI私有diffuse分母阈值按scale64对齐，保持5轮/实际单位/其它参数，完整控制和原门槛后取舍；所有F39/static/动态/1080完整预算与用户强制要求继续active。

步骤032：GI私有diffuse floor/64单候选失败（L1−3.9%、full2→0、11phase退化），已撤回/过滤后stock5实际资格重跑过，Runtime未改。033只复用现有权重/中心与样本方差作双向归一化核对，新可区分机制后再GPU，不重复常量扫描/旧矩阵；所有原范围、预算和用户强制要求继续active。

步骤033：离线forward/geometry与固定结构重放完成，无新GPU/源码改动；排除预测微小的luma对称化GPU方向，仅保留大步长factor2支持域的可测假说。034单Editor局部候选需实际新旧控制/full/phase/noise及后续资格，冻结重放不等于完成；全部原范围/预算/用户强制要求继续active。

步骤034：late factor2 guard名义收益弱且后两seed raw配对未满足，候选已移除、stock5实际资格复核，Runtime未变。035优先基线/无操作clone重现性和frame/light/sky/RIS/secondary核对，配对恢复前停算法试验；所有原范围/预算/用户强制要求继续active。

步骤035：当前stockA/B/no-op严格重现恢复，记录缺字段/编排失败停止修复，历史漏项/TDR/abort原样保留；小负载恢复与短资格通过后才运行有效矩阵。无画质/产品GPU预算结论，Runtime不改。036先同源输入双NRD基线资格再算法，所有旧质量/范围/预算与用户强制条件继续active。

步骤036：same actual GI input/common/settings的2独立NRD branch与baseline在1024帧全RGBA bit同，小资格证明reset隔离/恢复；初URP初始化与wrapper退出记录失败保留。仅Editor hook，无Player行为/画质/预算改变。037依M2同输入评估GI-private DI diffuse派生bundle，先exact差异与全控制资格，所有原范围/预算/用户强制要求继续active。


步骤037：同源GI实测DI diffuse bundle，新18/旧15总体reference L1改善42%/31%、full2→4/4，仍旧196141 B与141157 G/B硬退化，已拒绝生产接纳并撤回Editor覆盖；原stock5过滤复核过。1024帧物理GPU resolve对生产发布bitexact、全部数据/原源/取舍见[DiffusePolicy](../../Tests~/RTGI/DiffusePolicy/README.md)。038先双分支逐轮观察定位旧退化，不能放宽原门槛/直接移植；全部原质量/范围/预算和目标active。
用户每步过滤提交、前后review/收益取舍、长期无收益搁置、踩坑禁重试和跨压缩交接要求继续约束后续；短暂可见Player授权保留，本轮拒绝候选无需再跑该部署资格。


步骤038：同源stock/固定037策略28点×9stage实际RGBA观察合格（1024帧输出对037逐bit不变、112分量F32读取自检、全部stage对原整图copy匹配）。旧反例首轮颜色近似同，后4轮有益恢复减弱；196141后期variance更大仍恢复弱，不能只泛调variance，F58继续拒绝。[阶段证据与图](../../Tests~/RTGI/PairedStages/README.md)。039只一个后轮交换算子消融、其它保持，先短资格再原全部控制/门槛；原质量/F39/动态/完整预算与目标active。


步骤039：同源单归一化算子消融，整图首轮RGBA1024帧逐bit同，收回旧点89～98%退化却丢掉大部分总体收益（新18只改善3.26%<20%），已拒绝/撤回，038观察恢复实跑过。[算子取舍与原证据](../../Tests~/RTGI/ExchangeOperator/README.md)。按用户规则暂搁置局部公式试验，040先核F39原生scratch新机制，不重复018已做的mirror/flags等；无新证据则推进独立复杂动态Player/成本资格。全部原质量/范围/预算和目标active。


步骤040：两Release自动屏幕Player通过受控多接收面/bone+blendshape/移动遮挡/RGB/Cutout/Volume检查，各240camera帧/224GI更新、8固定instance，18实际PNG。发现运动48/48全局NRD reset，当前功能响应不等于动态收敛；已核DI31/32同采样confidence与直接保留历史的旧失败，041先审GI等价信号与oracle，禁止盲换StructureRevision。[实测与审查](../../Tests~/RTGI/ComplexMotion/README.md)。F39同host/有限官方核查无新行动依据未重跑，全部原质量/范围/预算与目标active。


步骤041：原SDK小GPU合约/独立oracle验证GI32无法唯一恢复旧次级DI选择、默认misc/unused丢失；保留同选择比新采样必要。实际GPU同时修正两CPU诊断字段标签，旧全包比较/texture质量不变，历史分项计数须旧schema解码。[采样契约与证据](../../Tests~/RTGI/ReplayContract/README.md)。042做真实T/S/Final来源继承观察后才动态confidence；未改生产reset，190.55MiB/1080候选开销待证，全部原缺口与目标active。


步骤042：真实古堡64²原版/lineage双GI两后端4T/S观察过，各128帧，1,572,864原包及raw/NRD输出逐bit同、2,097,152不可变来源检查，pre-S历史与实际Spatial1 Shade对应成立。capture保留字/UAV两失败完整，常规pass不覆盖age/Jacobian/视野/kind0/3或confidence。[来源证据与界限](../../Tests~/RTGI/SelectionLineage/README.md)。043补分支/生命周期，再当前TLAS同选择重求/oracle；生产reset未动，全部原缺口/预算与目标active。

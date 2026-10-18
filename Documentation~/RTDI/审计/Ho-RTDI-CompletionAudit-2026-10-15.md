# Ho-RTDI 完成度审计（2026-10-15）

> 状态：有日期/协议边界的审计证据。现行完成度与使用入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)；历史失败仍按原门槛保留。

审计结论：**独立 DI 光照生产端已具备可运行、可验证的实现；完整场景照明产品尚未完成。** 当前既有 DI 范围可称受限支持域的可用实现，不能称画质、性能和动态角色均已验收。最新第106步 RGB guide 实验已收尾并拒绝生产化，原 mixed 镜面偏差仍存在。

本轮按用户要求从持续滤波实验转向交付审计。代码基线为 `db6d8e8`，实验收尾提交 `32eb79b`；最近一次 RTDI Runtime 改动为 `f0bc793`。本轮没有修改生产算法、参数、用户场景或 GI。证据快照见 [CompletionAudit-2026-10-15.json](../../../Tests~/RTDI/CompletionAudit-2026-10-15.json)，保留入口、实际退出码、报告 SHA、通过项和失败项。

## 范围与完成判定

最初需求是用独立 RTDI/RTGI 改善场景打光并摆脱屏幕表示缺失。此前持续推进明确缩小为 DI 自身；最终 lilToon PBR/NPR 接收合成、通用 PT 不在该阶段范围，GI 仅做共享合同兼容与回归。审计同时列出这两个层次，避免把阶段范围完成误当最终用户目标实现。

| 交付项 | 当前状态 | 证据与边界 |
| --- | --- | --- |
| 独立 DI Feature 与 raw diffuse/specular 输出 | 已实现并验证 | SDK initial/temporal/spatial/shade、RG/compat、GB/SB 借用、逐相机历史和调试；本轮源码核对仍只发布独立纹理 |
| 解析灯与世界空间遮挡 | 受限支持域已交付 | point/spot/directional、屏幕外遮挡、刚性/骨骼 opaque、基础 lilToon Cutout；cookie、特殊灯光链接、面灯/发光网格等不在支持域 |
| 动态解析灯历史与索引 | 已实现并验证 | 真实 current/previous snapshot、双向身份映射、增删/换型/关灯；本轮真实 Runtime Cutout 动态9/9通过 |
| HDR 天空与 MIS | 已实现，过滤质量部分失败 | Ho-Atmosphere HDR、PDF/RIS、cos/VNDF MIS；本轮两个生产环境路径均2/4，mixed 镜面未过原5%门槛 |
| 功率提案、零 F0 弱彩灯、三最终样本 | 已接入生产并有具体收益 | Uniform/Single 保持默认；第93步弱彩灯与第100步辅助分层有真实 Runtime/Player 证据；不能推广为任意灯域或默认设置全通过 |
| 可选 NRD RELAX | 接入完成，完整画质未验收 | raw 同帧保留、派生 shader 可重建、材质/几何边界改善；mixed 镜面偏差与 lobe 距离代表性缺口仍在 |
| Win64 Mono Player | 受控部署已验证 | 历史 RG/compat Triple release 构建与实际 Player pass，DX11 inactive；本轮核对历史报告，没有重建，不覆盖 IL2CPP/XR |
| 实际场景几何支持 | 部分验证 | 古堡天空壳材质排除、合并网格和静态噪声有审计；不是实际场景全画质/性能验收 |
| 正常应用帧的物体运动输入 | 未完成 | 生产 NRD IN_MV 仍绑定黑纹理/world-motion 静态协议；第35步正常跨帧对应未通过，变换清历史/可见性 confidence 不等于正确物体运动重投影 |
| 目标场景 GPU 预算 | 未完成 | 历史计时报告 pass=false，frameSamples=0、frameGpuMs=-1；已知内存规模和同步端到端时长不能替代 GPU 分段预算 |
| 最终 lilToon PBR/NPR 合成与旧照明路由 | 未接入，此前明确排除 | 本轮搜索当前 lilToon shader 与 Runtime，DI 纹理消费者仍仅自身调试；正常 camera color 不自动获得 DI 光照 |
| GI 完整场景照明 | 独立一跳 diffuse 首版 | 有世界命中与 SDK GI 输出；本轮未重验。次级环境 NEE、specular GI、多跳、最终合成及完整材质支持不能由 DI 进展替代 |

不提供单个“完成百分比”：以上项目对最终可用性的权重不同，失败门槛也不能用新增入口数量抵消。

## 本轮确认的生产基线

Unity `6000.3.15f1`，隔离 `.codex-research/RTInputValidation`，D3D12，直接使用当前包。按入口分别启动隐藏 batchmode 进程，逐个等待实际退出；没有使用 `-nographics`，没有修改用户工程。

| 入口 | 实际退出 | 正式结果 |
| --- | --- | --- |
| `HoRTDIValidation.RunRuntimeMultiEnvironment` | 1 | 正交环境2/4，qualityPass=false |
| `HoRTDIValidation.RunRuntimeMultiEnvironmentCompatibility` | 1 | 正交环境2/4，与 RG 数值一致 |
| `HoRTDIValidation.RunRuntimeMultiDynamic` | 0 | 当前三样本 native Cutout 动态9/9，qualityPass=true |

生产正交 mixed filtered specular 红/蓝比值：无复用 `1.050288 / 1.048948`，时间+空间复用 `1.043948 / 1.050162`。每组有一个通道超过原5%门槛；临界超出也仍记失败。raw 同组红/蓝约 `1.003651 / .993275` 和 `.997251 / .996024`，说明这组正式失败在 filtered，不应把所有 DI 能量都归为不可用。

动态覆盖 balanced、100:1 powerTemperature、disableBlue、blueDirectional、redSpot、disableRed、allDisabled、restore、replaceGreen；九阶段全部通过。它证明当前三样本 Cutout 这组灯域有效，不代表 Single、opaque、所有分辨率或正常自动渲染帧全矩阵通过。

日志为 `.codex-research/completion107-RunRuntimeMulti{Environment,EnvironmentCompatibility,Dynamic}.log`。JSON 为 `rtdi-runtime78-env-baseline-{rg,compat}.json` 和 `rtdi-runtime78-dynamic-cutout-rg.json`。本轮确认文件确已更新，实际退出码与报告匹配；环境失败明确保留，不把“预期失败”标成产品通过。

## 最新实验处置与校验

第106步继承四个已完成进程及报告，不重复浪费构建：正交4/4；透视两路径2/4；动态1/3。稳定 RGB guide 改善了透视局部偏差，却让 rapid/step frameL1 升至 `.383257 / .402277`，原门槛 `.35`。因此只提交实验入口和拒绝结论，未进入生产。详见[实验收尾](../归档/Ho-RTDI-StableRgbGuideRejection.md)。

本轮重新执行：

- Runtime/Editor 分程序集 Roslyn 编译：0 errors；已有其它模块 warnings 保留。
- `generate_nrd_variance_adapter.py --check`：固定原 SHA 与四份 DenseDI 派生源码重建匹配。
- 29 份 RTXDI/Donut 参考文件：原 SHA 和去除 include 路径后的算法正文匹配。
- 本轮三个 Unity 生产基线日志及第106步日志无 shader error/warning；环境退出1来自正式质量断言。
- 全仓简易 shader reachability scanner 有13条 findings，**未通过**。它逐文件扫描，不能完整建模入口先后 include 和 Unity 内置 cginc；其中包含 DI、GI 及 AC/GD 调用。当前实际 Unity 入口编译正常，但不因此删除或宣称全仓静态检查通过。本轮没有修改这些生产源码。

## 为什么推进很久，完成感却不强

截至接手基线 `db6d8e8`，2026-10-09起与 DI Runtime/Editor/说明相关的103个提交中，59个以 `test(` 开头，39个触及 RTDI Runtime；两类可重叠，不是步骤完成统计。最近第101～106步都是诊断或拒绝候选，没有新的生产修复；最近生产收益仍来自第100步辅助分层。

已有实质交付包括功率采样、真实 NRD/Player、Cutout、RGB 边界、三最终样本、弱彩灯收敛和分层降噪收益。问题在于后续不断以一个新的质量假说接替上一个失败实验，同时把最终合成和正常帧性能长期留在边界之外。于是局部诊断增长，用户可见的场景照明交付没有同步前进。

把每个拒绝实验称为“完成一步”是研究记账；产品完成度必须回到上面的功能、质量与部署门槛。旧记录每节“下一项”从本审计起不再是自动执行队列。

## 收敛后的交付顺序

| 顺序 | 明确交付物与结束条件 | 约束 |
| --- | --- | --- |
| 1 | 正常自动渲染的固定场景/相机/分辨率基线，DI、NRD、visibility 的有效 GPU 时间和活动资源；计时确有样本，给出可复核原始证据 | 先解决观察环境，不再把 manual-request 同步时长冒充 GPU budget；不顺手改采样算法 |
| 2 | 一次范围明确的镜面修复提案，先写输入合同及生产基线，再同时守住正交、透视、动态门槛 | 第104～106步已否决的颜色/variance/phi变体停止自动延伸；若没有明确机制与完整收益，保留 raw/当前可选NRD和失败记录，结束该提案 |
| 3 | 对 lobe 距离代表性与正常帧运动输入分别作合同验收 | 不能用主样本灯距或稳定 guide 实验当合同已完成；额外查询必须记录预算 |
| 4 | 原始“改善实际场景打光”的后续阶段：明确接收材质、DI/GI/旧灯光各自路由、天空去重和合成前后对照 | 此项此前在范围外，应作为独立交付阶段规划；本轮审计没有擅自改最终 NPR 行为 |

本次接手任务以实验结案、生产基线复核和完成度审计为交付终点。尚未完成的产品门槛保留在表中，不因本轮审计结束而标为已修复。

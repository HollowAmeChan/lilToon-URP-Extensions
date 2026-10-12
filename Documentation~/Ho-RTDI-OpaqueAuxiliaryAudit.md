# Ho-RTDI：无 Cutout 的辅助样本执行审计

第68步仅补隔离 Editor 原型。生产仍保持单最终样本；不改变 lilToon 合成、GI、native DLL 或用户场景。

## 接入前提与参考

原三样本原型在 native FinalVisibility 内生成两个辅助样本，而生产仅在 `scene.CutoutCount>0` 时调度该 pass。全 opaque 场景不会进入该生成路径，不能据 Cutout 对照宣称三样本普遍有效。

核对用户 UnityPathTracing-6000.3.15 的 `Assets/Shaders/RayTracing/DI/ShadeSamples.hlsl`、本地 RTXDI `DI/Reservoir.hlsl` 和 HoRTDIApplicationBridge：最终样本需对应当前表面的 light sample、真实逆 PDF 和遮挡结果，零可见性不能删除辅助样本后再归一化。隔离原型在无 native FinalVisibility 时于 ShadeNRD 生成两次 RGB-power-CDF 抽样，用现有 inline conservative visibility，保留可见性为零的 reservoir，按 .5 primary + .25 auxiliary1 + .25 auxiliary2 求和。此 inline 路径仅验证 opaque，不能替代 alpha any-hit。

第二辅助仍使用独立 24-byte packed buffer；第一辅助复用 spatial 结束后的 slot2。CDF、第二辅助显式绑定 ShadeNRD。无 Cutout 的断言要求 native visibility texture 为空，避免用透明标记物强迫调度 native pass。GPU 读回核对两辅助 valid/index/inverse PDF、各自 visibility cache 及 primary cache，CPU 使用完整不透明 blocker 的解析遮挡。

## 实际结果

Unity 6000.3.15 / D3D12，256²，192 预热 + 256 观察帧，3584 点。RenderGraph 与兼容路径四组均通过，数据一致。

| 配置 | raw 红/蓝 | filtered 红/蓝 | frame L1 | changed L1 | NRD reset 帧 |
| --- | --- | --- | --- | --- | --- |
| spotRapid | .999146 / .999954 | 1.008522 / .996224 | .062609 | — | 256 |
| spotStep | .996436 / 1.001081 | .994646 / .996217 | .099089 | .075235 | 15 |
| manyWeakRapid | .999393 / .999252 | 1.015525 / .999398 | .062986 | — | 256 |
| hdrRapid | .999777 / .999602 | 1.017461 / 1.000273 | .059072 | — | 256 |

每组128次辅助实际 GPU PDF 检查；64对抽样同选30对，32灯27对。HDR scale=1/256。保持 raw 3%、filtered 5%、frame/changed L1 .35 的原门槛。opaque 使用 `tracedFrames==0 && resetFrames>0` 检查实际历史策略，Cutout 仍要求真实 confidence tracing；没有关闭或绕过既有门槛。

这里不保留每次 caster pose 变化前的 NRD 历史。rapid 每帧 reset，step 每次跳变 reset；三样本/不透明遮挡的空间结果因此不能和 Cutout 的历史保留质量直接比较，也不能据低 L1 断言统一历史策略已完成。`primaryFinalRays/firstAuxiliaryRays/secondAuxiliaryRays` 此入口没有启用 native counter，零值表示未测，inline queries 成本与 GPU 毫秒仍未知。额外 buffer 为1.5 MiB/256²，1080p约47.46 MiB仅估算。

原 Cutout 三样本 RenderGraph 四组回归均通过且保持第53步数值（rapid .324357、step changed .203049、32灯 .307974、HDR .308588）。混合环境镜面中心5%门槛仍失败，未借此宣称原型可以进入生产。

## 复查与下一项

报告 `rtdi-opaqueaux68-rg/compat.json` 位于隔离测试工程；两条实际执行 exit0，原 Cutout 回归 exit0，无新增 DI shader 诊断。首次导入因新 meta 的 GUID 长度错误被 Unity 拒绝，已改为工具生成的合法32位 GUID，再完整重跑；失败导入不计验证。Roslyn/GUID/whitespace 检查随本步完成。

下一项优先核对动态灯集与辅助 CDF 的帧同步：启停、排序、功率/类型变化与 buffer 生命周期。当前 CDF 只在 profile 开始时生成，不能直接迁入产品。之后才评估每相机状态、实际 Player 接入与成本；opaque 历史策略也需独立质量比较，保持 DI 范围与原有环境失败约束。

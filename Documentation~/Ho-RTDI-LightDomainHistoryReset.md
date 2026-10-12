# Ho-RTDI：灯集变化后的去噪历史失效

第70步把经过隔离对照的保守失效策略接入生产单最终样本 DI：灯集增删、同数量替换 Light 对象或 Point/Spot/Directional 类型变化时，重置该相机的 NRD 历史。普通位置、功率、色温及颜色变化继续走原有 confidence；ReSTIR identity maps、reservoir history 和 raw 着色保持。

## 问题与实现依据

第69步GPU读回证明关掉一个彩灯后对应raw通道已为0，但filtered仍残留旧颜色。单最终样本复现，故不能仅把它作为辅助采样原型问题处理。参考本地 fork 的 DI TemporalResampling light identity契约，以及固定NRD `Include/NRDSettings.h` 的 `CLEAR_AND_RESTART`；清除去噪的累积颜色与保留可重映射的ReSTIR样本是两项独立策略。

`LightFrame.domainChanged`在已有identity-map构建中计算：数量不同、新identity无旧映射，或映射到的旧 `radianceType.w` 不同。判断使用snapshot中的数值，不依赖会继续变化的Unity Light参数。RendererFeature把它加入既有NRD reset条件。未新增产品选项、持久GPU资源、native ABI或shader修改。

## 独立候选与生产对照

隔离候选只设置 `c.nrdHistoryValid=false`，未改raw采样。随后接入实际Runtime，在普通入口中核对每帧实际reset等于独立fixture的identity/type变化；并核对GPU双向mapping、按实际packed reservoir重构raw。production和候选前8组全部数值差0。

| 单最终样本关灯通道峰值（16帧后） | 改动前 | 候选／生产 |
| --- | --- | --- |
| Cutout关闭蓝灯 | .00108719 | .000496864 |
| Cutout关闭红灯 | .00101852 | .000581741 |
| opaque关闭蓝灯 | .000998497 | .000973701 |
| opaque关闭红灯 | .00218582 | .000586033 |

四项均回到原 `.001` 暗通道门槛内；opaque蓝通道仍接近门槛，不据此宣称没有滤波残留。每次灯集/类型改变仅首帧reset；功率/色温变化不reset。native Cutout遇reset不再trace旧confidence，该行为和原reset契约一致。

补充同数量替换green Light阶段，灯数仍3，GPU双向mapping对新/旧identity正确地给-1，首帧reset，后95帧继续积累。两可见性域的RenderGraph/compatibility实际策略检查通过；新增replacement在Cutout RenderGraph和opaque compatibility检查，能量门槛均通过。原ReSTIR raw数值保持。

## 质量失败未移除

新增单最终样本动态灯域测试整体仍exit1，9组中6组通过。100:1短窗口红raw1.263143、filtered红约1.187；方向光阶段raw红.968764略出3%，关闭红灯后存活蓝光filtered约.928/.933出5%。这些在改动前已失败；本次没有解决、放宽或删除。三最终样本候选仅用于历史对照，仍不接入生产。

64²动态漫反射跳变 `transitionChangedL1=.3523216385` 也超过 `.35`。专门在终止自有测试后暂时恢复两份Runtime文件到改动前HEAD，并完整复跑同一入口：原代码仍exit1，三组指标与候选逐项完全相同；随后恢复本步文件。该旧失败继续保留，不能称所有动态场景通过。

## 验证与部署

- Roslyn Runtime/Editor errors0；vendor原SHA及DenseDI派生check通过，native DLL SHA保持 `5EFF9D42D56DA8B38D305BC483D84263AEE3C31A6C6C3A0A637370DA68220EDB`。
- 两条 `RunReuseNoise` 各16组通过，两条完整 `RunPrimaryMotionQuality` 通过。
- 两条256/512动态漫反射各6组通过；两条64²动态镜面各3组通过。64²动态漫反射旧失败的改动前/后对照如上。
- 两份Win64 Mono release构建exit0；实际D3D12 Player RenderGraph与compatibility pass=true/exit0，包含关灯/recolor、边界、HDR、天空、Cutout及主表面origin。raw MSE约.050400/.050402，filtered约.000510/.000526；天空diffuse/spec filtered约.999130/.984608。
- 实际D3D11不支持分支pass=true/exit0，零指标表示功能未激活，不是零成本。

无新增DI shader诊断。构建仍有lilToon重复keyword，以及外部SolarHD/URP Shadows.hlsl的division-by-zero警告；不借此修改体积光。没有测得可用GPU毫秒。本次走既有 `CLEAR_AND_RESTART`，灯集频繁启停会丢失去噪历史并可能增加clear调度/短时噪声，应据实际场景继续评估。

报告 `rtdi-domainreset70-single-production-cutout/opaque-rg/compat.json` 及 `rtdi-domain70-player-*.json` 在隔离工程，原候选/原代码日志单独保留。`Baseline`方法名只表示单样本对照入口，它执行当前Runtime；改动前结论来自此前独立日志，不把当前入口冒充旧版本。

## 复查与下一步

这项改动针对动态灯集的历史正确性，不改变最终lilToon合成。接下来检查每相机DI状态与资源隔离，再推进采样原型接入条件。100:1短窗收敛、混合天空局部镜面偏差和64²动态diffuse跳变都仍是质量问题；额外采样显存/射线/GPU时间也未满足产品接纳条件。血月古堡的合并天空壳继续只作资产支持审计，保持独立质量fixture。

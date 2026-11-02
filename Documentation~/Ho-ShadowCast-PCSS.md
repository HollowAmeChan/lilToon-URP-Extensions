# ShadowCast PCSS：现状与坑

> 状态：**现行栅格化 ShadowCast 实现说明**，2026-10-28。角色 CS 的现行参数和采样见 [Ho-CharacterShadow](Ho-CharacterShadow.md)。
> 依据：URP17 **没有内置 PCSS**（源码 0 处命中，只有 PCF 软档 `_SHADOWS_SOFT_LOW/MEDIUM/HIGH`），所以 PCSS 是自研的；`Ho-ShadowCast` 的附加灯 atlas 与第二方向光级联都走它。
> 本页的边界仅适用于 Ho-ShadowCast；下一轮 RT 公共底座与 RTShadow 设计见 [Ho-RT](Ho-RT.md)。
>
> **2026-11-02 灯光互操作修复**：采集遵守 Light 的 None / 逐灯 Strength / Realtime-Mixed-Baked；Hard 不走 PCSS，Soft 保留组级 PCSS/PCF。投影对象遵守每灯 Culling Mask、独立 Shadow Rendering Layers 和管线/每灯 Bias。历史问题及其原始复现见[灯光与 Cast 审计](计划/Ho-Lighting-Shadow-Interop-Audit-20261102.md)，现行接收规则见下节。

## HoRP阴影模式

Ho lilToon 的「照明设置」提供 **HoRP阴影模式**，旧、新 Inspector 均可编辑，支持多材质混合值。源属性在 `CustomShaderResources/Properties/Default.lilblock` 和 `DefaultAll.lilblock`，不要只编辑生成的 `.shader`；生成器会覆盖生成物。内部值 `_HoRPShadowMode`：1=主阴影（默认），2=全阴影。旧材质缺少该键时使用 shader 默认值，故已有舞台若要继续使用附加 Cast，需要明确选择全阴影；本改动不批量重写用户材质。

- **主阴影**：保留 URP 天光 / HoCS / 当前 RTShadow Main Cast 的既有选择与优先级；普通附加灯的 HoShadowCast 不参与。附加灯挂 `Rendering/Ho-ShadowCast Light` 并勾选「加入主阴影」后，其投影同时进入主、全模式。
- **全阴影**：既有 Main Cast 加上所有合格的 HoShadowCast 附加投影。它是现有艺术投影合成语义，不是每灯物理 BRDF 的替代。普通附加灯不必挂新组件，仍自动参与全阴影。
- 此模式不改 Light 的颜色/强度/启用状态，也不改 `_MultiLightIntensity`。主模式仍能接收普通附加灯的颜色。全模式按照已有艺术合成方式衰减附加光与 toon 阴影；不意味着把一盏灯的阴影自动转换成正确的逐灯光能归属。
- `_ShadowReceive` / 第二、第三层的接收量仍决定 Cast 如何进入 toon 色阶；它们不是阴影来源选择器。烘焙到 Lightmap / Probe / APV 的明暗不会因为切换实时模式而被抹掉。
- Feature 的图集、分辨率、PCSS 预算仍由 HoShadowCast 管理；遵守每灯意图不意味着共用 URP 图集或精确复刻 URP 的 Soft Low/Medium/High。
- 本版仍不接管 URP 原生 additional-shadow atlas，也未实现未来 RT 全光源池的接替。关闭 HoShadowCast 后主阴影保留，附加 Ho 投影消失；不能把本版界面视为该 feature 已可退役的证明。

### 灯光资格

| Light 状态 | HoShadowCast 的行为 |
| --- | --- |
| Shadows=None / Strength=0 / Light 禁用 | 不申请切片；不改灯的颜色照明 |
| Baked | 不生成运行时 Ho 投影；已有烘焙结果由 Unity 保管 |
| Realtime / Mixed 且开启阴影 | 合格后申请切片；最终强度=Light.shadowStrength × Feature 全局强度 × 类型强度 |
| Hard | 单点深度比较；不走 PCSS |
| Soft | 使用组级 PCSS；关闭 PCSS 或软度为 0 时回退 PCF |
| Shadow Rendering Layers 单独开启 | 投影对象按 shadow layers 筛选，颜色照明仍按 lighting layers |

`Ho-ShadowCast Light` 只声明加入 Main；它不会强行打开 None 灯的阴影，不会让 Baked 灯产生动态投影，也不额外授予图集容量。Mixed 的 baked shadowmask/距离混合依然属于 URP 的原生数据路径；Ho 附加艺术投影不能当作完整的 Shadowmask 模式等价实现。

## 1. 形态

- **按 cast 组给软度，不按灯**：punctual 组与 second directional 组各有自己的 `softness`（默认 0.6 / 4.0），主光（URP 自己）不参与。
- **总开关 + 质量档**：`pcssEnabled`（默认**开**）、`pcssQuality` = `Low / High / Ultra`，只决定 blocker / filter 的采样数。
- **形状参数**：`pcssBlockerSearchRadius`（0.25~8，默认 2.8）、`pcssMaxPenumbraRadius`（1~32，默认 7.4）、`pcssDepthBias`（0~0.01，默认 0）。
- **常量**（每帧发布）：`_HoShadowCastPcssParams` = `(enabled, softness, blockerRadius, maxPenumbraRadius)`；`_HoShadowCastPcssParams2` = `(depthBias, blockerSamples, filterSamples, 0)`；第二方向光另有 `_HoShadowCastSecondDirectionalPcssParams`。
- **采样上限**：blocker ≤ **32**、filter ≤ **64**（`HoShadowCastShaderContract.PcssBlockerSamples / PcssFilterSamples`，与 HLSL 里的 `HO_SHADOW_CAST_MAX_PCSS_*_SAMPLES` 一一对应）。
- **PCSS 与材质模式分工**：PCSS 只决定可见度采样；材质的「HoRP阴影模式」另行决定消费 Main 或 Full。修改软度不会修改材质的来源选择。

## 2. 实现要点（`HoShadowCastSamplePcss`）

1. 出界（`sliceCoord.xy` 不在 0..1）直接返回 1（不遮挡）。
2. **近平面半径收缩**：`radiusScale ×= smoothstep(0.02, 0.12, nearDepthFactor)`；`nearDepthFactor` 按 `UNITY_REVERSED_Z` 取正。贴近光源近平面时若仍用满半径，会糊成一片。
3. **PCF 回退就是这一条分支**：`pcssParams.x < 0.5`（关闭）或 softness ≤ 0 或采样数 ≤ 0 或 `radiusScale` 太小 → 直接走 `HoShadowCastSampleManualPcf`。**降级即回退**，不是另一套 shader。
4. 采样偏移用**旋转图案**（`HoShadowCastPcssRotation`，按 atlas uv 出角度），filter 阶段换一个相位（`+1.731`），避免规律性条带。
5. 每个采样 uv 都 **clamp 到本 slice 的 `atlasMin / atlasMax`**——越界会采到邻居 slice 的深度，表现为“阴影边缘出现不属于自己的遮挡”。

## 3. 不做清单（已定的边界）

- 不做“每灯一个 PCSS 档”（按 cast 组即可）。
- 不做 PCSS 全屏后处理替代。

## 4. 坑

1. **C# 与 HLSL 的数值契约必须唯一**：采样上限、数组长度、档位、灯类型 id 全在 `HoShadowCastShaderContract.hlsl`，C# 镜像它，并由 `Editor/ShadowCast/HoShadowCastShaderContractValidator.cs` 在编辑器加载与菜单校验时解析比对**防漂移**。写死数组长度的调试 shader 也会被这个校验器抓到。
2. **全局数组长度是会话级缓存**：Unity 只在第一次分配时确定长度，之后只允许更小上传（>长度会报 "exceeds previous array size … Cap to previous size"）。所以 `ArrayLights = 48` / `ArraySlices = 128` 固定，**容量档只约束收集与采样循环、不改变数组长度**——这正是运行时切档安全的原因；**调大需要重启 Unity**，调小不需要。
3. **采样数上限是每像素成本旋钮**：Ultra 档也不能越过 32/64；越过会被 `min()` 截断（静默降质），所以在 C# 侧就要夹住。
4. **“光环 / 漏光”是 PCSS 的经典失败模式**：验收要求软阴影边过渡可调且无光环；bias 与 blocker 半径是主要旋钮。
5. **AOV `shadow.*` 不受影响**——换 PCSS 只是纹理内容不同，通道契约不变。

## 5. 验收

1. 软阴影边过渡可调、无 PCSS 常见“光环/漏光”。
2. 关闭时回退 PCF（降级即回退）。
3. AOV `shadow.*` 导出不受影响。

## 6. 与 CS / RTShadow 的边界

本页仅说明已实现的栅格化 `Ho-ShadowCast`。上面的组级参数、不做清单和 atlas 采样规则，不约束其他阴影模块。

- 当前主光角色局部高精度 shadow map、接收勾选、方向光角直径和连续滤波：[Ho-CharacterShadow](Ho-CharacterShadow.md)。
- 公共 Ho-RT host、独立逐灯 RTShadow、NRD SIGMA 与 NPR 接收已完成有界实现；配置、调试和验收范围见 [Ho-RT](Ho-RT.md)。

CS 的旧实现说明已合并至其现行文档，本页不再复制另一套 CS 参数或阶段调参建议。

## 7. 定点回归

`Tests/Editor/ShadowCast` 包含灯光资格、Main 池声明、Lighting/Shadow Layers 分离、每灯 Culling Mask、生成 shader 默认属性，以及直接 GPU 遮挡采样的回归用例。GPU 测试需要图形设备，不能用 `-nographics` 作为渲染通过的证据。

Unity Test Runner 需在测试工程 manifest 的 `testables` 启用本包。若只测试本模块，注意旧 ScreenProcess 测试在 Unity 6.3 的兼容性 define 条件；不要为了跑本模块改用户工程的全局兼容模式。

固定机位验证在独立 `D:/HoBakeAudit26` 工程中进行，`HoRPShadowModeValidation` 使用 capture 的 configure 回调覆盖本次加载实例的 MaterialPropertyBlock；不能在截帧工具重新打开 Scene 前覆盖旧实例，再将近似相同的截图当成模式通过。实测主／全模式命中同一场景 83 个材质槽；生成器重建后属性也必须仍存在。

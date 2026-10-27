# ShadowCast PCSS：现状与坑

> 状态：**已实现**（2026 文档审核核对）。文件名沿用历史（`*_PLACEHOLDER`），内容已更新为“已实现”现状；本文不再是占位。
> 依据：URP17 **没有内置 PCSS**（源码 0 处命中，只有 PCF 软档 `_SHADOWS_SOFT_LOW/MEDIUM/HIGH`），所以 PCSS 是自研的；`Ho-ShadowCast` 的附加灯 atlas 与第二方向光级联都走它。
> **CS（逐物体阴影）的现行实现见 §6**：方向光以角直径与世界遮挡距离估计半影，另有接收面外推保护，参数语义与 ShadowCast 不完全相同。

## 1. 形态

- **按 cast 组给软度，不按灯**：punctual 组与 second directional 组各有自己的 `softness`（默认 0.6 / 4.0），主光（URP 自己）不参与。
- **总开关 + 质量档**：`pcssEnabled`（默认**开**）、`pcssQuality` = `Low / High / Ultra`，只决定 blocker / filter 的采样数。
- **形状参数**：`pcssBlockerSearchRadius`（0.25~8，默认 2.8）、`pcssMaxPenumbraRadius`（1~32，默认 7.4）、`pcssDepthBias`（0~0.01，默认 0）。
- **常量**（每帧发布）：`_HoShadowCastPcssParams` = `(enabled, softness, blockerRadius, maxPenumbraRadius)`；`_HoShadowCastPcssParams2` = `(depthBias, blockerSamples, filterSamples, 0)`；第二方向光另有 `_HoShadowCastSecondDirectionalPcssParams`。
- **采样上限**：blocker ≤ **32**、filter ≤ **64**（`HoShadowCastShaderContract.PcssBlockerSamples / PcssFilterSamples`，与 HLSL 里的 `HO_SHADOW_CAST_MAX_PCSS_*_SAMPLES` 一一对应）。
- **材质零改动**：消费侧仍是 toon 门控 `shadow.main` / `shadow.add0..N` —— “换 PCSS 对材质零改动”，这是“材质不碰生成逻辑”的实证。

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

## 6. CS（逐物体阴影）的现行实现

CS 使用每个接收域独立的高精度正交深度图。逐相机参数来自 HoCharacterShadowVolume，RendererFeature 提供兜底值。

- 阴影软边（米）：额外的固定滤波半径；0 仍保留基础抗锯齿。
- 光源角直径（度）：字段名 pcssSoftness，范围 0–8，默认 2。0 关闭距离相关半影；方向光使用真实遮挡距离，不以虚拟近平面作为光源。
- Blocker 搜索半径（米）：寻找遮挡物的范围，不是软边宽度。
- 半影半径上限（米）：限制 PCSS 的世界半径；固定软边及基础抗锯齿独立生效。
- 质量档：Low / Medium / High / Ultra，对应 blocker/filter 次数 8/16、16/32、24/48、32/64。不再按采样预算缩窄世界半径；过大的范围依然需要足够的采样。

半影计算：

    gapMetres = averageBlockerGapDepth * (far - near)
    radiusMetres = min(maxPenumbraMetres, gapMetres * tan(angularDiameter / 2))

各 blocker 的 gap 先按接收面局部坡度校正。平移正交投影的近远平面不会改变实际遮挡距离；改变深度范围时，归一化深度差与 far-near 相互抵消。

采样契约：

- _HoCSPcssParams = (enabled, tanHalfAngle, blockerRadiusMetres, maxPenumbraMetres)。
- _HoCSPcssParams2 = (blockerDepthBias, blockerSamples, filterSamples, fixedSoftRadiusMetres)。
- _HoCSParameters[slice].w = 世界单位每 texel；_HoCSDepthParameters[slice].x = far-near。
- 先比较四个深度，再对可见度插值。支持 Gather 的平台一次读取四个深度；tile 边缘和不支持 Gather 的平台逐点夹取。
- 小半径使用精确积分的密集 tent PCF，按 texel 单元面积计算权重并合并相邻单元；全程 float。0 仍保留基础抗锯齿，正半径立即连续增加软度。额外半径 ≤2 texel 时最多 16 个双线性/Gather 四点读取；2–2.5 texel 平滑过渡到匹配二阶矩的固定对称盘。大半径保留稀疏盘，不依赖世界格子随机角或 TAA。
- 将世界位置导数线性变换到阴影空间，再求接收面梯度，避免对带平移的归一化 UV 做差。在实际采样 texel 中心修正参考深度。屏幕像素覆盖范围也作为滤波下限。
- 接收面不能无限外推：在 PCF 比较和 PCSS 搜索中，只有存储深度与外推平面的残差不超过约 4–8 个世界 texel 时才逐渐采用坡度补偿，否则回到中心参考深度。滤波同时累积同一批深度的未补偿可见度，约束补偿不能在未补偿核全暗/全亮时制造反相三角块。这是防漏光的启发式保护，不是几何身份判定；非常近的遮挡面仍可能与接收面混淆。
- Blocker 搜索包含中心；奇数子图案使用中心加完整反向样本对，避免未配对外圈导致偏心。对每个相邻 texel 先判断是否遮挡，再双线性加权累积遮挡覆盖率及深度差，不对原始深度先插值。第一份遮挡覆盖从 0 到一个完整 tap 之间平滑建立半影，避免刚出现极小 blocker 时半径跳变。没有找到 blocker 时使用基础 PCF，不凭空假设最大半影。

ValidateFiltering 使用真实 GPU 和 4096 纹理检验：连续过渡、Gather 与独立四点比较一致、斜面自遮挡、真实遮挡不漏光、相邻 tile 隔离、深度裁剪不变性，以及用户的 0.0001m 固定软边 / 0.072m 搜索 / 0.008m 上限 / 2° 参数。另扫 0、1、1.01、1.6、2、8、16 texel 的核半径，检验掠射接收面在独立遮挡面前后不产生反相块；PCSS 同样验证。ValidatePcss、ValidateRendering、ValidateDistanceRendering、ValidateSceneShadows 检查完整渲染链路。

密集核另与逐单元参考积分比较，并扫 0–4 texel / 八个边缘方向的过渡宽度，避免半径增加反而变硬；blocker 深度台阶、首个细遮挡物进入搜索盘均以 1/128 texel 位移检验连续性，并核对非 Gather 后备路径的 blocker 统计。端到端边宽取多个完整边缘剖面的中位数，排除剖面在到达 10%/90% 前出图的角点；原局部边宽与半影增宽阈值保持。

旧版本的 2px/22px 平面测试只证明过渡宽度可变，不能证明曲面抗锯齿正确；历史记录保留在 Ho-CharacterShadow-Plan.md §0.4–0.5。旧的“降低分辨率让软边变宽”建议已撤回。

限制：接收面修正是局部平面近似，不能替代足够的几何和阴影贴图采样。大范围低采样仍可能出现条带或漏掉细 caster；不把模糊作为恢复已丢失几何细节的方法。

lilToon 的接收发生在各层 toon ramp 前。某层 Blur 为 0 时，过滤后的可见度仍会被该层重阈值，可能把细微滤波波动放大为硬边；这是独立于深度图分辨率的材质响应。验证可在材质克隆上禁用第二/三层接收作对照，不自动修改用户材质。很强的 caster normal bias 与掠射面保护仍有耦合，投影边缘的宽核也可能受到 tile 内夹取影响；本轮不将这些未完整验证的路径称为已修。

# ShadowCast PCSS：现状与坑

> 状态：**已实现**（2026 文档审核核对）。文件名沿用历史（`*_PLACEHOLDER`），内容已更新为“已实现”现状；本文不再是占位。
> 依据：URP17 **没有内置 PCSS**（源码 0 处命中，只有 PCF 软档 `_SHADOWS_SOFT_LOW/MEDIUM/HIGH`），所以 PCSS 是自研的；`Ho-ShadowCast` 的附加灯 atlas 与第二方向光级联都走它。
> **CS（逐物体阴影）也走同一套形态**（2026-09-22 补）：见 §6。两边的开关/质量档/形状参数命名一致，但半影公式有一处有意的差异。

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

## 6. CS（逐物体阴影）的那一套

同一形态、不同数据源：CS 的图集是**每个接收域一张高精度局部深度图**，所以参数按"逐相机"给（`HoCharacterShadowVolume` 的「软阴影（PCSS）」分组，feature 上是兜底值）。

| 项 | CS | 与 ShadowCast 的差异 |
| --- | --- | --- |
| 开关 / 质量档 | `pcssEnabled`（默认开）、`pcssQuality` = `Low / Medium / High / Ultra` | 档位采样数不同：4/8、8/16、12/24、16/32（CS 只作用在角色附近的屏幕区域，预算比全屏的 ShadowCast 小） |
| 形状参数 | `pcssSoftness`（0~8，默认 2）、`pcssBlockerSearchRadius`（0.25~16 texel，默认 4）、`pcssMaxPenumbraRadius`（1~64 texel，默认 12）、`pcssDepthBias`（0~0.01，默认 0） | 单位是 **CS tile 的 texel**（tile 分辨率由「单角色分辨率」决定），不是全局 atlas 的 texel |
| 常量 | `_HoCSPcssParams` = `(enabled, softness, blockerRadius, maxPenumbraRadius)`；`_HoCSPcssParams2` = `(depthBias, blockerSamples, filterSamples, 0)` | 同构 |
| 采样上限 | `HO_CS_MAX_PCSS_BLOCKER_SAMPLES = 16`、`HO_CS_MAX_PCSS_FILTER_SAMPLES = 32`（`HoCharacterShadowShaderContract` 镜像） | 上限更小；防漂移由 `HoCharacterShadowValidation.Validate()` 解析 HLSL 比对（batch 里也跑） |
| PCF 回退 | 关闭 / softness 0 / 采样数 0 / 盘里没有 blocker → 回退 CS 自己的固定半径 3×3 PCF（半径 = feature 的「PCF 半径」） | 同构：降级即回退 |

**半影公式（有意与 ShadowCast 不同）**：CS 用物理形式

```text
penumbra = saturate((接收距离 - 遮挡距离) / 遮挡距离)
filterRadius = min(maxPenumbra, softness * maxPenumbra * penumbra)
```

atlas 里的 z 是阴影空间的归一化线性深度，所以"到光源的距离"用 `1 - z`（reversed-Z）表达，归一化的深度范围在分子分母里约掉。**ShadowCast 那边除的是接收深度**（`(blocker - receiver) / receiver`）：接收深度接近 0 时半影会被放大到把整片阴影糊掉 —— CS 试过那个形式，实测阴影核心会漏光到 0.376（`ValidatePcss` 的 `core` 断言直接抓到了），换成除遮挡距离后同样的参数下核心保持 0.000。

**回归入口**：`HoCharacterShadowValidation.ValidatePcss`（batch，D3D11/D3D12 都跑）。摆法是标准 PCSS 场景（接收面正对光源、投影物悬在光源与接收面之间），量 **10%–90% 边宽**（按图像梯度方向走，不按屏幕轴走 —— 按轴走会被"边与扫描方向的夹角"放大好几倍，这是量错过两次的坑）：

```text
pcf width=11px core=0.000 lit=1.000 | pcss width=59px core=0.000 lit=1.000 | softness0 width=11px
```

- PCF 边缘窄、核心全黑、外侧全亮；
- PCSS（softness 3 / maxPenumbra 24）把边宽放大到 5 倍，核心仍然全黑（不漏光）、外侧仍然全亮（无光环）；
- softness 0 时精确回到 PCF 的宽度（降级即回退）。

**坑**：`ValidatePcss` 两版都错在**测量**上，而不是实现上 —— 第一版用斜掠的接收面（PCF 自己就变成 20+px 的锯齿斜坡，量出来的是几何不是滤波），第二版沿屏幕轴量边宽（被夹角放大）。要加新的软阴影指标时先确认"这个数只反映滤波"。

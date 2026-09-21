# Ho-GTAO：开启 MSAA 后轮廓出现“外扩一圈”白线 —— 结论与坑

> 状态：**已定位并已修复**（2026-09-14，覆盖率加权复合）。本文原为过程记录，已压缩为「结论 + 踩过的坑 + 被证伪的假设」。
> MSAA 兼容性边界与验收标准独立成篇：`架构边界/MSAA.md`（“开了不能坏”：允许画质退化，不允许报错与明显崩坏）。

## 1. 一句话结论

**不是错位，也不是 AO 数值错**：出问题的是 **MSAA 混合像素**（只有 MSAA 下才存在）。GeometryBuffer 解析时取**最近 sample** 的几何（前景），trace 从前景表面出发 → 该像素 AO = 0（完全未遮挡、饱和在 `visibility = 1`）；而它的颜色是**全部 sample 平均**后的（大部分是背景皮肤）。于是形成 `发丝 | 1 px 白线 | 深色接触阴影`——接触阴影被吃掉一圈。

关键点：这条白线是**饱和值**，所以任何基于数值的调整（合成权重、backgroundBias、平滑、remap、染色）都**不可能**改变它——这解释了此前整整十轮“无变化”。

| 硬事实 | 来源 |
| --- | --- |
| MSAA 打开才出现，关闭时看不到 | 实机对比 |
| `_AODarkStrength = 0` 或关实时 AO → 现象消失（该 AO 差值是唯一呈现通道） | 实机 |
| AO 材质侧 remap 是恒等（`_AOContrast = 1`、`_AOLevel = 0`） | 配置确认 |
| 直出 `_HoAOTexture`（raw）**看不到**锯齿，纹理本身平滑 | 实机 |
| 发丝材质不透明、队列 2000（非 Cutout／Transparent） | 确认 |
| Game 视图与 Scene 视图现象相同 | 确认 |

**决定性实验**（把机理钉死，无需改代码）：`_AOColor` 设全红 + `_AODarkStrength = 1` → 那条线**颜色不变**（说明 `aoOcc = 0`，没有被压暗的量可染）；`_AOContrast` 拉到 10~20 → **毫无变化**（说明该处 AO 值钉在量程顶端，不是过渡值）。

## 2. 已落地的修法：覆盖率加权复合

业界通行形态：**解析出 per-surface 覆盖率 → 去噪之后按覆盖率复合**。

1. **几何解析多输出一个数**：coverage 纹理的 **G = 被解析（最近）表面占有该像素的比例**；R 保持原语义（总覆盖率，公共契约不变）。
2. **复合放在去噪之后**（`HoGTAO.shader` 的 `Spatial` 末尾）：这一格的遮蔽值属于“解析表面”（轮廓处按定义未遮挡），按 `1 - selected/total` 把**背景表面的遮蔽**混回来。放在去噪之后，是为了不让滤波器把结果重新平均回前景。
3. **背景遮蔽从邻居借**：本像素深度属于前景，无法就地 trace 背景（就地二次 trace 代价大且要和去噪链缠斗）。改为取 1 texel 环内、**更远**、且**自身被解析表面占多数（`selectedCoverage ≥ 0.5`）** 的邻居遮蔽量平均。`≥ 0.5` 是关键：另一个轮廓像素持有的是**前景**遮蔽，不能当背景用。
4. **不新增开关**：只在混合像素上生效；非混合像素逐位不变。
5. **一 texel 环落空时扩到两环**（`ring = 1..2`，第二环只在第一环完全没找到背景像素时才走 → 常见路径仍是 8 tap）。尖锐处（发丝尖端、收敛处）四周往往全是别的轮廓像素，最近的实心背景在 2 texel 外。

回归口径（`_HoGeometryBufferCoverageTextureValid = 0` 即 MSAA 关闭时，两个助手退化为 `step(eps, a)` → `selected == total == 1` → 复合分支不进入 → **逐位不变**）：

| 场景 | 行为 |
| --- | --- |
| MSAA 关闭 | 逐位不变 |
| MSAA 开启、单表面像素（`selected == total`） | 逐位不变 |
| MSAA 开启、轮廓混合像素（`selected < total`） | 按背景占比混入邻居背景遮蔽；白线被填掉，该像素整体转为接近背景的暗值 |

改动清单：`HoGeometryBufferFormatUtility.cs`（覆盖率 R8 → **R8G8**，后备 R16G16_SFloat）、`HoGeometryBufferResolve.shader`（`ResolvedGeometry` 增加 `selectedCoverage`：对“与最近 sample 朝向一致 `dot ≥ 0.9` 且相对深度差 ≤ 0.2%”的 sample 计数，两个 pass 都写 `.g`）、`HoGTAO.shader`（`HoGTAOGeometryCoverageAt`(R) / `HoGTAOGeometrySelectedCoverageAt`(G) + 复合）、`HoGTAORendererFeature.cs`（`_HoAOTexture` 显式单采样，见 §4.4）。

实测：**巨大改善**，白线基本消失，仅在特别尖锐／高频处残留极少量。

## 3. 数学结论：单值 AO × MSAA 平均色的固有误差（确定成立，可复用）

设像素内前景占比 `c_f`、背景 `c_b`，颜色 `H`／`F`，AO `AO_f`／`AO_b`：

```text
理想（逐 sample 遮光） = c_f·H·AO_f + c_b·F·AO_b
实际（单值 × AA 色）   = (c_f·H + c_b·F) · (c_f·AO_f + c_b·AO_b)
误差 = c_f·c_b·(AO_b − AO_f)·(H − F)
```

- 深色物体压在亮背景上（发丝压皮肤）：两项都 `< 0` → **误差为正 = 偏亮**；
- 代数字例：`c_f = c_b = 0.5`、`H = 0.05`、`F = 1.0`、`AO_f = 0.9`、`AO_b = 0.2` → 误差 `+0.166`，实际比理想**亮约 2.4 倍**；
- 误差宽度**恰好一个像素**、位于几何硬边界上 → 观感就是“往外溢一圈白、生硬、不像 MSAA”。

**推论**：改生产端 AO 数值**无法**消除它；能消除的是 (a) 逐 sample 应用 AO（材质按自己所属表面取值），或 (b) 把 AO 在该处的过渡做宽（把误差摊成渐变）。

## 4. 踩过的坑

1. **必须先分清“生产端数值”与“消费端应用”。** 生产出来的纹理平滑、最终画面有硬边时，继续调生产端数值是无效的——本会话绝大部分轮次浪费在这一点上。先做 §3 的数学判定再决定改哪一端。
2. **不要对混合权重（coverage）做邻域平滑。** 3×3 高斯重建把“1 个发丝 sample + 3 个皮肤 sample”的像素（真实 `c_front = 0.25`）算成 `≈ (8×1 + 4×0.25)/16 ≈ 0.81`——因为**带两侧邻居的前景占比都是 1**。合成值随之从暗变亮。这条数值链是确定的：这是后来发白的直接原因之一。
3. **深度金字塔里必须留着遮挡物。** 把 `DepthCopy` 从“最近 sample”改成“占多数表面”，会让金字塔里取不到贴边的遮挡物 → 反向外扩，**已回退**。
4. **遮挡物的屏幕覆盖不应削弱它的遮挡。** “按屏幕覆盖率按比例投射遮蔽”（horizon 按覆盖率衰减）→ 无改善，**已回退**：遮挡物是 3D 物体。
5. **发布给材质的全局纹理要显式关掉 MSAA。** `_HoAOTexture` 原先继承相机颜色描述（MSAA 下是 4x + `bindTextureMS = true`），而 lilToon 按普通 `Texture2D` + linear sampler 读 → API 未定义行为。修法：`msaaSamples = MSAASamples.None; bindTextureMS = false; filterMode = FilterMode.Bilinear;`（对照：同文件里 history、camera motion 本来就这么写，唯独“发布给材质的那张”漏了）。**这是 API 正确性修复，不改变本现象**（早期带着它锯齿依旧）。
6. **“发布给材质的全局纹理没有 RenderGraph 读取声明”是真实契约隐患，但不是本现象的原因。** `SetGlobalTextureAfterPass` 发布的是 transient 资源，debug 关闭时没有任何 pass 声明读它（唯一声明读的是只在 `debugMode != Off` 时入队的 debug pass），生命周期可能在 `Ho-GTAO Output` 后就结束；URP 自己的 SSAO（`DrawObjectsPass` 显式 `UseTexture(ssaoTexture, Read)`）与 Ho-SSGI 都声明了读。**实验 E1（启用 Ho-SSGI 以延长生命周期）现象完全未变** → 推断不成立。留作后续契约清理（改持久 RTHandle + `ImportTexture`，或在不透明绘制后保留一个极简声明 pass）。
7. **观察行为会改变被观察对象。** 打开任意 debug 模式看 AO 时纹理内容正常——因为 debug pass 声明了读、延长了生命周期。所以“诊断图里看不到锯齿”不能作为“生产端没问题”的充分证据。
8. **不能用 `total − front` 推断背面层存在**：那会永远为真，掩盖“取不到背面采样”的情况；要单独输出一个“背面层是否真的有采样”的通道。
9. **诊断视图必须带对比增益**（本次用 6×），否则 ±0.1 的遮蔽差在灰阶里看不出来，“看不见”会被误读成“不存在”。

## 5. 已排除的方向（不要再走）

| 假设 | 证伪方式 |
| --- | --- |
| AO 生产数值错误 | 纹理平滑；轮 3/4/8 的数值改动全无变化 |
| 消费端 remap 放大 | `_AOContrast = 1`、`_AOLevel = 0`，remap 恒等 |
| alpha 裁剪／透明队列 | 材质不透明、队列 2000 |
| 分辨率／格式不匹配 | Game 视图同样存在，raw 纹理平滑 |
| 消费端 UV 约定有 offset | `GetNormalizedScreenSpaceUV(fd.positionCS)` = 像素中心，与 URP 自带 SSAO 同约定；Y 翻转助手只服务相机深度／抓屏，AO 两侧同源不需要 |
| 深度金字塔遮挡物膨胀 | 改“多数表面”无改善且引入反向问题 |
| coverage 的 k/N 量化不匹配颜色边缘 | 平滑后无改善（且有害，见 §4.2） |
| 去噪核宽度（Box vs Disk） | 换 Disk 只是压住症状，不是根因 |
| RenderGraph 生命周期／内存复用 | 实验 E1 现象完全未变 |

排查期用于排除变量的“参数归零法”：`_AODarkStrength = 0`、`_AOStrength = 0`、`_UseRealtimeAO = 0` 逐项关。

## 6. 仍存在的近似（如需继续）

1. 背景遮蔽是**从邻居借**的屏幕空间近似，精确做法是从背景层再 trace 一次；
2. 3 个以上表面共存时只考虑“更远那一侧”；
3. **消费端仍是“一个 AO 值 × MSAA 平均色”**，§3 的结构误差只是被压小、没有归零。要归零必须让材质按自身所属表面取 AO（生产端同时发布前后两层 AO + 占比，材质用自身 fragment 深度与 GeometryBuffer 前层深度比较后选通道）。

残渣的后续选项（代价递增）：① 放宽“更远”判据（对 `dot < 0.9` 的邻居只要求“不显著更近”，解决“发丝尖端贴皮肤、深度差低于 fp16 量化台阶”）；② 借样半径再扩到 3 texel；③ 对残渣像素走“从背景层再 trace 一次”的精确路径。

## 7. 关键文件

| 责任 | 文件 |
| --- | --- |
| AO 生产与复合 | `Runtime/GTAO/Shaders/HoGTAO.shader`（`DepthCopy` / `HoGTAOCompute` / `OutputAO` / `Spatial`） |
| AO feature／发布纹理 | `Runtime/GTAO/HoGTAORendererFeature.cs`（`RecordBlit`） |
| 覆盖率生产（R = 总覆盖，G = 解析表面占比） | `Runtime/GeometryBuffer/Shaders/HoGeometryBufferResolve.shader`、`HoGeometryBufferFormatUtility.cs` |
| 消费端（逐像素、无逐 sample 通路） | `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\Includes\lil_common_frag.hlsl`（`lilSampleRealtimeAO` / `lilApplyAODark`） |

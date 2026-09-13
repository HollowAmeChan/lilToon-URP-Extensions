# Ho-GTAO：开启 MSAA 后轮廓出现"外扩一圈"锯齿 —— 调查记录

> 状态：**已定位并已修复**（2026-09-14）。本文记录现象、证据、被证伪的假设与结论，供后续接手。
> 记录时间：2026-09-14。
> **结论性文档已独立成篇：[`Documentation~/架构边界/MSAA.md`](../架构边界/MSAA.md)** —— MSAA 兼容性边界与验收标准（"开了不能坏"，允许画质退化，但不允许报错与明显崩坏）。本文保留为过程记录（含被证伪的假设与"为什么此前所有尝试无效"）。
> 修复已落地（覆盖率加权复合），详见本文 §12；修复前的工作区曾整体回退到 `7028fe9`。

## 0. 一句话结论

观测到的现象是：**开启 MSAA 后，紧贴物体（发丝压在脸上）的皮肤一侧出现一条约 1 px 宽、边界生硬、"看起来很白、不像经过 MSAA"的亮环**；它由 AO 的压暗通道（`_AODarkStrength`）呈现，但 **AO 纹理本身是平滑的**，因此问题**不在 AO 的生产数值**，而在"**AO 如何被消费端应用到像素**"这一环：**一个像素只有一个 AO 值，却要乘到已经 MSAA 平均过的颜色上**。

## 1. 症状与已确认的硬事实（全部来自实机观察）

| # | 事实 | 来源 |
|---|---|---|
| F1 | MSAA 打开后出现，关闭时看不到 | 用户截图对比 |
| F2 | `_AODarkStrength = 0`（AO 压暗）或关闭实时 AO → 现象消失 | 用户实测 |
| F3 | AO 材质侧 remap 是**恒等**：`_AOContrast = 1`、`_AOLevel = 0`（从未改动过） | 用户确认 |
| F4 | 直接查看发布给材质的 `_HoAOTexture`（无 pow、无反转的 raw 视图）**看不到这条锯齿** | 用户实测 |
| F5 | 轮廓带上"混合像素"确实被识别出来（诊断图显示该处 `R < 1` 且背面层存在） | 用户实测（Silhouette Fill 诊断模式） |
| F6 | 发丝材质是**不透明**渲染、**队列 2000**（非 Cutout / 非 Transparent） | 用户确认 |
| F7 | 用户在 Game 视图与 Scene 视图看到同样现象，因此不是"两个视图分辨率不同"导致 | 用户确认 |
| F8 | 用户判断：**就是采样错位**（AO 与颜色之间存在约 1 px 的错位） | 用户判断 |

## 2. 代码链路（回退后 = HEAD 状态）

- `Runtime/GTAO/Shaders/HoGTAO.shader`
  - `DepthCopy`（:71）用 `normalDepth.a` 当"覆盖率"：`half coverage = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, ...).a;`（:75）
  - `HoGTAOCompute`（:282）：单次 trace，原点取 `HoGTAOSampleDepth(uv, 0)`（深度金字塔 mip0）
  - `Generate`（:399）：算 AO 量；`OutputAO`（:657）：`visibility = 1 - ao`
  - `Spatial`（:665）：双边/盒式去噪；`DepthHistory`（:740）
- `Runtime/GeometryBuffer/Shaders/HoGeometryBufferResolve.shader`
  - MSAA 解算：`coverage` 统计的是**所有被覆盖的 sample**（:48、:57），而几何取的是**最近 sample**（:49-53，`nearestLinearDepth`）
  - 覆盖率写入 `SV_Target1`（:100 / :137），格式 `R8_UNorm`（`HoGeometryBufferFormatUtility.cs:15-19`）
- lilToon 消费端（`D:\Unity_Fork\lilToon\Assets\lilToon\Shader\Includes\lil_common_frag.hlsl`）
  - `lilSampleRealtimeAO`（:831-843）：`saturate((ao − 0.5) * _AOContrast + 0.5 − _AOLevel)`，采样 `_HoAOTexture` 用 `lil_sampler_linear_clamp`
  - `lilApplyAODark`（:881-885）：`fd.col.rgb *= lerp(1, _AOColor, aoOcc * _AODarkStrength)`，`aoOcc = 1 - aoVis`
  - **关键**：整条链只在**像素级**采一次 AO，颜色却是 MSAA 逐 sample 解算后再平均的

## 3. 排查路径（按轮次，含结论）

| 轮 | 假设 | 改动 | 结果 | 结论 |
|---|---|---|---|---|
| 1 | 轮廓外那一圈"部分覆盖"像素被当成完整表面，前景的 AO 顶掉了背景的接触阴影 | `Spatial` 增加"从邻居借背景 AO"的补遮蔽（`silhouetteFill` 开关） | **有改善**，仍有残留 | 方向对了一半：混合像素的**值**确实是问题之一；但权重的宽度/来源不对 |
| 2 | 判据太松（固定 5% 深度带）导致"发丝 vs 皮肤"被合并成同一表面，补遮蔽从不触发 | 判据改为"像素内部前后聚类间隙取较小者" | 诊断图出现蓝色细线（混合像素被识别） | 判据修好了，但现象未消 |
| 3 | coverage 的 k/N 量化让 AO 轮廓成台阶，与平滑的颜色边缘不匹配 | 对 coverage 做 3×3 高斯重建（`HoGTAOSmoothCoverageAt`） | 无变化（且事后证明**有害**） | **证伪**；并且该平滑把轮廓带的背景权重从 0.75 削到约 0.19（因为带两侧邻居的前景占比都是 1），是后来发白的直接原因之一 |
| 4 | 只 trace 最近表面不够，应"每个表面各 trace 一次再按覆盖率合成" | L2：GeometryBuffer resolve 增加第二层（背面）几何 + `Generate` 混合像素二次 trace + `(c_f·AO_f + c_b·AO_b)/(c_f+c_b)·coverage` | 仍无改善 | 结构上更正确，但没有解决最终图像的现象 |
| 5 | 深度金字塔代表"最近 sample"会让遮挡物被膨胀 1 px，接触阴影外扩 | `DepthCopy` 改为"占多数表面" | 无改善（并引入"贴边取不到遮挡物"的新问题） | **回退**。遮挡物必须留在金字塔里 |
| 6 | 遮挡物只按屏幕覆盖率按比例投射遮蔽 | `HoGTAOSample` 回传覆盖率、horizon 按覆盖率衰减 | 无改善 | **回退**。遮挡物是 3D 物体，其屏幕覆盖不应削弱它对邻居的遮挡 |
| 7 | 全局标志 vs 逐像素前置条件不一致，导致某些混合像素"两不管" | 兜底补遮蔽的开关改为逐像素 | 无改善 | 确实是个逻辑洞（已修），但不是本现象的原因 |
| 8 | 混合像素的 AO 应偏向背景一侧，以缩小"单值 × 平均色"的误差 | 合成改为 `backgroundBias = saturate(c_back/(c_f+c_b)·4)` | **毫无变化** | 说明这些像素的最终观感不取决于带内的数值 |
| 9 | 修正只有 1 px 宽，任何 ~1 px 采样错位都会落到未修正的像素上 | 把修正扩到 3 px 邻域 | 用户叫停 | 未完成验证 |
| 10 | 是否 AO 纹理格式/分辨率与屏幕不一致 | 加 `AO Output (raw)` 诊断模式（带 6× 对比增益） | 用户确认**纹理平滑**、Game/Scene 都一样 | **证伪**分辨率不匹配 |

## 4. 被证伪 / 已排除的假设（不要再走）

1. **AO 生产数值错误**：纹理平滑（F4），且数值层面的任何调整都不改变现象（轮 3/4/8）。
2. **消费端 remap 放大**：`_AOContrast = 1`、`_AOLevel = 0`，remap 为恒等（F3）。
3. **alpha 裁剪 / 透明队列**：材质不透明、队列 2000（F6）。
4. **分辨率/格式不匹配**：Game 视图同样存在（F7），raw 纹理平滑（F4）。
5. **深度金字塔"遮挡物膨胀"**：改"多数表面"后无改善，且引入反向问题（轮 5）。
6. **coverage 的 k/N 量化不匹配颜色边缘**：平滑后无改善（轮 3）。
7. **去噪核宽度（Box vs Disk）**：换 Disk "锯齿变少"只是把症状压住，不是根因。

## 5. 仍成立的分析结论（可复用）

### 5.1 单值 AO × MSAA 平均色的固有误差（数学，确定成立）

设像素内前景占比 `c_f`、背景占比 `c_b`，两块颜色 `H`（前景）、`F`（背景），两块 AO `AO_f`、`AO_b`：

```
理想（逐 sample 遮光）= c_f·H·AO_f + c_b·F·AO_b
实际（单值 × AA 色）  = (c_f·H + c_b·F) · (c_f·AO_f + c_b·AO_b)
误差 = c_f·c_b·(AO_b − AO_f)·(H − F)
```

- 深色物体压在亮背景上（发丝压皮肤）时：`(AO_b − AO_f) < 0`、`(H − F) < 0` → **误差为正 = 偏亮**；
- 代数字例：`c_f = c_b = 0.5`、`H = 0.05`、`F = 1.0`、`AO_f = 0.9`、`AO_b = 0.2` → 误差 `+0.166`，实际值比理想**亮约 2.4 倍**；
- 误差宽度**恰好一个像素**、且位于几何硬边界上 → 观感就是"往外溢一圈白、很生硬、不像 MSAA"。

**推论**：只改生产端的 AO 数值**无法**消除它；能消除的是 (a) 逐 sample 应用 AO（材质按自己所属表面取值），或 (b) 把 AO 在该处的过渡做宽（把误差摊成渐变，而不是 1 px 硬线）。

### 5.2 轮廓带的数值链（事后复盘，解释轮 3 为何"无变化"且有害）

一个"1 个发丝 sample + 3 个皮肤 sample"的像素：真实 `c_front = 0.25`、`c_back = 0.75`。
3×3 平滑时，**带两侧邻居的前景占比都是 1**（一侧纯前景、一侧纯背景），于是：

```
smoothed c_front ≈ (8×1 + 4×0.25)/16 ≈ 0.81   →   c_back ≈ 0.19
```

合成值随之从"暗"变"亮"。这条数值链是**确定的**，可作为"不要对混合权重做邻域平滑"的依据。

### 5.3 诊断手段（本次用过、证明有效的）

- 查看发布纹理本身：调试模式直出 `_HoAOTexture`（无 pow/反转）；**必须加对比增益**，否则 ±0.1 的遮蔽差在灰阶里看不出来。
- 查看混合像素识别：输出 `R = 前景 sample 占比`、`G = 总覆盖`、`B = 背面层是否真的有采样`（注意：**不能**用 `total − front` 去推断背面层存在，那会永远为真，掩盖"取不到背面采样"的情况）。
- 参数归零法：`_AODarkStrength = 0`、`_AOStrength = 0`、`_UseRealtimeAO = 0` 逐项排除。

## 6. 尚未定位的部分

**错位的具体来源未确定**（F8 是现象层面的判断，尚未找到产生 1 px 错位的代码位置）。待查清单：

1. ~~`_HoAOTexture` 的尺寸、GraphicsFormat/sRGB 是否与 `_ScreenParams` / 相机颜色完全一致~~ → 尺寸一致（派生自相机颜色，同为渲染分辨率）；MSAA/bindTextureMS/filterMode 的继承问题见 §10.4 **已修**。
2. 材质侧 UV 约定：`GetNormalizedScreenSpaceUV(fd.positionCS)` 与 AO 纹理写入时的采样中心约定是否严格一致（半 texel 差异即可造成 1 px 错位）。
3. 是否存在 **TAA/投影抖动**、dynamic resolution、Render Scale ≠ 1 等改变 UV 映射的因素。
4. 用像素级放大图对比"AO 纹理的暗带边缘"与"颜色的发丝边缘"的**相对位移方向与量级**（这是下一步最该拿到的数据）。

## 7. 下一步的候选方向（按投入排序）

| 方向 | 内容 | 代价 |
|---|---|---|
| A. 先定位错位 | 拿像素级放大对比图，量出 AO 与颜色的实际位移；据此检查 §6 的第 1/2 条 | 小 |
| B. 让消费端免疫错位 | 生产端把轮廓过渡做宽到 2~3 px（本会话轮 9 的方向），使 1 px 错位落在过渡带内 | 中（接触阴影会略厚一点） |
| C. 结构性正解 | 材质按**自己所属表面**取 AO：生产端发布前/后两层 AO + 占比，lilToon 用自身 fragment 深度与 GeometryBuffer 前层深度比较后选通道。这样 MSAA 平均后**精确等于逐 sample 理想值**，§5.1 的误差项归零 | 大（生产端要贯通两层 AO 的 temporal/spatial 链；lilToon 侧需改 `lilSampleRealtimeAO`） |

## 8. 本会话改动与回退

- 所有改动已回退，工作区 = `7028fe9`，`git status` / `git diff HEAD` 均为空。
- 涉及文件（回退前）：`Runtime/GTAO/HoGTAO.shader`、`Runtime/GTAO/HoGTAORendererFeature.cs`、`Runtime/GTAO/HoGTAOSettings.cs`、`Runtime/GTAO/HoGTAOVolume.cs`、`Runtime/GTAO/Shaders/Debug/HoGTAODebug.shader`、`Editor/GTAO/HoGTAORendererFeatureEditor.cs`、`Editor/GTAO/HoGTAOVolumeEditor.cs`、`Runtime/GeometryBuffer/HoGeometryBufferPass.cs`、`HoGeometryBufferRenderTargets.cs`、`HoGeometryBufferRenderGraphResources.cs`、`HoGeometryBufferShaderConstants.cs`、`HoGeometryBufferFormatUtility.cs`、`Shaders/HoGeometryBufferResolve.shader`、`Documentation~/架构优化/LILTOON_GTAO_H_TRACE_ALIGNMENT_WORKSHEET.md`。
- 回退前的完整 diff 备份（仓库外）：`%TEMP%\hogtao-backup-20260914-052602\full.patch`。

## 9. 方法论提醒（写给下一次接手）

1. **先分清"生产端数值"与"消费端应用"**：当生产出来的纹理本身平滑、而最终画面有硬边时，继续调生产端数值是无效的（本会话在这一点上消耗了绝大部分轮次）。
2. **单值 × MSAA 平均色**这类结构性误差必须用数学先判定，再决定改哪一端（§5.1）。
3. 任何 AO 相关的诊断视图都要**带对比增益**，否则"看不见"会被误读成"不存在"。
4. 每次只改一处、并保留可 A/B 的开关；不要在没有"位移方向/量级"证据时动手改生产端。

## 10. 追加调查：AO → lilToon 消费链路的 MSAA 支持情况（2026-09-14）

本节只做只读调查，未改任何代码。

### 10.1 消费端（lilToon）：**完全没有逐 sample 支持**

| 环节 | 位置 | 事实 |
|---|---|---|
| 声明 | `lil_common_input.hlsl:828` | `TEXTURE2D_SCREEN(_HoAOTexture);` |
| 宏展开 | `lil_common_macro.hlsl:429-438` | 非 XR → `TEXTURE2D(tex)`；XR → `TEXTURE2D_ARRAY(tex)`。**没有 MSAA 分支** |
| 采样 | `lil_common_frag.hlsl:841` | `LIL_SAMPLE_SCREEN(_HoAOTexture, lil_sampler_linear_clamp, screenUV).r` → 普通 `SAMPLE_TEXTURE2D`，位置是**像素中心** |
| 应用 | `lil_common_frag.hlsl:871 / 881-885` | `aoVis *= lerp(1, sample, aoMask)`；`col.rgb *= lerp(1, _AOColor, aoOcc * _AODarkStrength)` |

全仓库检索 `Texture2DMS`、`Texture2DMSArray`、`EvaluateAttributeAtSample`、`GetRenderTargetSampleCount`、`SV_Coverage`、`SampleShading`：
**零命中**（唯一相关的只有 `AlphaToMask` 材质属性，用于 Cutout 边缘，与本现象无关）。

→ 结论：**AO 在消费端是逐像素的 post-resolve 屏幕空间信号，设计上就没有逐 sample 通路**。于是必然出现 §5.1 的误差：一个 AO 值 × 已经 MSAA 平均过的颜色。

### 10.2 生产端（Extensions）：发现两处 MSAA 相关异常

**异常 1 —— `_HoAOTexture` 继承了相机颜色的 MSAA 描述**

`Runtime/GTAO/HoGTAORendererFeature.cs` `RecordBlit`（HEAD :1270-1277）：

```csharp
TextureDesc destinationDesc = renderGraph.GetTextureDesc(cameraColor);
destinationDesc.name = isDebug ? "_HoGTAODebugColor" : "_HoAOTexture";
destinationDesc.clearBuffer = false;
destinationDesc.depthBufferBits = 0;
TextureHandle destination = renderGraph.CreateTexture(destinationDesc);
```

没有覆盖 `msaaSamples` / `bindTextureMS` / `filterMode` → **MSAA 开启时 `_HoAOTexture` 本身就是一张 MSAA 纹理（如 4x，且 `bindTextureMS = true`）**，而 lilToon 按普通 `Texture2D` 采样 ✗。

对照同文件内其它辅助纹理都在显式关掉 MSAA：

- `HoGTAOHistory.Ensure`：`msaaSamples = 1`（:239）
- Camera Motion：`descriptor.msaaSamples = MSAASamples.None; descriptor.bindTextureMS = false;`（:1155-1156）

→ 唯独"发布给材质的那一张"没有关，属于遗漏。`filterMode` 同样继承（相机颜色通常为 Point），材质却用 linear sampler 读，跨 API 存在过滤语义差异。

**异常 2（更可疑）—— 发布给材质的全局纹理没有任何 RenderGraph 读取声明**

- 该纹理由 `renderGraph.CreateTexture(...)` 创建，是**临时（transient）资源**，生命周期 = 声明到的**最后一次使用**；
- 发布方式为 `builder.SetGlobalTextureAfterPass(destination, AOTextureId)`（:1289）；
- **debug 关闭时没有任何 pass 声明读取它**：唯一声明读取的是 `HoGTAODebugPass`（:379 `builder.UseTexture(data.source, ...)`，`data.source = gtao.aoTexture`），而该 pass 只在 `debugMode != Off` 时才入队（`AddRenderPasses` 的 `debugEnabledForCamera` 判断）；
- 对照 URP 自己的同类做法：`DrawObjectsPass.cs:318` 与 `:498` 显式 `builder.UseTexture(ssaoTexture, AccessFlags.Read)` —— 材质要采 `_ScreenSpaceOcclusionTexture`，就必须在绘制 pass 上声明；
- 对照本仓库自己的做法：Ho-SSGI **声明了**（`HoSSGIRendererFeature.cs:1078 / 1217 / 1246` 的 `UseTexture(data.ao, Read)`），且其 passEvent 为 `AfterRenderingOpaques .. BeforeRenderingPostProcessing`（:642-646）。

→ **推论**：debug 关闭（且 SSGI 关闭）时，AO 纹理的声明生命周期在 `Ho-GTAO Output` 之后就结束了；而 lilToon 材质是在**其后的不透明绘制**中通过全局采样它（graph 不知情 = 未声明的使用）→ 该内存可能已被后续 pass 复用 ✗。MSAA 开关会改变分配尺寸与复用模式，因此**只在 MSAA 下暴露**。

该推论同时解释了两个反直觉观测：

1. 打开任意 debug 模式去看 AO 时，纹理内容正常 —— 因为 debug pass 声明了读，生命周期被延长；
2. "AO Output (raw) 里看不到锯齿" —— 同上，观察行为本身改变了被观察对象。

> **2026-09-14 实验 E1 结果：不成立。** 启用 Ho-SSGI（其 pass 会声明 `UseTexture(data.ao)`，passEvent 在 `AfterRenderingOpaques` 之后）后，现象**完全未变**。因此"生命周期/内存复用"不是本现象的原因。本条保留为记录（同时说明：即使不成立，"发布给材质的全局纹理没有声明读取"本身仍是 RenderGraph 契约上的隐患，只是与本锯齿无关）。

### 10.3 可立即做的判定实验（不改生产逻辑）

| # | 实验 | 若成立的判读 |
|---|---|---|
| E1 | **启用 Ho-SSGI**（强度可给 0，只要其 pass 被记录并声明读取） | ❌ **已执行：现象完全未变** ⇒ 生命周期推断不成立 |
| E2 | 用帧调试器 / RenderGraph 资源视图看 `_HoAOTexture` 的生命周期是否在 `Ho-GTAO Output` 后立即结束 | （E1 已否定，无需再做） |
| E3 | 临时把 `RecordBlit` 的目标改为**持久 RTHandle + `ImportTexture`**（照抄 history buffer 的写法） | （E1 已否定，无需再做） |

### 10.4 修法

**已应用的修复（异常 1，2026-09-14）**：`HoGTAORendererFeature.RecordBlit` 中，非 debug 目标显式设置

```csharp
destinationDesc.msaaSamples = MSAASamples.None;
destinationDesc.bindTextureMS = false;
destinationDesc.filterMode = FilterMode.Bilinear;
```

目的：让 `_HoAOTexture` 成为普通单采样纹理，与 lilToon 的 `Texture2D` 声明和线性采样一致（此前它继承相机颜色描述，MSAA 下是 4x + `bindTextureMS = true`，材质读它属于 API 未定义行为）。
**预期：这是 API 正确性修复，不一定会改变画面现象** —— 本会话早期的一版代码就带着这个修复，当时的锯齿依旧存在。它消除的是一个平台相关的变量与潜在的 MSAA 采样告警。

**未应用（E1 证伪后降级，留作后续契约清理）**：

1. `_HoAOTexture` 改为 feature 持有 RTHandle + `renderGraph.ImportTexture`（避免"发布给材质的全局纹理没有声明读取"这一契约隐患）。
2. 或在**不透明绘制之后**保留一个声明读取它的极简 pass。

## 11. 追加调查（二）：消费端 UV 约定核对 + 错位机制定位（2026-09-14）

### 11.1 消费端 UV 约定：没有问题

| 检查项 | 结论 |
|---|---|
| UV 来源 | `lil_common_frag.hlsl:871` `GetNormalizedScreenSpaceUV(fd.positionCS)`；`fd.positionCS` 是 v2f 的 `SV_POSITION`（`o.positionCS = i.positionCS`，`lil_common_frag.hlsl:215`）→ 片元处即**像素中心** |
| 与 URP 自带 SSAO 是否同约定 | 是（`lil_pass_gbuffer.hlsl:181`、`:1937` 用同一个 `GetNormalizedScreenSpaceUV(input.positionCS)`） |
| Y 翻转 | lilToon 有翻转助手 `lilCameraDepthTexel`（`lil_common_macro.hlsl:723-732`），但只服务于相机深度/抓屏纹理；AO 是渲染目标空间的纹理、两侧同源，不需要翻转，AO 路径也确实没用它 → 不构成偏差 |
| 过滤 | 消费端 linear sampler；生产端原本继承相机颜色的过滤模式 → 已在 §10.4 修为 Bilinear |

→ **消费端读 UV 的公式没有 offset**。若画面上的错位真实存在，它只能是 AO *内容* 的空间采样位置造成的，而不是材质取 UV 的方式。

### 11.2 机制定位：AO 的边缘是"点采样几何"的硬边界，颜色是 MSAA 平均边界

MSAA 开启时，整条 AO 链的空间采样位置**不是像素中心**：

1. `HoGeometryBufferResolve.shader:42-53`：MSAA 解析时挑选**最近 sample** 作为该像素的几何（法线 + 线性深度）；该 sample 在像素内的位置由 MSAA 采样图案决定（±0.25 px 量级），**不是像素中心**；
2. HEAD 的 `HoGTAO.shader:75`（`DepthCopy`）用 `normalDepth.a`（即该最近 sample 的深度）判断覆盖 → 在"前景/背景共存"的轮廓像素上覆盖率恒为 1；
3. 因此 `OutputAO`（`HoGTAO.shader:657-663`）里的 `ao *= saturate(coverage)` 在这些像素上**完全不衰减**；
4. 净效果：AO 暗带的边界 = "**选取某个 sample 的几何**"得到的**硬边界**；而颜色的边界 = **全部 sample 平均**后的结果 → 两者必然错开约一个采样间距，且 AO 一侧**没有 MSAA 平滑**。

该机制与全部观测自洽：

| 观测 | 解释 |
|---|---|
| 只在 MSAA 下出现 | 关 MSAA 时解析出的就是像素中心那一个 sample，AO 边界与颜色边界同源 |
| 改 AO 数值（合成权重/偏置/平滑）全无效 | 错位来自**采样位置**，不来自数值 |
| AO 纹理看起来平滑 | 纹理里是一条连续暗带；错位体现在它**相对于颜色边缘的位置**上 |
| 约 1 px、边界硬、"不像 MSAA" | AO 边界未被覆盖率平均，而颜色边界被平均了 |
| `_AODarkStrength = 0` 即消失 | 该 AO 差值是唯一的呈现通道 |

### 11.3 需要的一项确认数据（无需改代码）

把 AO 的边缘变"脆"，再与颜色的发丝边缘对比：

1. 材质 `_AOContrast` 临时拉到很大（10~20）→ AO 过渡被压成近似二值边界；
2. （可选）`_AOColor` 设纯红 + `_AODarkStrength = 1` → AO 作用区染色，边界更易看；
3. 相机怼近到一根发丝占几十像素，截图对比**发丝颜色边缘**与**AO 硬边界**：是否重合、错开多少、朝哪侧。

判读：

- **基本重合** ⇒ 错位不是主因，问题就是 §5.1 的"单值 × MSAA 平均色"结构误差 → 修法是让 AO 输出也按覆盖率平均，或让材质按自身所属表面取 AO；
- **系统性错开约 0.25~0.5 px 且方向固定** ⇒ 坐实"解析几何取的是某个 sample 的位置" → 修法是让 AO 的几何与覆盖率都按 sample 覆盖率加权（即本会话早期的"G 通道 = 前景 sample 占比 + 双侧 trace 合成"方向）。

### 11.4 决定性实验结果（2026-09-14）：机制确认 —— 不是错位，是**该像素没有遮蔽**

用户实测（§11.3 的方法）：

| 操作 | 结果 | 含义 |
|---|---|---|
| `_AOColor` 设全红、`_AODarkStrength = 1` | **那条细线颜色不变** | 该处 `aoOcc = 1 - aoVis = 0`，没有被压暗的量可染 → **AO 遮蔽量是 0** |
| `_AOContrast` 拉到很大 | **无任何变化** | 该处的 AO 值被**钉在量程顶端（visibility = 1）**，不是过渡值（过渡值必然被 remap 改变）；也再次说明"改 AO 数值无效"是因为数值已经饱和 |

→ **确认机理**：出问题的是 **MSAA 混合像素**（只有 MSAA 下才存在）——

1. `HoGeometryBufferResolve.shader:42-53` 取**最近 sample** 作为该像素几何 → 在"发丝(前景) + 皮肤(背景)"共存的像素上，几何 = **发丝**；
2. trace 从发丝表面出发：轮廓处的发丝**不自遮蔽**；身后的皮肤在"更远"一侧（按 horizon 语义不产生遮挡）→ **该像素 AO = 0（完全未遮挡）**；
3. HEAD 的覆盖率判据是 `HoGTAO.shader:75` 的 `normalDepth.a`（= "这个像素有没有几何"）→ 两个表面共存时**恒为 1** → `OutputAO`（`:657-663`）的 `ao *= saturate(coverage)` **完全不衰减**；
4. 净结果：**颜色上是皮肤的那些像素，AO 是无遮挡的白**；而再外面一圈的皮肤像素，其 trace 能看到发丝（更近 → 强遮挡）→ 是暗的。于是形成 "发丝 | 1px 白线 | 深色接触阴影" —— 即"接触阴影被吃掉一圈 / 外扩一圈"。

这也解释了此前所有"无变化"：该白线是**饱和值**，任何基于数值的调整（合成权重、偏置、平滑、remap、染色）都无法改变它。

**最小修法（不需要新通道、不需要第二层几何）**：在 `HoGTAO.shader` 的 `Spatial` 里，对**更远且被覆盖**的邻居做一次 8 tap 的遮蔽量取 **max**（`filtered = max(filtered, maxFartherOcclusion)`）。因为取 max，邻居即使同样是"白"的混合像素也不会带来副作用（max 只会变暗）；影响范围仅限轮廓两侧各约 1 px（把接触阴影加厚一格），其余像素完全不变。等价于本会话早期"补遮蔽兜底"的**无门控**版本，因而不需要 G 通道（前景 sample 占比）。

## 12. 实施：MSAA 轮廓复合（2026-09-14，已落地）

最终采用的做法（业界通行形态：**解析出 per-surface 覆盖率 → 去噪之后按覆盖率复合**）：

### 12.1 设计

1. **几何解析多输出一个数**：coverage 纹理的 **G** 通道 = 被解析（最近）表面**占有该像素的比例**。R 保持原语义（总覆盖率，公共契约不变）。
2. **复合放在去噪之后**（`Spatial` 末尾）：这一格的遮蔽值是"解析表面"的，轮廓处它按定义是**未遮挡**的；而该像素的颜色是覆盖率加权混合。因此按 `1 - selected/total` 把**背景表面的遮蔽**混回来。
3. **背景表面的遮蔽从邻居借**：本像素的深度属于前景，无法就地 trace 背景（这正是本会话早期 L2 二次 trace 解决的事，代价大且要和去噪链缠斗）。改为取 **1 texel 环内、更远、且自身被解析表面占多数（G ≥ 0.5）** 的邻居的遮蔽量平均。取"G ≥ 0.5"是关键：另一个轮廓像素持有的是**前景**的遮蔽，不能当背景用。
4. **不新增开关**：只在"混合像素"上生效，非混合像素逐位不变。

### 12.2 改动清单

| 文件 | 改动 |
|---|---|
| `Runtime/GeometryBuffer/HoGeometryBufferFormatUtility.cs` | 覆盖率格式 R8 → **R8G8**（R8G8_UNorm，退回 R16G16_SFloat / 后备） |
| `Runtime/GeometryBuffer/Shaders/HoGeometryBufferResolve.shader` | `ResolvedGeometry` 增加 `selectedCoverage`：对"与最近 sample 朝向一致（dot ≥ 0.9）且相对深度差 ≤ 0.2%"的 sample 计数（两个 pass 都写入 `.g`） |
| `Runtime/GTAO/Shaders/HoGTAO.shader` | 新增 `HoGTAOGeometryCoverageAt`（R）/ `HoGTAOGeometrySelectedCoverageAt`（G）+ coverage 纹理声明；`Spatial` 末尾加入覆盖率复合 |
| `Runtime/GTAO/HoGTAORendererFeature.cs` | 前一轮的异常 1 修复（`_HoAOTexture` 显式单采样 + Bilinear）保留 |

### 12.3 行为与回归

- **MSAA 关闭**：`_HoGeometryBufferCoverageTextureValid = 0` → 两个助手都退化为 `step(eps, a)` → `selected == total == 1` → 复合分支不进入 → **逐位不变**。
- **MSAA 开启、单表面像素**：`selected == total` → 不进入 → 不变。
- **MSAA 开启、轮廓混合像素**：`selected < total` → 按背景占比混入邻居的背景遮蔽 → 那条白线被填掉；同时（因为背景占比大）该像素整体转为接近背景的暗值。
- 仍存在的近似：① 背景遮蔽是**从邻居借**的屏幕空间近似（精确做法是从背景层再 trace 一次）；② 3 个以上表面时只考虑"更远那一侧"；③ 消费端仍是"一个 AO 值 × MSAA 平均色"，§5.1 的结构误差只是被压小、没有归零（要归零必须让材质按自身所属表面取 AO）。

### 12.4 实测结果与残渣处理（2026-09-14）

**实测：巨大改善**，白线基本消失，仅在**特别尖锐/高频**处（发丝尖端、收敛处）残留极少量。

残渣成因（与 §12.1 的"借邻居"近似直接相关）：这些位置的 1 texel 环里**没有"实心背景"像素** —— 尖端四周往往全是别的轮廓像素（`G < 0.5` 被排除），或最近的实心背景在 2 texel 外 → `backgroundWeight = 0` → 复合不发生 → 该处仍保留前景的未遮挡值。

**本次追加改动**：把借样扩为**两环**（`ring = 1..2`），但第二环只在第一环**完全没找到**背景像素时才走（`if (backgroundWeight > 0.5) continue;` 守卫），因此常见路径仍是 8 tap，代价只落在残渣那些点上。

**若仍有残渣，按代价递增的后续选项**：

1. 放宽"更远"判据：对**朝向明显不同**（`dot < 0.9`）的邻居，只要求"不显著更近"即可（解决"发丝尖端贴着皮肤、深度差低于 fp16 量化台阶"这一类）；
2. 把借样半径再扩到 3 texel（只对仍未找到背景的点生效）；
3. 对这些点改走**从背景层再 trace 一次**的精确路径（即早期 L2 的第二层几何，但只用于残渣像素，代价可控）。

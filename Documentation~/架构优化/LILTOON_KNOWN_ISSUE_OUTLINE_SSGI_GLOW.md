# lilToon 已知问题：外扩描边在 HTrace SSGI 下发亮 / 白色边缘

> 状态：**已知 bug，暂不修复**（2026-05）
> 结论一句话：HTrace SSGI 是偏 PBR 的屏幕空间间接光，它把 lilToon 的“外扩描边壳”当成一颗真实表面去计算光照，但由于描边壳只存在于相机深度/颜色、而不存在于 SSGI 的法线/rendering-layer 输入里，产生“深度有壳、法线/层是背景”的割裂，导致描边那一圈被错误照亮、发亮并出现白色锯齿边缘。

---

## 1. 现象

- 使用 **HTrace SSGI**（`D:\Unity_Project\BREAK_URP\Assets\HTraceSSGI`）时，角色描边（外扩出去的那一圈）会被 SSGI 打亮，出现发亮的白色/亮色边缘。
- 边缘处伴随不连续的白色锯齿渣滓。
- 与角色特化（CharacterSpecialization / MetadataBuffer）**无关**——那套走的是 `HoMetadataBuffer` / `HoGeometryBuffer` 隔离的 RT，不经过 SSGI 相机颜色/深度。

## 2. 根因（已确认）

lilToon 的描边是可选的 `Hidden/lilToonOutline`（`lts_o`，URP 子着色器 `DefaultDirectOutline`）。它的 pass 结构是：

```
FORWARD           Cull [_Cull]         <- 渲染主体表面（无 LIL_OUTLINE）
FORWARD_OUTLINE   Cull [_OutlineCull]  <- 渲染外扩描边壳（有 LIL_OUTLINE）
DEPTHNORMALS      Cull [_Cull]         <- SSGI 法线来源（用基础几何）
GBUFFER           Cull [_Cull]         <- SSGI albedo / rendering layer 来源（用基础几何）
```

HTrace SSGI 读取的场景输入：

| SSGI 输入 | 描边壳有没有 | 说明 |
|---|---|---|
| 相机颜色 `g_HTraceColor` | ✅ 有 | `FORWARD_OUTLINE` 画出外扩壳并写亮描边色 |
| 相机深度 `g_HTraceDepth` | ✅ 有 | `FORWARD_OUTLINE` `ZWrite` 默认开，写外扩壳深度 |
| 法线 `_CameraNormalsTexture`（URP DepthNormals） | ❌ 没有 | `DEPTHNORMALS` 用**基础网格**（`lil_pass_depthnormals.hlsl` 不走 `lil_vert_outline.hlsl`，不外扩） |
| albedo / rendering layer（`UniversalGBuffer`） | ❌ 没有 | `GBUFFER` 同样用基础网格 |

所以 SSGI 在描边那一圈读到的是：**近的深度（描边壳）+ 背景的法线/层**。这一“深度/法线/层割裂”让 SSGI：

- 对描边像素按背景法线计算间接光 → 描边被塞进错误（偏亮）的 GI → 发亮；
- 屏幕空间射线在轮廓边缘因深度/法线不一致而命中到描边亮色 → 白色锯齿。

## 3. 尝试过的修法及为何不行

针对"主体 + 描边一体材质"（`Hidden/lilToonOutline` 同时用 `FORWARD` 渲主体、`FORWARD_OUTLINE` 渲描边）:

1. **让 `DEPTHNORMALS`/`GBUFFER` 也做外扩**（加 `#define LIL_OUTLINE`、以及为不触发 `sampler_MainTex` 重定向而引入的 `LIL_OUTLINE_EXTRUDE` + `Cull [_OutlineCull]`）
   - 能让 SSGI 在描边带拿到一致几何，**但这两个 pass 是整颗 mesh 的，外扩会把主体表面也一起外扩** → SSGI 用错误法线/深度去照亮主体 → **主渲染损坏**（脸/身体发红、错乱）。**已回退。**
   - 附属问题：外扩后 `sampler_MainTex` 被 `#define` 成 `sampler_OutlineTex`，与 GBUFFER/DEPTHNORMALS 的片段采样 `_MainTex` 冲突，报 `sampler_outlinetex / sampler_maintex` 不匹配。

2. **描边 `ZWrite` 关掉**
   - 描边消失（视觉不可接受）。

3. **用 SSGI 的 rendering layer 排除（`Exclude Casting/Receiving Mask`）**
   - 描边那圈在 rendering-layer 纹理里是**背景**（`DEPTHNORMALS`/`GBUFFER` 用基础几何，不在外扩壳上写层），没有描边自己的层可排除；且描边与主体共用 renderer 的层，无法单靠层把描边和主体分开。即有“耦合”且不可行。

## 4. 结论（为什么决定暂不修）

- HTrace SSGI 是**偏 PBR** 的屏幕空间 GI，它假设屏幕上的每个像素都是“有正确 albedo + 法线 + 深度”的真实表面。
- lilToon 的外扩描边是一种**非物理的装饰壳**：它只出现在画面和相机深度里，SSGI 的材质输入（法线/albedo/rendering layer）根本看不到它。两者的语义天然不匹配。
- 在“主体 + 描边一体材质”的当前架设下，**没有既不动主体深度/法线、又不关 ZWrite、又不需要独立 rendering layer 的干净修法**。
- 因此决定：**暂时按已知 bug 处理**。后续大概率需要**自己搓一套接入本管线（BRP/URP fork）的 SSGI**，用能识别 lilToon 语义（描边壳、toon 法线、`_FlipNormal`/背面法线等）的输入来做间接光，而不是依赖偏 PBR 的 HTrace。

## 5. 当前工作区状态

- 本仓库（`D:\Unity_Fork\lilToon`）**没有**为修此问题保留任何改动；所有实验性 lilToon 改动已回退。
- **描边 z-bias 遮罩**（`_OutlineZBiasMask`）那套是独立的、已在库里的改动，与 SSGI 问题无关，保留。
- 需要你做的一次手动操作：执行 `Assets/lilToon/[Shader] Refresh shaders` 让生成 shader 恢复，主渲染即正常。

## 6. 若以后真要做“自建 SSGI”，建议的关注点

- 屏幕空间输入应针对 lilToon 语义：diffuse / albedo、**真实几何法线（`_HTraceSSGIBackfaceNormalFix` 已用于 `UniversalGBuffer`/`DepthNormals`）**、深度、motion、UV/遮罩。
- 描边壳应被显式识别并排除（不作为受光/发光面）。
- 参考本仓库成熟做法：`CustomShaderResources/URP/*.lilblock` 里的 `HoMetadataBuffer` / `HoGeometryBuffer` 等 pass 是 lilToon 向屏幕空间管线暴露场景输入的既有接口，可在其上扩展。

## 7. 相关源码位置

- lilToon 描边实现：`Assets/lilToon/Shader/Includes/lil_vert_outline.hlsl`、`lil_common_functions.hlsl`（`lilCalcOutlinePosition`）
- lilToon 深度/法线、GBuffer：`lil_pass_depthnormals.hlsl`、`lil_pass_gbuffer.hlsl`
- 描边 URP 子着色器模板：`CustomShaderResources/URP/DefaultDirectOutline*.lilblock`、`DefaultLiteDirectOutline*.lilblock`、`DefaultMultiOutline.lilblock`
- SSGI：`D:\Unity_Project\BREAK_URP\Assets\HTraceSSGI\Scripts\Passes\URP\GBufferPassURP.cs`、`...\Includes\HRayMarchingSSGI.hlsl`、`...\Computes\HDepthPyramid.compute`、`...\Shaders\URP\ColorComposeURP.shader`
  - 排除机制：`_ExcludeCastingLayerMaskSSGI` / `_ExcludeReceivingLayerMaskSSGI`（`HDepthPyramid.compute`、`ColorComposeURP.shader`）

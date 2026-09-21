# lilToon 已知问题：外扩描边在 HTrace SSGI 下发亮／白色边缘

> 状态：**已知问题，暂不修复（2026-05）**，本文已压缩为「结论 + 坑」。
> 文中的 `HoMetadataBuffer` 已在 R6／R7 删除（对象／材质语义归 OB + AC，surface 族归 SB）；该删除与本问题无关——Ho buffer 体系走自己隔离的 RT，不经过 SSGI 的相机颜色／深度。

## 1. 结论一句话

HTrace SSGI 是偏 PBR 的屏幕空间间接光，它把 lilToon 的“外扩描边壳”当成一颗真实表面去算光照；但描边壳只存在于相机颜色／深度，不存在于 SSGI 的法线／rendering-layer 输入里 → **“深度有壳、法线／层是背景”的割裂**，描边那一圈被错误照亮并出现白色锯齿。

现象：使用 `D:\Unity_Project\BREAK_URP\Assets\HTraceSSGI` 时，角色描边（外扩的那一圈）被 SSGI 打亮，边缘伴随不连续白色锯齿渣滓。

## 2. 根因（已确认）

lilToon 描边是可选的 `Hidden/lilToonOutline`（`lts_o`，URP 子着色器 `DefaultDirectOutline`）：

```text
FORWARD           Cull [_Cull]         <- 主体表面（无 LIL_OUTLINE）
FORWARD_OUTLINE   Cull [_OutlineCull]  <- 外扩描边壳（有 LIL_OUTLINE）
DEPTHNORMALS      Cull [_Cull]         <- SSGI 法线来源（基础几何）
GBUFFER           Cull [_Cull]         <- SSGI albedo / rendering layer 来源（基础几何）
```

| SSGI 输入 | 描边壳有没有 | 原因 |
| --- | --- | --- |
| 相机颜色 `g_HTraceColor` | ✅ 有 | `FORWARD_OUTLINE` 画外扩壳并写亮描边色 |
| 相机深度 `g_HTraceDepth` | ✅ 有 | `FORWARD_OUTLINE` 默认 `ZWrite` 开 |
| 法线 `_CameraNormalsTexture` | ❌ 没有 | `DEPTHNORMALS` 用基础网格（不走 `lil_vert_outline.hlsl`） |
| albedo／rendering layer（`UniversalGBuffer`） | ❌ 没有 | `GBUFFER` 同样用基础网格 |

于是 SSGI 在描边那一圈读到“近的深度 + 背景的法线／层”：按背景法线算间接光 → 描边进错误（偏亮）的 GI；屏幕空间射线因深度／法线不一致命中描边亮色 → 白色锯齿。

## 3. 尝试过的修法及为何不行

1. **让 `DEPTHNORMALS`／`GBUFFER` 也外扩**（加 `#define LIL_OUTLINE` + `LIL_OUTLINE_EXTRUDE` + `Cull [_OutlineCull]`）
   - 这两个 pass 是**整颗 mesh** 的，外扩会把主体表面也一起外扩 → SSGI 用错误法线／深度照亮主体 → **主渲染损坏**（脸／身体发红、错乱）。**已回退。**
   - 附属坑：外扩后 `sampler_MainTex` 被 `#define` 成 `sampler_OutlineTex`，与同 pass 的片段采样 `_MainTex` 冲突，报 `sampler_outlinetex / sampler_maintex` 不匹配。
2. **关掉描边 `ZWrite`** → 描边消失（视觉不可接受）。
3. **用 SSGI 的 rendering layer 排除（`Exclude Casting/Receiving Mask`）** → 描边那圈在层纹理里是**背景**（层写自基础几何），没有描边自己的层可排除；且描边与主体共用 renderer 的层，无法按层拆分。

## 4. 为什么不修

- SSGI 假设屏幕上每个像素都是“有正确 albedo + 法线 + 深度”的真实表面；外扩描边是**非物理装饰壳**，两者的语义天然不匹配。
- 在“主体 + 描边一体材质”的当前架设下，**没有既不动主体深度／法线、又不关 ZWrite、又不需要独立 rendering layer 的干净修法**。
- 后续大概率要自建一套接入本管线（BRP／URP fork）的 SSGI，用能识别 lilToon 语义（描边壳、toon 法线、`_FlipNormal`／背面法线等）的输入做间接光，而不是依赖偏 PBR 的 HTrace。描边语义应显式识别并排除（参见 `归档/Outline_Surface_Semantics_Investigation.md §2` 的三种语义契约）。

## 5. 备注

- 本仓库（`D:\Unity_Fork\lilToon`）**没有**为修此问题保留任何改动，实验性修改已全部回退。
- **描边 z-bias 遮罩**（`LIL_FEATURE_OutlineZBiasMask` / `_OutlineZBiasMask`）是独立的、已在库里的改动，与 SSGI 问题无关，保留。
- 相关源码：`lil_vert_outline.hlsl`、`lil_common_functions.hlsl`（`lilCalcOutlinePosition`）、`lil_pass_depthnormals.hlsl`、`lil_pass_gbuffer.hlsl`、`CustomShaderResources/URP/Default*Outline*.lilblock`；SSGI 侧 `HTraceSSGI/.../GBufferPassURP.cs`、`HRayMarchingSSGI.hlsl`、`HDepthPyramid.compute`、`ColorComposeURP.shader`（层排除：`_ExcludeCastingLayerMaskSSGI`／`_ExcludeReceivingLayerMaskSSGI`）。

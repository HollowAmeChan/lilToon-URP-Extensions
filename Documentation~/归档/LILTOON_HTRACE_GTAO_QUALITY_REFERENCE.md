# HTrace GTAO 质量档与参数档案（Ho-GTAO 的对照依据）

> 状态：**对照档案（2026 文档审核核对）**。HTrace 侧全部是包源码事实（`D:\Unity_Project\BREAK_URP\Assets\HTraceAO`，URP 前向 / compute 版 GTAO 链）；Ho 侧一列已更新为**当前实现现状**。
> 用途：Ho-GTAO 调档 / 加档时的原始依据——HTrace 原本有什么档、每个参数怎么生效、我们保留或剪掉了什么。
> 配套：`Ho-GTAO.md`（Ho-GTAO 规划）、`归档/LILTOON_GTAO_H_TRACE_ALIGNMENT_WORKSHEET.md`（对齐口径与坑）。
> 行号为 HTrace 包源码实际行号；缩写：GTAO.cs=`Runtime/Shared/MainPasses/GTAO.cs`，HRender=`.../Computes/HRenderGTAO.compute`，HTPF=`HTemporalFilterGTAO.compute`，HSPF=`HSpatialFilterGTAO.compute`，HPYR=`HDepthPyramidAO.compute`，Enums=`Runtime/Shared/Globals/HEnums.cs`，Settings=`Runtime/Shared/Data/Public/GTAOSettings.cs`，Feature=`Runtime/URP/Infrastructure/HTraceAORendererFeature.cs`，Output=`Shaders/URP/OutputCompositionURP.shader`。

## 0. 一句话结论

HTrace GTAO 的“质量”由 **4 个互相独立的轴**构成：**算法（TracingMode）× 去噪（Denoising）× 分辨率（Resolution）× 密度参数（Slice/Step/滤波趟数/历史帧数）**。
⚠️ 它的**默认 profile 只是“中高档”**（HorizonSearch + Step16/Slice2 + Box×2 + Linear 重投影），不是全开。

## 1. HTrace 全链路（10 步，顺序固定）

```text
[0] Feature 按 InjectionPoint 排 pass：PrePass(矩阵/噪声/兜底) → (MotionVectors，仅 SampleCountTemporal>1) → GBuffer → GTAO 链
[1] Temporal Reprojection   GTAO.cs:228-241 → HTPF:64-295   运动矢量重投影历史（含分辨率/窗口缩放补偿）；4 邻居双线性或 Bicubic；
                                                            深度/平面/法线(0.5)阈值判样本有效性；输出 R32_UInt 打包的 ReprojectedGTAO
[2] Buffer Cleanup          GTAO.cs:245-256                 清 FilterDepth / OcclusionNormal / DepthPyramid MIP0-3（顺序固定）
[3] 深度金字塔              GTAO.cs:260-268 → HPYR:53-163   全分辨率深度 → MIP0 全 / 1 半 / 2 四分之一 / 3 八分之一（2×2 取“最远”）；
                                                            ExcludeCasting 层深置 0；非 SimpleRejection 时用运动符号标记深度
[4] Occlusion Tracing       GTAO.cs:272-286 → HRender:134-304 核心 ray march（§2）；输出 RG8(visibility+hitVelocity) 或 RGBA8(+法线)
[5] Temporal Accumulation   GTAO.cs:292-307 → HTPF:300-372  min(计数+1, 6×2=12) 帧累积；5×5 LDS 高斯矩 history clamp；
                                                            写出 Depth / SamplecountNormal / OcclusionVelocity 三张历史
[6] Spatial Filter          GTAO.cs:311-372 → HSPF          Disk：Poisson 8/16 点单发；Box：1~3 趟 ping-pong，步长 (4,2,1)px，每趟 4/8 tap
[7] Interpolation 上采样    GTAO.cs:376-389 → HSPF:246-360  仅 Half/Quarter；按平面+法线加权，输出全分辨率
[8] Output Composition      GTAO.cs:393-412                 BeforeOpaque：pow(|AO|, Intensity×1.2) + Fadeout + ExcludeReceiving lerp；
                                                            AfterOpaque：Blend One SrcAlpha 色彩乘 AO
[9] Debug（可选）           GTAO.cs:416-432 → HDebugAO     MainBuffers/AO/TemporalDisocclusion 四象限拼图
[10] 全局发布               GTAO.cs:434-435                 _HTraceBufferAO（BeforeOpaque 用 OcclusionShaderGraphGTAO，否则 FinalOutput）
                                                            + _ScreenSpaceOcclusionTexture
```

## 2. 算法档位：TracingMode（Enums:52-58）

| | **HorizonSearch**（HTrace 默认，Enum 0） | **VisibilityBitmasks**（Enum 1，更准确档） |
| --- | --- | --- |
| 机制 | 每步 ± 两侧采样 → 3D delta → `Horizon = dot(dir,delta)·rsqrt(|delta|²)`；各步对 ± 取 `max` → 该 slice 最大地平角 → `HFastACos` 反解 → 解析余弦加权积分（Jimenez 2016 精确公式，HRender:243-275） | 每步 ± 两侧 → 算**两个**余弦（正面地平线 / 反面 = 减厚度张角）→ `HFastACos` + smoothstep 归一化到 [0,1] 32 档 → `UpdateBitmask` 把 [min,max] 置成 32 位连续位段并 OR 累加 → slice 结算 `1 − countbits(mask)/26` |
| 精度 | 连续无量化，平滑、噪声低 | 32 档量化（≈5.6°/bin），**每个采样独立标记“被遮挡的角度段”**：细杆/栅栏/薄柱这类只占小段角度的遮挡体被精确编码；HorizonSearch 取“最大地平线”会把小段遮挡当整段（**过度暗**） |
| 代价 | 每步 1×rsqrt + 少量 FMA；slice 结算 2×近似 acos | 每步 2×rsqrt(float2) + 2×acos + 2×smoothstep + 整型移位/OR/位计数；**countbits 需 SM5**（GLES3 片段不支持）→ 移动端不可行；量化噪声**必须配 temporal** |
| 距离衰减 | **恒有效** | 受 `_UseAttenuation` 控制 |
| 最佳匹配 | 移动端 / 低配 / 无 temporal | 细几何多的前后景（栅栏/发丝/细柱）+ 有 temporal 的静态镜头 |

> 官方自述 bitmask “更精确，尤其薄几何，中等性能代价”，horizon “快而高效”。**两套都是“精确版”**：bitmask 优势在薄几何，horizon 无量化、设备门槛低。
> **Ho-GTAO 现状**：只实现 Bitmask（32-bin，已修正 signed horizon cosine），**没有 HorizonSearch 连续 arc 分支**——所以拿 HTrace 默认档直接对比会把 bitmask 的角度量化误判成 Ho 的算法错误。

## 3. 去噪档位：Denoising（Enums:149-159）

| 档 | 链路 | 表现 | 备注 |
| --- | --- | --- | --- |
| None | 无重投影/累积/空间滤波，直接出 AO+法线 | 最便宜（省 4 个 pass）；静止噪声/闪烁明显 | 无历史依赖，无鬼影/disocclusion 问题 |
| SpatialOnly | 无重投影；Trace（噪声相位**冻结**）→ Spatial → Composition | 静态噪声图案被平滑、不闪；薄细节被糊 | ⚠️ **坑**：Disk 路径读从未更新的 `TemporalDataReprojected`（当 clamp 权重 0）；Box 无此问题 |
| TemporalOnly | 重投影 → Trace → 累积（无 Spatial） | 12 帧累积噪声极干净；**不消空间低频误差/banding**；相机运动有拖影风险 | 需要运动矢量数据 |
| **SpatioTemporal**（HTrace 默认，最高档） | 全链：重投影 → 金字塔（运动符号标记）→ Trace → 累积 → Spatial（仅最后一趟输出可见度）→ Composition | 时间累积 + 空间滤波 + 5×5 history clamp 三者叠加互相增强，**质量上限最高** | vendor tooltip “maximum noise reduction” |

## 4. 分辨率档位：Resolution（Enums:42-50）

- HTrace：Full=(1,1)、Half=(2,1)、Quarter=(2,2)。
- ⚠️ **HTrace 的 Half/Quarter 不是缩小纹理**：所有 AO RT 仍按**全分辨率**分配，只是减少 dispatch 线程数 + 棋盘打包（`HALF_RESOLUTION` keyword）+ 压缩寻址，最后 Interpolation 上采样。
- **Ho-GTAO 现状**：用的是**真降分辨率 RT**（`ResolutionDivisor` = 1/2/4）+ 深度/法线引导上采样——观感等价、省带宽，但不是同一实现。

## 5. 参数全表（HTrace 原始 → 生效点 → 机制 → Ho-GTAO 现状）

| HTrace 参数（默认值） | 生效点 | 机制一句话 | Ho-GTAO 现状 |
| --- | --- | --- | --- |
| TracingMode=HorizonSearch | GTAO.cs:276 | keyword `VISIBILITY_BITMASKS` 切算法 | **只有 Bitmask**；HorizonSearch 未实现 |
| Denoising=SpatioTemporal | GTAO.cs:165/228/292/311 | 门控 temporal/spatial 链路 | 质量档预设：Low=SpatialOnly、Medium=简单 temporal(4 帧)+双层 Box、High=SpatioTemporal |
| Resolution=Full | GTAO.cs:123/141/376 | 棋盘压缩 + 上采样 | 真降分辨率 RT（Full/Half/Quarter） |
| StepCount=16 | GTAO.cs:217 → HRender:153/212 | 每 slice 步数 | 直接映射（默认 32） |
| SliceCount=2 | GTAO.cs:218 → HRender:152/190 | slice 数（π/n 角度） | 直接映射（默认 4） |
| WorldSpaceRadius=1 | GTAO.cs:213 → HRender:170-172 | 世界半径→屏幕像素，Falloff 1/r² | 直接映射（默认 3.0） |
| ScreenSpaceRadius=25 | GTAO.cs:214 | 屏幕像素最小半径地板 | 直接映射（默认 30） |
| Thickness=0.2 | GTAO.cs:216 → HRender:183 | 厚度；Linear = `max(T/10·depth, T)` | 直接映射（默认 0.6） |
| ThicknessMode=Linear | GTAO.cs:220 | Linear vs Uniform 厚度假设 | **只做 Linear**（更物理） |
| UseAttenuation=true | GTAO.cs:219 → HRender:128 | 仅 bitmask 分支；horizon 恒衰减 | 保留（默认开） |
| DepthFormat=R16 | GTAOPassURP:179 | 金字塔精度；R32 枚举被注释但代码支持 | Ho 侧不受此枚举约束（自有 pyramid 格式） |
| SampleCountTemporal=8 | Feature:220-222 | ⚠️ **不控制累积帧数**，只门控自制 motion vectors pass | Ho 的 `temporalFrameCount` 映射**真实累积上限**；MV 门控语义不搬（用对象 Motion Mask/Delta + 相机 MV） |
| TemporalRejection=0.7 | GTAO.cs:205/212 | 历史 clamp 强度 / 命中速度缩放 | 直接映射（默认 0.7） |
| UseSimpleRejection=false | GTAO.cs:202/206/277 | 精确 = ray march 命中速度 + 金字塔运动符号 | Ho 已有对象 Motion Mask/Delta 与逐 hit 速度拒绝；render-scale / previous-Z 补偿仍登记为差异 |
| ReprojectionFilter=Linear | GTAO.cs:232 | 双线性 4-tap vs 4×4 gather 锐化 5-tap | Ho 为四 tap（bicubic 未做） |
| UseNormalWeightingTemporal=true | GTAO.cs:233 | 法线点积 > 0.5 作样本有效性 | 直接映射（已做，且移除了多余的整像素 vote） |
| SpatialFilterType=Box | GTAO.cs:317-370 | Disk = Poisson 8/16 自适应半径；Box = 固定步长多趟 | **两种都有**（`HoGTAOSpatialFilter`：Disk 默认，Box 便宜稳定） |
| FilterRadius=0.4（getter remap 后实际 0.048） | GTAO.cs:200 | 世界空间滤波半径（仅 Disk） | 直接映射（默认 0.4，仅 Disk 生效） |
| FilterAdaptivity=0.1 | GTAO.cs:201 | 半径随对比度自适应（仅 Disk） | 直接映射（默认 0.1） |
| BoxPassCount=2 | GTAO.cs:335-369 | 趟数/步长（1→(1,1,1)、2→(2,1,1)、3→(4,2,1)） | 直接映射（High 档 Box×3） |
| DoubleSampleCount=false | GTAO.cs:315 | 采样数翻倍（8→16 / 4→8） | Ho 的 Box 用双层 tap 实现同类效果 |
| DebugModeGTAO=None | GTAO.cs:416-432 | 调试视图（None 才启用 Composition/ApplyIntensity） | feature-local debug（Off/AO/Depth/Normal/Motion/Temporal Disocclusion），不逐档复刻 |
| ExcludeCasting/Receiving=0 | GTAO.cs:192/402 | RenderLayerMask 排除 | **不做**（无层掩码通道）→ 用材质语义 + ScreenProcess 遮罩替代 |
| ExcludedIntensity=0 | HSPF:387-390 | 排除层保留 AO 比例 | 用材质侧 AO 遮罩替代 |
| Intensity=2（×1.2） | GTAO.cs:182 | 输出 pow 强度 | **不带进 producer**：公共契约是 0..1 visibility，强度归材质 `_SSAOStrength`；调试显示另用 `debugIntensity`（HTrace 对照值 3.672，只影响调试画面） |
| Fadeout=0 | GTAO.cs:199/399 | 远处 smoothstep 淡化 | 未做（可选） |
| OutputDithering/Animate=false | GTAO.cs:183-184 | ⚠️ **死参数**（全局已设，包内无 HLSL 消费点） | 忽略 |
| ReconstructNormals=false | GTAO.cs:137/186 | 非八面体法线解码（兼容） | 不需要（GB 有真法线） |

## 6. HTrace“最高档配方”（作者建议，全部可配）

```text
TracingMode=VisibilityBitmasks + Denoising=SpatioTemporal + UseSimpleRejection=false（精确拒绝）
+ Resolution=Full + DepthFormat=R32（代码支持，UI 隐藏）+ ThicknessMode=Linear + UseAttenuation=true
+ StepCount=32 + SliceCount=4 + Thickness≈0.2~0.5 + WorldSpaceRadius≈1~3 + ScreenSpaceRadius≈25
+ TemporalRejection≈0.7~0.8 + SpatialFilterType=Box + BoxPassCount=3 + DoubleSampleCount=true（或 Disk 0.5/0.5）
+ ReprojectionFilter=Linear + UseNormalWeightingTemporal=true（抗鬼影优先；求锐利换 Bicubic）
+ SampleCountTemporal>1（触发自制 MV pass）
```

## 7. HTrace 侧的坑与关键事实（实现 / 调档必读）

1. **`SampleCountTemporal` 不控制累积帧数**（固定 12 帧上限），只门控自制 motion vectors pass——UI 措辞有误导性。
2. **`OutputDithering/Animate` 是死参数**（设置了但无人消费）。
3. **`DepthFormat=R32` 枚举被注释但代码支持**（要做“最高档”可直接启用）。
4. **Half/Quarter = 棋盘压缩寻址，不是缩小纹理**（纹理仍全分辨率分配）。
5. **`UseAttenuation` 只对 bitmask 分支生效**；HorizonSearch 恒有距离衰减。
6. **bitmask 需 SM5 `countbits`**——移动端片段不可行，且 32 档量化噪声必须配 temporal。
7. **SpatialOnly + Disk 会读陈旧的 `TemporalDataReprojected`**（当 clamp 权重 0 处理）；Box 无此问题。
8. **中间缓冲存“AO 量”（1−可见度），最终输出存“可见度乘数”（1=无 AO）**，再由 OutputComposition 做 pow——fragment 版必须沿用这个极性约定。
9. **精确拒绝（`UseSimpleRejection=false`）依赖 HTrace 自制 MotionVectors/MotionMask/MotionDelta**；没有自制 MV 就只能退化。
10. HTrace 默认 profile 实为**中高档**（HorizonSearch + Step16/Slice2 + Box×2 + Linear）；朱木古堂场景 profile：`GTAO Mode=1, Intensity=3.06, WorldSpaceRadius=5, Thickness=0.2, ScreenSpaceRadius=25, Slice=2, Step=16, Denoising=3(SpatioTemporal), Temporal=8, FilterRadius=0.5, Adaptivity=0.5`。

## 8. HTrace 三档 vs Ho-GTAO 三档（现状）

| 档 | HTrace | Ho-GTAO（`HoGTAOSettings`） |
| --- | --- | --- |
| Low | Slice1 / Step8 / SpatialOnly(Box×1) / Half / 16F | **Bitmask + 仅空间滤波 + Quarter + 8 步** |
| Medium | Slice2 / Step16 / SpatioTemporal(简单拒绝) / Box×2 / Full / R16 | **Bitmask + 简单时间累积(4 帧) + 双层 Box + Half + 12 步** |
| High（Ho 默认） | Slice4 / Step32 / 精确拒绝 / Box×3+double / R32 / Bitmask | **Bitmask + SpatioTemporal + Full + 4 切片 / 32 步 + 12 帧累积 + Box×3**（Disk/Box 皆可选） |

> 差异说明：Ho-GTAO 算法固定 Bitmask，HTrace 的 Medium 默认是 HorizonSearch——即“**Ho 默认比 HTrace 默认高一档算法、低一档密度**”，视觉上限对齐 HTrace 的 High。

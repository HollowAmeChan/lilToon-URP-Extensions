# HTrace GTAO 质量档与参数档案（供 Ho-GTAO 质量档切换参考）

> 状态：**档案**（HTraceAO 包源码事实，`D:\Unity_Project\BREAK_URP\Assets\HTraceAO`，URP 前向 / compute 版 GTAO 链）。
> 用途：未来做 Ho-GTAO 质量档切换时的原始依据——HTrace 原本有什么档、每个参数怎么生效、我们剪枝保留了什么。
> 配套：`LILTOON_GTAO_PLAN.md`（Ho-GTAO 实现规划，本标准答案）；本档案只回答"HTrace 原本如何"。
> 行号：以下所有行号为**当前包源码实际行号**；缩写：GTAO.cs=`Runtime/Shared/MainPasses/GTAO.cs`，HRender=`Runtime/Shared/Resources/HTraceAO/Computes/HRenderGTAO.compute`，HTPF=`.../HTemporalFilterGTAO.compute`，HSPF=`.../HSpatialFilterGTAO.compute`，HPYR=`.../HDepthPyramidAO.compute`，Settings=`Runtime/Shared/Data/Public/GTAOSettings.cs`，Feature=`Runtime/URP/Infrastructure/HTraceAORendererFeature.cs`，Enums=`Runtime/Shared/Globals/HEnums.cs`，Output=`Shaders/URP/OutputCompositionURP.shader`。

---

## 0. 一句话结论

HTrace GTAO 的"质量"由 **4 个轴** 构成，互相独立可组合：
**算法（TracingMode）× 去噪（Denoising）× 分辨率（Resolution）× 密度参数（Slice/Step/滤波趟数/历史帧数）**。
⚠️ 其**默认 profile 只是"中高档"**（HorizonSearch + Step16/Slice2 + Box×2 + Linear 重投影），不是全开。

---

## 1. 全链路（10 步，顺序固定）

```text
[0] Feature 按 InjectionPoint 排 pass：PrePass(矩阵/噪声/兜底) → （MotionVectors, 仅 SampleCountTemporal>1 时）→ GBuffer → GTAO 链
[1] Temporal Reprojection    GTAO.cs:228-241 → HTPF:64-295   仅 Denoising 含 Temporal 时；用运动矢量把历史重投影到当前帧（HTPF:118-121，含分辨率/窗口缩放补偿）；
                                                               4 邻居双线性（HTPF:224-259）/ Bicubic（HTPF:141-163,222-260）；深度阈值(HTPF:92)+平面阈值(:93)+法线阈值 0.5(:94, USE_NORMAL_WEIGHTING) 作样本有效性；
                                                               输出 TemporalDataReprojectedGTAO（R32_UInt 打包：speed12bit/count4bit/occlusion16bit, HCommonAO.hlsl:115-131）
[2] Buffer Cleanup           GTAO.cs:245-256                 清 FilterDepth/OcclusionNormal(Filtered)/DepthPyramid MIP0-3（顺序固定）
[3] 深度金字塔              GTAO.cs:260-268 → HPYR:53-163   全分辨率深度 → MIP0=全 / MIP1=½ / MIP2=¼ / MIP3=⅛（2×2 合并，取"最远深度" HPYR:25-48）；
                                                               ExcludeCasting 层深度置 0（HPYR:102-105）；!SimpleTemporalRejection 时用运动符号标记深度（HPYR:107-114）
[4] Occlusion Tracing       GTAO.cs:272-286 → HRender:134-304   核心 ray march（见 §2）；输出 _Occlusion RG8(visibility+hitVelocity) 或 _OcclusionNormal RGBA8(+法线)
[5] Temporal Accumulation   GTAO.cs:292-307 → HTPF:300-372  min(计数+1, 6×2=12) 帧累积（HTPF:323-324）；5×5 LDS 高斯矩 history clamp（HTPF:305-362，系数 2~5 由 rejection 决定 :330/:355）；
                                                               输出新三张历史：Depth / SamplecountNormal(计数/16+法线) / OcclusionVelocity(值+速度)（HTPF:368-371）
[6] Spatial Filter          GTAO.cs:311-372 → HSPF           Disk：Poisson 8/16 点单发（HSPF:40-96,161-241）；Box：1~3 趟 ping-pong，步长 (4,2,1)px（GTAO.cs:334-369），每趟 4/8 tap（HSPF:129-151）
[7] Interpolation 上采样    GTAO.cs:376-389 → HSPF:246-360  仅 Half/Quarter；按平面+法线加权；输出全分辨率（HSPF:359）
[8] Output Composition      GTAO.cs:393-412                非 AfterOpaque：HSPF:364-393（pow(|AO|,Intensity×1.2) 仅 DebugMode==None 时 _ApplyIntensity=1、Fadeout、ExcludeReceiving lerp）
                                                             AfterOpaque：Output:37-87（Blend One SrcAlpha 色彩乘 AO）
[9] Debug（可选）           GTAO.cs:416-432 → HDebugAO      MainBuffers/AO/TemporalDisocclusion 四象限拼图
[10] 全局发布               GTAO.cs:434-435                 _HTraceBufferAO（BeforeOpaque 用 OcclusionShaderGraphGTAO；否则 FinalOutput）+ _ScreenSpaceOcclusionTexture
```

---

## 2. 算法档位：TracingMode（Enums:52-58）

| | **HorizonSearch**（默认，Enum 0） | **VisibilityBitmasks**（Enum 1，更准确档） |
| --- | --- | --- |
| 机制 | 每步±两侧采样 → 3D delta → `Horizon=dot(dir,delta)·rsqrt(\|delta\|²)`；各步对±取 `max`（HRender:243-257）→ 该 slice 最大地平角 → `HFastACos` 反解 → 解析余弦加权积分 `0.25·(arcL+arcR)·ProjNormalLen`，除 `ProjNormalLen·(N·sinN+cosN)`（HRender:267-275，与 Jimenez 2016 一致的精确公式） | 每步±两侧 → 算**两个**余弦（正面地平线 / 反面=减厚度张角，HRender:119-120）→ `HFastACos`+smoothstep 归一化到 [0,1] 32 档 → `UpdateBitmask` 把 [min,max] 置为 32 位连续位段并 OR 累加（HRender:69-76,221-242）→ slice 结算 `1 - countbits(mask)/26`（HRender:261-263） |
| 精度 | 连续（无量化），平滑、噪声低 | 32 档量化（≈5.6°/bin），**每个采样独立标记"被遮挡的角度段"**——细杆/栅栏/薄柱这类只占小段角度的遮挡体被精确编码；HorizonSearch 取"最大地平线"会把小段遮挡当成整段（**过度暗**） |
| 代价 | 每步 1×rsqrt + 少量 FMA；slice 结算 2×近似 acos + sin/cos | 每步 2×rsqrt(float2) + 2×acos + 2×smoothstep + 整型移位/OR/位计数；**countbits 需 SM5**（GLES3 片段不支持）→ 移动端不可行；量化噪声**必须配 temporal 去噪** |
| 距离衰减 | **恒有效，无开关**（HRender:94-95） | **受 `_UseAttenuation` 控制**（HRender:123-128，唯一生效点） |
| 最佳匹配 | 移动端/低配/无 temporal 引擎 | **细几何多的前后景（栅栏/发丝/细柱）+ 有 temporal 的静态镜头**（PC/SM5+） |

**结论**：官方自述 bitmask"更精确，尤其薄几何，中等性能代价"（Enums:56）、horizon"快而高效"（Enums:54）。**两套都是"精确版"**——bitmask 优势在薄几何，horizon 无量化且设备门槛低。

---

## 3. 去噪档位：Denoising（Enums:149-159）

| 档 | 链路（GTAO.cs 行号） | 表现 | 备注 |
| --- | --- | --- | --- |
| None | 无重投影/无累积/无空间滤波；Trace 走 DEPTH_NORMAL_OUTPUT（GTAO.cs:275）直接出 AO+法线；`IsFinalPass=1` 输出可见度；UseTemporalNoise=1（噪声每帧换相位） | 最便宜（省 4 个 pass）；静止画面噪声/闪烁明显 | 无历史依赖（无鬼影/disocclusion 问题） |
| SpatialOnly | 无重投影；Trace（DEPTH_NORMAL_OUTPUT on；**UseTemporalNoise=0 → 噪声相位冻结**，GTAO.cs:221）→ Spatial → Composition | 静态噪声图案被平滑、不闪；但每帧新采样**时间上仍闪烁**；薄细节被糊 | ⚠️ **坑**：Disk 路径读从未更新的 `TemporalDataReprojected`（HSPF:185-190，当 clamp 权重 0 处理）；Box 不读它，无此问题 |
| TemporalOnly | 重投影 → Trace（写 _Occlusion float2）→ 累积（`IsFinalPass=1`）→ 无 Spatial | 12 帧指数累积噪声极干净；**不消空间低频误差/banding**；相机运动有拖影风险（靠 depth/normal 校验+clamp 缓解）；历史失效即回噪声 | 需要运动矢量数据（见 §8 坑①） |
| **SpatioTemporal**（默认，最高档） | 全链：重投影 → 金字塔（运动符号标记）→ Trace → 累积（`IsFinalPass=0` 存 AO 量）→ Spatial（仅最后一趟 `IsFinalPass=1` 输出可见度）→ Composition | 时间累积 + 空间滤波 + 5×5 history clamp 三者叠加互相增强（空间滤波"对比度自适应"参考已累积数据）——**质量上限最高** | vendor tooltip"maximum noise reduction"（Enums:157） |

---

## 4. 分辨率档位：Resolution（Enums:42-50）

- Full=(1,1)；Half=(2,1)；Quarter=(2,2)（GTAO.cs:141-143）。
- ⚠️ **HTrace 的 Half/Quarter 不是缩小纹理**：所有 AO RT 按**全分辨率**分配（GTAOPassURP:159-182），只是减少 dispatch 线程数 + 棋盘打包（HALF_RESOLUTION keyword）+ `GetUnscaledCoords` 压缩寻址（HCommonAO.hlsl:40-44），最后 Interpolation 上采样（HSPF:246-360，Half=交叉/十字 4 邻域，Quarter=2×2 双线性，平面+法线加权）。
- 质量影响：Half 减少一半像素计算（棋盘）；Quarter 减 3/4。细节/噪声受影响，性能 ↑2-4×。

---

## 5. 参数全表（HTrace 原始 → 生效点 → 机制 → 质量影响 → Ho-GTAO 剪枝）

| HTrace 参数（默认值） | 生效点 | 机制一句话 | 质量影响 | **Ho-GTAO 剪枝决定** |
| --- | --- | --- | --- | --- |
| TracingMode=HorizonSearch | GTAO.cs:276 → HRender 两分支 | keyword `VISIBILITY_BITMASKS` 切换算法 | 高 | **默认 Bitmask**（用户要求最高档）；HorizonSearch 留作同 shader keyword 后备（移动端/无 temporal） |
| Denoising=SpatioTemporal | GTAO.cs:165-166/228/292/311 | 门控 temporal/spatial 链路 | 高 | **Quality 档预设**（High/Medium/Low 映射 SpatioTemporal/SpatioTemporal(简)/SpatialOnly） |
| Resolution=Full | GTAO.cs:123-125/141-143/376-389 | 棋盘压缩 + 上采样 | 中 | 映射（我们可用真半分辨率 RT，等价） |
| StepCount=16 | GTAO.cs:217 → HRender:153/212 | 每 slice 步数 | 高 | 直接映射 |
| SliceCount=2 | GTAO.cs:218 → HRender:152/190 | slice 数（π/n 角度） | 高 | 直接映射 |
| WorldSpaceRadius=1 | GTAO.cs:213 → HRender:170-172/185 | 世界半径→屏幕像素；Falloff=1/r² | 中-高 | 直接映射（场景=5） |
| ScreenSpaceRadius=25 | GTAO.cs:214 → HRender:171-172 | 屏幕像素最小半径地板（FOV/分辨率换算 GTAO.cs:154-157） | 中 | 直接映射 |
| Thickness=0.2 | GTAO.cs:216 → HRender:183 | 厚度；Linear = max(T/10·depth, T) | 中 | 直接映射 |
| ThicknessMode=Linear | GTAO.cs:220 → HRender:183 | Linear vs Uniform 厚度假设 | 中 | 我们只做 Linear（更物理） |
| UseAttenuation=true | GTAO.cs:219 → HRender:128 | **仅 bitmask 分支**；horizon 恒衰减 | 中 | 保留（Bitmask 默认开） |
| DepthFormat=R16 | GTAOPassURP:179 | 金字塔精度；R32 枚举被注释但**代码支持**（Enums:74-75） | 低-中 | v1 R16；高配可 R32 |
| SampleCountTemporal=8 | **Feature:220-222 / MotionVectorsPassURP:105-106** | ⚠️ **不控制累积帧数**（累积上限硬编码 `g_HTemporalSamplecountAO=6` → 12 帧，GTAO.cs:187 → HTPF:323-324）——只门控**自制 motion vectors pass** | 高（间接：时间稳定依赖 MV） | 我们的 `TemporalFrameCount` 映射**真实累积上限**；MV 门控语义不搬（我们 v1 用相机运动+校验） |
| TemporalRejection=0.7 | GTAO.cs:205/212 → HTPF:330 / HRender:290 | 历史 clamp 强度/命中速度缩放 | 中-高 | 直接映射 |
| UseSimpleRejection=false | GTAO.cs:202/206/209/277 → HPYR:107-114 / HRender:279-287 | 精确=ray march 命中速度+金字塔运动符号标记；简单=固定强度 | 高（运动场景） | v1 用"简单"（相机运动+校验）；精确拒绝记 v1.1 |
| ReprojectionFilter=Linear | GTAO.cs:232 → HTPF:141-163/222-260 | 双线性 4-tap vs 4×4 gather 锐化 5-tap（footprint 无效回退双线性） | 中 | v1 双线性；bicubic 记 v1.1 |
| UseNormalWeightingTemporal=true | GTAO.cs:233 → HTPF:198-208 | 法线点积>0.5 作样本有效性（**仅非 Bicubic**） | 中 | 直接映射（v1 做） |
| SpatialFilterType=Box | GTAO.cs:317-370 | Disk=8/16 poisson 自适应半径；Box=4/8 tap 固定步长多趟 | 中 | **Box 为主**（简单稳定）；Disk 记 v1.1（或 High 档启用） |
| FilterRadius=0.4（getter remap 0-0.12 → **实际 0.048**） | GTAO.cs:200 → HSPF:197-202 | 世界空间滤波半径（仅 Disk 生效） | 中 | 做 Disk 才需要 |
| FilterAdaptivity=0.1 | GTAO.cs:201 → HSPF:192 | 半径随对比度自适应（仅 Disk 生效） | 中 | 同上 |
| BoxPassCount=2 | GTAO.cs:335-369 | 趟数/步长（1→(1,1,1) 2→(2,1,1) 3→(4,2,1)），最后一趟 IsFinalPass=1 | 中 | 直接映射 |
| DoubleSampleCount=false | GTAO.cs:315 → HSPF:44-94/129-132 | 采样数翻倍（8→16 / 4→8） | 中 | 直接映射 |
| DebugModeGTAO=None | GTAO.cs:416-432 | 调试视图（None 才启用 Composition/ApplyIntensity） | 低 | 用 DebugTile 直出替代（不逐档复刻） |
| ExcludeCasting/Receiving=0 | GTAO.cs:192-194/402 | RenderLayerMask 排除 | 中（功能） | 不做（无层掩码通道）→ 以材质语义替代 |
| ExcludedIntensity=0 | HSPF:387-390 / Output:78-81 | 排除层保留 AO 比例 | 中 | 用材质 `_SSAOMask` 替代 |
| Intensity=2 | GTAO.cs:182（**×1.2**）→ HSPF:377-378 / Output:69 | 输出 pow 强度 | 中-高（观感） | **不带**（契约 0..1，强度归材质 `_SSAOStrength`，见计划 §2.4） |
| Fadeout=0 | GTAO.cs:199/399 | 远处 smoothstep 淡化 | 中 | 可选（v1.1） |
| OutputDithering/Animate=false | GTAO.cs:183-184 | ⚠️ **死参数**（全局已设，包内无任何 HLSL 消费点） | 无 | 忽略 |
| ReconstructNormals=false | GTAO.cs:137-138/186 | 非八面体法线解码（兼容） | 中 | 不需要（ScreenGeometryBuffer 有真法线） |

---

## 6. HTrace "最高档配方"（作者建议，全部可配）

```text
TracingMode=VisibilityBitmasks + Denoising=SpatioTemporal + UseSimpleRejection=false（精确拒绝）
+ Resolution=Full + DepthFormat=R32（代码支持，UI 隐藏）+ ThicknessMode=Linear + UseAttenuation=true
+ StepCount=32 + SliceCount=4 + Thickness≈0.2~0.5 + WorldSpaceRadius≈1~3 + ScreenSpaceRadius≈25
+ TemporalRejection≈0.7~0.8 + SpatialFilterType=Box + BoxPassCount=3 + DoubleSampleCount=true（或 Disk + 0.5/0.5）
+ ReprojectionFilter=Linear + UseNormalWeightingTemporal=true（抗鬼影优先；求锐利换 Bicubic）
+ SampleCountTemporal>1（触发自制 MV pass）
```

---

## 7. 关键事实与坑（实现/后续切换必读）

1. **`SampleCountTemporal` 不控制累积帧数**（GTAO 固定 12 帧上限），只门控自制 motion vectors pass——文档/UI 措辞有误导性。
2. **`OutputDithering/Animate` 是死参数**（设置全局但无消费）。
3. **`DepthFormat=R32` 枚举被注释但代码支持**（若要做"最高档"可直接启用）。
4. **Half/Quarter = 棋盘压缩寻址，不是缩小纹理**（纹理全分辨率分配）。
5. **UseAttenuation 只对 bitmask 分支生效**；HorizonSearch 恒有距离衰减。
6. **bitmask 需 SM5 `countbits`**——移动端片段不可行，且 32 档量化噪声须配 temporal。
7. **SpatialOnly+Disk 读陈旧 `TemporalDataReprojected`**（坑；Box 无此问题）。
8. **中间缓冲存"AO 量"（1-可见度），最终输出存"可见度乘数"（1=无 AO）**，OutputComposition 再 pow——fragment 版必须沿用此约定（`HRender:291-292` / `HSPF:359`）。
9. 精确拒绝（`UseSimpleRejection=false`）需要 HTrace 自制 MotionVectors/MotionMask/MotionDelta 数据——若无自制 MV pass，temporal 只能退化为"简单拒绝"或仅相机运动近似。
10. 默认 profile（`Default HTraceAO Profile.asset`）实为**中高档**（HorizonSearch + Step16/Slice2 + Box×2 + Linear），不是全开；朱木古堂场景 profile：`GTAO Mode=1, Intensity=3.06, GTAOWorldSpaceRadius=5, GTAOThickness=0.2, ScreenSpaceRadius=25, Slice=2, Step=16, Denoising=3(SpatioTemporal), Temporal=8, FilterRadius=0.5, Adaptivity=0.5`（其余默认）。

---

## 8. HTrace 三档 vs Ho-GTAO 三档对照

| 档 | HTrace 原始参数 | Ho-GTAO 计划（见 plan §2.2 表） |
| --- | --- | --- |
| Low | Slice=1/Step=8/SpatialOnly(Box×1)/Half/16F | Bitmask+Spatial(Box×1)+Quarter+2×8 |
| Medium（默认） | Slice=2/Step=16/SpatioTemporal(简单拒绝)/Box×2/Full/R16 | Bitmask+SpatioTemporal(简)+Disk?+Half+2×16+8帧 |
| High | Slice=4/Step=32/精确拒绝/Box×3+double/R32/Bitmask | Bitmask+SpatioTemporal+Disk+Full+4×32+12帧（HorizonSearch 为后备 keyword） |

> 差异说明：Ho-GTAO 算法固定 Bitmask（v1 计划），HTrace 的 Medium 默认是 HorizonSearch——即"Ho 默认比 HTrace 默认高一档算法、低一档密度"，视觉上限对齐 HTrace 的 High。

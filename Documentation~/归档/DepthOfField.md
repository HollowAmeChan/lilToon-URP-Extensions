# 景深（ScreenProcess · `DepthOfField`）

> **状态：已修复并验证。** 本文记录根因（含一次行为回归）、修复内容、修复前后实测对比，以及仍然存在的取舍。
> 相关：`归档/Outline_Surface_Semantics_Investigation.md`（描边与景深的深度来源）、`Ho-GeometryBuffer.md` §3.3。

## 0. 定位与判定标准

ScreenProcess 的景深是**图像合成**景深：用 GeometryBuffer 的线性眼深（描边像素用描边自己的 visual depth）算 CoC，再对当前颜色层做一次 **CoC 一致的 gather 模糊**。

判定标准是物理的，不是口味：

> **合焦的不透明几何，CoC≈0，它的像不扩散；而且它在物理上遮挡它后面的东西。因此背景像素里不允许出现该主体的颜色。**

任何一条"DoF 有一圈 halo"的抱怨最终都落在这一条上。这也是本次修复的验收线。

## 1. 根因（修复前，一手证据）

文件：`Runtime/ScreenProcess/Shaders/ScreenProcess/DepthOfField.shader`

| 位置（修复前） | 事实 | 含义 |
| --- | --- | --- |
| `175-209` `SampleBlur()` | `ADD_DOF_SAMPLE` 只做 `color += tex * sampleWeight` | **没有任何一处读取 tap 自己的深度或 CoC**：权重与深浅、距离完全无关 |
| `222-225` `Frag()` | 只在中心像素采一次深度，算一个 `coc` → `radiusPx` 与 `amount` | 半径是"我自己的"，不是"来样自己的" |
| `231-232` | `lerp(source, blurred, amount)`，`amount = saturate(coc * _Intensity)` | 混合比与 CoC 同源，部分虚化变成"锐利原件叠在模糊件上" |

后果：一个像素只用**自己**的 CoC 决定半径。背景像素 CoC 很大 → 它以这个巨大半径把**合焦的前景**（自身 CoC≈0、本应完全锐利、且物理上遮挡背景）当成背景的一部分平均进来 → 主体外缘一圈 halo。这就是"把前景的东西模糊到背景"。

复现（修复前，面板默认 最大半径 18）：合焦主体向背景渗色 **12 列 / 峰值误差 0.272**（主体/背景色差 0.6 的 45%），halo 宽度随「最大半径」线性增长（6→4 列、18→12 列、48→33 列）。

对照生产实现，缺的是同一条规则：

- URP 自带 Bokeh DoF：`half farCoC = max(min(samp0.a, samp.a), 0.0); half farWeight = saturate((farCoC - disp.z + _BokehConstants.y) / _BokehConstants.y);`
  （`.codex-research/CompileCheck/Library/PackageCache/com.unity.render-pipelines.universal/Shaders/PostProcessing/BokehDepthOfField.shader:130-137`）
- PPv2 同理：`ReferencePackages~/UnityPostProcessingV2/PostProcessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl:167-177`；

而且两者都分近场/远场两层。

### 1.1 一次行为回归：`3485c92` 删掉了唯一的缓解措施

`git show 3485c92 -- Runtime/ScreenProcess/Shaders/ScreenProcess/DepthOfField.shader` 里有两处删除，提交标题只提到了"用描边 visual normal depth"：

```diff
-                        float outlineCoverage = SampleOutlineCoverage(sampleUv); \
-                        if (outlineCoverage <= 0.5) \
-                        { \
-                            color += ... * sampleWeight; \
-                            weight += sampleWeight; \
-                        } \
+                        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUv) * sampleWeight; \
+                        weight += sampleWeight; \
```

以及 `Frag()` 里 `if (SampleOutlineCoverage(uv) > 0.5) { return source; }` 的提前返回。

- 旧机制：落到描边覆盖像素上的 tap 不计入，描边像素自己不被虚化。粗暴，但方向对——挡住最显眼的那圈渗色源。
- 新机制 `SampleVisualEyeDepth()` 只改**中心像素**的深度。它达成了提交标题的意图（描边参与自然景深），但**结构上不可能**阻止任何 tap 渗色，因为渗色由 tap 权重决定。
- 覆盖范围还收窄了：旧代码读的 `_HoGeometryBufferOutlineCoverageTexture` 只在 MSAA resolve 路径存在（`HoGeometryBufferPass.cs:193-201`、`474-475` 回退为黑图），**非 MSAA 下旧机制本来就是空操作**。所以对开 MSAA 的工程，`3485c92` 是明确的行为回归。

本次修复没有把那个 hack 加回来：下面的 reach 权重**自动**覆盖了它的全部作用，而且不再依赖 MSAA。描边特性本身完整保留并加强（§2.3）。

### 1.2 为什么不是"采样太稀"

抽稀是另一个真实缺陷（大半径下变成稀疏斑点、且随时间抖），但不是本现象的原因：修复前的核权重是常量、且与深浅无关，**抽得再密，前景颜色依然每个 tap 全额进来**。修复后用同一套核、同样的稀疏度，渗色归零（§4）。

## 2. 当前实现

### 2.1 带符号 CoC

`ResolveSignedCoc()`（`DepthOfField.shader:192-203`）把两种模式统一成**有符号** CoC：负 = 近于焦平面（前景），正 = 远于焦平面（背景）。Gaussian 模式没有焦平面，只产出远侧（保持它"远景虚化"的语义，`ResolveGaussianCoc` 对 `depth <= 开始距离` 恒为 0）。Bokeh 模式按 `sign(depth - focusDistance)` 取符号。CoC 公式与参数含义**未改动**（见 §6.2）。

### 2.2 tap 的 reach 权重（本次修复的核心）

`ScreenProcessDofKernelLq/Hq` + `SampleBlur()`（`227-262`）：

```hlsl
float2 offsetPx = ResolveBokehOffset(dir) * radiusPx;
float2 sampleUv = uv + offsetPx * texel;
float distPx = length(offsetPx);
float tapCoc = ResolveSignedCoc(SampleVisualEyeDepth(sampleUv));
float tapWeight = saturate((abs(tapCoc) * maxRadiusPx - distPx + marginPx) / marginPx);
```

**一个 tap 只能填它自己的 CoC 够得着的那个圆。** 比它所在距离更锐利的 tap 一旦被放进来，就是把合焦主体抹到失焦背景上（反之亦然）。`marginPx = max(radiusPx * 0.1, 1px)` 让这个截断不是硬边。中心样本恒参与（`ScreenProcessDofCenterWeight = 1.25`）：任何像素都不该被邻居抹掉自己的值。

这条规则同时取代了 §1.1 的描边 hack：合焦的描边 reach≈0 → 自动不渗色；失焦的描边 reach 足够 → 自然参与虚化。**不需要**区分描边与否。

### 2.3 描边 visual depth 保留并扩展到 tap

`SampleVisualEyeDepth()`（`145-158`）在描边壳覆盖处返回描边自己的线性眼深，否则回退到 `NormalDepth`。它现在**同时用于中心像素和每一个 tap**（`240`），因此：

- 描边与其所属表面一起被聚焦/虚化，而不是被强行保持锐利（`3485c92` 的特性，完整保留）；
- 描边不再需要 MSAA 才有正确行为（旧 hack 只在 MSAA 路径存在）；
- 描边与主体之间不会出现"描边锐、主体糊"的接缝。

### 2.4 采样核：黄金角螺旋

`r = sqrt((i + 0.5) / N)`，`θ = i × 2.399963…`（黄金角）。`sqrt` 让 tap 按**面积**均匀而不是按半径均匀，因此没有内圈空环、也没有外侧空心；黄金角避免它们排成辐条。修复前是两张手写表，其中最近 tap 在 `最大半径=18` 时位于 9.2 px（LQ）/4.1 px（HQ），近场连自己的 CoC 半径都够不到。

| 档位 | tap 数 | 最近 tap | 最远 tap |
| --- | --- | --- | --- |
| 低（`_LayerParams1.w = 0`） | 16（修复前 12） | 3.2 px | 17.7 px |
| 高（`= 1`） | 48（修复前 28） | 1.8 px | 17.9 px |

表由公式生成，`.codex-research/dof_sim/dof_bleed_check.py` 每次都从 shader 源码里把它们**解析出来跟公式对拍**（`[0]` 检查，偏差 < 1e-6），所以表和公式不会漂移。重新生成：`python .codex-research/dof_sim/dof_bleed_check.py --print-kernels`。

叶片多边形塑形（`ResolveBokehOffset`，`205-221`）与"叶片数量/弧度/旋转"三个参数行为不变；`distPx` 用的是塑形**之后**的偏移，所以 reach 判定与真实采样点一致。

### 2.5 合成：模糊强度只由半径表达

`Frag()`（`286-293`）：

```hlsl
float radiusPx = abs(coc) * maxRadiusPx;
float amount = saturate(_Intensity) * LilScreenProcessResolveLayerMask(uv);
...
return half4(lerp(source.rgb, blurred.rgb, amount), source.a);
```

修复前是 `amount = saturate(coc * _Intensity)`。CoC 同时进了**半径**和**混合比**，后果是部分失焦的像素只有 `coc` 比例被模糊、其余保持锐利——像贴了一层透明的锐利原件（双影），而不是"更小的 PSF"。修复后模糊强度完全由半径表达（这正是镜头的行为），`_Intensity` 只作为图层淡出，与其它 ScreenProcess 效果一致。

顺带修掉了"景深强度被用两次"：`p3.x` 现在只影响 CoC（=半径），不再额外乘一次混合比。

## 3. 参数与 Inspector

参数槽位**未改动**，Inspector 布局与行数**未改动**（`Editor/PostProcessing/ScreenProcess/Filters/DepthOfField.cs`）：

| 槽位 | 内容 |
| --- | --- |
| `parameters0` | (模式 0 Gaussian/1 Bokeh/2 目标跟焦, 焦点距离, 焦距, 光圈) |
| `parameters1` | (Gaussian 开始距离, Gaussian 结束距离, 最大半径 px, 高质量采样) |
| `parameters2` | (叶片数量, 叶片弧度, 叶片旋转) |
| `parameters3` | (景深强度/coc gain, 前景虚化, 远景虚化, 虚化曲线) |

三个预设（`ScreenProcessStackVolumeEditor.Presets.cs:241-269`）：`Gaussian 远景虚化`（半径 20，gain 2.4）、`Bokeh 人像虚化`（半径 34，gain 4.0，f/2.8）、`目标跟焦强景深`（半径 56，gain 6.0）——三者的 `高质量采样` 都是开。

> 注意：预设的 `前景虚化`（默认 1.0~1.8）是在旧合成下校的。§2.5 之后部分失焦区域比旧版更实，**观感会变强**；这是"更接近镜头"的方向，但旧预设可能需要按眼睛微调。

## 4. 修复前后实测

同一场景（背景 Z=40，主体方块 Z=3 不透明，面板默认参数），`naive` = 修复前的数学，`dof` = 当前实现：

| 检查 | naive | dof |
| --- | --- | --- |
| 主体合焦 → 背景渗色列数（LQ / HQ） | 12 / 8 | **0 / 0** |
| 主线（`最大半径` 6→48） | 4→37 列 | **0 列（全档）** |
| 主体失焦 → 自身扩散峰值 / 范围 | 0.272 / 15 px | 0.076 / 3 px（范围合规，强度保守，见 §6.1） |
| 主体合焦边缘阶跃（锐利参考 0.599） | — | **0.599（锐）** |
| 主体失焦边缘阶跃 | — | **0.254（糊）** |
| 背景合焦条带阶跃（锐利参考 0.450） | — | **0.450（锐）** |
| 背景失焦条带阶跃 | — | **0.042（糊）** |

## 5. 验证

| 检查 | 内容 | 结果 |
| --- | --- | --- |
| `.codex-research/dof_sim/dof_bleed_check.py` | 从 shader 源码解析采样核并与螺旋公式对拍；复刻 `ResolveSignedCoc`/`SampleBlur`/`Frag` 全部数学；5 组检查（核同步、渗色、近场扩散、对焦驱动模糊、CoC 天花板）+ 半径扫描 | 全过，`RESULT: PASS` |
| 同上 `--negative-control` | 把出货 gather 换回修复前的数学，要求检查 FAIL | `[A]`/`[B]` 失败 → `RESULT: FAIL`（检查会咬） |
| `.codex-research/shader-check/check_all.ps1 -Only DepthOfField` | D3DCompiler `ps_5_0` 独立编译（含 48 tap 全展开） | `compiled`，1 checked / 0 failed；blend include 6/6 负对照通过 |

## 6. 已知取舍与尚未验证

### 6.1 近场分层（唯一的真实短板）

reach 权重把"失焦主体向背景扩散"的**范围**限制在它自己的 CoC 半径内（正确），但**强度**偏保守：实测第一列背景像素拿到 0.076 的主体颜色，而镜头应该是 ~0.27（0.45 × 色差），衰减到 6.4 px 归零。

原因：单趟 gather 无法重建"主体自己的光圈足迹被覆盖的面积比例"。物理上背景像素的孔径足迹在主体深度处的半径 = 主体的 CoC，被主体剪掉多少取决于**边缘到像素的距离**；gather 只能用"落在主体上的 tap 数量 ÷ 总 tap 数"去估计，而 tap 是按背景像素自己的大半径铺的，于是系统性低估。正确做法是**近场/远场分层**（CoC 膨胀 → 近场层按自己的半径模糊 → 近场覆盖远场），即 URP/PPv2/HDRP 的两三层结构。当前 ScreenProcess 每层只 blit 一趟，要做需要给 feature 加多趟与中间 RT —— 本次没做。

实际观感：失焦前景**自身**是被正确模糊的（§4 第 5 行），只是它压在背景上的那圈"溢出"比真实镜头窄。对 NPR 反而更好读。

### 6.2 CoC 模型（**刻意未改**）

`ResolveBokehCoc` 的分母是**焦点距离**而不是物距（URP 用的是 `FocusDist / linearEyeDepth`），线性无界后被 `saturate` 压平。实测（focus=10、50mm、f/5.6，各自在 4× 焦点距离归一）：

| depth | 本实现 CoC | 薄透镜 CoC |
| --- | --- | --- |
| 1 | 0.456 | 12.000 |
| 3 | 0.354 | 3.111 |
| 5 | 0.253 | 1.333 |
| 10 | 0.000 | 0.000 |
| 20 | 0.375 | 0.667 |
| 40 | 1.000 | 1.000 |
| 160 | 1.000 | 1.250 |

`near(1m)/far(160m)`：本实现 0.456，薄透镜 9.600（差 ~21 倍）。直接后果：**近侧 CoC 有天花板**——默认参数下 `depth → 0` 时 CoC 只到 **0.506**，即前景永远用不满「最大半径」，而远侧很早就饱和到 1.0。

不改的理由：换成薄透镜形式会同时改变远场（d=2f 处直接减半）与近场，等于把 `Gaussian 远景虚化`/`Bokeh 人像虚化`/`目标跟焦强景深` 三个预设全部重新调一遍，而本环境没有 Unity 可以反复看画面。**建议的下一步**是最小侵入版本——只把近侧分母换成物距，远侧原样不动：

```hlsl
float denom = max(min(depth, focusDistance), 0.001);   // 近侧用物距，远侧不变
float coc = focusDelta / denom * lensScale * 0.014 * gain * sideBoost;
```

这样远场逐像素与今天完全一致，只有前景变强（前景不再被 0.506 封顶）。改完需要把 `前景虚化` 从 1.35 往下调回旧观感。

### 6.3 无 GeometryBuffer 时的深度回退

`SampleEyeDepth()` 在 `_HoGeometryBufferValid <= 0.5` 时回退到 `SampleSceneDepth`，但 `ScreenProcessRendererFeature.RequiresDepth()` 现在只为 DropShadow 的 SubjectMask 兼容路径请求 URP 深度（见 `归档/Outline_Surface_Semantics_Investigation.md` §11.2）。也就是说"只开 DoF、且 GeometryBuffer feature 不在 renderer 里"时，`_CameraDepthTexture` 可能没被生产，回退会读到未定义值。要么让 feature 在这种情况下显式请求深度（文档 §11.4 第 4 条的方向），要么按文档宣称的"GB 不可用即 no-op"。本次未动。

### 6.4 需要在实机确认

1. 把「最大半径」从 18 拉到 48，halo 应当**完全不再变宽**（修复前会线性变宽）——这是把本次修复对到画面上的最快方法。
2. 失焦前景的观感（§6.1 的保守强度）是否能接受；不能接受就要做近场分层。
3. 开/关 MSAA 时描边是否都与主体一起虚化（§2.3 之后应当一致；修复前非 MSAA 下没有描边保护）。
4. 半精度 GB 眼深在远裁剪面很大时的台阶（与本次无关，见 `DepthFog.md` §7）。
5. 三个预设在本实现下的观感，特别是 `前景虚化`（§3 的注意）。

## 7. 后续可做

1. **近场/远场分层**（§6.1）：真正的质量跃迁，需要 feature 支持多趟 + 中间 RT。
2. **CoC 换薄透镜**（§6.2）：先做最小侵入版本，再决定是否整体重新标定。
3. **深度不连续处的双边保护**：现在中心像素的 CoC 点采样、颜色双线性采样，剪影边缘会有 1 px 的颜色跨越（实测保留 1 列）。可用"tap 的四个邻居深度一致性"再压一档。
4. 无 GB 时的深度提供者策略（§6.3）。

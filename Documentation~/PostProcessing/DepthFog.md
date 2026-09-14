# 深度雾 / 高度雾（ScreenProcess）设计方案

> **状态：设计规划，尚未实现。** 本文只写"要做什么、为什么可行、怎么落地、怎么验证"，不含已落地代码。
> 相关背景：家族 C（深度/大气/天空遮罩驱动的染色）的划分见 `GradientInvestigation.md` 第 6 节；本效果是那一支里最常用的一个。

## 0. 结论速览

| 问题 | 结论 | 依据 |
| --- | --- | --- |
| 深度雾能不能做 | 能，而且**不依赖 GeometryBuffer**：只用 URP 的相机深度纹理也能做 | `ScreenProcess/Shaders/ScreenProcess/DepthOfField.shader:95-104` 已经写了"GB 优先、否则 `LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams)`"的双路径 |
| 高度雾要不要 position buffer | **不需要**。用"线性眼深 + 屏幕 UV + `unity_CameraInvProjection` / `unity_CameraToWorld`"反投影即可拿到世界坐标 | 深度已可拿到（上一行）；矩阵在 `HoUrp17.3.0/ShaderLibrary/UnityInput.hlsl:100`(`unity_CameraInvProjection`)、`:102`(`unity_CameraToWorld`)、`:234`(`unity_MatrixInvVP`) |
| 天空怎么分辨 | 有 GB 时用 coverage（天空 `a≈0`）得到干净遮罩；没有 GB 时用"深度≈远裁剪面"近似 | `Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl:12-15`、`SkyTyndall.shader:392-393` |
| 做不了什么 | 真实体积散射（需要 raymarch + 光锥/阴影）、按物体高度着色（需要精确高度场或 position RT） | 体积雾需要逐像素积分光源可见性，本包 ScreenProcess 没有光源/阴影资源；position RT 见 §6 阶段 4 |
| 和 URP 自带雾的关系 | 并存但不冲突：URP 的雾是 per-object、tonemap 之前（`ShaderLibrary/Lighting.hlsl:212-223`、`ShaderVariablesFunctions.hlsl:315-327`）；我们是后置、tonemap 之后的屏幕空间雾。两层都开就是叠两次，需要在文档里讲清楚 | 同上；fork 里没有任何 fog RendererFeature（搜 `Ho*Fog`/`HeightFog`/`VolumetricFog` 无结果） |

## 1. 现状调查（都是本仓库/本 fork 的一手证据）

### 1.1 ScreenProcess 的位置与预算

- 注入点：`ScreenProcessRenderPassEvents.cs:7` `ScreenProcessStack = AfterRenderingPostProcessing`，ImageProcess 在 `+1`。**也就是说 ScreenProcess 的整个栈跑在 tonemap 之后**（显示空间）。→ 雾是"显示空间合成雾"：颜色按最终画面取，混合在显示空间做；代价是雾不会参与 bloom、也不会被 tonemap 压肩（见 §6 的取舍）。
- 图层预算（`ScreenProcessLayer.cs:171-181, 218, 225`）：`color`（HDR Color）、`texture`（1 个槽）、`parameters0..parameters5`（**6 个 Vector4 = 24 float**）、`intensity`、`blendMode`（Normal/Add/Screen/Multiply），外加**规则遮罩**（最多 4 条，按 MetadataBuffer 通道）。规则遮罩是白送的：可以立刻得到"只对背景上雾""角色不受雾""只对某材质上雾"。
- 效果注册点：`ScreenProcessEffect.cs:3-12`（枚举）、`ScreenProcessEffectRegistry.cs:5-25`（默认 shader）、`ScreenProcessShaderConstants.cs:7-13`（shader 名常量）。

### 1.2 深度来源与精度

| 来源 | 内容 | 精度 | 何时可用 |
| --- | --- | --- | --- |
| GeometryBuffer `_HoGeometryBufferNormalDepthTexture` | `rgb` = 世界法线（编码，`*2-1` 还原）、`a` = **线性眼深（世界单位）** | RT 是 `R16G16B16A16_SFloat`（每通道半精度，相对精度 ≈ 4.9e-4）→ 100 单位处约 0.05、1000 处约 0.5、4000 处约 2 单位 | `_HoGeometryBufferValid > 0.5`（图层自己声明需要，见 §4） |
| URP 相机深度纹理 | 原始深度 → `LinearEyeDepth(..., _ZBufferParams)` | 取决于深度缓冲（24/32 bit 定点），远距离一般比半精度浮点更好 | 任何情况（`ConfigurePass` 里声明 `ScriptableRenderPassInput.Depth`） |

证据：`HoGeometryBufferFormatUtility.cs:9-13`（格式）、`HoGeometryBufferSampling.hlsl:42-45`（`LilHoGeometryBufferLinearDepthOrFar`）、`DepthOfField.shader:95-104`（两条路径等价使用眼深）、`Outline.shader:39-48`（`Linear01Depth` 变体）。

**设计含义**：深度雾默认"GB 优先、相机深度兜底"，和 DepthOfField 完全一致；文档里要写清"远裁剪面很大时半精度会台阶化，此时把 GeometryBuffer 关掉反而更平滑"这种反直觉但真实的情况。

### 1.3 天空、法线与 coverage

- `LilHoGeometryBufferCoverage(normalDepth) = step(0.0001, a)`（`HoGeometryBufferSampling.hlsl:12-15`）→ **天空/无几何像素 `a≈0`**，`LilHoGeometryBufferLinearDepthOrFar` 会把它们替换成远裁剪面。所以：有 GB 时"天空"可以精确判定；没有 GB 时只能靠 `depth >= far - eps` 近似（远景山脊会一起被当成天空，需要在文档里说明）。
- coverage 纹理（`_HoGeometryBufferCoverageTexture`）可选，`SkyTyndall.shader:392-393` 就是先 `LilHoGeometryBufferCoverage(normalDepth)` 再用。
- 法线可以顺手用来做"只对朝上的面上雾"（地面雾/贴地雾）——`LilHoGeometryBufferWorldNormalOrZero` 现成。

### 1.4 现有 ScreenProcess 效果怎么取深度（可照抄的骨架）

```hlsl
// DepthOfField.shader:95-104 的精简版
float SampleEyeDepth(float2 uv)
{
    if (_HoGeometryBufferValid <= 0.5)
        return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
    half4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
    return LilHoGeometryBufferLinearDepthOrFar(normalDepth, _ProjectionParams.z);
}
```

### 1.5 URP 自带雾（要讲清楚的关系）

- per-object、在 Lit/Unlit 的 fragment 里做：`ShaderLibrary/Lighting.hlsl:212-223`（`FOG_LINEAR`/`FOG_EXP`/`FOG_EXP2`）、`ComputeFogFactorZ0ToFar` → `ShaderVariablesFunctions.hlsl:315-327`，参数是 `unity_FogParams`（`UnityInput.hlsl:57` 一带的全局）。
- 它是**tonemap 之前**、逐物体的：只作用于写深度/带雾 keyword 的物体，天空盒由 skybox shader 自己决定。
- 我们的是**tonemap 之后**、逐像素的：对透明物体、粒子、天空都能统一处理（只要深度对），但不会和 HDR 光照/泛光产生物理正确的相互作用。
- fork 里没有任何 fog RendererFeature（`Ho*Fog`/`HeightFog`/`VolumetricFog` 搜不到），所以不存在和自研雾冲突的问题。

## 2. 高度雾的关键：反投影（不需要 position buffer）

思路：**我们不需要 GB 输出 position，只需要它在每个像素给出"眼深"**，剩下的用相机矩阵还原：

```hlsl
// 1) 眼深（>0，沿相机 -Z 方向的距离），两条路径见 1.4
float eyeDepth = SampleEyeDepth(uv);

// 2) 屏幕 UV -> 视空间方向（透视：远平面点归一化；正交：XY 与深度无关，单独分支）
float3 viewRay = ResolveViewRay(uv);      // 透视: normalize 到 z=-1；正交: (ndc.x/halfW, ndc.y/halfH, -1)

// 3) 视空间位置 -> 世界位置
float3 viewPos  = viewRay * eyeDepth;
float3 worldPos = mul(unity_CameraToWorld, float4(viewPos, 1.0)).xyz;
float  height   = worldPos.y;             // 绝对世界高度（"海平面/地面高度"参考）
```

- 透视分支用 `unity_CameraInvProjection`（`UnityInput.hlsl:100`）把远平面点变到视空间再归一化；也可以直接用相机投影矩阵的两个缩放项：`viewX = ndcX * eyeDepth / unity_CameraProjection[0][0]`、`viewY = ndcY * eyeDepth / unity_CameraProjection[1][1]`（自动带 aspect，`unity_CameraProjection` 在 `UnityInput.hlsl:99`），实现更短。
- 正交分支必须单独处理（正交下 XY 与深度无关）：用 `unity_OrthoParams`（`UnityInput.hlsl:81`）或 `unity_CameraProjection[3][3]` 判断，`viewXY = ndc / m00/m11`、`viewZ = -eyeDepth`。
- **相机相对模式**可以完全跳过 `unity_CameraToWorld`：直接用 `viewPos.y`（相对相机的高度），这也是很多作品里"地平线附近的雾"最省事的做法。

精度与边界：

- GB 眼深是半精度 → 极远处会台阶化（§1.2 的数字）；相机深度路径更稳。可以让用户在需要时切"深度来源"。
- 反投影的正确性**可以在 Unity 之外验证**：在测试工程里造一个合成相机矩阵，把已知世界点投影成 `uv + 眼深`，再跑一遍反投影，检查是否回到原点（见 §5）。这条往返测试能挡住 Y 翻转、reversed-Z、正交分支这类经典坑。
- 天空像素：眼深≈远裁剪面 → 反投影出的"世界点"其实在很远的地方，高度值无意义；所以天空必须走 §3 的"天空处理"分支，不能拿它的高度去算雾密度。

## 3. 效果设计草案

### 3.1 形态：**一个效果 + 模式**（推荐），而不是"深度雾 / 高度雾"两个效果

理由：两者共享颜色、距离模型、天空处理、混合与规则遮罩；分成两个效果会逼用户加两层、还要自己避免重复叠加。业界同族（Unreal `ExponentialHeightFog`、HDRP `Fog`）也都是"一个组件里带高度设置"。高度项做成**可关**的乘子，关掉就是纯深度雾。

- 枚举：`ScreenProcessEffect.DepthFog`，面板名「深度雾」。
- 文档里说明它同时覆盖：大气透视（远色 + 去饱和）、高度雾（贴地/山谷）、以及"只对某类物体上雾"（规则遮罩）。

### 3.2 密度模型

```
深度项（三选一）：
  直线雾   dDepth = saturate((eyeDepth - start) / max(far - start, eps))
  指数雾   dDepth = 1 - exp(-density * max(eyeDepth - start, 0))
  指数平方 dDepth = 1 - exp(-pow(density * max(eyeDepth - start, 0), 2))     // 最"厚"的一种
高度项（可选乘子）：
  h        = exp(-heightFalloff * max(baseHeight - worldY, 0))              // baseHeight 之上不衰减、之下越来越浓
  d        = saturate(dDepth * lerp(1.0, h, heightEnabled)) * maxOpacity
```

- `start`（起始距离）让近处干净；`maxOpacity` 限制最大浓度（动画风常用 ~0.85，避免完全盖掉固有色）。
- 高度项用"从地面向上指数衰减"是最常见的形式；也提供"反向"（越往上越浓，云海/高空雾）。
- 想要"只贴地一层"可以再给一个 `heightBand`（上下限），用平滑窗函数 `smoothstep` 夹出带状雾——先作为可选参数放进去，实施时若参数不够再裁剪。

### 3.3 颜色与大气透视

- `color` = **近色**（雾最薄处的颜色）；`parameters` 里放 **远色**（rgba）。
- 合成：`fogColor = lerp(nearColor, farColor, saturate(d * farColorMix))`，再 `outColor = lerp(src, fogColor, d)`。
- 可选"大气透视"项：`src = lerp(src, saturate(luminance(src)), desaturate * d)`（远处去饱和）+ 轻微蓝移（用一个 0..1 的量把色相往远色偏）。这两项让"远山发灰发蓝"看起来比单纯加雾更像实拍。
- 混合空间是显示空间（§1.1）；颜色按最终画面调，直观但不受 tonemap 影响，文档写清楚。

### 3.4 天空处理（必须有，且要有默认值）

三种模式：**跳过天空**（推荐默认，动画风最常用；有 GB 用 coverage 判定，没有则用远裁剪面近似）、**一起上雾**（统一大气感）、**单独天空色/强度**（天空按自己的颜色和浓度上雾）。

### 3.5 参数草案（24 float + color + texture 刚好够）

| 槽位 | 内容 |
| --- | --- |
| `color` | 近色（HDR Color 字段，实际按显示空间使用） |
| `parameters0` | (深度模式 0=直线/1=指数/2=指数平方, 起始距离, 远距, 密度) |
| `parameters1` | (最大不透明度, 高度项开关, 高度参考模式 0=世界/1=相机相对, 地面高度或相机高度) |
| `parameters2` | (高度衰减, 高度反向 0/1, 高度带上下限压缩, 预留) |
| `parameters3` | (远色混合强度, 去饱和强度, 蓝移强度, 抖动) |
| `parameters4` | 远色 rgba |
| `parameters5` | (天空模式 0/1/2, 天空强度, 天空去饱和, 深度来源覆盖 0=自动/1=强制相机深度) |
| `texture` | 预留（例如以后放"按距离的色带"或用一张噪声做雾的扰动） |
| 规则遮罩 | 复用图层字段（免费得到"角色不受雾""只对背景上雾"） |

抖动：半精度深度 + 大面积平滑渐变容易出现色带，沿用 ImageProcess 里的做法（±0.5/255 三角噪声，单位可调）。

### 3.6 预设草案（每组 1-3 个）

- 大气：`清晨薄雾`、`黄昏尘雾`、`远景空气感`（远色偏蓝 + 去饱和，几乎不改近处）
- 天气：`雨雾`、`浓雾`、`雪雾`
- 风格：`深谷高度雾`（高度项为主）、`云海`（高度反向）、`夜景霓虹雾`（近色暗、远色偏紫，配合规则遮罩只对背景）
- 极端：`沙漠热霾`（远处抖动大、近处几乎无雾）

## 4. 集成清单（改动点，逐条给位置）

运行时（`Runtime/ScreenProcess`）：

1. `ScreenProcessEffect.cs`：追加 `DepthFog`（枚举末尾，保持旧序列化稳定）。
2. `ScreenProcessShaderConstants.cs`：加 `DepthFogShaderName = "Hidden/lilToon/URP/ScreenProcess/DepthFog"`。
3. `ScreenProcessEffectRegistry.cs`：`case ScreenProcessEffect.DepthFog` 返回上面那个 shader。
4. `ScreenProcessRendererFeature.cs`：
   - `RequiresDepth()`（`:1166-1184`）加入 `DepthFog`（这样 URP 会准备相机深度，兜底路径才有输入）；
   - `passData.useRuleNormalDepth`（`:851`）的条件里加入 `DepthFog`（这样 GB 的 normal/depth 会被绑成全局，`_HoGeometryBufferValid=1`）；
   - 若要用天空色/天空遮罩：参考 `:994-1006` 的 SkyTyndall 分支，加一个 `useSkyTexture` 的同类条件（**先不做**，天空用 coverage/远裁剪面判断即可）；
   - 兼容（非 RenderGraph）路径的同一处（`:435-450` 一带的兜底绑定）也确认覆盖到。
5. 新 shader：`Runtime/ScreenProcess/Shaders/ScreenProcess/DepthFog.shader`（单 pass，采样深度 + 反投影 + 合成），include `HoGeometryBufferSampling.hlsl`。
6. 图层默认值/预设：`ScreenProcessStackVolumeEditor.cs` 的默认值 switch（`:476-489` 一带）加 `DepthFog` 的初值；`ScreenProcessStackVolumeEditor.Presets.cs` 加预设菜单。

编辑器：

7. `Editor/PostProcessing/ScreenProcess/ScreenProcessStackVolumeEditor.cs`：`EffectToggleEntry` 调色板（`:35-41`）加图标项、`GetElementLineCount`（`:186-203`）加行数分支（**必须与绘制行数一致**，可以用刚加的 `ui_rows_check.js` 思路固化成检查）。
8. `Editor/PostProcessing/ScreenProcess/Filters/DepthFog.cs`：新增参数 UI（复用 `DrawLayerCoreFields`/`DrawBlendModeLine`/`DrawPropertyLine` 那套约定）。
9. 图标：从 `Editor/ImageProcessIcons` 里挑一个未用的（例如 `icon_Weather_v1` 已给天气用，可以新画或在既有里挑）。

文档：

10. 新增 `Documentation~/PostProcessing/DepthFog.md`（由本文档升级而来）、`README.md` 的 ScreenProcess 段落与"新增效果接入规则"补充、`GradientInvestigation.md` 第 6 节把 C 标记为已落地。

## 5. 验证计划（沿用 GradientMap 那一套）

1. **C# 纯函数核心 + 可执行测试**：把密度/高度/合成数学写成一个不依赖 UnityEngine 的纯核心（例如 `ScreenProcessFogMath.cs`：`ComputeDepthDensity(mode, depth, start, far, density)`、`ComputeHeightFactor(...)`、`ComposeFogColor(...)`），再用 `.codex-research/` 下的 dotnet 工程（照 `gradient_map_sim/bake_core_check` 的样子）链接出货源文件跑检查：
   - 三种深度模式在参考点上的解析值一致性与单调性；
   - 高度因子：`baseHeight` 之上为 1、之下指数衰减、`反向` 行为、`maxOpacity` 夹取；
   - **反投影往返**：合成一个透视相机矩阵 → 世界点 → 投影成 (uv, 眼深) → 跑反投影 → 与原点比较（正交相机再跑一遍）；
   - 负对照：把矩阵某个元素或某个系数改一点，往返/单调性检查必须 FAIL。
2. **shader 结构 / 编译**：`.codex-research/shader-check/` 的 `extract.js` 现在会内联包内 include，再补上相机矩阵与深度宏的 stub，就能用 `ps_5_0` 编译 `FragDepthFog`（含负对照用法）。
3. **文档图**：在 JS/Python 里合成"地面 + 远山 + 天空"的深度图与颜色图，按 §3 的公式渲染几组预设 → `Images/DepthFog/`（和 GradientMap 的 sheet 同一套路）。
4. **UI 行数检查**：把 `ui_rows_check.js` 泛化成"任意效果的常量/分支 == 实际绘制行数"，避免层列表高度错位的经典 bug。
5. **Roslyn**：`check_compile.ps1` 保持 0 error。
6. **实机清单**（写进文档）：深度来源两条路径的画面差异、天空三种模式、正交相机、规则遮罩把角色排除、URP 自带雾同时开启时的叠加效果、半精度深度在远裁剪面下的台阶。

## 6. 取舍与待定问题

### 6.1 取舍

- **tonemap 之后**（现状）：颜色直观、能统一处理透明/天空/粒子；但雾不参与 bloom，也不会被 tonemap 压肩，强光场景里"远处的太阳附近发白"这类效果做不出来。要做 tonemap 之前的雾，需要给 ScreenProcess 增加一个 `BeforeRenderingPostProcessing` 注入点（或单独 feature）——那是另一个决定，本文按"后置雾"设计。
- **屏幕空间**：雾只按深度上色，不能做"光柱/上帝光"（那是 SkyTyndall 的职责），也不做体积自阴影。
- **半精度 GB 深度**：极远距离有台阶；提供"强制相机深度"作为补救。

### 6.2 需要你定的

1. **一个效果（推荐）还是"深度雾 / 高度雾"两个效果？**
2. **高度参考**：世界绝对高度（需要 `地面高度` 参数，跨场景要调）/ 相机相对高度（省矩阵、跟着相机走）/ 两者都提供（推荐）。
3. **天空默认**：跳过（推荐）/ 一起上雾 / 天空单独色。
4. **是否现在就做"远色 + 大气透视（去饱和/蓝移）"**：参数预算够（推荐做，这是"像实拍"的关键一半）。
5. **是否需要 pre-tonemap 注入点**（默认不做）。

## 7. 分阶段实施建议

| 阶段 | 内容 | 产出 |
| --- | --- | --- |
| 1 | 深度雾最小可用：深度项（三种模式）+ 近色 + 天空跳过 + 强度/混合/规则遮罩 | 可看画面的第一版 + 密度数学的 C# 检查 + HLSL 编译检查 |
| 2 | 高度项：反投影 + 世界/相机参考 + 高度衰减/反向 + 往返测试 | 高度雾可用，含正交相机分支 |
| 3 | 远色/大气透视 + 抖动 + 预设库 + 文档图 | 完整效果 + `DepthFog.md` |
| 4（可选） | 需要精确世界坐标/体积感时：给 GeometryBuffer 增加可选 position RT，或做 raymarch 版体积雾 | 另立方案 |

每个阶段都以"能失败的自检 + 文档 + 提交"收尾（和 Gradient、GradientMap 的做法一致）。

# 深度雾 / 高度雾（ScreenProcess）设计方案

> **状态：设计已定稿（§6.2 的分叉都已确认），尚未实现。** 本文只写"要做什么、为什么可行、怎么落地、怎么验证"，不含已落地代码。
> 相关背景：家族 C（深度/大气/天空遮罩驱动的染色）的划分见 `GradientInvestigation.md` 第 6 节；本效果是那一支里最常用的一个。

## 0. 结论速览

| 问题 | 结论 | 依据 |
| --- | --- | --- |
| 深度雾能不能做 | 能，而且**不依赖 GeometryBuffer**：只用 URP 的相机深度纹理也能做 | `ScreenProcess/Shaders/ScreenProcess/DepthOfField.shader:95-104` 已经写了"GB 优先、否则 `LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams)`"的双路径 |
| 高度雾要不要 position buffer | **不需要**。用"线性眼深 + 屏幕 UV + `unity_CameraInvProjection` / `unity_CameraToWorld`"反投影即可拿到世界坐标 | 深度已可拿到（上一行）；矩阵在 `HoUrp17.3.0/ShaderLibrary/UnityInput.hlsl:100`(`unity_CameraInvProjection`)、`:102`(`unity_CameraToWorld`)、`:234`(`unity_MatrixInvVP`) |
| 天空怎么分辨 | 有 GB 时用 coverage（天空 `a≈0`）得到干净遮罩；没有 GB 时用"深度≈远裁剪面"近似 | `Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl:12-15`、`SkyTyndall.shader:392-393` |
| 做不了什么 | 真实体积散射（需要 raymarch + 光锥/阴影）、按物体高度着色（需要精确高度场或 position RT） | 体积雾需要逐像素积分光源可见性，本包 ScreenProcess 没有光源/阴影资源；position RT 见 §6 阶段 4 |
| 和 URP 自带雾的关系 | 并存但会**双重上雾**：引擎雾是 per-object、tonemap 之前（`Lighting.hlsl:206-240`；延迟走全屏 `Utils/FogDeferred.hlsl`，正交下被禁用），距离量只有 view Z。用本效果时要把 Render Settings 的 Fog 关掉（或对齐它的 `max(viewZ - near, 0)` 约定）。**天空是它的盲区**，也是我们后置雾的增量 | §1.5 |

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

fork 身份核对：`HoUrp17.3.0/package.json` = `com.unity.render-pipelines.universal` **17.3.0**，git log 只有一个导入提交加 MSAA / SceneView AA / render-pass 顺序的改动 → **雾相关代码是 stock URP 17.3**。

- 距离量只有 **view Z**：`ShaderVariablesFunctions.hlsl:315-347`（`saturate(z * unity_FogParams.z + unity_FogParams.w)`，exp/exp2 走 `unity_FogParams.x * z`）、`:355-450`（`ComputeFogIntensity`）、`:452-521`（`MixFogColor` / `MixFog`）；fragment 距离是 `max(viewZ - _ProjectionParams.y, 0)`（`:411-414`）。
- 生效位置：前向是 per-object（`Lighting.hlsl:206-240`、`LitForwardPass.hlsl:109-112`）；**延迟是一条全屏 pass**（`Utils/FogDeferred.hlsl:1-49`，由 `Runtime/DeferredLights.cs:1134-1151` 驱动），并且**正交相机下这条被禁用**（`DeferredLights.cs:1136-1139`）。参数由引擎写 `unity_FogParams` / `unity_FogColor`（`UnityInput.hlsl:225-226`），keyword 是 `multi_compile_fog`（`ShaderLibrary/Fog.hlsl:5-9`；`USE_DYNAMIC_BRANCH_FOG_KEYWORD (0)` 见 `HoUrpConfig17.0.3/Runtime/ShaderConfig.cs.hlsl:13`）。
- **双重上雾的风险**：引擎雾在物体着色时就烤进去了，发生在我们的后置 pass **之前**，我们无法"去掉"它。所以文档必须写：用本效果时把 Render Settings 的 Fog 关掉，或者让距离量对齐它的约定（`max(viewZ - near, 0)`）。
- **天空是引擎雾的盲区**（skybox 不受 RenderSettings 雾影响）→ 这正是后置雾的增量价值：能把"远处几何 + 天空"放到同一条大气曲线上（默认仍是"跳过天空"，见 §3.4）。
- fork 里没有任何 fog RendererFeature / 体积雾：`Ho*` 命名的文件搜不到，`Volumetric*` 命中的全是 2D `Light2D` 的 volumetric shadow（`Runtime/2D/...`）。唯一痕迹是默认 Volume Profile 里一个**悬空**的 `OasisFogVolumeComponent`（`Editor/Volume/DefaultVolumeProfile.asset:255-282`：`Density`/`StartDistance`/`HeightRange{0,50}`/`Tint`/`SunScatteringIntensity`），其脚本 GUID 在整个 fork 里不存在 → 没有运行时类，既不会冲突也不可依赖，但可以当参数命名的参考。

### 1.6 参考实现与"不复制"纪律

| 实现 | 有什么 | 能不能借 |
| --- | --- | --- |
| PPv2 `Deferred Fog`（`ReferencePackages~/UnityPostProcessingV2`，3.0.2） | 最小/最大深度、`Shaders/Builtins/Fog.hlsl:4-30` 的 linear/exp/exp2、`DeferredFog.shader:18-23` 合成；**只有两个 bool**（`Runtime/Effects/Fog.cs:16-17,22-23`），颜色/密度/起止全来自 `RenderSettings`（`:45-47`）；**没有高度项**；天空用硬编码 `depth < 0.9999` + 第二 pass（`DeferredFog.shader:12,26-37`） | 只借参数形状；**代码是 Unity Companion License（`LICENSE.md:1-5`），不复制** |
| Standard Assets `GlobalFog`（`ReferencePackages~/…NatureStarterKit/…/ImageEffects`） | **唯一的现成高度雾**：Lengyel 解析半空间积分（`Shaders/GlobalFog.shader:101-119`），一个雾顶高度 + `heightDensity`，没有 start/end、没有衰减曲线；`useRadialDistance` 决定距离按视线欧氏距离还是 view Z（`GlobalFog.shader:87-99`）；天空用 `rawDepth == _DistanceParams.y` 精确判定（`:141-143`）；用"逐物体梯形顶点"塞视锥角（`Scripts/GlobalFog.cs:57-85`） | **该目录里没有 licence 文件**，所以代码一行都不抄；只采用公开文献里的解析雾数学（Lengyel 的 analytic fog / 指数高度雾积分），自己实现并自己验证 |
| X-PostProcessing | 有可复用的 `ReconstructViewPos`（`XPL/Shaders/XPostProcessing.hlsl:268-281`），但整套 XPL 里**没有任何采样深度纹理的效果** | 共享 shader 是 Unity Companion License（`XPL/Shaders/License.txt`），不复制 |
| Kino / RetroLookPro / Shoost | 没有任何雾 | — |

结论：**没有可以直接搬的雾实现**；参数模型参考 PPv2（最小参数）与 `GlobalFog`（高度项 + 距离量选项），数学按公开的解析雾公式自己写，来源与许可以上表为准。

### 1.7 包内已有的先例（这条最重要：高度雾在本包里已经是解决过的形状）

- **高度窗已经有人做过**：角色特化的发梢/身体高度渐隐 `Runtime/CharacterSpecialization/Shaders/HoCharacterSpecializationComposite.shader:487-521` 就是"`ComputeWorldSpacePosition` 得到世界坐标 → 取 world Y → smoothstep 窗 + hardness 重映射"，参数形状是 `(mode, groundY, start, 1/(end-start))` + hardness（`HoCharacterSpecializationSettings.cs:37-45,385-404`，打包在 `Effects/HoCharacterSpecializationPass.SubjectOutline.cs:98-109`）。深度雾的高度项**直接对齐这套参数形状与数学**，能省掉一轮设计。
- **反投影的模板**：`HoUrp17.3.0/Shaders/Utils/ScreenSpaceShadows.shader:23-29` 就是 `unity_MatrixInvVP` + `ComputeWorldSpacePosition` + `LoadSceneDepth` 的可用写法；`CameraMotionBlur.shader:8-11,77`、`CameraMotionVectors.shader:20,78` 也是同类。
- **深度→设备深度的转换**：`Runtime/GTAO/Shaders/HoGTAOCommon.hlsl:19`（linear → device depth），GB 眼深要喂给 `ComputeWorldSpacePosition` 时需要它。
- **天空/远平面测试**：`Runtime/GTAO/Shaders/HoGTAO.shader:116-118`（远平面/sky 判定）、`HoGeometryBufferSkyPass.cs:11-56`（sky = 1 − coverage）、`SkyTyndall.shader:203-219,392-407`（天空门控）。

## 2. 高度雾的关键：反投影（不需要 position buffer）

思路：**我们不需要 GB 输出 position，只需要它在每个像素给出"眼深"**，剩下的用相机矩阵还原：

```hlsl
// 首选路径（core/ShaderLibrary/Common.hlsl:1390-1401，写法模板见 ScreenSpaceShadows.shader:23-29）
//   deviceDepth = 原始深度缓冲值（SampleSceneDepth 直接给）；ndc = uv * 2 - 1
float3 worldPos = ComputeWorldSpacePosition(ndc, deviceDepth, UNITY_MATRIX_I_VP);
float  height   = worldPos.y;   // 绝对世界高度（"地面/海平面"参考）

// 只有 GB 眼深时：先 linear -> device（模板 GTAO/Shaders/HoGTAOCommon.hlsl:19），再喂给同一个函数，
// 这样两条深度来源共用同一条反投影代码路径。

// 交叉验证用的手写版本（§5.1 会拿它和上面比）
float3 viewRay  = normalize(mul(unity_CameraInvProjection, float4(ndc, 1, 1)).xyz);
float3 viewPos  = viewRay * eyeDepth;                                   // 眼深沿 -Z
float3 worldPos2 = mul(unity_CameraToWorld, float4(viewPos, 1.0)).xyz;
```

- `UNITY_MATRIX_I_VP`（`UnityInput.hlsl:234` 的 `unity_MatrixInvVP`，别名见 `Input.hlsl:216`）与 `unity_CameraToWorld`（`:102`）都可用，但**它们声明在 URP 的 `UnityInput.hlsl` 里，core 的库并不声明**——所以 shader 必须 include URP 的 `Core.hlsl`（本包 ScreenProcess 现有 shader 都这么做）。
- 用 `ComputeWorldSpacePosition` 的好处是**正交相机自动正确**（通用逆 VP 变换）；改用手写"视线 × 眼深"时正交必须单独分支，而且 `LinearEyeDepth` 本身**不支持正交**（`core/Common.hlsl:1205`），URP 的延迟雾也是直接跳过正交（`DeferredLights.cs:1136-1139`）。
- **相机相对模式**不需要世界矩阵：直接用视空间的 `viewPos.y` 与 `-viewPos.z`；省事但做不了固定海拔的雾层。
- **距离量也要可选**：引擎雾和 `GlobalFog` 都区分"view Z"与"视线欧氏距离"（后者即 `useRadialDistance`），我们用同一个开关，默认对齐引擎 = view Z。

精度与边界：

- GB 眼深是半精度 → 极远处会台阶化（§1.2 的数字）；相机深度路径更稳。可以让用户在需要时切"深度来源"。
- 反投影的正确性**可以在 Unity 之外验证**：合成一个相机矩阵，把已知世界点投影成 `(uv, 设备深度)`，再跑反投影，检查是否回到原点（§5.1）；透视与正交各跑一遍。这条往返测试能挡住 Y 翻转、reversed-Z、正交分支这类只在画面上很难看出来的坑。
- 天空像素：深度≈远裁剪面 → 反投影出的"世界点"在很远的地方，高度值无意义；所以天空必须走 §3.4 的"天空处理"分支，不能拿它的高度算雾密度。

## 3. 效果设计草案

### 3.1 形态：**一个效果 + 模式**（推荐），而不是"深度雾 / 高度雾"两个效果

理由：两者共享颜色、距离模型、天空处理、混合与规则遮罩；分成两个效果会逼用户加两层、还要自己避免重复叠加。业界同族（Unreal `ExponentialHeightFog`、HDRP `Fog`）也都是"一个组件里带高度设置"。高度项做成**可关**的乘子，关掉就是纯深度雾。

- 枚举：`ScreenProcessEffect.DepthFog`，面板名「深度雾」。
- 文档里说明它同时覆盖：大气透视（远色 + 去饱和）、高度雾（贴地/山谷）、以及"只对某类物体上雾"（规则遮罩）。

### 3.2 密度模型

```
距离量（可选）：dist = viewZ        （默认，对齐引擎雾的 max(viewZ - near, 0)）
              或 dist = length(viewPos)（视线欧氏距离，GlobalFog 的 useRadialDistance）
深度项（三选一）：
  直线雾   dDepth = saturate((dist - start) / max(far - start, eps))
  指数雾   dDepth = 1 - exp(-density * max(dist - start, 0))
  指数平方 dDepth = 1 - exp(-pow(density * max(dist - start, 0), 2))     // 最"厚"的一种
高度项（可选乘子，参数形状对齐包内既有先例，见 §1.7）：
  窗模式   h = 1 - smoothstep(startY, endY, worldY)              // 世界 Y 上的高度窗 + hardness 重映射
  衰减模式 h = exp(-heightFalloff * max(baseHeight - worldY, 0))  // 地面之上不衰减、之下越深越浓
  d        = saturate(dDepth * lerp(1.0, h, heightEnabled)) * maxOpacity
```

- `start`（起始距离）让近处干净；`maxOpacity` 限制最大浓度（动画风常用 ~0.85，避免完全盖掉固有色）。
- 高度项提供**两种形态**：`窗模式`（start/end + hardness，和 `HoCharacterSpecializationComposite.shader:487-521` 的发梢高度渐隐同一套参数与数学，用户已经熟悉）与 `衰减模式`（groundY + falloff，更接近 Unreal 的指数高度雾）。两者共用同一份 world Y。
- 高度项也要有"反向"（越往上越浓，云海/高空雾）与"只贴地一层"（窗模式的 start/end 天然就是带状）。

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
| `parameters1` | (最大不透明度, 距离量 0=viewZ/1=欧氏, 起始距离偏移或起始距离, 预留) |
| `parameters2` | (高度项开关, 高度形态 0=窗/1=衰减, 高度参考 0=世界/1=相机相对, 高度反向) |
| `parameters3` | (高度 startY 或 groundY, 高度 endY 或 1/falloff, 高度 hardness, 预留) |
| `parameters4` | (远色混合强度, 去饱和强度, 蓝移强度, 抖动) |
| `parameters5` | (天空模式 0/1/2, 天空强度, 深度来源覆盖 0=自动/1=强制相机深度, 预留) |
| 远色 | 需要一个 rgba：可以放 `texture` 之外的空位，或与 `parameters3.w` 组合（实施时按实际占用再定） |
| `texture` | 预留（例如以后放"按距离的色带"或用一张噪声做雾的扰动） |
| 规则遮罩 | 复用图层字段（免费得到"角色不受雾""只对背景上雾"） |

> 6 个 Vector4 只够"刚好"：远色的 4 个分量需要在实施时和 `parameters` 的空位一起排（例如把 `parameters3.w`、`parameters5.w` 和 `parameters1.w` 拼成第二个颜色，或者把天空项并进 `parameters5` 后腾出一整个 `parameters4`）。这一条在阶段 1 落地时确定，并写进参数表注释。

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

1. **C# 纯函数核心 + 可执行测试**：把密度/高度/合成数学写成一个不依赖 UnityEngine 的纯核心（例如 `ScreenProcessFogMath.cs`：`ComputeDepthDensity(mode, distance, start, far, density)`、`ComputeHeightFactor(mode, worldY, a, b, hardness)`、`ComposeFog(...)`），再用 `.codex-research/` 下的 dotnet 工程（照 `gradient_map_sim/bake_core_check` 的样子）链接出货源文件跑检查：
   - 三种深度模式在参考点上的解析值与单调性；`viewZ` 模式与引擎雾 `max(viewZ - near, 0)` 的距离约定一致性；
   - 高度因子：窗模式与 hardness 重映射（和 `HoCharacterSpecializationComposite.shader:487-521` 同形）、衰减模式的解析值、`反向`、`maxOpacity` 夹取；
   - **反投影往返**：合成透视相机矩阵 → 世界点 → 投影成 `(uv, 设备深度)` → 跑反投影 → 与原点比较；正交相机再跑一遍；**并且交叉验证 `ComputeWorldSpacePosition` 语义的手写实现（视线 × 眼深）与它给出一致结果**（两侧都写在测试里，公式独立）；
   - 负对照：把矩阵某个元素或某个系数改一点，往返/单调性检查必须 FAIL。
2. **shader 结构 / 编译**：`.codex-research/shader-check/` 的 `extract.js` 现在会内联包内 include，再补上相机矩阵（`UNITY_MATRIX_I_VP`、`unity_CameraToWorld`）、`ComputeWorldSpacePosition`、`SampleSceneDepth` 与深度宏的 stub，就能用 `ps_5_0` 编译 `FragDepthFog`（含负对照用法）。
3. **文档图**：在 JS/Python 里合成"地面 + 远山 + 天空"的深度图与颜色图，按 §3 的公式渲染几组预设 → `Images/DepthFog/`（和 GradientMap 的 sheet 同一套路）。
4. **UI 行数检查**：把 `ui_rows_check.js` 泛化成"任意效果的常量/分支 == 实际绘制行数"，避免层列表高度错位的经典 bug。
5. **Roslyn**：`check_compile.ps1` 保持 0 error。
6. **实机清单**（写进文档）：深度来源两条路径的画面差异、天空三种模式、正交相机（URP 延迟雾自己都跳过正交，要把我们的行为测清楚）、规则遮罩把角色排除、**渲染设置里 URP 自带雾同时开启时的双重上雾**、半精度深度在远裁剪面下的台阶、`Fixed`/窗模式下高度阶跃是否可见。

## 6. 取舍、已定决策与风险

### 6.1 取舍

- **tonemap 之后**（现状）：颜色直观、能统一处理透明/天空/粒子；但雾不参与 bloom，也不会被 tonemap 压肩，强光场景里"远处的太阳附近发白"这类效果做不出来。要做 tonemap 之前的雾，需要给 ScreenProcess 增加一个 `BeforeRenderingPostProcessing` 注入点（或单独 feature）——那是另一个决定，本文按"后置雾"设计。
- **屏幕空间**：雾只按深度上色，不能做"光柱/上帝光"（那是 SkyTyndall 的职责），也不做体积自阴影。
- **半精度 GB 深度**：极远距离有台阶；提供"强制相机深度"作为补救。

### 6.2 已定决策

1. **形态**：一个效果 `ScreenProcessEffect.DepthFog`「深度雾」+ 模式；高度项是可关的乘子（不做成两个效果）。
2. **高度参考**：**世界绝对高度（默认）与相机相对高度都提供**，共用同一段反投影代码；世界模式配一个可调的「地面高度」。
3. **合成时机**：接受**后置（tonemap 之后、显示空间）**合成，不新增 `BeforeRenderingPostProcessing` 注入点。
4. **天空默认**：跳过天空（有 GB 用 coverage 判定，没有则用远裁剪面近似），另提供"一起上雾"和"天空单独色"两种模式。
5. **远色 + 大气透视（去饱和/蓝移）**：做，参数预算够（`parameters3`/`parameters4`）；抖动一起做。
6. **暂不做**：pre-tonemap 注入点、GeometryBuffer 的 position RT、体积 raymarch（见 §7 阶段 4）。

### 6.3 仍然存在的风险与实现陷阱

- 半精度 GB 深度在极远距离会台阶化（§1.2），需要实机确认"远裁剪面 1000+ 时高度雾是否出现台阶"。
- 高度雾依赖相机矩阵与眼深的约定（reversed-Z、Y 翻转、正交分支）——**必须靠 §5.1 的反投影往返测试**把关，而不是靠肉眼看画面。
- 天空在"没有 GB"时只能靠远裁剪面近似，远景山脊可能被误判成天空；文档要写明"想要精确天空遮罩就启用 GeometryBuffer"。
- **双重上雾**：引擎雾在物体着色阶段就烤进颜色了（§1.5），用本效果时必须在文档与预设说明里写"把 Render Settings 的 Fog 关掉"，否则远景色会被上两次。
- **矩阵声明位置**：`unity_MatrixInvVP` 等只在 URP 的 `UnityInput.hlsl` 里声明，core 的库不声明。新 shader 必须 include URP 的 `Core.hlsl`，否则会在 D3D 上编译失败（我们的 HLSL 检查脚本正好能挡住这类问题）。
- **许可纪律**：PPv2 的雾是 Unity Companion License、XPL 共享 shader 同为 Companion License、Standard Assets 那份 `GlobalFog` 目录里根本没有 licence 文件——三者都**不复制代码**，只参考参数形状与公开文献里的解析雾数学（见 §1.6）。

## 7. 分阶段实施建议

| 阶段 | 内容 | 产出 |
| --- | --- | --- |
| 1 | 深度雾最小可用：深度项（三种模式）+ 近色 + 天空跳过 + 强度/混合/规则遮罩 | 可看画面的第一版 + 密度数学的 C# 检查 + HLSL 编译检查 |
| 2 | 高度项：反投影 + 世界/相机参考 + 高度衰减/反向 + 往返测试 | 高度雾可用，含正交相机分支 |
| 3 | 远色/大气透视 + 抖动 + 预设库 + 文档图 | 完整效果 + `DepthFog.md` |
| 4（可选） | 需要精确世界坐标/体积感时：给 GeometryBuffer 增加可选 position RT，或做 raymarch 版体积雾 | 另立方案 |

每个阶段都以"能失败的自检 + 文档 + 提交"收尾（和 Gradient、GradientMap 的做法一致）。

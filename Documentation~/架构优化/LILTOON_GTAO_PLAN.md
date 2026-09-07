# Ho-GTAO 独立规划（v1 定稿）

> 状态：**规划定稿**（不动代码；实现按 §5 步骤推进）。
> 实现进度：① lilToon 语义 ✅（7a652e5）② 骨架 ✅（4371c43）③ march ✅（e5b8a26）④ 滤波+上采样 ✅（9343e87）⑤ temporal ✅（b40d9aa）⑥ Volume+DebugTile+Editor ✅（d352e6e）→ ⑦ 收尾：Unity 侧（refresh shaders / GeometryBuffer passEvent→250 / PC_Renderer 挂 Ho-GTAO 并移除 HTrace AO feature）；代码侧 grep 已清零（lilToon + 扩展包 `_HTraceBufferAO`/HTraceAO 0 命中）。
> 依据：契约 v1（`ao` 通道：R8f 0..1，生产端=自研 AO，消费端=材质采样 + AOV）；草案 §5 替换位。
> 关联：`LILTOON_CHANNEL_CONTRACT_V1.md`（冻结）、`LILTOON_FORMAL_PIPELINE_DRAFT.md` §5/§2（帧序）。
> 结论先行：**v1 用「材质采样模式」+ 公共 AO 语义**。lilToon 侧删除 `_ScreenSpaceAOSource` 0/1 分支与 URP 内置 fallback，**语义上直接采样公共纹理 `_HoAOTexture`**（与 `_HoGeometryBuffer*`/`_HoMetadataBuffer*` 公共资源命名一致，不含算法名）；自研 Ho-GTAO 只替换生产端。核心时序改动 = **GeometryBuffer 提前到 BeforeRenderingOpaques（250）**，让 AO 纹理在 opaque 材质绘制前就绪（与 Ho-GTAO 同事件，靠列表顺序）。

---

## 0. 目标

- 用独立 `Ho-GTAO` RendererFeature 替换 HTrace AO 生产端；材质（`_UseScreenSpaceAO`/`_SSAO*`）与场景零改动。
- 输出全局纹理 **`_HoAOTexture`**（R8，0..1 visibility），lilToon 材质 shade-time 采样、AOV `ao` 通道导出、DebugTile 直出。
- 产出可回退：关闭 feature = 无 AO 且不崩；任何时候可切回 HTrace 直至替换验收通过。

---

## 1. 接缝事实（已核实，代码为证）

### 1.1 材质侧（lilToon，本 fork）

- `Assets/lilToon/Shader/Includes/lil_common_frag.hlsl` **1189-1214** `lilSampleScreenSpaceAO`：
  - `_ScreenSpaceAOSource == 1` → 采 `_HTraceBufferAO`（`lil_sampler_linear_clamp`，值=0..1 可见度因子，1=无遮挡）；`directAO = lerp(1,h, _SSAODirectStrength)`、`indirectAO = lerp(1,h, _SSAOIndirectStrength)`，`ao = min(...)`。
  - **1200-1207 else 分支 = URP 内置 fallback**：`#if defined(_SCREEN_SPACE_OCCLUSION)` → `GetScreenSpaceAmbientOcclusion(screenUV)`（读 `_ScreenSpaceOcclusionTexture`，配合 `_AmbientOcclusionParam`）——**本规划要删除的分支**。
  - `_SSAORemap`（min/max 归一）→ `_SSAOContrast`（幂曲线）→ 0..1 因子。
- **1216-1228** `lilScreenSpaceAO`：`_UseScreenSpaceAO` 门控 → `fd.col.rgb *= lerp(1.0, ao, _SSAOStrength * aoMask)`（`aoMask` 来自 `_SSAOMask` 纹理，`LIL_FEATURE_SSAOMask`）。
- **调用位置**：`lil_pass_forward_normal.hlsl` 439-442，主光+2nd/3rd 层贴图混合**之后**、SSS **之前**（`BEFORE_SSAO`/`OVERRIDE_SSAO`）。
- 结论①：AO 是**光照后**对最终色的单因子乘法，**不是逐光遮挡**——生产端只输出单张 0..1 因子即可。
- 结论②：lilToon **不消费** `_AmbientOcclusionParam`（`_ScreenSpaceAOSource==1` 分支只用 `_HTraceBufferAO`）——HTrace 喂给 URP 的那套全局只影响 URP 内置 shader，与本栈无关。
- 声明：`lil_common_input.hlsl` 830 行 `TEXTURE2D(_HTraceBufferAO)`（**无条件声明**，在 LIL_INPUT_BASE 保护块之外；全 lilToon 仅此一处）。**`lil_common_input_opt.hlsl` / `_base` 文件只替换 CBUFFER 标量段，无纹理声明；且 opt 文件在整个仓库无 include 引用（剥离遗留）**——改动只需 input + frag 两处，无需第三处。

### 1.2 HTrace AO 生产端（现状，替换对象）

- `Assets/HTraceAO`（工程内第三方包）：compute 重核 `HRenderGTAO / HTemporalFilterGTAO / HSpatialFilterGTAO / HDepthPyramidAO` + 深度金字塔 + 法线重建（`ReconstructNormals`）+ 历史缓冲（velocity/samplecount/filter depth）+ temporal/spatial denoise。
- **为什么 HTrace 要 BeforeOpaque 注入（兼容妥协）**：它不信任任何工程已有的 depth/normal（URP 前向管线"有没有 depth prepass / normal 谁写"取决于工程配置），所以**自建 PrePass 重渲深度、再用深度金字塔重建法线**，然后才做 GTAO、把结果在 opaque 材质绘制前就绪。代价：① 场景多画一遍深度；② 重建法线 ≠ 真实渲染法线（toon 材质带法线贴图时两者不一致 → AO 错位/网格感的来源）；③ 与我们的 ScreenGeometryBuffer 两套深度/法线语义并存。
- **我们不需要这个妥协**：管线已有 ScreenGeometryBuffer（真实表面法线=含法线贴图 + 线性深度），GTAO 直接消费，即"强硬用共用的东西"，同时消除 ②③ 两类问题。
- 注入点（profile `GeneralSettings.InjectionPoint`）：
  - **BeforeOpaque**（本基线场景，实际事件 = `BeforeRenderingGbuffer`）：PrePass → 金字塔 → GTAO → 写 `_HTraceBufferAO` 全局 → 材质绘制时采样。
  - **AfterOpaque**：输出合成乘入 camera color（OutputComposition），且 **`_HTraceBufferAO` 强制改设为 white**（GTAOPassURP.cs 140-141）——即材料采样路径失效。
- `HTraceAORendererFeature` 强制 `Shader.EnableKeyword(_SCREEN_SPACE_OCCLUSION)`（仅影响 URP 内置路径）。
- **数值语义（已核实）**：`_HTraceBufferAO.r` = 0..1 可见度（1=无遮挡）；`g_HIntensityAO = Intensity*1.2` 在 OutputComposition 处 `pow` 折入（**材质侧读的是 pow 之前的原始值**）——即 HTrace 的"强度"本来就不喂材质，与"生产端不烘焙强度"的契约一致。
- 基线 profile 实际参数（朱木古堂）：`Mode=GTAO, Intensity=3.06, GTAOWorldSpaceRadius=5, DirectLightingOcclusion=1, GTAOThickness=0.2, ScreenSpaceRadius=25, Slice=2, Step=16, Denoising=3(枚举编号, 实现时对照 GTAODenoisingMode), Temporal=8, FilterRadius=0.5, Adaptivity=0.5`（其余默认）。

### 1.3 管线侧（lilToon-URP-Extensions 实机顺序）

`PC_Renderer.asset` 实测（GUID→类已解析，`passEvent` 均为 URP17 枚举值）：

| 值 | 枚举 | 类 |
| --- | --- | --- |
| 50 | BeforeRenderingShadows | Ho-ShadowCast |
| 150 | BeforeRenderingPrePasses | Ho-ShadowCast（atlas 相关第二档） |
| 250 | BeforeRenderingOpaques | Ho-SSS（一档，用途实现时确认） |
| 300 | **AfterRenderingOpaques** | **Ho-GeometryBuffer**、Ho-MetadataBuffer |
| 400 | AfterRenderingSkybox | Ho-SSS（另一档，用途实现时确认） |
| 450 | BeforeRenderingTransparents | Ho-PlanarReflection、OIT |
| 500 | AfterRenderingTransparents | Ho-CharacterSpecialization |
| 600 | AfterRenderingPostProcessing | Ho-DebugTile |

- **GeometryBuffer/MetadataBuffer 当前在 opaque 之后（300）**——这是本规划的核心矛盾：材质采样模式要求 AO **在 opaque 绘制前**存在。
- GeometryBuffer 输出：`_HoGeometryBufferNormalDepthTexture`（**偏好 R16G16B16A16_SFloat**，rgb=法线 `*0.5+0.5`（非 oct）、a=**线性深度**（LinearEyeDepth）；sky/无覆盖 → a=0）+ `_HoGeometryBufferDepthTexture`（独立深度，用相机 depthStencilFormat，FilterMode.Point）+ `_HoGeometryBufferSkyTexture`（可选）。pass 为 `ShaderTagId("HoGeometryBuffer")` 重画 opaque 几何（lilToon 每个 URP 变体都带该 LightMode pass；其他材质走 fallback shader `Hidden/lilToon/URP/GeometryBuffer/Fallback`）。
- `HoGeometryBufferSettings.passEvent` **可配置**（默认 300，见 `HoGeometryBufferSettings.cs:24`）；`SetGlobalTextureAfterPass` 写全局（pass 内 199-200 行）；`beginCameraRendering` 时 `ResetGlobalState`（设 black 兜底）；RG 资源 = `HoGeometryBufferRenderGraphResources : ContextItem`，每帧 `frameData.GetOrCreate<...>()` 声明。
- 采样 API（AO shader 直接复用）：`Shaders/HoGeometryBufferSampling.hlsl` —— `LilHoGeometryBufferWorldNormalOrZero(half4)`、`LilHoGeometryBufferLinearDepthOrFar(half4, farDepth)`、`LilHoGeometryBufferCoverage()`（`step(eps, a)`）。
- 单 asmdef（`jp.lilxyzw.liltoon.urp.extensions.Runtime.asmdef`），新模块无需程序集配置。

---

## 2. 核心设计（决策）

### 2.1 时序：GeometryBuffer 提前（本规划唯一结构性改动）

```text
[现状] PrePass(URP depth) → ShadowCast(50/150) → SSS源(250) → opaque绘制 → Geometry/Metadata(300) → 反射/OIT(450) → ...
[目标] PrePass(URP depth) → ShadowCast(50/150) → SSS源(250)
       → GeometryBuffer(250) → Ho-GTAO(250) → opaque绘制(材质采样AO) → ...同现状
```

- 改动点：**Ho-GeometryBuffer settings.passEvent: 300 → BeforeRenderingOpaques（250）**；Ho-GTAO settings.passEvent 默认 **BeforeRenderingOpaques（250）**——⚠️ `passEvent` 是枚举，**无 251/252**（Inspector 无法选非枚举值），先后**由 Renderer 特性列表顺序保证**：PC_Renderer 里 GeometryBuffer 在前、Ho-GTAO 紧随（现有列表已是该顺序）。
- **架构立场（强硬版）**：GeometryBuffer 是**屏幕几何的唯一生产者**（保持全分辨率、不动 renderScale——SSS/GI/反射等消费方拿到的都是全屏一致数据）；GTAO 是它的**消费者**，半分辨率只是消费者自己的计算分辨率（shader 内 2x2 步进），不是 GeometryBuffer 降档。这与 HTrace"自画 PrePass + 自建金字塔"的隔离路线相反：**我们只用一个屏幕几何，谁用谁降采样。**
- 其余消费方（SSS/AO/GI/反射/角色特化）**只读全局纹理，不受事件提前影响**（同帧内数据一致；反而消除了"用到上一帧/未就绪"风险）。SSS 的 250 档与 GeometryBuffer 250 的先后由**列表顺序 + 事件值**共同保证（SSS 源档=250 与 GeometryBuffer 同事件——顺序上 GeometryBuffer 在 feature 列表 #1、SSS 在 #7，同事件按列表序执行，安全）。
- 备选（若实机发现提前有问题）：Ho-GTAO 退到 300 档做 **后处理乘模式**（读 camera color 乘 AO），但该模式失去 per-material `_SSAOStrength/Remap/Mask` 语义，只作降级预案。

### 2.2 算法（v1 = HTrace 最高档配方，参数可降；已逐行验证 compute 源码）

> 依据：HTrace GTAO 链路 = 深度金字塔 → HRenderGTAO（ray march）→ temporal（重投影+累积）→ spatial（Box/Disk）→ 上采样/合成。所有 kernel 均为**逐像素、无共享内存/粒子**——fragment 版 1:1 复刻可行，且免掉 compute 的 dispatch/历史绑定复杂度。**HTrace 原始档位档案（算法/去噪/分辨率逐档机制、参数生效点、最高档配方）→ `LILTOON_HTRACE_GTAO_QUALITY_REFERENCE.md`。**

**算法三选最（不妥协项）：**

1. **TracingMode = VisibilityBitmasks**（HTrace 最高档；HorizonSearch 是"快而多数场景够用"的默认档，**保留为同 shader keyword 后备**——移动端/无 temporal 时可用，见档案 §2）：
   - 机制（HRenderGTAO.compute:68-75/240-241/262）：每步采样把 horizon 角量化成 **32-bin**（`round(h*32)`），`OccludedBits = (0xFFFFFFFF<<min) & (0xFFFFFFFF>>(32-max))`，跨所有步进**按位或累加**成 uint；遮挡 = `1 - countbits/26`（26 = 有效 bin 归一）。
   - 质量：**细几何（栅栏/铁丝/发丝/细柱）显著更准**（官方 tooltip 明说）——这正是 toon 角色日常（头发缝隙/眼睫毛/眼镜框）。
   - fragment 代价：每步只多 2 次移位/与/或；**省掉** horizon 版的 acos 反解+积分（:268-275），反而更简。⚠️ `countbits` 需 **SM5**（移动端片段不支持）；32 档量化（≈5.6°/bin）噪声需 temporal 配合——本管线是 PC 渲染环境，无碍。
2. **去噪 = SpatioTemporal**（Denoising 最高档；None/SpatialOnly/TemporalOnly 为降档）：
   - **temporal**：运动矢量重投影（`ReprojectionCoord = px − motion`，:118）+ 分辨率/窗口变化校正（:119-120）+ 4 邻居双线性/双三次（Bicubic 档）+ **深度 gather 校验 + 法线校验**（`dot(histN, curN)` 阈值，:201-207）+ sampleCount 累积（截止 `count*2`，:323）+ rejection（**精确**=ray march 记录的 hit-velocity 指数衰减 / **简单**=常数 `1-TemporalRejection`，:330）。历史双纹理：RG16（AO+velocity）、RGBA8（samplecount/16 + normal）。
   - **spatial**：**Box**（Poisson 8 点固定偏移 × Plane/Normal/Gaussian 权重，pass 可叠，:101-150）或 **Disk**（动态半径：`Adaptivity = lerp(0.25, 1, pow(|1−ao|, _FilterAdaptivity*2))` 保边缘 / `Radius = dist·FilterRadius/RadiusScale` 深度缩放，:161-235）——两型皆双边式，Disk 边缘保护更好、略贵。
3. **厚度 = Linear**：`max(thickness/10 · linearDepth, thickness)`（随深度线性增长，:183）；**UseAttenuation 开**（距离加权 lerp，:123-128）。

**可降参数（性能→质量权衡项）：** 分辨率（Full/Half/Quarter；⚠️ HTrace 的 Half=(2,1)/Quarter=(2,2) 是**棋盘压缩寻址**——纹理仍全分辨率分配、只减 dispatch 线程+棋盘打包+上采样，不是缩小纹理；本实现用真半分辨率 RT 等价，:141-143）、SliceCount（1-4）、StepCount（8-32；步进**平方分布**近密远疏 + 蓝噪声抖动 + MinStep，:212-219）、TemporalFrameCount（**真实累积帧数上限**，HTrace 固定 12 帧；其 `SampleCountTemporal` 参数实际只门控自制 MV pass——见档案 §7）、BoxPassCount/DoubleSampleCount、FilterRadius/FilterAdaptivity。

**fragment 版替代项**：
- **无深度金字塔**：HTrace 的 march 按 offset 长度选金字塔 LOD（:218）省深度采样；我们的 march 直接用屏幕偏移 + `_HoGeometryBufferNormalDepthTexture.a` 线性深度逐点比较（采样仍是逐点，半分辨率下 fan-out 可接受；可选在 Unity 侧给 AO 计算纹理建 3 级 mip 实现同款 LOD——**登记为 v1.1 可选**）。
- **temporal 的运动矢量**：URP 内置 motion vector pass 只在 TAA/MotionBlur 启用时渲染，**管线目前没有**（`motion` 通道是占坑 ◻）→ v1 High 档 temporal 用**相机运动矢量 + 深度/法线校验**重投影（重投影不一致即重置历史，等价 HTrace 简单 rejection 思想；渲染环境多为静态镜头+循环动画，运动物体 AO 短暂重置可接受）。**逐物体运动矢量的完整版（自建 motion vector 重画 pass）登记 v1.1 增强**。

**质量档预设（我们 Ho-GTAO volume 提供 Quality 档，参数可再单调）：**

| 档 | 算法 | 去噪 | 分辨率 | Slice×Step | temporal 帧数 |
| --- | --- | --- | --- | --- | --- |
| High（默认） | Bitmask | SpatioTemporal + Disk | Half | 2×16 | 8 |
| Medium | Bitmask | Temporal(简单拒绝) + Box×2 | Half | 2×12 | 4 |
| Low | Bitmask | Spatial(Box×1) | Quarter | 2×8 | — |

> High 是"最高档配方 + 保守参数"的平衡（半分辨率也是 HTrace 场景的原档）；要最顶就 Full+Step32+SampleCount12。

### 2.3 输出与命名：公共 AO 语义（`_HoAOTexture`）

- 全局纹理：**`_HoAOTexture`**（语义名，不含算法名——与 `_HoGeometryBuffer*`/`_HoMetadataBuffer*` 公共资源命名一致；契约 `ao` 通道的运行时载体；AOV 导出名 = `ao`）。`Shader.PropertyToID` 常量入 `HoGTAOShaderConstants`，R8 UNORM，半分辨率；`SetGlobalTextureAfterPass`（GeometryBuffer pass 199-200 行同款模式）。
- **lilToon 语义直接写 HoAO**（不再关心吃的是什么算法）：
  ```hlsl
  // lil_common_frag.hlsl lilSampleScreenSpaceAO 改造后：
  float ao = LIL_SAMPLE_2D(_HoAOTexture, lil_sampler_linear_clamp, screenUV).r;
  float directAO = lerp(1.0, ao, _SSAODirectStrength);
  float indirectAO = lerp(1.0, ao, _SSAOIndirectStrength);
  ```
- **删除面清单**（一次性，含 shader 重生成 Generate All；`_ScreenSpaceAOSource` 全 lilToon 共 7 处见下）：
  1. `lil_common_frag.hlsl` 1189-1214：删 `_ScreenSpaceAOSource` if/else 分支与 **else 的 URP fallback**（`GetScreenSpaceAmbientOcclusion`/`_SCREEN_SPACE_OCCLUSION`）——只留直接采样 `_HoAOTexture`。
  2. `lil_common_input.hlsl` 830：`TEXTURE2D(_HTraceBufferAO)` → `TEXTURE2D(_HoAOTexture)`。
  3. CBUFFER 三处 `uint _ScreenSpaceAOSource`：`lil_common_input.hlsl`(:404) / `lil_common_input_base.hlsl`(:280) / `lil_common_input_opt.hlsl`(:280，尽管 opt 无 include 引用，保持一致删)。
  4. lilblock 属性：`CustomShaderResources/Properties/Default.lilblock:33` 与 `DefaultAll.lilblock:33` 的 `_ScreenSpaceAOSource ("AO RT", Int) = 0`：删。`_UseScreenSpaceAO` 保留（就是 AO 开关，轻量调参项）。
  5. Inspector：`lilPropertyGroupDrawerBaseSetting.cs:500-501`（HTraceAO HelpBox + "AO RT" 下拉）删；`lilMaterialProperties.cs:167`(定义)+`:746`(属性遍历数组) 删。
- **从此 lilToon 不再依赖**：`_ScreenSpaceOcclusionTexture` / `_SCREEN_SPACE_OCCLUSION` / `_AmbientOcclusionParam` / `GetScreenSpaceAmbientOcclusion`（URP 内置 SSAO 三条全局全解耦，见 §8）。旧材质资产上的 `_ScreenSpaceAOSource` 值残留无影响（属性不存在即忽略）。
- 回退/兜底：`HoGTAORendererFeature` 在 `beginCameraRendering` 设 `_HoAOTexture = white`（未启用/相机不渲染时材质采样安全）；GeometryBuffer 同款 `ResetGlobalState` 模式。

### 2.4 契约与意图参数

- `ao` 通道：R8f 0..1 ✅ 生产端=Ho-GTAO；**生产端不烘焙强度**（强度归属材质 `_SSAOStrength`，契约 0..1 干净）。HTrace 的 `Intensity=3.06` 属"生产端折叠强度"，移植时改由材质 `_SSAOStrength` 对齐视觉（在验收里做对照）。
- `aointent`（意图模式）：**v1 不做**（契约已占位 ◻，保持）。
- 无新通道、不改契约。
- `gisexclude`：与 AO 无关（GI 用）；描边不写 GeometryBuffer（此前 outline extrude 实验已回退），AO 天然不受描边壳影响——验收时再目视确认一次。

### 2.5 调试 / AOV

- DebugTile：新增 `HoGTAODebugViewInfo.Views`（直出 `_HoAOTexture`），**`HoDebugViewRenderKind` 枚举需先加 `GTAO` 值**（`Runtime/Debug/HoDebugViewInfo.cs:3-11`），再注册进 `HoDebugViewRegistry.AllViews`（+`using` 一行）。
- AOV：`ao` 通道 = 直接抓该全局纹理（AOV 导出层 v1 时登记，此处只保证纹理名冻结为 `_HoAOTexture`）。

---

## 3. 文件清单（模块模板对照，子代理已逐文件核查）

> 结构仿 `Runtime/SubsurfaceScattering`（feature 文件内含全部 pass 类 + RenderGraphResources + RenderTargets，同 SSS 单文件风格）；**Volume 部分参照 ScreenProcess/ImageProcess 的 Volume 惯例**（本模块参数是"场景可调"的艺术参数，对齐 HTraceAOVolume 的语义；SSS 是 feature Settings-only 的另一风格）。v1 有 5 个 pass（GTAO/重投影累积/空间滤波/上采样/Debug），链式数据流 = HTrace：march → temporal → spatial → upsample → 全局纹理。

### 3.1 UI 风格规范（实现时照抄现有 Ho-*，不另创风格）

**Feature Inspector**（`Editor/GTAO/HoGTAORendererFeatureEditor.cs`，仿 `Editor/GeometryBuffer/HoGeometryBufferRendererFeatureEditor.cs`）：
- `namespace lilToon.URP.Extensions.Editor.GTAO`；`[CustomEditor(typeof(HoGTAORendererFeature))] internal sealed class ... : UnityEditor.Editor`。
- 顶部 `EditorGUILayout.HelpBox(英文一句话定位 + 顺序建议, MessageType.Info)`（仿 GeometryBuffer:36-38："…writes… Add it before…"）。
- 三个分节（静态 `bool showXxx` + `LilUrpEditorSectionGui.DrawSectionHeader(ref show, "中文节标题", summary, 主题色)`）：
  - 节标题**中文**（"运行"/"调试"/"高级"）；摘要用 `LilUrpEditorSectionGui.BoolSummary/EnumName/IntSummary`（如 "开 / High"）。
  - 主题色照抄：`RuntimeColor = (0.46,0.64,0.92)` 蓝、`DebugColor = (0.86,0.62,0.38)` 橙、`AdvancedColor = (0.62,0.58,0.78)` 紫（GeometryBuffer:11-13）。
  - 内容 `DrawProperty("x")` → `EditorGUILayout.PropertyField`（序列化字段直接显示）。
  - 建议分节：**运行**（enabled/quality/resolution）、**调试**（debugMode/debugInSceneView/debugInGameView）、**高级**（passEvent/shader 引用/历史纹理档）。
- 共享工具：`Editor/LilUrpEditorSectionGui.cs`（`DrawSectionHeader/BoolSummary/EnumName/IntSummary/FloatSummary/FormatAvailable`）直接复用，**不改该文件**。

**Volume 组件**（Runtime/GTAO/HoGTAOVolume.cs，仿 Runtime/CharacterSpecialization/HoCharacterSpecializationVolume.cs）：
- 双菜单特性（两个都要）：`[VolumeComponentMenu("Post-processing/Ho-GTAO/屏幕空间 AO"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]` + `[VolumeComponentMenuForRenderPipeline("Post-processing/Ho-GTAO/屏幕空间 AO", typeof(UniversalRenderPipeline))]`；类 `public sealed class HoGTAOVolume : VolumeComponent, IPostProcessComponent`。
- 字段样式：**中文 `[InspectorName("…")]` + 中文 `[Tooltip("…")]`**（Volume 侧全中文，参考 CharacterSpecialization:196-410；Flow：质量档/分辨率/半径/厚度/切片/步进/去噪）。
- 自定义参数类 `Ho<X>Parameter : VolumeParameter<T>`（`Interp` = `t > 0 ? to : from`，仿 HoCharacterLayerMaskParameter:9-21）——枚举/特殊类型都这样包一层；float/int 用标准 `ClampedFloatParameter` 或照抄自定义（与 CharacterSpecialization 保持一致优先）。

**Volume 编辑器**（`Editor/GTAO/HoGTAOVolumeEditor.cs`，仿 `Editor/CharacterSpecialization/HoCharacterSpecializationVolumeEditor.cs`）：
- `[CustomEditor(typeof(HoGTAOVolume))] internal sealed class ... : VolumeComponentEditor`；`PropertyFetcher<HoGTAOVolume>` + `SerializedDataParameter` 字段缓存 + `OnEnable` 一次绑定（:78-80 样板）。
- UI 结构：分组（每组 `EditorGUILayout.BeginVertical(EditorStyles.helpBox)` + 标题）或按 CharacterSpecialization 的分节；建议组：**质量**（Quality/Resolution）、**追踪**（Radius/ScreenSpaceRadius/Thickness/SliceCount/StepCount）、**去噪**（TemporalFrameCount/TemporalRejection/SpatialFilter/FilterRadius/FilterAdaptivity/BoxPassCount）、**高级**（UseAttenuation/ThicknessMode/Fadeout）。
- 涉及条件显示（Quality 档联动、Disk/Box 互斥显隐）用 `ShowIf` 类逻辑做显隐（参考 ImageProcessStackVolumeEditor.Presets.cs 的条件组样式）。

**其余配套**：DebugTile 视图（`HoGTAODebugViewInfo` 视图短名如 "AO"/"AO History"/"AO Normal"，仿 SSS :12-30）；Editor asmdef 已存在（`Editor/jp.lilxyzw.liltoon.urp.extensions.Editor.asmdef`），新编辑器无需新增程序集。

| 文件 | 仿照 | 作用 |
| --- | --- | --- |
| `Runtime/GTAO/HoGTAORendererFeature.cs` | HoSubsurfaceScatteringRendererFeature.cs | `[DisallowMultipleRendererFeature("Ho-GTAO")]`；feature + 全部 pass（GTAO / Temporal / Spatial / Upsample / Debug）+ RenderGraphResources（含**两帧历史纹理字段**）+ RenderTargets 单文件；SSS 为 `Execute`/`RecordRenderGraph` 双路径，GTAO 可只写 RG 路径（URP17） |
| `Runtime/GTAO/HoGTAOSettings.cs` | HoSubsurfaceScatteringSettings.cs | 序列化：主事件（默认 BeforeRenderingOpaques，**靠列表顺序在 GeometryBuffer 之后**）、**Quality 档枚举（Low/Medium/High）**、分辨率档、模糊档、debug 开关、shader 引用 + 枚举（RenderScale/DebugMode） |
| `Runtime/GTAO/HoGTAOVolume.cs` | HTraceAOVolume.cs（`[VolumeComponentMenu("Lighting/lilToon: AO")]`、ClampedFloatParameter） | VolumeComponent：Radius/Thickness/Slice/Step/BlurRadius/BlurQuality/DebugMode（场景可调） |
| `Runtime/GTAO/HoGTAOShaderConstants.cs` | HoSubsurfaceScatteringShaderConstants.cs | `"Hidden/lilToon/URP/HoGTAO"`、Debug shader 名、**`_HoAOTexture`**（PropertyToID） |
| `Runtime/GTAO/HoGTAODebugPass.cs` + `HoGTAODebugViewInfo.cs` | HoSubsurfaceScatteringDebugPass.cs / DebugViewInfo.cs | DebugTile 视图（`ShaderAssetPath` 用 `Packages/jp.lilxyzw.liltoon.urp.extensions/...` 相对路径） |
| `Runtime/GTAO/Shaders/HoGTAO.shader` | HoSubsurfaceScattering.shader | 全屏：`ZWrite Off / ZTest Always / Cull Off` + Blit.hlsl `Vert/Frag`；多 Pass = GTAO 主体 / 双边模糊 / 上采样 |
| `Runtime/GTAO/Shaders/HoGTAOCommon.hlsl` | HoGeometryBufferSampling.hlsl | include `Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl`；GTAO 数学、噪声 |
| `Runtime/GTAO/Shaders/Debug/HoGTAODebug.shader` | HoSubsurfaceScatteringDebug.shader | DebugTile 直出（`_HoGTAODebugParams.x` 模式） |
| 蓝噪声资源（若无可复用） | — | 64×64 LDR |
| `Runtime/Debug/HoDebugViewRegistry.cs` | — | +1 行 `AddRange(views, HoGTAODebugViewInfo.Views)` + using |
| `Runtime/Debug/HoDebugViewInfo.cs` | — | `HoDebugViewRenderKind` 加 `GTAO` 枚举值 |
| `Editor/GTAO/HoGTAORendererFeatureEditor.cs` | Editor\GeometryBuffer\HoGeometryBufferRendererFeatureEditor.cs | **必做**（§3.1 规范：三色分节"运行/调试/高级"） |
| `Editor/GTAO/HoGTAOVolumeEditor.cs` | Editor\CharacterSpecialization\HoCharacterSpecializationVolumeEditor.cs | **必做**（§3.1 规范：VolumeComponentEditor + 分组） |

lilToon 侧改动（见 §2.3）：input+frag 两处 + 属性/分支/UI 删除面 5 项 + 重生成。关键 API（摘自 SSS 模板，已核实）：`frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>()`、`renderGraph.CreateTexture(...)`、`builder.SetGlobalTextureAfterPass(ao, HoGTAOShaderConstants.TextureId)` + `builder.AllowGlobalStateModification(true)`、`Blitter.BlitTexture`（或自定义 frag 的 RasterRenderPass）。

---

## 4. 参数表（v1，HoGTAOVolume；HTrace 全参数→我们映射已核实）

**质量档（volume，三档预设见 §2.2 表）+ 逐项可调：**（**HTrace 原始 32 项参数的完整"生效点/机制/剪枝"表 → `LILTOON_HTRACE_GTAO_QUALITY_REFERENCE.md` §5**，下表是本实现精选）

| 参数（volume） | 默认 | HTrace 对应（GTAOSettings） | 说明/生效点 |
| --- | --- | --- | --- |
| Quality | High | Denoising=SpatioTemporal + TracingMode=VisibilityBitmasks | 三档预设一键 |
| Resolution | Half | Resolution | Full/Half(棋盘2,1)/Quarter(2,2) |
| Radius（世界） | 5 | WorldSpaceRadius(场景=5) | falloff=1/r²（HRenderGTAO:185） |
| ScreenSpaceRadius（像素） | 25 | ScreenSpaceRadius(25) | 屏幕空间步进半径（:195/:214） |
| Thickness | 0.2 | Thickness(0.2) | Linear 档= max(thickness/10·linDepth, thickness)（:183/:220） |
| SliceCount | 2 | SliceCount(2) | 正交切片数 1-4（:218） |
| StepCount | 16 | StepCount(16) | 每切片步进 8-32，平方分布+噪抖（:217） |
| TemporalFrameCount | 8 | （HTrace 累积上限**固定 12 帧**：`g_HTemporalSamplecountAO=6`×2 :187/:323；其 `SampleCountTemporal` 参数**不控制帧数**，只门控自制 MV pass——见档案 §7） | 历史累积帧数上限（我们自定，映射真实上限） |
| TemporalRejection | 0.7 | TemporalRejection(0.7) | 简单拒绝强度（:330） |
| ReprojectionFilter | Bilinear | ReprojectionFilter(Bilinear/Bicubic) | 历史重投影滤波（:232 主代码开关） |
| SpatialFilter | Disk | SpatialFilterType(Disk/Box) | Disk=半径+自适应保边缘；Box=Poisson 8 点×可叠 pass |
| FilterRadius | 0.4 | FilterRadius(0.4→0..0.12 remap) | Disk 半径（:197） |
| FilterAdaptivity | 0.1 | FilterAdaptivity(0.1) | 边缘保护强度（:192） |
| BoxPassCount | 2 | BoxPassCount(2)/DoubleSampleCount | Box 叠 pass 数（:335-360） |
| UseAttenuation | 开 | UseAttenuation(true) | 距离加权；**仅 bitmask 分支生效**（HorizonSearch 恒有衰减，:123-128） |
| ThicknessMode | Linear | ThicknessMode(Linear/Uniform) | 线性/均匀厚度（:220） |
| NoiseScale | 蓝噪声 | （内置蓝噪声/随机抖动） | march 相位抖动（:184） |
| Fadeout | 关 | GeneralSettings.Fadeout | 距离淡出（:199） |
| ExcludeCasting/Receiving | 0 | ExcludeCasting/Receiving | rendering layer mask 排除（:192-194） |

**HTrace 参数里我们不带的（及原因）：**
- `Denoising=None/SpatialOnly/TemporalOnly`（我们用 SpatioTemporal 最高档，不暴露开关，质量档内部处理）。
- `TracingMode=HorizonSearch`（只保留最高档 Bitmask）。
- `DepthFormat`（HTrace 只有 R16 一档，R32 被注释——无需选择）。
- `UseSimpleRejection`（我们 temporal 即"简单拒绝"，精确 hit-velocity 拒绝是 v1.1 增强，对应 HTrace `UseSimpleRejection=false` 路径）。
- `UseNormalWeightingTemporal`、`ReprojectionFilter=Bicubic`：**保留为 v1.1 可选**（temporal 法线加权/双三次在渲染环境收益小）。
- `DebugModeGTAO`（MainBuffers/AmbientOcclusion/TemporalDisocclusion）：我们用 DebugTile 直出 `ao` + 模式参数替代，不逐档复刻。
- `ExcludedIntensity`：排除表面 AO 强度——用材质 `_SSAOMask`（已有）替代，不引入新通道。
- `Intensity`：**不带**（契约 0..1，强度归材质 `_SSAOStrength`，见 §2.4）。
- `g_HIntensityAO = Intensity*1.2`（OutputComposition 的 pow）与 `OutputDithering`（全包无消费点，遗留参数）：不带。

---

## 5. 实施步骤

1. **lilToon 语义改造**：按 §2.3 删除面清单（frag 直采 `_HoAOTexture`、input 改名、CBUFFER×3、lilblock 属性、Inspector）→ 重生成 shader → grep `_HTraceBufferAO`/`_ScreenSpaceAOSource` 归零验证（lilToon 内）。
2. **Ho-GTAO 模块骨架**（§3 文件）→ 先出"直出调试"版本（只采样 GeometryBuffer 显示 normal/depth，验证时序与纹理链路）。
3. **GTAO 主体（Bitmask march）**（§2.2：32-bin 量化 + 按位或 + countbits/26；半分辨率、Slice2×Step16、蓝噪声抖动）→ Frame Debugger 目测。
4. **空间滤波（Disk）/上采样** → 关掉 HTrace feature、开 Ho-GTAO（Quality=Low 先对齐纯空间档），朱木古堂实机对比（AO 强弱/边缘/无 artifact）。
5. **Temporal（High 档）**：历史两帧纹理 + 相机运动矢量/深度法线校验重投影 + sampleCount 累积 → 对比 HTrace SpatioTemporal 的静态稳定性。
6. **Volume 参数化（Quality 三档）** → DebugTile 注册 → 关闭兜底（white）验证。
7. **收尾**：从 PC_Renderer 移除 HTrace AO feature（替换为 Ho-GTAO）；HTraceSSGI 保留（SSGI 仍占用），grep 全工程确认 lilToon/扩展无 `_HTraceBufferAO`/HTraceAO 消费引用。
8. 提交。

---

## 6. 验收

1. 朱木古堂实机：AO 观感与 HTrace 基线（强度/范围/网格感）对齐（对照：HTrace 材质侧读 pow 前原始因子 → Ho-GTAO 也输出原始 0..1 因子；HTrace Intensity 3.06 属后处理折叠，不与材质对照，视觉对齐即可）。
2. 材质/场景**零改动**（除 GeometryBuffer passEvent 300 → 250 一处 asset 设置 + shader 重生成）。
3. **语义验收**：lilToon 面板 GI/AO 区不再有"AO RT"下拉与 HTrace HelpBox；`grep _ScreenSpaceAOSource`（lilToon）= 0 命中；`grep GetScreenSpaceAmbientOcclusion | _SCREEN_SPACE_OCCLUSION | _AmbientOcclusionParam | _ScreenSpaceOcclusionTexture`（lilToon）= 0 命中。
4. Frame Debugger：可见 GeometryBuffer(250) → Ho-GTAO(250，按列表顺序紧随) → opaque 绘制（材质读 `_HoAOTexture`）；DebugTile 选择 `ao` 视图直出。
5. 关 Ho-GTAO feature：外观=无 AO（white 兜底），不崩、无粉色/黑块。
6. 移除 HTrace AO 后：Frames 无报错；`grep -R "_HTraceBufferAO"` 全工程 0 命中（lilToon + extensions）。
7. AOV 层接入后 `ao` 通道可导出（Nuke 可读，0..1）。

---

## 7. 风险与不做清单

**风险**：
- GeometryBuffer 提前（300→250）可能影响未预见的同帧消费方 → 步骤 2 的"直出调试"先验证时序；若回归，走 §2.1 备选（后处理乘模式，另开 v1.1 讨论 per-material 语义）。
- **temporal 的工程点**：历史两帧纹理的帧间管理（相机尺寸变化/相机切换重置——参考 GeometryBuffer 的 `beginCameraRendering` Reset 模式）；RG 跨帧资源声明；**运动矢量**：不做逐物体 motion pass，用相机运动矢量 + 深度/法线一致性校验（运动物体历史自动重置，视觉可接受；完整 motion-vector pass 登记 v1.1）。
- Bitmask+传统半分辨率下细几何（头发缝隙）仍可能渗漏 → 由 Radius/Thickness 调；半分辨率 checkerboard（HTrace Half=(2,1)）在静态镜头下可能有 checker 纹理残留 → 上采样 filter 用 HTrace Interpolation 同款（深度引导）。
- 无 temporal（Low 档）时静态噪声/闪烁 → 蓝噪声 + Disk 滤波兜底（与 HTrace SpatialOnly 同级别）。
- `_SSAOColor*` 类未在本 fork 材质侧出现（`lil_common_frag.hlsl` 只用 `_SSAOStrength/_SSAODirectStrength/_SSAOIndirectStrength/_SSAORemap/_SSAOContrast/_SSAOMask/_UseScreenSpaceAO/_ScreenSpaceAOSource`）——契约/草案里若提过 `_SSAOColor*`，以实测为准，实现时核对 lilblock 属性清单。
- 删除 `_ScreenSpaceAOSource` 属性后旧材质残留值不生效（Unity 忽略不存在属性）——无迁移成本；若担心，保留属性字段但不参与分支（二选一，推荐直接删，干净）。

**不做（登记不实现）**：深度金字塔（fragment 版以屏幕空间步进替代）、bent normals、RTAO、意图模式（`aointent`）、`gisexclude` 接入 AO、描边排除位、**URP 内置 SSAO 路径（lilToon 已解耦，内置 pass 本身保留不动，见 §8）**。~~temporal denoise（v1）~~ **已撤除**：按"算法取 HTrace 最高档"（§2.2），temporal 去噪纳入 v1（质量档 High 默认，做历史纹理 + 运动矢量重投影；Deep 档可关）。

---

## 8. URP 内置 SSAO：解耦（本次）与删除清单（后续）

> 状态：本次（Ho-GTAO v1）**只解耦不删除**——lilToon 不再消费内置 SSAO 任何全局，Ho-GTAO 走独立 `_HoAOTexture`，与内置 SSAO 零共享状态（不写 `_ScreenSpaceOcclusionTexture`、不启 `_SCREEN_SPACE_OCCLUSION`、不碰 `_AmbientOcclusionParam`、不占 `resourceData.ssaoTexture`）。内置 SSAO feature 装不装都无副作用（无 enable 方时 keyword 恒 OFF）。

### 8.1 运行时安全禁用（将来第一步，低风险）

- 仅"移除 feature"即可安全禁用：全部 URP 自带 shader（Lit/SimpleLit/BakedLit/Unlit/Particles/VFX/ShaderGraph 等）走 `_SCREEN_SPACE_OCCLUSION` OFF 分支 → AO 恒 1（AmbientOcclusion.hlsl:28-34），不会出错。
- **必须保留** `ScriptableRenderer.cs:1735` 的 `_AmbientOcclusionParam` 清零（每相机渲染前 `SetGlobalVector(..., zero)`）——它是"无 SSAO → AO=1"的安全开关（x=0 → `saturate(sample + 1) = 1`）。**别连这个一起删。**（附带：该向量 x=SSAO 开/关、w=directLightStrength（默认 0.25，`ScreenSpaceAmbientOcclusion.cs:18`）——x=0 时 w 也无效，清零即全关。）

### 8.2 完整删除的硬依赖（三个群，按风险排序）

1. **Deferred SSAOOnly pass**（高风险，动了就要一起改）：
   `DeferredLights.cs:698-700 / 1126-1132`（RenderSSAOBeforeShading，第一个方向光应用 SSAO）+ `DeferredLights.cs:98-107 / 116-125`（SSAOOnlyPassNames）+ `StencilDeferred.shader:390-429`（SSAOOnly pass + :420 multi_compile）+ `StencilDeferred.hlsl:56-63 / 294-304 / 335-347`。
2. **构建/剥离逻辑**（中高，删类必须同步这里，否则编译错误）：
   `ShaderBuildPreprocessor.cs:657 / 852-868 / 1107-1129`、`ShaderScriptableStripper.cs:654-668`、`ScreenSpaceAmbientOcclusionStripper.cs:7-62`（BlueNoise/SSAO shader 资源剥离）、`UniversalRenderPipelineAssetPrefiltering.cs:102-105 / 127-145`（**直接引用 `ScreenSpaceAmbientOcclusion.k_*` 常量**）+ 对应 Tests（ShaderPrefilteringTests.cs:427-450、ShaderBuildPreprocessorTests.cs:820-873）。
3. **资源注册**（中）：`ScreenSpaceAmbientOcclusion.cs:62-108`（Persistent/DynamicResources，`[ResourcePath]`/`[ResourceFormattedPaths]`）、`Textures/BlueNoise256/LDR_LLL1_*`、`Shaders/Utils/ScreenSpaceAmbientOcclusion.shader`、`ShaderLibrary/SSAO.hlsl`。

### 8.3 建议同批清理（低风险但扫尾）

- keyword 全局态：`ScreenSpaceAmbientOcclusionPass.cs:457 / 565 / 667` 是**唯一三处** enable/disable 点（且仅在 pass 执行过时清除）——删 SSAO 后在 `ClearRenderingState`（ScriptableRenderer.cs:1711-1736）追加 `SetKeyword(ScreenSpaceOcclusion, false)`，防第三方残留污染。
- `_ssaoTexture` 链路（`UniversalResourceData.cs:321-329 / :372`、`DrawObjectsPass.cs:316-318 / 496-498`、`RenderObjectsPass.cs:303-305`、`DBufferRenderPass.cs:286-287`）：删 SSAO 后全部 `IsValid` 自动短路——**最低改动=保留**；彻底清则删字段+四处消费。
- DepthNormals prepass 自动解除：无 pass 请求 Normal input 后 `requiresNormalsTexture=false`（UniversalRenderer.cs:949/958、UniversalRendererRenderGraph.cs:1013-1027/1611）——无需显式改动。
- 可选（省变体，工作量大）：20+ 处 `multi_compile _SCREEN_SPACE_OCCLUSION`（Lit/SimpleLit/Terrain/SpeedTree/Particles/StencilDeferred/ClusterDeferred/VFXPasses.template 等）与 ShaderKeywordFilter（Prefiltering.cs:102-105）——keyword OFF 变体不删也无运行时问题。

### 8.4 决策：本次不做，何时做

- **本次不入**（登记不实现）：内置 SSAO 整个移除。理由：① 属 URP fork 改动，周期长、要动构建管线+测试；② 现在 lilToon 已解耦，内置 SSAO 对我们零影响；③ 建议等 **Ho-SSGI 完工、HTrace 全移除**后再启动（一次动 URP fork + 构建 + 测试，避免半吊子状态）。
- **本次做的**：lilToon 解耦（§2.3）+ Ho-GTAO 走独立纹理——从此内置 SSAO 与我们管线无任何共享状态，它躺在那儿只是"未启用 feature + 无 keyword"。

---

## 9. 实机验证检查表（未验证的运行时假设 + 失败特征 + 预案）

> 代码已通过编译层验证（Unity 日志驱动 4 轮修复）。以下为**只有实机渲染才能暴露**的假设；出问题时按表对照。

| # | 假设 | 失败特征 | 预案 |
| --- | --- | --- | --- |
| 1 | RG 中 `ImportTexture(RTHandle)` 可作为 raster attachment 写（temporal 输出） | Console 断言（"Texture cannot be written/imported…"）/ 历史不更新 | temporal 改为输出 RG transient，帧内 `CopyTexture` 到持久 RTHandle |
| 2 | `UNITY_MATRIX_P/V` 在 RG raster pass 内可用（march 的视空间重建） | AO 图案扭曲/位置错位 | march 显式 `ctx.cmd.SetGlobalMatrix` 相机矩阵（HTrace 同款） |
| 3 | `_ZBufferParams` 逆式在反向 Z 平台正确（temporal deviceZ） | temporal 重投影错乱/AO 闪烁位错 | 改用 `GL.GetGPUProjectionMatrix` 显式逆投影 |
| 4 | `GL.GetGPUProjectionMatrix(camera.projectionMatrix, false)` 与渲染目标一致 | 相机运动时历史重投影漂移 | 按 `cameraData.renderIntoTexture` 传参 |
| 5 | 半分辨率 RT + `_ScreenParams.zw`（全屏）乘 divisor 的采样换算（march/滤波/上采样） | AO 分辨率感错误（半径变小/条纹） | 用计算 RT 的 `_TexelSize` 全局参数替换 |
| 6 | 材质属性 `_HoAOTexture` 默认 white 在全局 SetGlobalTexture 后正确覆盖 | 挂载后仍全白（无 AO） | 确认 Ho-GTAO pass 执行（Frame Debugger）；检查 feature enabled 与 GeometryBuffer passEvent 已改 250 |
| 7 | 250 同事件下 GeometryBuffer→Ho-GTAO 顺序 = 列表顺序 | AO 读空（黑色 texture 采样）时序颠倒 | 确认 feature 列表顺序（GeomBuffer 在前）；或改用非枚举事件值方案（自定义枚举添加） |

> 常见现象与对应项：**全白/无效果**→#6/#7；**图案扭曲**→#2/#5；**相机动时闪烁/拖影**→#3/#4；**历史不更新**→#1。反馈格式：现象 + Frame Debugger 截图 + Console 报错（三选一）即可让我定位。

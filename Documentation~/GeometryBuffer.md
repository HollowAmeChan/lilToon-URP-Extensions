# GeometryBuffer 公共契约

> **状态：现行契约**（2026 文档审核时按当前实现校正：语义数值现由 SB / OB / AC 提供，MetadataBuffer 已删除）。
> 相关：`架构优化/Ho-ObjectBuffer.md`、`架构优化/Ho-SurfaceBuffer.md`、`架构优化/Ho-AttributeComposite.md`、
> `架构边界/MSAA.md`（GB 是唯一的 MSAA 解析入口）。

> GeometryBuffer 是 Ho-URP Extensions 的屏幕几何输入层。
>
> 它提供真实几何的法线、线性深度和有效覆盖判断，并额外提供与物理几何隔离的描边视觉 coverage。AO、GI、SSS、反射、角色特化和 ScreenProcess 都应按本文的语义消费这些资源。

## 1. 设计边界

GeometryBuffer 不是 URP `_CameraDepthTexture` 的别名，也不是完整 deferred GBuffer。它是由 Ho 管线主动重绘得到的、可复用的屏幕空间几何层：

```text
真实几何            -> NormalDepth / DepthTexture
视觉描边外扩壳      -> OutlineNormalDepthTexture
天空/无几何区域      -> SkyTexture（可选）
```

最重要的语义分离：

- `NormalDepth.a` 的 coverage 表示“这里有没有真实几何表面”；
- 描边外扩壳不是物理几何，不能写入主 `NormalDepth.a`；
- `_HoGeometryBufferOutlineNormalDepthTexture` 的 `RGB/A` 表示描边自己的法线和线性深度，并由 `A > 0` 派生描边 coverage；它不能被 AO/GI 当作物理表面；
- CameraColor 仍然是已经渲染的颜色源，GeometryBuffer 不替代它。

这套分离解决两个相反的问题：SSGI/GTAO 不会把描边当真实表面，DOF 等视觉后处理又可以识别并保护描边。

GeometryBuffer 的长期扩展模型是：**PhysicalGeometryBuffer 保持稳定，VisualSurfaceBuffer 按需增加**。后续增加描边深度、视觉法线或颜色时，不应覆盖物理通道，而应作为视觉表面扩展供特定消费者选择。

## 2. 生产顺序

入口：`Runtime/GeometryBuffer/HoGeometryBufferRendererFeature.cs`

默认时序：

```text
beginCameraRendering
  -> 重置 GeometryBuffer 全局状态
  -> Ho-GeometryBuffer Output（默认 BeforeRenderingOpaques）
       -> fallback/base GeometryBuffer pass
       -> lilToon HoGeometryBuffer pass
       -> lilToon HoGeometryBufferOutlineNormalDepth pass
       -> MSAA resolve（仅 msaaSamples > 1，见下）
  -> 可选 Ho-GeometryBuffer Sky（默认 AfterRenderingSkybox，enableSkyBuffer 开启时才跑）
  -> 其他消费者读取全局 RT / RenderGraph TextureHandle
  -> 可选 Debug（默认 AfterRenderingPostProcessing）
  -> 相机结束或 ScreenProcess 收尾时清理全局绑定
```

MSAA resolve 不属于独立 pass 类，它挂在 `HoGeometryBufferPass` 内部：

- RenderGraph 路径：`AddRasterRenderPass` 名为 `Ho-GeometryBuffer MSAA Resolve` 与 `Ho-GeometryBuffer Outline MSAA Resolve`，把 MSAA 附件归约进单采样目标并顺带产出覆盖率；
- 兼容路径：`ResolveGeometryBuffer(cmd)` / `ResolveOutlineNormalDepth(cmd)`；
- 两者都设置 `_HoGeometryBufferCoverageTextureValid` / `_HoGeometryBufferOutlineCoverageTextureValid`（非 MSAA 为 0）。

`HoGeometryBufferPass` 同时生产真实几何和描边 visual normal/depth：

1. 清空 `NormalDepthTexture` 和独立 depth attachment。
2. 使用 `LightMode = HoGeometryBuffer` 绘制基础几何；没有该 pass 的材质可走 fallback shader。
3. 清空 `OutlineNormalDepthTexture`，使用 `LightMode = HoGeometryBufferOutlineNormalDepth` 绘制外扩描边；该 pass 写入描边编码法线和线性深度，不写主物理 geometry。
4. 发布全局纹理和 `_HoGeometryBufferValid`。

GeometryBuffer 的 `passEvent`、render queue、layer mask、render scale 都由 `HoGeometryBufferSettings` 控制。默认 `passEvent` 是 `BeforeRenderingOpaques`，默认 render scale 是 Full。

## 3. 输出资源

### 3.1 NormalDepth

全局名：`_HoGeometryBufferNormalDepthTexture`

默认优先格式：`R16G16B16A16_SFloat`。

| 通道 | 语义 | 无效值 |
| --- | --- | --- |
| `RGB` | 世界法线编码：`normal * 0.5 + 0.5` | 未定义；必须先检查 coverage/normal validity |
| `A` | 线性 eye depth，单位与相机空间深度一致 | `0` |

Coverage 在非 MSAA 下不是额外的通道，而是从 alpha 派生（MSAA 下另有 resolve 出来的 R8 覆盖率图，见下）：

```hlsl
half coverage = LilHoGeometryBufferCoverage(normalDepth);
// 等价于 step(0.0001, normalDepth.a)
```

公共采样函数位于 `Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl`：

- `LilHoGeometryBufferCoverage()`：判断真实几何是否覆盖像素；
- `LilHoGeometryBufferCoverageAt(uv, normalDepth)`：**MSAA 下优先读 resolve 出来的覆盖率图**，否则回退到 `LilHoGeometryBufferCoverage()`；
- `LilHoGeometryBufferOutlineCoverageAt(uv, outlineNormalDepth)`：同上，作用于描边覆盖率；
- `LilHoGeometryBufferNormalValid()`：判断 coverage 和法线是否同时有效；
- `LilHoGeometryBufferLinearDepthOrFar()`：无 coverage 时返回调用方指定的远裁剪面；
- `LilHoGeometryBufferEncodedNormalOrBlack()`：无效法线返回黑色；
- `LilHoGeometryBufferWorldNormalOrZero()`：无效法线返回零。

MSAA 下覆盖率有独立来源，不能只看 alpha：

- `_HoGeometryBufferCoverageTexture` / `_HoGeometryBufferOutlineCoverageTexture`：R8，`HoGeometryBufferResolve.shader` 的 `SV_Target1`，只在 `msaaSamples > 1` 时创建；
- `.r` = 该像素被几何覆盖的样本占比（**总覆盖率**，公共契约）；
- `.g` = **被解析面占有率**：resolve 选中的那个最近样本所代表的表面占了多少像素。它由"法线夹角小 + 深度落在同一条带内"的启发式分组算出（不是按对象身份分组的），所以只能当"最近面占多少"，**不能当成"某个部件占多少"**；
- 有效性由 `_HoGeometryBufferCoverageTextureValid` / `_HoGeometryBufferOutlineCoverageTextureValid` 发布（非 MSAA 时为 0，此时 `...CoverageAt()` 回退到 alpha 路径）。

#### NormalDepth 的消费者规则

- AO/GI/SSS/PLR/CharacterSpecialization：必须以 coverage 作为几何有效性 gate。
- coverage 为 0 的像素不能进入 GI source、history、depth pyramid、AO 表面采样或物理遮挡判断。
- 不能把 `NormalDepth.a == 0` 直接当普通深度 0；需要使用 `LilHoGeometryBufferLinearDepthOrFar()` 或显式跳过。
- 描边像素即使在 CameraColor 中有黑色/彩色，也应保持 `NormalDepth` coverage 为 0。

### 3.2 独立 DepthTexture

全局名：`_HoGeometryBufferDepthTexture`

这是 GeometryBuffer pass 使用的独立 depth/stencil attachment，主要用于：

- GeometryBuffer 本身的深度测试；
- OutlineNormalDepth pass 的可见性测试；
- 需要硬 ZTest 的内部绘制流程；
- **GTAO 的深度输入**：`HoGTAO.shader` 点采样 `.r` 得到**原始设备深度**（reversed-Z，与 `_CameraDepthTexture` 同一值域），再用 `LinearEyeDepth(raw, _ZBufferParams)` 转成线性；C# 侧通过 `HoGeometryBufferShaderConstants.DepthTextureId` 发布它并当作只读 depth attachment 绑定。

它不是 `_CameraDepthTexture` 的别名。**这里有两套深度语义，混用即错**：

| 来源 | 内容 | 用法 |
| --- | --- | --- |
| `_HoGeometryBufferDepthTexture.r` | 原始设备深度（reversed-Z，非线性） | 必须 `LinearEyeDepth(raw, _ZBufferParams)` |
| `NormalDepth.a` | 已经是线性 eye depth | 直接用，或 `LilHoGeometryBufferLinearDepthOrFar()` |

需要在 shader 中消费深度语义时，**优先读 `NormalDepth.a`**（少一次转换、且带 coverage gate）；只有确实需要与相机深度缓冲同值域的原始深度时才读 DepthTexture。

### 3.3 OutlineNormalDepthTexture

全局名：`_HoGeometryBufferOutlineNormalDepthTexture`

默认格式：`R16G16B16A16_SFloat`，与主 `NormalDepth` 同构。

| 通道 | 语义 |
| --- | --- |
| `RGB` | 描边壳世界法线编码：`normal * 0.5 + 0.5` |
| `A` | 描边壳线性 eye depth；`A > 0.0001` 即描边 coverage 有效 |

这个 RT 的设计目的不是让描边成为真实表面，而是让视觉后处理识别描边：

- DOF 可以选择描边自己的 visual depth，使描边参与自然景深，而不是强行保持锐利；
- 后续可以在 blur 后重新合成描边；
- 调试系统可以直接验证描边 pass 是否被执行；
- SSGI/GTAO 不应读取它来建立物理几何。

OutlineNormalDepth pass 使用 `LightMode = HoGeometryBufferOutlineNormalDepth`，顶点路径启用 lilToon 的 `LIL_OUTLINE`，因此会执行 outline vertex expansion；fragment 做 outline alpha/cutout/dissolve clipping，并输出翻转后的描边法线与线性深度。

lilToon 的 `CustomShaderResources/URP/Default*Outline.lilblock` 模板直接声明这个 pass。使用 `UsePass` 的自定义模板只有在其 `lilPassShaderName` 指向的隐藏 pass shader 自己提供该 pass 时才能引用它；本次不向通用 `DefaultUsePassOutline*.lilblock` 强行添加不存在的 UsePass。`lilShaderContainerImporter` 只负责展开模板占位符，不承载这个 pass 的业务结构。修改模板后需要执行：

```text
Assets/lilToon/[Shader] Refresh shaders
```

否则已有生成 shader 仍没有新 pass，OutlineNormalDepthTexture 会是全黑。

### 3.4 SkyTexture

全局名：`_HoGeometryBufferSkyTexture`

仅在 `HoGeometryBufferSettings.enableSkyBuffer` 开启时生产。由 `HoGeometryBufferSkyPass` 在天空绘制之后从当前 camera color 捕获：

| 通道 | 语义 |
| --- | --- |
| `RGB` | 天空/背景 radiance |
| `A` | sky contribution / 可用性贡献 |

Sky capture 会读取 `NormalDepth` coverage：无真实几何的区域才贡献天空。`SkyTyndall` 等天空效果必须同时检查 `_HoGeometryBufferSkyTextureValid`。

### 3.5 Valid flags

- `_HoGeometryBufferValid`：GeometryBuffer 主 pass 是否在当前帧生产了真实资源；
- `_HoGeometryBufferSkyTextureValid`：SkyTexture 是否在当前帧生产。

GeometryBuffer 被禁用、当前 camera 不支持或资源被 reset 时，纹理会回退到 black texture，valid flag 会清零。消费者必须把 valid flag 和纹理有效性一起考虑。

## 4. lilToon 输入 pass

### 4.1 基础几何

lilToon 的各类基础/outline shader 需要提供：

```text
LightMode = HoGeometryBuffer
fragment  = fragGeometryBuffer
```

`fragGeometryBuffer()` 输出基础网格位置的世界法线和线性深度。该 pass 不定义 `LIL_OUTLINE`，因此不会执行外扩顶点变换。

没有该 pass 的普通材质可以使用 `Hidden/lilToon/URP/GeometryBuffer/Fallback`，但 fallback 只能提供基础几何，不能自动产生描边 coverage。

### 4.2 描边 normal/depth

带 lilToon 描边的 shader 额外提供：

```text
LightMode = HoGeometryBufferOutlineNormalDepth
fragment  = fragOutlineNormalDepth
define    LIL_OUTLINE
```

这个 pass 与基础 `HoGeometryBuffer` pass 有意分离。不能简单地给基础 GeometryBuffer pass 加 `LIL_OUTLINE`，因为那会把整颗 mesh 的基础几何也按 outline expansion 外扩，导致主体法线/深度错误。

## 5. RenderGraph 与兼容路径

### RenderGraph

`HoGeometryBufferRenderGraphResources` 每帧暴露：

- `normalDepthTexture`
- `depthTexture`
- `outlineNormalDepthTexture`
- `coverageTexture`（仅 MSAA resolve 生产时有效）
- `outlineCoverageTexture`（仅 MSAA resolve 生产时有效）
- `skyTexture`

`HasRequiredTextures` 只要求 `normalDepthTexture` 有效；其余按生产者条件可选。

生产 pass 使用 `SetGlobalTextureAfterPass` 发布全局资源；消费者通过 `frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>()` 获取 TextureHandle，并在实际读取时 `builder.UseTexture(..., AccessFlags.Read)`。

### Compatibility

兼容路径使用 `RTHandle`：

- 基础几何先写 `NormalDepthTexture + DepthTexture`；
- 描边 normal/depth 再绑定 `OutlineNormalDepthTexture + DepthTexture`，保留 depth 内容做 ZTest；
- pass 结束后设置对应 global texture；
- camera begin/reset 或 ScreenProcess 收尾时恢复 black texture 和 valid flag。

## 6. DebugTile 调试入口

GeometryBuffer DebugView 和 DebugTile 已注册：

| View ID | 作用 |
| --- | --- |
| `geometry.coverage` | 真实几何 coverage |
| `geometry.linear-depth` | NormalDepth alpha 的线性深度 |
| `geometry.world-normal` | 编码世界法线 |
| `geometry.normal-validity` | coverage + 法线有效性 |
| `geometry.outline-normal` | 独立描边法线，预期描边区域显示编码法线 |
| `geometry.outline-linear-depth` | 独立描边线性深度 |
| `geometry.sky-radiance` | SkyTexture RGB |
| `geometry.sky-contribution` | SkyTexture alpha |

排查描边时必须同时看：

1. `geometry.coverage`：描边应保持黑色/无真实几何 coverage；
2. `geometry.outline-normal`：描边应显示有效编码法线；
3. `geometry.outline-linear-depth`：描边应显示有效线性深度，非描边区域为 0；
4. `geometry.linear-depth`：基础表面深度是否连续；
5. DOF 开关前后 CameraColor：确认描边是否按视觉深度参与模糊。

## 7. 消费者契约

| 消费者 | 应读什么 | 不应读什么 |
| --- | --- | --- |
| GTAO | NormalDepth normal/depth/coverage | OutlineNormalDepth 当作物理几何 |
| Ho-SSGI | NormalDepth coverage、normal、depth | OutlineNormalDepth 作为 caster/receiver |
| SSS | NormalDepth 几何 + SB 的 Classification（经 AC 门面） | 用描边 coverage 伪造物理表面 |
| PLR | NormalDepth 深度/法线 + OB 覆盖率（接收面遮罩）+ SB 的 Material/Reflection | 把 outline mask 当反射平面 |
| CharacterSpecialization | NormalDepth + AC 的 Selection 池（语义覆盖率） | 用 outline coverage 推断角色几何 |
| ScreenProcess DOF | NormalDepth 深度 + OutlineNormalDepth visual depth | 把描边写入主 NormalDepth |
| ScreenProcess Outline/EdgeLight | NormalDepth 几何；遮罩用 OB 覆盖率 | 把 OutlineNormalDepth 当真实法线/深度 |

公共原则：

```text
NormalDepth = physical geometry truth
OutlineNormalDepth = visual outline normal/depth truth
OB identity pool + part tags = object/semantic truth（覆盖率与归属）
SB five surfaces = material/surface numeric truth
CameraColor = rendered color source
```

## 8. 反射专用边界

GeometryBuffer 只提供 PLR/SSR 所需的物理法线、线性深度和 coverage；材质 roughness、metallic、reflectance 与 PLR strength 现在由 **SB 的 `Material` / `Reflection`** 提供（经 AC 门面 `HoAC_Attribute` 两行读出），表面色提示由 SB 的 `Color` 提供 —— 这些都不再经过 GeometryBuffer，也不再有任何 `Target5` 之类的槽位叫法。

当前实现没有 GeometryBuffer `Custom0` producer 或消费者。若未来需要逐像素 PLR receiver 数据，应新增并命名为 PLR 专用 RT，先冻结 RGBA 语义后再接入；不得把它重新定义为跨功能的泛用 Custom0，也不得覆盖 `NormalDepth` 的物理几何语义。

## 9. 常见错误

- 只打开 GeometryBuffer，但没有刷新 lilToon shader：NormalDepth 有数据，OutlineNormalDepth 全黑。
- 把 `_HoGeometryBufferDepthTexture` 当线性深度采样；应该使用 `NormalDepth.a`。
- 把 outline coverage 写入 `NormalDepth.a`，导致 AO/GI/SSS 把描边当真实表面。
- GeometryBuffer layer mask 或 render queue 没覆盖角色，导致基础几何和 outline coverage 都缺失。
- 只看 `geometry.coverage` 就判断描边是否输出；描边的正确检查是同时看 physical coverage 和 outline coverage。

## 10. 相关源码

- Producer：`Runtime/GeometryBuffer/HoGeometryBufferRendererFeature.cs`
- 主 pass：`Runtime/GeometryBuffer/HoGeometryBufferPass.cs`
- RT：`Runtime/GeometryBuffer/HoGeometryBufferRenderTargets.cs`
- RenderGraph 资源：`Runtime/GeometryBuffer/HoGeometryBufferRenderGraphResources.cs`
- 采样函数：`Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl`
- DebugTile 注册：`Runtime/GeometryBuffer/HoGeometryBufferDebugViewInfo.cs`
- Debug shader：`Runtime/GeometryBuffer/Shaders/Debug/HoGeometryBufferDebug.shader`
- lilToon outline 模板：`D:\Unity_Fork\lilToon\Assets\lilToon\CustomShaderResources\URP\Default*Outline.lilblock`
- lilToon UsePass 模板（仅当隐藏 pass shader 提供对应 pass 时适用）：`D:\Unity_Fork\lilToon\Assets\lilToon\CustomShaderResources\URP\DefaultUsePassOutline*.lilblock`
- outline normal/depth fragment：`D:\Unity_Fork\lilToon\Assets\lilToon\Shader\Includes\lil_pass_outline_normal_depth.hlsl`

## 11. VisualSurfaceBuffer 扩展与 RT 成本

### 11.1 推荐的数据分层

```text
PhysicalGeometryBuffer
  NormalDepth / physical coverage / DepthTexture
  -> AO / GI / SSS / 物理遮挡

VisualSurfaceBuffer
  OutlineNormalDepth / OutlineColor / KindFlags
  -> DOF / motion blur / visual occlusion / 后续视觉合成
```

消费者按需求选择：

| 模式 | 深度选择 | 适用消费者 |
| --- | --- | --- |
| `PhysicalOnly` | `NormalDepth.a` | GTAO、SSGI、SSS、物理遮挡 |
| `PhysicalPlusVisualDepth` | 描边 normal/depth alpha 命中时使用 OutlineNormalDepth，否则使用 NormalDepth | DOF、motion blur、视觉景深 |
| `VisualOcclusionOnly` | VisualSurface 只参与 ray blocking，不参与 radiance/normal/energy | 需要避免屏幕空间射线穿过描边的 GI/AO 变体 |
| `VisualComposite` | 读取 OutlineColor/coverage，在后处理后重新合成 | 描边、特殊视觉壳层、风格化后处理 |

### 11.2 为什么不能直接合并进 NormalDepth

把 OutlineNormalDepth 写入 `NormalDepth.a` 会让所有现有消费者自动看到描边：

- SSGI 可能把描边当 caster、receiver 或 source；
- GTAO 会在描边壳上计算遮蔽；
- SSS/反射/角色特化会把描边误判成真实表面；
- 但这些效果仍然没有对应的物理法线、albedo、厚度和材质语义。

因此“补进主 GBuffer”应理解为建立一个可解析的 visual surface view，而不是改写 PhysicalGeometryBuffer。

### 11.3 RT 数量和带宽取舍

当前资源规模（全分辨率、无 MSAA 乘数）：

| 资源 | 常见格式 | 约每像素字节 | 1920×1080 近似显存 |
| --- | --- | ---: | ---: |
| `NormalDepth` | `R16G16B16A16_SFloat` | 8 | 15.8 MiB |
| `DepthTexture` | D24/D32 | 3-4 | 6.0-7.9 MiB |
| `OutlineNormalDepth` | `R16G16B16A16_SFloat` | 8 | 15.8 MiB |
| `CoverageTexture` + `OutlineCoverageTexture`（仅 MSAA） | R8 ×2 | 2 | 4.0 MiB |
| MSAA 阶段（仅 `msaaSamples > 1`，瞬态） | 两张 16F MSAA color + MSAA depth | (8+8+4)·N = 20N | 例如 4x = 80 B/px ≈ 158 MiB |
| `SkyTexture`（仅 `enableSkyBuffer`，否则记 0） | `R16G16B16A16_SFloat` | 8 | 15.8 MiB |

上表**不含** MSAA 阶段：它只在 output pass 期间存在，但通常是整个系统里最大的一笔带宽（已单列一行）。

分配策略现状（读代码得出，别按"懒分配"假设）：`ReAllocateIfNeeded` **无条件**分配 `NormalDepth` / `DepthTexture` / `OutlineNormalDepth`；MSAA 额外件（两张 MSAA color、MSAA depth、两张 coverage）只在 `msaaSamples > 1` 时分配，否则 `ReleaseMsaaResolveResources()` 回收；只有 sky 是按需的。

实际成本还会受到 render scale、MSAA、XR slice、RT 对齐和 RenderGraph 生命周期影响。当前 OutlineNormalDepth 与主 NormalDepth 同构，便于复用采样协议，但会比原先 R8 coverage 占用更多带宽；真正需要关注的是它是否按视觉消费者需求懒分配。

### 11.4 推荐的优化路线

1. `OutlineNormalDepth` 与主 `NormalDepth` 使用同构格式，避免消费者维护另一套采样协议。
2. 需要颜色或种类标记时继续扩展同一个 visual surface contract，避免每个效果各自创建一套 RT。
3. 只有存在 Outline、DOF、motion blur 或明确的 visual surface 消费者时，才分配 VisualSurfaceBuffer；纯 AO/GI 场景不应为它付出成本。**尚未实现**：当前除 sky 外没有按需分配，见 §11.3。
4. DebugTile 必须为每个新增 visual channel 提供视图，否则 RT 成本无法在 Frame Debugger 之外被验证。

当前实现处于第 1 阶段：已经有独立 `OutlineNormalDepthTexture`。它目前随 GeometryBuffer 主资源一起创建，后续可以根据项目中的视觉消费者登记做懒分配（目前并没有"消费者登记"这种机制，唯一的按需分配是 `enableSkyBuffer`）；normal/depth 已经在同一张视觉 RT 内打包，不再需要再增加独立 `OutlineDepth` attachment。

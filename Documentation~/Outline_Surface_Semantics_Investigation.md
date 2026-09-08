# lilToon 描边与 ScreenProcess 景深：现况调查

> 状态：调查记录，尚未提出最终实现方案。
>
> 目标：确认 ScreenProcess 景深实际消费的深度来源，追踪该来源的生产路径，并核对 lilToon 描边是否参与该路径。本文不把“描边应不应该被当作真实表面”预先当成结论。

## 1. 当前结论

调查初始状态的结论是：**景深的基础几何输入没有优先使用我们自己的 GeometryBuffer；DepthOfField 直接读取 URP 的 `_CameraDepthTexture`。**本次独立修复已将 DOF 改为消费 GeometryBuffer 线性深度，并在 GeometryBuffer 不可用时显式 no-op。

同时，lilToon 的描边外扩只存在于 forward outline pass；它没有独立的 `DepthOnly`、`DepthNormals`、`HoMetadataBuffer` 或 `HoGeometryBuffer` 描边 pass。因此，在由这些专用 pass 生成的深度/法线/语义 buffer 中，当前写入的是原始网格表面，而不是外扩后的描边表面。

这解释了“描边处景深行为异常”的一个完整候选原因，但还不能仅凭静态代码断言当前项目中的 `_CameraDepthTexture` 一定完全不含 forward outline。URP 6000/17.3 还可能根据 renderer 配置选择 depth prepass 或 copy-depth；必须用 Frame Debugger/RenderDoc 确认实际采用的分支。

## 2. 景深实际消费什么

文件：`Runtime/ScreenProcess/Shaders/ScreenProcess/DepthOfField.shader`

关键事实：

- 第 25 行包含 URP `DeclareDepthTexture.hlsl`。
- `SampleEyeDepth()` 对 `SampleSceneDepth(uv)` 做 `LinearEyeDepth`。
- `Frag()` 在第 190 行用该深度计算 CoC，再决定模糊半径和混合量。
- 模糊采样只读取 `_BlitTexture`（当前 ScreenProcess layer 的颜色输入）；没有 MetadataBuffer、GeometryBuffer 或法线采样。

因此，DepthOfField 的输入关系是：

```text
当前颜色层 (_BlitTexture) + URP 场景深度 (_CameraDepthTexture)
                                      |
                                      v
                           CoC / blur radius / blend amount
```

`ScreenProcessRuleMask.hlsl` 只负责通用 rule/debug mask 支持。现在 DepthOfField 的几何输入来自 GeometryBuffer；只有 layer 开启 `useRuleMask` 或 `debugRuleMask` 时，RenderGraph 才会额外绑定 MetadataBuffer 规则输入。

## 3. ScreenProcess 如何请求和绑定深度

文件：`Runtime/ScreenProcess/ScreenProcessRendererFeature.cs`

- `ConfigurePass()` 在存在 DepthOfField layer 时，`RequiresDepth()` 返回 true，并调用 `ConfigureInput(ScriptableRenderPassInput.Depth)`（约第 997-1011 行）。
- 因此 `_CameraDepthTexture` 的生产者不是 ScreenProcess 自己，而是 URP 内置的 depth prepass/copy-depth 基础设施。
- ScreenProcess stack 的 render pass event 是 `AfterRenderingPostProcessing`；景深在这里对颜色层做后处理。
- RenderGraph 路径虽然为每一层准备 `HoMetadataBufferRenderGraphResources` 和 `HoGeometryBufferRenderGraphResources`，但只有 EdgeLight/PostLighting/SkyTyndall/DropShadow/rule mask 等分支会设置相应的 `useRule*` 标志。DepthOfField 没有这些绑定。

### URP 17.3 的生产分支

本机 Unity 6000.0.30f1 的 URP 源码显示：

- `UniversalRenderer.cs` 根据 `ScriptableRenderPassInput.Depth` 和 renderer/camera 设置决定是否创建 `_CameraDepthTexture`。
- 深度可能来自 `DepthOnlyPass`（shader tag `DepthOnly`），也可能先渲染相机深度再由 `CopyDepthPass` 复制；copy event 会被提前到需要深度的自定义 pass 之前。
- `DepthNormalOnlyPass` 使用 `DepthNormals` / `DepthNormalsOnly` shader tag；它不是 lilToon 的 `HoGeometryBuffer` pass。
- RenderGraph 路径创建的同名资源也是 `_CameraDepthTexture`，然后由调度的 depth copy/prepass 填充。

所以需要在目标场景记录实际的 URP 分支：

1. Frame Debugger 中找到 `DepthOnly`/`DepthNormals` prepass 或 `CopyDepth`。
2. 检查该事件发生在 lilToon forward outline 绘制之前还是之后。
3. 抓取 `_CameraDepthTexture`，对比描边像素和主体表面像素的深度。

## 4. MetadataBuffer 的生产和 lilToon 输出

### 4.1 生产端

文件：`Runtime/MetadataBuffer/HoMetadataBufferPass.cs`

- 专用 draw list 只使用 `ShaderTagId("HoMetadataBuffer")`。
- 另有独立的 `HoMetadataBufferSurfaceColor` pass，用于 SurfaceColor。
- pass 会清空并写入 maskId、surfaceData、custom0、objectCustom0、objectCustom1，以及自己的深度附件；它不会把结果写回 URP 的 `_CameraDepthTexture`。
- 没有命中 `HoMetadataBuffer` tag 的对象可以走可选 fallback material，但 fallback 仍然是替代材质绘制，不会自动得到 lilToon 的描边外扩逻辑。

### 4.2 lilToon 端

以 `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\lts_o.shader` 为代表（其他 outline 变体结构相同）：

- `FORWARD` pass：`LightMode = SRPDefaultUnlit`。
- `FORWARD_OUTLINE` pass：`LightMode = UniversalForward`，并定义 `LIL_OUTLINE`。
- `HO_METADATA_BUFFER` pass：`LightMode = HoMetadataBuffer`，定义 `LIL_PASS_DEPTHNORMALS`，fragment 为 `fragMetadataBuffer`。
- `HO_METADATA_BUFFER_SURFACE_COLOR` pass：`LightMode = HoMetadataBufferSurfaceColor`。

`lil_pass_metadata_buffer.hlsl` 的 `fragMetadataBuffer()` 输出的是语义字段：

```text
maskId / surfaceData / custom0 / objectCustom0 / objectCustom1
```

它没有颜色输出，也没有法线输出。描边 pass 不会被 `HoMetadataBufferPass` 的专用 tag list 选中，因此当前 MetadataBuffer 中没有描边覆盖区。

## 5. GeometryBuffer 的生产和 lilToon 输出

### 5.1 生产端

文件：`Runtime/GeometryBuffer/HoGeometryBufferPass.cs`

- 专用 draw list 只使用 `ShaderTagId("HoGeometryBuffer")`。
- 输出 `NormalDepthTexture`（RGB 法线、A 线性深度）和独立 depth texture。
- 该 depth texture 是 GeometryBuffer 自己的附件，不是 `_CameraDepthTexture`。
- 没有命中 `HoGeometryBuffer` tag 的对象可选用 fallback shader；这同样不会自动执行 lilToon outline 顶点外扩。

### 5.2 lilToon 端

`HO_GEOMETRY_BUFFER` pass 使用 `fragGeometryBuffer()`：

- `linearDepth = LIL_TO_LINEARDEPTH(input.positionCS.z, input.positionCS.xy)`。
- 法线来自 `fd.N`，并按 `fd.facing` / `_FlipNormal` 做正反面修正。
- 返回 `half4(normalize(fd.N) * 0.5 + 0.5, linearDepth)`。

但该 pass **没有** `#define LIL_OUTLINE`。因此其 vertex 路径不会执行 `lil_vert_outline.hlsl` 中的 `lilCalcOutlinePosition()`；写入的是原始网格位置和原始/材质法线。

## 6. 描边外扩实际发生在哪里

`FORWARD_OUTLINE` pass 定义了 `LIL_OUTLINE`，`lil_common_vert.hlsl` 会包含 `lil_vert_outline.hlsl`。其中：

- `lilCalcOutlinePosition()` 按 `_OutlineWidth`、宽度 mask、顶点色、outline vector、Z bias 修改 `input.positionOS`。
- 这意味着描边是一个“由原始网格派生出的屏幕可见外壳”，不是另一个独立 Renderer/mesh。
- `FORWARD_OUTLINE` 的 raster state 使用 `_OutlineCull`、`_OutlineZWrite`、`_OutlineZTest`、Stencil 和 Blend；这些状态和基础表面的 `DepthOnly`/`DepthNormals` 状态不同。
- `DepthOnly`、`DepthNormals`、`HO_METADATA_BUFFER`、`HO_GEOMETRY_BUFFER` 各自都没有 `LIL_OUTLINE`，也没有调用 outline vertex expansion。

对 `ltsl_o.shader`（Lite outline）也观察到相同 pass 分层：forward outline 有 `LIL_OUTLINE`，而 Depth/Metadata/Geometry pass 没有。因此这不是单个完整 lilToon shader 的偶发现象，而是 outline 变体的系统性设计。

## 7. 现阶段能确认与不能确认的事

### 已确认

1. DepthOfField 采样 `_CameraDepthTexture`，不采样 MetadataBuffer/GeometryBuffer。
2. MetadataBuffer/GeometryBuffer 是独立的自定义 buffer，不能因为写入它们就自动改变 DOF 的深度输入。
3. lilToon 的描边外扩只在 forward outline 路径中发生。
4. lilToon 的 DepthOnly/DepthNormals/HoMetadataBuffer/HoGeometryBuffer 路径当前都按原始网格位置绘制，没有描边外扩。
5. GeometryBuffer 的法线消费确实有正反面处理基础，但这只对“该 pass 是否覆盖描边”和“描边法线应该如何定义”有意义；当前 pass 根本没有描边几何。

### 仍需运行时确认

1. 当前项目 Renderer Data 的 `CopyDepthMode`、Depth Priming、Depth Texture 开关和 RenderGraph/Compatibility 路径。
2. `_CameraDepthTexture` 是由 DepthOnly prepass 还是 CopyDepth 填充。
3. 如果是 CopyDepth，copy 发生时 forward outline 是否已经写入 active depth attachment，以及 outline 的 `_OutlineZWrite`/`_OutlineZTest` 实际材质值。
4. 透明 outline、cutout outline、overlay、MSAA、camera stacking 下的行为是否一致。

## 8. 为什么“直接把描边当真实表面”不是一个局部修复

如果把 outline 直接加入 GeometryBuffer/MetadataBuffer，至少会同时影响：

- DOF/景深的遮挡与 CoC 连续性（若未来改为消费 GeometryBuffer，或同步修复 `_CameraDepthTexture`）。
- GTAO/SSGI/SSS 等依赖深度、法线、coverage、厚度、曲率的效果。
- 屏幕空间 GI 的亮面/漏光：描边外壳若只有颜色没有物理一致的法线、厚度和材质语义，可能制造新的反射/间接光源。
- 轮廓颜色与表面颜色的语义冲突：MetadataBuffer 的 mask/id/flags 是对象归属，SurfaceColor 是材质颜色，GeometryBuffer 的 normal/depth 是几何解释；它们不一定都应该把描边视为同一类“真实表面”。
- 深度竞争：outline 使用独立 ZTest/ZWrite/Offset/Stencil，简单复制基础 surface 的深度规则会改变可见层次。

因此后续方案应先定义至少三种语义，而不是只加一个开关：

| 语义 | 可能的输出 | 适用问题 |
| --- | --- | --- |
| Visual-only outline | 只进颜色，不进深度/法线/语义 buffer | 保持当前 NPR 轮廓，不让 AO/GI 把轮廓当实体 |
| Occluding shell | 进深度，法线按外壳朝向翻转/重建 | 让 DOF、遮挡、阴影把轮廓当屏幕前方壳层 |
| Semantic extension | 进 mask/id/flags，几何字段按规则写入 | 让规则 mask、角色分组、SSS 等知道轮廓归属，但不一定改变物理深度 |

这三种语义可以组合，但不应默认绑定为“所有 buffer 都写”。

## 9. 建议的下一步验证顺序

1. 在一个最小场景中只保留一个带 outline 的 lilToon opaque/cutout 材质，打开 ScreenProcess DepthOfField。
2. 用 Frame Debugger 标出：基础 forward、forward outline、DepthOnly/DepthNormals、CopyDepth、ScreenProcess DOF 的顺序。
3. 导出/查看 `_CameraDepthTexture`、`HoGeometryBufferNormalDepthTexture`、`HoMetadataBufferMaskIdTexture`，分别看主体内部、轮廓外沿、背景三个区域。
4. 临时做一个实验 shader/pass：只在 GeometryBuffer 中复用 outline vertex expansion，不改 DOF；如果 GeometryBuffer debug 轮廓出现但 DOF 不变，便可直接证明“当前 DOF 不消费 GeometryBuffer”。
5. 另做一个只改变 `_CameraDepthTexture`/active depth 的实验（不改 Metadata/Geometry）；如果 DOF 轮廓恢复，问题就被定位到 URP camera-depth 生产路径。
6. 在此基础上再决定是修 URP camera depth、增加 outline-aware GeometryBuffer，还是定义 visual-only / occluding-shell / semantic-extension 三种可独立选择的契约。

## 10. 文件索引

| 责任 | 文件 |
| --- | --- |
| DOF 消费深度 | `Runtime/ScreenProcess/Shaders/ScreenProcess/DepthOfField.shader` |
| ScreenProcess 请求深度 | `Runtime/ScreenProcess/ScreenProcessRendererFeature.cs` |
| MetadataBuffer 生产 | `Runtime/MetadataBuffer/HoMetadataBufferPass.cs` |
| GeometryBuffer 生产 | `Runtime/GeometryBuffer/HoGeometryBufferPass.cs` |
| lilToon forward/outline/depth/metadata/geometry pass | `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\lts_o.shader` |
| lilToon Lite outline 对照 | `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\ltsl_o.shader` |
| Metadata 输出实现 | `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\Includes\lil_pass_metadata_buffer.hlsl` |
| Outline 顶点外扩 | `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\Includes\lil_vert_outline.hlsl` |

## 11. ScreenProcess 全效果资源审计

这一节是对整个 `ScreenProcess` feature 的资源审计。这里要区分两件事：

- `_BlitTexture` / active camera color 是效果要修改的颜色源，仍然应该来自当前相机颜色链；MetadataBuffer/GeometryBuffer 不是颜色替代品。
- 深度、法线、coverage、对象语义和天空信息应该优先来自我们自己的语义/几何 RT。当前实现只对部分 effect 做到了这一点。

### 11.1 效果矩阵

| Effect | 颜色源 | 当前几何/语义输入 | 当前主输入 | 当前缺口 |
| --- | --- | --- | --- | --- |
| `CustomMaterial` | active color | feature 不主动绑定任何语义 RT；自定义 shader 可自行依赖全局状态 | 颜色-only | 没有明确的自定义资源契约，不能假定它已经是 Metadata/Geometry-first |
| `EdgeLight` | active color | `HoGeometryBufferNormalDepthTexture` + `HoMetadataBufferMaskIdTexture`/rule channels | **Geometry + Metadata** | 缺少任一核心输入时直接不产生 edge light；不会回退到 URP depth/normals |
| `Outline` | active color | `HoGeometryBufferNormalDepthTexture`；Metadata 只在启用 rule mask 时参与区域调制 | **GeometryBuffer** | GeometryBuffer 不可用时显式 no-op，不再请求 URP camera normals/depth |
| `DropShadow` | active color | MetadataBuffer MaskId/rule channels 优先；Metadata 不可用时使用兼容 SubjectMask RT | **Metadata -> SubjectMask fallback** | SubjectMask 已接入兼容路径和 RenderGraph，但它不是 GeometryBuffer，也不覆盖 Metadata 的对象语义 |
| `DepthOfField` | active color | `HoGeometryBufferNormalDepthTexture.a`；Metadata 只有在 layer 开启 rule mask 时调制 amount | **GeometryBuffer** | coverage 无效像素按远裁剪面处理；GeometryBuffer 不可用时显式 no-op |
| `PostLighting` | active color | `HoGeometryBufferNormalDepthTexture` + MetadataBuffer MaskId/rule channels | **Geometry + Metadata** | 当前已经对齐自有 RT 优先策略；缺失输入时不做 URP 几何回退 |
| `SkyTyndall` | active color | GeometryBuffer `NormalDepth` + `SkyTexture`；Metadata 只在启用 rule mask 时参与 | **Geometry + Sky，Metadata optional** | sky buffer 是 GeometryBuffer 的附属输出；没有读取 URP camera normals/depth 作为主路径 |

### 11.2 资源依赖在 C# 中如何决定

文件：`Runtime/ScreenProcess/ScreenProcessRendererFeature.cs`、`Runtime/ScreenProcess/ScreenProcessRuntimeDiagnostics.cs`

- `RequiresDepth()` 现在只为兼容 SubjectMask fallback 的 DropShadow 请求 URP depth；Outline/DOF 不再请求 URP camera depth/normals。
- `AnalyzeRequirements()` 把 `EdgeLight`、`Outline`、`DepthOfField`、`PostLighting`、`SkyTyndall` 标为需要 GeometryBuffer normal/depth；把 `EdgeLight`、`DropShadow`、`PostLighting` 以及启用 layer rule 的效果标为需要 MetadataBuffer mask。
- `RecordRenderGraph()` 中，Geometry/Metadata 的 `TextureHandle` 只有在对应 `useRule*` 标志为 true 时才会 `UseTexture()` 并绑定为 shader global。每个 layer 的绑定是按 effect 分支执行的，不是统一的“所有 layer 先绑定一套自有 RT”。
- `ScreenProcessRuntimeDiagnostics` 现在把 Outline/DOF 的 GeometryBuffer 依赖纳入 `RequiresGeometryBuffer`；SubjectMask fallback 仍属于 DropShadow 的内部兼容输入。

### 11.3 当前实现与目标设计的对齐程度

结合 `Documentation~/PostProcessing/README.md`、GTAO/SSGI 设计文档，当前项目已经有一个更明确的总体方向：

```text
MetadataBuffer = 对象/材质/分组语义真值
GeometryBuffer = 屏幕几何（normal/depth/coverage）真值
CameraColor    = 已渲染颜色源，不负责替代语义/几何真值
```

按这个方向，现有 ScreenProcess 可以分为三类：

1. **已对齐自有输入**：EdgeLight、Outline、DepthOfField、PostLighting、SkyTyndall。
2. **部分对齐**：DropShadow 使用 MetadataBuffer，并有兼容 SubjectMask fallback；CustomMaterial 没有 feature 级资源契约。
3. **明确保留为外部颜色输入**：所有效果仍对 active camera color 做 blit，不把 Metadata/Geometry 当成颜色替代品。

另外，DropShadow 的资源契约已经在本次修复中明确为“MetadataBuffer 优先、SubjectMask fallback”。这个问题与“优先使用自有 RT”直接相关：资源契约必须同时落实到 `UseTexture`、global binding 和 shader sample，不能只停留在字段名或设计意图。

这也意味着上一节关于描边的判断要分层理解：

- 对 `ScreenProcess.Outline` 和 `ScreenProcess.DepthOfField`，本次修复已改为消费 GeometryBuffer，因此可以继承“描边 coverage 无效”的自有几何真值策略。
- 如果后续再次引入 URP camera depth/normals fallback，必须把它做成显式 provider 选择，不能隐式覆盖 GeometryBuffer。
- 对未来 SSGI/GTAO，现有架构文档已经把 GeometryBuffer 定义为唯一几何真值，并明确描边 coverage 应保持无效。把描边无条件加入 GeometryBuffer 会与这条设计冲突，而不是单纯“补齐数据”。

### 11.4 应优先确定的资源策略

如果“优先吃我们自己的输出”作为 ScreenProcess 的正式设计原则，建议先固定下面的契约，再改 shader：

1. `GeometryBuffer.NormalDepth.a` 是所有需要几何深度的 ScreenProcess effect 的首选线性 eye depth；`NormalDepth.rgb` 是首选世界法线；alpha/coverage 为 0 表示不参与几何消费。
2. `MetadataBuffer.MaskId` 和 rule channels 是所有对象/材质选择的首选语义输入；缺失时应有明确的 no-op 或降级策略，不能静默混用不一致的 camera RT。
3. `HoGeometryBufferDepthTexture` 是 GeometryBuffer 内部/需要硬 ZTest 的独立深度附件；它不应被误认为 `_CameraDepthTexture` 的别名。
4. URP camera depth/normals 只作为显式兼容回退，至少需要在 diagnostics 中显示“当前使用的是自有 RT 还是 URP RT”。
5. `CameraColor` 仍保留为颜色源；“自有 RT 优先”不意味着用 Metadata/Geometry 去替换最终颜色。

按此契约，下一轮实现讨论的顺序应是：

```text
GeometryBuffer coverage/normal/depth 契约
  -> ScreenProcess effect 资源选择（自有 RT first）
  -> 缺失输入的显式降级/诊断
  -> 描边作为 visual-only / invalid-geometry 的处理
  -> 最后才讨论是否让某些 effect 把描边当 occluding shell
```

### 11.5 需要单独登记的实现不一致

#### DropShadow SubjectMask 路径

此前审计发现的死路径已在本次修复中接通：

- `EnsureSubjectMaskMaterial()` 在兼容路径和 RenderGraph 路径都会按 active DropShadow layer 初始化。
- 兼容路径分配并绘制 SubjectMask RT；RenderGraph 路径新增 SubjectMask renderer list 和纹理依赖。
- `DropShadow.shader` 首先采样 MetadataBuffer；只有 `_HoMetadataBufferActive` 无效时才采样 `_lilScreenProcessSubjectMaskTexture`。
- ScreenProcess stack 结束时会清理 SubjectMask global 和 valid flag，避免泄漏到 ImageProcess。

因此当前 DropShadow 的真实行为是：**MetadataBuffer rule mask 优先，SubjectMask 只作为显式 fallback**。SubjectMask 仍然不等于 GeometryBuffer，也不承载 MetadataBuffer 的 ID、厚度、曲率等字段。

#### 诊断覆盖不完整

`ScreenProcessRuntimeDiagnostics` 能报告自有 Metadata/Geometry/Sky 资源，但不报告：

- `CustomMaterial` 自己声明的隐式全局资源；
- DropShadow 的 SubjectMask fallback 是否可用。

所以后续如果把自有 RT 设为优先，诊断结构也应从“Metadata/Geometry 是否存在”升级为“每个 effect 实际选用了哪一个 resource provider”。

## 12. OutlineCoverage 独立通道

本次后续修复采用独立的 `_HoGeometryBufferOutlineCoverageTexture`，而不是把描边塞进现有 `NormalDepth.a`：

- `NormalDepth.a` 继续表示真实几何的线性深度/physical coverage，描边仍保持无效；
- lilToon outline 的 `.lilblock` 模板直接声明 `HoGeometryBufferOutlineCoverage` pass，只写外扩描边的 R8 mask；
- GeometryBuffer 额外输出 outline coverage，ScreenProcess DOF 用它保护描边中心像素，并拒绝描边颜色样本参与主体模糊；
- DebugTile/GeometryBuffer Debug 增加 `geometry.outline-coverage` 视图，用来直接确认描边 pass 是否命中；
- SSGI/GTAO 不消费该视觉 mask，因此不会把描边重新解释为物理表面。

这把“描边是否可见”和“描边是否是真实几何”拆成了两个独立问题：前者由 OutlineCoverage 处理，后者继续由 GeometryBuffer coverage 约束。

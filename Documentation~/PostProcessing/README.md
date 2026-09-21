> **状态：现行架构总览**（2026 文档审核时按 R6/R7 之后的实现校正：MetadataBuffer 已整块删除，语义与数值由 OB / SB / AC 提供）。
> 各 producer 的细节见 `Documentation~/架构优化/Ho-ObjectBuffer.md` / `Ho-SurfaceBuffer.md` / `Ho-AttributeComposite.md` 与 `Documentation~/GeometryBuffer.md`。

# PostProcessing 当前架构

本文档替代本目录原先的 HoAOV、HoPost、ShoostStack 和早期 ShadowCast 资料。以下内容基于 2026-05-27 对 `Runtime`、`Editor/PostProcessing` 和当前工作树状态的源码核对。

## 当前完成状态

已落地的运行时模块：

- `ImageProcess`：Volume 驱动的图像域后处理栈，支持 RenderGraph 和兼容路径，当前效果枚举覆盖 50 个以上图像效果，其中 `RemovedEffectSlot*` 只作为旧序列化槽位保留。
- `ScreenProcess`：Volume 驱动的语义屏幕效果栈，当前效果为 `CustomMaterial`、`EdgeLight`、`Outline`、`DropShadow`、`DepthOfField`、`PostLighting`、`SkyTyndall`、`DepthFog`。它的遮罩读角色覆盖率（AC 总覆盖率 / OB 身份池），其余输入是 SB 的材质数值、GeometryBuffer 与可选 Sky buffer。`DepthFog`（深度雾/高度雾，家族 C）是**一个效果里的两个槽**（各带开关，可同时开），见 `DepthFog.md`。
- `ObjectBuffer`：逐样本身份池（16 bit `(组, 槽位)`）→ 自己 resolve 出最多 4 层身份 + 每层覆盖率；对象侧用 `Ho-ObjectBuffer Group` 组件写身份与标签。
- `SurfaceBuffer`：五张表面数值图（Color / Normal / Material / Reflection / Classification）+ internal owner，由材质侧 `HO_SURFACE_BUFFER` pass 一趟写全。
- `AttributeComposite`：语义遮罩与合成属性的唯一逻辑入口 —— 读 OB + SB，按 `HoSemanticSchema` 解压出 Selection 池（每张两条 `(SemanticId, coverage)`）供消费者查询。
- `GeometryBuffer`：输出 normal/depth 缓冲，当前还增加了可选 Sky buffer 捕获，供 `SkyTyndall` 等 ScreenProcess 效果使用。
- `CharacterSpecialization`：角色特化合成已迁移到独立 RendererFeature 和 Volume，包含眼睛透过、前发投影、角色捕获 RT 和调试输出。
- `ShadowCast`：独立 RendererFeature，包含可见光采集、自定义阴影 atlas、第二方向光级联、PCSS 参数、运行时发布和调试视图。

已落地的编辑器侧能力：

- `ImageProcessStackVolumeEditor` 和 `ScreenProcessStackVolumeEditor` 负责层列表、图标按钮、预设菜单、每个效果的参数 UI。
- **效果浏览器**（两个编辑器共用）：顶栏搜索（只按中文标签与枚举名匹配，样式开关在最左、输入框占满其余宽度）+ 左侧可翻页图标侧栏（两档样式同一个侧栏宽度与同样的 20 行高度：默认纯图标 3×20 = 60 格/页、41 个效果一页放下，可切图标+名字 1×20 = 20 格/页；开关/清空/翻页/行内移除全是无底按钮，不够的格子留白不缩）+ 右侧原有图层列表（搜索只高亮命中行，不过滤不重排）；图标右键有添加/移除/重置菜单，图层行有 `×` 移除。见 `EffectBrowser.md`。
- `调色`（`ColorGradingCustom`）的预设根级只有 `默认`（重置），其余 34 个 look 统一走 `基础/电影感/胶片/动画/风格` 五个子菜单，见 `ColorGradingPresets.md`。
- `渐变`（`Gradient`）除原有 4 种形状外新增 4 个两点模式（线性/径向/椭圆/锥形，旋转靠拖 B 点）、过渡曲线、镜像（反向渐变）、线性光插值、分辨率量化与输出抖动的暴露，见 `GradientInvestigation.md`。
- `渐变映射`（`GradientMap`，新增）是亮度/通道驱动的颜色映射（Photoshop Gradient Map 那一类）：色标用 **Unity 原生 `Gradient`**（≤8 颜色键 + ≤8 透明度键，Blend/Fixed），运行时烘焙成 1×256 的 ramp 贴图，shader 只做一次采样；另有输入窗口、反转、色阶数（平涂）、显示空间/线性光/Oklab 烘焙空间、输出抖动，以及 5 组 16 个 look（含 matplotlib/Google turbo/FLIR 风格色表采样）。见 `GradientMap.md`。
- `深度雾`（`DepthFog`，新增）是 ScreenProcess 里的合成雾：一个效果带**深度雾**与**高度雾**两个槽（各带开关，可同时开，两层在同一趟 pass 内按顺序合成）。深度项支持直线/指数/指数平方三种距离曲线与远近双色 + 空气感去饱和；高度项支持"高度窗／指数衰减 × 下方浓／上方浓"四种形式，高度取世界 Y（可切相机相对）并用包内既有的 smoothness + hardness 习惯；天空可跳过／一起上雾／单独染色；不依赖 GeometryBuffer 也能工作（自动回落相机深度，正交也走这条路）。预设 5 组 13 个，见 `DepthFog.md`。
- `网点`（`Halftone`，新增）是 ImageProcess 的半调网屏：模式有拜耳有序抖动（2/3/4/8 矩阵）与圆点/方点/菱形/线条四种面积调制网点，配色是「墨色（图层颜色）+ 纸色」两种颜色、四种合成（叠墨／双色／乘算／遮罩原色），另有输入窗口、暗部上墨、角度、柔化、网格抖动、浓度上限与输出抖动。设计上与 `渐变映射` 串联使用（ramp 管颜色、网点管墨量），也能单走。预设 5 组 15 个，见 `Halftone.md`。
- `Editor/PostProcessing/ViewControls` 提供屏幕空间中心、半径、方向等 SceneView 操作控件。
- `ScreenProcessRuleMaskEditorUtility`（规则遮罩编辑器）已删除：20 个 rule source 与 ≤4 条规则列表作为**无人使用**的功能移除。层遮罩保留——见下面的「层遮罩」。

当前仍需注意的状态：

- 规划中（**尚未实现**）：`角色特化`（`HoCharacterSpecialization`）的**图像链化（v2）** —— capture 序留在 Feature、
  效果变成固定顺序的链层、资源绑定按冻结的 `HoAC_*`，等到 AC（`Ho-AttributeComposite`）R5 一起做。
  v1（配置模型 + 与另外两块对齐的 UI：搜索栏 + 左侧图标侧栏 + 统一行样式，这块**没有图层也没有顺序**，
  5 个区段固定、只有启用开关）**已落地**，见 `CharacterSpecializationBrowser.md`。
- `Tests/Runtime` 目录为空，源码中也未检索到 `[Test]` 或 `[UnityTest]`；画面验证仍要人工做，静态检查只能保证"树没坏"。
- 包目录没有 `.sln` / `.csproj`，Unity 侧编译以编辑器为准；本仓另有三套 Roslyn 静态闸门可离线跑：
  `.codex-research/check_compile.ps1`（extensions Runtime + Editor）、`check_compile_liltoon.ps1`（lilToon.Editor）、
  `check_shaders.ps1`（HLSL include 解析与调用可达性）。
- 本文按 R6/R7 之后的实现核对（MetadataBuffer 已整块删除）；包内文档写作时间跨度大，具体细节以代码为准。
- 部分 Inspector 文本在源码里已经出现编码乱码，这不影响架构判断，但会影响编辑器显示质量，后续应单独修复。

## 共享图层混合表（IP / SP）

`ImageProcess` 与 `ScreenProcess` 的图层混合模式现在是**同一张表、同一套编号**：

- 实现：`Runtime/ImageProcess/Shaders/ImageProcess/ImageProcessBlend.hlsl`（文件名是历史遗留；includ 一律用包内绝对路径，
  两边都 include 它）。24 个模式，0 正常 / 1 相加 / 2 正片叠底 / 3 滤色 / 4 变暗 / 5 颜色加深 / 6 线性加深 / 7 变亮 /
  8 颜色减淡 / 9 叠加 / 10 柔光 / 11 强光 / 12 亮光 / 13 线性光 / 14 点光 / 15 实色混合 / 16 差值 / 17 排除 /
  18 减去 / 19 划分 / 20 色相 / 21 饱和度 / 22 颜色 / 23 明度（Hue/Saturation/Color/Luminosity 是不可分离模式，
  只有着色器实现；`ScreenProcessFogMath.BlendChannel` 的 CPU 镜像只覆盖可分离的 0..19）。
- 用它的地方：IP 的 `Gradient`/`GradientMap`/`LayerBlit`/`Halftone`，SP 的 `Outline`/`EdgeLight`/`PostLighting`/
  `SkyTyndall`/`DepthFog`。C# 侧是 `ScreenProcessBlendMode`（24 个成员，带 `[InspectorName]` 中文标签，
  序号即 `_LayerBlendMode` 的值）。
- `SkyTyndall` 的滤色需要 HDR 版本（云/神光的底色可能 > 1），走 `ApplyLayerBlendHdr`：只对 LDR 部分做 screen，
  再把底色超出 1 的部分加回去；其余 23 个模式与 `ApplyLayerBlend` 相同。
- **SP 的编号变过一次**：它以前只有 4 个模式，且 2/3 与 IP 相反（旧 2=滤色、旧 3=正片叠底）。统一时**没有做数据迁移**，
  所以旧资产里值为 2/3 的 SP 图层语义会跟着新表变；代码里一律用枚举名，不受影响。
- 检查：`.codex-research/shader-check/check_blend_include.js`（两边都必须 include、不得再有本地副本，
  6 条负对照每次运行都跑）+ `.codex-research/sp_blend_sim/check_sp_blend_parity.js`（把 HLSL 文本解析出来逐模式求值，
  验证重编号前后每个模式的数学没变、C# 镜像与着色器一致、枚举序号与中文标签跟 IP 面板逐项对齐）。

## 总体渲染顺序

典型相机帧里的关系如下：

1. `HoShadowCastRendererFeature` 在 `BeforeRenderingPrePasses` 默认时机生成自定义阴影数据，并通过全局纹理、矩阵和参数发布给材质侧使用。
2. `Ho-ObjectBuffer` / `Ho-SurfaceBuffer`（以及 `Ho-GeometryBuffer`）默认都在 `BeforeRenderingOpaques` 输出身份、表面数值与几何；`Ho-AttributeComposite` 紧随其后把身份 + 标签解压成 Selection 池 —— 消费者按自己的 pass 时机读，不要求它更晚。
3. `HoGeometryBufferRendererFeature` 默认也是在 `BeforeRenderingOpaques` 输出 normal/depth；启用 Sky buffer 时，`HoGeometryBufferSkyPass` 在 `AfterRenderingSkybox` 默认时机从当前 camera color 捕获天空信息。
4. `HoCharacterSpecializationRendererFeature` 在 `AfterRenderingTransparents` 默认时机执行角色捕获和合成。
5. URP 原生后处理执行。
6. `ScreenProcessRendererFeature` 在 `AfterRenderingPostProcessing` 执行语义屏幕效果。
7. `ImageProcessRendererFeature` 在 `AfterRenderingPostProcessing + 1` 执行最终图像域效果。

`ScreenProcess` 必须早于 `ImageProcess`，因为前者负责需要场景语义的效果，后者负责最终图像风格叠加。

## 语义缓冲层

屏幕效果真正吃的语义由三个 producer 加一个合成器提供（几何见下一节）：

- **`ObjectBuffer`（身份与覆盖率）**：`Runtime/ObjectBuffer/HoObjectBufferRendererFeature.cs` → `HoObjectBufferPass`（自建 MSAA：逐样本写 16 bit 身份，再 resolve 出最多 4 层身份 + 每层覆盖率）。对象侧是 `Ho-ObjectBuffer Group` 组件（`HoObjectBufferGroup`：部件行表、标签位掩码、朝向参考系），把 `(组, 槽位)` 写进 `unity_RendererUserValue`。
- **`SurfaceBuffer`（表面数值）**：`Runtime/SurfaceBuffer/`，材质侧 `HO_SURFACE_BUFFER` pass 一次写五张图 + internal owner。
- **`AttributeComposite`（合成与查询）**：`Runtime/AttributeComposite/`，读 OB 身份池 + 部件标签，按 `HoSemanticSchema` 写 Selection 池；消费者只经 `HoAC_*` 查询，不自己解码 packing。

`GeometryBuffer` 的职责是提供屏幕空间法线、线性深度和可选天空缓存：

- RendererFeature：`Runtime/GeometryBuffer/HoGeometryBufferRendererFeature.cs`
- normal/depth 输出：`HoGeometryBufferPass`
- sky 输出：`HoGeometryBufferSkyPass`
- RenderGraph 资源：`HoGeometryBufferRenderGraphResources`
- 全局纹理：`_HoGeometryBufferNormalDepthTexture`、`_HoGeometryBufferDepthTexture`、`_HoGeometryBufferSkyTexture`

需要天空体积光或天空射线类效果时，必须启用 `HoGeometryBufferSettings.enableSkyBuffer`，否则 `SkyTyndall` 会因为缺少 `_HoGeometryBufferSkyTexture` 而只返回原图。

## ScreenProcess

`ScreenProcess` 是语义屏幕效果栈，入口文件为：

- `Runtime/ScreenProcess/ScreenProcessRendererFeature.cs`
- `Runtime/ScreenProcess/ScreenProcessStackVolume.cs`
- `Runtime/ScreenProcess/ScreenProcessLayer.cs`
- `Runtime/ScreenProcess/Shaders/ScreenProcess`
- `Editor/PostProcessing/ScreenProcess`

RendererFeature 只安装渲染 Pass，实际层配置来自 Volume。相机类型限制为 Game 和 SceneView；SceneView 还受 Volume 上 `ShowInSceneView` 控制。

每个 `ScreenProcessLayer` 包含：

- `effect`、`materialOverride`、`shaderOverride`、`passIndex`
- `intensity`、`blendMode`、`color`、`texture`
- `parameters0` 到 `parameters5`
- DepthOfField 的场景焦点目标和路径回退
- 层遮罩开关/反转/调试：`useMask`、`invertMask`、`debugMask`

层遮罩采样的是**角色覆盖率**（AC 的总覆盖率，来源是 OB 的四层身份覆盖率），配一个每层开关、一个反转和一个 debug 直出（`_LayerMaskDebugOutput`）；开关关闭时该层不做遮罩（乘 1），覆盖率来源不可用时乘 0。遮罩纹理的 texel / 尺寸由 C# 显式发布（`_lilHoSPMaskTexelSize` —— 全局纹理没有 `_TexelSize`，早先读它导致"按像素扩张 / 羽化"的半径恒为 0）。**规则来源（20 个 rule source、≤4 条规则列表）已作为未使用功能删除**；"按语义名选遮罩"仍是后续的新工作，不是迁移。

删除时顺带修掉两个意外（都发生在"没配任何规则"这条路径上）：旧实现即使没配规则也会合成一条 Direct/Mask 规则，
于是覆盖率在规则级和出口各乘一次、被连乘三次（`coverage³`）；现在就是 `coverage`。
另外强制路径（`ResolveCoverageMask`）在层开关关闭时会提前返回、跳过反转，现在会照常反转。
二值遮罩（0/1）且不反转时与旧结果逐位一致；只有覆盖率处于中间值（抗锯齿边缘）时结果不同。
本仓库当前没有任何 SP 图层开着遮罩（工程数据核对过），所以这次改动对现有画面不可见。

当前资源依赖：

- `EdgeLight`：需要角色全覆盖率与 GeometryBuffer normal/depth。
- `Outline`：优先需要 GeometryBuffer normal/depth；GeometryBuffer coverage 为 0 的像素不参与边缘检测。
- `DropShadow`：优先需要角色覆盖率；覆盖率不可用时兼容路径和 RenderGraph 使用内部 SubjectMask fallback。
- `DepthOfField`：需要 GeometryBuffer 线性深度；coverage 无效时按远裁剪面处理，支持固定焦距和 Transform 目标焦点。
- `PostLighting`：需要角色覆盖率与 GeometryBuffer normal/depth。
- `SkyTyndall`：需要 GeometryBuffer normal/depth 和 Sky buffer；启用层遮罩时还需要角色覆盖率。
- `CustomMaterial`：默认只做 layer blit，按用户材质或 shader 扩展。

`ScreenProcessRuntimeDiagnostics.CurrentSnapshot` 会记录 active layer 数、写入 layer 数、back buffer 状态、camera color 状态、角色覆盖率 / GeometryBuffer / SkyTexture 是否满足等信息（面板行是 `Coverage (AC/OB)`）。调试面板应该优先读这个 snapshot，而不是猜测缺哪个 RendererFeature。

RenderGraph 路径有一个刻意保留的小收尾 pass：`ScreenProcessRendererFeature` 在 ScreenProcess stack 后立即入队 `Ho-ScreenProcess Release Semantic Buffers`。它用 `SetGlobalTextureAfterPass` 把 GeometryBuffer 与 Sky buffer 的 shader global 改回 RenderGraph black texture，并把本 feature 的 `_lilHoSPMaskValid` / `_SubjectMaskValid` 清为 0。这样 ImageProcess 仍然只看到 camera color，不会因为上一阶段的全局绑定在 RenderDoc 里表现为继续持有 G/MBuffer。这个 pass 只解绑 global，不代表提前销毁资源；如果 DebugTile 或其他后续 pass 显式 `UseTexture` 读取这些 RenderGraph 资源，资源生命周期仍会延长到真实最后消费者。

## ImageProcess

`ImageProcess` 是最终图像域后处理栈，入口文件为：

- `Runtime/ImageProcess/ImageProcessRendererFeature.cs`
- `Runtime/ImageProcess/ImageProcessStackVolume.cs`
- `Runtime/ImageProcess/ImageProcessLayer.cs`
- `Runtime/ImageProcess/Renderer/ImageProcessPass.cs`
- `Runtime/ImageProcess/Renderer/EffectPipeline/ImageProcessPass.EffectDispatch.cs`
- `Runtime/ImageProcess/Shaders/ImageProcess`
- `Editor/PostProcessing/ImageProcess`

它不消费 OB / SB / AC 的产物，也不消费 GeometryBuffer，只处理 camera color。RendererFeature 从 Volume 生成运行时层列表，`ImageProcessPass` 用 ping-pong texture 或 RenderGraph `ImageProcessChain` 串起每个效果。最终结果回写到 camera color。

效果执行分类来自 `ImageProcessEffectDescriptor`：

- `SinglePass`：普通 fullscreen blit，例如 ColorGrading、Vignette、Pixelize、Kuwahara、CinematicBars。
- `MultiPass`：需要局部 ping-pong 或模糊链，例如 IrisBlur、RGBBlurV2、Glow、ApertureBokeh。
- `Stateful`：需要跨帧状态，例如 ChangeFrameRate。
- `Removed`：保留旧序列化槽位，不再执行。

`ImageProcessLayer` 的通用数据包括 `effect`、材质或 shader 覆盖、pass index、intensity、blend mode、color、主 texture、LogoOverlay 的 8 个纹理槽，以及 `parameters0` 到 `parameters12`。新增效果时优先复用这些参数槽，只有确实需要独立资源时再扩展 layer 数据结构。

`Glass / 玻璃` 是 camera color 上的单 pass 毛玻璃效果。它以 SDF 描述矩形、圆角矩形和正多边形，支持中心位置、宽高和旋转；边缘带可选沿 SDF 法线与切线混合方向置换背景，并加入随边缘方向变化的柔和高光；边缘颜色从玻璃主色逐步调制到“玻璃主色乘以原图颜色”。模糊采用圆盘采样核，低/中/高质量分别为 9/17/25 tap，避免四向十字纹理。Inspector 提供 GameView 手柄，可拖动中心、宽度、高度和旋转。其参数槽约定为：`parameters0 = (中心 X, 中心 Y, 宽, 高)`，`parameters1 = (旋转角, 形状, 圆角半径, 多边形边数)`，`parameters2 = (模糊像素, 质量, 边缘宽度像素, 边缘柔化像素)`，`parameters3 = (边缘置换开关, 置换像素, 边缘主色混合, 玻璃不透明度)`。

`GradientMap / 渐变映射` 是单 pass 的亮度/通道驱动颜色映射。色标是图层上的 `ramp` 字段（Unity `Gradient`，≤8 颜色键 + ≤8 透明度键，`Blend`/`Fixed`），渲染时由 `ImageProcessGradientRampCache` 按内容哈希烘焙成 1×256 的 `Texture2D`（`RGBAHalf`，`Blend` 用双线性、`Fixed` 用点采样）并以 `_LayerRampTex` + `_LayerRampTexelSize` 绑定；`ImageProcessGradientRampBaker` 是不依赖 UnityEngine 的纯烘焙核心（因此能在 `dotnet` 里被直接执行检查）。标量槽位：`parameters0 = (输入, 黑场, 白场, 抖动)`，`parameters6 = (插值空间, 反转, 色阶数, 预留)`；`parameters1..parameters5` 已归还为预留（旧版本曾用它们放 4 色标）。默认是黑白渐变 + `正常` 混合，所以新加的层等于"亮度转灰度"；预设根级只有 `默认`，其余 16 个 look 走 `双色调/胶片/风格/数据可视化` 四个子菜单。细节、来源与验证见 `GradientMap.md`。

## CharacterSpecialization

`CharacterSpecialization` 已不再作为早期 HoAOV 的一部分维护，而是独立 RendererFeature：

- `Runtime/CharacterSpecialization/HoCharacterSpecializationRendererFeature.cs`
- `Runtime/CharacterSpecialization/HoCharacterSpecializationVolume.cs`
- `Runtime/CharacterSpecialization/Shaders/HoCharacterCaptureCommon.hlsl`

它支持 Volume 覆盖，执行内容包括：

- 使用 `LightMode = HoCharacterCapture` 的材质 pass 捕获眼睛/脸部数据。
- 语义只经 AC 的 Selection 池查询（`HoAC_Selection` / `HoAC_Predicate` / `HoAC_Layer0Group`）：角色特化把它转置成自己的两张位平面，不再自己解码 OB 身份池与部件行表，也不再读任何 bit 掩码。
- 读取 GeometryBuffer normal/depth 辅助前发投影距离、深度、轮廓高度渐隐和遮罩判断。
- 合成眼睛透过、前发投影、脸色扩散、主体轮廓、增强轮廓，或输出调试视图。
- 眼睛透过支持相机角度修正：在 `Ho-ObjectBuffer Group`（`HoObjectBufferGroup`）上指定“面部朝向”（一个 Transform，骨骼或朝向正确的空物体均可；+Z 为脸前、+X 为角色右、+Y 为上，轴向可在组件上配置），CPU 每帧按相机与该朝向的平转/俯仰角写入 256×1 查询表，Composite 中按角色 ID 采样并衰减眼透不透明度。开关与平转/俯仰半角范围、柔化、强度由 Volume/Feature 全局控制（默认关闭）。

材质接入点保留在 shader include 中：角色捕获 pass 应调用 `LilHoCharacterBuildCaptureOutput`，并让材质自己的 alpha、cutout、dissolve 规则决定是否写入捕获 RT。

## ShadowCast

`ShadowCast` 是独立的自定义阴影发布系统，不再和后处理文档里的旧 HoAOV 计划绑定：

- `Runtime/ShadowCast/HoShadowCastRendererFeature.cs`
- `Runtime/ShadowCast/HoShadowCastPass.cs`
- `Runtime/ShadowCast/HoShadowCastFrameCollector.cs`
- `Runtime/ShadowCast/HoShadowCastPublisher.cs`
- `Runtime/ShadowCast/HoShadowCastShaderContract.cs` + `Runtime/ShadowCast/Shaders/HoShadowCastShaderContract.hlsl`（C#/HLSL 数值契约）

核心流程：

- 从 URP visible lights 或配置中收集灯光。
- 按方向光、点光、聚光生成 shadow slice，并装入 atlas。
- 第二方向光使用独立 atlas 和级联参数。
- PCSS 参数按 punctual 和 second directional 分开发布。
- `HoShadowCastPublisher` 在每个 camera 开始时 reset，在 pass 结束后发布全局阴影贴图、矩阵、灯光数据和调试数据。

### 容量档（Light Capacity）

容量分成两个互相独立的量：**档位决定"同时采样多少盏附加灯"**（逐像素代价），**图集几何决定"能放下多少片切片"**（进而决定能投影多少盏点光）。

| 档位 | 附加灯上限（采样/循环） |
| --- | --- |
| Low（默认） | 12 |
| Medium | 24 |
| High | 48 |

切片数不按档位写死，而是由图集尺寸与分辨率算出来：

- 切片是正方形，按行装箱，所以单一光类型的容量就是 `floor(atlasSize / resolution)^2`，并被固定数组上限 `HO_SHADOW_CAST_ARRAY_SLICES`（128 片）截断；混合聚光/点光的场景由 `HoShadowCastAtlasPacker` 在实际装箱时决定。
- 分辨率是权威值：收集端不再为了让"某个切片预算"塞进去而下调分辨率；想容纳更多灯就下调 `Spot/Point Face Resolution`，想更清晰就上调，切片数会自动跟着变。容量不足时诊断里区分两种原因：`atlas is full at Npx`（几何放不下）与 `slice array limit reached (128 …)`（硬数组上限）。
- 编辑器"容量"面板会按当前 `Atlas Size` / `Spot Resolution` / `Point Face Resolution` 实时显示算出的切片数与约合点光灯数。
- 被拒绝的灯会归还它是试装箱占用的图集空间，不会留下空洞。

其他约束：

- 聚光 1 片/盏、点光 6 片/盏；第二方向光是独立配额（4 灯 × 4 级联 = 16 片）。
- 档位通过全局 keyword（`HO_SHADOW_CAST_CAPACITY_MEDIUM` / `HO_SHADOW_CAST_CAPACITY_HIGH`，Low 不带 keyword）选择材质侧变体；keyword 声明在 `HoShadowCastSampling.hlsl`。`HoShadowCastPublisher.ApplyCapacityKeywords` 与收集逻辑读同一份 config，保证"收集上限"和"shader 循环上限"始终一致。
- **全局数组长度固定**（`HO_SHADOW_CAST_ARRAY_LIGHTS`/`_SLICES` = 48/128），publisher 每次上传固定长度数组。原因：Unity 会在会话内缓存全局数组槽位的长度，且之后只允许变小（`Property (_HoShadowCastLightData0) exceeds previous array size (48 vs 12). Cap to previous size. Restart Unity to recreate the arrays.`）。因此档位只约束收集与采样循环，不改变数组布局——这也让运行时切换档位变得安全（不需要重启、不会被静默截断）。注意：**调大**数组长度需要重启一次 Unity 编辑器（已分配的槽位无法变大），调小不需要；改动时 `HoShadowCastShaderContract.cs` 与 hlsl 必须同步，校验器会检查每个档位不超过数组长度。
- 固定数组的代价是常驻全局常量缓冲：约 15KB（48 灯 × 5 + 128 切片 × 5 个 float4，外加第二方向光）。若要面向 GLES3 等 UBO 上限 16KB 的平台，应同时下调 `HO_SHADOW_CAST_ARRAY_*` 与对应档位值（两侧一起改，校验器会守住一致性）。
- `HoShadowCastShaderContract.hlsl` 是 C#/HLSL 的唯一数值契约（数组长度、档位、第二方向光容量、PCSS 采样上限、灯光类型 id）。`Editor/ShadowCast/HoShadowCastShaderContractValidator.cs` 在编辑器加载时（以及 `Tools/lilToon URP Extensions/Validate ShadowCast Shader Contract` 菜单）解析该文件并与 C# 侧比对，出现漂移会报错；它还覆盖两个调试 shader（`HoShadowCastDebug.shader`、`HoDebugTile.shader`），要求它们引用契约、不得写死数组长度、不得依赖档位宏。
- 档位 keyword 由两处应用：`HoShadowCastRendererFeature.AddRenderPasses`（immediate，保证 pass 未入队时状态也正确）和 pass 自己的命令缓冲（与 atlas 发布同序，多相机/多档位时每个相机都用自己的档位）。
- 调试视图不依赖档位：按运行时计数和固定数组长度绘制，因此不需要 tier keyword。

## 调试与诊断

当前各模块都有运行时诊断或调试视图：

- `ImageProcessRuntimeDiagnostics`
- `ScreenProcessRuntimeDiagnostics`
- `HoGeometryBufferDebugPass`
- `HoShadowCastDebugPass`
- `HoCharacterSpecializationRuntimeDiagnostics`
- `Runtime/Debug/HoDebugTileRendererFeature.cs`

排查顺序建议：

1. 先确认对应 RendererFeature 是否启用，并且目标相机是 Game 或允许 SceneView。
2. 再看 Volume 是否 active，layer 是否 enabled 且 intensity 大于阈值。
3. 对 `ScreenProcess`，检查诊断 snapshot 中角色覆盖率 / GeometryBuffer / SkyTexture 的可用性。
4. 对 `ImageProcess`，检查 active layer 数、back buffer 状态和 camera color 是否可用。
5. 对 `CharacterSpecialization`，检查 capture shader tag、AC 的 Selection 池（OB 是否在 renderer 里）和 GeometryBuffer 输入。

## 新增效果接入规则

新增 `ScreenProcess` 效果时：

1. 在 `ScreenProcessEffect` 增加枚举值。
2. 在 `ScreenProcessShaderConstants` 和 `ScreenProcessEffectRegistry` 注册默认 shader。
3. 在 `ScreenProcessRendererFeature` 中声明资源依赖，并在 RenderGraph pass 中绑定需要的覆盖率（OB）、GeometryBuffer 或 Sky texture。
4. 在 `Editor/PostProcessing/ScreenProcess/Filters` 添加参数 UI。
5. 在 `ScreenProcessStackVolumeEditor` 和 presets 文件中加入图标、默认值和预设。
6. 在 `Runtime/ScreenProcess/Shaders/ScreenProcess` 添加 shader。
7. 不要在任何地方对效果下标写死上界（曾经 `GetEffect()` 里是 `Mathf.Clamp(value, 0, 6)`，`DepthFog = 7` 加进来后深度雾图层读回来变成 `SkyTyndall`，图标按钮每点一次就多加一层）。用 `Enum.GetValues(typeof(ScreenProcessEffect)).Length`，并跑 `.codex-research/effect_enum_check/check_effect_enum_coverage.js`（负对照会验证它确实能失败）：它检查图标面板、六个 `switch`、registry→shader 文件、以及"字面量夹枚举下标"这四类。
8. shader 里自己声明的 uniform 必须真的声明：`.codex-research/shader-check/check_all.ps1` 只给"被丢掉的 URP/core include 本该提供的东西"打桩，shader 自己的 uniform（如 `_HoGeometryBufferValid`）不代劳，否则本地编译通过、Unity 才报 `error X3004`。

新增 `ImageProcess` 效果时：

1. 在 `ImageProcessEffect` 增加枚举值；不能复用 `RemovedEffectSlot*`，除非明确要兼容旧序列化。
2. 在 `ImageProcessEffectDescriptor` 注册默认 shader、执行分类和资源请求。
3. 在 `ImageProcessPass.EffectDispatch` 注册执行器。
4. 在 `Runtime/ImageProcess/Renderer/Effects` 添加 partial 实现。
5. 在 `Runtime/ImageProcess/Shaders/ImageProcess` 添加 shader（文件名要与枚举名一致，`ImageProcessEffectDescriptor.ShaderName` 是按枚举名拼的）。
6. 在 `Editor/PostProcessing/ImageProcess/Filters` 添加参数 UI，并在 `ImageProcessStackVolumeEditor.cs` 里补齐四处：`VisibleEffectOrder`（面板图标）、`EffectDisplayNames`（**按枚举顺序排列的数组，新值必须追加在末尾**）、绘制分发、`GetElementLineCount` 的行数分支（必须与 UI 实际绘制的行数一致），以及在 `ResetEffectDefaults` 里给新效果的默认参数。
7. 在 `ImageProcessStackVolumeEditor.Presets.cs` 的预设分发里挂上入口（look 表建议像 `GradientMap` 那样单独放一个 partial 文件并由脚本生成）。
8. 新增 `.cs` / shader 文件时记得手写 `.meta`：**UTF-8 无 BOM**、32 位十六进制唯一 GUID、与同类文件相同的字段结构（带 BOM 的 `.meta` 会让 Unity 解析失败，脚本根本不导入）。
9. 需要运行时生成的贴图（例如 `GradientMap` 的 ramp）时，走 `ImageProcessRuntimeLayer` 的字段 + 在 `ApplyLayerRamp` 一类的地方绑定，并且**在主线程的运行时层构建阶段生成**（`ImageProcessRuntimeLayerBuilder`），不要在 RenderGraph 的 render func 里创建 Unity 对象。

## 当前推荐配置

URP Renderer Asset 中建议按依赖加入这些 RendererFeature：

- 必选：`Ho-ObjectBuffer` + `Ho-SurfaceBuffer` + `Ho-AttributeComposite`，当 ScreenProcess 层遮罩、角色语义、DropShadow、PostLighting 或 PLR/SSS 需要覆盖率与表面数值时启用（顺序：三个 producer 在前、AC 在后、消费者最后）。
- 必选：`Ho-GeometryBuffer`，当 EdgeLight、PostLighting、SkyTyndall 或 CharacterSpecialization 需要 normal/depth 时启用。
- 可选：`Ho-GeometryBuffer` 的 Sky buffer，只有 SkyTyndall 或后续天空采样效果需要时启用。
- 可选：`Ho-CharacterSpecialization`，角色眼透、前发投影、脸色扩散或轮廓效果需要时启用。
- 可选：`Ho-ShadowCast`，材质侧需要自定义阴影数据时启用。
- 必选后处理：`Ho-ScreenProcess` 在语义屏幕效果需要时启用。
- 必选后处理：`Ho-ImageProcess` 在最终图像风格栈需要时启用。

Volume 中则分别添加 `Ho-ScreenProcess/Process Stack`、`Ho-ImageProcess/Post Process Stack` 和需要时的 `Ho-CharacterSpecialization/角色特化`。

# PostProcessing 当前架构

本文档替代本目录原先的 HoAOV、HoPost、ShoostStack 和早期 ShadowCast 资料。以下内容基于 2026-05-27 对 `Runtime`、`Editor/PostProcessing` 和当前工作树状态的源码核对。

## 当前完成状态

已落地的运行时模块：

- `ImageProcess`：Volume 驱动的图像域后处理栈，支持 RenderGraph 和兼容路径，当前效果枚举覆盖 50 个以上图像效果，其中 `RemovedEffectSlot*` 只作为旧序列化槽位保留。
- `ScreenProcess`：Volume 驱动的语义屏幕效果栈，当前效果为 `CustomMaterial`、`EdgeLight`、`Outline`、`DropShadow`、`DepthOfField`、`PostLighting`、`SkyTyndall`、`DepthFog`。它可以读取 MetadataBuffer、GeometryBuffer 和可选 Sky buffer。`DepthFog`（深度雾/高度雾，家族 C）是**一个效果里的两个槽**（各带开关，可同时开），见 `DepthFog.md`。
- `MetadataBuffer`：输出 Mask/ID、SurfaceData、Material Custom0-3、Object Custom0-7 和 SurfaceColor 等语义缓冲，并提供 Subject/Group 组件写入对象级元数据。
- `GeometryBuffer`：输出 normal/depth 缓冲，当前还增加了可选 Sky buffer 捕获，供 `SkyTyndall` 等 ScreenProcess 效果使用。
- `CharacterSpecialization`：角色特化合成已迁移到独立 RendererFeature 和 Volume，包含眼睛透过、前发投影、角色捕获 RT 和调试输出。
- `ShadowCast`：独立 RendererFeature，包含可见光采集、自定义阴影 atlas、第二方向光级联、PCSS 参数、运行时发布和调试视图。

已落地的编辑器侧能力：

- `ImageProcessStackVolumeEditor` 和 `ScreenProcessStackVolumeEditor` 负责层列表、图标按钮、预设菜单、每个效果的参数 UI。
- `调色`（`ColorGradingCustom`）的预设根级只有 `默认`（重置），其余 34 个 look 统一走 `基础/电影感/胶片/动画/风格` 五个子菜单，见 `ColorGradingPresets.md`。
- `渐变`（`Gradient`）除原有 4 种形状外新增 4 个两点模式（线性/径向/椭圆/锥形，旋转靠拖 B 点）、过渡曲线、镜像（反向渐变）、线性光插值、分辨率量化与输出抖动的暴露，见 `GradientInvestigation.md`。
- `渐变映射`（`GradientMap`，新增）是亮度/通道驱动的颜色映射（Photoshop Gradient Map 那一类）：色标用 **Unity 原生 `Gradient`**（≤8 颜色键 + ≤8 透明度键，Blend/Fixed），运行时烘焙成 1×256 的 ramp 贴图，shader 只做一次采样；另有输入窗口、反转、色阶数（平涂）、显示空间/线性光/Oklab 烘焙空间、输出抖动，以及 5 组 16 个 look（含 matplotlib/Google turbo/FLIR 风格色表采样）。见 `GradientMap.md`。
- `深度雾`（`DepthFog`，新增）是 ScreenProcess 里的合成雾：一个效果带**深度雾**与**高度雾**两个槽（各带开关，可同时开，两层在同一趟 pass 内按顺序合成）。深度项支持直线/指数/指数平方三种距离曲线与远近双色 + 空气感去饱和；高度项支持"高度窗／指数衰减 × 下方浓／上方浓"四种形式，高度取世界 Y（可切相机相对）并用包内既有的 smoothness + hardness 习惯；天空可跳过／一起上雾／单独染色；不依赖 GeometryBuffer 也能工作（自动回落相机深度，正交也走这条路）。预设 5 组 13 个，见 `DepthFog.md`。
- `网点`（`Halftone`，新增）是 ImageProcess 的半调网屏：模式有拜耳有序抖动（2/3/4/8 矩阵）与圆点/方点/菱形/线条四种面积调制网点，配色是「墨色（图层颜色）+ 纸色」两种颜色、四种合成（叠墨／双色／乘算／遮罩原色），另有输入窗口、暗部上墨、角度、柔化、网格抖动、浓度上限与输出抖动。设计上与 `渐变映射` 串联使用（ramp 管颜色、网点管墨量），也能单走。预设 5 组 15 个，见 `Halftone.md`。
- `Editor/PostProcessing/ViewControls` 提供屏幕空间中心、半径、方向等 SceneView 操作控件。
- `ScreenProcessRuleMaskEditorUtility` 提供基于 MetadataBuffer 的规则遮罩编辑 UI。

当前仍需注意的状态：

- `Tests/Runtime` 目录为空，源码中也未检索到 `[Test]` 或 `[UnityTest]`。本次只能做源码结构和静态检查，不能替代 Unity Editor 编译和画面验证。
- 包目录没有 `.sln`、`.csproj` 或 Unity `ProjectSettings/ProjectVersion.txt`，无法在当前包根直接跑 C# 编译。
- 工作树里已有未提交改动，尤其是 `ScreenProcess`、`GeometryBuffer` 和 `SkyTyndall` 相关文件。本文档按这些改动后的源码状态描述。
- 部分 Inspector 文本在源码里已经出现编码乱码，这不影响架构判断，但会影响编辑器显示质量，后续应单独修复。

## 总体渲染顺序

典型相机帧里的关系如下：

1. `HoShadowCastRendererFeature` 在 `BeforeRenderingPrePasses` 默认时机生成自定义阴影数据，并通过全局纹理、矩阵和参数发布给材质侧使用。
2. `HoMetadataBufferRendererFeature` 在 `AfterRenderingOpaques` 默认时机输出对象和材质语义缓冲。
3. `HoGeometryBufferRendererFeature` 在 `AfterRenderingOpaques` 默认时机输出 normal/depth；启用 Sky buffer 时，`HoGeometryBufferSkyPass` 在 `AfterRenderingSkybox` 默认时机从当前 camera color 捕获天空信息。
4. `HoCharacterSpecializationRendererFeature` 在 `AfterRenderingTransparents` 默认时机执行角色捕获和合成。
5. URP 原生后处理执行。
6. `ScreenProcessRendererFeature` 在 `AfterRenderingPostProcessing` 执行语义屏幕效果。
7. `ImageProcessRendererFeature` 在 `AfterRenderingPostProcessing + 1` 执行最终图像域效果。

`ScreenProcess` 必须早于 `ImageProcess`，因为前者负责需要场景语义的效果，后者负责最终图像风格叠加。

## 语义缓冲层

`MetadataBuffer` 的职责是把对象、材质和分组语义写成可被屏幕效果采样的 RT：

- RendererFeature：`Runtime/MetadataBuffer/HoMetadataBufferRendererFeature.cs`
- 主输出 Pass：`Runtime/MetadataBuffer/HoMetadataBufferPass.cs`
- RenderGraph 资源：`HoMetadataBufferRenderGraphResources`
- 全局纹理：`_HoMetadataBufferMaskIdTexture`、`_HoMetadataBufferSurfaceDataTexture`、`_HoMetadataBufferMaterialCustom0_3Texture`、`_HoMetadataBufferObjectCustom0_3Texture`、`_HoMetadataBufferObjectCustom4_7Texture`、`_HoMetadataBufferSurfaceColorTexture`

对象侧有两种写入方式：

- `HoMetadataBufferSubject` 用 `MaterialPropertyBlock` 写 Mask、GroupId、ObjectId、Flags、Thickness、Curvature、TransmittanceHint 和材质自定义值。
- `HoMetadataBufferGroup` 把 CharacterId、PartId、Flags 和 ObjectCustom 位打包进 `unity_RendererUserValue`，也会回写对应的 MaterialPropertyBlock。

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
- 单规则遮罩字段和最多 4 条 `ruleMasks`

规则遮罩从 MetadataBuffer 采样，可匹配 Mask、GroupId、ObjectId、Flags、Thickness、Curvature、Material、TransmittanceHint、Material Custom0-3、Object Custom0-7。组合方式支持 Replace、Or、And、Subtract、Add、Multiply。

当前资源依赖：

- `EdgeLight`：需要 MetadataBuffer MaskId 和 GeometryBuffer normal/depth。
- `Outline`：优先需要 GeometryBuffer normal/depth；GeometryBuffer coverage 为 0 的像素不参与边缘检测。
- `DropShadow`：优先需要 MetadataBuffer MaskId，并可按规则读取 SurfaceData、Custom0、ObjectCustom0/1；Metadata 不可用时兼容路径和 RenderGraph 使用内部 SubjectMask fallback。
- `DepthOfField`：需要 GeometryBuffer 线性深度；coverage 无效时按远裁剪面处理，支持固定焦距和 Transform 目标焦点。
- `PostLighting`：需要 MetadataBuffer MaskId 和 GeometryBuffer normal/depth。
- `SkyTyndall`：需要 GeometryBuffer normal/depth 和 Sky buffer；启用规则遮罩时还需要对应 MetadataBuffer 输入。
- `CustomMaterial`：默认只做 layer blit，按用户材质或 shader 扩展。

`ScreenProcessRuntimeDiagnostics.CurrentSnapshot` 会记录 active layer 数、写入 layer 数、back buffer 状态、camera color 状态、MetadataBuffer/GeometryBuffer/SkyTexture 是否满足等信息。调试面板应该优先读这个 snapshot，而不是猜测缺哪个 RendererFeature。

RenderGraph 路径有一个刻意保留的小收尾 pass：`ScreenProcessRendererFeature` 在 ScreenProcess stack 后立即入队 `Ho-ScreenProcess Release Semantic Buffers`。它用 `SetGlobalTextureAfterPass` 把 MetadataBuffer、GeometryBuffer 和 Sky buffer 的 shader global 改回 RenderGraph black texture，并把 active / valid flag 清为 0。这样 ImageProcess 仍然只看到 camera color，不会因为上一阶段的全局绑定在 RenderDoc 里表现为继续持有 G/MBuffer。这个 pass 只解绑 global，不代表提前销毁资源；如果 DebugTile 或其他后续 pass 显式 `UseTexture` 读取这些 RenderGraph 资源，资源生命周期仍会延长到真实最后消费者。

## ImageProcess

`ImageProcess` 是最终图像域后处理栈，入口文件为：

- `Runtime/ImageProcess/ImageProcessRendererFeature.cs`
- `Runtime/ImageProcess/ImageProcessStackVolume.cs`
- `Runtime/ImageProcess/ImageProcessLayer.cs`
- `Runtime/ImageProcess/Renderer/ImageProcessPass.cs`
- `Runtime/ImageProcess/Renderer/EffectPipeline/ImageProcessPass.EffectDispatch.cs`
- `Runtime/ImageProcess/Shaders/ImageProcess`
- `Editor/PostProcessing/ImageProcess`

它不消费 MetadataBuffer 或 GeometryBuffer，只处理 camera color。RendererFeature 从 Volume 生成运行时层列表，`ImageProcessPass` 用 ping-pong texture 或 RenderGraph `ImageProcessChain` 串起每个效果。最终结果回写到 camera color。

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
- 读取 MetadataBuffer 的对象自定义位和角色 ID。`ObjectCustom0` 约定为 `CharacterFull / 全角色`，`ObjectCustom6` 约定为 `CharacterBody / 人体`。
- 读取 GeometryBuffer normal/depth 辅助前发投影距离、深度、轮廓高度渐隐和遮罩判断。
- 合成眼睛透过、前发投影、脸色扩散、主体轮廓、增强轮廓，或输出调试视图。
- 眼睛透过支持相机角度修正：在 `HoMetadataBufferGroup` 上指定“面部朝向”（一个 Transform，骨骼或朝向正确的空物体均可；+Z 为脸前、+X 为角色右、+Y 为上，轴向可在组件上配置），CPU 每帧按相机与该朝向的平转/俯仰角写入 256×1 查询表，Composite 中按角色 ID 采样并衰减眼透不透明度。开关与平转/俯仰半角范围、柔化、强度由 Volume/Feature 全局控制（默认关闭）。

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
- `HoMetadataBufferDebugPass`
- `HoGeometryBufferDebugPass`
- `HoShadowCastDebugPass`
- `HoCharacterSpecializationRuntimeDiagnostics`
- `Runtime/Debug/HoDebugTileRendererFeature.cs`

排查顺序建议：

1. 先确认对应 RendererFeature 是否启用，并且目标相机是 Game 或允许 SceneView。
2. 再看 Volume 是否 active，layer 是否 enabled 且 intensity 大于阈值。
3. 对 `ScreenProcess`，检查诊断 snapshot 中 MetadataBuffer、GeometryBuffer、SkyTexture 的可用性。
4. 对 `ImageProcess`，检查 active layer 数、back buffer 状态和 camera color 是否可用。
5. 对 `CharacterSpecialization`，检查 capture shader tag、MetadataBuffer 分组位和 GeometryBuffer 输入。

## 新增效果接入规则

新增 `ScreenProcess` 效果时：

1. 在 `ScreenProcessEffect` 增加枚举值。
2. 在 `ScreenProcessShaderConstants` 和 `ScreenProcessEffectRegistry` 注册默认 shader。
3. 在 `ScreenProcessRendererFeature` 中声明资源依赖，并在 RenderGraph pass 中绑定需要的 MetadataBuffer、GeometryBuffer 或 Sky texture。
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

- 必选：`Ho-MetadataBuffer`，当 ScreenProcess 规则遮罩、角色语义、DropShadow 或 PostLighting 需要对象语义时启用。
- 必选：`Ho-GeometryBuffer`，当 EdgeLight、PostLighting、SkyTyndall 或 CharacterSpecialization 需要 normal/depth 时启用。
- 可选：`Ho-GeometryBuffer` 的 Sky buffer，只有 SkyTyndall 或后续天空采样效果需要时启用。
- 可选：`Ho-CharacterSpecialization`，角色眼透、前发投影、脸色扩散或轮廓效果需要时启用。
- 可选：`Ho-ShadowCast`，材质侧需要自定义阴影数据时启用。
- 必选后处理：`Ho-ScreenProcess` 在语义屏幕效果需要时启用。
- 必选后处理：`Ho-ImageProcess` 在最终图像风格栈需要时启用。

Volume 中则分别添加 `Ho-ScreenProcess/Process Stack`、`Ho-ImageProcess/Post Process Stack` 和需要时的 `Ho-CharacterSpecialization/角色特化`。

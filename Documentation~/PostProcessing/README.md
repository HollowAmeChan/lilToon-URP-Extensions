# PostProcessing 当前架构

本文档替代本目录原先的 HoAOV、HoPost、ShoostStack 和早期 ShadowCast 资料。以下内容基于 2026-05-27 对 `Runtime`、`Editor/PostProcessing` 和当前工作树状态的源码核对。

## 当前完成状态

已落地的运行时模块：

- `ImageProcess`：Volume 驱动的图像域后处理栈，支持 RenderGraph 和兼容路径，当前效果枚举覆盖 50 个以上图像效果，其中 `RemovedEffectSlot*` 只作为旧序列化槽位保留。
- `ScreenProcess`：Volume 驱动的语义屏幕效果栈，当前效果为 `CustomMaterial`、`EdgeLight`、`Outline`、`DropShadow`、`DepthOfField`、`PostLighting`、`SkyTyndall`。它可以读取 MetadataBuffer、GeometryBuffer 和可选 Sky buffer。
- `MetadataBuffer`：输出 Mask/ID、SurfaceData、Material Custom0-3、Object Custom0-7 和 SurfaceColor 等语义缓冲，并提供 Subject/Group 组件写入对象级元数据。
- `GeometryBuffer`：输出 normal/depth 缓冲，当前还增加了可选 Sky buffer 捕获，供 `SkyTyndall` 等 ScreenProcess 效果使用。
- `CharacterSpecialization`：角色特化合成已迁移到独立 RendererFeature 和 Volume，包含眼睛透过、前发投影、角色捕获 RT 和调试输出。
- `ShadowCast`：独立 RendererFeature，包含可见光采集、自定义阴影 atlas、第二方向光级联、PCSS 参数、运行时发布和调试视图。

已落地的编辑器侧能力：

- `ImageProcessStackVolumeEditor` 和 `ScreenProcessStackVolumeEditor` 负责层列表、图标按钮、预设菜单、每个效果的参数 UI。
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
- `Outline`：需要 URP camera normals/depth。
- `DropShadow`：需要 MetadataBuffer MaskId，并可按规则读取 SurfaceData、Custom0、ObjectCustom0/1；兼容路径还会生成内部 SubjectMask。
- `DepthOfField`：需要 camera depth；支持固定焦距和 Transform 目标焦点。
- `PostLighting`：需要 MetadataBuffer MaskId 和 GeometryBuffer normal/depth。
- `SkyTyndall`：需要 GeometryBuffer normal/depth 和 Sky buffer；启用规则遮罩时还需要对应 MetadataBuffer 输入。
- `CustomMaterial`：默认只做 layer blit，按用户材质或 shader 扩展。

`ScreenProcessRuntimeDiagnostics.CurrentSnapshot` 会记录 active layer 数、写入 layer 数、back buffer 状态、camera color 状态、MetadataBuffer/GeometryBuffer/SkyTexture 是否满足等信息。调试面板应该优先读这个 snapshot，而不是猜测缺哪个 RendererFeature。

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

## CharacterSpecialization

`CharacterSpecialization` 已不再作为早期 HoAOV 的一部分维护，而是独立 RendererFeature：

- `Runtime/CharacterSpecialization/HoCharacterSpecializationRendererFeature.cs`
- `Runtime/CharacterSpecialization/HoCharacterSpecializationVolume.cs`
- `Runtime/CharacterSpecialization/Shaders/HoCharacterCaptureCommon.hlsl`

它支持 Volume 覆盖，执行内容包括：

- 使用 `LightMode = HoCharacterCapture` 的材质 pass 捕获眼睛/脸部数据。
- 读取 MetadataBuffer 的对象自定义位和角色 ID。
- 读取 GeometryBuffer normal/depth 辅助前发投影距离、深度和遮罩判断。
- 合成眼睛透过、前发投影，或输出调试视图。

材质接入点保留在 shader include 中：角色捕获 pass 应调用 `LilHoCharacterBuildCaptureOutput`，并让材质自己的 alpha、cutout、dissolve 规则决定是否写入捕获 RT。

## ShadowCast

`ShadowCast` 是独立的自定义阴影发布系统，不再和后处理文档里的旧 HoAOV 计划绑定：

- `Runtime/ShadowCast/HoShadowCastRendererFeature.cs`
- `Runtime/ShadowCast/HoShadowCastPass.cs`
- `Runtime/ShadowCast/HoShadowCastFrameCollector.cs`
- `Runtime/ShadowCast/HoShadowCastPublisher.cs`

核心流程：

- 从 URP visible lights 或配置中收集灯光。
- 按方向光、点光、聚光生成 shadow slice，并装入 atlas。
- 第二方向光使用独立 atlas 和级联参数。
- PCSS 参数按 punctual 和 second directional 分开发布。
- `HoShadowCastPublisher` 在每个 camera 开始时 reset，在 pass 结束后发布全局阴影贴图、矩阵、灯光数据和调试数据。

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

新增 `ImageProcess` 效果时：

1. 在 `ImageProcessEffect` 增加枚举值；不能复用 `RemovedEffectSlot*`，除非明确要兼容旧序列化。
2. 在 `ImageProcessEffectDescriptor` 注册默认 shader、执行分类和资源请求。
3. 在 `ImageProcessPass.EffectDispatch` 注册执行器。
4. 在 `Runtime/ImageProcess/Renderer/Effects` 添加 partial 实现。
5. 在 `Runtime/ImageProcess/Shaders/ImageProcess` 添加 shader。
6. 在 `Editor/PostProcessing/ImageProcess/Filters`、Volume editor 和 presets 中补齐 UI。

## 当前推荐配置

URP Renderer Asset 中建议按依赖加入这些 RendererFeature：

- 必选：`Ho-MetadataBuffer`，当 ScreenProcess 规则遮罩、角色语义、DropShadow 或 PostLighting 需要对象语义时启用。
- 必选：`Ho-GeometryBuffer`，当 EdgeLight、PostLighting、SkyTyndall 或 CharacterSpecialization 需要 normal/depth 时启用。
- 可选：`Ho-GeometryBuffer` 的 Sky buffer，只有 SkyTyndall 或后续天空采样效果需要时启用。
- 可选：`Ho-CharacterSpecialization`，角色眼透和前发投影需要时启用。
- 可选：`Ho-ShadowCast`，材质侧需要自定义阴影数据时启用。
- 必选后处理：`Ho-ScreenProcess` 在语义屏幕效果需要时启用。
- 必选后处理：`Ho-ImageProcess` 在最终图像风格栈需要时启用。

Volume 中则分别添加 `Ho-ScreenProcess/Process Stack`、`Ho-ImageProcess/Post Process Stack` 和需要时的 `Ho-CharacterSpecialization/角色特化`。

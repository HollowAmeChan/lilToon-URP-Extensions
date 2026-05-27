# 平面反射

`HoPlanarReflectionRendererFeature` 负责在每个源相机渲染前调度场景里的 `HoPlanarReflectionSurface`。每个有效表面都会生成一台隐藏的镜像相机，把反射结果渲染到自己的 `RenderTexture`，再通过 `MaterialPropertyBlock` 和全局 shader 属性提供给后面的材质或合成 pass 使用。

不要把它理解成两套独立的反射方案。核心输入始终是 `_LILPBRPlanarReflectionTexture`；目标架构是像延迟渲染一样，把材质只当作参数写入者，最后由统一的 fullscreen pass 负责混合。

- 目标链路：`HoPlanarReflectionCompositePass` 在透明物体之后读取 camera color、MetadataBuffer、GeometryBuffer 和反射纹理，用 fullscreen 后处理把反射混回画面。普通平面可以看成同一条链路里不做法线扰动的情况；水面只是额外写 wetness、normal strength 等参数来驱动扰动和权重。
- Legacy fallback：部分材质仍保留 ForwardLit 内直接采样 `_LILPBRPlanarReflectionTexture` 的旧路径。它只用于当前过渡期兼容，后续应移除。合成 active 时应通过 `_HoPlanarReflectionSuppressMaterialSampling` 抑制这一路，避免同一块水面反射混合两次。

## 快速设置

### 基础反射输入

1. 在当前 URP Renderer Asset 中添加 `HoPlanarReflectionRendererFeature`。
2. 在镜面、抛光地面或水面 mesh 上添加 `HoPlanarReflectionSurface`。
3. 确认 `目标渲染器` 指向接收反射的 renderer。留空时会自动取当前 GameObject 上的 `Renderer`。
4. 使用会参与平面反射的材质。材质的长期职责是写 MetadataBuffer / GeometryBuffer 参数，而不是在 ForwardLit 里采样反射。当前普通 `lilPBR` 仍有材质 fallback，属于待替换路径；水面目标路径已经通过 MetadataBuffer / GeometryBuffer 给后处理合成提供 mask 和材质参数。
5. 在 `HoPlanarReflectionSurface` 上设置 `反射层遮罩`，只包含需要出现在反射里的层。通常要把镜面自身所在层排除，避免反射相机被镜面面片或附近遮挡物挡住。
6. 调整 `反射分辨率`、`裁剪` 和材质里的 `Strength`、`Min Smoothness`、`Tint`、`Edge Fade`、`Fade Start/End`。

`自动启用材质开关` 默认开启，会通过 `MaterialPropertyBlock` 给目标 renderer 写入 `_UsePlanarReflection = 1`。如果关闭它，需要在材质面板里手动开启 `Planar Reflection`。

### 后处理合成

后处理合成需要辅助缓冲来告诉 fullscreen pass 哪些像素要混反射，以及如何混：

1. 在同一个 URP Renderer Asset 中启用 `HoMetadataBufferRendererFeature`。
2. 启用 `HoGeometryBufferRendererFeature`。
3. 启用 `HoPlanarReflectionRendererFeature` 的 `启用后处理合成`。
4. 使用带有 `HoMetadataBuffer` / `HoGeometryBuffer` pass 的水面材质。当前 `WaterFlowmap` 会在专门的 MetadataBuffer pass 中写入 `Custom0`，不是在 ForwardLit 主绘制时读取自己写出的缓冲；没有外部 custom override 时会自动写入：
   - `Custom0.r`：水面 smoothness
   - `Custom0.g`：wetness
   - `Custom0.b`：normal strength
   - `Custom0.a`：planar reflection strength
5. 确认 MetadataBuffer / GeometryBuffer 的 `Layer Mask` 和 `Render Queue` 覆盖水面对象。
6. 保持 `合成 Pass Event` 默认 `BeforeRenderingPostProcessing`，让合成发生在透明水面渲染之后、后处理之前。

合成开启时，`合成时禁用材质侧反射` 会发布 `_HoPlanarReflectionSuppressMaterialSampling = 1`，避免水面材质先采样一次、合成 pass 再混合一次。当前 `WaterFlowmap` 会读取这个全局开关。

所以目标路径是“前置缓冲 + 纯后处理合成”：主材质 pass 不依赖 MetadataBuffer / GeometryBuffer；这两个缓冲只是给后面的 fullscreen composite pass 使用。`WaterFlowmap` 里保留的材质侧 planar reflection 是 legacy fallback，后续应随普通平面分支一起移除。

当前 `HoPlanarReflectionComposite.shader` 是按水面输入写的，会读取 GeometryBuffer normal/depth。要彻底去掉材质采样路径，需要先补齐普通平面后处理分支：普通 lilPBR 写入 MetadataBuffer mask/strength/smoothness 等基础参数，composite shader 在 `normalStrength = 0` 或无水面法线时走 no-normal/no-distortion 分支。

## Surface 组件

`HoPlanarReflectionSurface` 是每个反射平面的描述和资源持有者。

### 目标与平面来源

| Inspector | 实际行为 |
| --- | --- |
| `目标渲染器` | 接收 property block 的 renderer。留空时使用当前 GameObject 的 `Renderer`。 |
| `反射平面锚点` | 指定反射平面的位置和法线。位置取 `planeTransform.position`，法线取 `planeTransform.up`。 |
| `使用渲染器中心` | 只在未指定锚点时生效。开启时平面位置取 `targetRenderer.bounds.center`；关闭时取组件 `transform.position`。 |

未指定 `反射平面锚点` 时，平面法线优先取 `targetRenderer.transform.up`，没有目标 renderer 时才取组件自身 `transform.up`。这意味着组件可以挂在同一个 mesh 上，也可以通过 `目标渲染器` 指向其他 renderer。

### 反射渲染参数

| Inspector | 实际行为 |
| --- | --- |
| `反射层遮罩` | 写入反射相机 `cullingMask`，只影响镜像相机看到的对象。 |
| `反射分辨率` | 作为反射纹理宽度，范围 `64..4096`。高度按 `resolution / sourceCamera.aspect` 计算并 clamp 到 `64..4096`。 |
| `反射中隐藏本体` | 渲染反射时临时设置目标 renderer 的 `forceRenderingOff = true`，结束后恢复。 |
| `自动启用材质开关` | 对目标 renderer 的 property block 写 `_UsePlanarReflection`。启用时成功渲染写 `1`，失效写 `0`。 |
| `反射场景视图` | 控制单个 surface 是否响应 Scene View 相机。RendererFeature 也有全局 Scene View 开关，两者都要允许才会渲染。 |
| `更新帧间隔` | 大于 `1` 时按帧复用上一张反射纹理，只刷新 property block。 |

反射纹理使用 `RenderTextureFormat.DefaultHDR`、24-bit depth、`FilterMode.Bilinear`、`TextureWrapMode.Clamp`，不生成 mipmap。

### 裁剪与背景

| Inspector | 实际行为 |
| --- | --- |
| `使用平面裁剪` | 开启时用反射平面生成 oblique projection，裁掉镜面背后的内容。 |
| `裁剪平面偏移` | 在 `CameraSpacePlane` 中沿平面法线偏移裁剪面。`0` 表示贴合平面。 |
| `最小裁剪距离` | 源相机太贴近平面时，把裁剪面沿可见侧后退，避免 oblique clipping 把整张反射裁掉。 |
| `反射相机近裁剪` | 反射相机 `nearClipPlane` 的最小值。 |
| `复制相机清屏设置` | 开启时使用源相机 clear flags 和背景色；关闭时使用 `备用清屏方式` 与 `备用背景色`。 |

## RendererFeature 设置

| 设置 | 作用 |
| --- | --- |
| `启用` | 关闭后停止所有反射，并把 surface property block 置为不可用。 |
| `渲染 Game View` | 控制 `CameraType.Game`。 |
| `渲染 Scene View` | 控制 `CameraType.SceneView`。 |
| `每相机最大表面数` | `0` 表示不限制。超过上限的 surface 会被写成 disabled property block。 |
| `启用后处理合成` | 启用 `Ho-PlanarReflection Composite` fullscreen pass。长期目标是所有平面反射都依赖它；关闭后不应再作为正式画质路径。 |
| `合成时禁用材质侧反射` | 合成有效时发布 `_HoPlanarReflectionSuppressMaterialSampling`，给材质避免双重反射。 |
| `合成 Pass Event` | 默认 `BeforeRenderingPostProcessing`。需要在 MetadataBuffer / GeometryBuffer 之后，并在最终后处理之前。 |
| `合成 Shader` | 可覆盖默认 `Hidden/lilToon/URP/PlanarReflection/Composite`。 |
| `合成强度` | 写入 `_HoPlanarReflectionCompositeParams.x`。 |
| `法线扰动` | 写入 `_HoPlanarReflectionCompositeParams.y`，用 GeometryBuffer 法线扰动采样 UV。 |
| `屏幕边缘像素外扩` | 写入 `_HoPlanarReflectionCompositeOptions.z`，把越界扰动 UV clamp 到屏幕内侧采样。 |
| `最小 Smoothness` | 写入 `_HoPlanarReflectionCompositeParams.z`，水面 smoothness 低于该值时渐隐。 |
| `反射 Tint` | 写入 `_HoPlanarReflectionCompositeTint`，RGB 乘到反射颜色，A 乘到合成权重。 |
| `反射纹理 Flip Y` | 写入 `_HoPlanarReflectionCompositeOptions.x`。 |
| `启用深度门控` | 写入 `_HoPlanarReflectionCompositeOptions.y`。 |
| `深度容差` | 写入 `_HoPlanarReflectionCompositeParams.w`，用于过滤扰动 UV 采到的非同一水面深度。 |

## 运行流程

### 镜像相机渲染

1. `HoPlanarReflectionRendererFeature.Create()` 注册 `RenderPipelineManager.beginCameraRendering`。
2. 每个源相机开始渲染时，RendererFeature 会选择一个可用 feature，发布合成相关全局参数，然后调用 `HoPlanarReflectionSurface.RenderAllSurfaces()`。
3. `RenderAllSurfaces()` 先重置全局反射状态：
   - `_HoPlanarReflectionCompositeActive = 0`
   - `_HoPlanarReflectionSuppressMaterialSampling = 0`
   - `_LILPBRPlanarReflectionTexture = Texture2D.blackTexture`
   - `_LILPBRPlanarReflectionTextureMatrix = identity`
   - `_LILPBRPlanarReflectionParams = 0`
4. 如果当前相机是 Reflection / Preview，相机为空、递归渲染中、或 Game/Scene 开关不允许，本帧直接跳过。
5. 对每个 active surface：
   - 按上面的规则解析平面位置和法线。
   - 计算平面反射矩阵。
   - 从源相机复制投影、FOV、clear flags 等相机设置。
   - 把反射相机移动到源相机的镜像位置，并写入 `worldToCameraMatrix = sourceWorldToCamera * reflectionMatrix`。
   - 如果开启平面裁剪，使用反射平面计算 oblique projection。
   - 临时翻转 `GL.invertCulling`，再调用 `UniversalRenderPipeline.RenderSingleCamera(context, reflectionCamera)`。
   - 结束后恢复 `GL.invertCulling` 和目标 renderer 的 `forceRenderingOff`。
6. 成功渲染后，surface 通过 property block 写入目标 renderer，并同时发布一份全局反射输入给合成 pass。

### Legacy 材质 fallback

`HoPlanarReflectionSurface` 给目标 renderer 写入：

| 属性 | 值 |
| --- | --- |
| `_UsePlanarReflection` | `1` 或 `0`，仅在 `自动启用材质开关` 开启时写入。 |
| `_LILPBRPlanarReflectionTexture` | 当前 surface 的反射 `RenderTexture`。 |
| `_LILPBRPlanarReflectionTextureMatrix` | `GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, true) * reflectionCamera.worldToCameraMatrix`。 |
| `_LILPBRPlanarReflectionParams` | `(active, width, height, 0)`，active 成功时为 `1`，失效时为 `0`。 |

普通 `lilPBR` 当前会在 `_UsePlanarReflection != 0`、`_PlanarReflectionStrength > 0` 且 `_LILPBRPlanarReflectionParams.x > 0.5` 时直接采样反射。它使用屏幕 UV 采样 `_LILPBRPlanarReflectionTexture`，再根据 smoothness、edge fade、distance fade、strength 和 tint 混合到环境反射中。这是 legacy 兼容路径，不是目标架构，迁移完成后应删除。

`WaterFlowmap` 也保留了同类 fallback，并额外用水面法线扰动 UV。后处理合成有效且 `_HoPlanarReflectionSuppressMaterialSampling > 0.5` 时，它会跳过材质内采样，把反射交给 composite pass。新的材质不应再增加这类 ForwardLit 采样逻辑。

### 后处理合成

合成 pass 的 RenderGraph 路径读取以下输入：

| 输入 | 来源 |
| --- | --- |
| camera color | 当前 `UniversalResourceData.activeColorTexture`。 |
| `_LILPBRPlanarReflectionTexture` | 最近一次成功发布的平面反射纹理。 |
| `_LILPBRPlanarReflectionParams` | 反射是否有效和纹理尺寸。 |
| `_HoMetadataBufferMaskIdTexture` | MetadataBuffer 的 mask/id RT，`maskId.r` 作为水面 mask。 |
| `_HoMetadataBufferMaterialCustom0_3Texture` | MetadataBuffer 的材质自定义通道，`Custom0.rgba` 提供水面合成参数。 |
| `_HoGeometryBufferNormalDepthTexture` | GeometryBuffer 的世界法线与线性深度。 |

fragment 处理逻辑：

1. 读取当前 camera color。
2. 如果调试模式是 `InputStatus`，直接输出输入状态：
   - R：合成是否 active 且反射纹理有效
   - G：MetadataBuffer 是否 active 且 mask/custom0 有效
   - B：GeometryBuffer normal/depth 是否有效
3. 如果合成未 active、MetadataBuffer 未 active、或 `_LILPBRPlanarReflectionParams.x <= 0.5`，普通模式返回原 camera color，调试模式返回洋红色。
4. 读取 `maskId`、`custom0`、`normalDepth`。
5. 计算水面权重：
   - `waterMask = saturate(maskId.r) * coverage(normalDepth)`
   - `smoothness = custom0.r`
   - `wetness = custom0.g`
   - `normalStrength = custom0.b`
   - `materialReflectionStrength = custom0.a`
   - `smoothnessFade = saturate((smoothness - minSmoothness) / (1 - minSmoothness))`
   - `centerWeight = waterMask * wetness * materialReflectionStrength * smoothnessFade`
6. 把 GeometryBuffer 的世界法线转到 view space，用 `normalVS.xy * distortion * normalStrength * wetness` 扰动屏幕 UV。
7. 用 `屏幕边缘像素外扩` 把扰动后的 UV clamp 到屏幕内，避免采到纹理外。
8. 如果启用深度门控，采样扰动 UV 位置的 GeometryBuffer 深度，并按 `深度容差` 衰减跨物体采样。
9. 根据 `反射纹理 Flip Y` 可选翻转反射 UV。
10. 采样 `_LILPBRPlanarReflectionTexture`，乘 `反射 Tint.rgb`。
11. 计算 `compositeWeight = centerWeight * depthGate * 合成强度 * Tint.a`。
12. `lerp(cameraColor.rgb, reflection, compositeWeight)` 写回新的 camera color。

兼容模式路径会先把 camera color blit 到 `_HoPlanarReflectionCompositeSource`，再用同一个 shader 合成回 camera color。

默认时序是：`HoMetadataBufferRendererFeature` 与 `HoGeometryBufferRendererFeature` 在 `AfterRenderingOpaques` 绘制各自的 buffer pass，透明水面再走 `ForwardLit`，最后 `HoPlanarReflectionCompositePass` 在 `BeforeRenderingPostProcessing` 读取 camera color、MetadataBuffer、GeometryBuffer 和反射纹理做 fullscreen 合成。只要没有把这些 pass event 调乱，合成读取时缓冲已经存在。

## IO 契约

### Surface 输出

| 名称 | 类型 | 写入位置 | 消费者 |
| --- | --- | --- | --- |
| `_UsePlanarReflection` | float | 目标 renderer property block | legacy 材质 fallback 开关；后处理迁移后可弱化或删除 |
| `_LILPBRPlanarReflectionTexture` | texture | 目标 renderer property block 与全局 shader state | Composite shader、DebugTile、legacy 材质 fallback |
| `_LILPBRPlanarReflectionTextureMatrix` | matrix | 目标 renderer property block 与全局 shader state | 预留给按反射相机 VP 投影的 shader 使用 |
| `_LILPBRPlanarReflectionParams` | float4 | 目标 renderer property block 与全局 shader state | `.x` 表示是否有效，`.y/.z` 是 RT 尺寸 |

注意：property block 是 per-renderer 的，主要服务 legacy 材质 fallback。全局 shader state 只有一份，后处理合成 pass 会读取最后一次成功发布的 surface。需要多块独立反射平面同时合成时，应给合成 pass 增加 per-surface 选择机制；当前实现更适合限制 `每相机最大表面数 = 1`，或只让一个水面参与合成。

### Composite 输入输出

| 名称 | 类型 | 含义 |
| --- | --- | --- |
| `_HoPlanarReflectionCompositeActive` | float | 本帧至少有一个 surface 成功渲染，且合成可用。 |
| `_HoPlanarReflectionSuppressMaterialSampling` | float | 合成 active 且设置要求材质 fallback 跳过采样。 |
| `_HoPlanarReflectionCompositeParams` | float4 | `(strength, distortion, minSmoothness, depthTolerance)`。 |
| `_HoPlanarReflectionCompositeOptions` | float4 | `(flipY, enableDepthGate, edgeExtendDistance, 0)`。 |
| `_HoPlanarReflectionCompositeTint` | float4 | `(tint.r, tint.g, tint.b, tint.a)`。 |
| `_HoPlanarReflectionDebugParams` | float4 | `(debugMode, debugDepthFar, debugDistortionScale, 0)`。 |
| `_HoPlanarReflectionDebugInputStatus` | float4 | `(source, maskId, normalDepth, custom0)`，RenderGraph 路径按实际资源有效性写入。 |
| `_HoMetadataBufferMaskIdTexture` | texture | `r` 是 mask coverage，合成中作为水面 mask。 |
| `_HoMetadataBufferMaterialCustom0_3Texture` | texture | `rgba` 是水面合成参数。 |
| `_HoGeometryBufferNormalDepthTexture` | texture | `rgb` 是编码世界法线，`a` 是线性深度。 |
| `_HoPlanarReflectionCompositeColor` | texture | RenderGraph 合成输出，赋回 `resourceData.cameraColor`。 |

### WaterFlowmap 的 Custom0 语义

`WaterFlowmap` 会为 MetadataBuffer pass 解析 `Custom0`：

| 通道 | 默认写入值 | 合成用途 |
| --- | --- | --- |
| `r` | `surface.smoothness` | 与 `最小 Smoothness` 计算 smoothness fade。 |
| `g` | `surface.wetness` | 衰减反射强度，并参与法线扰动。 |
| `b` | `_NormalStrength` | 控制扰动强度。 |
| `a` | `_PlanarReflectionStrength * (_UsePlanarReflection != 0)` | 材质级反射强度。 |

如果材质或 `HoMetadataBufferSubject` 写入 `_HoMetadataBufferCustomWriteMask >= 0.5`，`WaterFlowmap` 会改用 `_HoMetadataBufferCustomValues0`，并按 custom write mask 过滤通道。手动覆盖时要保证 0..3 四个 custom bit 覆盖到需要的通道，否则合成权重可能为 0。

## 调试

- RendererFeature Inspector 的 `运行状态` 会显示上一帧相机、surface 总数、本帧有效 surface 数和跳过原因。
- `调试模式 = InputStatus` 会把合成 pass 直接输出为 RGB 状态图：
  - 红色通道为反射输入是否有效。
  - 绿色通道为 MetadataBuffer mask/custom0 是否有效。
  - 蓝色通道为 GeometryBuffer 是否有效。
- 其他调试模式会显示 mask、smoothness、wetness、normal strength、reflection strength、world normal、linear depth、distortion、distorted UV、reflection color、composite weight、depth gate、custom0 或 edge extend。
- 如果不想替换整屏颜色，可以添加 `Ho-DebugTile` 并选择 `PlanarReflection` 条目做小窗调试。

## 常见问题

### 完全没有反射

- 检查 URP Renderer Asset 是否添加并启用 `HoPlanarReflectionRendererFeature`。
- 检查当前相机类型是否被 `渲染 Game View` / `渲染 Scene View` 允许。
- 检查 surface 的目标 renderer 是否存在且 enabled。
- 检查 `_LILPBRPlanarReflectionParams.x`。为 `0` 说明本帧 surface 没有成功发布反射。
- 检查材质 `_UsePlanarReflection`、`_PlanarReflectionStrength` 和 `Min Smoothness`。

### 合成调试显示 Metadata 或 Geometry 缺失

- 确认添加并启用了 `HoMetadataBufferRendererFeature` 和 `HoGeometryBufferRendererFeature`。
- 确认两个 buffer feature 的 layer mask 覆盖水面。
- 确认水面材质或 fallback pass 能写 `HoMetadataBuffer` 和 GeometryBuffer 需要的数据。
- 确认 `合成 Pass Event` 在 MetadataBuffer / GeometryBuffer 的 pass event 之后。

### 反射被镜面本身挡住

- 开启 `反射中隐藏本体`。
- 把镜面自身从 `反射层遮罩` 中排除。
- 对很贴近镜面的相机，适当增大 `最小裁剪距离` 或检查 `使用平面裁剪`。

### 水面合成过强或双重反射

- 开启 `合成时禁用材质侧反射`，让水面材质 fallback 不再额外混一次。
- 确认水面材质读取 `_HoPlanarReflectionSuppressMaterialSampling`。当前 `WaterFlowmap` 已处理。
- 降低 `合成强度`、材质 `_PlanarReflectionStrength`、wetness 或 `Tint.a`。

### 扰动边缘拉伸明显

- 降低 `法线扰动` 或水面 normal strength。
- 调小 `屏幕边缘像素外扩` 可以减少边缘复制，但可能露出越界断层。
- 对局部水面可尝试开启 `启用深度门控`，并设置合适的 `深度容差`；大面积平水面通常保持关闭更稳定。

## 性能注意

- 每个 active surface 都会额外渲染一次场景。`每相机最大表面数`、`更新帧间隔`、`反射分辨率` 和 `反射层遮罩` 是主要性能控制点。
- 反射相机关闭 MSAA 与 occlusion culling，减少额外成本并避免反射视角裁剪不稳定。
- 后处理合成额外读取 camera color、MetadataBuffer、GeometryBuffer 和反射纹理。普通镜面如果暂时没有写 MetadataBuffer mask/strength，当前只能走 legacy 材质 fallback；要统一到纯后处理，需要补基础平面分支，然后删除材质内采样路径。

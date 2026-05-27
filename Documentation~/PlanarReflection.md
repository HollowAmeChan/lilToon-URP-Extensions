# 平面反射

`HoPlanarReflectionRendererFeature` 负责在每个源相机渲染前调度场景里的 `HoPlanarReflectionSurface`。每个有效表面都会生成一台隐藏的镜像相机，先把反射结果渲染到带 depth/stencil 的相机输出 RT，再拷贝到 color-only 的反射纹理，把这张 color-only 纹理和状态参数发布成全局 shader 输入，最后由统一的 fullscreen composite pass 混回 camera color。

当前正式链路只有一条：材质负责把“哪些像素需要反射、按什么强度和扰动混合”写进 MetadataBuffer / GeometryBuffer；`HoPlanarReflectionCompositePass` 在透明物体之后读取 camera color、MetadataBuffer、GeometryBuffer 和反射纹理，统一做后处理合成。内置 `lilPBR` 和 `WaterFlowmap` 不再在 `ForwardLit` 里直接采样平面反射纹理。

这意味着主材质绘制时不需要、也不应该读取自己写出的缓冲。MetadataBuffer / GeometryBuffer 是独立的辅助 pass；它们在 composite 之前已经画好，fullscreen pass 才是唯一消费这些缓冲并混合反射的地方。

## 快速设置

### RendererFeature

1. 在当前 URP Renderer Asset 中添加并启用 `HoMetadataBufferRendererFeature`。
2. 添加并启用 `HoGeometryBufferRendererFeature`。
3. 添加并启用 `HoPlanarReflectionRendererFeature`。
4. 在 `HoPlanarReflectionRendererFeature` 中开启 `启用后处理合成`。
5. 保持 `合成 Pass Event` 为默认 `BeforeRenderingPostProcessing`，除非你明确调整了其他 buffer pass 的时机。它必须晚于 MetadataBuffer / GeometryBuffer，并且晚于透明物体主绘制。
6. 如需让反射更亮，调高 `反射曝光 EV`；如需柔化反射，调高 `圆盘模糊半径`。

如果关闭 `启用后处理合成`，运行时仍可以生成和发布反射纹理，但内置材质不会再把它混入画面；这个状态主要用于调试或给外部自定义管线接管。

### Surface

1. 在镜面、抛光地面或水面 mesh 上添加 `HoPlanarReflectionSurface`。
2. 确认 `目标渲染器` 指向接收反射开关和 property block 的 renderer。留空时会自动取当前 GameObject 上的 `Renderer`。
3. 设置 `反射层遮罩`，只包含需要出现在反射里的层。通常要把镜面自身所在层排除，避免反射相机被镜面面片挡住。
4. 按需要设置 `反射平面锚点`。未指定时，平面位置和法线会从目标 renderer 或组件 transform 推导。
5. 调整 `反射分辨率`、裁剪参数和 `更新帧间隔`。

`自动启用材质开关` 默认开启。成功渲染反射时，Surface 会通过 `MaterialPropertyBlock` 给目标 renderer 写入 `_UsePlanarReflection = 1`；失败或跳过时写入 `0`。现在这个开关用于让材质的 MetadataBuffer pass 写出反射强度，不再表示材质会直接采样反射纹理。

### 材质

参与平面反射的材质必须提供 `HoMetadataBuffer` 和 `HoGeometryBuffer` pass：

- 普通 `lilPBR` 已写入基础平面反射参数。它把 smoothness、反射强度和无扰动标记写进 MetadataBuffer。
- `WaterFlowmap` 写入水面专用参数，包括 wetness 和 normal strength，用于扰动反射 UV。
- 新材质不要再在 `ForwardLit` 或其他主光照 pass 里采样 `_LILPBRPlanarReflectionTexture`。需要参与反射时，按下面的 Custom0 契约写 MetadataBuffer。

MetadataBuffer / GeometryBuffer 的 `Layer Mask` 和 `Render Queue` 必须覆盖这些材质对象，否则 composite pass 看不到 mask、材质参数或法线深度。

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
| `更新帧间隔` | 大于 `1` 时按帧复用上一张反射纹理，只刷新 property block 和全局状态。 |

反射相机的 `targetTexture` 使用 `RenderTextureFormat.DefaultHDR` 并带 depth/stencil，因为 URP RenderGraph 的相机输出要求 Output Texture 的 Depth Stencil Format 不能为 `None`。渲染结束后会把它 Blit 到另一张 color-only 反射颜色纹理；这张发布给 composite 的颜色纹理使用 `RenderTextureFormat.DefaultHDR`、`FilterMode.Bilinear`、`TextureWrapMode.Clamp`，不生成 mipmap，并且必须保持不带 depth/stencil，才能作为普通颜色 RT import 到 RenderGraph。

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
| `启用后处理合成` | 启用 `Ho-PlanarReflection Composite` fullscreen pass。内置路径依赖它把反射混回画面。 |
| `合成 Pass Event` | 默认 `BeforeRenderingPostProcessing`。需要在 MetadataBuffer / GeometryBuffer 和透明物体绘制之后，并在最终后处理之前。 |
| `合成 Shader` | 可覆盖默认 `Hidden/lilToon/URP/PlanarReflection/Composite`。 |
| `合成强度` | 写入 `_HoPlanarReflectionCompositeParams.x`。 |
| `法线扰动` | 写入 `_HoPlanarReflectionCompositeParams.y`，用 GeometryBuffer 法线扰动采样 UV。普通平面通过 `normalStrength = 0` 关闭扰动。 |
| `反射曝光 EV` | 写入 `_HoPlanarReflectionPreprocessParams.x` 的曝光倍率。`0` 表示原始亮度，`1` 表示乘 2。 |
| `圆盘模糊半径` | 写入 `_HoPlanarReflectionPreprocessParams.y`，单位为反射纹理像素。`0` 关闭预处理模糊。 |
| `屏幕边缘像素外扩` | 写入 `_HoPlanarReflectionCompositeOptions.z`，把越界扰动 UV clamp 到屏幕内侧采样。 |
| `最小 Smoothness` | 写入 `_HoPlanarReflectionCompositeParams.z`，smoothness 低于该值时渐隐。 |
| `反射 Tint` | 写入 `_HoPlanarReflectionCompositeTint`，RGB 乘到反射颜色，A 乘到合成权重。 |
| `反射纹理 Flip Y` | 写入 `_HoPlanarReflectionCompositeOptions.x`。 |
| `启用深度门控` | 写入 `_HoPlanarReflectionCompositeOptions.y`。 |
| `深度容差` | 写入 `_HoPlanarReflectionCompositeParams.w`，用于过滤扰动 UV 采到的非同一表面深度。 |

## 运行流程

### 镜像相机渲染

1. `HoPlanarReflectionRendererFeature.Create()` 注册 `RenderPipelineManager.beginCameraRendering`。
2. 每个源相机开始渲染时，RendererFeature 会选择一个可用 feature，发布合成相关全局参数，然后调用 `HoPlanarReflectionSurface.RenderAllSurfaces()`。
3. `RenderAllSurfaces()` 先重置全局反射状态：
   - `_HoPlanarReflectionCompositeActive = 0`
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
   - 临时翻转 `GL.invertCulling`，再调用 `UniversalRenderPipeline.RenderSingleCamera(context, reflectionCamera)`，渲染目标是带 depth/stencil 的反射相机 RT。
   - 恢复 culling 后，把反射相机 RT 拷贝到 color-only 反射纹理；后续全局状态和 RDG import 只使用这张 color-only 纹理。
   - 结束后恢复 `GL.invertCulling` 和目标 renderer 的 `forceRenderingOff`。
6. 成功渲染后，surface 通过 property block 写入目标 renderer，并同时发布一份全局反射输入给 composite pass。
7. 当本帧至少有一个 surface 成功，并且 `启用后处理合成` 打开时，运行时发布 `_HoPlanarReflectionCompositeActive = 1`。

### Buffer 写入

MetadataBuffer 和 GeometryBuffer 是独立渲染阶段，不是主材质 `ForwardLit` 期间的临时输出：

1. `HoMetadataBufferRendererFeature` 使用 `LightMode = "HoMetadataBuffer"` 绘制参与对象，写入 mask/id、surface data 和 custom 通道。
2. `HoGeometryBufferRendererFeature` 使用 `LightMode = "HoGeometryBuffer"` 绘制参与对象，写入世界法线和线性深度。
3. 普通 `lilPBR` 的 `HoMetadataBuffer` pass 会把平面反射参数写到 `Custom0`，`HoGeometryBuffer` pass 写法线深度。
4. `WaterFlowmap` 的同名 pass 写水面参数和法线深度。
5. 这些 buffer 在 `HoPlanarReflectionCompositePass` 执行前已经存在；composite pass 是唯一读取它们并采样反射纹理的位置。

### 反射预处理

RenderGraph 路径会把当前 surface 发布的 color-only `RTHandle` import 成 graph 资源。默认情况下 composite 直接读取这份反射输入；当 `反射曝光 EV != 0` 或 `圆盘模糊半径 > 0` 时，会额外记录一个 `Ho-PlanarReflection Preprocess` raster pass：

1. 读取 import 后的反射纹理。
2. 按 `圆盘模糊半径` 做固定采样的 disk blur，半径单位是反射纹理像素。
3. 按 `反射曝光 EV` 转换出的倍率乘到反射颜色。
4. 写入 RDG 临时纹理 `_HoPlanarReflectionProcessedTexture`。
5. 后续 composite pass 只采样 `_HoPlanarReflectionProcessedTexture`。

非 RenderGraph 兼容路径也会在需要时分配同名临时 `RTHandle` 做同样预处理，但主路径按 RDG 资源所有权处理。

### 后处理合成

合成 pass 的 RenderGraph 路径读取以下输入：

| 输入 | 来源 |
| --- | --- |
| camera color | 当前 `UniversalResourceData.activeColorTexture`。 |
| 反射 RTHandle | 最近一次成功发布的 color-only 平面反射纹理。RenderGraph 路径会先 import 它，再决定是否进入预处理 pass。 |
| `_HoPlanarReflectionProcessedTexture` | composite shader 实际采样的反射纹理。没有开启曝光或模糊时指向 import 后的原始反射输入。 |
| `_LILPBRPlanarReflectionParams` | 反射是否有效和纹理尺寸。 |
| `_HoMetadataBufferMaskIdTexture` | MetadataBuffer 的 mask/id RT，`maskId.r` 作为反射表面 mask。 |
| `_HoMetadataBufferMaterialCustom0_3Texture` | MetadataBuffer 的材质自定义通道，`Custom0.rgba` 提供合成参数。 |
| `_HoGeometryBufferNormalDepthTexture` | GeometryBuffer 的世界法线与线性深度。 |

fragment 处理逻辑：

1. 读取当前 camera color。
2. 如果调试模式是 `InputStatus`，直接输出输入状态：
   - R：合成是否 active 且反射纹理有效
   - G：MetadataBuffer 是否 active 且 mask/custom0 有效
   - B：GeometryBuffer normal/depth 是否有效
3. 如果合成未 active、MetadataBuffer 未 active、或 `_LILPBRPlanarReflectionParams.x <= 0.5`，普通模式返回原 camera color，调试模式返回洋红色。
4. 读取 `maskId`、`custom0`、`normalDepth`。
5. 计算表面权重：
   - `surfaceMask = saturate(maskId.r) * coverage(normalDepth)`
   - `smoothness = custom0.r`
   - `wetness = custom0.g`
   - `normalStrength = custom0.b`
   - `materialReflectionStrength = custom0.a`
   - `smoothnessFade = saturate((smoothness - minSmoothness) / (1 - minSmoothness))`
   - `centerWeight = surfaceMask * wetness * materialReflectionStrength * smoothnessFade`
6. 把 GeometryBuffer 的世界法线转到 view space，用 `normalVS.xy * distortion * normalStrength * wetness` 扰动屏幕 UV。普通平面写 `normalStrength = 0`，因此不会发生扰动。
7. 用 `屏幕边缘像素外扩` 把扰动后的 UV clamp 到屏幕内，避免采到纹理外。
8. 如果启用深度门控，采样扰动 UV 位置的 GeometryBuffer 深度，并按 `深度容差` 衰减跨物体采样。
9. 根据 `反射纹理 Flip Y` 可选翻转反射 UV。
10. 采样 `_HoPlanarReflectionProcessedTexture`，乘 `反射 Tint.rgb`。
11. 计算 `compositeWeight = centerWeight * depthGate * 合成强度 * Tint.a`。
12. `lerp(cameraColor.rgb, reflection, compositeWeight)` 写回新的 camera color。

兼容模式路径会先把 camera color blit 到 `_HoPlanarReflectionCompositeSource`，再用同一个 shader 合成回 camera color。

默认时序是：`HoMetadataBufferRendererFeature` 与 `HoGeometryBufferRendererFeature` 在 `AfterRenderingOpaques` 绘制各自的 buffer pass，透明表面再走 `ForwardLit`，最后 `HoPlanarReflectionCompositePass` 在 `BeforeRenderingPostProcessing` 读取 camera color、MetadataBuffer、GeometryBuffer 和反射纹理做 fullscreen 合成。只要没有把这些 pass event 调乱，合成读取时缓冲已经存在。

## IO 契约

### Surface 输出

| 名称 | 类型 | 写入位置 | 消费者 |
| --- | --- | --- | --- |
| `_UsePlanarReflection` | float | 目标 renderer property block | 材质的 MetadataBuffer pass，用于决定 `Custom0.a` 是否写出反射强度。 |
| `_LILPBRPlanarReflectionTexture` | texture | 目标 renderer property block 与全局 shader state | 保留给外部扩展、兼容入口和 DebugTile 原始反射预览；内置材质主 pass 不采样它，内置 composite 会把对应 `RTHandle` import 后统一走 `_HoPlanarReflectionProcessedTexture`。 |
| `_LILPBRPlanarReflectionTextureMatrix` | matrix | 目标 renderer property block 与全局 shader state | 保留给外部自定义投影需求；内置 composite 当前不依赖它。 |
| `_LILPBRPlanarReflectionParams` | float4 | 目标 renderer property block 与全局 shader state | `.x` 表示是否有效，`.y/.z` 是 RT 尺寸。Composite 用 `.x` 做有效性判断。 |

注意：property block 是 per-renderer 的，主要用于让目标材质 pass 写出正确的 `_UsePlanarReflection` 和保留外部扩展入口。全局 shader state 只有一份，后处理合成 pass 会读取最后一次成功发布的 surface。需要多块独立反射平面同时合成时，应给合成 pass 增加 per-surface 选择机制；当前实现更适合限制 `每相机最大表面数 = 1`，或只让一个主要反射平面参与合成。

### Composite 输入输出

| 名称 | 类型 | 含义 |
| --- | --- | --- |
| `_HoPlanarReflectionCompositeActive` | float | 本帧至少有一个 surface 成功渲染，且合成可用。 |
| `_HoPlanarReflectionCompositeParams` | float4 | `(strength, distortion, minSmoothness, depthTolerance)`。 |
| `_HoPlanarReflectionCompositeOptions` | float4 | `(flipY, enableDepthGate, edgeExtendDistance, 0)`。 |
| `_HoPlanarReflectionCompositeTint` | float4 | `(tint.r, tint.g, tint.b, tint.a)`。 |
| `_HoPlanarReflectionPreprocessParams` | float4 | `(exposureMultiplier, diskBlurRadiusPixels, 0, 0)`。 |
| `_HoPlanarReflectionDebugParams` | float4 | `(debugMode, debugDepthFar, debugDistortionScale, 0)`。 |
| `_HoPlanarReflectionDebugInputStatus` | float4 | `(reflection, maskId, normalDepth, custom0)`，RenderGraph 路径按实际资源有效性写入。 |
| `_HoPlanarReflectionProcessedTexture` | texture | Composite 实际采样的反射纹理。RDG 预处理开启时是临时 graph texture，否则是当前发布的原始反射 RT。 |
| `_HoMetadataBufferMaskIdTexture` | texture | `r` 是 mask coverage，合成中作为反射表面 mask。 |
| `_HoMetadataBufferMaterialCustom0_3Texture` | texture | `rgba` 是平面反射合成参数。 |
| `_HoGeometryBufferNormalDepthTexture` | texture | `rgb` 是编码世界法线，`a` 是线性深度。 |
| `_HoPlanarReflectionCompositeColor` | texture | RenderGraph 合成输出，赋回 `resourceData.cameraColor`。 |

### 普通 lilPBR 的 Custom0 语义

普通 `lilPBR` 没有水面法线扰动，因此默认写入：

| 通道 | 默认写入值 | 合成用途 |
| --- | --- | --- |
| `r` | `ShadingParams.smoothness` | 与 `最小 Smoothness` 计算 smoothness fade。 |
| `g` | `1` | 作为 wetness 权重保持满值，让普通平面不被水面湿度逻辑衰减。 |
| `b` | `0` | 关闭 normal distortion，走无扰动反射。 |
| `a` | `_PlanarReflectionStrength * (_UsePlanarReflection != 0)` | 材质级反射强度。 |

如果材质或 `HoMetadataBufferSubject` 写入 `_HoMetadataBufferCustomWriteMask >= 0.5`，普通 `lilPBR` 会改用 `_HoMetadataBufferCustomValues0`，并按 custom write mask 过滤通道。手动覆盖时要保证 0..3 四个 custom bit 覆盖到需要的通道，否则合成权重可能为 0。

### WaterFlowmap 的 Custom0 语义

`WaterFlowmap` 会为 MetadataBuffer pass 解析 `Custom0`：

| 通道 | 默认写入值 | 合成用途 |
| --- | --- | --- |
| `r` | `surface.smoothness` | 与 `最小 Smoothness` 计算 smoothness fade。 |
| `g` | `surface.wetness` | 衰减反射强度，并参与法线扰动。 |
| `b` | `_NormalStrength` | 控制扰动强度。 |
| `a` | `_PlanarReflectionStrength * (_UsePlanarReflection != 0)` | 材质级反射强度。 |

如果材质或 `HoMetadataBufferSubject` 写入 `_HoMetadataBufferCustomWriteMask >= 0.5`，`WaterFlowmap` 会改用 `_HoMetadataBufferCustomValues0`，并按 custom write mask 过滤通道。

## 调试

- RendererFeature Inspector 的 `运行状态` 会显示上一帧相机、surface 总数、本帧有效 surface 数和跳过原因。
- `调试模式 = InputStatus` 会把合成 pass 直接输出为 RGB 状态图：
  - 红色通道为反射输入是否有效。
  - 绿色通道为 MetadataBuffer mask/custom0 是否有效。
  - 蓝色通道为 GeometryBuffer 是否有效。
- 其他调试模式会显示 mask、smoothness、wetness、normal strength、reflection strength、world normal、linear depth、distortion、distorted UV、reflection color、composite weight、depth gate、custom0 或 edge extend。
- 如果不想替换整屏颜色，可以添加 `Ho-DebugTile` 并选择 `PlanarReflection` 条目做小窗调试。DebugTile 会在自己的 RDG pass 中 import 当前原始反射 RT，因此 `ReflectionColor` 小窗显示的是曝光/模糊预处理前的输入。

## 常见问题

### 完全没有反射

- 检查 URP Renderer Asset 是否添加并启用 `HoPlanarReflectionRendererFeature`。
- 检查 `启用后处理合成` 是否开启。内置材质不再走 ForwardLit 采样 fallback。
- 检查当前相机类型是否被 `渲染 Game View` / `渲染 Scene View` 允许。
- 检查 surface 的目标 renderer 是否存在且 enabled。
- 检查 `_LILPBRPlanarReflectionParams.x`。为 `0` 说明本帧 surface 没有成功发布反射。
- 检查材质 `_UsePlanarReflection`、`_PlanarReflectionStrength` 和 RendererFeature 的 `最小 Smoothness`。
- 如果权重已经为 1 但观感仍不明显，调高 `反射曝光 EV`，确认反射纹理本身不是接近 camera color 或黑色。

### 合成调试显示 Metadata 或 Geometry 缺失

- 确认添加并启用了 `HoMetadataBufferRendererFeature` 和 `HoGeometryBufferRendererFeature`。
- 确认两个 buffer feature 的 layer mask 和 render queue 覆盖反射表面。
- 确认材质有 `HoMetadataBuffer` 和 `HoGeometryBuffer` pass。
- 确认 `合成 Pass Event` 在 MetadataBuffer / GeometryBuffer 的 pass event 之后。

### 材质绘制时缓冲还没画好怎么办

不依赖这个时机。`ForwardLit` 主材质 pass 不读取 MetadataBuffer / GeometryBuffer，也不采样平面反射纹理；它只是正常把物体画进 camera color。MetadataBuffer / GeometryBuffer 是为后面的 fullscreen composite pass 准备的独立输入，所以只要 composite 的 pass event 在它们之后即可。

### 普通反射和水面反射是不是两种消费方式

不是。它们都走同一个 composite pass，区别只在 `Custom0` 参数：普通 `lilPBR` 写 `wetness = 1`、`normalStrength = 0`，所以不扰动 UV；水面额外写 wetness 和 normal strength，让同一套后处理逻辑产生水面扰动和权重变化。

### 反射被镜面本身挡住

- 开启 `反射中隐藏本体`。
- 把镜面自身从 `反射层遮罩` 中排除。
- 对很贴近镜面的相机，适当增大 `最小裁剪距离` 或检查 `使用平面裁剪`。

### 合成过强或边缘拉伸明显

- 降低 `合成强度`、材质 `_PlanarReflectionStrength`、水面 wetness 或 `Tint.a`。
- 降低 `反射曝光 EV` 或 `圆盘模糊半径`。
- 降低 `法线扰动` 或水面 normal strength。
- 调小 `屏幕边缘像素外扩` 可以减少边缘复制，但可能露出越界断层。
- 对局部水面可尝试开启 `启用深度门控`，并设置合适的 `深度容差`；大面积平水面通常保持关闭更稳定。

## 性能注意

- 每个 active surface 都会额外渲染一次场景。`每相机最大表面数`、`更新帧间隔`、`反射分辨率` 和 `反射层遮罩` 是主要性能控制点。
- 反射相机关闭 MSAA 与 occlusion culling，减少额外成本并避免反射视角裁剪不稳定。
- 后处理合成额外读取 camera color、MetadataBuffer、GeometryBuffer 和反射纹理。普通平面与水面共用同一个合成 pass；普通平面不做法线扰动，因此成本主要来自一次 fullscreen pass 和一次反射纹理采样。

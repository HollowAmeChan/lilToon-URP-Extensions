# PLR（Planar Reflection）

PLR 为水面、光滑地面、玻璃和镜子生成镜像相机反射源。它只生产 radiance source，不决定材质最终反射强度；最终消费必须结合 roughness、metallic、reflectance/F0、Fresnel 和接收 mask。

当前 opaque lilToon ForwardLit 已直接消费 source；fullscreen composite 只保留给水面/OIT、调试或明确声明的特殊材质，并且默认关闭。

PLR 的 PBR 材质路径只支持完整 lilToon Forward，不接入 lilToonLite/Gem；本管线不为低质量材质变体维护另一套反射模型。

## 快速设置

1. 启用 `HoMetadataBufferRendererFeature` 和 `HoGeometryBufferRendererFeature`。
2. 启用 `HoPlanarReflectionRendererFeature`，在需要透明/OIT 合成时才打开 `启用特殊表面后处理合成`。
3. 在水面、镜面或光滑地面 renderer 上添加 `HoPlanarReflectionSurface`。
4. 设置反射层遮罩，通常排除接收平面自身；按需要设置分辨率、更新帧间隔、裁剪和 Scene View 开关。
5. opaque ForwardLit 只依赖 surface 写入的 source 与 `_UsePlanarReflection`；特殊 fullscreen 路径还要求材质拥有 `HoMetadataBuffer` 与 `HoGeometryBuffer` pass。

## Surface 参数

| 参数 | 语义 |
| --- | --- |
| `目标渲染器` | 接收 property block 的 renderer；留空使用当前 GameObject 的 Renderer |
| `反射平面锚点` | 平面位置取 Transform.position，法线取 Transform.up |
| `反射层遮罩` | 镜像相机的 culling mask |
| `反射分辨率` | color-only source 的宽度；高度按源相机宽高比计算 |
| `反射中隐藏本体` | 生成 source 时临时关闭目标 renderer |
| `更新帧间隔` | 大于 1 时复用上次 source |
| `使用平面裁剪` / `裁剪平面偏移` | oblique clip plane 及其偏移 |
| `复制相机清屏设置` | 是否复制源相机 clear flags 和背景色 |

镜像相机仍使用带 depth/stencil 的 HDR target；渲染后复制到不带 depth/stencil 的 color-only RT，并通过逐级 13-tap tent downsample 生成完整 HDR 预过滤 mip chain。后者才是对外发布的 PLR source，ForwardLit 用 perceptual roughness 选择 mip；预过滤 shader 缺失时才回退到 Unity `GenerateMips`。

## 运行时边界

```text
beginCameraRendering
  -> 选择有效 surface
  -> 镜像 source camera
  -> oblique clip + GL.invertCulling
  -> RenderSingleCamera
  -> 发布 color-only source 与有效性参数
```

递归相机、Reflection/Preview 相机、被禁用的 Game/Scene View 或超过 `每相机最大表面数` 时跳过 surface，并把 `_UsePlanarReflection` 清零。ForwardLit 通过每个 Renderer 的 PropertyBlock 读取各自 source，因此支持多个 surface；特殊 fullscreen 路径仍只有一个全局 source，在 source id/PLR 专用 receiver RT 落地前，多于一个有效 surface 时会自动关闭特殊合成。

## 反射输入契约

下面的公共 buffer 供特殊 fullscreen resolve、SSR 和后续统一反射消费；opaque ForwardLit 使用同一套材质语义直接计算：

| 输入 | 读取内容 |
| --- | --- |
| `_HoMetadataBufferReflectionMaterialTexture` | `R=perceptualRoughness`、`G=metallic`、`B=reflectance`、`A=PLR 接收强度` |
| `_HoMetadataBufferSurfaceColorTexture` | 线性 baseColor 提示；RGB 不钳制，A 为 coverage |
| `_HoMetadataBufferMaskIdTexture` | 接收面 mask/id |
| `_HoGeometryBufferNormalDepthTexture` | RGB 世界法线编码，A 线性深度/coverage |

`_LILPBRPlanarReflectionParams` 的冻结语义为 `(valid, width, height, maxMipLevel)`；`maxMipLevel` 用于 roughness-aware source 采样。

规范化响应：

```text
roughness = perceptualRoughness²
F0        = lerp(reflectance, baseColor, metallic)
```

smoothness map、MetallicGlossMap 和 GSAA 必须在 lilToon producer 端完成。不要在 PLR fullscreen shader 中通过 `_Smoothness` 或 Custom0 重新推导。

GeometryBuffer 当前没有 `Custom0` producer。若需要 per-pixel PLR receiver 数据，新增槽位必须直接命名为 PLR 专用 RT，并单独冻结 RGBA 语义；不得复用 MetadataBuffer 的通用 Custom0。

## 时序要求

```text
PLR source update         -> beginCameraRendering
Metadata/Geometry output  -> BeforeRenderingOpaques（或更早）
Opaque ForwardLit         -> 直接消费 PLR（已实现）
Transparent/OIT           -> PLR 专用 composite（过渡路径）
SSR                       -> AfterRenderingOpaques，作为屏幕内补充
Probe/Sky                 -> PLR/SSR miss fallback
```

同一表面不能同时启用 ForwardLit PLR 与特殊 fullscreen PLR resolve，否则会重复累计间接高光。

## 调试与排查

- `metadata.reflection-material`：检查 roughness、metallic、reflectance、PLR strength。
- `metadata.surface-color`：检查 baseColor 与 coverage；HDR 高于 1 的 RGB 是允许的。
- `geometry.world-normal` / `geometry.linear-depth`：检查物理几何输入；描边不得成为 coverage。
- PLR source debug：检查镜像相机内容、Flip Y、source 有效性和分辨率。
- 无反射时依次检查 Feature、surface renderer、反射层遮罩、`_UsePlanarReflection`、buffer layer mask/render queue，以及 source 是否被清成 black。

## 后续实现顺序

1. 完成多平面 source 选择和 PLR 专用 receiver RT。
2. 评估用 GGX importance sampling 进一步替换当前能量保持的 tent 预过滤。
3. 再接入 SSR、Probe/Sky fallback 与玻璃/水面的透明扩展。

PLR 不再维护 lilToon 卡通反射模式；材质统一使用 glTF/PBR 风格的 smoothness、metallic、baseColor 和 reflectance 输入。

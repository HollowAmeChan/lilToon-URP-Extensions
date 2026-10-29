# PLR（Planar Reflection）

> **状态：现行功能说明**（2026 文档审核时按 R6/R7 之后的实现校正：MB 的桥接输入已删除，数值改读 SB、遮罩改读 OB）。

PLR 为水面、光滑地面、玻璃和镜子生成镜像相机反射源。它只生产 radiance source，不决定材质最终反射强度；最终消费必须结合 roughness、metallic、reflectance/F0、Fresnel 和接收 mask。

当前 opaque lilToon ForwardLit 已直接消费 source；fullscreen composite 只保留给水面/OIT、调试或明确声明的特殊材质，并且默认关闭。

PLR 的 PBR 材质路径只支持完整 lilToon Forward，不接入 lilToonLite/Gem；本管线不为低质量材质变体维护另一套反射模型。

## 快速设置

1. 按管线总览的列表顺序放置 PLR（Atmosphere 后、GeometryBuffer 前）。镜像源在 `beginCameraRendering` 生成；特殊合成和调试在 GB/OB/SB/AC 产出之后执行，不能用 Feature 列表位置代替 pass 时序。
2. 启用 `Ho-PlanarReflection` Renderer Feature，在需要透明/OIT 合成时才打开 `启用特殊表面后处理合成`。
3. 在水面、镜面或光滑地面 renderer 上添加 `Ho-PlanarReflection Surface`。
4. 设置反射层遮罩，通常排除接收平面自身；按需要设置分辨率、更新帧间隔、裁剪和 Scene View 开关。
5. opaque ForwardLit 只依赖 surface 写入的 source 与 `_UsePlanarReflection`；特殊 fullscreen 路径还要求材质拥有 `HoGeometryBuffer` pass 与 SB 的材质 pass（`HO_SURFACE_BUFFER`，22 个 lilToon URP lilblock 都带）。
6. 特殊合成与材质调试还需要 GB、OB、SB、AC，以及接收 Renderer 的 `HoObjectBufferGroup` 身份。没有身份时，SB owner=0，AC 返回空遮罩；仅把 Feature 填进列表不能赋予物件身份。场景物件使用 `tags=None`，不要添加角色标签。

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

## 输入（已换到 OB / SB / GB）

特殊 fullscreen resolve 的输入**只**来自现役 producer；opaque ForwardLit 直接使用材质值：

| 输入 | 来源与读取内容 |
| --- | --- |
| 接收面遮罩 | **OB 的 `coverageTexture`**（四层身份覆盖率之和）；composite 里用 `HoAC_TotalCoverage(uv)` 读，同一份也用于"这一格有多少属于角色" |
| 材质数值 | **SB 的 `Material` / `Reflection`**，经 AC 门面 `HoAC_Attribute(uv, 1)`（perceptualRoughness / metallic / thickness）与 `HoAC_Attribute(uv, 2)`（reflectance / plrStrength）两行拼出老的 `reflectionMaterial` 四通道序 |
| 表面色提示 | **SB 的 `_HoSurfaceBufferColorTexture`**（线性 HDR，`Color.a` 不承载覆盖率） |
| 几何 | `_HoGeometryBufferNormalDepthTexture`（RGB 世界法线编码，A 线性深度 / coverage） |
| 有效性 | `HoAC_SurfaceValid(uv)`：SB 有产出 **且** SB 的 owner 与 OB 层 0 身份一致 |

`_LILPBRPlanarReflectionParams` 的冻结语义为 `(valid, width, height, maxMipLevel)`；`maxMipLevel` 用于 roughness-aware source 采样。

规范化响应：

```text
roughness = perceptualRoughness²
F0        = lerp(reflectance, baseColor, metallic)
```

smoothness map、MetallicGlossMap 和 GSAA 必须在 lilToon producer 端完成。不要在 PLR fullscreen shader 中通过 `_Smoothness` 或 Custom0 重新推导。目标输入归属和迁移顺序见 [`ReflectionPipelineDesign.md`](ReflectionPipelineDesign.md)。

## 时序要求

```text
PLR source update         -> beginCameraRendering
GB / OB / SB output       -> BeforeRenderingOpaques（或更早）
Opaque ForwardLit         -> 直接消费 PLR（已实现）
Transparent/OIT           -> PLR 专用 composite（过渡路径）
SSR                       -> AfterRenderingOpaques，作为屏幕内补充
Probe/Sky                 -> PLR/SSR miss fallback
```

同一表面不能同时启用 ForwardLit PLR 与特殊 fullscreen PLR resolve，否则会重复累计间接高光。

PLR 始终服从 lilToon 的 `_UseReflection` 总开关。总开关关闭时，即使 `_UsePlanarReflection`、surface 和 source 都有效，也不会产生 ForwardLit 或特殊 fullscreen PLR；Probe/Sky fallback 同样关闭。

## 调试与排查

DebugTile / 整屏调试里 PLR 有自己的一整套视图（`planar-reflection.*`，与 `HoPlanarReflectionDebugMode` 一一对应）：

- `planar-reflection.inputs`：RGB 输入自检 —— R = source 句柄存在、G = AC/覆盖率/颜色/反射属性资源存在、B = 几何资源存在，A 恒 1。白色表示资源存在，仍需检查 Mask/属性图确认接收像素。
- `planar-reflection.surface-mask` / `perceptual-roughness` / `metallic` / `reflectance` / `reflection-strength`：逐项对齐 composite 的输入。
- `planar-reflection.composite-weight` / `depth-gate` / `edge-extend`：特殊合成权重、深度门控与边界。它们不代表 ForwardLit 的最终逐材质权重。
- `planar-reflection.reflection-color`：未遮罩的 source mip 0；多 surface 时显示最后一个成功渲染的 source。全屏合成关闭或存在多个 surface 不再使调试返回紫色。
- `geometry.world-normal` / `geometry.linear-depth`：物理几何输入；描边不得成为 coverage。
- PLR source debug：检查镜像相机内容、Flip Y、source 有效性和分辨率。

无反射时依次检查 Feature、surface renderer、反射层遮罩、材质 `_UseReflection`/`_UsePlanarReflection`，并读回 source 的 mip 0 和粗糙度使用的 mip。原图有内容而 mip 1+ 全黑，仍然会让光滑度小于 1 的材质几乎没有反射。预过滤必须显式采样输入的 LOD 0；隐式 LOD 会在第一次缩小时读到尚未生成的 mip 1。

`Ready=true` 只代表镜像源被分配并提交渲染，不证明像素非黑或最终地面有反射。使用 `HoMmdPlrValidation.Run -plrMode on/off/debug -hoStage 孔雀月令` 做同镜头开关对比、source/mip 读回；debug 模式还执行 `HoMmdPlrGpuCheck.CheckPrefilter` 的恒定 HDR 能量测试。CLI 入口复制到项目制作工具后使用，不加 `-quit` 或 `-nographics`。

回归检查：将材质 `_UseReflection = 0`、`_UsePlanarReflection = 1`；`Reflection.g`（PLR 接收强度）应为 0，ForwardLit 与特殊 composite 都必须保持原色。再打开 `_UseReflection` 后，才允许按 `_UsePlanarReflection` 和 source 有效性产生 PLR。

## 后续边界

PLR 后续只处理水面/玻璃的直接消费、必要时的 receiver/source-id RT，以及预过滤质量评估。SSR、Probe 工作流和完整优先级统一在 [`ReflectionPipelineDesign.md`](ReflectionPipelineDesign.md) 规划，本文不再复制路线图。

PLR 不维护 lilToon 卡通反射模式；材质统一使用 glTF/PBR 风格的 smoothness、metallic、baseColor 和 reflectance 输入。

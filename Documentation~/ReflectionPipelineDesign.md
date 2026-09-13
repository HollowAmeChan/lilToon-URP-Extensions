# HoRP 反射方案与输入契约

> 状态：设计冻结稿（2026-09-13）
> 目标：先冻结两个 buffer 的生产槽位，再修复 PLR，最后接入 SSR 与探针消费。
> 约束：实验性管线；不做旧资产兼容，不为移动端低质量档牺牲算法质量。

## 1. 名词与范围

- **PLR（Planar Reflection）**：镜像相机生成的平面反射源。使用缩写 PLR，避免“平面反射”在代码和调试视图中反复出现。
- **SSR（Screen-Space Reflection）**：当前相机可见内容的屏幕空间反射源。
- **Probe/Sky**：Reflection Probe 或天空盒提供的屏幕外 fallback。
- **Reflection response**：材质对 radiance 的响应，包括 roughness、metallic、reflectance、F0、Fresnel 和接收权重。

第一批需要反射的表面：水面、光滑地面、玻璃、镜子。透明/OIT 表面先走 PLR + 专用合成；opaque 的 SSR 在后续阶段接入。动态探针先实现“消费图片”的路径，探针布置和用户工作流另行设计。

## 2. 已冻结的两个 Buffer

### 2.1 MetadataBuffer：材质与对象语义

MetadataBuffer 的 MRT 槽位固定如下，新增反射输入不得挪用已有 SSS 或角色语义：

| 槽位 | 全局资源 | 语义 | 反射用途 |
| --- | --- | --- | --- |
| Target0 | `_HoMetadataBufferMaskIdTexture` | mask / group / object id | 接收面选择 |
| Target1 | `_HoMetadataBufferSurfaceDataTexture` | thickness、curvature、material/profile、transmittance | 非反射材质语义；保持给 SSS |
| Target2 | `_HoMetadataBufferMaterialCustom0_3Texture` | 通用材质 Custom0-3 | 不再定义为反射材质结构 |
| Target3 | `_HoMetadataBufferObjectCustom0_3Texture` | 对象位语义 | 角色与 matte |
| Target4 | `_HoMetadataBufferObjectCustom4_7Texture` | 对象位语义 | 角色与 matte |
| Target5 | `_HoMetadataBufferReflectionMaterialTexture` | 规范化反射材质输入 | PLR / SSR / Probe |

Target5 当前编码为：`R = perceptualRoughness`、`G = metallic`、`B = reflectance`、`A = PLR 接收强度（由 _UseReflection 与 _UsePlanarReflection 共同门控）`。生产端必须复用 lilToon 的 smoothness map、MetallicGlossMap 和 GSAA 规则；消费者再按需要计算 `roughness = perceptualRoughness²` 与 F0。

### 2.2 GeometryBuffer：物理几何真值

当前正式输出只有：

| 资源 | 语义 |
| --- | --- |
| `NormalDepth` | RGB 编码世界法线，A 为线性 eye depth；A>0 表示物理几何 coverage |
| `DepthTexture` | GeometryBuffer 内部深度/模板附件，不是公共线性深度输入 |
| `OutlineNormalDepth` | 描边视觉壳的法线/深度；不得作为物理表面 |
| `SkyTexture`（可选） | 无几何区域的天空 radiance |

仓库当前没有 GeometryBuffer `Custom0` 的生产 pass 或消费者。文档中不再把它描述成已存在的通用通道；若实现 PLR receiver 扩展，直接把该槽位命名为 PLR 专用 RT，并在实现时一次性冻结其 RGBA 语义，不恢复为泛用 Custom0。

所有屏幕空间反射都必须先用 `NormalDepth` coverage gate。描边不得进入反射 ray、深度金字塔或 history。

## 3. SurfaceColor 决议

`_HoMetadataBufferSurfaceColorTexture` 是未完整光照的材质表面色，不是最终颜色，也不是自动钳制到 `[0,1]` 的 albedo GBuffer：

| 通道 | 冻结语义 |
| --- | --- |
| RGB | 线性材质表面色，保留 HDR 能量，不在 producer 端 `saturate` |
| A | 材质 coverage，范围 `[0,1]` |

已核对的消费者：

- SSS Source 直接用 RGB 构造散射源；保留 HDR 可避免先压低能量。
- Face/Hair diffuse 直接乘语义 mask 使用 RGB。
- DebugTile 和 Metadata debug 直接显示 RGB。
- 未来反射只把它当 baseColor 提示；需要物理范围时由消费者在计算 F0、曝光或显示时自行钳制。

因此不增加“钳制版 SurfaceColor”。如果某个新消费者需要 `[0,1]` albedo，必须在该消费者中显式 `saturate` 并说明原因。

## 4. lilToon 材质语义

lilToon 的 `_Smoothness`、`_Metallic` 及其贴图可以作为常见 PBR 输入；lilToon 仍然负责最终 NPR 光照表现。反射输入按 glTF/PBR 习惯解释：

```text
perceptualRoughness = 1 - finalSmoothness
roughness           = perceptualRoughness²
F0                  = lerp(reflectance, baseColor, metallic)
```

`finalSmoothness` 必须已经应用 smoothness map 与 GSAA，不能在 fullscreen pass 中重新猜测 `_Smoothness`。不再维护 lilToon 旧的卡通反射分支；反射统一走 PBR 输入，风格差异通过 lilToon 材质模型和响应参数调整。

## 5. 反射来源与消费边界

```text
PLR source ─┐
SSR source ─┼─> source hierarchy ─> material response ─> resolve
Probe/Sky ──┘
```

- 镜子、光滑地面、明确的水面：PLR 优先，SSR 作为屏幕内细节。
- 普通 glossy opaque：SSR 命中，miss 回退 Probe/Sky。
- 玻璃和水面/OIT：第一版 PLR + 专用透明合成；SSR 作为后续扩展。
- 任何 source miss 都不能直接变黑。

PLR 的镜像相机、oblique clip、`GL.invertCulling` 和 color-only RT 继续保留。需要修复的重点是消费端：按最终 roughness、metallic、F0/Fresnel 和接收 mask 采样 source，而不是把 source 作为 camera color 做无条件 `lerp`。统一 fullscreen composite 仅保留给水面/OIT/调试等明确声明的特殊路径，避免与材质内响应双重合成。

SSR 需要 GeometryBuffer normal/depth、ReflectionMaterial roughness 和 camera color/depth pyramid，输出 `ReflectionColor.rgb + Confidence.a`；先做高质量 linear tracing，再做 Hi-Z、temporal 和 denoise。SSR 只支持 opaque 起步。

探针消费沿用 lilToon/URP 的环境反射采样（roughness mip、probe blend、sky fallback）。PLR 不绕过反射总开关：`_UseReflection = 0` 时 PLR、Probe/Sky 和后续 SSR 都必须停止消费。探针如何布置、绑定和在 Inspector 中配置不属于本阶段的生产契约。

## 6. 时序冻结

```text
beginCameraRendering       -> PLR source update
BeforeRenderingOpaques     -> MetadataBuffer + GeometryBuffer
Opaque ForwardLit           -> 消费 PLR（修复后）
AfterRenderingOpaques       -> color pyramid + SSR
BeforeRenderingTransparents-> 玻璃/水面/OIT 专用路径
BeforeRenderingPostProcessing -> 必要的 fullscreen resolve / debug
```

实际 RendererFeature 的 `passEvent` 可以覆盖默认值，但必须满足：buffer 生产早于消费者；SSR 读取 camera color 前不得提前执行；同一相机不能同时走材质 PLR 和全屏 PLR 合成。

## 7. 实施顺序

1. **Phase 0：冻结输入（已完成）**
   - 保持 MetadataBuffer Target0-5 语义不变。
   - 保持 GeometryBuffer 的物理/描边分离；未来 `Custom0` 只允许登记为 PLR 专用 RT。
   - 保持 SurfaceColor RGB 不钳制、A 为 coverage。
   - 为 ReflectionMaterial、SurfaceColor、NormalDepth 提供 debug view。
2. **Phase 1：修复 PLR（进行中，最高实现优先级）**
   - PLR source 生成保持现有镜像相机流程。
   - ForwardLit 已按与 ReflectionMaterial 相同的 smoothness/metallic/reflectance 规则计算 PBR 反射响应。
   - source 已用逐级 13-tap tent downsample 生成 HDR 预过滤 mip chain，并按 perceptual roughness 采样；默认 fullscreen composite 已关闭。
   - 后续评估 GGX importance-sampled 预过滤，并完成水面法线扰动的专用输入。
   - 多平面选择机制在 source id/PLR 专用 RT 契约冻结后实现。
3. **Phase 2：SSR**
   - 输出颜色和 confidence，使用 GeometryBuffer 与 ReflectionMaterial。
   - linear tracing 之后接入 Hi-Z；miss 回退 PLR/Probe/Sky。
4. **Phase 3：Probe/Sky 消费与透明扩展**
   - 先完成图片采样、roughness mip 和 fallback。
   - 再处理探针布置工作流、玻璃和水面的 SSR/折射扩展。

## 8. 明确删除的旧假设

- 不再把 `Custom0.rgba` 定义成 `smoothness/wetness/normalStrength/reflectionStrength` 的通用反射契约。
- 不再把 PLR 描述为只在 After Opaque 做 camera-color `lerp` 的唯一正式路径。
- 不再保留或新增 lilToon 卡通反射模式。
- 不再为旧资产、移动端或低质量档保留专门的反射降级设计。

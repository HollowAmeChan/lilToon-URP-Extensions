# lilToon / Ho-GI 实施规划

> 状态：Draft v0.1
>
> 目标：先完成可控、可调试的 Ho-SSGI，验证 lilToon 的 GI 接收契约；再在不改材质消费接口的前提下接入 Brixelizer GI。

## 0. 结论

当前推荐路线：

```text
Ho-SSGI v1
    -> lilToon GI 接收与 NPR 风格化
    -> 朱木古堂验收
    -> Ho-SSGI 作为 fallback
    -> Brixelizer GI 世界空间增强
```

不把 Lumen、硬件 RTGI 或完整 ReSTIR GI 作为主线。DDGI 保留为后续可选研究，不作为当前实现的前置条件。

Brixelizer GI 值得作为第二阶段的原因：它使用稀疏 SDF、screen probes 和 radiance cache，能够覆盖屏幕外几何，官方提供 HLSL/SDK/样例，并以 MIT 许可发布；但它要求 DX12/Vulkan 和 HLSL CS 6.6，Unity 接入成本明显高于 SSGI。

参考：

- [AMD FidelityFX Brixelizer/GI](https://gpuopen.com/fidelityfx-brixelizer/)
- [Brixelizer GI 技术文档](https://gpuopen.com/manuals/fidelityfx_sdk/techniques/brixelizer-gi/)
- [FidelityFX SDK 源码](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK)

## 1. 当前边界

### 1.1 GI 不依赖 MetadataBuffer

`HoMetadataBuffer` 已经定位为角色语义和后期 feature 的输入，不应成为 GI 的前置依赖。

GI 直接依赖：

- `HoGeometryBuffer`：屏幕法线、深度、天空信息；
- 当前相机颜色或独立的 GI radiance source；
- APV/天空 fallback；
- 运动矢量或深度/法线/历史校验；
- 后续 Brixelizer 的世界空间 SDF 与 radiance cache。

MetadataBuffer 可以继续提前到 `BeforeRenderingOpaques`，供 CharacterSpecialization、SSS、AOV 和 ScreenProcess 使用，但 GI 不读取它。当前 GeometryBuffer 和 MetadataBuffer 的 RenderGraph 资源分别位于：

- `Runtime/GeometryBuffer/HoGeometryBufferRenderGraphResources.cs`
- `Runtime/MetadataBuffer/HoMetadataBufferRenderGraphResources.cs`

### 1.2 不把 SurfaceColor 当成最终 GI 辐射

MetadataBuffer 的 `SurfaceColor` 是材质颜色/反照率语义，不是经过直接光照后的 outgoing radiance。SSGI 如果只采它，会得到材质颜色扩散，而不是可靠的间接光。

因此 Ho-SSGI 至少需要以下一种 source：

1. 不含描边的直接光照 source；
2. 当前帧相机颜色，但配合 metadata/coverage 排除非物理表面；
3. 前一帧的 radiance history；
4. APV/天空作为射线未命中 fallback。

首版可以使用经过明确遮罩的相机颜色验证链路，但不能把它作为最终架构。描边问题记录已经证明，直接读取包含外扩描边的 camera color 会产生白边和错误颜色反弹。

## 2. Ho-SSGI v1

### 2.1 目标

- 复用 Ho-GeometryBuffer，不再创建 HTrace 的独立 normal/depth GBuffer；
- 不创建 MetadataBuffer 专用输入；
- 输出独立的 GI 语义纹理，不直接修改材质最终颜色；
- 明确排除 outline、非物理壳和不参与 GI 的对象；
- 先做单 bounce、屏幕空间增强，不追求多 bounce；
- 保留 APV/天空 fallback，使屏幕外区域不变黑。

### 2.2 输出契约

建议生产端发布：

- `_HoGITexture`：HDR 间接光颜色；
- `_HoGIConfidenceTexture`：命中、fallback、历史有效性；
- `_HoGIBentNormalTexture`：可选，用于环境补光和 debug；
- `_HoGITexture` 未生成时回退为黑色，confidence 回退为 0。

材质或 ScreenProcess 只消费这些语义名，不读取 HTrace/Brixelizer 算法名。这样 Brixelizer 后续可以替换 Ho-SSGI producer，而不改 lilToon 接收端。

### 2.3 建议 pass 链

第一版以 RenderGraph 为主：

```text
HoGeometryBuffer (250)
    -> Ho-SSGI source / depth pyramid
    -> ray march
    -> temporal history
    -> bilateral / spatial filter
    -> APV / sky fallback
    -> _HoGITexture + confidence
    -> lilToon / ScreenProcess 接收
```

Ho-SSGI 不应该复制 HTrace 的完整 GBuffer、独立深度、独立 rendering layer 和重复的 prepass。深度金字塔可以从 Ho-GeometryBuffer 的深度派生。

### 2.4 质量档

| 档位 | tracing | denoise | 用途 |
|---|---|---|---|
| Low | 半分辨率、4-8 rays | 单次 bilateral | 调试链路和低成本预览 |
| Medium | 半分辨率、8-16 rays、Hi-Z | temporal + bilateral | 朱木古堂默认验收档 |
| High | 全/半分辨率、16-32 rays | temporal validation + spatial | 静帧和作品集输出 |

Temporal history 必须使用深度、法线、相机运动和材质/对象稳定性校验。第一版不需要完整逐物体 motion-vector pass，但动态角色必须有明确的 history rejection 策略。

## 3. lilToon 接收

GI producer 和材质接收端分开验收。

### 3.1 接收顺序

1. 先做全屏 composite，仅验证 `_HoGITexture` 的颜色、强度、遮罩和 confidence；
2. 再接入 lilToon 的 indirect/toon shadow 逻辑；
3. 最后决定是否增加 material-side GI intent；
4. outline pass 不读取 GI，也不参与 GI source。

### 3.2 NPR 风格化

GI 不应直接按 PBR 方式叠加到 beauty：

- 主要影响 toon 阴影侧和环境补光；
- 支持 GI strength、color tint、luminance remap、contrast 和 clamp；
- 对直接光照区域设置较低权重，避免平面色阶被洗掉；
- AO 只削弱 GI/ambient，不直接污染主光；
- confidence 低时使用 APV/天空或保守 fallback。

## 4. 描边与非物理表面

Ho-SSGI 的 source、receiver、caster 都要有明确排除策略：

- 不从含 outline 的 camera color 直接取 radiance；
- outline 不写入 GI source；
- outline 不接收 GI；
- 深度/法线不一致时拒绝 screen hit；
- 不命中 metadata 不能作为“有效物理表面”参与 GI。

这是 HTrace 白边问题的核心修复方向，而不是继续调整 ray length 或 brightness clamp。

## 5. Brixelizer GI 过渡

当 Ho-SSGI 链路和 lilToon 接收契约稳定后，再实现 Brixelizer backend：

1. 建立动态/静态几何注册和稀疏 SDF 更新；
2. 将 HoGeometryBuffer 作为 visible surface 输入；
3. 将直接光照或前一帧 GI 写入 radiance cache；
4. 使用 screen probes 生成世界空间 irradiance；
5. 输出相同的 `_HoGITexture` 和 confidence；
6. 保留 Ho-SSGI 作为平台/场景 fallback。

Brixelizer 不应先改 lilToon 材质接口。它只替换 GI producer。

### 5.1 Brixelizer 的接入限制

- 需要 DX12/Vulkan 和 HLSL CS 6.6；
- 需要处理 Unity RenderGraph 与 native/compute resource 的桥接；
- 动态 skinned mesh 的 SDF 更新成本要单独测量；
- 需要 debug SDF、probe、radiance cache 和 leak/relocation 视图；
- 不能把 Brixelizer SDK 当作普通 Unity C# 包直接拷贝进来。

## 6. DDGI 的位置

DDGI 保留为第三阶段候选：

- 适合有硬件 RT 或专门 probe tracing 的平台；
- 需要 probe placement、visibility、relocation、irradiance moments 和历史更新；
- 源码和许可证要单独审核，尤其是 NVIDIA RTXGI 不能按 MIT 代码处理；
- 如果 Brixelizer 已满足世界空间动态 GI，就没有必要再并行维护一套 DDGI。

## 7. 朱木古堂验收矩阵

Ho-SSGI v1 至少要通过：

1. 红墙/白地面的颜色反弹；
2. 室内遮挡和近距离 contact indirect；
3. 镜头移动时的 temporal 稳定性；
4. 角色移动、头发和描边不出现白边；
5. 关闭 Ho-SSGI 后回退为无 GI，不读上一帧；
6. APV/天空 fallback 与 screen hit 的交界不闪烁；
7. 平面反射、SSS、OIT 和 CharacterSpecialization 不被 GI 改坏；
8. 将 source、GI、confidence、最终 composite 分别 debug 输出。

验收顺序固定为：

```text
APV only
  -> Ho-SSGI source only
  -> Ho-SSGI + temporal
  -> Ho-SSGI + lilToon receiver
  -> Ho-SSGI + outline/SSS/OIT
  -> Brixelizer producer replacement
```

## 8. 完成标准

Ho-SSGI 阶段完成的标准：

- HTrace 的独立 GBuffer/prepass 不再是 Ho-GI 必需输入；
- lilToon 只依赖 GI 语义输出，不依赖算法名；
- 朱木古堂的描边、角色、透明和后期链路通过验收；
- GI 关闭时所有纹理有确定 fallback；
- RenderDoc/Frame Debugger 能定位 source、trace、history、denoise、composite；
- Brixelizer 可以作为 producer 替换，而不修改材质消费契约。

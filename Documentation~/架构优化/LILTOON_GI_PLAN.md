# lilToon / Ho-SSGI 实施规划

> 状态：Draft v0.2
>
> 主线：先把 HTrace SSGI 改造成适合 lilToon 的 Ho-SSGI；Brixelizer GI 只作为后续 producer 替换，不提前展开完整实现。

## 0. 结论

当前最实际的路线是：

```text
Ho-SSGI（HTrace 改进）
    -> lilToon GI 接收与 NPR 风格化
    -> 朱木古堂验收
    -> 保持同一 GI 输出契约
    -> 后续可替换为 Brixelizer GI
```

不为低质量档另做一套算法。先确定一条高质量路径，用分辨率、ray count、history length 和 denoise 参数控制成本；Low/Medium 只改变参数，不改变算法结构。

Brixelizer GI 只保留为后续方向。它是 compute/SDF/radiance-cache 路线，不是硬件 RTGI，但需要 DX12/Vulkan、HLSL CS 6.6 和较重的世界空间接入。[官方资料](https://gpuopen.com/fidelityfx-brixelizer/)

## 1. HTrace SSGI 的真实光照输入

### 1.1 当前 HTrace 怎么得到 hit radiance

HTrace 的 `HRenderSSGI.compute` 在屏幕空间 ray 命中后读取 `_Color`：

```hlsl
HitRadiance = UnpackColorHit(H_LOAD(_Color, HitData.xy).x, MovingHitPoint);
```

URP 路径里 `_Color` 绑定的是 camera color。也就是说，HTrace 的射线并没有在命中点重新计算灯光；它把命中像素已经得到的最终颜色当作 outgoing radiance，再做距离衰减、temporal accumulation 和 ReSTIR 重采样。

### 1.2 GBuffer 在 HTrace 中做什么

HTrace 自己生成或读取 GBuffer、depth pyramid、rendering layer 和 motion/history 资源。GBuffer0/1 在 `ColorComposeURP.shader` 中主要用于：

- GBuffer0：albedo；
- GBuffer1：metallic/specular 相关信息和 AO；
- 最终间接光合成：`GI * albedo * (1 - metallic) * AO`；
- ambient override：从 camera color 中减去估计的环境间接光，避免重复叠加。

它没有一张独立的“灯光属性 buffer”。直接光照已经被烘进 camera color，APV/天空只在 ray miss 时作为 fallback。

相关本地源码：

- `D:/Unity_Project/BREAK_URP/Assets/HTraceSSGI/Resources/HTraceSSGI/Computes/HRenderSSGI.compute`
- `D:/Unity_Project/BREAK_URP/Assets/HTraceSSGI/Resources/HTraceSSGI/Shaders/URP/ColorComposeURP.shader`
- `D:/Unity_Project/BREAK_URP/Assets/HTraceSSGI/Scripts/Passes/URP/GBufferPassURP.cs`

### 1.3 对 lilToon 的问题

camera color 不是干净的 GI source，因为它可能已经包含：

- 外扩描边；
- toon 最终合成；
- 透明/OIT；
- SSS、反射和后期处理；
- 不属于真实表面的装饰效果。

这正是 HTrace 描边发白的根本原因。问题不是单纯的 ray length、brightness clamp 或 sample count。

HTrace 的 `DirectLighting` debug 也不是一张独立的 direct-light RT。URP 中它显示的是间接光注入前的 camera color，所以会包含 opaque outline；透明物体尚未进入这个时机，因此会表现为没有透明内容或出现历史拖影。这解释了你现在看到的现象。

### 1.4 HTrace 已有设计盘点

HTrace 的 URP 链路实际上是：

```text
PrePass / motion vectors
    -> GBuffer 或 ForwardGBuffer0..3
    -> rendering layer mask
    -> depth pyramid MIP0..4
    -> temporal reprojection
    -> checkerboard（可选）
    -> ray tracing
    -> ReSTIR temporal / firefly / spatial
    -> temporal + spatial denoise
    -> interpolation（低 render scale 时）
    -> camera color composite
```

以下设计应先研究后再删减：

| HTrace 设计 | 当前机制 | Ho-SSGI 处理 |
|---|---|---|
| `ExcludeCastingMask` | 在深度金字塔 MIP0 把 caster 深度置为无效，再生成更高 MIP | 保留；改用 Ho 的几何/对象排除语义 |
| `ExcludeReceivingMask` | 最终合成时把 receiver 的 GI 替换为 APV/天空 fallback | 保留；改成 GI output 层的 receiver mask |
| `MaskExclude` kernel | HTrace shader 中存在，但当前 URP 链没有调用，主要是 HDRP 路径 | 不照搬 kernel，先修正 URP 路径 |
| `AmbientOverride` | 从 camera color 减去 APV/天空估计的环境光，避免双重 GI | 不直接照搬；Ho 输出独立 GI，避免 PBR albedo/metallic 假设 |
| `Multibounce` | 把 camera color/上一帧颜色写入 radiance history | 保留 history 思路，但 history 必须是干净 source/GI，不包含 outline/post |
| `FallbackType` | None、Sky、APV；ray miss 时补环境光 | 保留 APV/sky fallback |
| `NormalBias` / `ViewBias` / `SamplingNoise` | APV fallback 采样偏移与噪声 | 保留，作为 fallback 参数 |
| `BackfaceLighting` | 命中表面背面法线时限制或拒绝 hit radiance | 保留，按 lilToon 双面/背面语义重新验收 |
| `ThicknessMode` / `Thickness` | 线性或 uniform thickness，控制屏幕相交容差 | 保留；这是薄片、头发和描边附近稳定性的关键 |
| `RefineIntersection` | 命中后用更细的中间点确认相交 | 高质量路径默认开启 |
| `FullResolutionDepth` | 低分辨率 tracing 时仍使用全分辨率深度 | 高质量路径默认开启 |
| `Checkerboard` | 交错像素分类和间接 dispatch | 先关闭，确认高质量主链正确后再作为参数优化 |
| `BrightnessClamp` | 按最大值或邻域偏差限制亮点 | 保留，避免颜色反弹 firefly |
| ReSTIR validation | temporal lighting/occlusion validation、half-step validation | 保留已有设计，先验证 source 替换后的有效性 |
| spatial reservoir | 依据深度、法线、AO guidance 做空间重采样 | 保留，不另写一套低质量 blur |
| recurrent blur | 使用 spatial output 作为后续 temporal history | 先作为可选质量参数，不能污染 source history |

一个重要事实：`ExcludeCastingMask` 在 HTrace 的深度金字塔阶段是有效的，但 `ExcludeReceivingMask` 只能排除已经拥有正确 rendering layer 的接收像素。当前 lilToon outline 的基础 GBuffer/DepthNormals 不包含外扩壳，因此 HTrace 的 layer mask 不能单独解决 outline 白边。

### 1.5 Ho 的排除真值：GeometryBuffer coverage

Ho-GeometryBuffer 已经验证为：

- normal/depth pass 不执行 outline extrusion；
- outline 壳不写入 normal/depth；
- 可用 coverage、normal validity 和 depth 作为物理表面判断。

因此第一版不需要额外渲染一张“无描边完整 RT”，也不需要把主 shader 和 outline shader 拆成两个材质。SSGI 的 hit acceptance 统一使用：

```text
hitUV
  -> sample HoGeometryBuffer normal/depth
  -> coverage valid
  -> normal valid
  -> ray depth 与 geometry depth 一致
  -> 才允许 sample source radiance
```

描边仍然存在于 camera/opaque color，但它对应的 GeometryBuffer coverage 为空，会在 source 采样前被拒绝。它也不会出现在 Ho-SSGI 的 depth pyramid 中，因此不会作为 caster 遮挡或反弹。

当前 producer vertical slice 在 `BeforeRenderingOpaques` 运行，因此 source 使用 `HoMetadataBufferSurfaceColor` 的 clean base。opaque/direct-light source 留到后续独立 source pass；不能在 Opaques 之前读取最终 opaque color。

透明/OIT 第一版不进入 GI caster/receiver 域，使用 APV fallback。这样先消除 HTrace 的透明拖影问题，再单独研究透明 GI，不把透明路径混进主 SSGI 验证。

### 1.6 现有双语义绘制就是干净 source 的基础

主 forward pass 把主体和 outline 一起画进 camera color；`HoGeometryBuffer` 和 `HoMetadataBufferSurfaceColor` 则通过独立的 base geometry pass 再画一次主体：

```text
camera opaque color       = 后续 direct-light source，可能包含 outline
HoGeometryBuffer          = 无 outline 的 normal/depth/coverage
HoMetadataBufferSurfaceColor = 无 outline 的纯色/coverage
```

这不是为了 GI 新增一张 no-outline RT，而是复用已经存在的语义绘制成本。Ho-SSGI 可以按以下方式使用它们：

- GeometryBuffer 决定 hit/receiver 是否是真实几何；
- SurfaceColor 提供干净 albedo 和覆盖率；
- 后续 direct-light source 提供已着色 radiance；
- 三者在 hit UV 上做 coverage/depth 一致性校验后才进入 GI。

SurfaceColor 本身是纯色，不等于间接光。它适合做 source 的 albedo/validity，不能单独替代 direct-light radiance。另一个限制是没有 `HoMetadataBufferSurfaceColor` pass 的材质不会自动获得这张颜色图，因此 fallback/非 lilToon 材质需要走 opaque color 或 APV fallback。

## 2. Ho-SSGI v1 的核心设计

### 2.1 复用输入

Ho-SSGI 直接复用：

- `HoGeometryBuffer` 的 normal/depth/sky；
- URP camera motion vector，或深度/法线历史校验；
- APV/天空 fallback；
- Ho 自己的 depth pyramid 和 temporal history。

Ho-SSGI 不创建 HTrace 那套重复 GBuffer、重复 depth prepass 和独立 rendering-layer buffer。

MetadataBuffer 不属于 GI 输入。它继续作为角色语义、CharacterSpecialization、SSS、AOV 和 ScreenProcess 的来源。当前它已经可以在 `BeforeRenderingOpaques` 生成，但 GI 不读取它。

### 2.2 新增一个干净的 GI source

Ho-SSGI 不能继续无条件读取最终 camera color。需要一个 source 选择：

1. **首选**：lilToon 输出的不含 outline 的直接光照 source；
2. **过渡**：读取 opaque color，并用 GeometryBuffer coverage 排除 outline 和无效几何；
3. **fallback**：APV/天空或上一帧有效 radiance。

这个 source 要表达的是“命中点可以向外贡献的已经着色辐射”，不是单纯 albedo。对于 lilToon，最合理的形式是：

```text
GI source = toon direct diffuse + 可控环境/主光补光
```

不需要把每个 Light 对象的参数单独塞进 SSGI。灯光属性应该在 source pass 里完成直接光照计算，SSGI 只负责：

```text
source radiance
    * ray visibility
    * normal / cosine weighting
    * distance falloff
    * temporal / spatial filtering
```

这样主光、附加光、阴影、toon ramp 和材质颜色都已经在 source 中表达，SSGI 不需要重新实现一套 light loop。

### 2.3 输出契约

生产端发布算法无关的语义：

- `_HoGITexture`：HDR 间接光颜色；
- `_HoGIConfidenceTexture`：命中、fallback、history 有效性；
- 可选 `_HoGIBentNormalTexture`：环境补光和 debug 使用。

关闭 Ho-SSGI 时，GI 颜色为黑色、confidence 为 0。lilToon 消费端只知道 GI 语义，不知道 HTrace 或其他实现。

## 3. 一条高质量 Ho-SSGI 链

```text
HoGeometryBuffer (250)
    -> HoMetadataBuffer (250)
    -> GI source（clean base，无 outline）
    -> Hi-Z depth pyramid
    -> cosine hemisphere tracing
    -> intersection refinement
    -> temporal reprojection + history validation
    -> firefly clamp / bilateral spatial filter
    -> APV/sky fallback
    -> _HoGITexture + confidence
    -> lilToon forward opaque receiver
```

第一版不拆出低质量算法。质量参数只控制：

- render scale；
- ray count；
- trace step count；
- history length；
- spatial radius；
- source/indirect intensity。

Ho-SSGI v1 应尽量沿用 HTrace 已验证的 ReSTIR、temporal validation、firefly suppression、depth pyramid 和 recurrent history 结构。接入顺序是：先替换 source 并保持 HTrace 链路行为，再逐项检查每个阶段在 lilToon 上的语义，而不是重新发明一套简化算法。

## 4. lilToon 接收顺序

先验证 producer，再验证材质接收：

1. debug 直出 GI source；
2. debug 直出未滤波 ray result；
3. debug 直出 temporal/confidence；
4. fullscreen composite 验证颜色反弹；
5. 接入 lilToon indirect/toon shadow；
6. 最后处理 SSS、OIT、平面反射和角色特化的 pass 顺序。

### 4.1 GI 在 lilToon 中的语义

Ho-SSGI 输出的是一项可控的间接光贡献，不直接改写 `fd.lightColor`，也不强行伪装成 `fd.indLightColor`。后者是现有 SH/APV 的环境方向因子，不是完整的 GI 颜色。

片元阶段只采样一次：

```text
hoGIColor
hoGIConfidence
hoGIWeight = hoGIConfidence * materialMask * volumeStrength
```

之后由 lilToon 决定它进入 direct、indirect 还是最终颜色。

### 4.2 最小材质控制面

第一版只需要以下控制，不把 HTrace 的几十个参数暴露到材质：

- `HoGI Enabled`；
- `HoGI Strength`：抑制或放大 GI；
- `HoGI Color`：颜色乘法/色调控制；
- `HoGI Apply Mode`：`IndirectTint`、`IndirectAdd`、`LightColorMultiply`；
- `HoGI Shadow Weight`：限制 GI 只进入 toon 阴影侧；
- `HoGI Transition`：基于 `fd.shadowmix` 的平滑过渡起止；
- `HoGI Mask`：材质纹理遮罩；
- `HoGI Clamp`：限制颜色反弹峰值。

全局 Volume 只控制 producer：ray length、step、temporal、denoise、source、fallback 和全局强度。材质只表达“我怎样接收这项 GI”。

当前 Ho-SSGI producer 的主要参数已放入 `HoSSGIVolume`：启用、ray count、step count、ray length、thickness、GI intensity、source saturation 和 feature-local debug mode。RendererFeature 上的同名字段只作为没有 Volume 时的兜底配置。

### 4.3 三个接入点

#### A. `BEFORE_SHADOW`：LightColorMultiply

在 `OVERRIDE_SHADOW` 之前，可选地把 GI 色调作为灯光颜色调制：

```hlsl
fd.lightColor = lerp(fd.lightColor,
                     fd.lightColor * hoGIColor,
                     hoGIWeight * lightColorMultiplyStrength);
```

这个模式改变 direct 和 shadow 的共同光色，必须显式开启，默认关闭。它适合整体色调、魔法光或场景色污染，不应作为默认物理解释。

#### B. `lilGetShading` 内部：IndirectTint / ShadowColorBlend

`lilGetShading` 已经把 toon 阴影拆成 `directCol` 和 `indirectCol`：

```hlsl
directCol = fd.albedo * fd.lightColor;
indirectCol = ...;
fd.col.rgb = lerp(indirectCol, directCol, lns.x);
```

Ho-GI 最适合在 `indirectCol` 完成颜色构造、`min(indirectCol, directCol)` 和最终 mix 之前注入：

```hlsl
float shadowWeight = 1.0 - fd.shadowmix;
float transition = smoothstep(_HoGITransition.x,
                              _HoGITransition.y,
                              shadowWeight);
float weight = hoGIWeight * transition;
indirectCol = lerp(indirectCol,
                   indirectCol * hoGIColor,
                   weight);
```

这样 direct lit 明面默认不被污染，GI 主要影响阴影侧、环境补光和色阶过渡。`IndirectAdd` 可在同一位置增加一条受 clamp 的 contribution，但必须避免再次无条件乘 albedo。

#### C. `BEFORE_SSAO`：FinalContribution

这是低耦合的第一版接入点。它位于追加光合并之后、SSAO/SSS 之前，适合验证：

```hlsl
fd.col.rgb += hoGIColor * hoGIWeight * finalContributionStrength;
```

它不需要改动 `lilGetShading`，但不具备完整的 direct/indirect 分离能力。验证成功后，默认切到 B 模式，C 只保留为兼容/调试模式。

### 4.4 `fd` 内部缓存

为了避免在多个接入点重复采样 GI，建议给 `lilFragData` 增加一次性缓存：

```text
hoGIColor
hoGIConfidence
hoGIWeight
```

初始化为零，只有启用 Ho-GI shader feature 时采样 `_HoGITexture` 和 confidence。outline、metadata、geometry 等 pass 不编译或不执行这个接收逻辑。

### 4.5 GI 的 NPR 合成规则

- 主要作用于 toon 阴影侧和环境补光；
- 支持 GI strength、tint、luminance remap、contrast 和 clamp；
- AO 只削弱 GI/ambient，不直接污染主光；
- confidence 低时使用 APV/天空或保守 fallback；
- outline 不接收 GI，也不作为 source caster。

## 5. Producer-only vertical slice

在接入 lilToon 之前，先只实现 Ho-SSGI producer 和 debug 输出。每一步都必须能独立观察，避免把 source、ray、history 和 toon 合成混在一起调。

### 5.1 输入验证

先验证三种已有输入：

1. `HoGeometryBuffer` normal/depth/coverage；
2. URP opaque color；
3. 可选 `HoMetadataBufferSurfaceColor` 纯色/coverage。

第一版不要求 SurfaceColor 覆盖所有材质。它只作为可用时的干净 albedo/coverage；没有这张图的材质继续使用 GeometryBuffer + opaque color + APV fallback。

当前 producer vertical slice 的 source 组合是：

```text
opaque color                 = 已着色 radiance
HoMetadataBufferSurfaceColor = 干净 base / coverage validity
HoGeometryBuffer              = normal / depth validity
```

SurfaceColor 不是第二份灯光结果，而是对 opaque radiance 的物理表面约束。Source validity 建议编码为：

```text
sourceValid = geometryCoverage
            * normalValid
            * surfaceColorCoverage
            * depthValid
            * opaqueSourceValid
```

描边像素的 geometryCoverage 应为 0，即使 opaque color 中仍然能看到描边颜色，也不能进入 source 或 history。

### 5.2 Depth pyramid

沿用 HTrace 的 MIP0..4 结构，但输入改为 Ho-GeometryBuffer depth：

- MIP0：GeometryBuffer depth；
- MIP1..4：用于 Hi-Z ray traversal；
- caster exclusion 在 MIP0 生效；
- sky/invalid depth 保持明确的 invalid 标记。

每个 MIP 都应有 debug 模式。首版由 Ho-SSGI RendererFeature 的 feature-local debug pass 直接显示，不要求接入 DebugTile。

### 5.3 Raw ray result

每个像素至少保留以下临时结果：

- hit flag；
- hit UV；
- hit distance；
- hit surface depth；
- hit surface normal validity；
- hit source color；
- invalid reason（无 coverage、深度不一致、背面、超出屏幕）。

高质量路径的初始设置：full-resolution depth、intersection refine、cosine hemisphere sampling、较高 ray/step 参数、关闭 checkerboard。这里不做另一套低质量算法。

### 5.4 Temporal result

沿用 HTrace 的 motion/depth/normal/history validation，但 history 只存 Ho-SSGI 的 source/GI 语义，不复制最终 camera color：

- history sample count；
- reprojected hit validity；
- depth/normal rejection；
- source luminance change；
- moving object rejection；
- confidence。

需要分别 debug current raw、reprojected history 和 accumulated result，才能区分 ray 错误与 temporal 拖影。

### 5.5 Denoise result

先复用 HTrace 的高质量思路：

- firefly suppression；
- temporal accumulation；
- 深度/法线引导的 spatial filter；
- spatial occlusion guidance；
- 可选 recurrent blur。

第一版只保留一条主链，参数变化只影响 sample、history 和 filter 半径，不额外维护一套 Low 算法。

### 5.6 Producer debug views

Ho-SSGI 首版只需要 feature-local debug pass 和一个 `DebugMode` 枚举，至少包括：

```text
gi.source-opaque
gi.source-valid
gi.surface-color
gi.geometry-coverage
gi.depth-pyramid-mip0..4
gi.ray-hit
gi.ray-distance
gi.raw-radiance
gi.temporal-validity
gi.sample-count
gi.confidence
gi.fallback
gi.filtered-radiance
```

调试目标是能够回答：描边有没有进入 source、有没有进入 depth pyramid；ray 是否命中有效 geometry；颜色错误来自 source、ray、history 还是 composite；透明是否被错误写入 caster/receiver/history。

DebugTile/`HoDebugViewRegistry` 只作为后续统一调试入口，不作为 Ho-SSGI producer v1 的依赖。

## 6. Producer 验收顺序

朱木古堂只按以下顺序验收：

1. GeometryBuffer coverage/normal/depth；
2. opaque source 与 source-validity；
3. depth pyramid；
4. 单帧 raw ray result；
5. temporal reprojection；
6. spatial/temporal denoise；
7. APV/sky fallback；
8. `_HoGITexture` 独立 fullscreen composite；
9. 最后才进入 lilToon 内部接收。

完成第 8 步之前，不调 `_HoGIStrength`、toon transition 或 material mask，否则无法判断问题来自 producer 还是材质合成。

## 7. 描边排除

以下规则必须在 Ho-SSGI 第一版就成立：

- 不把包含 outline 的 camera color 当作唯一 source；
- outline 不写入 GI source；
- outline 不接收 GI；
- caster 排除应在 depth pyramid 生成前生效，等价于 HTrace 的 `ExcludeCastingMask`；
- receiver 排除应在 GI output/composite 阶段生效，等价于 HTrace 的 `ExcludeReceivingMask`；
- 深度/法线不一致时拒绝 screen hit；
- 无有效几何覆盖的像素不参与历史累积；
- camera color、source、GI、confidence 都要能单独 debug。

## 8. 朱木古堂验收顺序

1. HoGeometryBuffer + source debug；
2. 单帧 SSGI，不开 temporal；
3. temporal + depth/normal rejection；
4. spatial filter 和 firefly clamp；
5. 红墙/白地面颜色反弹；
6. 室内遮挡和近距离间接光；
7. 角色移动、头发和外扩描边；
8. SSS、OIT、平面反射和 CharacterSpecialization；
9. 关闭 Ho-SSGI 后确认无 GI、无上一帧残留；
10. 用 RenderDoc/Frame Debugger 对照 HTrace 的输入、输出和 GPU 成本。

完成标准是：HTrace 的独立 GBuffer/prepass 不再是必需输入；Ho-SSGI 能稳定输出适合 lilToon 的 GI；关闭时安全回退；后续替换 Brixelizer 时不改 lilToon 消费契约。

## 9. Brixelizer 只保留为后续替换

Ho-SSGI 接收契约稳定后，再评估 Brixelizer 的世界空间 SDF、screen probe 和 radiance cache。它只替换 GI producer，不改变 `_HoGITexture`、confidence 和 lilToon 接收接口。

当前不展开 DDGI、Lumen 或硬件 RTGI 的实现规划。

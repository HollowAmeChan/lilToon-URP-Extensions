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

Ho-SSGI 不能继续无条件读取 camera color。需要一个 source 选择：

1. **首选**：lilToon 输出的不含 outline 的直接光照 source；
2. **过渡**：读取 camera color，但对 outline、透明、后期和无效几何做排除；
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
    -> GI source（不含 outline）
    -> Hi-Z depth pyramid
    -> cosine hemisphere tracing
    -> intersection refinement
    -> temporal reprojection + history validation
    -> firefly clamp / bilateral spatial filter
    -> APV/sky fallback
    -> _HoGITexture + confidence
    -> lilToon GI receiver
```

第一版不拆出低质量算法。质量参数只控制：

- render scale；
- ray count；
- trace step count；
- history length；
- spatial radius；
- source/indirect intensity。

HTrace 已有的 ReSTIR、temporal validation、firefly suppression 和 recurrent blur 可以作为参考，但接入顺序应是：先让 source、trace、history 和 composite 正确，再逐项恢复这些质量机制。

## 4. lilToon 接收顺序

先验证 producer，再验证材质接收：

1. debug 直出 GI source；
2. debug 直出未滤波 ray result；
3. debug 直出 temporal/confidence；
4. fullscreen composite 验证颜色反弹；
5. 接入 lilToon indirect/toon shadow；
6. 最后处理 SSS、OIT、平面反射和角色特化的 pass 顺序。

GI 的 NPR 合成规则：

- 主要作用于 toon 阴影侧和环境补光；
- 支持 GI strength、tint、luminance remap、contrast 和 clamp；
- AO 只削弱 GI/ambient，不直接污染主光；
- confidence 低时使用 APV/天空或保守 fallback；
- outline 不接收 GI，也不作为 source caster。

## 5. 描边排除

以下规则必须在 Ho-SSGI 第一版就成立：

- 不把包含 outline 的 camera color 当作唯一 source；
- outline 不写入 GI source；
- outline 不接收 GI；
- 深度/法线不一致时拒绝 screen hit；
- 无有效几何覆盖的像素不参与历史累积；
- camera color、source、GI、confidence 都要能单独 debug。

## 6. 朱木古堂验收顺序

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

## 7. Brixelizer 只保留为后续替换

Ho-SSGI 接收契约稳定后，再评估 Brixelizer 的世界空间 SDF、screen probe 和 radiance cache。它只替换 GI producer，不改变 `_HoGITexture`、confidence 和 lilToon 接收接口。

当前不展开 DDGI、Lumen 或硬件 RTGI 的实现规划。

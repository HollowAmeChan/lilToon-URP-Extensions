# lilToon / Ho-SSGI：设计意图、契约与坑

> 状态：**已实现并收敛（2026 文档审核核对）**。原“Draft v0.6 实施规划”的任务清单已执行完，本文保留**设计意图、source 分析、资源所有权、契约、描边排除与坑**。
> 配套：`归档/LILTOON_SSGI_RESTIR_ALIGNMENT_WORKSHEET.md`（链路阶段 / reservoir 契约 / 已知差异 / 验收）、`归档/LILTOON_RESTIR_SHARED_LAYER_REPORT.md`（共享层评估）、`Ho-已知问题-描边SSGI白边.md`（描边白边已知问题）。
> 主线决策：**先把 HTrace SSGI 改造成适合 lilToon 的 Ho-SSGI**；Brixelizer GI 只作为后续 producer 替换，不提前展开。**不为低质量档另做一套算法**——先确定一条高质量路径，用分辨率 / ray count / history length / denoise 参数控成本，Low/Medium 只改参数、不改算法结构。

## 1. 插入时机的取舍（唯一的硬约束）

`BeforeRenderingOpaques` 与 `AfterRenderingOpaques` 拿不到同一种 source：

- **After Opaques**：能读当前帧已完成 direct lighting 的 opaque camera color（HTrace 在 URP 里就是这么做的）→ 最容易得到“有灯光存在感”的 GI，但**只能在 forward 材质之后 composite**。
- **Before Opaques**：能让 lilToon 在本帧 forward 中采样 GI，但本帧 direct radiance 还没生成；只读表面色就只是 base-color transfer / SSDO 风格的增强，**不是完整 GI**（要含 Point Light、阴影、toon ramp、cookie、ShadowCast，必须另做 direct-source 评估或 source pass）。

→ 算法基线放在 **After Opaques**：用 GB 做 hit validity 与描边排除，读已着色的 opaque camera color。**Ho-SSGI 当前是纯后处理 producer，不向当帧材质提供 GI。**

## 2. HTrace 的真实光照输入（它其实没有灯光 buffer）

- `HRenderSSGI.compute` 命中后读 `_Color`（URP 下 = camera color）：**射线不在命中点重算灯光**，而是把命中像素的最终颜色当 outgoing radiance，再做距离衰减 / temporal / ReSTIR。
- HTrace 自己的 GBuffer0/1 只用于 albedo 与 metallic/specular/AO（合成 `GI * albedo * (1−metallic) * AO`）以及 ambient override；**没有独立的“灯光属性 buffer”**，direct lighting 已经烘进 camera color，APV/天空只在 miss 时兜底。
- HTrace 的 `DirectLighting` debug 也**不是**独立 direct RT，而是间接光注入前的 camera color——所以它同样包含 opaque outline。

**对 lilToon 的问题**：camera color 不是干净 source，它可能已包含外扩描边、toon 最终合成、透明/OIT、SSS/反射/后期、装饰效果。**这正是描边发白的根因**，不是 ray length / brightness clamp / sample count 能解决的。

## 3. 排除真值 = GeometryBuffer coverage

GB 的 normal/depth pass 不执行描边外扩、描边壳不写 GB → 第一版**不需要**额外渲染“无描边完整 RT”，也不需要把主 shader 与 outline shader 拆成两个材质。统一 hit 接受条件：

```text
hitUV → 采样 GB normal/depth → coverage 有效 → normal 有效 → ray depth 与 geometry depth 一致 → 才允许采样 source radiance
```

描边仍存在于 camera color，但它的 GB coverage 为空，会在采样前被拒绝；它也**不进入 GB depth pyramid**，因此不会当 caster 或反弹源。
透明/OIT 第一版**不进入** GI caster/receiver 域，走 APV fallback——先消除 HTrace 的透明拖影，再单独研究透明 GI。

### SSGI 真正需要的输入

| 输入 | 用途 | 依赖 ShadowCast？ |
| --- | --- | --- |
| Depth / normal / coverage | 重建位置法线、判断 screen hit | 否 |
| Hi-Z depth pyramid | 加速 march、caster 排除 | 否 |
| **Source radiance RGB** | 命中点向外贡献的 direct/toon/emissive 辐射 | 阴影已间接烘入 source |
| Diffuse / metallic / AO | 接收面响应与合成 | 否 |
| Motion / history | 重投影与灯光变化检测 | 否 |
| Light 列表 | **不由 SSGI 核心读取** | 只有追加的 source pass 才需要 |

SSGI 的屏幕遮挡来自 camera depth / GB，**不是** ShadowCast atlas；ShadowCast 只有在生成“独立 lit source”时才参与（source pass 必须与主 forward 用同一套阴影/衰减，否则会把处于阴影的表面错当受光 source）。

## 4. 资源所有权（ReSTIR 不是一张可共享的颜色图）

reservoir 保存 `selected radiance / Wsum / M / W / 方向与距离 / HitFound / OriginNormal`；最终 GI 是 `selectedColor * W` 而不是平均；temporal 合并重投影历史、spatial 按平面/法线/距离权重合并邻居、firefly 主要改 `W` 而不动样本本身。

```text
GB（+ 后续共享屏幕空间基础层） = normal / depth / coverage / 可复用 Hi-Z / 几何有效性
Ho-SSGI   = GI candidate、GI reservoir、GI temporal/spatial resampling、GI denoise
Ho-GTAO   = AO candidate、AO history、AO temporal/spatial filter（**当前没有 reservoir**）
Ho-DI（后续）= light candidate/RIS、DI reservoir、visibility validation、DI denoise
```

执行时机也不能全部提前：GB 可以提前准备输入与分配资源，但 **GI candidate 必须等 source radiance 可用**、DI candidate 必须等灯列表与材质/几何响应可用。所以资源管理基础设施可以做成 GB 之后的共享中控，**但 GI/DI reservoir 不能塞进 GeometryBuffer feature**——三者的候选权重、验证条件、打包字段、reset 条件都不同。

**共享层（`HoScreenSpaceContext` 方向）：现在不建。** 早先的 `Ho-RTBuffer` 名字不作为最终命名（易被理解成硬件 RT buffer）。等到满足任一条件再建：① GTAO 与 SSGI 都需要同一套 Hi-Z 且重复生成已是可测成本；② DI 开始共用 motion / neighbor offset / blue noise / 相机 history reset；③ 需要跨 producer 统一暴露 RenderGraph 资源。**首个迁移目标**是把 GTAO 的 `CreateDepthPyramid` 抽成共享 Hi-Z producer。

## 5. v1 核心设计

- **复用输入**：GB normal/depth/sky、URP motion vector（或深度/法线历史校验）、APV/天空 fallback、自有 depth pyramid 与 temporal history。**不创建** HTrace 那套重复 GBuffer / 重复 depth prepass / 独立 rendering-layer buffer。
- **GI source** = After Opaques 的 opaque camera color（已含 toon direct lighting），配合 §3 的有效性校验。灯光属性已由 forward pass 完成，SSGI 只做 `source radiance × ray visibility × cosine 权重 × 距离衰减 × 时空滤波`，**不重新实现 light loop**。
- **后续可选 `HoGI Lit Source` pass**（当 outline / 透明 / 反射 / 后期污染成为主要误差时）：输出“干净表面辐射”，`RGB = direct diffuse/toon + emissive`、`A = valid opaque coverage`，排除 outline / specular / 透明 / post。理想实现是 lilToon opaque forward 用 MRT 同时写 camera color 与 lit source；退而求其次用独立 `LightMode = HoGI` pass，但必须复用主 shader 的灯光/ramp/cookie/light layer/shadow attenuation 规则。它只解决**可见表面辐射**，不能充当世界空间 radiance cache。
- **输出契约（现状）**：只发布 **`_HoGITexture`**（HDR 间接光颜色）；合成端用 `receiverValid = step(eps, GB coverage)` 控制接收。**规划但未实现**：`_HoGIConfidenceTexture`、可选 `_HoGIBentNormalTexture`。关闭 Ho-SSGI 时 GI 为黑、不叠加；消费端只知道 GI 语义，不知道实现。
- **合成（现状）**：`cameraColor.rgb += gi.rgb * (_HoSSGIIntensity * receiverValid)`，在 `BeforeRenderingPostProcessing` 读当前 camera color 后叠加；**不改写** `fd.lightColor` / `fd.indLightColor`，也不增加 lilToon 材质采样点。`sourceSaturation` 只影响命中 radiance 色彩，`intensity` 只影响最终 composite；材质内 GI 接收与 NPR 阴影过渡**暂不纳入**。
- **参数面**：以 `HoSSGIVolume` 为准（启用 / ray count / step count / ray length / thickness / temporal blend / spatial radius / GI intensity / source saturation / debug mode）；RendererFeature 上的同名字段只是无 Volume 时的兜底。
- **算法链**：`GB → opaque forward → source → raw trace（world-space cosine hemisphere，GB 线性深度 crossing）→ candidate reservoir → temporal reservoir reuse + validation → firefly W clamp → spatial reservoir reuse（world-plane Poisson）→ selected-ray re-march validation → temporal/spatial reconstruction → APV/天空 fallback → _HoGITexture + confidence`。旧的“额外视空间轴翻转”路径**已删除**（会让上下方向与相机移动产生错位）。

## 6. 描边排除（第一版就必须成立的规则）

- opaque camera color 可以当 radiance source，但 **outline 必须被 GB coverage 拒绝**；
- outline 不进入 GI source 的 hit sample，也**不接收** GI；
- caster 排除在 depth pyramid 生成前生效（等价 HTrace `ExcludeCastingMask`）；
- receiver 排除在 GI output/composite 阶段生效（等价 `ExcludeReceivingMask`）；
- 深度/法线不一致时拒绝 screen hit；无有效几何覆盖的像素不参与历史累积；
- camera color / source / GI / confidence 都要能单独 debug。

> 注意：`ExcludeCastingMask` 在 depth pyramid 阶段有效，但 **`ExcludeReceivingMask` 只能排除“已有正确 rendering layer”的像素**——lilToon 的 outline 壳在基础 GBuffer/DepthNormals 里根本不存在，所以 HTrace 的 layer mask **单独解决不了**描边白边。

## 7. 验收顺序

**Producer**：① GB coverage/normal/depth；② opaque source 与 source-validity（`sourceValid = geometryCoverage × normalValid × depthValid`）；③ depth pyramid（MIP0 = GB depth，caster 排除在 MIP0 生效，sky/invalid 保持明确标记）；④ 单帧 raw ray result（hit flag/UV/distance/surface depth/normal validity/source color/invalid reason 都要能单独看）；⑤ temporal；⑥ denoise；⑦ APV/天空 fallback；⑧ `_HoGITexture` 独立 fullscreen composite；⑨ 暂不进入 lilToon 内部接收。

**场景（朱木古堂）**：GB+source → 单帧 SSGI（关 temporal）→ temporal + 拒绝 → spatial + firefly → 红墙/白地面颜色反弹 → 室内遮挡与近距间接光 → 角色移动/头发/外扩描边 → SSS/OIT/PLR/角色特化 → 关闭后无 GI 与上一帧残留 → RenderDoc/Frame Debugger 对照 HTrace 输入输出与 GPU 成本。

**完成标准**：HTrace 的独立 GBuffer/prepass 不再是必需输入；source 可从 opaque camera color 平滑替换为 `HoGI Lit Source`；关闭时安全回退；后续替换 Brixelizer 不改 `_HoGITexture` 契约。

## 8. 坑

1. **source 不干净 = 一切后续都白做**：outline / 透明 / 后期一旦进入 source，就会被当作真实表面反弹。先修 source 与有效性，再谈 temporal/spatial。
2. **不要用 debug 视图推断“有没有独立灯光输入”**：HTrace 的 `DirectLighting`、near-hit AO 都不是独立 RT；“自己渲了一遍 GTAO”推不出有灯光 buffer。
3. **不要把不同算法的 reservoir 合并成一套“万能 RT”**：GI 是二次表面样本、DI 是灯光样本、AO history 是遮挡统计。共享的只能是**资源管理基础设施**（生命周期、ping-pong、reset、offset/noise、Hi-Z），不是 payload 语义。
4. **不要过早抽共享层**：当前只有 Ho-SSGI 有 reservoir，提前拆 Feature 只会增加顺序/依赖/回收路径而没有收益。
5. **GI candidate 不能提前到 GB 阶段**：GB 之后才有 source radiance。
6. **旧视空间翻转路径必须删掉**，否则上下方向与相机移动会错位。
7. **描边排除靠 GB coverage，不要靠 rendering layer**（见 §6 注）。
8. **透明第一版不入 GI**（走 APV fallback），否则会重现 HTrace 的透明拖影。
9. **纯后处理合成阶段不要同时改材质**：先把 producer 调稳（Volume 参数 + fullscreen composite），材质内接收与 NPR 过渡留到之后，否则 source/ray/history/toon 四种误差会混在一起。

## 9. 后续（Brixelizer 只作替换）

`_HoGITexture` 契约稳定后再评估 Brixelizer 的世界空间 SDF、screen probe 与 radiance cache——它**只替换 GI producer**，不改 `_HoGITexture` 与 confidence。当前不展开 DDGI、Lumen 或硬件 RTGI 的实现规划。

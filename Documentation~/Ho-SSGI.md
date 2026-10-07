# lilToon / Ho-SSGI：设计意图、契约与坑

> 状态：**链路已落地；基础历史累积回归通过，真实场景仍待验收（2026-10-07）**。此前“已实现并收敛”的结论不成立：用户实测仍发散，本轮 GPU 回归确认了每帧丢失历史与历史矩阵约定错误，见 §0。本文保留设计意图、source 分析、资源所有权、契约、描边排除与坑。
> 配套：`归档/LILTOON_SSGI_RESTIR_ALIGNMENT_WORKSHEET.md`（链路阶段 / reservoir 契约 / 已知差异 / 验收）、`归档/LILTOON_RESTIR_SHARED_LAYER_REPORT.md`（共享层评估）、`Ho-已知问题-描边SSGI白边.md`（描边白边已知问题）。
> 主线决策：**先把 HTrace SSGI 改造成适合 lilToon 的 Ho-SSGI**；Brixelizer GI 只作为后续 producer 替换，不提前展开。**不为低质量档另做一套算法**——先确定一条高质量路径，用分辨率 / ray count / history length / denoise 参数控成本，Low/Medium 只改参数、不改算法结构。

## 0. 2026-10-07：先恢复真实历史累积

此次修正两个使静止画面不能积累的实现错误，保留原来的追踪、ReSTIR 和去噪算法：

- **历史分配**：Prev/Next 每帧交换后，`Ensure()` 仍按角色原名调用 `ReAllocateIfNeeded()`。URP 的重分配判断包含纹理名字，导致历史纹理每帧被重新分配。现在仅在首次使用、尺寸或相机身份变化时分配，交换不改变物理纹理身份。
- **历史矩阵**：先前保存普通 CPU projection 的逆，shader 却输入 GPU device depth。现在在 RenderGraph 执行时按 source 的实际 UV origin 计算 GPU projection，包含 reversed-Z / Y flip，再保存 inverse VP；与 URP 相机矩阵约定一致。

独立 batch 场景（彩色墙 / 白色接收面，160×128，静止 32 帧）读回持久历史纹理；D3D11、D3D12 均通过。表中帧间变化为 temporal denoised RGB 的整图平均绝对差，比较第 25–32 帧，数值只用于这个固定夹具：

| 路径 | 原历史接受率 / 累积帧数 | 修复后接受率 / 累积帧数 | 原后期帧间变化 | 修复后后期帧间变化 |
| --- | --- | --- | --- | --- |
| 时域重用，无空间/验证/Firefly | 0% / 1 | 100% / 16 | 0.098188 | 0.004359 |
| 完整链路 | 0% / 1 | 100% / 16 | 0.098423 | 0.009572 |
| 完整链路，URP render scale 0.7 | 0% / 1 | 100% / 16 | 0.100021 | 0.013194 |

回归入口为 `lilToon.URP.Extensions.SSGI.HoSSGIValidation.Run`，**只能在独立 batch 项目中执行**。检查 22 张历史纹理在交换后保持同一组物理对象、有效像素的接受率、累积帧数、GI 有效性与后期帧间变化下降。`RunBaseline` 仅记录数据；`RunLegacyProjection` 保留分配修复但重新注入旧 CPU inverse VP，作为矩阵错误的负对照。

矩阵负对照在 D3D11 上重新得到 **0% 接受率 / 累积帧数 1**，证明只修分配仍不足以恢复历史，两处修复均必要。

### 0.1 细网纹：采样、滤波与阶段参数（同日后续）

用户继续反馈有带运动感的细网纹，基础累积通过并不等于画面已干净。本轮进一步修正：

- **去掉稀疏滤波栅格**：原两轮 5×5 filter 在 radius=8 时以 4/8 像素为步长，周期纹理的像素类互不连通。现在用连续像素的水平/垂直 bilateral 两轮，radius 控制滤波支持范围与 Gaussian 宽度，不再放大像素间距；保留 normal/depth/plane/AO/occlusion 边界权重。
- **每个阶段按命令顺序设置参数**：原多个 callback 通过 `Material.Set*` 修改共享 Material，后阶段值可能覆盖之前尚未执行的 draw，包括 reuse/validation、radius、Hi-Z texel size。现改为 `context.cmd.SetGlobal*`，明确记录各阶段的值。横向条纹测试能抓到“两轮意外都读成同一方向”的错误。
- **采样不再每 16 帧重复**：整数 hash 替代浮点 sine hash；像素使用稳定 scramble，射线按逐相机递增的渲染序号推进二维 digital sequence，march jitter / reservoir replacement 独立变化。同一 Unity update 中两次 RenderRequest 也推进采样，不依赖 `Time.frameCount`。
- **统一纹理尺寸**：GI/history footprint 与像素索引用实际 GI RT 的 `_HoSSGITextureSize`，不再使用未缩放的 `_ScreenParams`。

GPU 证据（D3D11 / D3D12）：

| 固定夹具 | 修复前 | 修复后 |
| --- | --- | --- |
| 周期棋盘格的输出对比度 | 0.7991 | 0 |
| 周期竖条纹的输出对比度 | 0.7991 | 0.00952 |
| 几何断面需要保留的亮度差 | — | 0.79999 |
| radius=8、9 rays、99 steps、128 帧后，同平面相邻像素高频残差 | 0.0022933 | 0.0011706 |

最后一项测的是最终 GI，并同时保留 GI 能量与历史累积检查；不是只检查计数或帧间变化。`HoSSGIFilterValidation.Run` 验证周期噪声清除及几何边界，`HoSSGIValidation.RunPattern` 验证长时间静止的实际链路，`RunSamplingSequence` 验证重复渲染推进与 16 帧候选不重复。

`Spatial Filter 1`（debug 17）现在显示第一轴的中间结果；最终去噪 GI 使用 `Raw GI`（debug 4），最终场景合成使用 `Off`。本轮仍不以夹具结果代替朱木古堂实际镜头验收。

### 0.2 响应速度：输入颜色历史与去噪历史分开处理

用户确认横向条纹是在 debug 17 看到的：只完成横向过滤时，未做纵向混合的行间噪声仍可见。不能以该中间结果判断最终 GI。

“响应变懒”另有两层原因：

- Source reprojection 在 temporalBlend=1 时完全取上一帧颜色，让 trace / lighting validation 迟读旧输入。Source history 保存的是**原始当帧颜色**，因此这里是一帧输入延迟，而不是无限递归冻结。现在 source 历史权重上限为 0.9，并根据钳制前的 RGB 变化降低历史权重；不降低 GI reservoir / denoiser 的静止累积上限。
- 去噪 metadata 原来只验证几何。几何不变而光照/颜色大变时，旧 GI 会继续留在 16 帧历史里。现用同一组几何有效的重投影 tap 比较前后原始 source 的 RGB；显著变化时将该像素的 sample count 重置为 1，并拒绝旧 denoised history。静止稳定输入仍能累积到 16。

响应夹具改用 Unlit 表面，严格把全部辐射降至 10%，避免 Lit 镜面项不随 base color 同比缩放而误判。以静止 64 帧后的最终 GI 为 100%：

| 变化后帧数 | 原 source/denoise 行为 | 两层修正后（D3D11） |
| --- | --- | --- |
| 1 | 90.09% | 10.79% |
| 2 | 27.39% | 10.27% |
| 8 | 15.26% | 10.28% |
| 64 | 10.29% | 10.22% |

`HoSSGIValidation.RunResponse` 检查首帧接近新亮度及后续能量保持。旧行为对照使用独立 batch 工程里的 `Hidden/HoSSGI/ResponseBaseline` 快照。后续临时工程放到包外 `D:/Unity_Fork/.codex_temp/SSGIValidation-20261007`，避免包内临时 Library 写入触发主工程的文件监控。

以上证明基础累积与周期纹理问题得到针对性修复，不代表整个 SSGI 已验收。仍需处理或验证：

- 邻居/重投影代表射线换起点后的命中点重连，以及 target / radiance 的一致性。
- 当前 SSGI 仅读 GB/GTAO/URP 资源，**未接 AC `gisexclude`**；其他文档“含 gisexclude”的说法不能当实现证据。
- 描边背后有实体表面时，GB coverage 不一定为空；必须检查 source 与 GB 可见表面一致性。
- Camera cut、投影参数变化、运动物体与灯光变化的历史拒绝；GB 采用不同分辨率时的纹理 footprint。
- 朱木古堂实际镜头的画面对照。此次未修改主工程 Renderer / Volume 资产。

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

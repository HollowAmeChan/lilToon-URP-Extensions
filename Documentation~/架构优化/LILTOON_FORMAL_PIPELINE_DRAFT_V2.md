> **已过时（R6/R7）**：本文写作时 MetadataBuffer 还在。它已在 R6（摘槽）／R7（消费者换源 + 整块删除）中删掉：`maskId` 与自定义通道归 OB + AC，surface 族归 SB。当前架构以 `Documentation~/架构优化/Ho-*.md` 与 `CHANGELOG.md` 为准。

# 正式管线草案 v2（重新串联）：三轴输入 + 属性合成 + 屏幕效果

> 状态：**三轴拆分、typed ID、sample 级 object/surface SemanticId 合成、SurfaceOwner validity 与 4/8/16 lane batching 已冻结**。
> 2026-09-20 OB 勘误基线已合并到 [`Ho-ObjectBuffer_规划.md`](Ho-ObjectBuffer_规划.md) §0。
> 取代：v0.1 的层模型 / 帧序 / 旧→新映射 / 命名决策。`LILTOON_RENDER_PIPELINE_REVIEW_AND_PLAN.md` 只保留功能域盘点与非反射背景。

## 0. 冻结的四条修正

1. **PLR source 与 PLR resolve 分开记**：镜像相机 source 在 opaque 之前；透明/特殊 fullscreen resolve 在 OIT 之后。
2. **执行顺序 ≠ 读依赖**：GB / OB / SB 的 producer 互不读；跨轴 gate 必须显式登记，不写成默认偏序。
3. **运行时合成器叫 `Ho-AttributeComposite`（AC）**；**"Cryptomatte" 在 GB / OB / SB / AC 里一个名字都不用**——合规的 `crypto_*` 导出（float 位重解释 + manifest + 32 bit）由以后的独立 AOV/export feature 负责，经 AC 资源集使用 OB 身份池与 runtime catalog。
4. **SB 先冻结最小字段 packing**（Color / Normal / Material / Reflection / Classification），再迁移消费者；Target5 的 PLR strength 不兼任通用 reflection mask。

同时冻结：coverage、反射总开关、depth 编码。

---

## 1. 层模型 v2（6 层）

```text
[L0 灯光/阴影]   ShadowCast（附加灯 atlas + URP 主光阴影）                    → shadow.main / shadow.add0..N
[L1 输入缓冲]    GB（几何轴） / ObjectBuffer（逐物体轴） / SurfaceBuffer（表面轴）→ 见 §3 的归属表
[L2 属性合成]    Ho-AttributeComposite（AC）：typed ID 解压 + sample 级 object/surface semantic 合成 + `constant < surface` 数值属性
[L3 屏幕效果]    GTAO / SSGI / SSS / PLR·SSR / 角色特化 / OIT                 → ao, gi, sss, reflection, eyecolor/eyedata …
[L4 图像链]      ImageProcess（只读 camera color）
[L5 输出/调试]   AOV 导出层（多通道 EXR）+ DebugTile
```

**与 v0.1 的差别（三处）**：

| v0.1 | v2 | 理由 |
| --- | --- | --- |
| L1 只有 `CharacterBuffer(Metadata)` + `ScreenGeometryBuffer` | L1 是**三轴并列**：GB / ObjectBuffer / SurfaceBuffer | 一个 buffer 不能同时回答"这是谁"和"表面是什么样" |
| 没有属性合成层 | **新增 L2 `Ho-AttributeComposite`（AC）** | 多来源（纯值/object/surface）需要一条明确的覆盖链；Cryptomatte 只保留给 AOV ID/manifest 导出 |
| 语义效果直接读 MetadataBuffer 的五张图 | 语义效果经 **L2** 拿遮罩 | 今天 ScreenProcess 只剩一个"每层开关 + MetadataBuffer 覆盖率"的遮罩采样（原来的 20 个 rule source 已作为未使用功能删除），这就是"没有合成层"的代价 |

---

## 2. 帧序

| 位置 | 谁 | 前置条件 |
| --- | --- | --- |
| **opaque 之前** | 阴影 → GB / OB / SB → **AC SemanticResolve + AttributeComposite** → **GTAO** | AC 要在 OB/SB 的 MSAA sample 资源后统一合成同 SemanticId；GTAO 和 opaque ForwardLit 可在登记后查询 AC |
| **opaque 之后** | **SSGI** → SSS → OIT → PLR → 角色特化 → ScreenProcess | SSGI 需要 opaque color；后续消费者读 pre-opaque 产生的 AC 资源 |
| **图像链** | ImageProcess | 只读 camera color |
| **最后** | AOV 导出（可选） → DebugTile | 调试最后 |

```text
[1]  URP 主光阴影
[2]  Ho-ShadowCast（附加灯 cast 组）                        → shadow.main / shadow.add0..N
[2.5] PLR source update（`beginCameraRendering`；不占用一个 raster pass）
        ── opaque 之前 ──────────────────────────────────────────────
[3]  GB：Ho-ScreenGeometryBuffer（现名 GeometryBuffer）      → 几何法线 / depth / 几何覆盖率（+ 描边视觉壳 / sky 可选）
[4]  OB：Ho-ObjectBuffer（原 CharacterBuffer）               → IdentityMS / Id0/Id1/Coverage + Facing + object semantic mask
[5]  SB：Ho-SurfaceBuffer                                    → 数值 RT + SurfaceOwner + SurfaceSemantic MSAA source
[6]  AC：typed query + SemanticResolve + AttributeComposite       → `_HoACSelection*` + 合成属性
[6.5] Ho-GTAO（独立 feature）                               → ao / aointent   ★ 必须在 opaque 之前
        ── URP opaque / cutout / 透明 常规绘制 ───────────────────────
[7]  Ho-SSGI（独立 feature，读 gisexclude）                   → gi              ★ 需要 opaque 后的颜色
[8]  AC 资源由 after-opaque 消费者直接读取，不在此再重做一趟合成
[9]  Ho-SubsurfaceScattering                                 → sss
[10] Ho-WeightedOIT（透明合成，最难搞）
[11] PLR 透明/特殊 resolve（如启用）在 OIT 之后；opaque PLR 已在 ForwardLit 消费
[11.5] URP 常规 transparent 绘制（实际在 `BeforeRenderingTransparents` 之后）
[12] Ho-CharacterSpecialization（眼透 / 发影 / 脸色）          → eyecolor / eyedata
[13] Ho-ScreenProcess（其余语义效果）
[14] Ho-ImageProcess（最终图像链）
[15] AOV 导出层（可选）
[16] DebugTile（调试时最后）
```

> **勘误：**上图的“URP opaque / cutout / 透明常规绘制”标签应读作“URP opaque / cutout”；常规 transparent 在 `BeforeRenderingTransparents` 之后，位于 SSGI/AC/SSS source 之后。保留 `[11.5]` 行表示其真实位置。

**必须成立的偏序关系**（比线性列表更重要，改 pass event 时照这个检查）：

| 约束 | 为什么 |
| --- | --- |
| `Ho-ShadowCast → 所有效果`（**含 OIT**） | 实机确认 **OIT 在 `Ho-ShadowCast` 之后**，所以"材质/效果都要吃阴影"成立 |
| `GB ↮ OB`、`GB ↮ SB` | 三个生产轴默认互不读取；跨轴 gate 只能由具体消费者显式声明 |
| `OB ↮ SB`（互不依赖） | 谁先都行，但都必须在 pre-opaque **AC** 之前 |
| **`GTAO → opaque`** | 材质 forward 里采样 AO |
| **`opaque → SSGI`** | GI 要 opaque 后的颜色 |
| `{OB, SB} → AC → GTAO → opaque` | AC 只读 OB/SB，GB 不喂 AC；SemanticResolve 在 producer sample 资源后、pre-opaque 消费者前 |
| `AC → GTAO / opaque ForwardLit / SSS / OIT / PLR / 角色特化 / ScreenProcess` | AC 已在 pre-opaque 产生语义/属性；每个消费者按登记需求查询 |
| **GTAO 可选读 AC 语义** | 它排在 AC 之后；未登记语义需求时仍只用 layer mask / 材质意图 |

**实机校准过的三条顺序**（以实机为准，与直觉/推导冲突的地方按这里）：

| 实机事实 | 它推翻了什么 |
| --- | --- |
| **1. OIT 在 `Ho-ShadowCast` 之后** | 与 v0.1 的线性顺序一致（`[2] ShadowCast … [10] OIT`），因此**"阴影 → 所有效果"成立，含 OIT**。⚠ 此条我先按"OIT 在 ShadowCast **之前**"记过一次，已按实机更正——**OIT 可以吃附加灯 atlas** |
| **2. SSGI 在角色特化之前** | 推翻 v0.1 §7 待确认里记的"SSGI 在 CharSpec **后**"——那条按实机为准应写成"之前"。也意味着**角色特化不能成为 SSGI 的输入**（GI 要用角色相关的排除，只能走 `gisexclude`） |
| **3. GB 在 OB 之前** | 这是当前实机 pass 顺序事实，不代表 OB producer 读取 GB；三轴仍保持互不读取 |

> 这三条是**实机 Frame Debugger 校准**的结果，优先于本文按前置条件推出来的线性列表；线性列表继续表示"默认意图"，冲突处以本表为准。GB→OB 只冻结为当前执行顺序，不冻结跨 buffer 读依赖。

> GB/OB/SB/AC/GTAO 默认都在 `BeforeRenderingOpaques`，同事件内按 Renderer Feature 列表表达 `{GB,OB,SB} → AC → GTAO`。这些 `passEvent` 不是可任意打破偏序的自由项。

**反射偏序（与本 v2 同时冻结）**：

| 反射文档的时序 | v2 里对应什么 | 状态 |
| --- | --- | --- |
| `beginCameraRendering` → **PLR source update** | 镜像相机必须早于 opaque | ✅ `PLR source → opaque` |
| `BeforeRenderingOpaques` → **GB + OB + SB + AC + GTAO** | 三轴输入→semantic/attribute resolve→材质 AO 依赖 | ✅ |
| Opaque ForwardLit → **SSR / SSGI** | SSR/SSGI 需要 opaque color；AC 已在 pre-opaque 生产语义/属性资源 | ✅ |
| OIT → **PLR 透明/特殊 resolve** | 透明反射只能在 OIT 结果之后读取完整颜色 | ✅ |
| `BeforeRenderingPostProcessing` → fullscreen resolve / debug | 只保留明确声明的特殊路径 | ✅ |

因此 v2 的完整反射相关偏序是：`PLR source → {OB,SB} → AC → opaque ForwardLit → SSR → OIT → PLR 透明/特殊 resolve`；Probe/Sky 是 source miss 时的材质 fallback，不另占一个 fullscreen pass。

---

## 3. 数据归属与"谁能读谁"

| feature | 回答什么 | 输出 | 不许做 |
| --- | --- | --- | --- |
| **GB**（GeometryBuffer，**名字不改**） | 几何在哪、朝向如何、几何覆盖多少 | **几何法线** / depth / 几何覆盖率 / 描边视觉壳 / sky | 不发表面数值、不发身份、不发着色法线 |
| **OB**（Ho-ObjectBuffer） | 这是谁、占多少 | 4 层 IdentityId+coverage、Facing(layer 0)、entry/group `objectSemanticLaneMask` | 不发表面数值、不画 object Selection RT |
| **SB**（Ho-SurfaceBuffer） | 表面是什么样、这个 sample 有哪些材质语义 | 五张数值 RT + SurfaceOwner；MSAA `(SemanticId,value)` + semantic owner | 不定义物体身份；owner 只是 AC 对齐键 |
| **Ho-AttributeComposite（AC）** | typed ID 怎么解压，object/surface 同 SemanticId 怎么合成 | `_HoACSelection{0..7}` + 合成属性图 + typed API + runtime catalog；身份池/Facing 引用 | 不画几何/表面、不写 EXR |

**三条读的规矩**：

1. **三者并列、禁止互读**：GB / OB / SB producer 互不读对方的产物；需要跨轴的量由消费端**在一次读取里各取一份**。只有登记过的具体 gate 才能形成额外依赖。
2. **遮罩只有一个来源 = AC**：任何屏幕效果都不许自己再攒一套语义图。
3. **消费者读合成结果，不读原始通道**；Cryptomatte ID/manifest 只属于 AOV 导出层，不是运行时属性合成器。

**法线分家**（已定论）：**GB = 几何法线**（AO/GI 遮挡、shadow bias、物理遮挡、描边）；**SB = 着色法线**（PBR 光照、SSR 反射方向、以及后续一切吃法线贴图的 feature）。两者平行、不互为来源。

### 3.1 本次冻结的最小契约

这些是实现可以直接依赖的语义；格式可以在逐通道落地时选择，但不能改变通道含义：

| 轴/字段 | 冻结语义 | 当前桥接 |
| --- | --- | --- |
| GB `NormalDepth.rgb` | world geometric normal，编码 `normal * 0.5 + 0.5` | `GeometryBuffer.NormalDepth.rgb` |
| GB `NormalDepth.a` | linear eye depth；`a > 1e-4` 才算 physical coverage | 当前已有 |
| GB `Depth.r` | raw/device depth；只供明确登记的 AO/Hi-Z consumer | `_HoGeometryBufferDepthTexture.r` |
| OB 身份池 `Id0` / `Id1` / `Coverage` | 4 层 ranked `(组 8 bit, 槽位 8 bit)` + 4 层覆盖率；ID 点采样 + `round(v*255)`；覆盖率线性、不归一化。请求 N=4，实际 N=4/2/1；只对“每 sample 唯一前表面 ID”承诺 `K≥N` 不丢，不表示多层透明贡献 | `MetadataBuffer.maskId` |
| AC `Selection` | `HoSemanticSchema` 将 8-bit SemanticId 绑定到最多 16 个 lane；AC 按 IdentityId 解压 object membership，与 SB sample `(SemanticId,value)` 按 sourceMode 合成，再 resolve 为 `(SemanticId,coverage)` | `_HoMetadataBufferMaterialCustom0_3Texture`（bridge） |
| SB `Color.rgb` | linear HDR base color；producer 不钳制；无 coverage 语义 | `SurfaceColor.rgb`（当前透明桥接可能是 premultiplied） |
| SB `Material.rgba` | `perceptualRoughness / metallic / thickness / reserved` | Target5 的 R/G；thickness 来自 `surfaceData.r` |
| SB `Reflection.rgba` | `reflectance / plrStrength / reserved / reserved` | Target5 的 B/A；通用环境/SSR strength 暂不占槽 |
| SB `Classification.rgba` | `sssProfileIdByte / curvatureHint / transmittanceHint / materialClassIdByte`；R/A 以 `round(v*255)` 还原 | `surfaceData.g/b/a` + 新 class ID（bridge） |
| SB `SurfaceOwner.r` | 16-bit IdentityId；0=未写，AC 与 OB layer 0 比较后接受数值属性 | 新 |
| SB `Normal.rgba` | `octa(shadingNormal).rg / reserved / reserved` | 当前尚未生产；GB 仍是 geometric normal |
| AC mask | 唯一的 runtime 具名遮罩与 coverage 来源 | 当前各消费者仍直接读 MetadataBuffer，迁移前不得假称已完成 |

反射总开关规则冻结为：`_UseReflection = 0` 时，所有 reflection strength 归零；`_UsePlanarReflection` 只能在总开关打开时生效。`roughness = perceptualRoughness²`，`F0 = lerp(reflectance, saturate(baseColor), metallic)`。

`SurfaceColor.a` 不进入 v2 的长期 coverage 契约；在桥接期只允许当前 SSS/角色特化消费者使用，迁移到 SB/AC 后删除该依赖。

---

## 4. 材质接口脚印的"载体"（v0.1 §6.4 的续写）

v0.1 的脚印表只写了"管线决定 / 材质轻量参数"，**没写这些数据存在哪**——这正是"多出一个 buffer 却没人预料到"的原因。补上载体列：

| 系统 | 它的数据载体 | 它的遮罩来源 |
| --- | --- | --- |
| OIT | 无（accumulation/revealage 结构在管线内） | AC（可选） |
| GI | `gisexclude`（独立通道） | AC |
| Shadow | `shadow.main` / `shadow.add0..N` | — |
| 反射 / PLR | **SB**（roughness / metallic / reflectance / PLR strength） | AC |
| 透射 / 折射 | **SB**（thickness / 吸收 …） | AC |
| AO | GTAO 的产物（屏幕空间）+ 材质意图 `_SSAO*` | AC |
| SSS | **SB**（thickness / curvatureHint / 表面色）+ `sssProfileIdByte`（与通用 `materialClass` 分离，通道待 SB §0.2 冻结） | AC |
| 角色特化（眼透 / 发影 / 脸色 / 轮廓） | **OB**（身份池：组 / 部件 / 物体位 / 覆盖率；朝向）+ GB（几何门控） | AC |
| AOV / 导出 | **OB 身份池**（Cryptomatte 语义面）+ SB（albedo） | 合规 `crypto_*` 导出归 AC 直出或以后的独立 feature |

---

## 5. 契约与 AOV 的变更清单（v2 要动的地方）

| 契约 v1 的条目 | v2 的动作 |
| --- | --- |
| `maskId`（MetadataBuffer Target0） | → **OB 的身份池**（`Id0` = 组 / `Id1` = 槽位 / `Coverage`，ranked、常开） |
| `objectCustom0/1`（Target3/4，8 位语义） | → **OB 身份池条目的物体位属性**（组 / 部件 / 标记 / 物体位 0~7），**不再是两层图**；位含义条款作废 |
| `custom0`（Target2，未登记） | → 默认 4 个 surface-writable SemanticId/lane；SB 逐 sample 写 ID+value，AC 与 object membership 合成 |
| `surfaceData`（Target1） | → **SB** `Material.b + Classification`，用 SurfaceOwner 表达显式 validity |
| `reflectionMaterial`（Target5） | → **SB**（具名 `Material` + `Reflection`）；当前 Target5 只作为 bridge |
| `surfaceColor` | → **SB**（`Color`）；`A=coverage` 只作为当前 bridge，长期由 OB/AC 管理 |
| `sssprofile` | → SB `Classification`；`sssProfileIdByte` 与通用 `materialClass` 必须拆分语义，见 SB §0.2 |
| `motion` | 不变（占坑） |
| 新增 | OB IdentityMS/identity pool/Facing/objectSemanticLaneMask；SB SurfaceOwner + SurfaceSemantic MSAA source；AC `_HoACSelection*` / typed query / runtime catalog |
| 导出档位 | 现状（UINT 通道）保留；**合规 `crypto_*`（float 位重解释 + manifest + 32 bit）归独立 AOV/export feature**，经 AC 资源集读 OB 身份池 + runtime catalog |

**当前桥接规则**：反射仍临时读取 MetadataBuffer Target5；SurfaceBuffer 的 Reflection 通道落地后，Target5 停止新增消费者，再删除桥接。`NormalDepth` coverage gate 继续是所有屏幕空间反射的硬规则；`DepthTexture.r` 的 raw/device depth 只允许在明确登记的 Hi-Z/AO 消费者中读取。

> 契约 v2 必须走 `LILTOON_CHANNEL_CONTRACT_V1.md` §3 的登记模板 + §5 变更记录；AOV 名只增不改，编码变更必须明确标为 bridge → v2。

---

## 6. 路线图（串联后的顺序）

| 阶段 | 内容 | 为什么在这个位置 |
| --- | --- | --- |
| **R0** | 本文 + 最小契约冻结 + 在 v0.1/反射文档上标注取代关系 | **已完成**；后续代码以本表为准 |
| **R1** | 修 CB 骨架的 Reverse-Z/actual N/group ID 冲突/Debug Registry；完成 ObjectBuffer 改名、IdentityMS+Facing、entry semantic mask 与 lilToon `HoObjectBuffer` pass | 跨两仓库的底层协议迁移 |
| **R2** | OB 的**朝向图**（forward+side，octahedral 打包进一张 RGBA8）+ 调试视图 | 它同时验证"逐物体辅助量"这条可写通道的机制 |
| **R3** | **SB 落地**：五张数值 RT + SurfaceOwner；Classification 四通道；4/8/16 lane SurfaceSemantic MSAA batches | 数值 0/未写可区分，同 SemanticId 可由表面贴图生产 |
| **R4** | **AC 落地**：`HoSemanticSchema` + typed API + runtime catalog + pre-opaque SemanticResolve/AttributeComposite + MRT batching + owner alignment debug | object/surface 语义在 sample 级统一；GTAO 和后续 feature 共用同一解压结果 |
| **R5** | **消费者输入切换**（§6.2）：ScreenProcess 图层**新接** AC 具名遮罩（原 20 个 rule source 已作为未使用功能删除，没有旧配置要迁移）；角色特化 `maskId` / `objectCustom0-1` / `surfaceColor` → AC（组 / 物体位）+ SB（表面色）；**它们自己的调试与登记也按 §6.1 改** | 按依赖面从小到大迁移 |
| **R6** | 删 MetadataBuffer；契约出 v2 | 全仓库无 `_HoMetadataBuffer` 引用 |

**与 v0.1 §11 推进顺序的关系**：v0.1 定的是 `GTAO → SSGI → 其余系统`。GTAO/SSGI 已经在做，**R1-R4 属于"其余系统"里的地基工程**，与它们并行不冲突（互不读对方的产物）。

### 6.1 每个 feature 都要做的"调试与登记"（OB / SB / AC 各自）

**不是可选项**：没有 debug 视图与登记，通道就等于没落地（契约 §3 模板本来就要求 debug 列）。

| # | 事项 | 落点（仓库里现成的机制） |
| --- | --- | --- |
| 1 | **自己的 debug pass + debug 视图**：每个池 / 每张图一条，能单独看 | feature 自己的 `*DebugPass` + `Shaders/Debug/*.shader` |
| 2 | **注册视图** | `HoDebugViewInfo` + 加进 `HoDebugViewRegistry.AllViews`。⚠ **今天 `HoCharacterBufferDebugViewInfo` 没进 registry**——OB 顺手补上 |
| 3 | **新增 render kind** | `HoDebugViewRenderKind` 加枚举值（每个 feature 一个） |
| 4 | **让 DebugTile 接得上** | `HoDebugTileRendererFeature`：可用性判定（`has*`）+ `BuildTiles` 过滤 + `ResourceNeeds.FromTiles` 分支；`HoDebugTile.shader` 加对应 slice；`LilUrpDebugShaderValidator` 的收集表 |
| 5 | **契约登记的 debug 列** | `LILTOON_CHANNEL_CONTRACT_V1.md` §3 模板逐条填 |
| 6 | **失败可见**（不静默） | 声明与已分配 RT 张数不一致 / 未声明 ID / 一像素 ID 溢出 / 非法槽 / 消费者声明的名字解析不到 → 视图里标出 + 告警 |
| 7 | **UI 按家规写** | `Ho-UI_风格规范.md`：**调试入口在 Volume**（`HoXxxVolume` 的「调试」分组），**feature 只放高级设置 + 兜底默认值**；分节走 `LilUrpEditorSectionGui`，色板与摘要格式照旧 |

### 6.2 消费者输入切换（R5 的具体清单）

| 消费者 | 今天吃什么 | 切换后 | 要一起做掉的 |
| --- | --- | --- | --- |
| **ScreenProcess** | 只剩"层遮罩 = MetadataBuffer `maskId` 覆盖率 + 每层开关/反转/debug"；原 `ScreenProcessRuleSource` 20 个值 + ≤4 条规则列表**已作为未使用功能删除**（`Runtime`/`Editor` 里已无引用） | **AC 的具名条目**：图层声明名字，C# 侧解析成 ID，shader 走 `HoAC_*` | ① R5 作为**新工作**加"具名条目 + 组"（没有旧序列化配置要迁移）；② `ScreenProcessRuntimeDiagnostics` 的 `Requires*` / `*Available` 家族换成 AC 的登记与解析诊断；③ 遮罩采样点（`ScreenProcessMask.hlsl`）保持不变，只换来源；④ 4 张缺省黑纹理 → AC 的缺省 |
| **角色特化** | `maskId`（组 / 部件 / 标记 / 权重）+ `objectCustom0/1`（8 位）+ `SurfaceColor` | **AC**：组 / 物体位 / 覆盖率（`HoAC_Group` / `HoAC_Mask`）+ **SB**：表面色 | ① 输入改走 AC 的查询 API；② `_HoMetadataBufferActive` → AC 的 active；③ 遮罩模糊等 pass 的输入同样换掉；④ 自己的 debug 视图与登记（§6.1） |
| **SSS / PLR** | MetadataBuffer 的 surface 族 + `custom0` | **SB 数值 + AC 遮罩** | 遮罩模糊与分类读法一起换；`surfaceData` 的老依赖删掉 |

---

## 7. 待确认

1. **AC 的纯值来源**：由材质声明的 surface payload 提供，还是由独立 Subject/Group 组件提供；必须先于消费者迁移冻结。
2. **透明 PLR 是否需要 receiver/source-id RT**：只有材质/OIT 直接消费无法满足多平面时才立项。

（已解决并移出：SB 的 RT packing → **`Material` 与 `Reflection` 不并**，各自一张；OB 的命名搬迁 → **`HoObjectBuffer*` / `_HoObjectBuffer*` / `object.*`**，见 `Ho-ObjectBuffer_规划.md` §3。）

# Ho-SurfaceBuffer 规划：把散落的表面属性收进一个 feature

> 状态：**设计草案待拍**（§7 有 5 项要定；定了才开工。**其中第 1 项会改动 v1 契约的冻结决议**，见 §0.1）。
> 前因：`Ho-CharacterBuffer_规划.md` 决策 20——MetadataBuffer 被拆成"身份/覆盖率"（CB）、"几何"（GB）、"表面数值"（本文）三段。
> 目标：让"表面是什么样"有唯一的家，且**不重新变成一个什么都装的 buffer**。
> 已冻结的上位文档：`LILTOON_FORMAL_PIPELINE_DRAFT.md`（§3 层模型/帧序、§6 设计决策、§10 命名分析）、`LILTOON_CHANNEL_CONTRACT_V1.md`（§3 声明模板、§4 冻结决议）。**动手前先读 §0.1。**

---

## 0. 摘要

MetadataBuffer 今天一个 buffer 里同时装着身份、覆盖率和表面数值。身份与覆盖率已经拆去 CB；**剩下这一堆表面属性没有家**：线性表面色、roughness / metallic、reflectance / PLR strength、thickness / curvature / materialClass / transmittance、以及 `custom0~3` 那四个匿名通道。

`Ho-SurfaceBuffer`（SB）接住它们。它的边界只有一句话：**回答"表面是什么样"，不回答"这是谁"、也不回答"几何在哪"**。身份与覆盖率读 CB，几何读 GB；SB 自己**不发布深度、不带任何身份 ID**。

---

## 0.1 与已冻结文档的关系（动手前必读）

这份规划不是白纸起草，它落在三份已存在的文档之间，其中两处**必须走修订**而不是顺手改：

| 已冻结的东西 | 它怎么说的 | 本规划的关系 |
| --- | --- | --- |
| `LILTOON_FORMAL_PIPELINE_DRAFT.md` **§3.1 层模型 / §3.2 帧序**（定稿） | L1 语义输入 = "`CharacterBuffer(Metadata)`（对象 / mask / **surface**）+ `ScreenGeometryBuffer`"；帧序 [3] 是 `Ho-CharacterBuffer(MetadataBuffer) → 语义（对象/mask/**surface**/objectCustom bits）` | **surface 原本就写在 CB 的槽位里**——所以"S B 独立"是对**定稿文档的修订**：L1 要拆成"身份与覆盖率（CB）"+"表面数值（SB）"两条，帧序 [3] 要拆成两个条目（SB 紧跟 CB，同属 L1）。**修订要落进那份文档本身**，不能只写在本文里 |
| 同 **§6.2 排序铁律** + **§6.4 材质接口脚印**（定稿） | "7 个系统做稳（OIT / GI / shadow / 反射折射透射 / AO / SSS / 多光）→ **每系统落地时登记"材质接口脚印"**到通道契约 + AOV → 最后统一收敛 lilToon 暴露面" | **SB 的通道集合由这些"脚印"驱动，不是"MetadataBuffer 有什么就搬什么"**：§6.4 里 SSS 要 `sss strength/mask/tint`（收进 profile preset）、透射要 `厚度/吸收/强度/菲涅尔`、反射要 `强度/扰动/平滑/遮罩`——SB 是这些数值的**容器**，通道表要能逐条指回某个系统的脚印（§2.0） |
| 同 **§10 命名分析**（定稿） | `MetadataBuffer → CharacterBuffer`；`GeometryBuffer → ScreenGeometryBuffer`；**"两者并列、禁止互读"**；改名在 **v1 冻结时批量做**（避免中途两套名） | SB 是**第三个兄弟**：同样"并列、禁止互读"。命名分析要按 §10 的表格式给出（§1.5），改名同样**并进那次批量改名**，不要现在单独改 |
| `LILTOON_CHANNEL_CONTRACT_V1.md` **§3 声明模板 / §4 冻结决议** | §3 给了通道登记的固定模板；§4 冻结了两条与 SB 直接冲突的语义 | **SB 会改动两条 v1 冻结决议**（见下表），必须走 **v2 修订**并记进 §5 变更记录 |

**会被 SB 触发修订的两条冻结决议**（这是本规划最需要谨慎的部分）：

| 契约 §4 冻结项 | 冻结的时候是这么说的 | SB 要改成 | 为什么必须改 |
| --- | --- | --- | --- |
| 第 4 条 `SurfaceColor` | "producer 不钳制 RGB；需要 `[0,1]` 的消费者自行显式钳制，**A 始终为 coverage**" | **A 不再承载覆盖率**（保留位）；覆盖率只从 CB 的 `_Coverage` 读 | 一个 byte 同时当颜色和覆盖率，就是 MetadataBuffer 那四个消费端"一改深度策略就集体变味"的根因。覆盖率**必须只有一个来源** |
| 第 5 条 `ReflectionMaterial` | "MetadataBuffer Target5 语义冻结为 perceptualRoughness / metallic / reflectance / PLR strength；A 同时服从 `_UseReflection` 与 `_UsePlanarReflection`" | 拆成 SB 的 `Material`（roughness/metallic）+ `Reflection`（reflectance/PLR strength），**门控语义照旧**（仍由 `_UseReflection` + `_UsePlanarReflection` 决定 A 是否有效） | 16F 装 4 个 0~1 量浪费一半带宽；且"反射参数"与"通用材质参数"混在一张图里，消费者要靠约定认通道 |

> 另外：契约 §4 第 1 条"matte bits 位含义以组件 Inspector 为准"这句，会随 CB 的**部件表 + Cryptomatte 选择**取代 8 bit 语义而失效——那条的修订归 CB 的 P4，本文不重复。

---

## 1. 决策（草案）

**结构与边界**

| # | 决策 | 一句话后果 |
| --- | --- | --- |
| 1 | **只装表面数值**：线性表面色、粗糙度、金属度、反射率、PLR 强度、厚度、曲率、材质分类、透射提示、（可选的）SSS profile | 身份 → CB；几何/深度 → GB；SB **不发布深度、不发布 ID** |
| 2 | **写入端 = 材质（逐像素）** | 谁的值谁写。不再有"组件覆盖材质"的协议，也没有第二份副本（决策 20 顺带解掉的题） |
| 3 | **单采样 + 自己的深度附件，沿用两段式深度策略** | opaque/cutout 先写深度确立归属，transparent 只 ZTest 后叠加；与 ID pass 的"全员 ZWrite On"互不干扰（因为分属两个 feature） |
| 4 | **按需分配，逐通道登记消费者** | 没有消费者的通道不分配（规划沿用 `LILTOON_CHANNEL_CONTRACT_V1.md` 的"无消费者不登记、不输出"） |
| 5 | **`custom0~3` 不复存在** | 四个匿名通道由 CB 的 **Cryptomatte 选择层**取代（CB 规划 §5.11）——SB 不接这锅 |

**工程默认**（按推荐值执行，改动成本低）

| # | 决策 | 依据 |
| --- | --- | --- |
| 6 | 通道布局见 §4.1：`Color`(16F) + `Material`(8×4) + `Reflection`(8×4) + `Classification`(8×4) | 管线量都是 0~1 归一量（8 bit 够）；表面色是线性 HDR（必须 16F） |
| 7 | **不做"常量进 palette"的优化** | 那需要 CPU 知道材质值（要么重复一份、要么走跨仓属性名协议）；8 bit × 几张图的带宽换一个明确的写入端，划算 |
| 8 | 采样：**Point**（材质值不做双线性） | 与 `surfaceData` / `reflectionMaterial` 今天的取样方式一致；也避免"跨部件混色"——材质值属于**最近的那个面** |
| 9 | 名字：`Ho-SurfaceBuffer` / `_HoSurfaceBuffer*`，**不用 `VisualSurface*`** | GB 文档 §11 的 `VisualSurfaceBuffer` 是**描边视觉壳层**（几何轴），两者不是一回事，名字必须避开 |

---

## 2. 现状调查（要从 MetadataBuffer 接住什么）

### 2.0 设计驱动：不是"搬什么"，而是"谁要什么"

按 `LILTOON_FORMAL_PIPELINE_DRAFT.md` §6.2 的铁律，通道集合应当**由各系统的"材质接口脚印"驱动**（§6.4 已给出三行与 SB 直接相关）：

| 系统（§6.4） | 要落进 SB 的数值 | 落在哪张图 |
| --- | --- | --- |
| **SSS** | `thickness`（厚度）、`curvature`、`material class`、`transmittance`；profile 另议（§7） | `Material.b` + `Classification` |
| **透射 / 折射** | 厚度 / 吸收 / 强度 / 菲涅尔（"收成预设"） | 厚度复用 `Material.b`；其余若要做，按同一流程登记 |
| **反射** | intensity / 扰动 / 平滑（roughness）/ 遮罩；`_UseReflection` + `_UsePlanarReflection` 门控 | `Material.r`(roughness) + `Reflection` |

**这一节决定了下面清单的地位**：§2.1/§2.2 是"今天真实存在着什么"（迁移时一个都不能掉），但**通道的取舍依据是上表**——加通道要能指回某个系统的脚印，不然就是又一次"往 buffer 里塞东西"。

### 2.1 通道清单（今天在哪、谁写、谁读）

| 今天 | 格式 | 内容 | 写入端 | 消费端 |
| --- | --- | --- | --- | --- |
| `surfaceColor` | RGBA16F | RGB = 线性 HDR 表面色；**A = coverage** | 材质（`HoMetadataBufferSurfaceColor` pass） | 角色特化·脸色扩散、SSS、PLR、AOV `diffuse_albedo`、debug |
| `surfaceData` | RGBA16F | r = thickness、g = curvature、b = material class、a = transmittance hint | 材质（`HoMetadataBuffer` pass） | SSS、ScreenProcess 规则（4 个 source：Thickness / Curvature / Material / TransmittanceHint） |
| `reflectionMaterial` | RGBA16F | r = perceptualRoughness、g = metallic、b = reflectance、a = PLR strength（由反射总开关 + PLR 开关共同门控） | 材质（Target5） | PLR / SSR / Probe、AOV |
| `custom0`（`MaterialCustom0_3`） | RGBA16F | 材质自定义 0~3 | 材质 | ScreenProcess 规则（Custom0~3 四个 source） |
| `maskId` | RGBA8 | r = coverage/权重、g = 角色 ID、b = 部件 ID、a = flags | 材质 / 对象 | 角色特化、ScreenProcess、AOV `id_object`/`id_group` |
| `objectCustom0/1` | RGBA16F ×2 | 8 个语义各 1 bit（CharacterFull / Face / FrontHair / Eye / EyeRevealArea / Accessory / CharacterBody / Reserved7） | 材质 / Group | 角色特化、ScreenProcess（8 个 source）、AOV `matte_*` |
| `depth` / `mBufferDepth` | 深度附件 | 两张 depth-stencil（前者纯附件、后者只有两个 debug 视图读） | — | debug |

> **结论**：`maskId` + `objectCustom0/1` 属 **CB**；`surfaceColor` / `surfaceData` / `reflectionMaterial` / `custom0` 属 **SB 或选择层**；两张深度附件**谁都不该发布**（SB 只留一张自用）。

### 2.2 消费端逐个点名（迁移时一个都不能掉）

| 消费端 | 今天读什么 | 迁移后读什么 |
| --- | --- | --- |
| SSS | `maskId.r`（覆盖率）、`surfaceData`（thickness / curvature / material class / transmittance）、`surfaceColor` | CB `_Coverage` + SB `Material` / `Classification` / `Color` |
| PLR | `maskId.r`、`reflectionMaterial`（roughness / metallic / reflectance / PLR strength）、`surfaceColor.a` | CB `_Coverage` + SB `Reflection` + SB `Color` |
| 角色特化·脸色扩散 | `surfaceColor`（含 `.a` 当 coverage） | SB `Color.rgb` + CB `_Coverage`（**`.a` 不再承载覆盖率**） |
| 角色特化·composite / 轮廓场 | `objectCustom*`、`maskId.g` | CB `_Id*` / `_Coverage` / palette（不碰 SB） |
| ScreenProcess 规则（20 个 source） | `maskId` 四通道、`surfaceData` 四通道、`custom0` 四通道、`objectCustom0/1` 八位 | **身份族（12 个）→ CB**；**表面族（8 个：Thickness / Curvature / Material / TransmittanceHint / Custom0~3）→ SB + Cryptomatte 选择**（`Custom0~3` 换成具名选择） |
| AOV | `diffuse_albedo`(surfaceColor)、`id_object`/`id_group`(maskId)、`matte_*`(objectCustom) | SB `Color` 供 `diffuse_albedo`；其余 CB |
| DebugTile / DebugView | 全部 | 每个通道都要有 SB 视图（沿用"没有视图 = 成本不可验证"） |

### 2.3 已经不属于 SB 的（写在这里免得日后又混进来）

- **身份 / 覆盖率 / 选择** → CB（`_Id0` / `_Id1` / `_Coverage` / `_Selection`）。
- **法线 / 深度 / 几何覆盖率 / 描边 / 天空** → GB。
- **深度通道**：SB **不发布**。需要几何门控的消费者读 GB（与 CB 同一条纪律，见 CB 规划决策 16）。

---

## 3. 现状的债（SB 要顺手还掉的）

1. **`surfaceData` 用 16F 装 4 个 0~1 归一量**：thickness / curvature / material class / transmittance 全是小范围标量，16F 每像素 8 B —— 8 bit 就够，**一张图省一半带宽**。
2. **`reflectionMaterial` 同病**：roughness / metallic / reflectance / PLR strength 也是 0~1（PLR strength 可 >1，见 §7 未定项）。
3. **`custom0~3` 是匿名通道**：契约里连登记都没有（只在备注里被点过一次名），消费端只能写 `Custom2 > 0.5` 这种没信息量的条件 —— 由 CB 的 Cryptomatte 选择层取代。
4. **`surfaceColor.a` 兼任覆盖率**：同一个 byte 既当颜色又当覆盖率，四个消费端各自解释（MetadataBuffer 的 `mBufferDepthWriteStateBlock` 一改就集体变味）。迁移后 **SB 只管颜色，覆盖率只有 CB 一个来源**。
5. **两张深度附件**：`depth` 纯附件（连全局 ID 都没有）、`mBufferDepth` 只有两个 debug 视图读 —— SB 只留一张自用附件，不发布。

---

## 4. 设计

### 4.1 通道布局（草案）

| 通道 | 格式 | 内容 | 分配 |
| --- | --- | --- | --- |
| `_HoSurfaceBufferColor` | RGBA16F | RGB = 线性 HDR 表面色（今天 `surfaceColor.rgb`）；**A 保留**（不承载覆盖率——覆盖率只有 CB 一个来源） | 按需（角色特化脸色扩散 / AOV） |
| `_HoSurfaceBufferMaterial` | RGBA8 | r = roughness、g = metallic、b = thickness、a = 备用 | 按需（PLR / SSS） |
| `_HoSurfaceBufferReflection` | RGBA8 | r = reflectance、g = PLR strength（归一后）、b/a 备用 | 按需（PLR / SSR / Probe） |
| `_HoSurfaceBufferClassification` | RGBA8 | r = material class、g = curvature、b = transmittance hint、a = 备用 | 按需（SSS / ScreenProcess 规则） |
| （SB 自己的 depth-stencil） | 深度格式 | 两段式深度策略用 | 常开、**内部附件**、不发布 |

**合计**：全开 20 B/px；只开 `Material` + `Classification` 这些常见组合是 8 B/px。

### 4.2 精度为什么这么分

| Buffer | 语义定位 | 内容 | 主服务对象 | 消费方 |
| --- | --- | --- | --- | --- |
| `CharacterBuffer`（已定名） | 对象/角色语义（forward-style，随材质 pass 写） | `Id0`/`Id1`/`Coverage`（4 层 `(ID, 覆盖率)`）+ Cryptomatte 选择 | **角色/对象身份** | 角色特化、ScreenProcess 规则、AOV、SSS/PLR 的门控 |
| **`SurfaceBuffer`（本规划）** | **表面数值**（material-style，随材质 pass 写） | 线性表面色 + roughness/metallic/thickness/reflectance/PLR/class/curvature/transmittance | **"表面是什么样"** | SSS、PLR/SSR/Probe、角色特化脸色扩散、ScreenProcess 规则、AOV `diffuse_albedo` |
| `ScreenGeometryBuffer`（已定名，现名 GeometryBuffer） | 屏幕可见几何（deferred-style） | normal / depth / 几何覆盖率 / 描边 / sky | **全屏几何** | AO/GI/SSS/反射/角色特化/AOV |

**结论**：

1. 三者**并列、禁止互读**（沿用 §10 第 3 条；CB ↔ GB 已经是这条，SB 加入后同样适用）。需要跨两边的消费者，**自己在一次读取里取两份数据**，不让一个 feature 去读另一个的产物。
2. 名字用 **`Ho-SurfaceBuffer`**：`Material` 会与"材质资产/材质参数"混淆，而这张图里最贵、最不可替代的是**线性表面色**（它就是"表面"）；`VisualSurface*` 已被 GB §11 的描边视觉壳层占用，**必须避开**。
3. 改名与文件重命名**并进"v1 冻结时批量改名"那一次**（§10 第 3 条），现在不动 GB / MetadataBuffer 的名字。

### 4.2 精度为什么这么分

- **8 bit**：roughness / metallic / thickness / curvature / transmittance / material class / reflectance —— 全是 0~1（或小整数）标量，8 bit 的分辨率在着色里看不出台阶，而它们是**线性量**（可乘可 lerp）。
- **16F**：只有表面色需要（线性 HDR，且会被当作 albedo 参与合成与 AOV 导出）。
- **需要单独判的**：PLR strength 可能 >1（今天 `reflectionMaterial.a` 是 16F 且不限幅）→ 见 §7。

### 4.3 写入端与门控

- **材质侧逐像素写**（决策 2）：lilToon 材质在 `HoSurfaceBuffer` pass 里按材质开关写对应 MRT；跨仓属性名与开关表在实施时与 lilToon 侧一起冻结（与 CB 的选择槽协议同形）。
- **图按需分配**：feature 设置里逐通道开关（或由消费者登记自动推导），没开的通道不分配、shader 也不输出 MRT（**MRT 数量必须按实际需要来**，否则白付带宽）。
- **fallback 材质**：只覆盖不透明队列（override 材质看不到源材质的 alpha/cutout），与 CB / MetadataBuffer 同一取舍。

### 4.4 深度策略

沿用决策 15（CB 规划）搬过来的那条：

- **opaque / cutout**：写深度，确立"这个像素的表面是谁"；
- **transparent**：只 ZTest、不写深度，颜色叠加；
- 两张清单在 feature 设置里各有一组 render queue 范围（与今天 `SurfaceColorOpaque/Transparent` 两个 filtering settings 同构）。

### 4.5 与 GB / CB 的边界与禁令

- **不发布深度、不发布 ID**（决策 1）。
- **不读 CB 的 ID**：需要"某个部件的表面值"的消费者，自己在一次读取里同时拿 CB 的覆盖率与 SB 的数值，再按 CB 规划 §6 第 7 条加权（`Σ coverage_i · f(id_i)`）——**不让一边去乘另一边的图**。
- **取样一律 Point**（决策 8）：材质值属于最近的那个面，双线性会把两个部件的材质糊在一起。
- **SB 里没有任何身份**，所以它不背 CB 的"ID 不可滤波"禁令；反过来 CB 的 ID 也绝不允许出现在 SB 里。

### 4.6 消费端迁移映射

| 消费端 | 迁移动作 |
| --- | --- |
| SSS | `surfaceData` → `Classification`（class / curvature / transmittance）+ `Material.b`（thickness）；覆盖率 → CB `_Coverage` |
| PLR / SSR / Probe | `reflectionMaterial` → `Material.rg` + `Reflection.rg`；覆盖率 → CB |
| 角色特化·脸色扩散 | `surfaceColor` → `Color.rgb`；**不再读 `.a`**（覆盖率读 CB） |
| ScreenProcess 规则 | surface 族 source 改指 SB；`Custom0~3` 四个 source **换成具名 Cryptomatte 选择** |
| AOV | `diffuse_albedo` 指向 SB `Color`；其余不变 |
| DebugTile | 新增 `surface.*` 视图（4 张图 + 逐通道） |

### 4.7 契约登记（按 `LILTOON_CHANNEL_CONTRACT_V1.md` §3 的模板逐条写）

```text
通道名: surface.color
生产端: 材质 → Ho-SurfaceBuffer pass
消费端: 角色特化·脸色扩散、SSS、PLR、AOV diffuse_albedo
编码:   RGBA16F（RGB = 线性 HDR 表面色，不钳制；A = 保留）
生命周期: 帧持久（按需分配）
AOV:    diffuse_albedo（沿用现有导出名，换生产端）
debug:  surface.color
冻结:   否
```

```text
通道名: surface.material
生产端: 材质 → Ho-SurfaceBuffer pass
消费端: SSS（thickness）、PLR/SSR/Probe（roughness/metallic）、ScreenProcess 规则
编码:   R8G8B8A8（r=roughness, g=metallic, b=thickness, a=备用）
生命周期: 帧持久（按需分配）
AOV:    不导出（除非将来有需求）
debug:  surface.material
冻结:   否
```

```text
通道名: surface.reflection
生产端: 材质 → Ho-SurfaceBuffer pass（A 有效性仍由 _UseReflection + _UsePlanarReflection 决定）
消费端: PLR / SSR / Probe
编码:   R8G8B8A8（r=reflectance, g=PLR strength, b/a 备用）
生命周期: 帧持久（按需分配）
AOV:    不导出
debug:  surface.reflection
冻结:   否
```

```text
通道名: surface.classification
生产端: 材质 → Ho-SurfaceBuffer pass
消费端: SSS（class/curvature/transmittance）、ScreenProcess 规则
编码:   R8G8B8A8（r=material class, g=curvature, b=transmittance hint, a=备用）
生命周期: 帧持久（按需分配）
AOV:    不导出
debug:  surface.classification
冻结:   否
```

**同时要改的既有行**（改的是"谁提供"和"编码"，**名字不动**——契约 §2 冻结"AOV 只增不改"）：

| 既有行 | 改什么 |
| --- | --- |
| `surfaceColor` | 生产端换 SB；编码的 `A=coverage` 去掉（**v2 修订**，见 §0.1） |
| `surfaceData` | 整行退役 → `surface.material` + `surface.classification` |
| `reflectionMaterial` | 整行退役 → `surface.material.rg` + `surface.reflection`（**v2 修订**） |
| `custom0`（未登记） | 不迁进 SB：由 CB 的 Cryptomatte 选择取代 |

> 走**契约 v2**：`LILTOON_CHANNEL_CONTRACT_V1.md` §4 的两条冻结决议要改（§0.1 的表），并在 §5 变更记录里留一行。**在这之前 SB 的通道只登记、不冻结。**

### 4.8 成本对照（1920×1080）

| | 现状（MetadataBuffer 的相关部分） | SB |
| --- | --- | --- |
| 常驻 | `surfaceData` 8 + `custom0` 8 + `reflectionMaterial` 8 + `surfaceColor` 8 = **32 B/px ≈ 63 MiB** | 按需：`Color` 8 + `Material` 4 + `Reflection` 4 + `Classification` 4 = **全开 20 B/px ≈ 39 MiB** |
| 常见组合 | 4 张全在 | `Material` + `Classification` = **8 B/px ≈ 16 MiB** |
| 深度附件 | 2 张（`depth` + `mBufferDepth`） | **1 张自用、不发布** |

即：**全开也只有现状的 62%，常见组合是 25%**；而且"只开需要的"这件事在 SB 里是结构保证（按需分配 + 无消费者不登记）。

---

## 5. 线性/非线性纪律

SB 里**全是线性量**（颜色、0~1 标量），所以：

- **允许**：乘、lerp、按覆盖率加权求和、按需双线性（但默认 Point，见决策 8）。
- **禁止**：把 SB 的值当身份用（不许拿 roughness 去判"这是哪个部件"）；也不许把 CB 的 ID 塞进 SB。
- **颜色空间**：`Color` 是**线性 HDR**（不钳制），与今天 `surfaceColor` 一致；AOV 导出时由导出链决定是否转显示空间。

---

## 6. 分期（与管线草案 §6.2 铁律、§11 推进顺序对齐）

**铁律决定了分期形状**：SB 是"容器"，但它的每一格要等对应系统落地时才登记（§6.4 的脚印）。所以**不是一次把 4 张图全开出来**，而是容器先立、格子按系统逐步填。

| 阶段 | 内容 | 验收 |
| --- | --- | --- |
| **S0（本文件）** | 调查 + 布局草案 + 边界 + 与已冻结文档的修订清单（§0.1） | §7 的 5 项拍定 |
| **S1（文档先行）** | ① 修订 `LILTOON_FORMAL_PIPELINE_DRAFT.md` §3.1/§3.2（L1 拆"身份/覆盖率"与"表面数值"，帧序 [3] 拆两条）；② 提**契约 v2**（§0.1 的两条修订 + §4.7 的登记行）；③ 把本文 §4.9 的命名分析并入 §10 的批量改名计划 | 两份定稿文档里能查到 SB；契约 §5 变更记录有 v2 一行 |
| **S2** | `HoSurfaceBuffer*` feature：通道**按系统逐个开**（先 `Material` + `Classification` 给 SSS，再 `Reflection` 给反射），自用深度 + 两段式策略，RG/兼容两条路径，调试视图 + 编辑器抽屉（与 CB 同款式） | 开着的通道在 debug 里能看到；没开的**不分配、不输出**；消费端登记与通道一一对应 |
| **S3** | lilToon 侧 `HoSurfaceBuffer` pass（跨仓）：按材质开关写 MRT；`HoMetadataBufferSurfaceColor` 那一趟退役；**同步登记"材质接口脚印"**（§6.4 的三行：SSS / 透射折射 / 反射） | 表面色与材质值与原通道逐像素一致（可 A/B 对比）；脚印在契约里能查到 |
| **S4** | 消费端迁移：SSS / PLR / 角色特化脸色扩散 / ScreenProcess 规则 / AOV `diffuse_albedo` | 行为不变或更好；`surfaceColor.a` 不再是覆盖率（v2 生效） |
| **S5** | 与 CB 一起进 CB 规划的 P4：删 MetadataBuffer 的 surface 族通道与写入端 | 全仓库无 `_HoMetadataBuffer` surface 族引用 |

> 依赖关系（§11 推进顺序是 `Ho-GTAO → Ho-SSGI → 其余系统`）：**S2 可以在 GTAO/SSGI 之后并行做**，但 S3 的材质脚印要跟着 SSS / 反射系统各自的落地节奏走——否则就是"先裁材质暴露面"，正好违反 §6.1 的"不能现在就裁剪"。

---

## 7. 未定项（拍完才能开工）

1. **契约 v2 的两条修订，现在一起提还是往后放？**（§0.1）其中 `SurfaceColor` 的 `A=coverage` 是 v1 明写的冻结项，改它等于改"覆盖率有几个来源"——**这是本次整改的核心，我建议一起提**；`ReflectionMaterial` 的拆分可以晚一步（它只关乎带宽与命名，不关乎正确性）。
2. **`materialClass` / curvature / transmittance 放图还是放表**：本文草案放 `Classification`（材质侧逐像素写）。但 `materialClass` 严格说是**分类不是数值**——按 CB 刚立的规矩（身份与分类归表、数值归图）它更该进 palette，那就要回答"谁写 palette"（我倾向材质侧写 + 跨仓属性名协议，与 CB 的选择槽协议一起冻结）。
3. **PLR strength 的范围**：今天 `reflectionMaterial.a` 是 16F 且不限幅。约定 ≤1 就能塞进 8 bit（`Reflection.g`）；否则要单独一个 16F 通道或"归一 + 系数"（系数进 palette 或材质常量）。
4. **SSS profile 是否并进来**：今天 `sssprofile` 是独立 R8 通道（材质/profile 写、SSS 与 AOV 读）。并进来能少一张图，但要先确认它算不算"分类"（若算，按第 2 项一起进表）。
5. **管线草案的修订措辞**（§3.1 的 L1 两行怎么分、§3.2 帧序 [3] 是拆成 [3a]/[3b] 还是插一条 [3.5]）：我倾向**现在就写进那份定稿文档并标注"待实现后生效"**——理由是那份文档是别人读的入口，SB 只写在本文里等于没登记。

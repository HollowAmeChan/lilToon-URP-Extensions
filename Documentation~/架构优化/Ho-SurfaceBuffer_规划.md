# Ho-SurfaceBuffer 规划：把散落的表面属性收进一个 feature

> ⚠ **部分章节已作废（身份与遮罩已全部移出 SB）**：**决策 10-13、§4.10（场景侧身份）、§4.7 里的 `scene.idcoverage` / `scene.selection` / `scene.palette` 三条登记**均作废——场景物体的 ID 与具名遮罩归 `Ho-ObjectBuffer` + `Ho-Cryptomatte`。
> **SB 只回答"表面是什么样"**（含未来 PBR）。当前三轴划分见 `LILTOON_FORMAL_PIPELINE_DRAFT_V2.md`。
> 📁 `Ho-CharacterBuffer_规划.md` 已移入 `_归档/`（CB 已取消；其业界依据与覆盖率分析仍作参考）。

> 状态：**设计草案待拍**（§7 有 5 项要定；定了才开工。**其中第 1 项会改动 v1 契约的冻结决议**，见 §0.1）。
> 前因：`Ho-CharacterBuffer_规划.md` 决策 20——MetadataBuffer 被拆成"身份/覆盖率"（CB）、"几何"（GB）、"表面数值"（本文）三段。
> 目标：让"表面是什么样"有唯一的家，且**不重新变成一个什么都装的 buffer**。
> 已冻结的上位文档：`LILTOON_FORMAL_PIPELINE_DRAFT.md`（§3 层模型/帧序、§6 设计决策、§10 命名分析）、`LILTOON_CHANNEL_CONTRACT_V1.md`（§3 声明模板、§4 冻结决议）。**动手前先读 §0.1。**

---

## 0. 摘要

MetadataBuffer 今天一个 buffer 里同时装着身份、覆盖率和表面数值。身份与覆盖率已经拆去 CB；**剩下这一堆表面属性没有家**：线性表面色、roughness / metallic、reflectance / PLR strength、thickness / curvature / materialClass / transmittance、以及 `custom0~3` 那四个匿名通道。

`Ho-SurfaceBuffer`（SB）接住它们。它的边界是两句话：

1. **回答"表面是什么样"**（PBR 与屏幕效果的输入），**不回答"几何在哪"**（GB）；
2. **承担"场景侧的身份"**（`scene.*` 的 ID/覆盖率 + Cryptomatte），**与角色侧的身份（CB）平行且互斥**——场景物体因此也有可点选的 ID 遮罩，而两边数量各自独立增长。

**"表面"与"身份"在 SB 里不混**：表面数据是全场景的、单采样、逐像素；身份是分域的、自建 MSAA、逐样本投票。两趟 pass、两套生命周期（决策 13）。

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
| 1 | **装表面数值 + 场景侧身份**：线性表面色、粗糙度、金属度、反射率、PLR 强度、厚度、曲率、材质分类、透射提示、（可选的）SSS profile；外加**场景对象的 ID/覆盖率与 Cryptomatte**（决策 10-13） | 身份分域：**角色的身份归 CB，场景的身份归 SB**；几何/深度仍归 GB；SB **不发布深度** |
| 2 | **写入端 = 材质（逐像素）** | 谁的值谁写。不再有"组件覆盖材质"的协议，也没有第二份副本（决策 20 顺带解掉的题） |
| 3 | **单采样 + 自己的深度附件，沿用两段式深度策略** | opaque/cutout 先写深度确立归属，transparent 只 ZTest 后叠加；与 ID pass 的"全员 ZWrite On"互不干扰（因为分属两个 feature） |
| 4 | **按需分配，逐通道登记消费者** | 没有消费者的通道不分配（规划沿用 `LILTOON_CHANNEL_CONTRACT_V1.md` 的"无消费者不登记、不输出"） |
| 5 | **`custom0~3` 不复存在** | 四个匿名通道由 **Cryptomatte 选择层**取代（CB 规划 §5.11）——SB 不接这锅 |
| 10 | **SB 也做身份与 Cryptomatte（`scene.*`），与 CB 平行** | 场景物体因此也有 ID 遮罩，且**与角色彻底分开**：两套注册表、两套 ID 空间、两个组件，**两边数量各自独立扩展**（角色受 256×256 槽位 / 4096 部件行约束，场景受它自己的表约束，互不挤占） |
| 11 | **ID 归属互斥**：一个 renderer 只能进 CB 的部件表**或** SB 的对象表 | 否则同一个物体有两个身份 = 又一个"同义量两个来源"。两边都登记时要报出来（复用 CB 已有的冲突机制：裁决顺序固定 + Inspector 逐条列出）。**这条只约束身份**：表面数据是**全场景**的，SB 的表面 pass 画全场景（含角色），CB 的身份 pass 只画角色 |
| 12 | **身份与覆盖率的机制共用实现，不各写一套** | MSAA 目标、关键词分支、逐样本投票、resolve、Cryptomatte 成对布局——这些在 CB 已经写过一遍。SB 复用同一套代码（各自持有注册表与 ID 空间），否则同一个东西两份实现，早晚跑偏。**对代码结构的要求：把"ID + 覆盖率 + Cryptomatte"抽成共用内核**，CB / SB 各自挂一份注册表 |
| 13 | **SB 是"两趟 pass、两种采样"，不是一个 pass 塞两种数据** | 表面数值 = **单采样**（材质逐像素写、两段式深度）；身份/覆盖率 = **自建 MSAA + 逐样本投票**（与 CB 同机制）。两者分开两趟画、门控与生命周期各自独立——这样 SB 才不会在自己内部重演"一个 pass 塞多种语义" |

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

### 2.5 ⚠ 未来 PBR：本次 review 的主要结论

**SB 不只是"MetadataBuffer 的剩余部分"，它是以后 PBR 的载体。** 那布局就不能照 lilToon 现在这几个量来定，得按"一个 PBR 表面到底需要什么"来定，并且**留出增长位**。

**先看业界把 PBR 表面数据放哪儿、放多少**

- **HDRP**（官方 17.0.4）："HDRP renders the **Material properties** of every GameObject visible on screen into a **GBuffer**"，并且它自己承认代价："**HDRP compresses Material properties, such as normals or tangents, in the GBuffer. This results in compression artifacts**"（即：压缩是常规做法，但要知道它会有 artifact）。见 [HDRP: Forward and Deferred rendering](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Forward-And-Deferred-Rendering.html)。
- **UE**（官方，移动端延迟）：几何 pass "handles **BaseColor, Metallic, and Roughness** parameters and stores them in a temporary buffer, usually called **GBuffer**"；硬上限写得很直白——"**16 bytes or 128-bit per-pixel in GBuffer**"、"只能 4 张 input attachment"、"光照阶段只能取 3 张颜色 + 1 张深度"，并且"**Deferred rendering can not support MSAA** due to the amount of space it would need in GBuffer"；法线用 octahedron 编码省地方。见 [UE: Mobile Deferred Shading Mode](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-the-mobile-deferred-shading-mode-in-unreal-engine)。

**三条必须现在就定的结论**

**① 法线：分家（已定论）**

结论先写：**材质里会吃法线贴图，所以后续 feature 尽量吃带法线贴图的着色法线；几何法线与着色法线是平行的两条，不互为来源。**

- **GB 保持几何法线**——它回答"几何在哪、朝向如何"，服务于 SSAO/SSGI 的遮挡、shadow bias、物理遮挡、描边。HDRP 官方原话还给了它一个理由："Forward 用几何法线（vertex normal）做 shadow bias，所以伪影更少；Deferred 用 pixel normal，所以更多"。
- **SB 新增 `_HoSurfaceBufferNormal`（着色法线，含法线贴图）**——它回答"表面是什么样"，服务于 PBR 光照、SSR 反射方向、以及**后续所有要吃法线贴图的 feature**。
- **为什么不能互换**：AO 不该被法线贴图的微观凹凸驱动（那是表面细节，不是几何遮蔽）；反过来把几何法线喂给 PBR 光照，就等于丢掉法线贴图——材质白做了。
- **待办**：核对 lilToon 侧 `fragGeometryBuffer` 实际写的是哪一种（本仓库两处证据都指向几何法线）；**`Documentation~/GeometryBuffer.md` 要把"几何法线"写明**（现在"世界法线编码"/"基础网格位置的世界法线"太容易被当成着色法线）。

**② SB 不是"第二个杂物间"：效果提示与分类不进 SB**

PBR 表面数值（"表面**是**什么"）与效果提示（"效果**该**怎么用它"）是两件事。后者——`_SSAOStrength/Remap/Contrast/Mask`、`giStrength/giMask`、`gisexclude`、`sss strength/mask/tint`、`materialClass`/profile——在管线草案 **§6.3** 里被明确定义为"**材质轻量参数（用户可调）**"，且它们今天是**forward 阶段的材质状态**或**各自登记的通道**（`aointent` / `gisexclude` 已经是独立通道）。

> **规则**：SB 只放"表面**是**什么"；"效果**该**怎么用它"走 §6.3 的材质轻量参数或它自己登记的通道。加了这一条，SB 才不会长成 MetadataBuffer 那样。

**③ 位预算要写死（UE 那条 128-bit/px 是很好的清醒剂）**

UE 把**整个 G-buffer** 压在 16 B/px。我们现在：CB 12 B/px（+4 深度）+ SB 全开 20 B/px —— **两者相加已经超过别人整个 G-buffer 的预算**。所以：

- SB 的定位写清楚：它是**补充性表面 buffer**（给屏幕效果与 AOV 用），**不是**一个完整的延迟 G-buffer；
- 真要走 PBR 延迟路线时，**CB + SB 要一起重新算预算**，而不是在 SB 上继续加图；
- 增长规则：**每加一张图必须指回某个系统的脚印（§6.4）并登记进契约**，且优先"按需分配"而不是"常开"。

---

## 3. 现状的债（SB 要顺手还掉的）

1. **`surfaceData` 用 16F 装 4 个 0~1 归一量**：thickness / curvature / material class / transmittance 全是小范围标量，16F 每像素 8 B —— 8 bit 就够，**一张图省一半带宽**。
2. **`reflectionMaterial` 同病**：roughness / metallic / reflectance / PLR strength 也是 0~1（PLR strength 可 >1，见 §7 未定项）。
3. **`custom0~3` 是匿名通道**：契约里连登记都没有（只在备注里被点过一次名），消费端只能写 `Custom2 > 0.5` 这种没信息量的条件 —— 由 CB 的 Cryptomatte 选择层取代。
4. **`surfaceColor.a` 兼任覆盖率**：同一个 byte 既当颜色又当覆盖率，四个消费端各自解释（MetadataBuffer 的 `mBufferDepthWriteStateBlock` 一改就集体变味）。迁移后 **SB 只管颜色，覆盖率只有 CB 一个来源**。
5. **两张深度附件**：`depth` 纯附件（连全局 ID 都没有）、`mBufferDepth` 只有两个 debug 视图读 —— SB 只留一张自用附件，不发布。

---

## 4. 设计

### 4.1 通道布局（草案，按"未来 PBR"重新排过）

**核心三张**（PBR 的最小集合 + lilToon 现有消费者的需要）

| 通道 | 格式 | 内容 | 分配 | 服务于 |
| --- | --- | --- | --- | --- |
| `_HoSurfaceBufferNormal` | RGBA8 | **r/g = 着色法线的 octahedral 编码**（含法线贴图；业界就用这种压缩，HDRP 官方也承认"compresses normals… results in compression artifacts"）、b = 备用（将来的切线/各向异性方向）、a = 备用 | 按需 | PBR 光照 / SSR 反射方向 |
| `_HoSurfaceBufferColor` | RGBA16F | RGB = 线性 HDR 表面色（albedo，不钳制）；**A = 保留**（覆盖率只有 CB 一个来源） | 按需 | PBR / 角色特化脸色扩散 / AOV `diffuse_albedo` |
| `_HoSurfaceBufferMaterial` | RGBA8 | r = roughness、g = metallic、b = thickness、a = 备用 | 按需 | PBR / SSS / PLR |
| `_HoSurfaceBufferReflection` | RGBA8 | r = reflectance（F0 的标量近似）、g = PLR strength、b/a 备用 | 按需 | PBR / PLR / SSR / Probe |

**PBR 还想要、但现在没有家的（本次 review 新增到议题里）**

| 需要什么 | 今天在哪 | SB 的处置 |
| --- | --- | --- |
| **着色法线（含法线贴图）** | GB 发布的是**几何法线**（§2.5 ① 的两处证据） | **SB 新增 `_HoSurfaceBufferNormal`**：两者是两个不同的量（几何遮蔽 vs 表面微观朝向），不互为来源；GB 保持不动，但它的文档要写明"几何法线" |
| **emissive（HDR 强度）** | 契约里已登记 `emission`（◻ 未实现，AOV 用） | **SB 是它的家**：新增 `_HoSurfaceBufferEmission`（RGBA16F，按需）——它就是"表面自己发光" |
| **AO（环境光遮蔽）** | 屏幕空间的 GTAO 产物 + 材质的 `_SSAOMask` 意图 | **不给 SB**：AO 是屏幕空间产物，材质侧只是意图（§2.5 ②） |
| **clearcoat / sheen / anisotropy / transmission-IOR** | 今天完全没有 | **预留**：`_HoSurfaceBufferLobes`（RGBA8，按需）——四个 0~1 的高级叶瓣参数。**开了才分配**，且必须指回某个系统的脚印 |
| **分类（materialClass / profile）** | `surfaceData.b` + `sssprofile`（R8） | **倾向进表不进图**（§7 第 2 项）：分类是"这是什么"，按 CB 立的规矩归 palette |

**预算线（写死，防止以后无限加图）**

```text
核心 4 张 = 4（法线）+ 8 + 4 + 4 = 20 B/px（按需）
+ Emission 8 / + Lobes 4 = 全开 32 B/px
定位：补充性表面 buffer（给屏幕效果与 AOV），不是完整延迟 G-buffer
        —— 真要上 PBR 延迟路线，CB + SB 一起重算预算（§2.5 ③）
```

### 4.9 命名与三者定位（按管线草案 §10 的表格式给）

| Buffer | 语义定位 | 内容 | 主服务对象 | 消费方 |
| --- | --- | --- | --- | --- |
| `CharacterBuffer`（已定名） | 对象/角色语义（forward-style，随材质 pass 写） | `Id0`/`Id1`/`Coverage`（4 层 `(ID, 覆盖率)`）+ Cryptomatte 选择 | **角色/对象身份** | 角色特化、ScreenProcess 规则、AOV、SSS/PLR 的门控 |
| **`SurfaceBuffer`（本规划）** | **表面数值 + 场景侧身份**（material-style，随材质 pass 写） | 线性表面色 / 着色法线 / roughness / metallic / thickness / reflectance / PLR / class / curvature / transmittance（**+ `scene.*` 的 ID 与 Cryptomatte，决策 10-13**） | **"表面是什么样"**；**场景对象的身份** | SSS、PLR/SSR/Probe、角色特化脸色扩散、ScreenProcess 规则、AOV `diffuse_albedo`、场景抠像 |
| `ScreenGeometryBuffer`（已定名，现名 GeometryBuffer） | 屏幕可见几何（deferred-style） | **几何法线** / depth / 几何覆盖率 / 描边 / sky | **全屏几何** | AO/GI/SSS/反射/角色特化/AOV |

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

身份/覆盖率那半（决策 10-13）按同一模板另登记三条，命名与 CB 并列（`character.*` ↔ `scene.*`）：

```text
通道名: scene.idcoverage
生产端: SB 的 ID pass（自建 MSAA + resolve，与 CB 共用内核）
消费端: 场景抠像、AOV id_object/id_group 的场景侧、ScreenProcess 的按场景对象规则
编码:   Id0/Id1 R8G8B8A8（4 层 (ID, 覆盖率)）+ Coverage R8G8B8A8
生命周期: 帧持久（按需分配）
AOV:    id_object / id_group（场景侧）
debug:  scene.id0..3 / scene.coverage.*
冻结:   否
```

```text
通道名: scene.selection
生产端: 材质 → SB 的 ID pass（Cryptomatte 成对布局，与 CB 同构）
消费端: 后期按名字点选、AOV matte_*
编码:   R8G8B8A8（R=id0,G=cov0,B=id1,A=cov1）
生命周期: 帧持久（按需分配）
AOV:    matte_*（场景侧）
debug:  scene.selection
冻结:   否
```

```text
通道名: scene.palette
生产端: Extensions 侧（SB 的组件 → 注册表 → StructuredBuffer）
消费端: 材质侧按 RSUV 索引查名字/标签；debug 与 AOV manifest
编码:   StructuredBuffer（对象表；行宽与容量见 §7）
生命周期: 帧持久
AOV:    不导出（manifest 走 EXR metadata）
debug:  不适用
冻结:   否
```

### 4.10 场景侧身份与 Cryptomatte（SB 的第二半，决策 10-13）

**为什么要做**：现在只有角色能出 ID 遮罩。场景物体（道具、环境、特效件）要单独抠出来，要么进角色表（污染角色的 ID 空间、还会被"同角色"判断误伤），要么没得用。SB 自己做一套之后：**场景与角色两套身份平行、互不挤占、各自扩展**。

| | CB（角色） | SB（场景） |
| --- | --- | --- |
| 身份结构 | **层级化**：角色 → 部件（16 bit = 角色 8 + 槽位 8） | **扁平**：对象表（宽度待定，见 §7） |
| 注册表 | `HoCharacterBufferGroup`（部件表 + 角色表 + 选择表） | **自己的组件**（决策 10）：对象表 + 选择表 |
| 图 | `_HoCharacterBufferId0/Id1/Coverage/Selection` | `_HoSurfaceBufferId0/Id1/Coverage/Selection`（同构，复用同一套 shader 内核） |
| 契约名 | `character.*` | **`scene.*`**（并列易读） |
| 消费者 | 角色特化、同角色判断、眼透/发影 | 场景抠像、AOV、ScreenProcess 的"按场景对象"规则 |

**规则（写死）**

1. **ID 归属互斥**（决策 11）：一个 renderer 只能进 CB 的部件表或 SB 的对象表之一；两边都登记要报出来（复用 CB 的冲突机制）。
2. **表面数据不受互斥约束**：SB 的表面 pass 画全场景（角色也有表面），CB 的身份 pass 只画角色。
3. **机制共用实现**（决策 12）：MSAA 目标 / 关键词分支 / 逐样本投票 / resolve / Cryptomatte 成对布局抽成**共用内核**，CB 与 SB 各自挂一份注册表与 ID 空间。**不允许两份实现**。
4. **两趟 pass 分开**（决策 13）：表面（单采样、两段式深度）与身份/覆盖率（自建 MSAA、逐样本投票）各自独立分配与发布；没有身份需求时可以只跑表面那趟。
5. **覆盖率同样只来自自建 MSAA**：场景物体的遮罩也要抗锯齿——这正是当初角色那边踩过的坑（硬边掩码），不能因为"场景物体比较方"就退回单采样。

### 4.8 成本对照（1920×1080）

> 下表是**表面那半**的成本；身份/覆盖率那半与 CB 同构（3 张 RGBA8 = 12 B/px + MSAA 瞬态 + 按需的选择图），见 CB 规划 §5.9⑥。

| | 现状（MetadataBuffer 的相关部分） | SB 表面 |
| --- | --- | --- |
| 常驻 | `surfaceData` 8 + `custom0` 8 + `reflectionMaterial` 8 + `surfaceColor` 8 = **32 B/px ≈ 63 MiB** | 按需：`Normal` 4 + `Color` 8 + `Material` 4 + `Reflection` 4 = **全开 20 B/px ≈ 39 MiB**（+ `Emission` 8 / `Lobes` 4 若开） |
| 常见组合 | 4 张全在 | `Color` + `Material` = **12 B/px ≈ 24 MiB** |
| 深度附件 | 2 张（`depth` + `mBufferDepth`） | **1 张自用、不发布** |

即：**表面那半开全也比现状省**；而且"只开需要的"在 SB 里是结构保证（按需分配 + 无消费者不登记）。**但加上身份那半之后总量要合起来算**（§2.5 ③ 的预算线）。

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
| **S0（本文件）** | 调查 + 布局草案 + 边界 + 与已冻结文档的修订清单（§0.1）+ **未来 PBR 的 review（§2.5）** | §7 的 7 项拍定（第 0 项尤其） |
| **S1（文档先行）** | ① 修订 `LILTOON_FORMAL_PIPELINE_DRAFT.md` §3.1/§3.2（L1 拆"身份/覆盖率"与"表面数值"，帧序 [3] 拆两条），并按 §7 第 6 项补一条"buffer 边界"通则；② 提**契约 v2**（§0.1 的两条修订 + §4.7 的登记行）；③ 把本文 §4.2 的命名分析并入 §10 的批量改名计划；④ **把 `GeometryBuffer.md` 的"几何法线"写明**（§7 第 0 项） | 两份定稿文档里能查到 SB 与新通则；契约 §5 变更记录有 v2 一行；GB 文档明确写"几何法线" |
| **S2** | **表面那半**：`HoSurfaceBuffer*` feature，通道**按系统逐个开**（先 `Color` + `Material` 给 SSS，再 `Reflection` 给反射，`Normal` 跟 PBR/SSR 一起），自用深度 + 两段式策略，RG/兼容两条路径，调试视图 + 编辑器抽屉（与 CB 同款式） | 开着的通道在 debug 里能看到；没开的**不分配、不输出**；消费端登记与通道一一对应 |
| **S2.5** | **把 CB 的"ID + 覆盖率 + Cryptomatte"抽成共用内核**（决策 12），CB 改为使用它 | CB 行为不变（回归验证：相机 MSAA 关时覆盖率仍是 4x）；内核里没有 CB 专有的东西 |
| **S3** | **身份那半**：SB 的对象表 + 自己的组件 + 复用内核的 ID/覆盖率 pass + `scene.selection`；**ID 归属互斥的冲突检测**（决策 11）与告警 | 场景物体能出抗锯齿的 ID 遮罩；同一个 renderer 同时进两边时能报出来；两侧数量各自增长互不挤占 |
| **S3** | lilToon 侧 `HoSurfaceBuffer` pass（跨仓）：按材质开关写 MRT；`HoMetadataBufferSurfaceColor` 那一趟退役；**同步登记"材质接口脚印"**（§6.4 的三行：SSS / 透射折射 / 反射） | 表面色与材质值与原通道逐像素一致（可 A/B 对比）；脚印在契约里能查到 |
| **S4** | 消费端迁移：SSS / PLR / 角色特化脸色扩散 / ScreenProcess 规则 / AOV `diffuse_albedo` | 行为不变或更好；`surfaceColor.a` 不再是覆盖率（v2 生效） |
| **S5** | 与 CB 一起进 CB 规划的 P4：删 MetadataBuffer 的 surface 族通道与写入端 | 全仓库无 `_HoMetadataBuffer` surface 族引用 |

> 依赖关系（§11 推进顺序是 `Ho-GTAO → Ho-SSGI → 其余系统`）：**S2 可以在 GTAO/SSGI 之后并行做**，但 S3 的材质脚印要跟着 SSS / 反射系统各自的落地节奏走——否则就是"先裁材质暴露面"，正好违反 §6.1 的"不能现在就裁剪"。

---

## 7. 未定项（拍完才能开工）

0. **【法线分家·已定论】**（§2.5 ①）GB 保**几何法线**、SB 加**着色法线**，两者平行；后续 feature 尽量吃着色法线。**剩下两件小事**：核对 lilToon 侧 `fragGeometryBuffer` 实际写的是哪一种（本仓库两处证据都指向几何法线）；**`Documentation~/GeometryBuffer.md` §3.1/§4.1 把"几何法线"写明**。
0b. **【新增·场景侧身份】对象表的宽度与容量**（决策 10）：扁平对象表用 16 bit（65536 个对象，够用且与 CB 的部件 ID 同宽）还是 8 bit + 两级表？容量上限定多少（CB 那边是 4096 部件行 / 256 选择，场景通常更多）？以及 **SB 的组件叫什么**（`HoSurfaceBufferGroup`？还是与 CB 平行的 `HoSceneBufferGroup`）。
1. **契约 v2 的两条修订，现在一起提还是往后放？**（§0.1）其中 `SurfaceColor` 的 `A=coverage` 是 v1 明写的冻结项，改它等于改"覆盖率有几个来源"——**这是本次整改的核心，我建议一起提**；`ReflectionMaterial` 的拆分可以晚一步（它只关乎带宽与命名，不关乎正确性）。
2. **`materialClass` / curvature / transmittance 放图还是放表**：本文草案放 `Classification`（材质侧逐像素写）。但 `materialClass` 严格说是**分类不是数值**——按 CB 刚立的规矩（身份与分类归表、数值归图）它更该进 palette，那就要回答"谁写 palette"（我倾向材质侧写 + 跨仓属性名协议，与 CB 的选择槽协议一起冻结）。
3. **PLR strength 的范围**：今天 `reflectionMaterial.a` 是 16F 且不限幅。约定 ≤1 就能塞进 8 bit（`Reflection.g`）；否则要单独一个 16F 通道或"归一 + 系数"（系数进 palette 或材质常量）。
4. **SSS profile 是否并进来**：今天 `sssprofile` 是独立 R8 通道（材质/profile 写、SSS 与 AOV 读）。并进来能少一张图，但要先确认它算不算"分类"（若算，按第 2 项一起进表）。
5. **管线草案的修订措辞**（§3.1 的 L1 两行怎么分、§3.2 帧序 [3] 是拆成 [3a]/[3b] 还是插一条 [3.5]）：我倾向**现在就写进那份定稿文档并标注"待实现后生效"**——理由是那份文档是别人读的入口，SB 只写在本文里等于没登记。
6. **【新增】要不要给管线草案补一条"buffer 边界"通则**：那份文档之所以完全没预料到还要多一个 buffer，是因为它**没注意 MetadataBuffer 本身就是杂糅的**（身份 + 覆盖率 + 表面数值）。建议补一条写死进契约的通则：
   > **buffer 的边界由它"回答哪个问题"定义，不由"它先存在"定义；新增 buffer 必须写明它*不*回答什么。**
   顺带把 §3.3 旧→新映射与 §10 命名分析两张表各加一行 SB，§6.4 的脚印表加一列"**载体**"（哪个 buffer 承载这个系统的材质数据）——否则下一个人还是会在同一处踩空。

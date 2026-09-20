# Ho-ObjectBuffer（OB）规划

逐物体 buffer：回答"**这是谁、占多少、我要抠哪一块**"。**几何在 GB，表面数值在 SB，合成在 AC。**

> **状态：身份池、Facing、同 SemanticId 的 object/surface 合成、Selection lane 与 4/8/16 lane batching 已冻结，可按 §0.5 开工。**
> 2026-09-20 流水线复核与 P0 勘误已合并到本文 §0；§0 与后文旧冻结条款冲突时，以 §0 为准。
> **"Cryptomatte" 在本 feature 一律不用**：合规导出档位（`crypto_*`、float 位重解释 + manifest）**不是 OB 的事**，归 AC 或以后的独立 feature。

---

## 0. 流水线复核与 P0 勘误（2026-09-20）

### 0.1 这次重构的定位

这次不是在 MetadataBuffer（MB）旁边增加几个业务 feature，而是底层协议换代：

1. 把 MB 中混在一起的**身份**、**bit 语义**和**表面数值**拆开。
2. 需要抗锯齿的离散语义先写成 **ID + coverage**，再通过声明表解压；不再把 bit 通道当普通 UNORM 值过滤。
3. OB 建立管线唯一的身份事实，AC 建立统一的解压/查询入口，SB 承接逐像素表面数值和材质选区。
4. SSS、PLR、角色特化、ScreenProcess、AOV 等后续 feature 不再自己解码 MB bit 布局，统一经 AC 使用这套协议。

验收重点是：**ID 稳定写入、coverage 与 ID 对齐、语义只在一处解码、边缘不再出现 bit 插值污染**。

### 0.2 已确认的主干

- 共享 16-bit 身份空间：`groupId:8 | slotId:8`，不以高字节分“角色/场景”。
- 4 层 ranked 身份池：每层一个 `(ID, coverage)`，coverage 不重新归一化，背景由残差表示。
- 通过自建 MSAA 逐 sample 写 ID，resolve 时按整数 ID 分组统计 coverage，不对 ID/bit 做硬件平均。
- 物体位/分类/标记留在两级表中，消费端以 `sum(cov_i * predicate(entry_i))` 解压抗锯齿遮罩。
- `faceBone` 朝向保留为逐像素 Facing RT，以支持多角色同屏的屏幕空间查询。
- OB 只生产底层事实；后续消费者经 AC 的统一 API 获取解压后的遮罩/属性。

### 0.3 已闭合的 P0 与实现约束

#### 0.3.1 Facing 必须和身份 resolve 绑定

- ID pass 的每个 MSAA sample 同时写 `sampleId + sampleFacing`。
- 先完成 ID 数票/排序，再从 **ranked layer 0 获胜 ID** 所属 sample 取 Facing。同 ID 有多个 sample 时，以线性眼深最近、sample index 为次级平票得到确定结果。
- `_HoObjectBufferFacingTexture` **只与 ranked layer 0 身份对齐**。查询 layer 1..3 时不得误用它。
- 多角色同屏由每像素的获胜 ID 自然支持；**禁止跨 ID 平均方向**。

#### 0.3.2 `K=N=4 无损` 必须收紧

现有 `GetSupportedSampleCount` 会把 4x 降到 2x/1x。只能承诺：“请求 N=4，K=4 不丢当帧实际 N≤4 的**每 sample 唯一前表面 ID**”。1x 时 coverage 只有 0/1；必须发布 `requestedSampleCount` 与 `actualSampleCount`，降级可见。

#### 0.3.3 coverage 不是半透明颜色贡献

coverage 的准确定义是：通过该材质 `HoObjectBuffer` pass 的 clip/cull/depth 规则后，属于某 ID 的 MSAA sample 占比。它不是 alpha blend 透射贡献，也不是 Weighted OIT accumulation/revealage。普通透明要冻结为“默认不参与”或“显式 opt-in 前表面近似”之一，不得宣称为完整画面贡献。

#### 0.3.4 Reverse-Z 平票必须修正

`HoCharacterBufferResolve.shader` 当前对 raw depth 取 `min`，并把更小值当成更近；reversed-Z 下方向相反。R1 必须改为比较 linear eye depth，或显式分支 `UNITY_REVERSED_Z`。同一规则同时用于 ID 平票和 Facing 获胜 sample。

#### 0.3.5 表容量与冲突

- group table = 256 行，0 保留；每 group 最多 256 slot。
- 稠密 entry/palette 表当前预算上限 = 4096 行，0 保留。
- 独立 Selection ID 表才是 8-bit / 最多 255 个有效名字。
- 当前 CB 未阻止两个 Group 使用同一 ID。OB 必须把 group ID 重复定为硬错误，冲突组无效，不能后写覆盖。

#### 0.3.6 Selection 冻结为“固定 transport lane + 权威 SemanticId”

三种编号严格分型：

| 类型 | 宽度 | 含义 |
| --- | --- | --- |
| `IdentityId` | 16 bit | `groupId:8 | slotId:8`，回答“这是谁” |
| `SemanticId` | 8 bit | 1..255 的具名语义，0 = 未写/无 writer |
| `LaneIndex` | 0..15 | 屏幕传输位，不是 ID；每个 surface-writable SemanticId 在 schema 中独占一个稳定 lane |

- 共享 `HoSemanticSchema` 声明 `name / SemanticId / LaneIndex / sourceMode / debugColor`。AC 将它编译成 runtime catalog；OB/SB 都只读该声明，不自造 ID。
- schema 可有最多 255 个具名 SemanticId，但同时 surface-writable 的只能占用 16 个 lane。其他 object predicate 仍可直接经身份池查询，不占 lane。
- OB entry/group 表每行存一个 16-bit `objectSemanticLaneMask`；AC 按每个 MSAA sample 的 IdentityId 查表，得到 object 语义值 `o ∈ {0,1}`。object 语义不再单独画 Selection RT。
- SB semantic sample 每 lane 写 `(SemanticId, value)`。`SemanticId=0` = 该 sample 未写；`SemanticId=声明 ID, value=0` = **明确写 0**。因为 AC 在 resolve 前逐 sample 合成，不需要额外 writer-validity RT。
- SB semantic pass 另写 `SurfaceSemanticOwnerMS`（16-bit IdentityId）。AC 只在 `surfaceOwner == objectSampleIdentity` 时接受 SB 语义；不匹配的 sample 丢弃并记入 alignment debug。

每个 semantic 在 sample 级选一个 `sourceMode`：

| mode | sample 合成 |
| --- | --- |
| `ObjectOnly` | `o` |
| `SurfaceOnly` | `written ? s : 0` |
| `Union` | `max(o, written ? s : 0)` |
| `SurfaceOverride` | `written ? s : o` |
| `Intersection` | `written ? o * s : 0` |

`s = saturate(surfaceValue)`，`written = surfaceId == declaredSemanticId`。AC 在所有 sample 合成完成后只 resolve 一次：`coverage = Σ sampleValue / actualN`。输出每 lane 仍是 `(SemanticId, coverage)`；已分配 lane 的 ID 始终是 schema ID，即使 coverage=0 也不丢失语义。

这允许同一 SemanticId 同时有 object 来源和 surface 来源。例如整个眼部 Renderer 在 OB 中标记“眼部”，SB 在同一 ID/lane 上用贴图写眼白=1、虹膜=0；`SurfaceOverride` 使眼白细分在 MSAA sample 级替换 object 粗分。

#### 0.3.7 4/8/16 lane 的 MRT batching

- OB identity pass 写 `IdentityMS + FacingMS`（2 MRT + depth）；identity resolve 写 `Id0 + Id1 + Coverage + Facing`（4 MRT）。
- SB 数值 pass 不与 MSAA semantic pass 混合附件；它写五张数值 RT + 一张 internal `SurfaceOwner`（6 MRT）。
- SB semantic pass：4 lane = `OwnerMS + 2 lane RT`（3 MRT）；8 lane = `OwnerMS + 4 lane RT`（5 MRT）；16 lane = 两个 8-lane batch，每 batch 5 MRT，重画几何两次。
- AC semantic resolve：4/8/16 lane 分别写 2/4/8 张 RGBA8。当 `SystemInfo.supportedRenderTargetCount` 低于当前输出数时，按 lane batch 拆成多个 fullscreen pass，不降低语义数。
- 默认 4 lane 是快路径；16 lane 是明确的高成本档，UI 必须显示额外几何 pass 与显存成本。

#### 0.3.8 R1 是跨两仓库的协议迁移

Extensions 仓库已有 CharacterBuffer 的 C#/resolve 骨架，但 `D:\Unity_Fork\lilToon` 当前仍只有 `HoMetadataBuffer` / `HoMetadataBufferSurfaceColor` 材质 pass。R1 必须包含 Extensions 迁移、lilToon `HoObjectBuffer` 的 ID/Facing 写入、SB SurfaceSemantic 写入、cutout/dissolve/cull 对齐、RG/兼容路径和 MB/OB A/B debug。

### 0.4 与当前 HoUrp 17.3 流水线的契约

- HoUrp fork 的 pass 排序键是 `(renderPassEvent, renderPassEnqueueOrder)`；同事件下 Renderer Feature 列表顺序就是记录顺序。
- GB / OB / SB producer 互不读；AC 若在同事件记录，必须排在 OB/SB 之后。
- producer 用 `SetRenderAttachment` 声明写入并把句柄写入 `ContextItem`；consumer 检查 `IsValid/HasRequiredTextures` 并对实际采样图调 `UseTexture(Read)`。`SetGlobalTextureAfterPass` 不代替 RenderGraph 读依赖。
- AC SemanticResolve/AttributeComposite 默认在 `BeforeRenderingOpaques`，排在 OB/SB 后、GTAO 前；它们不读 camera color。
- URP 的常规 transparent 位于 `BeforeRenderingTransparents` 之后；pre-opaque AC 与 `AfterRenderingOpaques` 的 SSGI 都没有看到常规透明颜色结果，因此 OB/SB 透明语义仍以专用 capture pass 契约为准。

### 0.5 开工顺序与最小验收

| 阶段 | 内容 |
| --- | --- |
| R1a | 先修现有 CB 骨架：Reverse-Z、actual N 诊断、group ID 冲突、Debug Registry、资源 valid/fallback |
| R1b | 落实 `HoSemanticSchema`、typed ID、SB owner 校验、五种 sourceMode 与 4/8/16 lane batching |
| R1c | Extensions 改名为 ObjectBuffer，同时修正精确 packing 和 Facing-layer0 契约 |
| R1d | lilToon 新增 `HoObjectBuffer` 材质 pass，完成 opaque/cutout 逐 sample A/B 验证 |
| R2 | 接通 Facing MSAA 写入/同 ID resolve，完成多角色同屏、遮挡和边缘验证 |
| R3 | AC 提供统一 ID 解压/遮罩 API，先迁一个角色特化消费者做端到端基准 |

最小验收必须覆盖：相机 AA 关闭下的自建 4x ID 边缘、4x→2x→1x 降级、normal/reversed-Z 平票、多角色相反朝向交界、cutout/dissolve 对齐、多 ID 加权解压、RG 错序 fallback、group/Renderer/entry/Selection 冲突诊断。

---

## 1. ID 空间（冻结）

**一个共享 ID 空间，不区分角色与场景。** 角色的部件和场景物体混在同一个空间、同一套表里；"这是什么区域、有什么属性"由名字表的**类型 / 标记 / 位**回答，不靠 ID 的高字节分域。

- **ID = 组 8 bit + 槽位 8 bit**。高字节只用来做"**同一组**"的廉价比较（发影 / 眼透要用），不再意味着"这是角色"。
- **"组"是泛指的**：一个角色、一组道具、一片场景区域都行；组的边界由挂组件的人定，组件文档要写明这一点。

### 1.1 ID 类型清单（冻结）

下表就是**今天 MetadataBuffer 的全部 ID 语义**，一条不丢；UI 上填的时候看到的就是这些名字。

| 类型 | 今天对应 | UI 名 | 谁写 |
| --- | --- | --- | --- |
| **组**（Group） | `maskId.y` = `characterId` | 「组 ID (GroupId)」 | OB（组件，逐物体） |
| **部件**（Part） | `maskId.z` = `partId` | 「部件 ID (PartId)」 | OB（组件，逐物体） |
| **标记**（Flags） | `maskId.w` = `flags` | 「标记 (Flags)」 | OB（组件，逐物体） |
| **物体位 0~7**（ObjectFlag） | `objectCustom0~7` | 「全角色」「脸」「前发」「眼睛」「眼透区域」「配件」「人体」「预留 7」 | OB（组件，逐物体） |
| **材质位 0~3**（MaterialFlag） | 材质 custom0~3 | Settings 里可自定义名字 | **SB**（材质，逐像素）——OB/runtime catalog 预先声明默认 4 个 Selection ID；lane 映射待 §0.3.6 冻结 |

- **覆盖率**（今天 `maskId.x` = `_HoMetadataBufferMaskWeight`）不是类型，它是每个 ID 对里的另一半。
- **朝向**（今天 `faceBone` + 三轴）不是 ID，是**逐物体辅助量**（§2）。
- **遮罩 = 按条目属性加权求和**：`matte_X = Σ_i cov_i · [条目_i 命中 X]`，**不是"取某一层"**。

**角色特化固定只吃这几条**：组 + 物体位{全角色, 脸, 前发, 眼睛, 眼透区域, 配件, 人体} + 覆盖率；**预留 7 不是承诺**，可被场景拿走用。角色特化之后改吃 **AC 合成的结果**，AC 依旧**按组**来吃、来合成。

### 1.2 名字表（冻结）

- **两级**：**组表**（组 ID → 组名 / 朝向来源）→ **条目表**（ID → 名字 / 部件 / 标记 / 物体位）。
- **身份表与 Selection 表分开计数**：256 行 group table（0 保留），当前稠密 entry/palette 预算上限 4096 行（0 保留）；独立 8-bit Selection ID 表才是最多 255 个有效名字。
- **第 0 行 = 未知**；越界查表**回落到未知行，不钳制**。
- **它是共享的声明数据**（作者在组件 / Settings 里填的），**不是 OB 的输出**——OB 与 SB 都按它解析名字，所以 SB 读它**不违反"producer 互不读"**；两个 buffer 各自的 ID 存储仍互不可见。
- GPU 侧是 StructuredBuffer（`_HoObjectBufferGroups` / `_HoObjectBufferEntries`）；**RSUV 不序列化 ⇒ 每次重建都要重写**。
- 平台不支持 StructuredBuffer（shader level < 4.5）⇒ **整条不跑并告警，不静默降级**。

### 1.3 身份池与 Selection 池

**① 身份池（ranked，OB 独占，常开）**：4 层 `(组, 槽位)` + 4 层覆盖率。**按覆盖率降序排，槽号不承载语义**；一个像素可以同时属于多个物体（相机 AA 关掉也不丢覆盖率）。

**② Selection 池（固定 transport lane，AC 统一产出）**：`HoSemanticSchema` 把最多 16 个 surface-writable SemanticId 绑定到稳定 LaneIndex。OB 不画 object Selection RT；AC 从每 sample IdentityId + entry/group `objectSemanticLaneMask` 解压 object 语义，再与 SB sample `(SemanticId,value)` 合成。

**AC 合成**：身份 predicate 查询仍按 `Σ cov_i · predicate(entry_i)`；已分配 Selection lane 按 §0.3.6 在 sample 级执行 `ObjectOnly / SurfaceOnly / Union / SurfaceOverride / Intersection`，再统一 resolve 为 `(SemanticId,coverage)`。

**两个面的定位（冻结）**：

- **身份池 = 合规导出的运行时身份源**：成对 `(ID, coverage)` 并按 coverage 降序排层。AC runtime catalog 把名字解析成运行时 ID；导出层再生成 32-bit hash 与 Cryptomatte manifest。
- **Selection 池 = AC 统一解压后的运行时语义面**：同一 SemanticId 可由 object 粗分与 surface 贴图细分同时生产，以 schema sourceMode 在 sample 级合成。
- **角色特化不需要固定槽**：它吃的 `全角色 / 脸 / 前发 / 眼睛 / 眼透区域 / 配件 / 人体` 是**物体位**——身份池条目的属性，纯 ranked 语义就够。
- **AC 对任意身份 ID 都能按需生成遮罩**（身份池 + runtime catalog）；Selection 是独立 8-bit ID 空间，不与身份 ID 混用。

**身份池的空 = coverage 0**：背景不占层。SB semantic sample 中 `SemanticId=0` 是未写，`SemanticId!=0,value=0` 是显式写 0；AC 合成后的固定 lane 即使 coverage=0 也保留 schema SemanticId。

**失败必须可见（不静默错位）**：声明的槽数与已分配的 RT 张数不一致 → 告警 + debug 标出；查表落到第 0 行（未声明 ID）→ 标出；身份池溢出（一像素 > 4 个物体）→ 标出；SB 写了未声明的槽 → 该值无效 + 诊断。

---

## 2. 每像素存储（冻结）

| 纹理 | 格式 | 内容 | 分配 |
| --- | --- | --- | --- |
| `_HoObjectBufferId0Texture` / `Id1Texture` | RGBA8 ×2 | **身份池**（ranked）：各 4 层，`Id0 = 组 8 bit`、`Id1 = 槽位 8 bit` | 常开 |
| `_HoObjectBufferCoverageTexture` | RGBA8 | **身份池覆盖率**：4 层，不归一化（残差 = 背景占比） | 常开 |
| `_HoACSelection0Texture` ... `_HoACSelection7Texture` | RGBA8 ×2/4/8 | AC 统一 resolve 后的固定 lane；每张 `R=id0,G=cov0,B=id1,A=cov1` | 按 schema 启用 4/8/16 lane；归 AC，**不是 OB 输出** |
| `_HoObjectBufferFacingTexture` | RGBA8 | 逐像素辅助量，`RG = octahedral(forward)`、`BA = octahedral(side)`；**与 ranked identity layer 0 的获胜 ID 同步 resolve，禁止跨角色平均**；消费端重建第三轴并正交化 | 按需（有物体提供 `faceBone` 才开） |
| 组表 / 条目表 | StructuredBuffer | §1.2 | 常开（小） |
| 内部 depth-stencil | 深度格式 | 两段式占用判定 + tie-break | **不发布** |

**两个池分型**：身份池是 ranked IdentityId+coverage；Selection 池是 AC 产生的 fixed transport lane + SemanticId+coverage。LaneIndex 不是 ID，只是 schema 稳定绑定的传输位。

**不变式（冻结）**：
- ID **点采样** + `round(v*255)` 还原；**不滤波、不平均**。
- **覆盖率线性，不归一化**（残差 = 背景占比）；**只能按 ID 匹配加权**；背景不占身份池的层。
- 身份池请求 `N=4`，但实际 N 会按平台协商为 4/2/1。`K=4` 只承诺不丢当帧实际 N≤4 个“每 sample 唯一前表面 ID”；1x 时 coverage 退化为 0/1，多层透明贡献不在该承诺内。

---

## 3. 名字（冻结）

| 类别 | 冻结名 |
| --- | --- |
| feature / 代码目录 | `HoObjectBufferRendererFeature`；`Runtime/ObjectBuffer/`（R1 从 `Runtime/CharacterBuffer/` 改名搬迁；CB 那批文件就是骨架，选择层完好） |
| 组件 | **`HoObjectBufferGroup`**（今天的 `HoMetadataBufferGroup`：组 ID / 部件 ID / 标记 / 物体位名单 / 朝向）、**`HoObjectBufferSubject`**（今天的 `HoMetadataBufferSubject`：逐物体覆盖） |
| Volume | **`HoObjectBufferVolume`**（**调试入口**；`VolumeComponentMenu("Post-processing/Ho-ObjectBuffer/逐物体通道")`） |
| 纹理 | `_HoObjectBufferId0Texture` / `_HoObjectBufferId1Texture` / `_HoObjectBufferCoverageTexture`（身份池）、`_HoObjectBufferFacingTexture`；统一 Selection 输出名归 AC（`_HoACSelection{0..7}Texture`） |
| 表 | `_HoObjectBufferGroups`、`_HoObjectBufferEntries` |
| 契约登记族 | **`object.*`**（`object.selection` / `object.facing` / `object.palette`）；CB 时代的 `character.*` 一律不用 |
| 禁用名 | `Cryptomatte` / `crypto_*` / `_HoCryptomatte*` / `HoCryptomatteGroup` —— **本 feature 一个都不用** |

**UI 名**：组 ID / 部件 ID / 标记用第 1.1 节的表；物体位 8 条沿用今天的名字（**去掉"角色"字样**，改成通用的「组」的说法）；材质位 0~3 的名字在 Settings 里可自定义。

---

## 4. 消费者（冻结）

| 消费者 | 从 OB / AC 拿什么 | 还需要别的吗 |
| --- | --- | --- |
| **角色特化**（眼透 / 发影 / 脸色扩散 / 主体与增强轮廓） | **只吃 AC**：组 + 物体位那 7 条 + 覆盖率 | GB（几何门控） |
| **SSS** | 遮罩（具名选择）+ 分类/profile | SB（thickness / curvature / 表面色）+ GB |
| **ScreenProcess** | **只吃 AC**：具名遮罩 + 覆盖率（原来那 20 个 source 的"通道 + 阈值"规则已作为未使用功能删除，今天只采样 MetadataBuffer 覆盖率） | 材质意图仍在材质侧 |
| **PLR** | 反射平面 / 参与物体的遮罩 | GB + SB |
| **AOV / 导出** | ID + 覆盖率 + runtime catalog | **不做合规导出档位**（独立导出 feature 构建 Cryptomatte manifest） |

**分工**：OB 产身份 sample 与 object semantic membership，SB 产 surface semantic sample，AC 按 schema sourceMode 逐 sample 合成并只 resolve 一次。消费者不自行解码 OB/SB packing，但 RenderGraph 仍经 AC 资源集声明实际物理纹理的 `UseTexture(Read)`。

---

## 5. 边界与准入

| feature | 回答什么 | 输出 |
| --- | --- | --- |
| GB | 几何在哪、朝向如何、几何覆盖多少 | 几何法线 / depth / 几何覆盖率 / 描边 / sky |
| **OB（本 feature）** | **这是谁、占多少、抠哪一块** | ID + 覆盖率 + 具名选择 + 朝向 |
| SB | 表面是什么样 | 表面色 / 着色法线 / roughness / metallic / thickness / … |

- **不发深度、不发几何/着色法线、不发表面数值**；**遮罩只有一个来源**。
- **准入判据**：这个量是「**逐物体 / 逐区域**」的吗？是 → 可以进；逐像素材质量 → SB；几何 → GB；效果调参意图 → 材质轻量参数；都不是 → 它该有自己的 feature。
- **允许两类**：① 身份（组/条目 ID、覆盖率、具名选择）；② 逐物体辅助量（朝向）。
- **类② 上限两张图**；超了先回答"为什么不能去 SB / 自己的 feature"。
- **登记制度**：每个通道写明生产端 / 消费端 / 编码 / 生命周期 / debug 视图；**没有消费者的不分配**。
- **不与别的语义打包**：几何 → GB，屏幕空间产物 → 各效果自己的通道，效果调参 → 材质轻量参数。

---

## 6. 执行

| 阶段 | 内容 | 验收 |
| --- | --- | --- |
| **R1** | CharacterBuffer → ObjectBuffer 迁移；落实 IdentityMS/identity resolve/Facing、`HoSemanticSchema`、entry semantic mask、lilToon `HoObjectBuffer` pass、Reverse-Z 与 actual-N 诊断 | 编译通过；相机 AA 关掉仍请求 4x；ID/Facing/object semantic debug 可见 |
| **R2** | 朝向图：`faceBone` + 三轴（已有）→ MSAA sample Facing 写入→跟随 layer 0 获胜 ID resolve 到 `_HoObjectBufferFacingTexture` + debug 视图 | 多角色同屏/遮挡/轮廓交界时 Facing 不跨 ID 平均；眼透相机角度修正读到与 layer 0 身份一致的 forward / side |
| **R3/R4** | 消费者迁移：角色特化 → AC（组 / 物体位 / 覆盖率）；ScreenProcess → 只吃具名遮罩（V2 §6.2） | 行为不变或更好；`Requires*` 诊断可删 |
| **R5** | SSS / PLR 的**遮罩**切过来（数值走 SB） | 行为不变；无跨来源相乘 |
| **R6** | 与 SB 一起删 MetadataBuffer | 全仓库无 `_HoMetadataBuffer` 引用 |
| **全程** | **调试与登记**（V2 §6.1）：debug 视图 + 进 `HoDebugViewRegistry`（⚠ **CB 今天没注册，R1 顺手补**）+ `HoDebugViewRenderKind` + DebugTile 的可用性 / 资源需求 / shader slice + 契约 debug 列 + **失败可见**（声明与 RT 张数不一致 / 未声明 ID / 溢出 / 非法槽） | 每个池与每张图都能单独看；四种失败在视图里标出，不静默 |

**与旧 bridge 的差异**：身份走 ranked ID+coverage 池，物体 bit 语义由 entry 表解压，材质具名遮罩由 SB 写 surface SemanticId/value，最终只由 AC 产生 Selection ID+coverage。

**不在本文件冻结范围**：三张 StructuredBuffer 的行宽与字段排布（实现细节）。

---

## 7. 冻结决议

1. **ID 不区分角色与场景**：一个空间、一套表、一个池；"组"是泛指的，组的边界由挂组件的人定。
2. **ID = 组 8 + 槽位 8**；高字节只做"同一组"的廉价比较。
3. **ID 类型清单 = 今天 MetadataBuffer 的全部语义**：组 / 部件 / 标记 / 物体位 0~7 / 材质位 0~3（§1.1），**一条不丢**。
4. **UI 名可读**：上面的类型名就是 Inspector 上填的时候看到的名字；物体位 8 条沿用今天的名字，**去掉"角色"字样**。
5. **角色特化固定只吃**：组 + 物体位{全角色, 脸, 前发, 眼睛, 眼透区域, 配件, 人体} + 覆盖率；**预留 7 不是承诺**。
6. **两个池**：① OB 身份池 ranked（4 层 16-bit IdentityId + coverage，请求 N=4、实际 N=4/2/1）；② AC Selection 池 fixed-lane（4/8/16 个 8-bit SemanticId + coverage）。
7. **lane 不是 ID**：`HoSemanticSchema` 为每个 surface-writable SemanticId 分配稳定 lane，最多 16 个；SemanticId 仍是权威语义。
8. **SB 不得自造 Selection ID**；它按 lane 写 `(SemanticId,value)`，AC 校验 owner 后按 schema sourceMode 逐 sample 合成。
9. **遮罩 = 按条目属性加权求和**，不是取某一层。
10. **朝向图内容**：`RG = octa(forward)`、`BA = octa(side)`，一张 RGBA8（4 B/px）；它只对应 ranked layer 0，必须与获胜 ID 同步 resolve，不允许跨 ID 平均。
11. **名字表容量分开统计**：group 256 行、稠密 entry 当前预算 4096 行、Selection 有效 ID 最多 255；各自的第 0 行/ID = 未知，越界回落未知不钳制。
12. **组件通用**：任何物体都能挂，不再是"角色专用"。
13. **本 feature 不带 Cryptomatte 名字、不做合规导出档位**；`crypto_*` 归 AC 或以后的独立 feature。
14. **登记族 `object.*`**；`character.*` 与 `_HoCryptomatte*` 一律不用。
15. **准入判据 + 类②上限两张图 + 没有消费者的不分配**（§5）。
16. **两个面的定位**：身份池是点选/任意身份遮罩/合规导出的运行时身份源；Selection 是 AC 合并 object 语义与 SB surface 语义后的统一运行时语义面。
17. **调试与登记是落地的一部分**（V2 §6.1）：debug 视图 + 进 `HoDebugViewRegistry`（CB 今天没注册，R1 补）+ DebugTile 接得上 + 契约 debug 列 + 四种失败可见；**没有 debug 视图就不算落地**。
18. **UI 按 `Ho-UI_风格规范.md`**：**调试入口在 `HoObjectBufferVolume`**，feature 里只放高级设置 + 兜底默认值（槽数声明默认 4）；ID 的 UI 名就是 §1.1 那一套。

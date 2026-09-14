# Ho-ObjectBuffer（OB）规划

逐物体 buffer：回答"**这是谁、占多少、我要抠哪一块**"。**几何在 GB，表面数值在 SB，合成在 AC。**

> **状态：ID 空间、名字表、每像素存储、纹理与登记名全部冻结，可开工。**
> **"Cryptomatte" 在本 feature 一律不用**：合规导出档位（`crypto_*`、float 位重解释 + manifest）**不是 OB 的事**，归 AC 或以后的独立 feature。

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
| **材质位 0~3**（MaterialFlag） | 材质 custom0~3 | Settings 里可自定义名字 | **SB**（材质，逐像素）——OB 只**预先声明**这批名字，**它们就是默认 4 槽** |

- **覆盖率**（今天 `maskId.x` = `_HoMetadataBufferMaskWeight`）不是类型，它是每个 ID 对里的另一半。
- **朝向**（今天 `faceBone` + 三轴）不是 ID，是**逐物体辅助量**（§2）。
- **遮罩 = 按条目属性加权求和**：`matte_X = Σ_i cov_i · [条目_i 命中 X]`，**不是"取某一层"**。

**角色特化固定只吃这几条**：组 + 物体位{全角色, 脸, 前发, 眼睛, 眼透区域, 配件, 人体} + 覆盖率；**预留 7 不是承诺**，可被场景拿走用。角色特化之后改吃 **AC 合成的结果**，AC 依旧**按组**来吃、来合成。

### 1.2 名字表（冻结）

- **两级**：**组表**（组 ID → 组名 / 朝向来源）→ **条目表**（ID → 名字 / 部件 / 标记 / 物体位）。
- **≤256 条具名条目**（与 SB 同量级）。
- **第 0 行 = 未知**；越界查表**回落到未知行，不钳制**。
- **它是共享的声明数据**（作者在组件 / Settings 里填的），**不是 OB 的输出**——OB 与 SB 都按它解析名字，所以 SB 读它**不违反"producer 互不读"**；两个 buffer 各自的 ID 存储仍互不可见。
- GPU 侧是 StructuredBuffer（`_HoObjectBufferGroups` / `_HoObjectBufferEntries`）；**RSUV 不序列化 ⇒ 每次重建都要重写**。
- 平台不支持 StructuredBuffer（shader level < 4.5）⇒ **整条不跑并告警，不静默降级**。

### 1.3 两个池与对齐规则（冻结）

**① 身份池（ranked，OB 独占，常开）**：4 层 `(组, 槽位)` + 4 层覆盖率。**按覆盖率降序排，槽号不承载语义**；一个像素可以同时属于多个物体（相机 AA 关掉也不丢覆盖率）。

**② 语义槽池（fixed，OB 声明，OB 与 SB 都写）**：**槽号 = 语义**，由 OB **单方声明并固定**（一份声明，帧间不变）：

- 默认 **4 槽**，可配 **8 / 16 槽**；每槽 = 一对 `(ID, 覆盖率)`；每张 RGBA8 装 2 槽 ⇒ 2 / 4 / 8 张，**上限 8 张**。
- **默认 4 槽 = 今天的材质位 0~3**。
- **OB 写物体归属，SB 写材质覆盖**；同一个槽两边都写 ⇒ **取 surface**（递进链是覆盖，不是求和）。
- **天然对齐**：布局由声明固定，**不需要每帧协商、不需要同步**；SB 只读声明、按同一个槽号写，**不得自造槽或 ID**。

**AC 的合成**：身份池按覆盖率加权；语义槽按槽声明（名字 / 类型 / ID）合成——`matte_X = Σ cov_i · [条目_i 命中 X]`。

**空 = 覆盖率 0**：背景不占身份池的层；语义槽没写就是 0。两处都不需要额外的 valid 位。

**失败必须可见（不静默错位）**：声明的槽数与已分配的 RT 张数不一致 → 告警 + debug 标出；查表落到第 0 行（未声明 ID）→ 标出；身份池溢出（一像素 > 4 个物体）→ 标出；SB 写了未声明的槽 → 该值无效 + 诊断。

---

## 2. 每像素存储（冻结）

| 纹理 | 格式 | 内容 | 分配 |
| --- | --- | --- | --- |
| `_HoObjectBufferId0Texture` / `Id1Texture` | RGBA8 ×2 | **身份池**（ranked）：各 4 层，`Id0 = 组 8 bit`、`Id1 = 槽位 8 bit` | 常开 |
| `_HoObjectBufferCoverageTexture` | RGBA8 | **身份池覆盖率**：4 层，不归一化（残差 = 背景占比） | 常开 |
| `_HoObjectBufferSelectionTexture` | RGBA8 ×N（N ≤ 8） | **语义槽池**（fixed）：每张 `R=id0, G=cov0, B=id1, A=cov1` = 2 槽；槽数 4 / 8 / 16（默认 4 ⇒ 2 张）；**与 SB 的 `Selection` 同构同槽** | 按声明的槽数分配，上限 8 张 |
| `_HoObjectBufferFacingTexture` | RGBA8 | 逐物体辅助量，**两个方向**：`RG = octahedral(forward)`、`BA = octahedral(side)`；消费端叉乘得第三轴 | 按需（有物体提供 `faceBone` 才开） |
| 组表 / 条目表 | StructuredBuffer | §1.2 | 常开（小） |
| 内部 depth-stencil | 深度格式 | 两段式占用判定 + tie-break | **不发布** |

**两个池不混**：身份池 ranked（槽号无义、覆盖率为序），语义槽池 fixed（槽号即语义）。**不分角色池与场景池**——同一个 ID 空间混存。两个池都只进 AC，由 AC 合成后才给下游。

**不变式（冻结）**：
- ID **点采样** + `round(v*255)` 还原；**不滤波、不平均**。
- **覆盖率线性，不归一化**（残差 = 背景占比）；**只能按 ID 匹配加权**；背景不占身份池的层。
- 身份池 **`K = N = 4` ⇒ 无尾部丢失**（自建 MSAA 与相机 AA 解耦：相机把 AA 关掉也照跑，`N = 4`）。

---

## 3. 名字（冻结）

| 类别 | 冻结名 |
| --- | --- |
| feature / 代码目录 | `HoObjectBufferRendererFeature`；`Runtime/ObjectBuffer/`（R1 从 `Runtime/CharacterBuffer/` 改名搬迁；CB 那批文件就是骨架，选择层完好） |
| 组件 | **`HoObjectBufferGroup`**（今天的 `HoMetadataBufferGroup`：组 ID / 部件 ID / 标记 / 物体位名单 / 朝向）、**`HoObjectBufferSubject`**（今天的 `HoMetadataBufferSubject`：逐物体覆盖） |
| 纹理 | `_HoObjectBufferId0Texture` / `_HoObjectBufferId1Texture` / `_HoObjectBufferCoverageTexture`（身份池）、`_HoObjectBufferSelectionTexture`（语义槽池）、`_HoObjectBufferFacingTexture` |
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
| **ScreenProcess** | **只吃 AC**：具名遮罩 + 覆盖率（今天 20 个 source → 1 族） | 材质意图仍在材质侧 |
| **PLR** | 反射平面 / 参与物体的遮罩 | GB + SB |
| **AOV / 导出** | ID + 覆盖率 + manifest（名字表） | **不做合规导出档位**（AC 或独立 feature 的事） |

**分工**：各 buffer 只产出自己那一轴的原始数据 + 预留可写通道；**AC** 定递进覆盖（**纯值 < object < surface**）、具名遮罩、给消费者的统一入口。**下游只吃 AC，不吃 OB/SB 的原始图。**

**SB 覆盖 OB 的 ID 渠道**：语义槽池由 **OB 单方声明**（槽号 = 语义、默认 4 槽、可配 8 / 16），OB 写物体归属、SB 写材质覆盖，AC 按 `object < surface` 叠；**布局是固定的 ⇒ 两边天然对齐，SB 不参与同步、不得自造槽或 ID**。

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
| **R1** | 改名搬迁 + 新布局：`Runtime/CharacterBuffer/` → `Runtime/ObjectBuffer/`，常量 `_HoCharacterBuffer*` → `_HoObjectBuffer*`，feature / 组件 / 设置 / 调试 / 编辑器同步改名；**选择层保留**，存储改成 §2 的两个池（身份 ranked 常开 + 语义槽 fixed） | 编译通过；相机 AA 关掉时覆盖率仍是 4x；ID 视图与选择视图都在 |
| **R2** | 朝向图：`faceBone` + 三轴（已有）→ 每帧写 `_HoObjectBufferFacingTexture` + debug 视图 | shader 里能按像素读到 forward / side；眼透相机角度修正改为读它 |
| **R3/R4** | 消费者迁移：角色特化 → AC；ScreenProcess → 只吃具名遮罩 | 行为不变或更好；`Requires*` 诊断可删 |
| **R5** | SSS / PLR 的**遮罩**切过来（数值走 SB） | 行为不变；无跨来源相乘 |
| **R6** | 与 SB 一起删 MetadataBuffer | 全仓库无 `_HoMetadataBuffer` 引用 |

**与 `LILTOON_FORMAL_PIPELINE_DRAFT_V2.md` §3.1 的差异**：那两行桥接口径（`ID0.rgba = coverage/groupId/objectId/flags`、`ID1.rgba = object custom bits`）**已被本文 §2 取代**——身份走 ranked 池（多物体归属 + 真实覆盖率），具名遮罩走**固定语义槽**。

**不在本文件冻结范围**：三张 StructuredBuffer 的行宽与字段排布（实现细节）。

---

## 7. 冻结决议

1. **ID 不区分角色与场景**：一个空间、一套表、一个池；"组"是泛指的，组的边界由挂组件的人定。
2. **ID = 组 8 + 槽位 8**；高字节只做"同一组"的廉价比较。
3. **ID 类型清单 = 今天 MetadataBuffer 的全部语义**：组 / 部件 / 标记 / 物体位 0~7 / 材质位 0~3（§1.1），**一条不丢**。
4. **UI 名可读**：上面的类型名就是 Inspector 上填的时候看到的名字；物体位 8 条沿用今天的名字，**去掉"角色"字样**。
5. **角色特化固定只吃**：组 + 物体位{全角色, 脸, 前发, 眼睛, 眼透区域, 配件, 人体} + 覆盖率；**预留 7 不是承诺**。
6. **两个池**：① **身份池 ranked**（`Id0` = 组 8、`Id1` = 槽位 8、`Coverage`，各 4 层，**常开**，`K = N = 4` 无损）；② **语义槽池 fixed**（槽号 = 语义，每槽一对 `(ID, 覆盖率)`，每张 RGBA8 装 2 槽）。
7. **固定语义由 OB 先一步持有**：**默认 4 槽**（= 今天的材质位 0~3），**可配 8 / 16 槽**，**上限 8 张**（16 槽）；布局帧间不变 ⇒ **天然对齐，SB 不参与同步**。
8. **SB 写同一批槽并按槽覆盖 OB**（OB 写物体归属，SB 写材质覆盖，AC 按 `object < surface` 叠）；**SB 不得自造槽或 ID**。
9. **遮罩 = 按条目属性加权求和**，不是取某一层。
10. **朝向两张内容**：`RG = octa(forward)`、`BA = octa(side)`，一张 RGBA8（4 B/px）；不够用时升 16F，消费端不改。
11. **名字表两级、≤256 具名、第 0 行 = 未知、越界回落未知不钳制**。
12. **组件通用**：任何物体都能挂，不再是"角色专用"。
13. **本 feature 不带 Cryptomatte 名字、不做合规导出档位**；`crypto_*` 归 AC 或以后的独立 feature。
14. **登记族 `object.*`**；`character.*` 与 `_HoCryptomatte*` 一律不用。
15. **准入判据 + 类②上限两张图 + 没有消费者的不分配**（§5）。

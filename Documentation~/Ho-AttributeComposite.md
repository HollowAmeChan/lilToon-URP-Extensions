# Ho-AttributeComposite（AC）

> **状态：已落地**（代码在 `Runtime/AttributeComposite/`）。本文是 **AC 的现行架构说明**：边界、输出、查询 API、
> 覆盖链、帧序与生命周期、导出面。**AC 不画几何、不画表面**：当前输入为 GB + OB + SB；AC 提供屏幕后处理的 typed 遮罩与 ID 查询（见下方接入状态）。
> 相关：`Ho-ObjectBuffer.md`、`Ho-SurfaceBuffer.md`、`Ho-UI_风格规范.md`（面板与 Volume 的排法）。

**所有语义遮罩与合成属性的唯一逻辑入口**：它屏蔽 OB/SB 的存储布局与 ID 解压规则，并发布 GB 场景几何、描边覆盖率查询；下游不再自行解码遮罩与身份。

> **接入状态（2026-10-05）**：AC 已消费 **GB + OB + SB**。GB 作为独立的几何与描边覆盖范围发布；没有 OB 时仍能查询 GB。ScreenProcess 已接 typed 遮罩选择、范围内反选、直接预览与缺失输入诊断。Scalar V1 已接 OB/SB 样本关联统计；按需求裁剪尚未实现，详见 `计划/Ho-AttributeComposite-Plan.md`。GD 仍负责几何数据生产，不建立 GD 属性覆盖链。

> **落地状态（R4b，surface 来源本轮）**：AC 已经存在并可跑，范围是 **object + surface 两个来源**：
> - 已落地：`HoSemanticSchema`（由 `HoObjectBufferPartTags` 生成 8 条 lane，SemanticId = 位序+1、Lane = 位序；**默认 `Intersection`**）、runtime catalog（按 LaneIndex 首次编译成 GPU 常量表；schema 编辑与变脏版本机制待实现）、`SemanticResolve`（一轮 4 张 RGBA8 = 8 条 lane 的 `(SemanticId, coverage)`）、资源集 `HoAttributeCompositeRenderGraphResources`、`HoAC_*` 查询（Identity / Group / Layer0Group / Predicate / TotalCoverage / Selection）、消费者登记与解析失败诊断、feature 面板（schema 与登记只读汇总）、Volume + 调试直出（lane 覆盖率 / lane ID / catalog）。
> - 第一个消费者：**角色特化**已切过来 —— 它不再自己解码 OB 身份池与部件行表，改读 AC 的 Selection 池（转置成自己的位平面），并把 8 个物体位登记为消费者。
> - **surface 来源（R4b）已落地**：SB 的语义 lane（4 张 RGBA8，一张装两条 `(SemanticId, value)`）在 `SemanticResolve` 里按 catalog 的 `sourceMode` 合成（`Intersection` 为默认：表面侧只能收窄 / 细化），**单采样、普通采样**读 —— **不读 MSAA**（按 `Texture2DMS`+`Load` 读时坐标 / 采样数 / `bindMS` 任一处对不上都会静默读错，实测过：池子整片均匀无形状、移动时局部拖影）。此段描述旧的近似路径。2026-10-06 新增 Scalar V1：SB 按 OB 实际 N 捕获 owner/weight/written，在生产侧关联归约为单采样 W/V，AC 消费这些统计。
> - **`AttributeComposite`（R4c）已落地**：`HoAC_Attribute(uv, 0)` = `Classification` 四通道（`sssProfileIdByte / curvatureHint / transmittanceHint / materialClassIdByte`），覆盖链是 `constant < surface` —— **surface 可用性由 `HoAC_SurfaceValid(uv)` 判定**（SB 有产出 **且** SB 的 owner == OB 层 0 身份），不匹配就返回 constant 兜底（本轮恒 0，等有消费者要非 0 再加常量表）。**本轮不落"合成属性图"这张 RT**：AC 只把 SB 的 Classification + owner 作为**引用**发布（与它发布 OB 身份池引用同一套做法），合成在读取时做；真有"要一张图"的需求（调试视图 / 屏空间复用）时再落 RT。
> - **仍然不做**：Selection 池 16 lane 的 MRT 分批（现在固定 4 张 = 8 lane，词表超过 8 位才需要）、DebugTile 的 AC 九宫格（需要给 Debug 轴加一个 `HoDebugViewRenderKind`，AC 自带整屏调试不受影响）、**按消费者登记决定要不要产出 Selection 池**（现在只要 OB 有身份就恒产出 4 张，等消费者多起来再按登记裁剪）。


---

## 0. 流水线复核与 P0 勘误（2026-09-20）

### 0.1 “只吃 AC”是逻辑边界，不是物理句柄消失

- SSS / PLR 仍需要 SB 的 thickness / roughness / color 等物理纹理；角色特化仍需要 OB Facing 和 SB Color。AC 的责任是把这些句柄组织成统一资源集与 HLSL 查询门面，不是假装它们没有被采样。
- RenderGraph 上，消费 pass 必须对 AC 资源集中所有实际采样的 OB/SB `TextureHandle` 声明 `UseTexture(Read)`。查询 API 不能代替 GPU 依赖。
- “消费者不读原始图”的准确含义是：不直接依赖 OB/SB 的 packing 与全局名字，而是通过 AC 契约取句柄并调用 `HoAC_*`。

### 0.2 查询类型必须分开

`HoAC_Mask(id)` 的 `id` 目前同时可被理解为 16-bit 物体 ID、8-bit Selection ID 或某个条目属性，不够严格。实现前把 API 拆成：

- `HoAC_Identity(uint id16)`：完整 `(group,slot)` 精确匹配。
- `HoAC_Group(uint group8)`：按 ID 高字节匹配。
- `HoAC_Predicate(uint predicateId)`：查 entry/group 表中的分类、标记或物体位，对四层做 `sum(cov_i * predicate(entry_i))`。
- `HoAC_Selection(uint semanticId8)`：runtime catalog 先把 SemanticId 解析为 LaneIndex，再读 AC 统一 resolve 的 `(SemanticId,coverage)`。
- `HoAC_TotalCoverage()`：四层身份 coverage 之和，不包含多层 alpha/OIT 颜色贡献。

名字在 C# 变脏重建时解析成上述带类型的 query descriptor。消费者登记用于资源规划与诊断；HLSL 无法阻止未登记代码直接调函数，所以“没登记就读不到”应改成“没登记就报诊断，且不为它分配可选物化产物”。

### 0.3 runtime catalog 与 Cryptomatte manifest 分名

- AC 常驻的“名字 → typed query descriptor”叫 **runtime catalog**，不叫 manifest。
- `idmanifest`、`crypto_object`、32-bit MurmurHash3 和 float 位重解释只属于 AOV 导出层的 **Cryptomatte manifest**。
- 导出层可通过 AC 资源集读 OB 身份池与 runtime catalog，但 AC 不应声称自己复制或“合成”了一份新身份池。

### 0.4 Selection 精度与样本关联

- 当前 OB 已从逐 sample 身份得到 ranked 身份池与 coverage；AC 读取这份单采样身份池。
- SB 发布单采样 `(SemanticId,value)` lane，AC 按五种 sourceMode 在像素级合成。`Intersection` 是 `objectCoverage * surfaceValue`，不是精确的 sample 集合交集。
- SB lane 的 `SemanticId=0` 是未写，声明 ID 配 value=0 是显式写零。近似路径不使用 semantic owner；关联路径逐样本匹配 owner 后统计 W/V；数值属性的 owner 校验是另一条通路。
- Scalar V1 已保留 object/surface 对应 sample 与 owner 关系，在 SB 专用 pass 归约；D3D11/12 完整 2D viewport、N=1/2/4 支持关联，其他域明确近似降级。消费者继续采样单采样产物。详见 `计划/Ho-AC-SemanticPrecision-Contract.md`。

### 0.4.1 表面属性的 object tier 尚无合法生产者

表面数值（roughness / metallic / thickness / curvature / class / profile）本轮冻结为 `constant < surface`，**取消 object tier**。OB entry 不重新塞入旧的 MB 表面值。如果未来需要逐物体数值覆盖，新增 AC 自己的 object-default table，不改 OB 身份表。

### 0.5 pass 数量与时序

- 查询函数本身无 pass。当前为无绘制的输入发布 pass，加有 OB 时固定四张 MRT 的 `SemanticResolve`；lane batching 和独立合成属性 RT 为后续目标。
- `SemanticResolve` 和 `AttributeComposite` 默认都放 `BeforeRenderingOpaques`，顺序是 `{OB, SB} → AC → GTAO → opaque`。它们不读 camera color；如果未来真有 color-dependent 合成，另拆 after-opaque pass。

---

## 1. 边界（当前实现与目标）

| 项 | 内容 |
| --- | --- |
| **输入** | 当前：**GB + OB + SB**；各输入域独立有效，没有 OB 仍可发布 GB |
| **输出** | 当前：合成属性查询、Selection 池、资源引用与 runtime catalog（§2）。新增：屏幕后处理 typed 遮罩与 ID 查询、范围选择、缺失输入诊断 |
| **只经 AC 查询语义的消费者** | ScreenProcess、角色特化；SSS / PLR 是“物理数值句柄经 AC 资源集取自 SB，遮罩用 `HoAC_*`” |
| **禁止** | 消费者不许自己解码 OB/SB packing、不许自己再攒一套语义图；RenderGraph 仍必须声明底层物理纹理的读依赖 |
| **不负责** | 不定义"这是谁"（OB 定）、不定义"表面是什么样"（SB 定）、不做合规导出（导出层做，§6） |

**名字**：`HoAttributeCompositeRendererFeature`（代码目录 `Runtime/AttributeComposite/`）、**`HoAttributeCompositeVolume`**（**调试入口**）、契约登记族 `ac.*`。**UI 按 `Ho-UI_风格规范.md`**：调试在 Volume，feature 只放高级设置 + 兜底默认值 + 消费者登记表（只读汇总）。

**合成原则**：语义 Selection 按 schema sourceMode 合并 object/surface；满足 Scalar V1 域时使用 C/W/V，否则使用旧的像素近似；表面数值本轮只走 `constant < surface`，用 SB SurfaceOwner 匹配 OB layer-0 IdentityId 表达 validity。

---

## 2. 输出（当前实现阶段）

AC **不为每个消费者烤遮罩图**。它发布**一份可查询的合成结果 + 一个查询 API**；要烤成图的效果自己用 API 烤，烤出来的图记在**它自己**名下。

诊断另发布 SB semantic owner 与 lane 的当帧只读引用。`Object Coverage / Surface Written / Surface Value / Semantic Owner Match / Final Coverage / Semantic Compare` 可按语义选择；owner 的在池与主导匹配分开显示。关联统计契约与支持边界见 `计划/Ho-AC-SemanticPrecision-Contract.md`，另有 Written Coverage / Weighted Coverage / Semantic Precision 视图；关联路径的重组误差按 C/W/V 重算。

| 输出 | 内容 | 形态 |
| --- | --- | --- |
| **AC 合成属性查询** | `sssProfileIdByte / curvatureHint / transmittanceHint / materialClassIdByte` 及登记属性；SB owner 匹配时取 surface，否则取 AC constant fallback | 当前引用 SB，读取时合成；独立 RT 尚未实现 |
| **身份池**（引用） | OB 的身份池（ranked：组 / 槽位 / 覆盖率，K=4，实际 N=4/2/1）——**SB 不写身份 ⇒ 不复制**，AC 只发布句柄 | 引用 |
| **AC Selection 池** | Scalar V1 关联或像素近似合并 object/surface 后的 8（目标 4/8/16） 个 `(SemanticId,coverage)` 固定 lane | `_HoACSelection{0..3}Texture`，当前 RGBA8 ×4；16 lane 分批为目标 |
| **GB 覆盖范围**（引用） | 场景几何与描边分别引用 GB coverage / NormalDepth，单采样时以深度有效性回退 | 引用 |
| **查询 API** | §3 的 `HoAC_*` | API |
| **runtime catalog** | 语义名字 → schema entry；typed descriptor 编码查询类型、数值与范围 | lane 表首次上传，SP 配置变化时重新解析 |

- 当前 AC 只写 Selection resolve（四张 RGBA8）；GB / OB / SB 的其他纹理是引用，属性读取时合成。独立合成属性 RT 和按登记裁剪尚未实现。
- AC 只装"**把三来源压成一个每像素答案**"的东西；**不往里塞新语义**（判据见 §7）。

---

## 3. 查询 API（冻结的接口形状）

| 侧 | 谁做什么 |
| --- | --- |
| **C#** | feature **声明它要哪些名字**；变脏重建时经 runtime catalog 解析成 typed query descriptor；**解析不到 → 报诊断，不静默** |
| **shader** | `HoAC_Identity` / `HoAC_Group` / `HoAC_Predicate` / `HoAC_Selection` / `HoAC_Attribute` / `HoAC_TotalCoverage`，不混用不同 ID 空间 |
| **登记** | 消费者登记用于资源规划与 debug 诊断；未登记读取报诊断，不为它分配可选物化产物 |

- **名字只在重建时解析一次**，像素里只有 ID 比较 ⇒ 便宜。
- `HoAC_Identity/Group/Predicate` 对四层身份做 ID 匹配加权；`HoAC_Selection` 按 runtime catalog 定位固定 lane、校验图内 SemanticId 后返回 coverage。
- AC 查询函数不产生 pass；SemanticResolve 当前固定四张 MRT；分 batch 和独立 AttributeComposite RT 为目标，都不做 per-consumer pass。

---

## 4. 覆盖链逐属性解释（冻结）

| 属性 | 纯值（底） | object | surface（顶） |
| --- | --- | --- | --- |
| 语义遮罩 | 0 | OB IdentityId 查表得 object lane membership | SB `(SemanticId,value)`；按 schema sourceMode 逐 sample 合成 |
| 分类 / 曲率 / 透射提示 / SSS profile | AC 常量 fallback | 本轮无 object tier | SB `Classification`，用 SurfaceOwner 匹配表达 validity |
| 朝向 | 无（缺省 = 不生效） | 组表（forward / side） | 材质**不覆盖**朝向 |

身份 predicate 查询按四层 coverage 加权。Selection 在支持域内使用 Scalar V1 关联统计；其他域明确使用像素近似。

---

## 5. 帧序与生命周期（冻结）

- **opaque 之前**：`{OB, SB} → AC SemanticResolve/AttributeComposite → GTAO → opaque`。
- GTAO 现在可以在登记后查询 AC 语义；不需要的项目仍可只用 layer mask / 材质意图。
- **分清每帧与变脏重建**：像素纹理每相机每帧生产；当前 lane catalog 首次上传后缓存；可编辑 schema 的变脏重编译尚未实现。RSUV 在表重建、组启用或域/场景重载后重写，不是无条件每帧全量重写。
- **合成属性图为后续目标**：当前不分配、不写，属性通过引用查询。
- 平台不支持 StructuredBuffer（shader level < 4.5）⇒ 整条不跑并告警，**不静默降级**。

---

## 6. 导出面（数据面冻结，实现待排）

- **导出的唯一逻辑入口 = AC 资源集中引用的 OB 身份池 + runtime catalog**；导出层由此构建 Cryptomatte manifest，不再自行解码另一套 ID 数据。
- 导出是 **L5 的一层、独立 feature**，**不是 AC 的一部分**；**AC 不写 EXR**。
- 编码转换（导出层自己的事，登记在案）：`MurmurHash3_32` → `uint32_to_float32` **位重解释**（含指数位修补）→ **float 通道**存储；manifest **内嵌**（OpenEXR 3.0+ 的 `idmanifest`，sidecar 不被支持）；按 level 拆分（`crypto_object` / `crypto_asset` / `crypto_material`）。
- **现在不做**：EXR 写出、manifest 内嵌、Nuke 验证 —— **R6 之后单独立项**。
- **精度口子**：运行时覆盖率 8 bit；导出若要更高精度，由导出模式决定是否升 16F（口子留着，现在不动）。

---

## 7. 准入与拒绝

- **判据**：这个量是不是"**把多来源压成一个每像素答案**"？是 → AC；不是 → 回它自己的轴。
- **拒绝**：几何 → GB；表面数值 → SB；身份 / 逐物体量 → OB；效果调参意图 → 材质轻量参数；**纯导出的编码与写出 → 导出层**。
- AC 的产物**只有一个来源**（AC 自己）；消费者不许再攒一套。

---

## 8. 执行

| 阶段 | 内容 | 验收 |
| --- | --- | --- |
| **R3-obj**（已落地） | object 来源子集：schema + runtime catalog + typed query API + 消费者登记 + `SemanticResolve`（object-only）+ 资源集 + 调试 | AC 产出 8 条 lane 的 Selection 池；角色特化改读它且行为与迁移前逐位一致 |
| **R4a** | `HoSemanticSchema` 的作者侧入口（surface-writable 语义）+ runtime catalog 变脏重建 + 消费者登记（已先行落地一半） | IdentityId / SemanticId / LaneIndex 不混用；名字解析失败可见 |
| **R4b** | pre-opaque `SemanticResolve`：逐 sample owner 校验 + 五种 sourceMode + 4/8/16 lane MRT batching | 眼白等 SB 表面语义可与 OB 同 ID 粗分统一合成；边缘不出现 bit/coverage 误覆盖 |
| **R4c** | pre-opaque `AttributeComposite`：`constant < surface`，SurfaceOwner 对齐 | Classification 四通道、0 值与未写可区分 |
| **R5** | **消费者输入切换**（V2 §6.2）：ScreenProcess 图层**新接** AC 具名遮罩（原 20 个 rule source 已作为未使用功能删除，**没有旧序列化配置要迁移**，`Requires*` 诊断家族替换）；角色特化 → AC（组 / 物体位 / 覆盖率）+ SB（表面色）；**两者自己的 debug 视图与登记一起改** | 行为不变或更好；`Requires*` 家族消失；解析不到的名字在视图里报出来 |
| **R4 之后** ✅ | 与 OB / SB 一起进 R6：删 MetadataBuffer（已完成） | 全仓库无 `_HoMetadataBuffer` 引用 |
| **R6 之后** | 导出 feature 立项（经 AC 资源集读 OB 身份池 + runtime catalog，再生成 Cryptomatte manifest） | 外部处理与 Unity 内处理使用同一份身份事实 |

**AC 自己的 debug 视图要能看到**：合成属性图、每个 Selection lane 的 SemanticId/coverage/sourceMode、object sample 值、surface written/value、owner mismatch、**消费者登记表**、解析失败/非法 ID。

---

## 9. 冻结决议

1. **AC = 语义遮罩与合成属性的唯一逻辑入口**；下游不自行解码 OB/SB packing，也不再各自攒语义图，但 RenderGraph 仍显式声明底层物理句柄的读依赖。
2. **输出形态 = 可查询**：一份合成结果 + 查询 API；**不为每个消费者烤遮罩图**（要烤由消费者自己用 API 烤，图记在它自己名下）。
3. 当前 AC 写四张 Selection RT，其他输入发布引用；按需合成属性 RT 与 lane 分批尚未实现。
4. 表面数值本轮为 `constant < surface`，以 SurfaceOwner 匹配表达 validity；不把数值属性塞回 OB entry。
5. Selection 支持 Scalar V1 的样本关联；一般化的逐 lane 权重、其他采样域仍为后续目标。朝向材质不覆盖。
6. **排序按属性合成**：每条属性一条覆盖链，先 resolve 成每像素答案，再给消费者读。
7. **名字在 C# 侧解析一次**，像素里只有 ID 比较；消费者要**登记**自己读了什么，解析不到就报诊断。
8. AC 查询本身无 pass；SemanticResolve 按 MRT 能力分 batch，AttributeComposite 一趟；两者默认在 opaque 前。
9. GTAO 排在 AC 之后，可选查询 AC 语义；没登记语义需求时不分配对应可选产物。
10. **导出面冻结**：导出层经 AC 资源集读 OB 身份池 + runtime catalog，再构建 Cryptomatte manifest；导出是 L5 独立 feature，不是 AC 的一部分。
11. **AC 只装"三来源压成一个每像素答案"的东西**；新语义一律按 §7 的判据挡回去。
12. **调试与登记是落地的一部分**（V2 §6.1）：没有 debug 视图与登记就不算落地——AC 至少要能看到合成属性图、合成语义槽、消费者登记表、解析失败。
13. **UI 按 `Ho-UI_风格规范.md`**：调试入口在 **`HoAttributeCompositeVolume`**，feature 只放高级设置 + 兜底默认值 + 消费者登记表（只读）。
14. **待定：无。** 未来新 sourceMode 或 color-dependent 合成以契约变更单独立项。


## 10. ScreenProcess typed 遮罩接入（2026-10-05）

`HoACQueryDescriptor` 解析类型、值、范围；具名语义解析到 lane，组与完整身份保留各自的 ID 空间。ScreenProcess 按配置变化缓存 descriptor，每个渲染 pass 显式声明并绑定查询所需纹理。

| 选择来源 | 查询值 |
| --- | --- |
| 物体总覆盖率 | OB 四层覆盖率之和 |
| 具名语义 | schema 名字 → lane → 合成 Selection 覆盖率 |
| 角色组 | 对四层匹配组字节并累加 coverage |
| 完整身份 | 对四层精确匹配 16-bit 身份并累加 coverage |
| 场景几何 / 描边视觉壳 | GB 对应总 coverage；无 coverage RT 时由有效 NormalDepth.a 作二值回退 |
| 全屏 | 1 |

范围为全屏 / 已登记物体 / 场景几何 / 描边视觉壳。令范围覆盖率为 D、查询覆盖率为 Q，当前像素级裁剪为 `selected=min(D,Q)`，反选为 `D-selected`。这避免重复乘覆盖率，但不承诺两个独立来源之间的精确 sample 交集。查询无效、输入缺失或 AC 本相机未发布时返回 0，包括反选。

ScreenProcess 保留默认物体总覆盖率；新增默认范围为全屏，因此总覆盖率反选现在能排除已登记物体。若要“角色内部排除脸”，选择 Face 并将范围设为已登记物体。范围不是第三套身份池，不产生新的身份编号。

AC 调试增加 Geometry Coverage、Outline Coverage、Input Availability；ScreenProcess 的层调试直出选择结果。未知语义、非法 ID、缺失 producer 的说明显示在 ScreenProcess Feature 运行状态；具名需求同时登记到 AC，消费者停用或销毁后移除。

自定义材质需遵守 `ScreenProcessMask.hlsl` 的字段与查询接口；任意外部 shader 不会自动获得遮罩合成。Selection 仍固定 8 lane / 四张 RGBA8，消费者登记暂不裁剪 RT。GB 发布 pass 无绘制和新 RT；属性合成仍在读取时完成。

## 11. Scalar V1 关联资源（2026-10-06）

SB 归约显式读取 OB 原始身份样本及 ranked 身份纹理；这是登记的跨轴例外，不改变 SB 数值面的独立生产。AC resolve/调试对 W、V、status 逐一声明 `UseTexture(Read)`，按同一 sample domain 发布当相机资源。统计格式、降级规则与成本见 [关联契约](计划/Ho-AC-SemanticPrecision-Contract.md)。

## 12. 消费者需求快照（2026-10-06）

CS/SP 与 AC/SB 调试在入队阶段提交实例所有的本相机需求；Registry 在每次 `beginCameraRendering` 分配新 RenderSequence。生产 Record 阶段冻结 query、资源 flags 与 lane/attribute masks，并附于 AC 资源集。`Time.frameCount` 用于诊断，不作为渲染调用的唯一键。快照只读，冻结后拒绝新的本次声明；相机结束清理工作请求。

消费者显示名和实例 ID 分离，避免同名 Feature 跨 RendererData 覆盖。外部旧 `Declare(string,...)` 仍作为保守全局需求存在；新调用使用 `DeclareForCamera`/`DeclareSemantics`。需求快照现已用于 §12.1 的整池跳过；阶段记录见 [推进计划](计划/Ho-AttributeComposite-Plan.md)。

### 12.1 按需求生产

Selection、旧 surface lane 与 W/V 捕获现已按冻结快照独立判断。没有语义需求时 AC 保留三轴输入发布，Selection flag 为 0；总覆盖率、组/完整身份、GB 几何查询继续可用。需要 Selection 的相机仍保留旧/新 surface 两套输入，确保近似 fallback 的读取有对应资源。SB 数值面沿用自己的启用与消费者契约。

`HoAttributeCompositeProductionDiagnostics` 发布最近相机的逻辑 RT 分配计数。兼容与 RG 都处理需求停用/恢复，兼容 target 同步复位，未产出选择图使用中性绑定。DebugTile 的 SB 语义视图纳入需求收集。上述变化不重新编号 SemanticId/LaneIndex。

### 12.2 独立子相机

URP 的旧 `RenderSingleCamera` 不触发 begin/end camera 事件。PLR 在调用它时使用 `HoAttributeCompositeConsumerRegistry.BeginStandaloneCamera(camera)` 的 `IDisposable` 作用域：每次渲染分配新请求表和序号，结束/异常时移除子相机状态；同相机嵌套时恢复原表。外部直接调用同类无回调渲染 API 也应显式包裹作用域。

2D 相机的未产出 Selection 使用 `RenderGraph.defaultResources.blackTexture`，纹理数组相机仍使用匹配维度的 XR 黑图。独立反射可能在首个普通相机的 XR 默认纹理初始化之前执行，2D 路径不能依赖那份初始 XR 句柄。

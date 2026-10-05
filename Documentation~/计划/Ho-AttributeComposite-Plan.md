# Ho-AttributeComposite 推进计划

日期：2026-10-06。当前阶段：三轴输入/ScreenProcess、合成诊断与 Scalar V1 样本关联已落地；局部 stencil 残影已修复并经 HIRO 现场复测。下一阶段为按相机的消费者需求快照与资源规划。

## 1. 本轮落地

- AC 发布 GB / OB / SB 引用，GB 与描边覆盖范围不依赖 OB；无身份池时不执行 Selection resolve。
- `HoACQueryDescriptor` 分开总覆盖率、具名语义、组、完整身份、几何、描边与全屏查询。具名语义经 schema 解析，ID 不冒充 lane。
- ScreenProcess 每层增加来源、查询参数与范围；保留启用/反转/调试。范围同时限定选择与反选。
- 默认选择为原物体总覆盖率，范围默认为全屏。启用反选现在排除已登记物体；角色内排除某语义时显式选择“已登记物体”范围。
- 每相机从 AC frame resources 判断有效性；RG pass 按实际查询声明读依赖并绑定。兼容路径也按当帧相机 publication 与输入 flags 判断可用性。
- 无效名字、非法 ID、缺失输入均返回零，反选不扩大为整屏。ScreenProcess Feature 显示带层名的缺失原因。
- 层配置变更时重新解析 descriptor。具名需求登记到 AC，消费者停用/销毁后移除；本轮登记尚不规划 RT。
- AC 增加几何覆盖率、描边覆盖率、输入可用性视图；ScreenProcess 的层调试直接预览所选 AC 遮罩。
- 同一效果的不同图层使用独立的运行时材质槽，兼容命令缓冲不再让后一层改写前一层的查询；材质覆盖在运行时实例上应用。
- 兼容路径修正 OB 原生材质的 MSAA/Selection 布局关键字与重复 MRT 附件；AC 明确声明自己的 MRT 附件，使后续 opaque 正确恢复相机目标。

## 2. 覆盖率与 ID 契约

身份来自 OB，Selection SemanticId 来自 schema。GB 不携带新的身份；AC 不复制或重新编号身份池。

范围覆盖率 D、选择覆盖率 Q 的当前裁剪为 `min(D,Q)`，反选为 `D-min(D,Q)`。不重复相乘边缘覆盖率。这是单采样像素级范围裁剪，不代表独立来源的精确 sample 交集。

GB 存在 coverage RT 时取总覆盖率 R；无该 RT 时由有效几何深度得到二值覆盖。描边使用 GB 独立的视觉深度/coverage 输出，不混入物理几何域。空纹理与没有 producer 区分：前者是合法零覆盖，后者是不可用。

`TotalCoverage` 表示已登记物体的几何身份覆盖，不代表透明混合或 OIT 的颜色贡献。“全屏排除角色”包括天空与没有 OB 身份的几何；“场景几何排除角色”需要几何范围。

## 3. 下一阶段

1. 收集当前相机、当前渲染调用的 typed 需求，替换只记录语义名字的全局静态声明；补齐消费者实例、相机与启停/销毁生命周期。
2. 先实现 Selection 的“整池需要 / 整池不需要”：只有总覆盖率、组/身份、GB 几何查询时保留输入引用发布；无人消费语义时跳过 Selection 和语义统计生产。
3. 在关联能力、近似 fallback 和调试需求都明确后，条件生产旧 SB lane；可编辑 catalog 的 revision 与变脏重解析一起收口。
4. 稀疏 lane 对、16-lane 分批、逐 lane 独立权重、合成属性 RT 与导出按实际需求继续后置。

## 4. 验证

隔离 Unity 工程、源快照、GPU probe 与真实 RG 检查在本地忽略目录 `research~/AttributeComposite/`。验证关注具名解析、精确身份匹配、范围反选、0.5 边缘不重复相乘、缺失输入反选、多相机、GB-only，以及各内置 ScreenProcess 效果的遮罩预览。结果与限制以该目录的 Report.md 为准。

自定义材质需主动使用 ScreenProcess 遮罩契约；不对任意外部 shader 自动注入合成逻辑。XR、其他 Graphics API、复杂透明、多角色遮挡精度与大规模性能不属于本轮完整验收。

## 5. 合成正确性验收记录（2026-10-05）

先补可观察性和边界案例，再决定精确合成的数据形态。当前 `ResolveSurfaceLane` 只读取 SemanticId/value；semantic owner 虽作为输入绑定，却没有参与公式。材质只能写所属 renderer 已有的语义位，并不能证明其像素值与 OB 所有 ranked 身份的 coverage 一致。

- 增加单 lane 的 Object Coverage、Surface Written、Surface Value、Semantic Owner Match、Final Coverage 对照视图；owner 应分别检查是否属于身份池、是否为主导身份，二者不混为一项。
- 建立“未写”与“显式写零”、SB 关闭/未产出、owner 不在 OB 身份池、两个身份同语义但材质权重不同、移动交界、1/2/4 sample 的固定案例。现有测试主要覆盖查询和消费者接入，没有完成这些 surface 合成案例。
- 对五种 sourceMode 明确各案例的预期值，以及单采样表面只代表一个 writer 时能够保证的范围；不能把像素级近似标成精确交集。
- 在上述证据上设计 object/surface 对应 sample、owner、viewport 与采样位置的关联契约。只新增“等于 OB layer 0”的 gate，或让两侧分别 resolve 后相乘，都不能恢复多身份交界的关联信息。

该验收包完成后再接精确 producer/resolve 实现，然后推进按相机消费者需求裁剪。16-lane 与导出继续后置。

### 本轮落地记录

已新增单语义 Object Coverage、Surface Written、Surface Value、Semantic Owner Match、Final Coverage 与 Semantic Compare 六格视图。owner 的“在身份池内”和“为主导身份”分别显示。公式重算与生产 resolve 共用 `HoACSemanticCompose.hlsl`；差异图扣除 UNORM8 一 LSB 后放大，避免把存储量化当成公式错误。

RG 调试使用 AC 发布的当帧 semantic owner/lane 引用；不以跨相机静态 LastProduced 判定 RG 来源。当前合成数值行为保留，owner 未作为新 gate。

五种 sourceMode 的未写/写零/缺失/ID 不匹配 GPU 案例已建立；同语义不同材质权重与移动交界证明当前近似缺少 owner/sample 关联。下一轮采用每 owner 的 C/W/V 充分统计契约，详见 [Ho-AC-SemanticPrecision-Contract.md](Ho-AC-SemanticPrecision-Contract.md)。生产关联归约与 AC 消费已于 2026-10-06 接入 Scalar V1；一般化逐 lane 权重仍待实现。

## 6. 持续推进：Scalar V1 已接入（2026-10-06）

OB/SB common sample domain、raw owner 引用、sample-frequency 原生 writer、W/V/status 归约、AC 消费与降级可观察性均已接入。实际支持域与成本见 [关联契约](Ho-AC-SemanticPrecision-Contract.md)。下一优先项转为消费者需求规划：先收集当前相机需要的 lane/资源，再裁剪无消费者的 Selection 和重复旧 lane 捕获；在此之前不扩大为 16-lane 或逐 lane 独立权重。

## 7. 下一轮具体交付：需求快照与安全跳过

### 7.1 代码现状与阻碍

- `HoAttributeCompositeConsumerRegistry` 以字符串消费者名存全局静态表，只记录 semantic names；没有实例、相机、渲染调用或资源种类。多个 RendererData/Feature 可能覆盖同名声明。
- CS 在 `AddRenderPasses` 声明固定八条 lane；跳过与 `Dispose` 没有移除声明。SP 按活动图层维护声明，但它也会随着不同相机覆盖同一份全局状态。现有表适合诊断，不能直接用于资源裁剪。
- AC 只要 OB 有身份就写四张 RGBA8；SB 的旧语义几何捕获（owner + 四张 lane）与新 packet/W/V/status 同时运行。
- `catalogUploaded` 只表示首次上传完成。可编辑 schema 需要 revision；CPU query descriptor、GPU catalog 与 SB lane 声明必须使用同一版本。
- CS 的 ObjectSemantic shader 实际读取四张 Selection。首版保持这份整池需求，避免尚未改消费者就分配稀疏纹理。

### 7.2 顺序

**第一步：只引入需求快照和生命周期，保持原生产规模。**

每个活动消费者提交 query、所需 lane/属性、原始语义调试等需求。实例标识与显示名分开；以当前渲染调用为边界重置相机需求，避免同一 `Time.frameCount` 下多次 RenderRequest 复用旧请求。收集所有消费者的本相机配置后冻结计划，再由 producer 的 RecordRenderGraph 消费。调试视图也必须登记需求。先以面板只读摘要验证计划与实际消费者一致。

**第二步：落地整池跳过。**

计划至少区分 `NeedsSelection`、`NeedsSurfaceSemantic`、`NeedsLegacySemanticDebug` 与 lane mask。没有 Selection/语义调试需求时，AC 发布 GB/OB/SB 引用与可用性，跳过四张 Selection；SB 按计划跳过语义几何捕获及 W/V 归约。SB 数值面由 SSS/PLR 等自己的需求独立管理。具名需求存在、消费者恢复或调试打开时，在当前相机恢复生产。

**第三步：去除关联路径中的冗余旧 lane 捕获。**

当前 Selection shader 在像素关联校验失败时会读取旧 SB lane，所以仅凭 `HasCorrelatedSemantics` 就关掉旧捕获是不完整的。先冻结这条 fallback 的资源与数值契约；旧 writer、平台/采样域不支持、编辑器冷变体和旧语义调试都保留近似产物。处理好这些路径后再条件关闭旧几何捕获与五张附件。

### 7.3 验收

- CS/SP 全关、仅总覆盖率/组/身份/GB 查询时，Selection 与语义统计 pass/RT 数量为零；输入查询仍有效。
- 双相机不同需求、相同显示名的两个 Feature、重复 RenderRequest、启停/销毁与域重载不串请求。
- 打开语义图层或 AC/SB 调试，本相机立即恢复对应产物；关闭后无旧纹理、旧 active flag 或旧声明残留。
- 需求裁剪开/关时有效消费者输出一致；以 pass/RT 数量证明资源收益，再测 GPU 时间。
- 每轮继续执行 stencil 污染、相机移动、眼睛/前发遮挡、写零/未写、旧 writer、1/2/4 sample 与兼容路径回归。现场复测优先使用 HIRO，GPU 检查沿用无需 RenderDoc 的隔离工程。

### 7.4 第一轮已接入：需求快照（2026-10-06）

- 新增 `HoAttributeCompositeDemandSnapshot`：冻结 camera ID/name、frame、独立 RenderSequence、消费者实例、typed queries、资源 flags、lane/attribute mask 与无效查询诊断。query/name 集合复制后只读，同帧重复 RenderRequest 也重新收集。
- CS/SP 改为当前相机、当前实例登记；跳过和销毁移除当前可变请求，停用 Feature 下一次渲染不会继承旧声明。CS 仍声明全部八条 lane，因为当前 shader 实际读取四张 Selection。
- AC/SB 调试独立登记，区分最终 Selection、旧 surface lane 与 W/V 统计。AC RecordRenderGraph 或兼容 Execute 在消费者入队/Setup 完成后冻结，并把 RG 快照附于 AC frame resources。相机结束时清理工作表。
- 保留旧的字符串登记 API；未迁移的外部调用按全局需求保守合并，并在快照标记 `HasUnscopedConsumers`。新调用使用 `DeclareForCamera`/`DeclareSemantics`，显示名不参与实例去重。
- Feature 面板只读展示最近相机的渲染序号、请求资源与 lane mask。当前生产规模保持原样；尚未按计划裁剪 RT，也没有宣称 GPU 性能收益。属性 mask API 已预留，数值消费者仍由各 feature 管理。

下一轮直接推进 §7.2 第二步的整池跳过：先让无语义需求相机省掉 Selection 与语义统计，再处理旧 lane 的 fallback/调试依赖。catalog 可编辑 revision 与稀疏 lane 对仍为后续项。

# Ho-AttributeComposite 推进计划

日期：2026-10-05。当前阶段：三轴输入/ScreenProcess 接入与合成正确性诊断已落地；精确 producer/resolve 与按需求资源规划后置。

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

1. 定义并验证 surface 未写、显式写零、semantic owner 不匹配与多身份交界的行为。增加 object/surface/final 对照诊断。
2. 若要求精确交集，设计 object/surface sample 与 owner 的关联输出；不能把独立 resolve 后的乘法当成精确交集。
3. 将消费者登记扩展为查询类型、属性、lane 和当前相机需求；按需求裁剪 Selection 生产并引入 catalog revision、变脏重编译及生命周期管理。
4. 词表超过 8 lane 后再增加 MRT 分批；合成属性 RT、Cryptomatte/AOV 导出按实际消费者另排。

## 4. 验证

隔离 Unity 工程、源快照、GPU probe 与真实 RG 检查在本地忽略目录 `research~/AttributeComposite/`。验证关注具名解析、精确身份匹配、范围反选、0.5 边缘不重复相乘、缺失输入反选、多相机、GB-only，以及各内置 ScreenProcess 效果的遮罩预览。结果与限制以该目录的 Report.md 为准。

自定义材质需主动使用 ScreenProcess 遮罩契约；不对任意外部 shader 自动注入合成逻辑。XR、其他 Graphics API、复杂透明、多角色遮挡精度与大规模性能不属于本轮完整验收。

## 5. 下一轮具体落点：合成正确性验收

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

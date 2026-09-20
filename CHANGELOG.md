# Changelog

## Unreleased

- **Ho-SurfaceBuffer（SB）数值面落地**（规划 R3-sb，三轴里最后一条缺失的轴）：回答"表面是什么样"，几何在 GB、身份在 OB、合成在 AC。
  - **新轴 `Runtime/SurfaceBuffer/`**：feature（高级设置 + 兜底默认值）+ **数值 pass**（一趟几何 pass 写五张图 + internal owner：`Color` RGBA16F / `Normal` octa RGBA8 / `Material`(perceptualRoughness·metallic·thickness) / `Reflection`(reflectance·plrStrength) / `Classification`(sssProfileId·curvatureHint·transmittanceHint·materialClassId) / `Owner` R16_UNorm）+ **自用深度** + RDG 与兼容两条路径 + 资源集 `HoSurfaceBufferRenderGraphResources` + Volume 调试直出（五张 + **owner 对齐视图**：绿 = 与 OB 层 0 一致、红 = 不一致或没人写、洋红 = OB 没产出）。
  - **owner = pixel validity 的唯一判据**：数值图里的 0 是合法值，只有 owner 能区分"写了 0"和"没人写"（规划 §0.1 / §4.8）；owner 就是 RSUV 的低 16 bit（= OB 写的 `partId`），所以 AC 之后能用它跟 OB 层 0 的 IdentityId 逐像素对齐。规划里写的 `R16_UINT` 改成 `R16_UNorm`：0..65535 逐值精确，而且消费端不必换成整数纹理通道。
  - **透明不生产**：队列上限压在上不透明段末尾（`GeometryLast`）—— 多层透明加不出唯一的前表面真值（规划 §0.5），先明确"不生产"，等策略定下来再放开。
  - **材质侧（跨仓 lilToon）**：新增 `lil_pass_surface_buffer.hlsl`（由 metadata pass 的 surface 部分派生，**不用 MPB**）+ 22 个 URP lilblock 的 `LightMode=HoSurfaceBuffer` pass + 4 个新材质属性 `_HoSurfaceThickness` / `_HoSurfaceCurvature` / `_HoSurfaceTransmittanceHint` / `_HoSurfaceMaterialClassId`（**不再新增 `_HoMetadataBuffer*`**）。
  - **本轮刻意不做**：SB 的 MSAA semantic lane pass（`_HoSurfaceSemanticOwnerMS` / `Lane{0..7}MS`）与 AC 的 surface sourceMode 合成、`Classification` 的消费者迁移（SSS 仍读 MB）、DebugTile 登记。
  - 文档：SB 规划加「落地状态」并逐行标注执行表进度。

- **Ho-AttributeComposite（AC）起步**（规划 R3-obj）：**语义遮罩的唯一逻辑入口**从纸面变成代码，范围是 object 来源的子集。
  - **`HoSemanticSchema`**：由 `HoObjectBufferPartTags` 生成 8 条 object-only lane（**SemanticId = 位序 + 1、LaneIndex = 位序、objectTagBit = 位序**）——词表只有一份，不为 AC 另造名字；带唯一性 / 范围 / lane 上限校验。runtime catalog 按 **LaneIndex**（不是声明顺序）编译成 GPU 常量表，变脏重建时上传。
  - **`SemanticResolve`**：一趟全屏 pass 读 OB 身份池（4 层 + 覆盖率）与部件行标签，按 lane 算 `Σ cov_i · 该层带不带这一位`，写 4 张 RGBA8（每张两条 `(SemanticId, coverage)`）= **AC Selection 池**。所有消费者共用这一份，不再各自解码 OB；兼容（非 RenderGraph）路径一并接线。
  - **`HoAC_*` 查询门面**（`Runtime/AttributeComposite/Shaders/HoACQuery.hlsl`）：`Identity / Group / Layer0Group / Predicate / TotalCoverage / Selection`（按 lane 取覆盖率并**校验图内 SemanticId**）；`HoAC_Attribute` 本轮恒 0 并在注释里写明"等 SB/R4c"，不假装有。
  - **资源集 + 消费者登记**：`HoAttributeCompositeRenderGraphResources`（Selection 池 + 身份池引用）；`HoAttributeCompositeConsumerRegistry` 记录谁读了哪些名字，解析不到在面板与诊断里报出来（规划 §3：登记是诊断，不是权限）。
  - **面板**：feature 只放高级设置 + **只读**的 schema/catalog 汇总 + 消费者登记表；调试入口在 Volume（lane 覆盖率 / lane SemanticId / catalog 三个模式 + 整屏直出，没产出时暗红，与 OB 同一约定）。
  - **第一个消费者：角色特化**已切过来 —— 删掉自己"读 OB 身份池 + 部件行表"的解码，改读 AC 的 Selection 池并转置成自己的位平面（图记在它自己名下，规划 §9.2）；同角色判定改用 `HoAC_Layer0Group`；输入自检改为「OB 身份池（经 AC 引用）/ AC 语义槽 / GeometryBuffer」。**行为与迁移前逐位一致**（debug 3/4/8/9–15/18–21 不变）。
  - **本轮刻意不做**：surface 来源与 `SurfaceOnly / Union / SurfaceOverride / Intersection`（等 SB 落地）、`AttributeComposite` 数值属性（`constant < surface`）、16 lane 的 MRT 分批（现在固定 8 lane / 4 张）、DebugTile 的 AC 九宫格（需要给 Debug 轴加 `HoDebugViewRenderKind`，AC 自带整屏调试不受影响）。
  - 文档：AC 规划加「落地状态」与 R3-obj 行；`语义掩码.md` v2 的"谁解压"改成 AC。

- `Ho-ObjectBuffer`：组件新增**「赋值方式」**（面板底部，作用于整份部件列表；**默认覆盖**）——把"一个物体被多个部件条目命中"这件事显式化（规划 0.3.15）。
  - **覆盖**（默认）：条目顺序即优先级，**排在下面的条目接管上面条目里的同一个物体**，同组内不再报冲突 —— "顶上放一条『全体』（勾全角色），下面放人体 / 脸 / 前发…"这种用法不再需要反选没归属的物体、也不用给每个部件重复勾位。
  - **指定**：审计模式，重叠 = 配置错误并在面板底部逐条列出谁赢谁输，同组内**取条目顺序在前者**。顺带把两处裁决对齐了：RSUV 的写入（`ApplyIdentity`）此前只按条目顺序、冲突面板（`ResolveAssignments`）按距离，同组重叠且距离不同时两者会互相矛盾，现在统一为条目顺序。
  - **两种模式都不做标签继承**（刻意）：palette 一行 = 一个部件 = 一份标签掩码，像素里只有 `partId`；做继承会让"同一部件里被接管与没被接管的物体"需要两份标签、一行装不下，只能再靠校验强制拆部件。所以条目的标签就是它拿到的那些物体的全部标签（想让它们同时保有上一条的位，就在下面那条里一起勾）。
  - 跨组规则不变（离 Renderer 更近者胜，且一律报冲突）。

- `Ho-ObjectBuffer` **R2（第二步）：角色特化的语义源整支切到 OB**（规划 0.3.14）。**语义 = 身份 × 标签 × 覆盖率**：新增 `HoCharacterObjectSemantic.shader` —— 一趟全屏 pass 读 OB 身份池的 4 层 `(组, 槽位)` 与逐层覆盖率，按部件行表的标签位掩码把该层覆盖率累加到对应通道，打成两张位平面（通道布局与从前的 `objectCustom0_3` / `objectCustom4_7` 一致：`全角色/脸/前发/眼睛` + `眼透区/配件/人体/预留`）。**多归属直接累加**（"整角色 + 脸"同时成立），因此：
  - **前发投影 / 眼睛透过 / 脸色扩散 / 两条轮廓全部拿到真实覆盖率**——以前只有脸那一支碰巧有效、前发与眼透区读不到（MB 的 `objectCustom` 位本来就没被写对），现在四支都按标签工作；像素旋钮（柔化 / 羽化 / 扩张 / 模糊半径）语义不变。
  - **那套「掩码抗锯齿副本」整体删除**（`HoCharacterSemanticMaskBlur.shader` + pass + 5 个「读取抗锯齿掩码」开关 + 「抗锯齿宽度」）：覆盖率本身就是 MSAA resolve 出来的连续场，再滤波只会把已经正确的边缘搅糊（规划 0.3.x「不再把 bit 通道当普通 UNORM 过滤」）。读取一律点采样。
  - **同源判定改口径**：同角色比较从 `MaskId.g` 换成 OB 身份池层 0 的**组字节**（`Id0.r`），与眼透角度表的行号同一套编号（眼睛捕获里写的角色 ID 也改成这个组字节，两边单位统一为字节值）。
  - **材质捕获 pass 也改吃标签**：`LilHoCharacterCaptureShouldDraw()` 由"读 objectCustom 位"改成"读 RSUV 的 `partId` → 部件行表的标签"（`脸` → 脸捕获、`眼睛` → 眼捕获）；标签位名 `HO_OBJECT_TAG_*` 落在 `HoObjectBufferPalette.hlsl` 作为 HLSL 侧唯一权威。没有 OB 身份（RSUV = 0）的物件标签恒 0 ⇒ 两遍都不画。
  - **角色特化不再读 MetadataBuffer**（maskId / objectCustom / SurfaceColor 全部退出；SurfaceColor 那份 coverage 由标签覆盖率替代），feature 的输入自检改为「OB 身份池 / OB 语义位平面 / GeometryBuffer」三项；OB 不在 renderer 里时整支 no-op 而不是"退化成硬边"。
  - **顺带修掉一个静默失效**：屏幕空间的 texel size 以前读 `_HoMetadataBufferMaskIdTexture_TexelSize`（全局纹理没有这一项、实际为 0），"按像素扩张 / 羽化"的半径可能一直没生效；现在由 C# 显式发布 `_lilHoCharacterScreenTexelSize`。
  - 增强轮廓的「来源通道」枚举改名 `HoCharacterSemanticChannel`（值仍是位序号 0..7，与标签位序对齐）。
  - 文档：`语义掩码.md` 升 v2（盒子核服务已删、OB 就是"拿回亚像素相位"的落地）、眼睛透过 / 脸色扩散 / 通道契约 / 浏览器说明同步。

- 修复 `Ho-ObjectBuffer` 抽屉：**Renderer 列表为空时表头不收拖拽**——「Renderer（0）」那一行原来只在列表非空时才是拖放目标，空列表下往它上面放没任何反应（只有下面那条 16px 提示条收东西），看着像整个列表都不收。现在表头恒定接拖拽。

- `Ho-ObjectBuffer` **R2（第一步）**：眼透相机角度修正切到 OB 口径——角度表数据源改为 `HoObjectBufferGroup`（行号 = **OB 组 ID**，朝向取「朝向参考系」），屏幕空间查表键改为 **OB 身份池层 0 的组字节**（`Id0.r`，新增 `ResolveObjectBufferGroupId`，并加 `_HoObjectBufferValid` 兜底），于是多角色同屏不再跨 ID 平均、且不再依赖眼睛捕获缓冲里的预乘角色 ID。眼睛**遮罩**链路仍走 MetadataBuffer，随 R3/R4 的消费者迁移一起切。

- `Ho-ObjectBuffer`：**R1 收口**（规划 0.3.11 的四项）——① 发布 `requested` / `actual` 采样数：全局 `_HoObjectBufferRequestedSamples` / `_HoObjectBufferActualSamples` + 「Sample Count」调试视图（绿 4x / 橙 2x / 红 1x）+ 降级时告警一次，C# 侧读 `HoObjectBufferPass.LastActualSamples`；② 部件行表的全局名改为 `_HoObjectBufferEntries`（与 §1.2 对齐）；③ 删掉选择层里没人读的溢出计数；④ 把"身份池溢出在 N ≤ 4 下结构性不可能"写成结论（0.3.2）。**回归验证器已实跑通过**（`Id0=(1,1,1,1)` / `CoverageTotal=(1,1,1,1)` / `Valid=(0,0.796,0,1)` 绿哨兵 / `Id3=(0.271,0.271,0.271,1)` 背景灰），调查期那个硬编码 PTP 场景路径的场景诊断已删除，验证器保留为整链自检。

- `Ho-ObjectBuffer`：部件条目的「类别」正名为**「标签」（位掩码、可多选）**——`HoObjectBufferPartTags` 覆盖「全角色 / 脸 / 前发 / 眼睛 / 眼透区 / 配件 / 人体」+ 一个预留位，面板上用 Unity 自带的**位掩码下拉**（不另画控件，位多了也不会把面板撑长；显示名取自枚举的 `InspectorName`，加一位只改枚举）。**多选是必须的**："整角色 + 脸"同时成立时单值枚举表达不了；它与 MetadataBuffer 的 `objectCustomMask` 对等，并且是**角色侧唯一的语义扩展点**（角色有固定词表，场景才用自由创建的选区，见规划 0.3.12 / 0.3.13）。palette 部件行空出来的 `category` 列改名 `reserved` 恒 0（行仍是 64 B，不动跨仓契约）；选区条目的 `tags` 仍无输入——选区的语义由它的名字承担。边界不变：材质类语义（皮肤 / 不透明测试 / 半透明…）是**表面**语义，归 SB。

- `Ho-ObjectBuffer`：**朝向改为按身份查表**（规划 0.3.1 重写）——朝向是"每个角色一份"的常量，消费端拿像素里层 0 的获胜身份取组查它即可，所以不再计划逐像素的 `_HoObjectBufferFacingTexture`（留到"同一部件内部朝向逐像素不同"这类需求出现时再开）。会**相对身体转动**的部件（头 / 脸 / 前发）可在条目上填「朝向覆盖」（留空 = 继承组）；「朝向参考系」从「组设置」里独立出来，组这一级只剩它这一项共享常量。顺带删掉调试期的控制台刷屏（每条 RSUV 写入的前 5 条 + 汇总 + palette 表转储）。

- `Ho-ObjectBuffer`：**组 ID 改为自动分配**——注册表认领已落盘的值、把没号或撞车的补到最小可用号并在编辑器期写回组件（组数超过 255 时明确报错）；组件上不再手填，于是"两个组件抢同一个号"从硬错误变成不可能。顺带撤掉「组级标签」（没有任何消费端读它，"整组"语义用组 ID 判定即可）与「优先级」（裁决改为离 Renderer 更近者胜、距离相同用组 ID 定序）。详见规划 0.3.10。

- 新增 **`Ho-ObjectBuffer` R1**：用“per-pixel 只存 IdentityId + coverage，其余按 ID 查表”替换 MetadataBuffer bit mask 身份路径。MetadataBuffer 暂时并存。
  - **组件**：`HoObjectBufferGroup`（Add Component: `Rendering/Ho-ObjectBuffer Group`）维护组/部件表并把 16-bit `group:8 | slot:8` 写入 RSUV；重复组 ID 使冲突组全部失效并报错。
  - **覆盖率**：自建 MSAA（R1 固定 `R16_UNorm`）与相机 AA 解耦，resolve 按整数 ID 数票取 4 层；深度平票改在 linear eye depth 上比较，修复 reversed-Z 方向错误。
  - **lilToon 跨仓 pass**：22 个 URP `.lilblock` 模板新增 `LightMode=HoObjectBuffer`，复用 MetadataBuffer 已验证的 alpha-mask/dissolve/dither/cutout 逻辑；opaque fallback 仍作非 lilToon 兜底。
  - **调试**：`HoObjectBufferVolume` 提供 ID0-3 / total coverage / layer coverage / valid 直出；`object.*` 视图已注册进 DebugTile。无产出时暗红，unknown palette 为洋红。
  - **验证**：Unity 6000.3.15f1 实际编译 / Shader 导入通过；`RSUV → HoObjectBuffer pass → 自建 MSAA → resolve 数票 → palette → 调试视图` 整条链已在 PTP 场景实测闭合（组1 = `0x0100`、组2 = `0x0200`，层0 逐组上色，层1 在轮廓像素上给出第二个身份）。实测闭合的四条硬约束与"层1 边缘是设计结果"的判据见规划 0.3.9。
  - 编辑器菜单 `HoLil/Validation/Validate Ho-ObjectBuffer R1` 是最小闭环回归：断言 Id0 亮且中性（身份没有被写死成常量）、覆盖率为 1、Valid 是绿哨兵、单物体层3 是背景灰；验证相机放在 y=1000，不受当前场景内容影响。

- 修复：**切换场景后新场景渲染不出来（黑屏）、只能重启**，报 `MissingReferenceException: The object of type 'UnityEngine.Texture2D' has been destroyed`，栈顶为 `HoCharacterEyeAngleTable.Upload`。
  - 根因：`HoCharacterEyeAngleTable` 以 `Camera` 为键缓存"每相机一张"的表纹理，而 `RemoveStaleTables` 用 `camera == null` 判活。Unity 的 `UnityEngine.Object.==` 被重载为"已销毁对象的任何比较都返回 true"，于是**重载 / 域重载后连存活相机也被判成 stale**，其表纹理被 `CoreUtils.Destroy` 销毁，同一帧紧接着的 `Upload` 又去访问这张纹理。异常落在 `AddRenderPasses` 内部，会中断整条相机渲染录制的后续步骤，表现就是新场景什么都渲染不出来。
  - 修复（两层，任一层单独成立即可自愈）：① 判活改为 `ReferenceEquals(camera, null) || camera == null`；② 表纹理改为"上传前检查有效性，失效就在原条目上重建"（`EnsureTexture`），并在销毁纹理后给条目置 `textureDestroyed` 标记而不是摘掉条目，因此 `Release()` / 场景卸载触发的资源回收也不会再让后续 `Upload` 落到已销毁对象上。
  - 顺带：表纹理名不再带相机名后缀（`_lilHoCharacterEyeAngleTable_<相机名>` → `_lilHoCharacterEyeAngleTable`），避免逐场景切换时不断累积名字各异的泄漏纹理；`GetOrCreateEntry` 不再在构造条目时创建纹理，创建与重建统一收敛到 `Upload` 一处。

## 0.2.0

- 角色特化：眼透新增**相机角度修正**。
  - `HoMetadataBufferGroup` 新增"面部朝向"（Transform + 脸前/右/上三轴枚举）与 `TryGetWorldFacing()` 世界朝向入口（可供 SDF 等后续消费者复用）。
  - 新增 `HoCharacterEyeAngleTable`：每个渲染相机一张 256×1 角度表，CPU 在 `AddRenderPasses` 时机按相机计算平转角/俯仰角（atan2 全角域）并绑定为全局纹理；多相机/双窗口各自正确，无判定、无模式分支。
  - `Composite` 新增 `ResolveEyeAngleFactor`：视锥内 1 / 视锥外 0（柔化过渡），仅作用于眼透不透明度，前发投影 receiver 与原始 revealMask 不受影响。
  - 参数（Settings + Volume）：启用相机角度修正、角度修正强度、平转半角范围（默认 90°）、俯仰半角范围（默认 60°）、角度柔化（默认 40°）；默认关闭，行为与旧版一致。
  - 调试模式：`EyeAngleFactor (16)`、`EyeAngleTable (17)`。
  - 文档：新增 `Documentation~/CharacterSpecialization_EyeReveal.md`。

- 角色特化：**语义掩码抗锯齿**（feature 级公共服务）+ 前发投影的边缘锯齿修复。
  - 根因：FrontHair/Face 等语义来自 MetadataBuffer 的 `objectCustom` 位（契约就是 `RGBA(bits)`，单采样 0/1），任何把它当轮廓用的消费端都只能拿到硬边。旧的 9 抽整数偏移 + 点采样核只能把边放在整 texel 上：仿真的竖直边响应只有 `0 / 0.285 / 0.715 / 1`（两级 2px 平台夹 0.43 台阶），把「柔化像素」调大只会拉宽平台、不减小台阶 —— 表现就是"柔化无效"，且边缘随相机整 texel 跳动。
  - **新增服务**：`HoCharacterSemanticMaskBlur.shader` 把两张 `objectCustom` 纹理**整体**过一次盒核（每个通道本身就是一个语义的 0/1 场，盒核逐通道运算，不串道）产出抗锯齿版，一次 pass、两个 MRT、一帧一次，所有消费端共用。原位图不改，也不要把这条规则套到 `maskId`/`surfaceData`/`custom0`/`eyeData` 上。
  - **开关按效果就近放置，没有总开关**：顶部只有一个「抗锯齿宽度」（不带分组框/描述框，说明在 Tooltip 里），每个效果的分区里各有一行「**读取抗锯齿掩码**」（前发投影 / 脸色扩散 / 眼睛透过 / 主体轮廓 / 增强轮廓）—— 勾选的效果读抗锯齿版，取消则读原始 bit（硬边）。默认：前三个开，两个轮廓**不读**。五个全关时这趟 pass 不跑（零开销）。着色器侧由 `_HoCharacterSemanticMaskOptions`（x=副本存在，y/z/w=三个效果的勾选）与轮廓源 pass 的逐 pass 开关驱动。
  - **读取只有一处决策**：`SampleSemanticBit(uv, channel, useAntiAliased)` 在"副本存在且该效果勾选"时读副本、否则读原始 bit；`SampleSemanticEdge` 在读副本时退化为一次双线性读取，否则退回固定 1px 盒。因此前发投影的接收面（原来把投影硬裁在发际线、导致"下半段模糊、上半段是硬边"）、眼透区域、脸色扩散、眼睛透过闸门、主体/增强轮廓的语义源都吃到抗锯齿版，且与任何相机 AA 设置无关（MSAA/FXAA/TAA 都关着也照跑）。
  - 盒核而不是高斯：同宽度下高斯把权重堆在坡道中段，台阶比盒高 ~2.2 倍。实测跨竖直边逐 texel 台阶：旧核 `0.285 0.000 0.430 0.000 0.285 0.000`（有平台）、高斯螺旋 `0.033 0.243 0.441 0.248 0.035`、盒 `0.200 0.200 0.200 0.200 0.200`（= 1/宽度，二值输入下的理论下限）。宽度在着色器内下限 1px（0.5px 半径在单像素内仍跳 0.84），`柔化像素` 仍是各效果自己的艺术柔化，叠加在副本之上。
  - **删除「避开前发」参数**（Settings / Volume / Editor / 材质参数打包全部移除，`_HoCharacterHairShadowParams1` 重排为 x=spread, y=blend mode, z=use reveal area）：它本来就是多余的 —— receiver（Face 标记）已经把投影限制在脸上，而"减去前发自身 footprint"只会在投影与投影源重叠处切掉一圈半暗，表现就是边缘一圈叠不上去。现在 `shadowMask = shiftedHair × receiver × same`。
  - 通道契约：新增 `maskcoverage.low/high`（`LILTOON_CHANNEL_CONTRACT_V1.md`），生命周期为 RenderGraph transient / 兼容路径的两张 feature RT。
  - 上限说明：线性模糊只能把台阶做小做匀，边的**位置**永远量化在 texel 上；要拿回亚像素相位必须让掩码自带逐 sample 覆盖（MSAA / 超采样捕获），仍不在本次范围。
  - 文档：新增 `Documentation~/架构边界/语义掩码.md` —— bit 位存掩码的结构性弊端（为什么当轮廓用必然是硬边）、RSUV 装不下 per-pixel 覆盖、线性滤波的上限，以及本轮踩过的 7 个坑（滤波器抽头落在整 texel、二值闸门硬裁、拿未模糊 bit 做算术、透明队列故意丢 alpha、"通道预算"误判、依赖外部 AA、混合模式对比度伪装成几何问题），并附要保留的实现清单、新特性约束与亚像素相位的遗留方案。`MSAA.md` 已加交叉引用。

## 0.1.0

- Added the Unity Package Manager manifest.
- Added runtime/editor assembly definitions.
- Added the initial Weighted OIT renderer feature entry point.

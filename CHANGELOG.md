# Changelog

## Unreleased

- `Ho-ObjectBuffer`：**R1 收口**（规划 0.3.11 的四项）——① 发布 `requested` / `actual` 采样数：全局 `_HoObjectBufferRequestedSamples` / `_HoObjectBufferActualSamples` + 「Sample Count」调试视图（绿 4x / 橙 2x / 红 1x）+ 降级时告警一次，C# 侧读 `HoObjectBufferPass.LastActualSamples`；② 部件行表的全局名改为 `_HoObjectBufferEntries`（与 §1.2 对齐）；③ 删掉选择层里没人读的溢出计数；④ 把"身份池溢出在 N ≤ 4 下结构性不可能"写成结论（0.3.2）。**回归验证器已实跑通过**（`Id0=(1,1,1,1)` / `CoverageTotal=(1,1,1,1)` / `Valid=(0,0.796,0,1)` 绿哨兵 / `Id3=(0.271,0.271,0.271,1)` 背景灰），调查期那个硬编码 PTP 场景路径的场景诊断已删除，验证器保留为整链自检。

- `Ho-ObjectBuffer`：部件条目的「类别」正名为**「角色组分」**——它是"这个部件是角色的哪一块"（单值、互斥），本质是"角色的预置选区"（角色有固定语义，场景才用自由创建的选区，见规划 0.3.12）；枚举显示名同步中文，并写明 AC 上线也不改这套分类。顺带把**标签位撤出面板**（部件与选区两处）：它现在没有任何消费端、四位还混了角色语义与表面语义，字段保留不动契约，权威路径留给 R3/R4 的 schema + lane mask（规划 0.3.13）。

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

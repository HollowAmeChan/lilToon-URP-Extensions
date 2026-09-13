# Changelog

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

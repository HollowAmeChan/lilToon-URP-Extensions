# Ho-RTGI半分辨率：输入与重建事前设计

步骤012仅完成资源取证，本文是下一实现步骤的约束，未交付半GI。完整目标仍包含010的filtered RGB能量、实际动态/细节、正常相机与Player预算。

## 动机和参考边界

实际古堡512²自有GI/NRD/实例payload为65.151 MiB。相同布局投影到1080p约513.997 MiB；若仅把GI网格缩为960×540，该部分约128.799 MiB，但还需全分辨率输出/guide和重建开销，不能宣称已经节省75%总场景显存或GPU时间。HD首次运行已发生Unity fence错误，不能用推算代替完成。

本机参考`CameraFrameStateRtxdiExtensions.GetConstants`同时区分renderSize、rectSize、outputSize与前帧rectSize；`NativeGBufferConstants.BuildView`亦携带scale/jitter。`NativeRtxdiOutputBlitPass`会按resolutionScale读输出，并可选DlssOutput。它证明输入、历史、输出域需要整体协调；没有证明其最终DLSS/缩放可直接用于Ho的全分辨率GB/SB边缘重建。v0.1不集成DLSS。

DI第44步ResolutionQuality扩大的是整个相机256/512尺寸，保持固定像素半径并注明世界足迹变化；不能拿它作为半GI的质量证据。

## 当前必须拆开的坐标合同

| 当前代码 | 现有假设 | 半GI必须处理 |
| --- | --- | --- |
| Feature `context.Prepare(cameraTargetDescriptor.width,height)` | 相机、GB/SB、GI同尺寸 | 独立GI尺寸、原输入尺寸和输出尺寸；所有分配/dispatch/签名同步 |
| ApplicationBridge复用DI `RAB_GetGBufferSurface` | GI pixel直接是primary pixel | GI专属加载映射，保持DI原实现和材质合同；native/compute全部使用同一映射 |
| DI shared previous history + GI Final保存depth | UV=`(pixel+.5)/GI尺寸`同时代表实际full receiver | 必须还原被选中的真实full UV；不能用另一条视线的depth重建worldPos |
| Temporal motion与SDK spatial radius | 像素为原相机单位 | 明确GI单位、代表点偏移、前后尺寸；空间足迹先保持全屏像素含义再验收 |
| NRD viewZ/normal/common projection | guides落在规则grid像素中心 | 代表像素偏移必须与NRD camera/jitter一致；不能每块任意换前景点却仍传规则grid投影 |
| Resolve与PublishedDiffuse/Composite/debug | 发布完整receiver radiance、albedo已乘一次、alpha为receiver validity | 保留全域输出；不得point/bilinear放大跨墙或把低图albedo染到另一材质 |

最容易遗漏的是半个full pixel的中心偏移：2×2块里选择`fullPixel=2×giPixel+1`时，实际full中心为`2×(giPixel+.5)+.5`，并非GI中心按比例投影的位置。若采用该固定代表点，前帧世界重建、Temporal投影及NRD camera/jitter都要一致补偿；只改primary加载或只改dispatch不成立。奇数宽高末块也不满足统一映射，需专门证明或显式退回full，不能默默clamp。

## 建议实现顺序及接纳条件

1. 先做Editor可独立验证的规则代表点映射，检查像素边界、透视/正交、非中心投影、镜头移动、前后分辨率与奇数尺寸。CPU独立反投影对GPU实际receiver位置/深度/法线；坐标误差未过前不引入重建或调NRD。必要时先对奇数尺寸明确full回退并记录限制。
2. 将代表点合同接GI私有桥、Generate/native Cutout、Shade/Temporal/Spatial、Final history和NRD guides；尺度/代表策略变化必须reset。不得改共享DI像素语义或借用DI历史。保持raw与filtered可单独诊断。
3. 全分辨率重建使用当前full GB/SB的validity、深度和法线筛选低图邻居。优先重建demodulated diffuse，再按目标full receiver albedo仅乘一次；若保留已乘albedo输入，需证明跨材质边缘无颜色污染及黑材质安全。没有兼容样本的薄物体不能用背景填满，需明确定义保守缺样/补样策略并计成本。单纯bilinear不接纳。
4. 原5%逐RGB时间均值、非均匀彩墙/细节L1、薄边/前后景/孔洞漏光和运动首帧残色逐项测。半GI对full raw和独立几何/能量参考分别评分，不能只拿已有偏暗filtered作真值。古堡010质量失败独立保留，不靠半GI噪声减少抵消。
5. 所有新增full输出、低guide、映射与临时资源进入012同一清单；Total包含映射/重建而不是只报ray少了。新条件下恢复1080p及RG/compat、Player、正常enabled-camera验证，才谈实际预算和用户默认值。默认仍full。

每个有界实验独立review、实际报告、失败保留和过滤提交。若重建长期降低时间却丢失薄结构/能量，不以预算收益接纳画质失败；两轮无有效收益即按禁试文档搁置并写重开证据。

# Ho-RTDI 非零F0的RGB提案对照

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第49步尝试在保留动态可见性confidence的条件下减少输入彩灯噪声。结果证实全局RGB目标有收益和明确代价，未解决大尺寸快速/阶跃验收，不进入生产。

## 参考与隔离方式

核对用户6000.3.15的`Assets/Shaders/RayTracing/RtxdiApplicationBridge/RAB_LightSampling.hlsl`目标回调契约，以及固定SDK `DI/Reservoir.hlsl`的Stream/Finalize：提案允许近似贡献评分，归一化仍使用相同目标与inverse PDF。当前生产目标对量化零F0用RGB向量长度，非零F0用亮度；compute中的specular proposal系数4保持不变，最终物理BRDF/Shade不乘这个系数。

`RunCutoutResolutionSpecularRgbProposal/Compatibility`只在隔离工程生成Shader：复制本包compute/bridge，严格核对唯一的零F0策略表达式，把该限定改为所有F0都使用RGB目标；相对include改为本包逻辑路径，compute明确包含隔离bridge。临时RendererFeature的compute引用指向此副本，其余NRD、final visibility、材质和资源保持当前版本。没有改生产文件、增加产品参数或导入其他PT框架。

生成资产仅在`.codex-research`项目`Assets/HoRTDIProposalAudit`下；入口显式检查隔离路径，非预期bridge源码会拒绝生成。候选报告写`rtdi-specularproposal49-rg/compat.json`且proposal=rgb-all，不覆盖默认报告。分辨率/帧种子、192+256、8候选、CPU GGX/每帧遮挡、同世界区域3584点、raw3%/filtered5%/frame.35/step变化像素.35均与第46步一致。

## 两条正式候选结果

两条6组完全一致，仅2/6通过，qualityPass=false、退出1。平均raw与filtered红/蓝均通过，但rapid和两种尺寸step不通过。

| 尺寸 / 模式 | 默认frameL1 → RGB | 默认step变化像素L1 → RGB | RGB通过 |
| --- | --- | --- | --- |
| 256 / slow | .077365 → .070557 | — | 是 |
| 256 / rapid | .507913 → .470918 | — | 否 |
| 256 / step | .357448 → .322717 | .244762 → .459106 | 否 |
| 512 / slow | .068578 → .064005 | — | 是 |
| 512 / rapid | .539075 → .499755 | — | 否 |
| 512 / step | .346642 → .312456 | .225428 → .445930 | 否 |

第41步保留非零F0原策略的限制仍有效，不能因整体RGB噪声下降而直接推广。

## 阶段与置信度证据

`RunCutoutResolutionSpecularRgbProposalConfidence`在RenderGraph对256的slowTS/rapidTS/stepTS读取实际阶段与confidence，独立报告`rtdi-rgbproposalconfidence49-rg.json`，diagnosticOnly=true。三组仍保留正式动态visibility，不使用第48步confidence-off对照。

rapid原始RGB MSE从3.262382e-6降到1.589300e-6（约51%），Temporal从1.494918e-6降到7.547555e-7，最终从9.922330e-7降到6.583842e-7（约34%）；最终frameL1只从.507913降到.470918，仍不达标。平均confidence .852241→.859851，当帧变化点.339311→.367524；公共历史31、重投影1与镜面最大配置30不变。

step短期红通道误差在raw就从.231293升到.452006，Temporal从.266796升到.463147，发布从.244762升到.459106。这一局部误差包含当前采样噪声，不能全部归因于旧历史残影或只调NRD。CPU遮挡变化点和前三帧范围不变；全RGB目标改善总误差，却损失这一短窗口红通道表现。step平均confidence .964333→.965510，当帧变化点.101288→.114412，不能仅凭均值推断足够的响应能力。

## 复查与下一项

Roslyn errors0、隔离compute导入无新增Shader诊断，正式候选两条数值一致且失败出口正确，阶段读取/缩放/CPU参考检查通过。重新运行默认RenderGraph正式入口，仍原3/6、退出1、数值不变。生产Runtime/Shader/native/GI/Player无改动，未运行无关Player/GI构建；候选在当前最直接门槛已失败，不扩大接纳回归。

下一项应评估如何同时降低各颜色通道的瞬时方差，保留confidence与step/core门槛。先核对现有单reservoir/单最终样本的能力边界、alpha可见性和多样本成本，再考虑有明确收益的DI采样改动；不通过继续加初始候选数或放宽滤波门槛掩盖单最终样本的颜色权衡。古堡仍只用于资产支持审计，不能由本小测试替代场景GPU预算或最终材质合成。

第50步[第二最终样本隔离原型](Ho-RTDI-PairedFinalSamplePrototype.md)保留生产提案和confidence，在两条256/512镜面/漫反射及64 core中通过；每有效receiver增加一次native alpha射线，环境与多灯退化/部署/成本仍待检查，未进入生产。

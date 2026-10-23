# Ho-RTDI 动态最终可见性 confidence

> 状态：技术专题与接入证据。步骤中的测试数值及“下一项”保留当时语境；当前默认值、支持范围与剩余问题以 [Ho-RTDI 总文档](../../Ho-RTDI.md) 为入口。

第32步沿用[动态遮挡失败对照](../归档/Ho-RTDI-DynamicVisibility.md)的原能量、噪声和变化像素门槛。先实现真实历史采样重求，再保留NRD历史；没有关闭所有世界重置，没有修改古堡源资产或材质合成。

## 同采样梯度与调度

核对用户固定RTXDI FullSample `ComputeGradients.hlsl` 的同reservoir/light样本再求可见性，以及[NRD History Confidence](https://github.com/NVIDIA-RTX/NRD/blob/master/README.md#history-confidence)的同采样辐射变化契约。`VisibilityGradients` raygen复用原最终可见性shader、私有命中材质和4字节payload。上一帧实际Shade的reservoir、UV、权重、主表面和灯identity映射，在当前TLAS中重新求真实opaque/Cutout anyhit，与已存相同采样亮度比较。没有新RNG/当前reservoir采样差异混入梯度，不保存上一帧TLAS。

每个原1/3分辨率梯度格检查至多9个匹配历史像素，各lobe保留相对变化最大的有效信号；旧亮度为0也检查，避免漏掉遮挡转可见。主表面位置/法线匹配、灯映射失败和FP16亮度域显式处理，仍用原八次方转换与R8 confidence。原梯度、confidence、reservoir及采样亮度历史全部复用，没有新增运行纹理、buffer、Feature、native ABI。

四次原梯度模糊把局部变化扩散成大片低confidence，两次仍快速运动frame L1约.809。直接消费格内信号、由原confidence采样插值后，噪声和阶跃残影同时通过；静态compute梯度仍原四次处理。

仅在Cutout最终可见性路径中，pose/transform变化保留NRD历史并使用真实可见性confidence；ReSTIR仍以完整world revision拒绝可见性历史。新增 `HoRTScene.StructureRevision` 对实例/材质/掩码、源mesh替换和显式geometry dirty硬重置NRD。刚性/骨骼共同追踪原source mesh，避免同拓扑换源被误当作pose。无Cutout仍原world reset，单解析灯仍精确raw发布。

世界和灯参数稳定时走原compute捷径，跳过新增原生射线；历史可用且世界/灯变化时才发梯度。已有graph资源声明和相机历史继续沿用。

## 立体部署对照与最终profile

phi=.5/sigma=2候选通过平面运动，但实际Player实心Cutout立体caster连续运动蓝通道约.918，5%gate失败。仅改phi=1仍阶跃蓝约.923，未接受。隔离立体案例即时复制各NRD阶段GPU输出，按实际线性signal scale恢复物理单位；Far参考恒定，0输入像素也参与评分。

同phi=1，sigma=2阶跃蓝比例：Temporal .971260、HistoryClamping .960514、首Atrous .949187、最终 .967756；sigma=4约1.001048/1.000647/.987299/.998615。历史裁剪及反馈压低稀疏高亮蓝样本，不只空间边缘问题。跨Player/Editor随机历史不同，不把跨工程数值当作严格消融。

最终Cutout相机使用 diffusePhiLuminance=1、historyClampingColorBoxSigmaScale=4；无Cutout仍2/2。切换显式更新两项native设置，时间预算、五次Atrous、specularPhi等保持原值。sigma是RELAX共用设置，会影响两lobe裁剪；本步新增运动门槛主要验证漫反射，动态Cutout镜面仍应继续验证。没有亮度补偿，vendor/派生NRD算法/DLL未改。

## 验证和实际余量

原64²/896ROI八组真实红/蓝双灯：raw仍3%内；filtered各通道5%、mean L1<.15、frame L1<.35，正式qualityPass=true。

| 模式 | filtered红 | filtered蓝 | mean L1 | frame L1 |
| --- | ---: | ---: | ---: | ---: |
| 静态 | 1.045045 | 1.002270 | .076562 | .098270 |
| 相机TS | 1.005017 | 1.027981 | .080579 | .142012 |
| casterTS | 1.004122 | 1.018499 | .070784 | .127042 |
| 静态T | 1.035853 | 1.012828 | .090127 | .120521 |
| 静态S | 1.003726 | 1.026107 | .112014 | .126770 |
| 静态TS | 1.002010 | 1.022098 | .112378 | .134594 |
| 全透光 | .989645 | 1.013960 | .039568 | .065128 |
| 全透光TS | .969349 | 1.046886 | .068803 | .089051 |

新增慢速/快速/阶跃生产入口仍要求阶跃前三帧**变化像素红通道**L1<.35，恒亮蓝及未变化像素不稀释残影。两条硬件路径通过，frame L1约.128248/.213909/.179925，阶跃变化像素.209548；最终profile数值保存在对应JSON，不用早期phi=.5数据替代。

CPU原生梯度oracle读取真实历史reservoir权重，用独立平面射线/alpha及解析点灯计算新辐射，核对216格、169个可见性变化格，两条通过。gradient相对值容差.003包含解析/FP16误差；八次方导数上限8和R8量化传播到confidence容差.03，未放宽整体质量门槛。稳定帧跳过射线、pose保留NRD但拒绝ReSTIR temporal、实例增删和同拓扑skinned换源重置均验证。

Win64 Mono实际Player使用non-readable mesh/texture，末尾立体连续/阶跃运动以独立射线-AABB参考验证原3% raw/5% filtered/35%噪声及变化像素门槛。RG连续红/蓝.959989/.959887、frame L1 .157265；阶跃.968542/.950761、frame L1 .173242、变化像素 .109085。兼容连续.953063/.962718、frame L1 .164167；阶跃.967380/.969090、frame L1 .172591、变化像素 .113743，两者均pass=true；D3D11 inactive通过。部分能量接近5%边界，保留实际余量，不宣称普遍无偏。完整部署/必要共享GI/scene回归见持续记录第32步。

## 成本与下一项

有变化时最多每个有效匹配历史像素重求一次可见性，外加原最终Shade可见性；梯度网格仍1/3分辨率。静态世界/灯跳过新增射线，新增 `HoRTDI Visibility Gradients` GPU scope。不新增纹理内存，但dispatch和动态额外射线可能显著增加成本；尚无有效GPU毫秒预算，不能据小测试帧率估算。

第33步已补[指定GGX材料的动态Cutout镜面验证](../归档/Ho-RTDI-CutoutSpecular.md)，原八组与额外运动正式门槛通过，快速/阶跃噪声接近上限。第34步补[移动主表面起点修正与失败基线](../归档/Ho-RTDI-PrimaryMotion.md)：重求射线移到匹配当前表面，减少假自遮挡，完整diffuse能量gate仍失败。下一步验证真实运动输入、静态能量偏差及灯参数/身份，再评审成本和余量；不能推广为任意动态材质/环境或整座古堡。

入口：`RunCutoutMultiQuality`、`RunCutoutMotionProductionQuality`、`RunVisibilityConfidence`及各Compatibility；阶段诊断 `RunCutoutCubeStages/RunCutoutCubeOriginalStages/RunCutoutCubeSigmaFour`。报告为隔离工程 `rtdi-cutout32-current-rg/compat.*` 和 `rtdi-cutout32-motionProduction-rg/compat.*`。旧失败对照入口显式锁定旧参数/重置策略，仍可复现。

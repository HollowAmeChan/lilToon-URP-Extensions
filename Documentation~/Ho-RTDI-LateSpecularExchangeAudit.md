# Ho-RTDI 镜面后续Atrous固定交换对照

第57步针对第55步已定位的后续镜面能量变化，只做隔离NRD pipeline10对照。混合环境两条正式四组通过、原单样本同场景也通过；生产算法暂未改，仍需更广回归后接纳。

## 依据与隔离改动

核对固定NRD及用户fork `RenderingPlugin/External/NRD/Shaders/RELAX_Atrous.cs.hlsl`：specular将带权RGB和方差分别除以sumW与sumW²。当前DI diffuse已用固定Gaussian邻居系数、拒绝质量留在中心的形式，specular仍有逐像素归一化。

对照保持全部邻域、几何/normal/roughness/F0约束、pair-min variance带宽、luminance distance及参数，唯一替换specular输出归一化。中心原核系数为c=.44198²，邻居加权RGB/variance保留，中心系数改为`1-(sumW-c)`；variance按最终系数平方传播。没有用输出除以测得能量偏差、改灯强或放宽phi/迭代数。

隔离副本由当前派生body生成，保留source/license及绑定include；临时替换真实NRD shader数组的index10，校验main kernel=0，作用域退出恢复原shader。不增加pass/buffer/原生接口，也不修改upstream源码。随机step offset和部分几何/roughness权重仍不对称，因此不宣称严格全图能量守恒或普适无偏。

## 实际结果

`RunLateSpecularExchangeEnvironment/Compatibility`沿第54步CPU解析/GGX积分、512观察与raw/filtered各5%、非负门槛，两条完全一致、4/4、退出0。sky-only保持通过；mixed发布spec比例：

| 复用 | 原后续过滤红 / 绿 / 蓝 | 固定交换红 / 绿 / 蓝 |
| --- | --- | --- |
| none | 1.050745 / .984373 / 1.048828 | 1.025527 / .985077 / 1.017281 |
| T/S | 1.044358 / .983654 / 1.050032 | 1.022828 / .984289 / 1.020475 |

原始、diffuse与前面NRD阶段数值保持。`RunLateSpecularExchangeEnvironmentStages`物理单位/最终输出校验通过，首Atrous仍.992114/.981834或.989234/.985337，末轮变1.025533/1.017286或1.022837/1.020483；与发布差别只在FP16还原因子/量化容差内。

`RunLateSpecularExchangeEnvironmentBaseline`在原单样本同场景也4/4，mixed spec红/蓝none=1.028134/1.010951、T/S=1.025168/1.019505。不能用此能量通过替代单样本动态镜面噪声验收。

## 复查与下一项

报告`rtdi-auxenv54-partition-exchange-rg/compat.json`、`...baseline-exchange-rg.json`；阶段为`rtdi-auxenvstages55-partition-exchange-rg.json`。恢复原入口`RunAuxiliaryEnvironment`仍2/4、退出1、数值不变，证明对照未污染原版本。Roslyn errors0、无新增DI Shader诊断、GUID唯一/diff whitespace通过，生产Runtime/Shader/native/GI/Player未变。

下一项在可重建的DI私有派生中接入候选后，验证动态镜面/primary、曲面/材质边界、环境窄高光/小太阳、HDR、彩灯/core与实际Player；只在这些回归保持后接纳。该形式可能减弱噪声抑制，不能因一个环境case通过直接发布。三样本/opaque/动态CDF/多相机/GPU预算及最终产品接入仍待完成，不扩张NPR/PBR合成/PT。

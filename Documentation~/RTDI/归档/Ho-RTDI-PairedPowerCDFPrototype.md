# Ho-RTDI 辅助样本的RGB功率CDF

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第52步只在隔离原型中修复第51步大量弱灯导致的均匀辅助提案退化。32灯组恢复到门槛内，四组中3组通过；聚光灯快速组仍失败，未进入生产。

## 分布、索引与归一化

核对本地/用户局部光PDF契约和点/spot功率shaping。辅助权重为`max(实际RGB强度) × spot shaping`，point shaping=1；spot为平方cone积分`(1-innerCos+(innerCos-outerCos)/3)/2`。共同4π因子在归一化中抵消。使用RGB最大通道保留强红/蓝的平衡，避免直接复用亮度提案后重新引入颜色权衡；这是辅助全局功率代理，未声称空间/BRDF最优。

CDF按实际GPU灯列表的`previousLightKeys`当前快照构建。以double累计后存FP32，末项明确1，所有区间必须严格正；区间坍缩会拒绝该实验，不静默丢失来源。Native shader用独立RNG进行上界二分搜索，真实PDF为相邻FP32 CDF差值，辅助reservoir inverse PDF=1/PDF。主ReSTIR、物理BRDF、平均系数.5、两条当前可见性any-hit射线及旧primary缓存gradient保持第50步版本。

`RunPairedPowerCDFDomain/Compatibility`复用第51步四配置的参考/种子/门槛。每配置先由原shader渲染一次获取实际索引，再创建隔离CDF版本、复位历史/帧种子和预热。每观察帧核对灯索引次序；每16帧读取四个GPU辅助packed reservoir，检查valid/index与按真实CDF区间计算的inverse PDF。每配置64次、每条路径256次检查通过。

实验每配置增加一个临时structured float CDF buffer：2灯8 bytes、32灯128 bytes。数据上传后保持静态，最后GPU读取完成才释放，下一配置先恢复原shader再获取新灯表。跨配置覆盖2→32→2的集合变化；尚未实现产品中逐帧启停/调色/多相机重建。RenderGraph产品接入必须显式声明/绑定该资源，不能直接复制Editor对象参数方式。

## 两条正式结果

两条数值一致，qualityPass=false、退出1，保留剩余spotRapid失败。报告`rtdi-powercdf52-rg/compat.json`、auxiliaryPdf=rgb-power-cdf，与Uniform失败报告分开。

| 配置 | Uniform辅助 frameL1 | CDF辅助 frameL1 | CDF filtered红 / 蓝 | CDF变化像素L1 | CDF通过 |
| --- | --- | --- | --- | --- | --- |
| spotRapid | .360202 | .360202 | .985535 / .989369 | — | 否 |
| spotStep | .226086 | .226086 | .990500 / .982383 | .264976 | 是 |
| manyWeakRapid | .527315 | .344326 | .983309 / .991945 | — | 是，近门槛 |
| hdrRapid | .343575 | .343575 | .985697 / .990156 | — | 是，近门槛 |

32灯raw normalized MSE从7.764652降到.577339，filtered从1.087441降到.180342；原单样本baseline分别1.289396/.383570。CDF减少了Uniform稀有强灯的大权重，恢复能量与噪声，并未放宽raw3%、filtered5%、frame.35、step变化像素.35或HDR真实scale门槛。两灯同功率/相同cone分布的CDF恰好等价Uniform，因此spot/HDR结果不变。

## 复查与下一项

Roslyn Runtime/Editor errors0、无新增DI Shader错误/警告、GUID唯一/diff whitespace通过，两条实际索引/PDF检查有效。Uniform灯域RenderGraph回归仍第51步2/4、数值不变；生产Runtime/Shader/native/GI/Player未改，没有重复未变Player构建。

下一项在保留CDF与可见性响应的条件下检查额外独立辅助估计是否能给spotRapid足够余量，优先验证噪声收益与新增射线代价，不通过依赖当前随机样本的任意加权引入偏差。单个辅助样本与主ReSTIR的最优混合未知，直接用reservoir inverse PDF推断其精确选样概率做MIS也未经证明。环境MIS/opaque路径/动态CDF/多相机/实际Player/GPU预算仍须完成，不能把3/4当成完整DI接入。

第53步[第二独立辅助样本](Ho-RTDI-SecondAuxiliaryPrototype.md)四组均通过，spotRapid=.324357；实际新增第二辅助buffer与第三条最终射线，尚未进入生产。

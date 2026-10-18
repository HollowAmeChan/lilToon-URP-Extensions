# Ho-RTDI 零F0表面的RGB重要性

> 状态：技术专题与接入证据。步骤中的测试数值及“下一项”保留当时语境；当前默认值、支持范围与剩余问题以 [Ho-RTDI 总文档](../../Ho-RTDI.md) 为入口。

第41步在现有F0量化输入上限定RGB向量重要性，以改善彩灯颜色噪声并保留非零F0镜面路径。没有增加资源、射线、pass、Feature或native ABI；物理Shade、NRD参数和第40步RGB边界判定不变。

## 先拒绝预滤波，再核对噪声来源

在第40步RGB边界下重新测试首轮几何3×3线性预滤波：旧单候选无复用noise MSE .002776→.000299，但core最大.020757/.020997/.023142/.022987，四组均超过2%，已撤回。

新增`RunReuseNoiseStages/Compatibility`，8候选、同位置/分离红蓝灯、无复用/时间空间复用共四组；192预热/512观察central16×16。实际GPU阶段副本恢复本帧scale，逐像素统计RGB三通道MSE/时间方差与Rec.709亮度时间方差，避免空间照明梯度被算作噪声。Temporal/Clamping的alpha按second-moment spread读取，首Atrous按scalar variance读取；最后Atrous alpha是history length，明确不解释成variance。每阶段逐帧捕获、实际调度/复用状态和最终输出一致性均有断言。

同位置、无复用的基线Temporal RGB方差.01828579、亮度方差约1.6e-9；首Atrous RGB方差.00386015，最后.00385595，后续几乎不再抑制颜色噪声。Temporal alpha spread约1.25e-6、首Atrous约2.13e-7。alpha统计不是RGB协方差，也不是直接等同于滤波均值的不确定度；这些结果证实当前亮度统计未反映该信号的颜色波动。

## 采样改动及边界

统一RAB目标回调中，若量化后的`specularF0`三通道全为0，使用非负完整BRDF×辐射贡献的RGB向量长度；其他表面保持原亮度目标。initial/temporal/spatial/bias correction/gradient使用同一回调，SDK inverse-PDF归一化继续生效。零F0不是禁用镜面：已有Schlick项和完整BRDF响应仍参与目标/最终Shade。

核对用户`RAB_LightSampling.hlsl`目标权重契约、pinned SDK InitialSampling/Reservoir归一化、NRD Temporal/Smem的亮度moments。本步没有引入每通道reservoir，也未改变颜色/光强来补偿输出。严格零条件属于既有8-bit输入域，不新加材质参数；其他F0仍需后续单独验证，不能把本步结果推广为完整RGB误差模型。

## 实测收益与保留限制

两条原双灯八组及新16组正式通过。8候选的256帧双通道MSE：

| 灯位置 / 复用 | 第40步filtered MSE | 当前filtered MSE |
| --- | --- | --- |
| 同位置 / none | .005872 | .002550 |
| 同位置 / temporalSpatial | .003749 | .002297 |
| 分离 / none | .003930 | .002742 |
| 分离 / temporalSpatial | .003400 | .002486 |

同位置none raw MSE .083336→.050401，过滤MSE降低约57%；改目标同时增加亮度波动，不能把颜色改善称为所有信号都降方差。统一512帧阶段probe的最终RGB方差.00385595→.00166653；首Atrous后.00279843→最后.00166653，后续过滤开始降低颜色方差。两条阶段数值一致。

core最大四组.010407/.010699/.012265/.012363，raw红仍0，较第40步的1.7%～1.94%有更大余量。非零F0的原primary/GGX/动态镜面门槛、边界/环境/HDR/曲面通过。单候选且无复用的原32帧noise MSE仍.002776，本步未解决该配置的噪声。

新增`RunZeroF0Transition/Compatibility`将量化F0在0与1/255间每32帧切换，保持8候选、时间和空间复用开启，采用独立CPU diffuse参考；两条raw红/蓝.994014/1.001083、filtered .984434/.999019、frameL1 .185743，通过原3%/5%/.35门槛。该测试覆盖时间切换，不代表任意相邻材质边界或所有动画。

## 部署与下一项

两个Win64 Mono release errors0，实际D3D12两条全suite通过、D3D11 inactive通过。立体Cutout连续frameL1 .179656、阶跃.233885，filtered红/蓝1.012290/.990885、.987837/.991820；相对第40步整体误差与蓝光余量改善，但阶跃变化像素误差从RG .133722/compat .137003增加到.277329，仍过.35门槛，短期响应必须继续关注。单候选无复用Player MSE .000526维持，非零F0 primary spec起点仍通过。两条此轮数值相同。

阶段探针和F0切换仅属Editor测试。生成器/原始SHA及Roslyn检查通过，无新增DI shader诊断。第41步未编辑GI文件，但第42步核对发现共享include也继承了目标回调，已[编译隔离并做必要GI回归](../归档/Ho-RTDI-ProposalBoundary.md)。实际GPU成本尚未测量，零F0回调增加向量长度计算但不增加可见性射线或显存。

第42步[相邻策略边界](../归档/Ho-RTDI-ProposalBoundary.md)已扩大64/128验收并修复跨策略空间复用失败。后续仍需扩大材质、灯分布与更大尺寸，评审单候选及非零F0颜色误差如何改善；在新增RGB历史资源前先核对输入契约、容量和收益。继续保留core、能量、动态镜面与Player门槛，真实motion/GPU预算/整古堡质量仍未证明，不扩张最终合成或通用PT。

# Ho-RTDI：主/辅助贡献噪声分解

第101步在第100步真实生产分层基础上，确定mixed剩余噪声来自哪些贡献，不继续调整滤波常数。核对用户fork finalized reservoir逆PDF、生产env/analytic primary权重与辅助.25合同，直接读取GPUpacked主/第一辅助、8-byte第二辅助和native visibility。

128²原四组场景、192预热/512观察，固定x=52/60/68/76、y=64四点；每点分别做R/G/B/Rec.709亮度的时间Welford方差，再跨点平均。两辅助cov与primary/aux-sum cov同帧累积，按sample variance分母511。没有把空间照明梯度或不同探针均值混入方差。

局部specular由独立CPU GGX/光源/位置乘实际GPU finalized weight和可见性还原，再乘原合成权重。仅本fixture天空绿色、局部纯红/蓝，所以primary天空绿色直接来自raw.g；这一通道并非独立重算environment方向/BRDF。只对该fixture成立，不能用于任意彩色天空或绿色解析灯。

## 实际结果

两路径完全一致。mixed数据：

| 复用 | primary R方差 | primary G方差 | primary B方差 | primary亮度方差 | 两辅助合计方差 |
| --- | --- | --- | --- | --- | --- |
| none | 1.42632e-5 | .00205393 | 4.09404e-5 | .00103716 | 四通道均0 |
| TS | 1.42933e-5 | .00213615 | 4.12222e-5 | .00107972 | 四通道均0 |

分层平衡两灯，每个辅助仍波动，但两者负cov抵消：TS单辅助R方差2.10044e-7、两者cov=-2.10044e-7；B为1.96108e-7和同量负cov。不是各辅助都为常量，也不把此两灯结果外推任何CDF/灯排序。

primary与辅助合计cov约数值零，按 `Var(primary)+Var(auxSum)+2Cov` 重建raw方差，四通道最大相对差none .0660%、TS .0770%；逐帧RGB分解最大误差 .0830%/.0842%，在原.3%门槛内。剩余原始RGB波动几乎全部来自primary，绿色天空通道方差尤其大。亮度包含RGB协方差，不能把绿色加权方差除亮度方差直接称作可相加的百分比份额。

sky-only同样primary绿色波动 .00175081/.00182810，辅助0。样本数、权重与实际scalar数据保持。正式环境仍2/4，两个进程exit1；统计报告diagnosticOnly=true，读取成功不表示质量通过。

报告 `rtdi-componentnoise101-rg/compat.json` 在隔离工程；数组顺序R/G/B/luminance（报告writer已添加显式channel标签，不手改已有数值报告）。Roslyn errors0、GUID唯一、日志无新增shader warning/error或SourceAssetDB时间错误，diff通过。仅Editor/文档，无Runtime/GI/Player改动。

## 后续判断

继续增加局部辅助候选不能消除本观察中的主要波动。下一步检查primary环境样本的diffuse/specular混合方向分布及MIS贡献，先量化天空方向/BRDF响应与独立参考；保持local/sky路径归属、预算、物理shade、PDF和动态门槛。不按绿色硬分类滤波或直接清理输出，也不扩大通用PT/GI/最终NPR范围。距离lobe合同仍独立保留。

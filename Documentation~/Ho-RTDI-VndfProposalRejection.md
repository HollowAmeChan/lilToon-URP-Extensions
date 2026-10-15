# Ho-RTDI：相同预算VNDF BRDF候选拒绝

第103步依据102方向审计，检查混合diffuse/specular时把原四个BRDF候选转向VNDF是否改善镜面。核对用户fork RAB_Surface sampler/PDF共享diffuseProbability、固定SDK未命中/下表面零贡献与MIS分母合同，仅临时bridge改变该概率：两lobe都有效从.5改0，无spec仍1、纯metal仍0。

四个环境PDF候选、局部候选数、RIS最终target、实际shade、1/PDF、可见性、分层辅助、NRD保持。采样与PDF评估使用同一函数，下表面事件仍返回false，不强制重采样或条件归一化。没有新增ray预算、资源、用户设置或GI/PT。

## 原门槛实测

正交四组两路径一致，均2/4/exit1，raw3%通过，但mixed发布仍5%外：

| mixed复用 | 原filtered spec红/蓝 | VNDF候选 |
| --- | --- | --- |
| none | 1.050288 / 1.048948 | 1.047993 / 1.054176 |
| TS | 1.043948 / 1.050162 | 1.052991 / 1.053903 |

透视128²/FOV40也2/4/exit1，mixed none红/蓝1.082226/1.170676、TS1.099808/1.152047，raw与diffuse原门槛保持。未因部分颜色改善接纳，已在直接环境门槛失败，不追加不必要的Player或全动态矩阵；动态是否受益仍未证明。

## 方向/信号观察

四点512帧packed方向/weight/visibility读取实际执行，绿色独立重建误差≤.0923%，仍同等面积环境PDF；此入口正式环境失败/exit1，并非quality pass。

mixed none最终environment NoL>.9比例26.43%→30.16%，TS25.03%→25.29%。候选方向变了，但RIS target及重用继续影响最终分布，未变成全部near-normal。四探针 pooled绿色spread原none/TS .00205537/.00213341，候选 .00209389/.00213531，没有下降。pooled量混合点均值差异，不替代101逐点时间方差，也不当成方差置信区间。

运行时marker/固定NRD没有改动。四实际进程均exit1，Roslyn errors0、GUID唯一、日志无新增shader warning/error或SourceAssetDB时间错误、diff通过。报告 `rtdi-runtime78-env-baseline[-perspective]-vndf103-*`、`rtdi-envdirection103-vndf-rg.json` 位于隔离工程；原方向102报告未覆盖。

## 取舍

不生产化。单改BRDF候选分布不足，不能继续通过提高候选数、调整概率常数或放宽5%门槛压线。下一项转向受主样本天空波动影响的specular空间过滤range guide：先核对当前RGB/亮度guide是否随着每轮过滤改变而产生重复搬移，并用稳定阶段guide作独立控制；必须保留原moment/variance、动态响应与shape门槛，新增临时资源成本明确，未证明前不改产品。距离lobe合同仍保留，最终NPR不扩展。

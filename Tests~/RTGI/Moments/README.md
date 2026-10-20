# 步骤015：实际古堡低信号二阶矩

本步发现真实slow moment精度问题，但没有解决全部RGB能量失败；生产默认SignalScale仍为64，原版NRD/vendor/Shader/native参数和物理raw不改。新增Editor-only单位探针在NRD前除以scale、后乘同一scale，单位改变时清独立NRD历史；Player getter仍返回原常量64。

三个真实D3D12 Editor进程：baseline64 PID42056、scale1 PID46844、scale.125 PID37392，均256额外预热/256观察、同stride4、每阶段1,048,576样本，**实际exit1，原5% RGB门槛全部仍失败**。三个run的256条raw逐帧RGB均值完全相同，证明没有把修改采样/物理能量当修复。

| 单位scale（输入除／输出乘） | Temporal正均值但moment=0 | Clamping同指标 | Temporal明显低于mean²的样本* | RGB filtered/raw误差 |
| --- | ---: | ---: | ---: | --- |
| 原64 | 98.458% | 98.519% | 188,211 | −5.946%, −6.535%, −7.028% |
| 1，实验 | .704% | .729% | 69,685 | −6.220%, −5.317%, −4.699% |
| .125，实验 | .00315% | .00315% | 90 | −6.167%, −5.252%, −4.603% |

*明显亏损判据为`m2 + .5×2^-24 < .99×luma²`，把一次最小FP16 subnormal舍入量留作余量；不是把所有轻微负variance叫错误。每阶段样本均1,048,576。三组该网格未观察到moment饱和，不扩称HDR全域安全。

基线Temporal的平均物理m2约7.208e−5，平均同像素mean²约8.624e−5，存在不合理亏损；同样信号换为单位1后m2约2.320e−4、mean²约8.638e−5。局部记录包含frame/pixel、实际NRD input、slow RGB/m2、物理raw/filtered。不是只按FP16理论推断，也未把全finite当正确。filtered时间方差由2.307e−8降到约1.484e−8，仍不掩盖红/绿5%失败。

因换单位还会改变固定epsilon相对大小、RGBA16舍入等，不能声称moment是全部或唯一根因。两个单位实验已完成；不再盲试.0625/.015625或按通道调尺度。更宽sigma、盲history、补gain仍禁止；如后续改生产信号域，需连同HDR/动态/历史单位与完整质量验证。

## slow/fast口径更正与真实DLL确认

初读HistoryFix正文看到float4，误把其gOut_Diff alpha一律记作slow moment。基线保留了pipeline5原始数据和这一错误标签，**比较明确排除5**。实际运行的CPU描述符探针PID16272 exit0，绑定如下：

- Temporal gOut_Diff→transient0，gOut_DiffFast→transient1。
- HistoryFix读transient0、条件写transient1；稳定历史early-out后保留fast alpha=0。
- Clamping slow读transient0、fast读transient1，slow输出permanent0、fast输出permanent1。

所以真正无条件slow moment只采4/6；PrePass alpha距离、HistoryFix条件fast、A-trous variance全部不能混用。`baseline-interpretation.json`保留更正，原baseline报告未改写。源body及实际DLL资源映射共同证明口径；DLL只记录SHA，无新二进制。

scale1的Unity报告真实完成，但初runner用错误marker字符串`HoGICastle`没匹配失败异常，脚本也exit1。`scale1-original/run.json`保留marker=false，verifier根据实际原异常、fresh报告、audit完整性和退出核对，没有偷偷改成绿色。后续runner修marker后执行第三组与绑定探针。四份`*-original`保留各时刻精确源码；当前root runner只运行小尺寸绑定探针，所有历史报告/源码仍独立。

## Review和下一步

接纳moment/绑定取证能力与Editor诊断钩子，不接纳生产单位变更或声称古堡质量通过。没有半GI代码、DI派生、native或额外GPU资源变化；没有新增Player模式，未将本步Editor实验称为Player回归。

`verify_validation.py`核对真实进程退出/log/report/source、实际pool别名、三组逐帧raw相同、样本/分桶/局部物理单位、原5%失败和默认64。旧012/014验证器当前源SHA已因合法诊断变化而过时，按各自commit核对，不回滚本步去迎合旧SHA。

下一步补同窗完整图像RGB时间均值、16种stride4相位和局部raw→阶段→filtered误差，区分原取样网格与全图能量/空间细节问题；原stride4与5%失败继续保留，不能挑方便相位或用新统计静默替代原门槛。再决定是否沿DI已验证的颜色裁剪/信号域做有界候选。010/012、正常相机及完整Player预算仍未完成。

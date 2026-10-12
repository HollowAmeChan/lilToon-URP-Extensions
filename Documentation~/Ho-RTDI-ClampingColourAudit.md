# Ho-RTDI 稳定混合镜面的Clamping改色比例

第67步直接读取Temporal normal/fast和Clamping normal/fast，量化第65/66步静态混合场景的colour改变。生产未改，不能把本静态结果推广成所有动态帧可使用normal预算。

## 实际读取与阈值

`RunAuxiliaryClampingBaseline`读原单样本；`RunAuxiliaryClamping/Compatibility`读已修环境归属的三样本。128²、1024点、192预热/512观察、同参考/种子、固定60Hz，normal/fast配置30/6。

fast输出使用NRD dispatch的实际resource描述：Temporal4和Clamping6分别寻找自己的`gOut_SpecFast`，在对应dispatch后立即复制，不能把Temporal临时pool在帧结束时当Clamping输出。初次错误要求permanent pool被已有guard拒绝；之后按各pass descriptor修正读取。fast alpha不作为moment使用。

物理RGB沿uniform灰F0/正交场景恢复，变化量为三通道L1除该点完整CPU spec参考。阈值.0003用于排除FP16量化末位，记录阈值以上比例、全部观测平均变化、最大变化，以及normal与fast前后距离；它是诊断阈值，不是降低原质量门槛。每配置524288个像素/帧观测。

## 实测结果

| 版本 / 复用 | normal改变比例 | normal平均变化 | fast改变比例 | normal-fast平均距离前 → 后 |
| --- | --- | --- | --- | --- |
| baseline / none | .000025 | 7.396430e-7 | .000099 | .240239 → .240237 |
| baseline / T/S | .000036 | 7.853775e-7 | .000113 | .247150 → .247148 |
| 三样本 / none | .000025 | 3.497078e-7 | .000021 | .207316 → .207316 |
| 三样本 / T/S | .000031 | 2.751670e-7 | .000029 | .213850 → .213850 |

三样本分别13/16个normal观测超过阈值，最大归一化变化约.056635/.027270。当前稳定输入的大多数normal colour未被Clamping明显替换，但fast和normal确实是不同的积累结果。没有统计动态caster/场景启停，也没有证明30帧就是实际独立样本数；第66步动态失败继续是接纳约束。

## 复查与下一项

报告`rtdi-auxclamp67-<baseline/partition>-<none/reuse>-rg/compat.json`与独立stage文件，diagnosticOnly=true。三样本两条指标完全一致，阶段/最终RGB比例保持第55步，原stage入口回归有效；Roslyn errors0、GUID唯一/diff whitespace通过，无新增DI Shader诊断，Runtime/native/GI/Player未变。

后续需要在动态响应中读取相同normal/fast/change，而非继续全局替换预算；同时应审视DI采样原型的实际接入、opaque/动态CDF/多相机等未完成项，不因静态统计优先级高而将项目范围缩成NRD曲线调试。保持已有非负修复、局部高光/动态/Player门槛及DI范围。

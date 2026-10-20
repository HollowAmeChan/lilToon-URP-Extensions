# 步骤016：全图／16相位偏差与被拒绝的裁剪候选

新增Editor CPU审计读取同次GPU full256²数组，不增加产品GPU pass、修改取样或替换旧门槛。每组仍32初预热+256额外预热+256观察；保留旧stride4相位(0,0)，并计全部65,536 receiver、16相位、每像素时间均值/方差与同像素stage RGB。full每组16,777,216 receiver samples，16相位各1,048,576。

实际 baseline scale64 PID56596、已测scale.125 PID8564、凸裁剪候选PID37456均**真实exit1**，编译诊断0、无fence错误。三组256条full raw逐帧均值完全相同；默认单位64、Shader/vendor/DI/native与生产链不改。

| 同窗filtered/raw误差 | 原64 | 单位.125诊断 | 凸裁剪候选（拒绝） |
| --- | --- | --- | --- |
| 原相位(0,0) RGB | −5.95/−6.54/−7.03% | −6.17/−5.25/−4.60% | −6.91/−5.79/−4.94% |
| **全图RGB** | **−7.25/−8.42/−8.78%** | **−7.56/−7.34/−6.55%** | **−8.31/−7.89/−6.91%** |
| 全图time-mean normalized L1 | .19121 | .21131 | .21378 |
| 全图平均时间方差 filtered | 2.788e−8 | 1.774e−8 | 1.775e−8 |
| Clamping负分量计数 | 377 | 352 | 4 |

每组16相位均未全过原RGB5%门槛。不是原相位造成的假阳性，不能挑较好相位、只报蓝通道或用全图统计静默替代原失败。负分量这里只计数量，未把数量改善冒称可见画质收益。

原64全图红均值：raw .00154717、NRD input还原 .00154716、PrePass .00153321、Temporal slow .00146992、fast/条件Fix .00152301、Clamping slow .00146839、首Atrous .00145612、末Atrous .00143554、published .00143504。HistoryFix只标RGB fast/条件fix，不再误当slow moment；Atrous只读RGB。

最强局部损失位于(87,165)：raw红时间均值.02472，input/PrePass相近，Temporal约.01186，最终约.00363～.00372。该点raw时间方差约.00257，远高于filtered约3e−5。数据证明压低发生的环节和位置，**还没有独立高样本真值，不能仅凭有限窗raw就把这些高波动点的全部差异解释成真实物理能量损失**。下一步用同支持域高样本无复用一跳参考验证这一点，原失败继续保留。

## 同box线段裁剪候选

参考DI第82～84步和`Editor/RTDI/HoRTDIDiffuseSegmentValidation.cs`的scope做法，从原GI vendor HistoryClamping只替换一个diffuse轴向clip块：求fast→slow线段与同一YCoCg box交点、用一个标量插值。没有复制DI其它private策略；box/sigma/预算、acceleration/reset、moment correction和资源布局不改。单位固定为已测.125，与原.125同窗比较。

新shader只生成在自有GI工程Assets/HoRTGIClampingCandidate；仅当前GI core的shaders[6]临时替换，main kernel=0、15项库/消息均核对，finally恢复原对象。报告`clampingCandidate.restoredOriginal=true`。候选减少负分量数量，却使全图RGB三个通道、旧相位与L1更差，review拒绝并按F36搁置，不因为DI已成功就产品化GI。

候选Editor入口/helper已移出产品；精确源、生成body/wrapper和DI参考体保留在`convex-original`／`candidate`，source/生成SHA绑定报告。最终Castle恢复到两组全图审计版本，`candidate-review.json`记录；只有full-domain审计代码保留。过滤后Unity编译PID41164 actual exit0、C#0/fence0，未冒称Player或完整渲染回归。

`verify_validation.py`核对三个真实失败、全图与所有相位计数/均值守恒、原相位一致、三组raw逐帧相同、局部/阶段、候选单块/生成SHA/恢复、过滤后源码和编译。DLL只hash，未新加二进制。`run_validation.ps1`当前只跑原64/.125两个审计；先归档再重跑，不覆盖历史。不同时间源在baseline-original／convex-original逐字节保存。

下一项先看现有Reference与本地RTX一跳采样，再对热点和安静控制点做高样本无复用参考、批次收敛及primary/material/light域一致性验证。只在同一跳漫反射域，不做通用PT/多跳；参考子集是诊断，不能当完整场景验收。F27/F31/F33/F35仍有效。

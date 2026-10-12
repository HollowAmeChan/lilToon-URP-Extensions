# Ho-RTDI 混合天空/解析灯的NRD阶段定位

第55步只增加Editor捕获，沿第54步失败场景定位负漫反射与镜面能量偏差，没有修改生产滤波或放宽门槛。

## 捕获与物理单位

`RunAuxiliaryEnvironmentStagesBaseline`读取原运行时，`RunAuxiliaryEnvironmentStages/Compatibility`读取已修正贡献归属的三样本原型。仅mixed，分别关闭/开启ReSTIR T/S；128²、中心32×32、192预热/512观察、固定60Hz、同帧种子/独立CPU参考。阶段为Temporal4、Clamping6、首Atrous9、末Atrous10，另有raw=-1和发布=-2；每帧必须捕获全部阶段。

每lobe的4/6/9各复制512次，10每帧四轮，累计2048且读最后一轮。diffuse按HDR逆scale恢复，spec按固定均匀灰F0/roughness、正交场景的raw/归一化输入比例恢复材质因子与scale。末阶段在全部观测点检查RGB与发布相符（参考×.003），不把去调制信号、alpha或复制数量误作物理照明/方差/GPU时间。比例还原不外推任意材质/透视场景。

## 负值首次出现在漫反射Clamping

原运行时Temporal非负，Clamping首次产生明显负值，后续仍有残留：

| 复用 | Temporal min | Clamping min / 负通道观测数 | 首Atrous min | 末Atrous/发布 min |
| --- | --- | --- | --- | --- |
| none | 0 | -1.098633 / 2973 | -.263916 | -.199707 |
| T/S | 0 | -1.002930 / 3866 | -.228149 | -.193726 |

这是物理RGB通道值，不能按signed colour-space系数解释。原始/Temporal非负排除了本测试中的前端负输入；阶段6对应YCoCg colour box与history acceleration/reset，需要在该环节恢复非负物理域并使second moment与实际颜色一致。发布端截零不能证明历史/方差已正确。

三样本原型none没有负值，T/S Clamping仍有2次负通道观测、min=-.002018，后续消失。增加样本减少该现象不等同于根治clamping约束。

## 镜面偏差主要在后续Atrous

修正原型的解析红/蓝能量比例：

| 复用 | raw | Temporal | Clamping | 首Atrous | 末Atrous | 发布 |
| --- | --- | --- | --- | --- | --- | --- |
| none | 1.003404 / .994022 | .992410 / .982411 | .992410 / .982411 | .992114 / .981834 | 1.050748 / 1.048830 | 1.050745 / 1.048828 |
| T/S | .997004 / .996772 | .988856 / .983759 | .988856 / .983759 | .989234 / .985337 | 1.044366 / 1.050035 | 1.044358 / 1.050032 |

原运行时也从首Atrous红/蓝约.996/.974或.990/.981，变到最终约1.053/1.044或1.047/1.051。两版本镜面阶段均非负；该偏差与漫反射负值是两个需要独立验证的问题，不用一个发布clamp或CDF修改代表全部修复。

## 复查与下一项

baseline/partition报告为`rtdi-auxenvstages55-<mode>-rg/compat.json`，各24条记录，明确diagnosticOnly=true。baseline此次实测RG，partition两条完全一致且捕获/物理单位检查退出0；正式第54步RG回归仍2/4、退出1、数值不变。Roslyn errors0、GUID唯一、无新增DI Shader诊断/diff whitespace通过，Runtime/Shader/native/GI/Player无变化。

下一项先修复DI私有Clamping中非负物理RGB约束及moment一致性，保留upstream源码/可重建派生与core/彩灯/环境/动态门槛，再单独处理镜面后续滤波权重/能量。opaque、动态CDF、Player、GPU预算仍待完成，不扩张材质合成/PT。

第56步[非负历史修复](Ho-RTDI-NonnegativeDiffuseHistory.md)已在moment更新前约束normal/fast diffuse RGB，观测负值消失，Editor与实际Player回归通过；镜面偏差继续保留。

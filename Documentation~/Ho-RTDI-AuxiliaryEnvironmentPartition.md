# Ho-RTDI 辅助估计与天空贡献归属

第54步检查仅采解析灯的辅助估计与环境混合。隔离原型修复了sky-only错误归零和混合天空被减半的原始能量问题；混合场景镜面滤波仍略超5%，正式失败保留，生产未变。

## 参考与正确权重

核对用户ShadeSamples的所选光源与inverse PDF输出、固定SDK环境/BRDF MIS，以及当前`DispatchVisibilityRays`。当前native已绑定Ho-Atmosphere参数/辐射与环境PDF，没有漏绑；不能据猜测改输入设施。

主ReSTIR估计覆盖解析灯+天空，辅助CDF只覆盖解析灯。因此旧`.5主 + .25辅助1 + .25辅助2`会将主估计中唯一的天空贡献减半。修正按主样本光源类型加权：天空主样本权重1、解析主样本权重.5，两辅助仍各.25。按贡献域的线性期望，天空保留一份，解析保持`.5主解析 + .5辅助解析均值`。不是根据当前亮度取权，也没有用随机reservoir inverse PDF去猜其真实概率做新MIS。

旧native原型把所有工作包在“解析灯数>0”内，导致无解析灯时天空主样本也未追可见性。修正只把辅助生成包在该条件内，天空主样本仍正常查询；sky-only辅助写空reservoir，绑定dummy CDF但不访问。既有primary缓存gradient、物理BRDF与NRD保持不变。

## 独立输入与判定

`RunAuxiliaryEnvironmentBaseline/Compatibility`为原运行时；`RunAuxiliaryEnvironmentNaive`复现第53步直接平均；`RunAuxiliaryEnvironment/Compatibility`应用隔离贡献分配。128²正交固定平面、F0=10/255/roughness=102/255，Ho-Atmosphere常量绿色天空，两解析灯为红/蓝。alpha物体全透明但仍保留Cutout分类，保证native路径启用且整个天空无额外遮挡。它验证贡献归属与透明any-hit，不代表动态/有色Cubemap遮挡。

sky-only/mixed各关闭与开启ReSTIR T/S，共四组；8初始、4天空+4BRDF候选，192预热/512观察、中心32×32点。CPU解析响应独立计算；天空diffuse参考为绿色1，specular用既有double自适应Smith GGX积分。逐通道raw/filtered各5%、不存在通道绝对值<.001、有限且非负不变。负输出记录最小值并让case失败，继续保存完整报告，未 clamp 或删除坏样本。

## 实际结果

原运行时sky-only两组通过，mixed两组失败：filtered spec红/蓝约1.052769/1.043540（无复用）、1.047027/1.050594（T/S），并出现最小输出约-.199707/-.193726，尚待阶段定位。首轮硬中断已改为完整失败记录，非有限值仍立即拒绝。

Naive原型0/4：sky-only全部0，mixed天空raw diffuse/spec约.50；这证明两项原型错误。其解析灯能量正常不能替代天空判定。

修正两条一致、2/4，qualityPass=false、退出1：

| 场景 / 复用 | raw diffuse绿 | raw spec绿 | filtered spec红 / 绿 / 蓝 | 通过 |
| --- | --- | --- | --- | --- |
| sky-only / none | .999097 | .998350 | 0 / .984903 / 0 | 是 |
| sky-only / T/S | 1.000488 | .989264 | 0 / .979921 / 0 | 是 |
| mixed / none | .999099 | .999527 | 1.050745 / .984373 / 1.048828 | 否 |
| mixed / T/S | 1.000183 | .999481 | 1.044358 / .983654 / 1.050032 | 否 |

修正mixed解析raw spec红/蓝约1.003404/.994022、.997004/.996772，全部raw恢复到5%内；最小输出0，未复现原运行时负值，但filtered有真实边界失败，不放宽或四舍五入标绿。

## 复查与下一项

报告分别为`rtdi-auxenv54-baseline/naive/partition-rg/compat.json`，此次baseline/naive实测RenderGraph，partition两条实测。修正后的两条四组数值一致，原第53步解析四组回归4/4且数值不变。Roslyn errors0，无新增DI Shader诊断，GUID已修为32位且唯一，diff whitespace通过；Runtime/Shader/native/GI/Player未改，不重建未变Player。

下一项用现有NRD阶段捕获定位mixed镜面能量变化与原运行时负输出，再选择必要修复；不能仅在发布端强制截零就称输入/滤波正确。opaque路径、动态CDF/多相机、实际Player、GPU时间与真实Unity天空仍待扩大验证。保持只做DI与直接读取既有输入，不扩张材质合成、AC副本或通用PT。

第55步[阶段定位](Ho-RTDI-MixedEnvironmentStages.md)确认漫反射在Clamping首次转负，镜面偏差主要出现在后续Atrous；两项分开修复，正式2/4未改变。

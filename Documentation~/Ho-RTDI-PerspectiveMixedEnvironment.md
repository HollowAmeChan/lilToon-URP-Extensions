# Ho-RTDI 透视混合天空/解析灯对照

第62步将既有混合环境门槛扩大到透视相机，确认后续镜面偏差不是正交特有。没有改生产Shader或降低门槛。

## 独立参考

`RunPerspectiveAuxiliaryEnvironmentBaseline/Compatibility`为原单样本，`RunPerspectiveAuxiliaryEnvironment/Compatibility`为已修贡献归属的三样本原型。128²、相机z=-3、FOV40、固定平面，Ho-Atmosphere绿色常量天空、红/蓝点灯、全透明但保留native Cutout分类。sky-only/mixed各无复用与T/S，192预热/512观察中心32×32。

解析参考用逐像素平面交点、实际V=(camera-position)方向、距离衰减和独立Smith GGX。天空spec用现有double H-space/反射Jacobian积分，随各像素NdotV变化，NdotV按1e-6量化缓存；不把正交常量响应直接套到透视。天空diffuse参考仍绿色1。仅参考已做视线修正，没有改材质或以GPU均值拟合参考。

保持逐通道raw/filtered5%、非负和有限门槛。JSON额外记录projection=perspective，报告与正交分开。

## 两条实际结果

原单样本与三样本的两条路径各完全一致、sky-only两组通过，mixed两组失败，整体2/4、qualityPass=false、退出1。

| mixed复用 / 版本 | raw spec红 / 绿 / 蓝 | filtered spec红 / 绿 / 蓝 |
| --- | --- | --- |
| none / baseline | 1.007283 / .999450 / .989502 | 1.080427 / .983456 / 1.155045 |
| T/S / baseline | .993900 / 1.002205 / 1.013675 | 1.089021 / .984988 / 1.188413 |
| none / 三样本 | 1.003414 / .999450 / .995504 | 1.078953 / .983448 / 1.161082 |
| T/S / 三样本 | .996724 / 1.002205 / 1.007593 | 1.089771 / .984991 / 1.181745 |

raw能量保持5%内、各观测min=0；diffuse也在门槛内。实际滤波解析蓝高出约15～19%，比正交对照更明显。它不证明整个图增加相同能量：当前评分是中心ROI，透视下BRDF响应/高光梯度更强，必须区分邻域能量搬移与全接收面偏差。

## 复查与下一项

报告`rtdi-auxenv54-baseline/partition-perspective-rg/compat.json`，原正交入口回归仍2/4且原数值保持；Roslyn errors0、无新增DI Shader诊断/diff whitespace通过，Runtime/native/GI/Player未变。

下一项同时评分整个有效平面及嵌套空间区域，读取独立逐像素参考梯度/过滤前后积分，确认中心偏差来自多少空间搬移、多少权重问题，再选择必要的过滤/guide改动。不将正交表达或仅看一个ROI的“能量校正”推广成场景修复；保持动态噪声和既有门槛，目标仍是透视多灯DI。

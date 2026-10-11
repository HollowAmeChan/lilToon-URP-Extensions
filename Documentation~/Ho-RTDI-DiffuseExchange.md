# Ho-RTDI 漫反射方差传播与后续空间权重

第37步修正[第36步定位的彩灯偏差](Ho-RTDI-PrimaryFilteringAudit.md)，沿用原raw3%、filtered各通道5%、噪声/形状/残影gate。ReSTIR目标、Shade、光强、五次RELAX调度、历史预算、phi/sigma、资源/native ABI均不变。原始NRD源码未改，变更只在已有DI派生Atrous及其可重建生成器。

## 原因、变更和适用范围

核对pinned NRD `RELAX_AtrousSmem/Atrous`、用户NRD集成与本地已采用的specular方差处理。首轮原diffuse把邻域二阶矩平均后减混合均值平方，真实空间照明变化也进入后续的方差；后续使用中心方差设置range权重，再按每个像素不同的接受权重和归一化，明暗/颜色对照出现不对称混合。

最终保留三项改动：

- 首轮RGB仍原过滤，diffuse方差按各样本temporal moment spread以w²传播、除sumW²；不额外加入不同邻域均值之间的对比方差。没有增加不可靠的新有效历史样本数。
- 后续diffuse range bandwidth取两端非负方差的较小值，与现有specular pair策略一致；精确端点也能阻止对比被模糊。
- 后续diffuse使用固定Gaussian交换系数，未接受的邻域系数留在中心；不再用接受权重和重新放大所有系数。RGB与variance分别用系数/系数平方，最后一轮仍按SDK写history length。

3×3权重核的中心系数为.44198²、邻域base系数来自.44198/.27901；geometry/normal/material/range继续原门槛，原sample位置/offset不变。对有效非负输入是凸组合、恒定输入保持；随机offset和几何资格未必互惠，因此不声称任意区域/全图严格守恒或完全无偏。

只改后续权重会改善static但移动能量仍失败；把方差除历史数的候选增加噪声或仍失败，已撤去。最终首轮保留原range权重，仅修方差传播；specular代码未改变，GI不使用这些DI专用派生shader。

## 主表面完整门槛

两条D3D12路径正式qualityPass=true、originPass=true，raw每case与原基线一致。64² central256、192预热/256观察，F0=10/255、roughness=102/255。

| diffuse模式 | 原filtered红/蓝 | 当前filtered红/蓝 | 原/当前frame L1 |
| --- | --- | --- | --- |
| static | .946252/1.086598 | .983276/1.016998 | .094117/.083512 |
| smoothZ | .940400/1.108769 | .976124/1.041844 | .093396/.075203 |
| stepZ | .938281/1.119596 | .978639/1.043692 | .105428/.086434 |

原三组specular输出一致。原Cutout diffuse八组、指定GGX八组及额外specular运动两条正式gate通过；快速/阶跃镜面frame L1仍约.343487/.347752、变化像素.309657，原近上限余量并未解决。

## 噪声取舍与部署

旧opaque noise raw MSE .050401保持，filtered MSE .000183→.001333，仍通过原filtered<raw×.3条件，但绝对噪声约高7倍。本步接受的是完整能量gate修正与约97%原方差抑制的取舍，不能称降噪全面改善；噪声和复用相关性仍是后续改进项。不是通过抬亮/压暗补偿能量。

材质颜色/F0/金属、两像素边界、薄阴影/不等深度、HDR范围、环境镜面/MIS、曲面回归通过，无新增DI shader诊断。Roslyn及原始SHA/派生check通过。生成器精确检查每个替换布局，原始copyright/许可证保留。

两个Win64 Mono release errors0，实际D3D12 EXE原suite及non-readable Cutout连续/阶跃/主表面起点回归通过。RG连续红/蓝1.004425/.978847、frame L1 .206815，阶跃.989556/.961234、frame L1 .233400、变化像素.125998；compat连续.991852/.981208、frame L1 .207349，阶跃.987203/.981804、frame L1 .235467、变化像素.128844。Player动态漫反射噪声也高于旧版本(.157～.173)，仍在原门槛内，保留实际差异。D3D11 inactive通过。

## 复现和下一项

`RunPrimaryMotionQuality/Compatibility` 现验证完整能量；`RunCutoutMultiQuality`、`RunCutoutSpecularQuality`、`RunCutoutSpecularMotionQuality`及各Compatibility保留原gate。标准Player启动验部署；原源motion专用probe仍独立失败，不混同。

第38步的[复用/颜色噪声对照](Ho-RTDI-ReuseNoise.md)已发现额外分离灯spatial-only能量失败；第39步[双灯阴影core检查](Ho-RTDI-CutoutShadowCore.md)加入此前未覆盖的局部2%门槛，生产四组正式失败。这里保留第37步旧门槛的通过证据，不代表新范围通过。后续需同时解决局部过滤残留和颜色噪声；已有结果不是整座古堡质量/真实motion/GPU预算验收。用户原古堡资产、AC/合成、lilPBR/SSS和通用PT框架未扩张。

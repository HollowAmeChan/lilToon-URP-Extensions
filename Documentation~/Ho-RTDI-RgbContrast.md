# Ho-RTDI 后续Atrous的RGB差判定

第40步修复第39步的双灯阴影core及第38步分离彩灯spatial-only能量失败。实际D3D12两条Player验收通过，同时保留增噪取舍与较小的门槛余量。运行时只改现有DI派生Atrous的一项range距离；没有新资源、pass、射线、Feature、native ABI或NRD参数。

## 阶段定位和改动依据

新增`RunCutoutShadowStages/Compatibility`沿用独立CPU 5×5阴影mask、168 core像素、192预热/512观察，四种固定复用。现有Editor阶段回调在GPU dispatch后立即复制Temporal(4)、Clamping(6)、首Atrous(9)、最后Atrous(10)输出，按本帧GPU scale恢复单位。每个阶段必须每帧捕获，未遮挡Far蓝光和实际RELAX/时间复用状态必须有效；最后阶段与published输出逐像素匹配，容差为未遮挡参考的.3%。JSON中raw/published用负ID(-1/-2)，与native pipeline索引区分。

基线Temporal、Clamping、首Atrous的core红光最大/平均值均为0；后续Atrous开始混入红光，四组最大均值.023244/.023598/.026148/.025879，与published一致。由此优先改后续空间判定，未改可见性、ray bias、首轮预滤波或历史裁剪。

原range距离为`abs(L(A)-L(B))`，相反方向的RGB变化可能抵消。改为`L(abs(A-B))`，即Rec.709正权重下的逐通道绝对差之和。它不小于原距离，相同颜色比例且同向变化时相同；保留pair min variance、phi、截断/relaxation、固定Gaussian交换及variance传播。此处bandwidth仍由亮度moment提供，不声称已获得完整RGB协方差模型或任意场景无偏。

核对pinned NRD Atrous/HistoryClamping及用户参考工程RAB/NRD设置。原NRD HistoryClamping已用`Color::Luminance(abs(...))`度量history acceleration的颜色距离，本步将这一距离用于本应用的后续diffuse range，不声称是SDK默认行为。物理Shade、RAB提案、五次过滤、specular代码、vendor和许可证保持。

## 完整门槛结果

两条正式双灯八组qualityPass=true，raw core红光为0，原能量/形状/逐帧/动态门槛保留：

| 固定模式 | 基线core最大均值 | 当前core最大均值 |
| --- | --- | --- |
| static | .023282 | .017198 |
| staticT | .024576 | .017848 |
| staticS | .026633 | .019375 |
| staticTS | .025992 | .019200 |

统一种子的阶段probe最终最大.017195/.017891/.019436/.019488，两条一致；早期三阶段仍0。最高值距2%只余约.0005，不代表任意分辨率、灯或材质都达标。

第38步新16组两条正式qualityPass=true，原分离8候选spatial-only红/蓝.972318/1.051984→.988017/1.004080。原primary完整两条通过：static/smoothZ/stepZ diffuse红/蓝.996121/.977345、.987764/1.005844、.992809/1.000754；spec输出一致。指定GGX与动态specular、材质边界、HDR、环境镜面/MIS和曲面回归通过。

## 噪声取舍

旧32帧单通道noise raw MSE .050401不变，filtered .001333→.002776，仍低于raw×.3、约94.5% raw MSE被抑制，但filtered MSE约增加一倍。新256帧双通道8候选MSE同位置none .004429→.005872、分离spatial .000782→.003257。原primary diffuse逐帧L1 .083512/.075203/.086434→.135154/.125014/.135431，原完整/起点门槛均通过但余量变小。这是颜色/阴影保持与噪声的取舍，不能称降噪全面改善；完整RGB误差建模仍未完成。

## Player投影参考修正与部署

首次Player在新旧运行时均失败，mixed specular比值约1.394；追加raw诊断比值约1.418，SB实际roughness .297852、F0 .039215与配置一致。读回测试相机aspect=1.78887844、窗口1898×1061，CPU独立参考在清空targetTexture后恢复窗口投影，而URP请求临时绑定64×64目的纹理。HoURP `ProcessRenderRequests`绑定/恢复targetTexture的源码与日志相符。

最终仅在Player fixture启动时显式固定相机aspect为目标宽高比，并断言；仍用独立CPU交点/GGX/点灯参考和原门槛，没有按输出校准光强。保留raw/filtered mixed日志，临时SB捕获资源/反射诊断代码已撤回，未修改旁边仓库。

两个Win64 Mono release errors0，实际EXE两条原suite全部通过，D3D11 inactive通过。mixed specular恢复.980621/.975037等原范围，实际窗口仍1898×1061；Player噪声MSE .000526/.000510。立体Cutout连续frameL1 .234153/.234987、阶跃.271076/.272656、变化像素.133722/.137003，通过原gate但高于旧值；RG阶跃蓝.951655接近5%下界，保留风险。primary spec起点回归仍通过。

Roslyn errors0、原始SHA/派生check通过，无新增DI shader诊断。生成器精确重建派生改动；所有阶段复制只属Editor测试，Player aspect/raw日志只属Tests模板。无共享GI变更，不重复GI。

下一步应在已修正RGB边界判定下改善噪声，扩展材质/灯/分辨率并关注阴影与Player蓝光余量；不能重新采用第39步已证明增加core残留的预滤波而省略core验收。真实URP motion时序、GPU预算和整古堡质量仍未证明，最终NPR合成/通用PT仍在原范围之外。

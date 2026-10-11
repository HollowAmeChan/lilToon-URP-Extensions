# Ho-RTDI 双灯阴影内部的过滤漏光门槛

第39步只提交Editor验证与文档，以下保留当时的失败基线。第40步的[后续RGB差修正](Ho-RTDI-RgbContrast.md)已让core与新16组通过，伴随增噪取舍。第39步首轮归一化/两端方差/RGB重要性及线性预滤波候选均撤回；旧整体评分掩盖的局部问题仍作为回归范围保留。

## 必须补的覆盖

既有`RunBoundaries`薄阴影部分只有一盏解析点灯，采用single-delta raw直通，不能验证NRD空间过滤。已有单灯Cutout的两像素margin core<2%也经过该直通。旧双灯八组确实调度RELAX，但只按通道能量、ROI平均形状/逐帧误差评分，少量阴影内部的局部残留可能被平均掉。

现在在`RunCutoutMultiQuality/Compatibility`的diffuse static/staticT/staticS/staticTS加入core指标：原CPU Cutout遮挡判定确认完整5×5均遮挡；只取观测ROI内、距其边界两像素的中心；每个像素累计512帧filtered红均值，除该点独立CPU计算的未遮挡红灯Lambert/点灯衰减值。再取这些像素的最大值，不用全阴影平均值替代局部最大值。报告同时保存core像素数、raw最大值、filtered最大/平均值。

沿用原core<2%标准。raw同位置必须低于.3%相对泄漏；既有3% raw、5% filtered能量、.15平均形状、.35逐帧/阶跃变化像素门槛保留。新增core仅作用于固定diffuse四组，不声称覆盖任意移动阴影或specular。两盏非零灯实际调度检查仍保留。

## 生产失败基线

两条D3D12路径相同，每组168个core像素，raw最大红光为0：

| 模式 | filtered最大均值泄漏 | 阴影内平均泄漏 | 正式core门槛 |
| --- | --- | --- | --- |
| static | .023282 | .009354 | 失败 |
| staticT | .024576 | .009422 | 失败 |
| staticS | .026633 | .010697 | 失败 |
| staticTS | .025992 | .010683 | 失败 |

原整体能量/形状指标依然通过，但完整双灯入口现在qualityPass=false、实际exit1，不能沿用第32/37步的旧通过结论代表新覆盖。原主表面完整入口恢复后仍qualityPass=true。当前残留出现在NRD过滤输出，raw可见性正确；后续按阶段定位，不靠加ray bias或修改Cutout材质掩盖。

## 撤回候选与收益取舍

- 首轮也保留被拒绝Gaussian系数到中心，variance按实际中心总系数平方传播：原primary通过，但第38步新spatial-only蓝1.054805仍失败，8候选同位置none MSE .004429→.008842，未接受。
- 首轮两端min variance、固定系数加统一RGB向量目标：部分彩灯噪声/能量改善，但新spatial-only蓝1.050059、primary静态蓝1.052220仍失败，动态specular门槛也失败，未接受。
- 首轮改为geometry/normal/material约束的3×3线性预滤波，后续仍原range stopping：原primary、双灯整体、动态specular、HDR/环境/曲面/材质边界旧gate通过；8候选同位置none MSE .004429→.002668、分离none .002763→.002259，新spatial-only蓝1.051984→1.050140仍失败。双灯阴影core进一步恶化，static最大.062893、staticTS .066808、平均.022393/.024344，相对生产最大值约2.5～2.7倍，因此撤回。没有为局部噪声改善接受阴影损失，也未进入Player构建。

CSV的离线5×5 mask分析先发现该问题；正式Editor实现重新调用独立CPU遮挡/照明参考、实际两条运行复现，保证其不只是离线脚本推测。原始NRD Smem的中心方差/range及接受sumW归一化、后续Atrous传播、用户`RAB_LightSampling`和`NrdSettings`已核对；不改变vendor、物理Shade、灯功率、native ABI或NRD参数。

Roslyn errors0、原始SHA/派生check通过，运行时/生成器git diff为空。仅Editor验证变化，不重复相同Player/GI构建。下一步利用已有NRD阶段捕获接口，对同一CPU阴影core读取Temporal/Clamping/首轮/后续输出，定位红光混入环节并修复；同时保留彩灯噪声、原primary/动态镜面和实际Player门槛，不放宽2%或减少验收范围。古堡仍只做资产支持审计。

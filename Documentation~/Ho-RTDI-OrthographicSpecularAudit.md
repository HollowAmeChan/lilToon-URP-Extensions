# Ho-RTDI 正交镜面view权重对照

第61步核对正交相机视线差异，并分别替换后续Atrous及首/后续两处view表达。几何契约差异确实存在，但候选没有解决mixed能量偏差，未进入生产；不能据代码直觉将其认定为第54步全部根因。

## 来源与范围

固定NRD `RELAX_Common.hlsli::GetCurrentWorldPosFromPixelPos`在ortho模式返回`viewZ×gFrustumForward + 横向frustum偏移`。用户fork `Source/Relax.cpp`将世界空间frustumForward/Right/Up写入常量。原首/后Atrous `centerV=-normalize(centerWorldPos)`、sampleV按sampleWorldPos与centerWorldPos混合后归一化，仍随像素变化；正交射线方向应平行。

隔离候选只在gOrthoMode非0时以`-normalize(gFrustumForward.xyz)`作为centerV和sampleV，透视分支表达保持。先仅覆盖index10，再覆盖9+10，保持原row normalization、geometry/luminance/F0/roughness权重、variance、时钟/预算和采样。Shader数组作用域退出恢复两份原shader，不改生产源码/常量或native ABI。

## 两组候选结果

`RunOrthographicSpecularEnvironment/Compatibility`后续-only两条完全一致，sky-only两组通过、mixed两组失败：

| mixed复用 | 原filtered spec红 / 蓝 | 后续-only ortho | 首+后续 ortho |
| --- | --- | --- | --- |
| none | 1.050745 / 1.048828 | 1.057229 / 1.056807 | 1.057230 / 1.056803 |
| T/S | 1.044358 / 1.050032 | 1.050029 / 1.058171 | 1.050026 / 1.058169 |

`RunAllOrthographicSpecularEnvironment/Compatibility`首+后续两条也完全一致，整体仍2/4、qualityPass=false、退出1。raw/diffuse保持原数值，混合镜面偏差略大，不放宽5%。首轮同时替换几乎没有改变此次结果，不能用首轮未改解释后续-only失败。

后续-only动态两条3/3，slow/rapid/step frameL1=.119095/.345072/.345437；首+后续此次动态RG3/3，.119067/.345042/.345386。原生产为.110073/.343487/.347752：阶段变化有利有弊，不能只以step下降声称整体改进。

## 复查与后续判断

报告`rtdi-auxenv54-partition-ortho/allortho-rg/compat.json`、`rtdi-orthospec61-motion-rg/compat.json`与`rtdi-allorthospec61-motion-rg.json`独立保存。恢复原环境仍2/4、原动态3/3及数值保持；Roslyn errors0、无新增DI Shader诊断、diff whitespace通过。Runtime/Shader/native/GI/Player始终未变，没有构建未变或已失败候选Player。

正交差异可能值得独立正确性评估，但本次数据不支持其作为mixed能量修复。下一项应避免继续改交换曲线/正交表达，核对perspective与正交中该偏差的覆盖，以及noise-dependent luminance权重、随机大步offset等具体项的影响；对照必须保持原始归一化和门槛。目标场景是透视、多灯，需要从实际输入分布验证而非只在此简化场景消除一个数值差。

# Ho-RTDI：diffuse历史颜色线段裁剪对照

第83步仅隔离替换NRD pipeline6。第82步实测Clamping先生成关闭通道正值，再进入Temporal反馈；固定vendor/fork将YCoCg三轴独立clamp，会让原RGB平面的颜色离开该平面。测试改为沿fast中心→normal颜色的线段与原同一个box相交，不改box/sigma、历史预算、accelerate/reset、moment修正或specular。

Fast center已由原box扩展逻辑保证在box内。各轴按delta符号求允许的线段参数，取最小、限制0～1，再线性插值；RGB/YCoCg线性变换下，它是两个端点颜色的凸组合。因此共享的RGB零通道不会由独立三轴截取引入新正值，也没有根据当前随机raw样本为0硬清通道。

Scoped override仅替换context NRD shader数组index6，生成独立ignored shader asset，检查main kernel和编译诊断，Dispose恢复原引用。原vendor、生产private shader/native、GI均未改变。

## 实际结果

Opaque Runtime三样本动态九阶段，两路径阶段报告完全一致。关闭红/蓝时raw、PrePass、Temporal、Clamping、两Atrous及published峰值全部0；原.001141/.001584残色消失。正式dynamic从6/9变8/9，唯一未过是已有100:1短窗口能量，不删门槛；仍exit1。

256/512动态漫反射各slow/rapid/step共6组通过，独立CPU shadow/reference、changed L1与confidence不变；512step changed约.1665。Runtime Cutout spot/step/32灯/HDR四组全部通过，raw/filtered specular结果保持。混合环境依然2/4、exit1；specular1.050745/1.048828与1.044358/1.050032保持，diffuse未引入新的正式失败。不是全部质量问题已解决。

Roslyn errors0、无新增DI shader诊断、原stage/code-source guards有效；后续生产化仍需派生generator同步、single/multi实际Player及默认质量回归。本步报告`rtdi-diffusesegment83-stages-rg/compat.json`、`rtdi-diffusesegment83-dynamic-opaque-rg/compat.json`和隔离motion/domain/environment记录，scope单独保存。

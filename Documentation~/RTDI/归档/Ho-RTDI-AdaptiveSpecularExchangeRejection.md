# Ho-RTDI 连续接纳量策略拒绝

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第60步按第59步实际分布尝试连续交换量，动态门槛保持，但混合环境偏差没有修复，未进入生产。保留隔离复现，不继续用曲线参数把结果压线。

## 候选定义

中心核c=.44198²，邻居接纳比例a=saturate((sumW-c)/(1-c))，候选exchange=a²×当前线性采样的spec confidence。邻居scale=lerp(1/sumW,1,exchange)，中心系数=1-(sumW-c)×scale；RGB按实际系数线性、variance按平方传播。这是实验性的软曲线，不是上游最优策略，也没有按环境开关硬分类。

原子histogram不在候选中，邻域/F0/normal/roughness/luminance距离与参数、主采样和可见性保持。只在临时NRD shader数组index10作用域替换，退出恢复原版本。环境与motion报告分别加adaptive后缀，未覆盖前面对照。

## 两条结果

`RunAdaptiveSpecularEnvironment/Compatibility`完全一致，sky-only两组通过，但mixed两组失败，qualityPass=false、退出1：

| 复用 | 原filtered spec红 / 蓝 | adaptive红 / 蓝 |
| --- | --- | --- |
| none | 1.050745 / 1.048828 | 1.051969 / 1.048651 |
| T/S | 1.044358 / 1.050032 | 1.046612 / 1.051402 |

`RunAdaptiveSpecularMotion/Compatibility`两条一致、3/3、退出0；slow/rapid/step frameL1=.110193/.343609/.347919，对比生产.110073/.343487/.347752。它避免了第58步明显动态退化，但并未达到本次能量修复目的，因此不能据一个方向通过接纳。

接纳量和confidence统计不同不代表此曲线就是正确归一化。逐轮variance、下一轮权重与随机偏移共同作用，最终能量并不按exchange amount单调变化；不在这些结果上声称普适偏差修复或继续试指数/常数压线。

## 撤回范围与下一项

没有生产更改可撤：Runtime/Shader/native/GI/Player始终未变。恢复入口的混合环境仍2/4、退出1、原动态镜面3/3与数值保持；Roslyn/diff检查通过，无新增DI Shader诊断。仅保留Editor实验/文档，不构建未变或已失败候选Player。

下一项核对具体权重来源和相机契约。已只读发现`GetCurrentWorldPosFromPixelPos`区分正交/透视，但后续spec中心/邻居view仍由位置归一化构造；正交相机真实视线平行，需要结合固定NRD/frustum输入核对并做隔离验证，不能先凭这处差异修改整个denoiser或假定它解释全部能量问题。继续保留现有动态/环境/Player门槛与DI范围。

第61步[正交view对照](Ho-RTDI-OrthographicSpecularAudit.md)分别替换后续及首+后续，mixed仍2/4且偏差稍大，不接纳为当前能量修复。

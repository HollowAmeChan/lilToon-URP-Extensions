# 步骤014：半GI实际实现实验，画质拒绝并撤回

本目录归档真实运行过的完整原型、失败报告和撤回证据，产品没有保留半GI开关/重建代码。`verify_validation.py`通过的结论是“实验确实失败且已按review过滤撤回”，不是半GI画质通过。

原型以仅Editor非序列化入口选择half，接入初始native ray、GI私有RAB current/previous、Final depth/history、Temporal pixel motion、NRD补偿projection。half raw内部为demodulated，双full输出按深度、共面、几何/shading normal筛选四邻居，再乘目标full albedo一次；缺少兼容样本则零贡献、保留validity。DI公共RAB、NRD原参数及产品默认full不改。

实际 RG PID10316、compat PID32560，各169次受控RenderRequest、编译诊断0、无fence错误，均**真实exit1**。平面功能、历史切换/reset、稳定reuse、跨红蓝材质重建、full尺寸发布及实际.25合成、奇数65×63full回退和释放half outputs通过；空间复用仅在解析隔离夹具关闭，时间复用/NRD保留。不是正常相机/真实应用帧motion、Cutout或Player验收。

| 结果（两路径一致） | Full | Half |
| --- | ---: | ---: |
| 64²完整域、raw/filtered最大平面误差 | .00781 / .00879 | .00781 / .00928 |
| 实际自有GI+NRD+映射payload | 1,227,104 B | 521,556 B |
| 薄前景 raw均值 | .9921875 | 0 |
| 薄前景 filtered均值 | .9919271 | 0 |

原5%能量/局部最小值门槛明确失败。资源减少57.497%，已包含两个full重建输出；这是该小夹具的描述符payload收益，不是总VRAM/GPU时间或1080预算。没有计时DLL或新的HD尝试。

前景位于64宽的偶数列x=8，仅半个full像素宽，half固定奇数代表点都看不到它。30个内部白色前景receiver有合法full GB/SB输入/alpha，但四个低图候选都属于后方红/蓝平面。正确的深度/法线拒绝避免背景串入，也留下黑线；扩大权重/邻域无法创造未采集的照明。独立期望为白色常量天空下单位diffuse，full同场景满足门槛。`images/rg`与`images/compat`保存full白线/half黑线，已实际查看；无显示增益或改阈值。

首轮 PID48012因夹具重复F23（空间复用单帧逐像素当能量真值）提前exit1，full-flat raw最大误差1.996。`rejected-single-frame`保留报告、log SHA及精确源码；只在夹具关闭spatial以隔离解析信号，产品采样未改。该失败不当作半GI质量结论，正式两条仍因薄前景失败。

事后review拒绝本方案，按F33搁置。七个Runtime文件已恢复至基线e7fcb40，逐字节SHA与012已测源码一致；新Editor入口及meta已移出产品，归档在`rejected-editor`。Unity恢复编译PID57944真实exit0、C#0/fence0，未声称重新做Player/完整画面回归。`reversion.json`记录恢复SHA、原型patch和图片SHA；原始代码/runner在`source-at-run`，`rejected-runtime.patch`对基线`git apply --check`通过。

当前root runner后来只加拒绝重跑保护（未再实跑），因为实验入口已移出产品。重现应在隔离副本以基线+归档patch+Editor入口及meta恢复，并使用同深度的原runner；不能直接在产品恢复half。没有真实full receiver缺样补偿、其动态/噪声/成本证据前，不重启相同四邻居/更宽邻域/背景填充路线。

下一步回到010实际古堡质量。新的待验证线索是GI固定SignalScale=64时，低照度信号的平方是否损失FP16 moment精度；DI文档已有亮暗moment/缩放历史经验，原NRD本地shader确实统计二阶矩。必须先实际读Moment与局部信号再决定是否改单位，不先认定根因、不调gain/宽色盒。原质量失败和012HD限制继续。

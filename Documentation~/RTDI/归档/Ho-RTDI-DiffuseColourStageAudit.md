# Ho-RTDI：关闭彩灯后的残色入口

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第82步对第78步实际Runtime三样本opaque动态灯集读取RELAX阶段，未改生产shader。现有正式动态测试仍6/9失败，保持exit1；本报告diagnosticOnly。

使用已有per-dispatch ValidationDiffuseStage callback，立即CopyTexture捕获PrePass3、Temporal4、Clamping6、首Atrous9与最终10；repeat10保存最后一次。各阶段按实际FP32 signal scale恢复物理单位，raw/published同时读取。关闭蓝/红的96帧序列记录首个正值帧、重置首帧与16帧后的峰值/均值，防止把历史反馈误认成首发阶段。

两路径逐项完全一致，均实际执行完成、exit1。重置首帧所有阶段为0；raw与PrePass在整个关闭通道的序列始终为0。Clamping先在第2帧产生正值，Temporal从第3帧开始反馈；最终colour在第2帧即出现。

| 16帧后峰值 | 关闭蓝 | 关闭红 |
| --- | --- | --- |
| raw / PrePass | 0 / 0 | 0 / 0 |
| Temporal | .0190735 | .0188599 |
| Clamping | .0254364 | .0251465 |
| 首Atrous | .00554276 | .00700378 |
| 最终published | .00114059 | .00158405 |

Temporal的后期值不表示Temporal首发；Clamping产生的颜色进入下一帧normal/fast历史。不是旧灯对象映射错误、缺少history reset或raw输入残留的证据。

核对固定vendor/fork的HistoryClamping：normal colour转换YCoCg后各轴独立clamp，再转RGB，可能离开原fast/normal颜色共同满足的RGB零通道平面。第56步nonnegative约束只修复负值，不会删除正的假通道。接下来在隔离DI private Clamping里测试沿fast中心与normal颜色连线裁剪到同一YCoCg box，保留box、sigma、history acceleration/reset与moment修正，验证是否避免新颜色及是否影响其它质量。

报告`rtdi-colourstage82-rg/compat.json`，stage -1/-2为raw/published。两关闭阶段各80帧稳态统计；firstPositive阈值1e-6仅隔浮点末位，不改变正式.001暗通道门槛。Roslyn errors0、GUID唯一/diff whitespace通过，无新增DI shader诊断。Runtime/native/GI/Player未变；原正式失败及其它约束保持。

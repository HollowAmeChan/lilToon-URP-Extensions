# Ho-RTDI：opaque第二辅助的局部消费

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第75步仅隔离原型，生产DI仍单最终样本。opaque的第二fresh解析样本在HoDIShade内生成又从全分辨率sidecar读回；核对用户fork ShadeSamples的sample→visibility→physical shading流程及当前SDK Stream/Finalize，确定它没有跨pass/历史消费者，可以在同一线程以局部reservoir直接消费。

## 实现与限制

保持两次CDF抽样的RNG、顺序、SDK finalized inverse PDF、visibility、固定 .5/.25/.25 混合及primary distance guide。第一辅助仍复用spatial之后的slot2，第二辅助留在局部变量；compute不再声明、写入或读取 `_HoRTDISecondAuxiliary`，exact-source guard确认这一依赖已消失。

仅为了实际GPU审计，生成 `_HoRTDIProbeAuxiliary` 四条8-byte记录，32 bytes。该记录不参与估计器计算。原生ray shader的second sidecar不绑定且不得调度；每帧断言native visibility texture为空，明确此原型不支持Cutout出现后继续运行。环境混合也不纳入本步。

C#分配从完整像素数改为四条，按真实GPU probe坐标读回。256²域仍检查72/104/136/168、y128；动态CDF128²检查52/60/68/76、y64。动态sample分解在这四点检查，whole-ROI质量门槛继续使用原256点；baseline也使用相同四点分解，报告明确标记four-gpu-probes，未把缺少的第二辅助读回伪装为全ROI验证。

## 完整输出等价证据

baseline使用第74步完整8-byte sidecar，同一Unity进程配置和确定性输入序列，分别重新执行。每观察帧读取完整raw/filtered输出的RGBAFloat传输字节，按帧顺序累积SHA256；物理指标之外，这可发现ROI外或平均值掩盖的变化。这里只是验证，hash与CPU读回成本不能当作产品性能。

- 256²，四配置各256观察帧，RenderGraph/compatibility共8对完整输出哈希逐项相同。质量全部通过、exit0，所有指标差0；GPU index/PDF/cache与CPU遮挡检查保持。
- 动态CDF九阶段，128²、每阶段96帧，两路径共18对完整raw/filtered输出哈希全部相同；mapping/PDF、四点actual shade分解与各能量/颜色/reset数据差0。原100:1短窗及opaque颜色尾部失败保持，baseline/direct整体均exit1。
- native compact四配置RenderGraph回归全部通过，辅助绑定仍按原名字/容量工作，不受opaque专用路径影响。

观察帧包括opaque skinned blocker快速/跳变、Spot/Point/Directional、32灯、HDR、灯集全关/恢复及同数量替换。没有仅用本地CPU重放RNG宣称GPU等价。

## 实际资源变化

256²完整第二辅助8-byte sidecar为524,288 bytes，direct估计器不再持有它。诊断保留32-byte四点快照，加CDF后两灯40 bytes、32灯160 bytes。128²动态sidecar也由131,072 bytes变为32-byte快照，诊断CPU数组并未因此全部省去。

1080p可省第74步估算的15.82 MiB/view第二辅助GPU记录，未在该分辨率实测；原三SDK reservoir/history、CDF、NRD与第一辅助scratch均未减少。inline visibility次数没有减少，没有有效GPU毫秒证据。编译器寄存器/占用率变化也未测，不承诺帧耗时收益。

## 复查与下一项

报告 `rtdi-direct75-lightdomain-direct/baseline-rg/compat.json` 与 `rtdi-direct75-dynamic-direct/baseline-opaque-rg/compat.json`。Roslyn errors0、GUID唯一/diff whitespace通过，无新增DI shader诊断。首次C#导入因缺少UnityEngine.Rendering命名空间失败，修复后完整执行；失败导入不计验证。Runtime/native/GI/Player未变，未重建未变Player。

当前绑定是专用opaque隔离原型，不能让Cutout进入它后再以post-dispatch断言代替安全的资源切换。下一项应在受控Opaque↔Cutout变化下，选择匹配的compute/native变体和sidecar生命周期，验证输出及历史连续性；再覆盖每相机/实际Player、有效GPU时间与产品接入。保留原环境/动态质量问题和DI范围，不扩展通用PT或最终NPR合成。

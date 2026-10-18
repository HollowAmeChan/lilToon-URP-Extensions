# Ho-RTDI：第二fresh解析样本的8-byte记录

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第74步仅优化隔离三最终样本原型的第二辅助sidecar，生产仍为原单最终样本。没有改变SDK reservoir ABI、ReSTIR、NRD、GI或最终材质合成。

## 必要性与消费契约

原sidecar每像素使用24-byte `RTXDI_PackedDIReservoir`。核对fork ShadeSamples、SDK Stream/Finalize/Pack，以及当前HoRTDIApplicationBridge：此第二辅助每帧从解析灯CDF新抽样，M=1、target=1、age/distance=0、UV固定(.5,.5)，不会参与下一帧ReSTIR reuse。当前Point/Spot/Directional分支不消费UV；最终shade需要light index、完整inverse PDF及visibility。

因此用私有 `uint2` 保存同一个样本：word0低31位是analytic index、bit31是最终可见性；index=0x7fffffff表示empty；word1是inverse PDF的FP32原始位。只有当前解析delta辅助路径使用，环境样本仍由primary/原SDK存储负责；不能用于需要UV的面积/网格/环境辅助样本。

读入shade时重构临时SDK reservoir，保留valid bit、固定UV编码0x7fff7fff、target/M=1、FP32 weight与binary visibility。没有改变proposal或平均系数 .5/.25/.25，不在不可见时删掉样本重新归一化。Native使用实际R8 final visibility的第二辅助bit写缓存；opaque使用inline结果。C# GPU审计同时支持24/8-byte，按stride校验并解码；所有原型分配/读回入口同步切换，不改SDK结构。

## 实际内存与语义对照

256²第二辅助buffer从1,572,864降到524,288 bytes（1.5→.5 MiB），减少2/3。两灯加CDF为524,296 bytes，32灯524,416。1920×1080按同layout估算47.46→15.82 MiB，节约31.64 MiB；未实际验证该分辨率。上述只指第二辅助sidecar，不是全部DI/NRD显存。

Cutout/opaque × RenderGraph/compatibility四入口，spotRapid/spotStep/32灯/HDR共16配置全部通过、exit0；逐项raw/filtered能量、L1、MSE与24-byte原型数值差0。每配置128次GPU辅助valid/index/inverse PDF及CPU visibility检查；compact cache同时与native R8一致，opaque cache同CPU遮挡一致。opaque历史reset策略保持。

环境partition两路径仍2/4、exit1，混合镜面原失败保留；与重新执行的24-byte版本所有数值差0。动态CDF九阶段也重新执行24-byte/native和opaque对照：GPU index/PDF、mapping、raw sample分解均通过，报告所有能量/颜色/primary/auxiliary/reset指标差0；原100:1短窗及opaque颜色尾部失败仍保留，不能据压缩宣布质量问题已解决。关闭所有灯与恢复、同数量替换、Point/Spot/Directional重排均覆盖。

另执行compact的native原子计数入口，四组质量通过，射线数与第53步相同：256观察帧，spot主16,701,440、其他主16,777,216，两辅助各16,777,216。每辅助每帧65,536条，不含gradient/inline queries；counter增加12 bytes及原子开销，只用于审计。压缩没有降低最终射线数量，没有有效GPU毫秒证据，不能把66.7% buffer减少说成等比例帧耗时收益。

## 复查与边界

报告 `rtdi-compact74-lightdomain[-rays]-cutout/opaque-rg/compat.json`、`rtdi-compact74-env-partition-rg/compat.json`、`rtdi-compact74-dynamic-cutout/opaque-rg.json`。Roslyn errors0、vendor原SHA/派生check、GUID唯一及diff whitespace通过，无新增DI shader诊断。native DLL SHA保持 `5EFF9D42D56DA8B38D305BC483D84263AEE3C31A6C6C3A0A637370DA68220EDB`；没有构建未变Player。

shader按exact-source guard生成，后续原型若不再是fresh analytic delta必须重新评审；这种记录是辅助payload，不宣称等价于任意SDK reservoir。测试当前为单相机8-byte绑定，多相机/实际Player的该原型绑定仍未验证。

下一项评估opaque是否可在同一shade内直接消费第二fresh样本而不持有全分辨率sidecar，同时检查native alpha的资源生命周期与切换；保持原型质量约束、既有环境失败和DI范围。产品化仍需原型接入、有效GPU时间、实际Player及目标场景预算证据。

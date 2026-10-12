# Ho-RTDI 第二个独立辅助样本

第53步保留第52步CDF，在原ReSTIR估计之外使用两个新CDF估计，固定合成为`.5×主估计 + .25×辅助1 + .25×辅助2`。四组解析灯门槛全部通过，代价为额外样本缓冲和第三条最终射线；仍是隔离原型。

## 依据与实现

沿用户两份ShadeSamples的物理贡献/inverse PDF契约、固定SDK Pack/Unpack/Stream/Finalize和第52步真实CDF区间PDF，第二辅助使用同一RNG流的下一次新抽样。两个辅助各自按1/PDF归一化、各自追当前native any-hit，不用取当前颜色大小决定混合系数，也不把ReSTIR inverse PDF误当精确选样分布做未经证明的MIS。

原临时slot2容纳辅助1，第二辅助写入独立natural-layout、24-byte packed reservoir buffer；compute读取后使用相同物理BRDF。主reservoir/history、CDF、primary缓存gradient、NRD设置和距离guide沿用原型。临时R8改为三visibility位/7编码，RGB输出仍保持原receiver alpha。这个内部编码尚未作为生产/debug输出契约接入。

`RunSecondAuxiliaryDomain/Compatibility`只生成隔离compute/raytrace并绑定临时资源，不改Runtime/Shader/native/GI/Player。沿第51步256²、3584点、192预热/256观察、固定60Hz和独立CPU GGX/cone/alpha参考，保留raw3%、filtered5%、frameL1<.35、step真实变化像素前三帧L1<.35以及HDR实际scale<1。

## 两条正式结果

两条完全一致、4/4、qualityPass=true、退出0，报告为`rtdi-triplecdf53-rg/compat.json`且finalSamples=3。

| 配置 | 两样本CDF frameL1 | 三样本 frameL1 | 三样本filtered红 / 蓝 | 三样本step变化L1 |
| --- | --- | --- | --- | --- |
| spotRapid | .360202 | .324357 | .988799 / .987483 | — |
| spotStep | .226086 | .202892 | .991650 / .982868 | .203049 |
| manyWeakRapid | .344326 | .307974 | .988287 / .989365 | — |
| hdrRapid | .343575 | .308588 | .989950 / .988031 | — |

spotRapid raw normalized MSE从.620097降到.495804，filtered从.211896降到.176698；32灯分别.577339→.450058、.180342→.149500。没有把误差门槛改成.36，也没有关闭confidence。相比之前接近门槛的原型，现有配置有更多余量，但不代表所有灯/材质/动画质量。

每配置128次GPU辅助valid/index/inverse PDF检查通过，并按CPU alpha参考检查两辅助visibility及主visibility/缓存。每配置观察64对抽样，其中两灯同选30对、32灯同选27对，其他确实不同；这是实际执行/非重复覆盖检查，不是严格统计独立性证明。原两样本CDF RenderGraph回归仍3/4、spotRapid=.360202失败、数值不变。

## 真实射线与显存代价

第二辅助buffer在256²为1,572,864 bytes（1.5 MiB），加CDF两灯8 bytes/32灯128 bytes。按同样natural layout估算1920×1080第二辅助为47.46 MiB，尚未在该分辨率验证；不能再沿用第50步“不增加buffer”的说明。

`RunSecondAuxiliaryRayAudit`另生成带GPU原子计数的RenderGraph原型，先同步结束预热再清零，单独报告`rtdi-triplecdf53-rays-rg.json`。原子计数只在合法最终TraceRay前更新，gradient与inline queries不计入；普通质量入口没有这些计数开销。四组质量也通过且数值不变。

256个观察帧：spot两组主最终射线16,701,440，两辅助各16,777,216；manyWeak/HDR主与两辅助各16,777,216。每辅助每帧65,536条，spot主略少是部分像素没有有效primary reservoir。相对第52步确实增加一条全分辨率最终射线，相对原单样本增加两条；这些计数不是总GI/DI射线数，更不是GPU毫秒。审计另增加12-byte counter buffer和原子开销，不用于性能结论。

## 复查与下一项

Roslyn Runtime/Editor errors0、无新增DI Shader错误/警告、GUID唯一与diff whitespace通过。两条质量/实际PDF/可见性检查、独立射线审计、旧两样本CDF失败回归完成。生产未变，没有构建未变Player/GI，也没有宣称已部署或成本可接受。

下一项应覆盖环境与opaque路径，检查辅助仅采解析灯时的贡献归属，避免平均后把天空贡献错误减半；再完成动态CDF/多相机/实际Player与有效GPU时间核对。现有距离guide仍选主样本、额外显存和两条辅助射线也必须在产品接入前评估。古堡继续用于资产支持审计，不借本步扩张最终NPR/PBR合成或通用PT。

第54步[环境贡献归属](Ho-RTDI-AuxiliaryEnvironmentPartition.md)修复隔离原型sky-only归零与天空减半；原始能量恢复，但混合镜面filtered仍少量超5%，仅2/4，不进入生产。

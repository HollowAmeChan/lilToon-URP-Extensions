# Ho-RTDI 独立第二最终样本原型

第50步针对第49步单最终样本的颜色方差权衡，保留生产ReSTIR提案/复用与动态confidence，加入一个独立解析灯样本作平均。隔离原型通过当前两灯256/512动态镜面、漫反射与64阴影core门槛；尚未进入生产，没有据此证明多灯/环境/GPU预算。

## 参考与必要性

用户6000.3.15 `Assets/Shaders/RayTracing/DI/ShadeSamples.hlsl`与fixed FullSample `RenderingPlugin/External/RTXDI/Samples/FullSample/Shaders/LightingPasses/DI/ShadeSamples.hlsl`都按所选reservoir输出一个最终样本。更多初始候选会改变选样/权重，不能自动等同于多个独立最终估计。第49步RGB提案降低总噪声，却使短期红通道raw误差变差，因此本步检验增加最终估计是否能同时改善两种指标。

核对固定SDK `DI/Reservoir.hlsl`的Stream/Finalize/InvPdf及`Utils/ReservoirAddressing.hlsl`的块地址。辅助样本独立均匀选择当前解析灯，概率1/N，用target=1、invSourcePdf=N构建一候选reservoir；归一化后inverse PDF=N。平均输出为`.5×原ReSTIR估计 + .5×N×辅助灯物理贡献`。不是给RGB或灯强增加增益，也没有减少原初始候选、复用或动态置信度。

## 隔离实现与可见性

入口仅允许`.codex-research`工程，在`Assets/HoRTDIPairedFinalAudit`生成compute/raytrace副本，替换临时RendererFeature引用。生产Shader、Runtime、native ABI、GI与Player模板未变。

inline conservative查询使用`CULL_NON_OPAQUE`，不能承担Cutout辅助样本的最终可见性。原型在既有native final-visibility dispatch内多追一条相同any-hit射线。空间dispatch完成后，slot2临时reservoir不再被邻域读取，raygeneration将辅助样本存入该槽；没有增加buffer或纹理。现有R8暂用0、1/3、2/3、1编码两个二值visibility，compute解码后分别着色。这是原型内部格式，不能直接推广为公开visibility输出契约。

辅助RNG用不同seed与实际DI帧序号，由Editor渲染入口显式设置native shader参数。平均照明不再对应单个primary reservoir，因此gradient不能拿旧平均输出与重追的单个primary比较；原型从旧primary reservoir缓存的实际visibility、旧灯/表面及inverse PDF恢复其亮度，然后从匹配当前receiver重追同一样本。辅助样本没有历史reuse；原NRD距离guide仍沿用primary样本，这也是更广泛材质/灯域验收的限制。

原型目前要求激活native alpha visibility且无环境灯。两个最终样本只覆盖当前解析灯集；未覆盖天空MIS，不能把解析灯辅助估计直接平均进含环境的结果，否则会错误缩减环境贡献。64多灯入口中的无Cutout控制仍走原单样本路径，报告字段为requestedFinalSamples，未声称它也执行了两条射线。

## 两条正式质量结果

`RunCutoutResolutionPairedFinal/Compatibility`与`RunCutoutResolutionPairedFinalDiffuse/Compatibility`保持原256²/512²、3584点、192预热/256观察、8候选、固定60Hz与独立CPU响应/逐帧UV-alpha遮挡。raw每通道3%、filtered5%、frameL1<.35、step真实变化像素前三帧L1<.35不变。两条数值一致，各六组镜面和六组漫反射均通过、退出0。

| 尺寸 / 模式 | 镜面frameL1 | 镜面step变化L1 | 漫反射frameL1 | 漫反射step变化L1 |
| --- | --- | --- | --- | --- |
| 256 / slow | .069870 | — | .082792 | — |
| 256 / rapid | .343677 | — | .159824 | — |
| 256 / step | .217846 | .268236 | .113372 | .220855 |
| 512 / slow | .063285 | — | .077797 | — |
| 512 / rapid | .344150 | — | .155201 | — |
| 512 / step | .209182 | .256798 | .108362 | .204357 |

镜面rapid从生产默认.507913/.539075下降，仍接近.35门槛，不宣称足够的场景余量。gradient仍为slow/rapid/step 255/256/15帧，未以关闭置信度换取通过。

`RunCutoutPairedFinalMultiQuality/Compatibility`旧64八配置通过。168点5×5完整CPU阴影core，raw红最大0；static/T/S/TS filtered最大分别.007080/.007198/.007811/.007720，均<2%。这仍是既定场景而非任意阴影边界证明。

## 实际样本与缓存核对

`RunCutoutPairedFinalSequence/Compatibility`读取GPU slot2与primary的真实24-byte packed reservoir，按SDK块地址采四个像素、256帧，移动Cutout覆盖开/关遮挡。每样本检查valid、真实灯索引、inverse PDF=2，并按独立CPU alpha参考核对R8两个visibility位与primary缓存。两条共各2048次检查通过：辅助1024次中灯0选择515次、相邻帧选灯变化507次、可见835/遮挡189。每个probe另设非冻结/粗分布门槛；这证明执行与覆盖，不作为严格统计独立性证明。

正式报告分别为`rtdi-finalpair50-specular-rg/compat.json`、`...-diffuse-...`；64为`...-multi-...`，采样核对为`...-sequence-...`，不覆盖默认报告。Roslyn Runtime/Editor errors0、无新增DI Shader错误/警告、GUID唯一与diff whitespace通过。默认RenderGraph正式入口回归仍原3/6、退出1且数值不变。

## 下一步与成本边界

现有证据足以继续评估该DI采样方向。下一步用多灯分布/聚光灯与HDR检查均匀辅助提案的退化，再解决不含环境/opaque fallback的接入范围和实际部署；未完成这些检查前不接纳为生产默认。多灯场景均匀选样可能增加方差，不能由两点灯测试外推至古堡。

原型每个有效receiver最多增加一条当前最终可见性射线，原gradient与NRD仍运行。资源不增不代表GPU时间不增；没有有效GPU计时/capture时不宣称可接受预算。古堡继续只作合并天空/几何资产支持审计；最终材质合成、通用PT与GI扩张不属于本步。

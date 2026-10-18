# Ho-RTDI：多相机复用对照与质量判据复查

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第72步只扩展第71步的隔离Editor对照，未修改生产ReSTIR、NRD、GB/SB、Player或GI。先检查相同fixture分别关闭temporal/spatial，而非依据单帧最大误差修改主表面输入。

## 对照结果

两个相机、280render、80GPU内容隔离、87reset、skip/disposal、下游GPU颜色检查不变。No reuse的Cutout/opaque × RenderGraph/compatibility四入口实际exit0，raw最大逐点误差.000651128、filtered .00450590、absent colour0。

只temporal、只spatial及两者组合的RenderGraph入口仍按第71步的瞬时门槛exit1，所有隔离检查通过。现记录最坏点对应的实际GPUpacked sample index和inverse weight：

| 模式 | 瞬时raw最大误差 | 最坏实际weight | 最坏点类型 |
| --- | --- | --- | --- |
| no reuse | .000651 | 1.99999976 | Point |
| temporal | .355710 | 2.713319 | Point |
| spatial | .846843 | 3.694200 | Spot |
| temporal+spatial | 1.399074 | 4.802015 | Spot，resize后 |

两盏同色同位置灯的initial estimate在no reuse时接近确定性，但空间/时间MIS的选中来源表面不同。核对用户fork和本地RTXDI `DI/SpatialResampling.hlsl`：归一化用选中来源表面的`pi`，分母为各来源`targetPdf*M`之和；temporal也用历史表面的target和previousM。SDK `FinalizeResampling`按`weightSum*pi/(selectedTargetPdf*piSum)`归一化。变化的来源target可产生非恒定inverse weight，不能要求每一次复用后的weight都是2。

## 原瞬时门槛的解释需要修正

第71步把随机的单帧raw逐像素与解析期望比较，要求每次1%内，属于不合适的采样质量判据。第72步仍保留它与旧失败报告，没有将瞬时失败当作已证实的实现bug；增加阶段积分来检验是否存在实际能量偏差。

| 模式 | A Point64 raw / filtered | A Spot64 raw / filtered | A Spot80 raw / filtered |
| --- | --- | --- | --- |
| temporal | .998120 / .995647 | 1.002510 / 1.003261 | .995515 / .994461 |
| spatial | 1.002964 / .998478 | .996644 / .986775 | .999865 / .996624 |
| temporal+spatial | 1.009630 / 1.003261 | 1.004326 / .996526 | 1.002039 / .998362 |

B方向灯三模式均约.999964。A三阶段分别1024、512、1472个点帧观测，B2560。积分与瞬时极值给出不同结论；此数据尚不是逐像素长期均值或任意灯域无偏证明，也不能只凭区域积分忽略局部高光/边缘偏差。

阶段统计按独立CPU逐点reference累加，保留几何、投影、灯类型和解析衰减；最坏点CPU位置与GPU packed weight直接报告。无论何种模式，GPU内容隔离/索引映射和下游颜色均保持，未发现支持修改GB/SB的证据。

## 复查与下一项

报告 `rtdi-multicamera72-noreuse/temporal/spatial-cutout/opaque-rg/compat.json` 及新增region/worstRaw字段。Roslyn/GUID/diff检查通过，无新增DI shader诊断，原第71步两者组合数值完全恢复。现有环境局部镜面、动态遮挡/灯域的其他有效失败不受此判据复查影响。

下一项以同fixture的逐像素长期均值、区域积分和状态隔离分别验收。保留瞬时误差作为噪声诊断，不把它冒充Monte Carlo期望误差；真实移动/启停的响应应继续使用已有changed L1等门槛。只有取得这些证据后，才判断需修改复用实现还是推进产品接入，不为了最大值变小而关闭生产reuse或改MIS常数。

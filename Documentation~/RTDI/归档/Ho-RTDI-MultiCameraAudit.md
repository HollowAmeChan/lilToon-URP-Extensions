# Ho-RTDI：交替相机的状态与实际输出

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第71步仅Editor隔离检查，不修改生产、Player模板、native或GI。参考用户fork的 `NrdDenoiserHelper.CommonFrameInput`（当前/历史矩阵、分辨率与frameIndex），以及当前DI每相机LightFrame/NRD实例、permanent pool与三帧constants资源。目标是核对实际交替渲染，不以存在Dictionary推定所有输入都已隔离。

## 场景与检查

同一个DI feature，A为64²红色点灯，B为96×80蓝色方向灯，各两盏灯。交替96次预热后48轮：仅A改变为聚光灯、仅A改80×64、跳过A一次再恢复，以及销毁B后继续A。每个相机使用自己的60Hz测试时间；不使用会把LastContext当作当前相机的单相机辅助函数。

两盏灯确保NRD真正调度。首次单灯版本因已有 `singleDelta` 确定性直出而得到dispatch0，这不是相机故障；不把该版本计入NRD验证。resize依照生产契约重置该相机DI帧计数到1。

实际核对：

- 两相机context、reservoir/history、NRD实例及全部permanent RTHandle不共享。
- 渲染另一相机前后，非当前相机的frame、reservoir对象与所有packed内容指纹保持；80项实际GPU内容比较。
- 当前GPU双向灯identity mapping与期望灯组一致；每次渲染的NRD调度、尺寸及局部reset符合期望，共87项。
- 跳过A不推进帧，失效A的历史，active=0；B正常继续。恢复A只重置A。
- 销毁B后由后续A渲染清理B的context，B的reservoir与NRD均释放，字典仅剩A。
- 复用隔离工程现有Player capture feature，在当前相机图内拷贝DI，确认下游实际GPU输出是该相机的颜色；没有把它加入用户包的可添加feature。RenderGraph结束后全局texture binding已释放/恢复black，不能在CPU端用Shader.GetGlobalTexture指针来验证图内发布。

## 保留的物理参考失败

四个入口（Cutout/opaque × RenderGraph/compatibility）实际执行完：均 `isolationPass=true`，但 `pass=false`、exit1。280次实际render，80隔离、87reset、1skip、1disposal；absent colour为0。两路径每个可见性域数据一致。

独立CPU平面与点/聚光/方向灯参考仍要求raw逐点1%、filtered逐点5%。该检查没有放宽：Cutout2956个观测失败，opaque2947个；最大raw相对误差1.399074、filtered .810258。预热后的首个A点已经有raw .391846 vs reference .372491（约5.20%），尚不能仅归因resize。报告逐项累计，最终写报告再抛异常，使所有隔离/skip/disposal检查能够完成；`isolationPass`和完整质量通过严格区分。

这次发现既不能证明主表面输入错，也不能证明ReSTIR正确。相机矩阵m00在首个失败点相同，暂无根因证据；下一项需在同fixture关闭ReSTIR reuse并读实际primary/packed sample，区分采样权重、输入坐标/尺寸及CPU参考。不要先修改GB/SB或滤波系数。

报告 `rtdi-multicamera71-cutout/opaque-rg/compat.json` 在隔离工程。Roslyn errors0、GUID唯一、diff whitespace通过，无新增DI shader诊断。只Editor/文档变更，未重新构建未变Player；第70步已通过的实际Player结论不替代本步新场景的失败。

## 下一项

优先进行上述raw失败的独立控制，继续保持目标为DI实际可用性。多最终样本的CDF与每相机绑定尚未产品化；本步只覆盖生产单最终样本、正交Base Camera、SubmitRenderRequest，未证明真实跨应用帧、透视、XR、overlay、多GPU或多线程。古堡合并天空壳仍只作资产支持审计，不作为此处解析质量参考。

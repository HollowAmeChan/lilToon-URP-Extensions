# Ho-RTDI：三最终样本NRD距离输入审计

第95步只读实际GPU输入，未改Runtime、采样、NRD参数、距离或最终输出。第94步随机offset控制失败后，转向更基础的输入合同，避免继续按一个中心能量指标调整滤波系数。

## 来源与合同

生产 `HoRTDI.compute` 将两辅助最终样本混入diffuse/specular RGB，但NRD两lobe alpha仍写primary `selectedDistance`。环境/方向灯距离100000被限制为65504；这是灯样本代理距离，没有独立的in-lobe几何距离输入。

用户新参考 `RayTracing/DI/ShadeSamples.hlsl` 得到lightDistance后直接合成/StoreShadingOutput，没有NRD输入绑定，因此不能证明该代理符合NRD合同。当前BRDF环境查询 `RAB_TraceRayForLocalLight` 只返回是否遮挡，未保留几何hit distance。

[NRD官方README](https://github.com/NVIDIA-RTX/NRD/blob/master/README.md)要求距离代表对应BRDF lobe，ReSTIR灯样本距离需按lobe接受或用in-lobe回退；多path镜面应使用专用合并helper。固定本地v4.17的 `NRD_FrontEnd_SpecHitDistAveraging_Begin/Add/End` 实际是最小非零距离、无有效值时返回0，不是算术平均或亮度加权。官方当前说明不是升级本地ABI的授权，也不能代替对现有版本源码的核对。

## 实际GPU观察

128²原四组天空only/mixed × none/TS，512观察帧，每16帧读4固定探针，共每组128、每路径512。读取实际NRD diffuse/specular输入alpha、packed主reservoir、slot2辅助、8-byte第二辅助与native final visibility bits。局部贡献资格使用实际有效样本、正权重、可见性和独立CPU GGX非零响应；灯位置在fixture内固定，CPU距离独立按平面交点计算。

两路径报告完全一致：

| 场景/复用 | alpha与primary距离匹配 | 两lobe距离相等 | primary天空但有可见局部镜面辅助 |
| --- | --- | --- | --- |
| 天空/none | 128/128 | 128/128 | 0 |
| 天空/TS | 128/128 | 128/128 | 0 |
| mixed/none | 128/128 | 128/128 | 119/128 |
| mixed/TS | 128/128 | 128/128 | 120/128 |

天空距离全65504；mixed距离在1.643858到65504之间，距可见局部辅助光源的比值最大39847.71。该比值只描述关联距离差，不能自动称作真实几何距离误差或必须取最近灯距。相符计数是诊断观察，不断言旧行为正确。

报告 `rtdi-guidedistance95-rg/compat.json` 明确diagnosticOnly=true。原正式环境输出仍2/4，两实际进程exit1，raw/filtered能量保持；未把读回成功代替质量验收。Roslyn errors0、GUID唯一、shader/diff通过，无SourceAssetDB时间错误。

## 取舍与下一项

确认现有RGB组合与距离来源脱节，但尚未证明它解释第55/63步静态后续Atrous搬移；RELAX主要在PrePass与specular tracking使用距离，后续Atrous没有直接读取该值。应先做距离单变量控制，分别看阶段变化与运动追踪，再确定DI的lobe距离生产方式。

不能直接把所有灯距平均、设0或用最小灯距helper就宣称合同满足：首先须确认输入在lobe内。该工作是DI降噪guide，不构建通用PT/GI路径，也不新增Feature；如果需要额外查询，要明示射线与缓存成本并实际验证。最终NPR合成仍在范围外。

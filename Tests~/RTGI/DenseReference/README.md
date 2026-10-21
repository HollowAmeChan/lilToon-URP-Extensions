# 步骤017：高样本参考暴露并修复蒙皮 AS 顶点范围越界

本步产品改动仅在已有**自有 BakeMesh 光追快照**上规范索引：把局部index+baseVertex写成absolute index、baseVertex归零。顶点流、UV、法线、切线、材质/submesh顺序、三角形绝对顶点元组和原raster/source mesh保持。共享DI/GI调用同一HoRTSkinnedMesh；BakeMesh(true)及稳定pose缓存不改。

## 具体故障与修复收益

古堡1_mesh含251,321顶点、position stride40，VB共10,052,840字节；14个非天空submesh用非零baseVertex和局部索引。D3D12层实际报告14条`BuildRaytracingAccelerationStructure`范围越界：声明跨度10,052,812=(N−1)×40+12，却从baseVertex偏移地址开始，超出资源。两个长批出现887a0006/device removal，日志明确not out of Local memory；不能仅归因整卡显存占用，也未证明这是device removal唯一原因。

归零自有快照baseVertex后，同场景调试小批**14→0条API错误**，后续full审计和长采样也无验证/fence错误。CPU canonical-layout证明所有顶点流逐字节SHA不变、absolute三角形元组相同；Unity会重算firstVertex为实际minIndex，修复依据是baseVertex=0，不声称返回的firstVertex全为0。原source mesh仍保留局部索引/baseVertex。

索引格式尽量保持UInt16；absolute index不能容纳时用UInt32。仅需要规范且pose重新Bake时执行，稳定pose无重复Bake/规范化。此模型724,416 indices、约2.90MB UInt32索引；规范化有O(index count) CPU工作和临时数组，未声称动画CPU/GPU速度收益。**静态MeshRenderer非零baseVertex尚未由本修复覆盖**，需后续自有镜像处理，不能泛称所有mesh-instance范围均解决。

## 回归和部署证据

- shared scene baseVertex RG／compat：实际PID12436/58844，exit0，各145checks，D3D12 validation0，覆盖双submesh、骨骼/Morph、非均匀/镜像、稳定缓存、非readable source和生命周期；source base4保持、自有snapshot absolute元组校验。
- GI native材质命中 PID57560：exit0，141checks、validation0；特制unused UV0与active UV0不同，验证真实UV0/UV1 emission、normal/tangent、混权、Morph，而不是只看能打中位置。
- 两个新的Win64 Mono **Release** Player，RG build54680/Player52168、compat build45800/Player58604，全部exit0，各315请求/35checks。三个serialized非readable source均base4，实际owned snapshot均base0/absolute；原骨骼/Morph/Cutout/启停/合成检查保留。完整payload SHA在player-payloads.json，无HoGIGpuProbe.dll。它们仍为每实际Update一次请求，不冒称正常enabled-camera呈现。

## 有界参考与当前结论

Editor专用Feature从相同full256 GB/SB选定16个receiver，展开sampleID×receiverID，只包装primary loader，复用原一跳Generate→次级SDK RIS→真实alpha final visibility→Final。没有GI时间/空间复用或NRD，黑事件计入样本，参考输出FP32。共用材质/light实现，所以它是同模型高样本参考，**不是独立绝对着色真值**，也不是全图验收。

实际probe对齐原full primary、main history包、compute RAB与raygen读到的world/normal/albedo；ray/compute误差0。18活灯（容量32不能当活灯）、15实例、12Cutout、sky/RIS与原参数8 local/4 environment/.001 bias/1000距离相同；采样期间scene/light/sky revision与normal/albedo稳定。

修复后fixed-reference PID44048，2,048个独立seed批次×512=每点1,048,576样本，14/16满足预设：batch均值标准误≤2%、前后半均值差≤5%，未改门槛。两个暗控制点再由PID29832采16,384×512=8,388,608样本，其中(240,32)满足；(16,16)绿色标准误仍2.316%，**保持不确定**。最终只接纳15/16选点的有限精度参考；不无限加样本、不用子集冒充全图通过。

例如合法AS下(87,165) reference红均值约.001500，原256窗raw约.024724（16.48倍），filtered约.003716（2.48倍）；(185,180) filtered约参考.49倍。有过亮也有过暗，不能简单整体补gain，也不能把有限窗raw都当真值。选点包含事后最大误差点，存在选择偏差，不能据此推断全图resampling偏差。

修复后full空间审计PID21152仍原5%失败，RGB约(-7.25,-8.42,-8.78)%；几何范围合法不等于滤波质量通过。旧非法AS数据已隔离，不作为真值；修复后重采的可用点才形成上述结论。

## 故障保留和运行约束

early-allocation（URP pool未初始化）、missing-kernel（include里的pragma没导出Final）、两次device removal均有真实exit/report/log/source。第二个device失败只留下terminal=false的1024-batch检查点，不能算完整8M。小负载GPU资格57032有24个非零/有序/完成fence样本，证明恢复后能执行小负载，不保证后续大场景不挂。

调试层按[Unity 6.3官方参数](https://docs.unity3d.com/6000.3/Documentation/Manual/EditorCommandLineArguments.html#debugging)启用`-force-d3d12-debug`；没有改驱动/TDR或终止其它应用。首debug进程3432虽exit0，旧runner regex漏掉`d3d12 validation error`而计0，实际14条，已明确拒绝并修正计数；不能盲信绿色脚本。修复后小批13720、复核29476及全部后续debug0。

长采样改为每Editor update一请求、512小批，记录真实applicationFrame/renderedFrame和非终态检查点；这是诊断调度，不是正常相机自动绘制。非零Editor退出可留下空lockfile，只在独占打开成功确认无人持有后删除本任务项目的该单个锁；不删活锁、不操作别人的验证工程。

source-index.json按SHA映射source-blobs保存所有时刻精确执行源码，去掉重复副本；各`*-original/run.json`和原结果不改写。Player源单独保留，已有DLL只hash，无新binary。`verify_validation.py`复核API范围/14→0、CPU语义、Editor/Player/payload、失败与非终态、合法AS后选点收敛；fullSceneQuality仍false。

下一步先覆盖静态非零baseVertex的安全镜像与readonly源，再用合法AS和多个独立seed评估ReSTIR各复用模式对收敛参考的误差。保留原5%及未完成的moment/HDR、正常camera、1080预算；不盲调增益/色盒/单位或直接抄参考的混合lobe FinalMIS。

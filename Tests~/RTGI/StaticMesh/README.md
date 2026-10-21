# 步骤018：静态镜像候选搁置，保留AS增长最小复现

结论：**没有接纳静态非零baseVertex生产支持，也没有画质/性能收益声明。** 候选代码已撤回，原017蒙皮修复保留。目标继续active。

候选按source mesh缓存owned clone，将绝对索引写回自有index buffer，原mesh/raster不动。可读源无需GPU回读；非readable clone仍可替换index allocation，候选只回读源indices。稳定帧、共享缓存、source dirty、释放等功能检查157项曾完成，但D3D12 debug层在实例3→5增长时出现scratch范围错误，整组不能验收。尚未完成static native UV/Cutout、nonreadable Release Player、完整生命周期/成本资格。

## 已执行的有界对照

| 记录 | 实际Unity exit | debug错误 | 判断 |
| --- | --- | --- | --- |
| control-original：原Ho，无新增实例 | 0 | 0 | 基线 |
| initial-five-original：原Ho，初始5个实例 | 0 | 0 | 初始构建干净 |
| growth-controls-original：原Ho，共享/独立源3→5 | 各0 | 各1 | 不依赖候选mirror |
| deferred-growth-original：分Editor tick增长 | 0 | 1 | 延迟未解决 |
| rejected-scratch / rejected-shared-scratch：mirror | 各0 | 各1 | 功能完成不能代替debug资格 |
| rejected-immutable-mode / rejected-as-recreate | 全0 | 每进程1 | 改Static或重建AS无收益，已撤回 |
| rejected-fastbuild / rejected-minmemory | 各0 | 各1 | 构建偏好无收益，已撤回 |
| rejected-initial-update：省略新增geometry update | 0 | 1 | 调用顺序假说失败，已撤回 |
| root run：直接Unity API，初始5个 | 0 | 0 | 不经过HoRTScene |
| root run：直接Unity API，构建3个再增至5个 | 0 | 1 | 同样2560 B scratch范围错误 |

最后两项是Unity 6000.3.15f1、RTX 4060、D3D12 debug层的实际进程19372/41084。最小入口在`Editor/RayTracing/HoRTAccelerationGrowthProbe.cs`，不创建HoRTScene、GI、材质或renderer feature。它只注册原生mesh instance、build，再通过ReadPixels等待同队列提交完成；不证明光线命中/照明正确。初始与增长各独立进程。尚未定位Unity内部scratch分配/驱动具体根因，不宣称已确认上游bug。

`run_validation.ps1`当前运行这两个最小入口。**预期脚本因第二项debug错误退出1**；`report.pass=true`仅表示API调用完成，绝不代表D3D12资格通过。早期runner为对照暂时没有debug拒绝，但原始debugErrors=1保留，所有失败均在此明确拒绝。`results/capability.json`是早期只读clone能力报告，原始run元数据被覆盖，不能独立作为合格验收；没有补写其进程出处。

## 精确证据与重开条件

每次实际run保留exit、fresh、marker、源码/报告/日志SHA。`source-index.json`映射SHA到去重的逐字节源码；`source-files.json`保留原归档路径映射；完整日志在`logs/`，原路径对应`log-index.json`。原生DLL仅记录SHA。`rejected-candidate.patch`可在017基线上review/apply，候选完整原始字节仍在source blobs中；它包括最后失败的调用顺序试验，不应直接接入生产。

`review.json`记录恢复文件、基线commit与patch SHA。运行 `python Tests~/RTGI/StaticMesh/verify_validation.py` 核对15个实际进程、debug范围（原生及其余2560 B，FastBuild对照2432 B）、原生0/1对照、原始报告/SHA、产品精确恢复。这里的verifier通过表示**拒绝/恢复证据自洽**，不是static功能通过。

F39禁止继续盲调Static/Dynamic、PreferFastBuild/MinimizeMemory、重建AS、延后tick或省略新增update；重开需要新的原生分配诊断、明确host/引擎修复，或有合理机制且debug层0的新方案。不能通过跳过静态几何、关闭debug层或只测初始实例来宣称支持。后续先在017已确认合法的古堡AS域做独立seed和复用模式质量对照；静态base支持缺口继续显式保留。

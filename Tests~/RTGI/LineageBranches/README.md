# 043 — GI来源边界：事件与场景见证，两后端原输出不变

2026-10-23。继042常规lineage后，补可达边界。Runtime/vendor/DI/NRD/生产重置策略未改。私有Temporal clone只记录分支事件；新增Editor固定场景和安全重建，非新的滤波/采样参数候选。

## 实际执行与结果

47056短资格40帧、48524 RG及17244 compatibility各80帧，all actual0、D3D12/compile/Shaderwarning/绑定/UAV/fence/source drift0。64²古堡18灯/环境/Cutout，原版/记录版同设置/seed/相机。五fixture各8/16帧：age limit2、脚本相机平移/两帧转向、primary cullingMask0后恢复、finiteDistance.01+direct-sky的远处黑结果、同集合feature服务重建。

每正式后端983040个原GI32包及全部raw/NRD RGBA逐位同，1,245,184次immutable来源/meta/position bits/age/stamp检查。history对pre-S、lastShade对Spatial1仍对应；无效帧M0/meta0/origin0，恢复有效输入后找不到旧有效primary而走noMatch；服务重建第2代所有有效来源均来自重建后，旧ledger/buffer不会继承。

主要正式事件（RG/compat一致，数字是有该位的pixel/frame，不是互斥路径数）：

| fixture | 见证 |
| --- | --- |
| maxHistory2 | ageRejected16687，JacobianRejected44；独立核ageBefore+1>maxAge |
| 相机平移/转向 | JacobianRejected981、materialRejected27、neighborRejected6961、noMatch4556、projectionInvalid8192；核原jac值非有限/>10/<.1才reject |
| invalid→恢复 | kind0 16384、primaryInvalid16384、previousSurfaceInvalid4096/noMatch4096；失效期间全部包M0/metadata0 |
| 有限距离黑结果 | kind3 58529，原packet包含黑样本M；没有丢掉黑结果抬高能量 |
| 服务重建 | generation1→2；重建后来源不早于重建帧，首帧temporalDisabled，输出仍同 |

maxHistory2、有限range/direct-sky仅固定双路径fixture，生产默认不变。重建在前帧GPU读回完成后清hook/释放记录，重建两GI服务并同步seed，正常后续验证重新取得新control context；不是F39实例3→5增长修复，没有盲试旧flags。

## 可达性，不用打开新选项凑覆盖

生产参数零初始化且没有设置enableFallbackSampling/permutation，activeCheckerboardField0；SDK可选fallback/permutation未开启，本轮不称执行过。

previousReservoirInvalid事件为0。当前完整生产者契约下：有效primary生成必留flags1/2/3并MakeM1（黑样本也计数），T/S包含该有效current input并累计M，history来自该结果；新/resize/无效history先禁temporal。因此匹配有效previous primary却M0储库不是正常生产者预期。此是source invariant审计，**不是**该防御分支执行或对未来外部/损坏buffer的普遍证明，见reachability.json。当前新增material拒绝实际27个，不再误记“material未覆盖”。

本步资格针对记录传播和事件，不是全状态空间证明。没有真实secondary transport replay/动态confidence、灯/环境/材质变化replay、Player动态MC/噪声或1080预算接受。生产motion48/48 reset仍保留。

## 实现、数据和成本

单RW uint4记录buf追加N个事件word，每像素reset entry并记录17bit，匹配时另存ageBefore/limit/rawJacobian。低级SDK决定与原数学不变、原文件不改；事件在private helper输出，不能靠输出位置反查或主观推测分支。

HGE1 16B header，帧前缀mode/shader/application/generation四int，然后沿HGL1的5×N GI32、5×N uint4 meta/origin、原3pool GI32，末N个uint4事件；64²每帧1769488B。完整原流和逐帧SHA保留，Python独立重算所有source/事件条件。事件区域比042加65536B，诊断逻辑buffer共2031616B=1.9375MiB，不含额外GI AS/NRD/engine/读回，非产品显存/时间预算。

272源对象、3完整logs、6report/stream lossless归档（大流xz、原raw SHA核验），无GPU失败或降级warning。current源码对应compat-run；旧042归档只在其源版本复核，不能回滚当前代码凑旧SHA。

```powershell
python Tests~/RTGI/LineageBranches/verify_validation.py
python Tests~/RTGI/LineageBranches/analyze.py --mode matrix
python Tests~/RTGI/LineageBranches/generate.py
pwsh -NoProfile -File Tests~/RTGI/LineageBranches/run_validation.ps1 -Mode qualification
```

下一044应推进真实currentTLAS同DI选择的静态secondary重求/oracle，先让所选来源的材质/原normal/source-viewDir可重建、匹配GI codec/SignalScale64/旧权重；可在64²临时丰富诊断记录但要明示成本，不把toy/lineage合格当transport已证。原零/alpha/endpoint和两段visibility随后补充，不能新RNG或仅开区间可见性，正确性/变化像素/噪声/能量/成本之前不放开NRD历史。全部质量/F39/static/动态/环境/1080预算及完整目标active，局部phi/归一化/迭代方向仍搁置。

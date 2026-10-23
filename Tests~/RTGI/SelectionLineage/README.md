# 042 — 真实GI T/S/Final选择来源观察：两后端原输出逐位不变

2026-10-23。原版和实验版两个GI feature共享同相机、场景、设置、seed及原生材质路径；实验版只增加来源记录。合法古堡64²、18灯/环境/Cutout，生产Runtime/vendor/DI/NRD/重置策略均未改。新增Editor诊断是本步可保留收益，不宣称动态收敛已修。

## 实际数据与资格

49456短资格actual0：4mode×8frame=32；45856 RG正式、47228 compatibility正式actual0：各4mode×32frame=128。D3D12/compile/Shader warning/绑定/UAV/fence/drift均0，进程终态/active=null。四种T/S模式全部覆盖，首帧显式失效、后续普通temporal previous/current及spatial邻居选样有真实见证。

每正式后端对照1,572,864个原GI32 packet（有效64²×3pool×128frame），逐位0差；raw和物理NRD publication RGBA所有像素亦逐位0差并留整帧SHA。2,097,152次不可变initial ledger检查/后端：不按float匹配，查(origin frame,pixel)后核metadata逐word、位置原始bits、age及stamp。T-only/TS选previous分别116198/116328次，S-only/TS改变origin71507/47244次。pre-S历史0与Temporal2（S-off则对应Spatial1）、实际lastShade与Spatial1均包/metadata/origin全同。

使用同实际SDK T/S/Combine，私有helper只在寄存器增加source-pointer：Load标当前源地址，Combine选择分支继承，Store复制记录；初始记录直接取真实DI reservoir lightData/UV/invPdf/source-viewDir，无有效DI则UV域保留kind。另origin stamp是审计数据，不是生产必需负担。Final显式拷pre-S metadata，并捕获实际Spatial1 lastShade，避免把两个历史域混淆。原有GI32外存格式未动，原SDK许可/源保留；generator五份私有source、逐anchor patch和原/派生SHA可review。

**新观察：** 每后端105670个所选normal word与initial来源不同，但实验/原版完全同，是原SDK编码往返；LogLuv word差0。不能要求selected normal word永远等initial，也不能据此反查来源。后续重求须匹配原量化链/当前secondary，不把本步等价扩成任意场景量化保证。

## 两次失败原样保留

- 49216 actual1：拷贝Shader把HLSL interpolation关键字linear当局部变量，4 compile error；private FinalNRD还有12UAV warning。未写stage buffer使来源见证为空；初runner错误查256报告，fresh=false，实际64报告/partial binary手动原字节保存initial-capture。原metadata不补写fresh/pass。
- 38748 actual1：修pixelIndex、报告路径和readback后编译检查后，12UAV warning仍拒绝。未为D3D12特赦新warning。
- 改为一个RWStructuredBuffer<uint4>承载meta/origin/lastShade，减少5个额外UAV为1个，FinalNRD不再警告；不改变原数学。49456及正式两后端资格才有效。无效记录为空和编译诊断都会立即失败。

## 覆盖边界和下一步

本轮initial kinds只有1/2。**不声称穷尽**年龄上限/Jacobian拒绝/disocclusion/camera-motion fallback、invalid/black kind0/3、世界/灯/alpha/Volume/resize/资源重建所有分支；pre-review的全面分支目标尚未完全证明。这些要用针对性场景/事件见证补齐，不能靠本轮pass补发。也没有当前TLAS transport重求、confidence/NRD保历史、Player动态MC或1080预算。

043先补上述真实分支/生命周期观察，保持同源原包/输出严格对照，不改采样判据来过测试。之后才用小尺寸诊断材质记录做原DI选择的静态当前TLAS重求和独立oracle（source-viewDir量化、secondary有效性、零→亮/双段visibility），不能新RNG替代或只测开区间射线。只有选择关联、实际重求、变化像素残影/噪声/能量及成本都证明后才考虑NRD生命周期；GI reservoir保守失效继续，局部phi/归一化/迭代线仍搁置。

## 成本、归档、复核

诊断lineage/origin/lastShade/5stage capture逻辑buffer payload1,966,080B=1.875MiB，**不含第二套GI AS/NRD/engine/readback**，非产品显存/时间预算。041的190.55MiB/1080候选也未实现/未证充分，本轮无1080分配。

完整HGL1流：magic+int width/height/framesPerMode(16B)；每帧mode/shader/application三int，然后5×4096个GI32、5×4096个uint4 metadata、同量origin、原版3pool GI32。每帧1,703,948B，原packet32 offset沿041-v2。64²为16整数块，无未填padding被误算。每frame原数据全保留，Python独立解码所有包/来源，不只相信C#pass。

278精确source对象、5完整logs、10份report/stream无损归档；大流xz（总证据约42.3MiB）按原raw SHA复核，历史两次失败源码独立留存。初捕获partial stream不作有效来源证据；不用覆盖旧失败凑绿。source-index可复用旧source blobs/Git对象；current source是compat-run，旧版本按自己的snapshot。仅Editor工具与RTGI文档白名单，Ho-GD计划排除。

```powershell
python Tests~/RTGI/SelectionLineage/verify_validation.py
python Tests~/RTGI/SelectionLineage/analyze.py --mode matrix
python Tests~/RTGI/SelectionLineage/analyze.py --mode compat
python Tests~/RTGI/SelectionLineage/generate.py
pwsh -NoProfile -File Tests~/RTGI/SelectionLineage/run_validation.ps1 -Mode qualification
```

生成只写专用验证项目Assets。不要把这些诊断辅助buf/Shader和部分覆盖直接当生产confidence实现。完整目标、F39/static/质量/复杂动态/环境/1080全帧预算继续active。

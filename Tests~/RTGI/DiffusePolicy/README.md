# 037 — GI同源对照：DI diffuse bundle有总体收益，但旧点退化，未接入生产

2026-10-23。原生产NRD5/scale64/GI32F保持。实验只Editor独立branch1管线9/10，branch0与main原版共享实际GI输入/common/settings，资源/历史/OUT独立。原vendor、DI、Runtime均无改动。固定一组DI成熟diffuse策略，不扫描参数、不带specular/F0/material guide。

## 做法与资格

`build_policy.py`从原NRD重建DIFF-only body：Smem方差预模糊只平均各样本temporal variance、w²传播；后续min pair variance与RGB contrast控制带宽、被拒Gaussian权重留center。两个`.patch`是完整源码差异，`policy-source.json`含原版/DI/派生SHA。实际shader与工具原字节在`source-index.json`/`source-files.json`（可复用旧source-blobs/Git对象），不是仅留伪码。重建器当前只写忽略的`reconstructed/`，不会恢复实验覆盖。

四个自有Unity进程均actual0：29048小64²/64帧；17236真实场景短98请求/32观察帧；31812正式1314请求/1024观察帧；13432过滤后stock98请求/32观察帧。API/D3D12 debug/Shader/CS/绑定/UAV/fence/source drift均0，activeProcess均null。资格期间baseline branch0 RGBA逐帧bitexact；候选每帧确实改变，独立shader/kernel/hook/pool/OUT均恢复释放。正式含warm1280次实际callback，候选Native9×1280、10×5120，设置仍5轮。

物理发布使用实际GPU RGBA16F resolve（scale/clamp、当前packed albedo和valid与生产一致）。原版branch0逐帧与生产发布校准共1024帧全部bitexact，之后才评分候选物理输出。完整raw均值/逐帧raw相同，stock全raw+pub均值图仍对026逐字节同。本轮比较不依赖跨进程raytrace恰好相同。

## 结果和取舍

固定seed0/104729/5347609/6396185，64warm/256观察。新18 MC合格点reference normalized L1 .462545→.266596（改善42.36%），旧15合格点 .304146→.208852（改善31.33%）。全图能量原门槛2→4/4通过，pooled RGB偏差−4.167/−4.035/−3.671%；各seed16相位失败7/11/11/7→3/3/8/2，没有pass→fail，pooled仍3相位失败。filtered方差约2.94/3.55/8.49/3.17倍原版，filtered/raw .00496～.00744，过静态噪声筛选但不代表真实动态可接受。

**按预设门槛拒绝生产接纳**：旧(196,141)B误差增6.299e−6>界5.913e−6；(141,157)G增2.171e−5>1.891e−5、B增8.084e−6>5e−6。两处B/reference分别.9435→.8732、.9963→.9126。不能用总体误差和全图变好掩盖旧控制退化。`analysis.json`列全部34点、每seed/相位/方差、配对SE和门槛；旧dark仍未合格，不算通过。四seed经验SE不是正式置信保证，同漫反射模型参考不等于独立真值/全图MC。

实验新增类/meta、五shader/meta和入口/覆盖在保存原字节后移除；三个既有Editor文件恢复85ecb7a版本。过滤后原版/no-op32帧RGBA bitexact。没有继续8seed/HDR/Player/GPU资格，没有宣称修复产品画质。`filter-review.json`记录范围和取舍。原F39/static/复杂动态/1080完整预算与总目标继续active。

## 复核与接手

```powershell
python Tests~/RTGI/DiffusePolicy/verify_validation.py
python Tests~/RTGI/DiffusePolicy/analyze.py
python Tests~/RTGI/DiffusePolicy/build_policy.py
pwsh -NoProfile -File Tests~/RTGI/DiffusePolicy/run_filtered.ps1
```

`run_validation.ps1`保留历史编排内容但主动拒绝启动已搁置入口；历史候选要在隔离项目按source-index恢复原字节，不能回滚当前产品凑旧SHA。`filtered-run.json`是当前源码资格，旧small/qualification/matrix只按各自归档源重建。40份真实结果/二进制lossless gzip、4完整logs、272个源对象都核原SHA；约20.6MiB，不含重复原生DLL。HGC1局部流：magic+4int(width,height,seed,count27)+27×int2坐标，每观察帧shader/application两int+27×float3物理发布。HGI1全图格式沿026。全部二进制mean从真实GPU publication累积，没有补造数据。

038先作同源stock/该DI-derived策略的**阶段观察**：保留原27点并加入旧196141（至少28点），读Pre/Temporal/Clamp/首Atrous和每个后续ordinal的实际RGB/variance，与物理pub一起解释旧G/B退化出现在哪轮、哪些后续传播原本有帮助。先短observer资格，现有037原版/候选输出必须不受观察影响；再有必要的有限固定seed，不重跑MC/完整旧矩阵。仅有新机制证据后才选下一候选，不按坐标特判、不混spec、不扫phi/epsilon/迭代/ratio，也不以总体收益重开直接接纳本策略。

# 044 — 当前TLAS同次级DI选择静态重求：精确方向缓存编码一致

2026-10-23。64²真实古堡18灯/环境/Cutout，原版与只增加观察/重求的私有GI严格同源对照。Runtime/vendor/DI/NRD/reset未改，仍不接纳动态confidence或保历史策略。

## 实现和静态资格

只在诊断记录里暂存原selected secondary48B和原source-viewDir精确float3，沿042已证source-pointer传播；保留原packed16方向作独立比较。记录仍单RW uint4 buf。独立CaptureReplaySource raygen在原GenerateSecondary之后读取原输入，不在原着色路径新增精确方向局部量。

原FinalVisibility结束后、Final之前，native ReplaySelected对Spatial1选择：重建原材质/normal/emission、原DI lightData/UV/invPdf，使用当前TLAS重求secondary->light可见性，再按原BRDF权重和pdf求radiance，**没有新RNG/RIS**。另外重求primary->cachedPoint开区间visibility，与原TraceFinalVisibility值比较。后者不证明endpoint仍有当前geometry；本步仅静态世界，端点和运动变化后续验证。

35372短4mode×4frame=16、43728 RG/34696 compatibility各4mode×16frame=64，actual0、无compile/warning/Property/UAV/ASdebug/fence/drift、资源hook恢复。所有原3pool GI32与全部raw/物理NRD RGBA对未改原版逐bit同。每正式后端262144个selected sample，**exact view replay编码LogLuv与缓存全部bitexact**，primary open-interval可见性0差；实际解析灯选择107499、环境选择135571，不是只有空值通过。

16bit packed view并非严格等价：每后端2756个LogLuv word不同，radiance max absolute差.0159502、relative p99 .00030657/max .00156663。此差异单独保留，不以量化版本代替exact gate，不声称16B生产记录已足够。secondary端BRDF包含原有GGX方向项，原source primary方向不能换为重采样后receiver方向。

## 独立CPU oracle的覆盖和界限

double CPU自行解octnormal、8bit材质、Lambert/GGX/Smith/Schlick，再用真实sample radiance、DI invPdf/pdf和visibility重组；每component要求max(5e-6,abs(reference)*.003)。正式max abs误差.000818014，但所有component均在该绝对/相对结合门槛内（不能只把5e-6当总上限）。解析灯用实际light buffer另核距离/范围/spot attenuation；max abs .0000413005、0超界；环境UV方向和解析方向maxabs7.628e−7<1e−5。两后端结果相同。

GPU visibility和环境纹理sample radiance仍是实测输入，**不是独立CPU TLAS/alpha/环境纹理oracle**。该oracle核BRDF/解析光/方向/组合算式及记录一致性，没有替代动态geometry/endpoint正确性。

## 两次严格parity失败与观察隔离

46880 initial actual1：前2帧原包/输出和精确replay一致，第1002帧packet1293原GI32严格比较失败。47620 capture-only actual1：禁额外replay dispatch、同编译enrichedShader，仍同帧同包失败；position/M/weight/normal一致，仅packed radiance1176951723 vs1176689579。不无证归因额外射线或驱动，也不放宽原包gate。

最初把exact-view/材质写在原Shade中；移到独立Generate后观察dispatch，原Shading路径恢复042原局部计算，才得到后续全部严格parity。特定编译/排序根因未进一步单独证明，保留原源/partial binary/对照与失败证据。初partial2frame oracle仅作失败诊断，不补发整组资格。Capture-only未dispatch的replay buffer不作transport证据。

## 格式、成本和复核

HGR1 16B header，与HGL1每frame前缀及packet/meta/origin/control相同，末追加N×4uint4选中来源材质+exactView、N×8uint4重求输出（cached/exact/quant编码、RGB/visibility、sample radiance/pdf、L/distance、GPU BRDF/invPdf、primaryVisibility、view）。64²每frame2490380B。全部真实frame流lossless xz按原raw SHA复核，失败独立原版本留存；281源对象、5完整logs、10report/stream。current源是compat，旧initial/capture-only按其归档源重建。

enriched/capture/replay逻辑buffer3538944B=3.375MiB/64²，不含第二GI AS/NRD/engine、额外射线/读回，非产品显存/全帧时间。shader扩展仅Editor生成验证Assets，原SDK许可保留，产品不启用诊断。

```powershell
python Tests~/RTGI/SecondaryReplay/verify_validation.py
python Tests~/RTGI/SecondaryReplay/analyze.py --mode matrix
python Tests~/RTGI/SecondaryReplay/generate.py
pwsh -NoProfile -File Tests~/RTGI/SecondaryReplay/run_validation.ps1 -Mode qualification
```

下一045应从“静态缓存重求一致”推进current secondary endpoint重建/normal-material匹配：用previous actual lastShade和当前primary、原choice与weight，在受控固定实例移动secondary/遮挡、零→亮/alpha等几何oracle中判别失效，不能只追到旧位置的开区间或沿用旧材质就声称当前transport有效。先正确性/响应/噪声/能量/成本，再考虑NRD生命周期；GI reservoir保守失效/旧色Volume硬重置保持。全质量/F39/static/动态/环境/1080预算与目标active，局部滤波公式线仍搁置。

# 041 — GI同采样重求的必要信息与真实SDK GPU合约

2026-10-23。生产Runtime/Shader/vendor/NRD/重置策略不变。本步新增小型Editor合约测试、修正未来CPU诊断标签，尚未实现真实场景GI重求、T/S来源继承或动态confidence。

## 缺口不是改一个reset条件

已读当前HoGIShadeSecondary、secondary48B、SDK GI32与T/S/Final。secondary DI选中的light/UV/invPdf只在局部变量，最终GI包只留位置/normal/LogLuv聚合辐射/GI weight/M/age；没有次级DI选择或source pixel。当前secondary48B是本帧初始pixel缓存，不跟随GI选样。NRD最终信号用Spatial1，temporal history0却保留pre-S2，不可互换。

保存实际选中选择与原权重，比尝试重新生成旧8+4候选/RIS/RNG更直接；但还须原secondary BRDF source-viewDir和sample kind，实际选样链/最后Shade对应、当前secondary有效性、两段可见性与单位/量化，见[实现前契约](contract.md)。该契约是下一阶段要求，不是已实现功能。

## 小GPU实测与独立oracle

20788初运行actual1：HLSL保留字point被用作局部变量，3个编译错误，未写buffer后触发CPU检查。它**不是ABI故障证据**；原源/日志/report/原binary保留initial-compile。修为lightPosition，并关闭异步编译、在dispatch/readback后再核编译信息。

45904实际D3D12 Unity6000.3.15f1运行actual0，原SDK Pack/Unpack/Combine直接参与，64案例、0Shader/CS/D3D12/绑定/UAV/fence/source drift错误。两个诊断buffer逻辑payload共75,776B（非总GPU分配/VRAM），无AS/场景采样/NRD dispatch。

- 32个案例中不同旧次级灯选择产生**完全相同GI32字节**。加入矩形遮挡后，同一旧选择0应为0、旧选择1仍约(.455528,.114329,.028582)：单凭GI包不能分辨该如何重求。
- 原SDK显式Pack可写misc高16位，普通Load/Store在64案例全部丢该应用misc；reserved unused word也全部归0。不能写入空闲word后假设高层T/S自动继承。
- SDK Combine实际quantile选择B为48/64，返回的select bool可驱动显式aux ledger。四word不透明sentinel随选择复制；这是独立字段承载测试，ledger的7.5等sentinel不作为transport oracle的权重2，**没有证明真实Shade/高层T/S的元数据关联**。
- 同旧选择静态重求64案例RGB逐bit同。独立CPU用z=-1平面交点/矩形范围、double Lambert；GPU用AABB slab，最大绝对误差3.674e−8<1e−6。
- 负对照故意换另一个选择：32个不对称灯静态案例制造假变化；相同旧GI包的案例中16个漏掉实际变化、16个制造假变化。是确定性反例，不是实际RNG采样的统计概率。

模型只含点灯Lambert和矩形遮挡；UV/viewDir验证字段承载，没有area/environment/specular transport或真正TLAS/alpha验证。不能用本步pass声称运动降噪已修复。原040的48/48 reset和全部质量/性能缺口仍在。

## 修正一个真实诊断问题

SDK GI32实际：position0、misc/M/age12、LogLuv radiance16、weight20、normal24、unused28。旧CPU HistoryFeedback.Packet/RuntimeProbe.Packet把12标normal、16标misc、24标radiance。未来结构/JSON新增GI32-v2并按SDK修正，实际GPU sentinel同时验证两CPU布局；1024次逐word翻转验证两个Equal/Same都覆盖完整32字节。

**历史边界：** 全包byte equality、packed source复制以及RenderTexture raw/filtered/MC质量数值仍有效；旧normalMismatches实为misc、旧radianceMismatches实为normal，全局真实radiance mismatch未单独记录。只有全包0 mismatch时才能据此说所有字段都同。历史原源/报告不改；两个历史README新增提示，完整映射[legacy-layout-review.json](legacy-layout-review.json)。

## 下一步和成本

先在低分辨率做GI私有来源/sidecar **观察资格**，明确初始、所有temporal fallback/spatial选择、S-off、pre-S history copy和实际Spatial1 last-shade捕获；须主输出/原字段对照及碰撞样本证明。SDK文件/DI不改，不能按float坐标/颜色反查来源。之后才当前TLAS同旧选择重求、独立oracle和变化像素门槛。几何运动仍保守拒绝GI reservoir，未验证confidence前不放开NRD历史。

假定16B sidecar×3pool+48B last-shade：256²额外6MiB，1920×1080约190.55MiB/相机，未含confidence/gradient/对齐/原生分配，也未证明16B足够；不能直接开1080分配或当产品预算。优先资格/生命周期/成本，再谈历史策略，不重开phi/归一化/迭代公式线。

```powershell
python Tests~/RTGI/ReplayContract/verify_validation.py
python Tests~/RTGI/ReplayContract/analyze.py
pwsh -NoProfile -File Tests~/RTGI/ReplayContract/run_validation.ps1
```

262源对象按原SHA、2完整logs、4report/binary lossless gzip、实际CPU offsets/原失败/初旧布局映射/契约/oracle保留。current source对应qualification，initial按其归档源重建。已按前后review过滤，保留合格诊断/两个CPU标签修正和文档，Runtime/vendor无diff；F39/static/完整画质/动态MC/环境/1080预算与总目标active。

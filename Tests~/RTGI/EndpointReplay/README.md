# 步骤045：上一帧实际 Shade 样本的当前次级端点验证

受控64²平面场景下，上一帧真正Spatial1 Shade的端点、当前材质和两段visibility可用当前TLAS重求，并与独立CPU几何/alpha/解析灯BRDF oracle核对。两后端均检出“到旧点通畅但次级表面不存在”，所以只测primary→旧点开区间不能作为历史有效性。生产Runtime/DI/NRD/reset未改，没有动态降噪收益结论。

## 实际运行与结果

| 运行 | 实际进程 | 帧 | 结果 |
| --- | --- | --- | --- |
| 短资格RG | 48652 | 16 | actual exit0，原输出同，oracle过 |
| 正式RG | 51816 | 32 | actual exit0，原输出同，oracle过 |
| 正式compatibility | 3728 | 32 | actual exit0，原输出同，oracle过 |

compile/Shader warning/AS debug/fence/source drift均0，hook/资源恢复、activeProcess=null。每正式后端核126976个上一帧实际Shade包（31×4096）：位置/M、radiance、DI choice/UV、origin、GI weight；同像素primary位置逐位不变。不是拿当前Spatial1或pre-S history冒称旧历史。每帧原3pool GI32与物理raw/NRD全部RGBA逐位同。

八阶段：静态→次级面移远→恢复→opaque挡片移入→恢复→alpha关闭→alpha重开→次级白变绿。四个AS instance初始固定，excluded shell不进AS，不触发3→5增长。相机/primary静止、红蓝两点灯、环境关闭；无贴图常量材质roughness1/metallic0/F0zero/emission0。

current TLAS从oldP+oldGeoN×4bias沿−oldGeoN探测，TMin=bias/TMax=8bias；位置≤2bias且normalDot≥.9才匹配。使用当前native full-material payload，冻结旧DI choice/UV/invPdf、exact source-view及GI weight，不新采样。测primary→旧点、primary→匹配点及secondary→同灯visibility。Final覆盖lastShade之前读取上一帧，warm-up不读未初始化旧区。

两正式后端统计一致：次级面移走与alpha重开各4096次“旧点通畅而端点不存在”；geometry/status/match/material/visibility全部0错。位置/距离maxabs=1.7881393432617188e−7，normal=8.742277657347586e−8。radiance maxabs=9.628468508182664e−5，在预设逐分量max(5e−6,.003×abs(doubleReference))内0超界。

## 错误模型与修正

最初CPU仅Lambert，误以为F0=0消除全部specular，预言绿表面在红蓝灯下严格零。Schlick仍有(1−VoH)^5掠射项，roughness1仍执行GGX。初模型blocker/alpha/green超界115/4053/3755，几何/visibility已通过。原模型和SHA保留于initial-lambert-oracle.py.txt及oracle-model-review.json。

修正独立CPU roughness1 GGX：D=1/π，G=2NoL/(NoV+NoL)，F=(1−VoH)^5，response=baseColor×NoL/π+F×G/(4π)。GPU夹具/Shader/原数据/阈值未改。错误预言明确拒绝，不能改夹具去迎合预言。

## 范围、成本和接手

CPU独立求四有限矩形面/alpha交点、两段visibility、点灯衰减和BRDF，不把GPU visibility作为oracle。仅这些平面/常量材质/静止primary合格；位置法线匹配不证明任意表面身份，移动primary、变形、parallax/视角材质、环境域未覆盖。不能据此保留生产NRD历史。

逻辑buffer4194304B=4MiB；重复GI AS/NRD、额外rays、引擎/读回另计，不是总VRAM/时间/1080预算。产品仍stock5/scale64，局部公式线继续搁置。

先读review-contract.json/filter-review.json。generate.py只生成私有Shader，run_validation.ps1独占私有Unity工程。source/log/capture索引保存精确源、完整日志及原report/stream SHA；大流xz无损。复核：

```powershell
python Tests~/RTGI/EndpointReplay/verify_validation.py
```

verifier核所有SHA、逐帧原输出、真实上一帧关系、CPU几何/alpha/BRDF及阶段，并查当前源码漂移。下一步限定current-primary对应与同采样变化信号；正确性和变化像素响应/噪声/能量/成本后才评历史生命周期，保守GI reservoir及旧色/Volume重置继续。

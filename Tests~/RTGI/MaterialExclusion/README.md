# 步骤007：GI材质身份过滤

沿DI第80步与共享HoRTScene接口，为GI独立设置`excludedRayMaterials`，默认空。只改GI Settings、调用接线和高级Inspector；未改共享registry、SDK、NRD或shader算法。

`../Composite/run_validation.ps1 -MaterialExclusion`正式6项实际exit0、marker/fresh report成功、shader/C#编译诊断0。四组Linear/Gamma×RG/compat各12请求/21检查，另有完整空列表Composite回归及Gamma NRD compatibility回归。`results/run.json`包含来源HEAD035b9f9、五份实际改动源码SHA、PID/exit/log/report SHA；运行完恢复专用工程色彩空间设置，无活进程。

夹具为一个真实合并MeshRenderer中的前遮挡壳、后红emission面。camera只绘制主接收面，GI世界仍包含两块子网格。只过滤壳后，raw从0变为约(1,0,.001)，filtered与正常camera实际增加红贡献；清列表后两者首帧归零。同名不同Material引用不会误排；null/重复项、恢复新identity/保留原identity、reservoir/NRD reset、源mesh/material/raster属性不变都已检查。完全排除skin没有创建pose snapshot/Bake，仅证明过滤接线，没有宣称GI骨骼材质/动态全部支持。

保留失败：`rejected-single-pixel-spatial.json`与`smoke.json`。首轮把启用空间重采样后的单像素随机值当解析常量，得到.928而非1；本夹具改为关闭空间复用、保留temporal/NRD，验证过滤的解析贡献，门槛.03未放宽，生产采样默认与增益未变。复用/空间质量由步骤006等独立夹具负责，不根据该误用诊断修改GI算法。

本步验收为Editor两条实际生产路径；没有重新打包加入非空列表的GI Player、导入完整古堡或声称GPU毫秒收益。两个Mono Player的既有基础链路证据见Dynamic，但本次新设置的非空Player序列化仍应在完整矩阵补验。

排掉submesh不代表压缩合并mesh。部分排除skinned对象仍保留其完整pose snapshot；每帧按身份过滤使用共享既有CPU逻辑，未测具体成本。列表影响所有共享该Material引用的实例，并允许光穿过它们；不能当作仅改UI。

Cutout没有在本步接入：shared inline query明确CULL_NON_OPAQUE，DI有独立native最终可见性，GI secondary/current-final/secondary-light还需一致适配。只登记Cutout会产生不一致，已在过程与禁试说明，不通过开一个predicate冒称完成。

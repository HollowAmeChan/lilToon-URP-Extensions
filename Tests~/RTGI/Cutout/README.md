# 步骤008：基础Cutout完整GI路径

接纳范围为静态基础lilToon Cutout，不是只登记submesh。初始/远天空TraceRay去除FORCE_OPAQUE，GI私有any-hit执行与DI相同的Main alpha、AlphaMask0～4、静态ST/旋转及背面UV规则。有Cutout时，次级照明、时间/空间bias visibility与最终连接使用原生TraceRay真实alpha；没有Cutout保留原compute/inline路径。采样/重采样函数抽到共享头，SDK/NRD/vendor body未修改。

## 正式结果

`results/run.json`：10个实际Editor进程exit0、marker/fresh/SHA成功，shader/C#编译诊断0。两颜色空间的单射线测试、Linear/Gamma×RG/compat完整链路四组，以及opaque环境两路径/opaque材质/DI native Cutout回归。四组完整测试各284请求、17checks。

- 1024条冻结的有效后墙样本连接，与独立CPU线段交点、实际mesh UV和nearest纹理采样一致。512条实体alpha遮挡均无raw漏光，512条孔洞保留贡献；最终可见性距离和NRD alpha距离与实际遮挡面/后墙一致。
- 注入只用于最终阶段隔离，不计入估计器测量；恢复真实采样并清历史后，半开周期孔洞的独立积分为.5，四组ReSTIR raw均为`.4869144`（-2.62%，原5%门槛通过），filtered约`.4873～.4891`。
- 次级天空约`.36054`，透明Cutout保持约`.35986`；实体Cutout挡在主射线后方时，次级天空归零。次级方向光无Cutout参考约`.15574`、透明约`.15455`、实体归零。
- 有限1000反弹范围以外、1500单位处的实体Cutout仍挡直接天空；孔洞能透过。
- 最后Cutout移除后，全分辨率可见性buffer释放为单元素dummy。

`player-run.json`：两套Linear D3D12 Win64 Mono release构建及实际EXE均exit0，每条309次相机请求、304条有效应用帧记录、22checks。透明Cutout下原版GI/NRD/空间质量/动态/合成继续通过；实体alpha首帧清旧蓝光，恢复孔洞首帧恢复贡献，排除最后Cutout切回opaque并释放buffer，恢复实体及禁用Renderer均有实际验证。一个会挡住全场的壳材质在Feature中**非空序列化排除**，同时证明步骤007设置在Player实际可达。

每个真实Update一次SubmitRenderRequest，帧号/时间严格递增，仍不等同正常enabled-camera自动呈现。Player空间L1约10.4%、红蓝均值约-3%，原5%能量门槛保留；未为Cutout调NRD参数。

## 复现与来源

```powershell
& Tests~/RTGI/Cutout/run_validation.ps1
# Player要求专用工程manifest已恢复到实际工作区，MonoScript绑定正确
& Tests~/RTGI/Dynamic/run_validation.ps1 -Cutout
python Tests~/RTGI/Cutout/verify_validation.py
```

Editor因并行Dust施工出现C#错误而使用隔离包：base82df540加124份GI/NRD/RayTracing覆盖，`snapshot.json`与实际文件SHA可核对。快照MonoScript空映射仍按F19搁置，不能用来构建Player。正式Player改回实际工作区，base6be5bd9加本步GI代码，保留builder/probe和完整payload SHA。复用runner一度保留step006标签；原metadata在`player-run-original-label.json`，canonical只纠正步骤标识，不改进程/报告；之后runner已区分step008日志名。

`verify_validation.py`核对不可变Editor快照、当前Runtime源码、10次真实退出/报告、两Player及全部payload、源hash、GUID和真实帧。后续源码变化时按对应历史提交恢复或明确新验证输入，不能反向覆盖新工作来满足旧verifier。

## 保留失败与复审

- `rejected-alpha-uniforms.json`：LIL_RENDER=0没有AlphaMask数值字段，原型Shader编译失败。只在GI私有适配补显式uniform，未改lilToon/vendor。
- `rejected-hit-converted-material-tag.json`：从opaque改shader的夹具保留Opaque RenderType。按预期域配置TransparentCutout后通过，未放松生产classifier。
- `rejected-solid-initial-black.json`：误认为黑色albedo且F0=0就无反射，测到约.0013。GPU包flags1/z=-.5、diffuse/spec RGB均0、emission0；原版GGX Schlick擦边项仍非零。改成1024条真实命中/位置断言，不改模型/亮度或遮挡能量门槛。
- 并行Dust编译错误的那轮没有fresh report，未算新渲染结果；后续隔离重跑完整矩阵。
- 既有非GI构建警告、Player GB fallback/resolve缺失和退出GraphicsBuffer GC清理警告保留，没有宣称完整生命周期验收。

复审接纳：初始、照明、重采样bias、最终alpha语义一致，孔洞能量/NRD距离/Player有实测。代价为有Cutout时8B/像素（1080p约15.82MiB）可见性/距离buffer、一次额外dispatch及原生hit-group/纹理查询成本；尚无GPU毫秒。opaque只留8B dummy。动画alpha、MPB、多主色/dissolve/dither/视差等域不支持，骨骼GI vertex fetch、完整古堡、自动camera和GPU预算仍待验。

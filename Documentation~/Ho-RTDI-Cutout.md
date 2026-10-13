# Ho-RTDI Cutout 最终可见性

本步补 DI 所需的 lilToon 裁切阴影，不新增 Renderer Feature、PT 框架或最终 NPR 接收设计。重采样继续使用一致的保守可见性，最终着色前增加原生材质射线，读取原纹理和 Unity 三角形 UV，按实际 alpha/cutoff 决定遮挡。

## 路径与依据

用户 UnityPathTracing 的 `RAB_VisibilityTest.hlsl` 区分保守/最终可见性，`Debug/RayTracing.hlsl` 在 anyhit 根据材质 alpha 执行 IgnoreHit。当前 [RTXDI 应用桥契约](https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/RtxdiApplicationBridge.md) 允许保守测试跳过 alpha-test，要求初始样本与重采样偏差校正使用相同规则，指出与最终可见性差异较大时噪声会增加。

- DI 显式登记支持的 Cutout 为非 opaque submesh；默认 HoRTScene/GI 调用不启用这项能力，原不透明支持域保持独立。
- inline query 使用 CULL_NON_OPAQUE；初始候选、时空校正与 BRDF 环境候选采用同一保守规则，不把裁切面整体当成实体面。
- Spatial 后、Shade/ShadeNRD 前，`HoRTDIFinalVisibility.raytrace` 从已选 reservoir 和实际主表面重建同一灯光射线。材质 anyhit 通过 UnityRayTracingMeshUtils 取实际三角形 UV，透明 texel IgnoreHit，遮挡后停止搜索。点灯、聚光灯、方向灯与环境灯共享 RAB 的方向/距离逻辑和 ray bias。
- Shade 使用最终结果计算 raw/NRD 输入和 reservoir visibility，再保存历史。SDK、最终 BRDF 与 NRD 算法未修改。透明洞可透光，alpha 达到 cutoff 的 texel 仍遮挡；相等时保留，匹配 clip(alpha-cutoff)。

Unity 原生 Mesh attribute fetch 与 Material hit group 提供纹理/UV 输入，无纹理副本、UV atlas 或 CPU 纹理读取。Opaque 材质共用一个只报告遮挡的私有 Material；Cutout 的私有 Material 借用源纹理，复制相关材质值，源 raster 材质不变。没有 previous TLAS；几何变动与 alpha 参数/纹理内容/采样状态变化保守清 DI/NRD 历史。

## 支持与成本

接受 `Hidden/lilToonCutout` 系列、TransparentCutout tag、低于 Transparent 的队列、有效 UV0；源 MainTex/AlphaMask 可为 Texture2D 或默认空纹理。支持主纹理 tiling/offset、静态旋转、背面 UV 移动、纹理自身过滤/包裹、Color.a、cutoff，以及 AlphaMask 的替换/乘/加/减、scale/value。AlphaMask 使用 MainTex sampler，匹配本地 lil_common_frag.hlsl。射线显式 LOD 0，尚无 ray-cone/mip 覆盖过滤，AlphaToMask 的 MSAA 覆盖不作为物理透射。

不支持第二/第三主纹理、视差、dissolve、dither、distance-fade alpha、动态 UV 动画、RenderTexture alpha 输入、材质 property block 和任意自定义 vertex/alpha shader。未知情况明确排除，不做 opaque 近似。GI 尚未接入 Cutout secondary shading；其原生 FORCE_OPAQUE 路径只登记原有 opaque 几何，共享 inline flag 的回归另行验证。

只有存在已接受的 Cutout 且平台/序列化原生 shader 可用时，逐相机分配一张 R8_UNorm 最终 visibility（不支持 LoadStore 时 R32_SFloat）：R8 的 720p/1080p 载荷约 0.879/1.978 MiB，R32 回退为四倍。该阶段替代原 Shade 的 inline 最终查询，每个有效已选样本最多一条原生最终射线；新增全屏 dispatch、材质 anyhit/SBT、更多 AS submesh 的成本仍需实际 GPU 测量。无 Cutout 时释放该纹理并沿原路径执行；源纹理不复制。动态 skin 仍使用前步 CPU 快照并清历史。

## 验证

独立 64² 场景避免血月古堡天空球/合并网格干扰：两条路径验证 alpha 0/1、Color.a、cutoff 相等、四种 mask 及 scale/value、纹理 updateCount、骨骼移动/归位与条件资源释放。独立 CPU ray/triangle+纹理采样参考验证 8 组 Point/Bilinear 与混合 U/V Repeat/Clamp/Mirror/MirrorOnce、负 tiling/offset/rotation：4835 遮挡、1335 透光像素通过，102 个 alpha/采样边界像素明确跳过。首次失败来自 float CPU 参考同时漏掉共边三角形；临时 GPU 诊断确认主位置/UV/alpha 正确，修正参考的共边容差后通过，生产诊断 payload/buffer 已全部移除。

仅 Near 被 Cutout 遮挡、Far 可见的双灯场景，512 帧/模式、独立 frame 起点验证无/T/S/TS，raw 能量比例分别约 .996001/1.005337/.996663/1.003256，两条路径一致，原 3% gate 通过。Spot/Directional 全遮挡/全透光通过；独立 Cutout 环境壳闭合输出 0、透明输出约 (1,.5,.25)，环境 NEE/BRDF 提案的最终可见性通过。NRD 全遮挡与透光 5% gate 通过，不把这些简单场景当作复杂局部阴影或性能保证。

新增 `finalVisibilityShader`/`visibilityMaterialShader` 随旧资源修复流程补引用，三独立进程验证保存/重载与既有覆盖保留。Player fixture 引用真实 Cutout 源材质，并用不可 CPU-readable 的源 Mesh/纹理验收原生命中与骨骼阴影。执行入口为 `HoRTDIValidation.RunCutout/RunCutoutCompatibility`，Player 仍使用 Prepare/Build/BuildCompatibility；本轮日志前缀 `rtdi-cutout27-*`。

两个 Win64 Mono release 构建与实际 D3D12 EXE通过：Cutout shadow=0、open≈.222046，原环境/VNDF/曲面/HDR/NRD/混合灯/visibility gate通过，D3D11 inactive通过。旧DI/NRD两条、共享GI两条、rigid/skinned世界求交回归与Roslyn/派生SHA检查通过；未改NRD/native ABI，无本步新增Shader诊断，已有PLR/SSGI编译警告保留。原平面64帧失败继续保留，不据本步声称复杂动态接收或所有场景质量已达标。

## 古堡的验收边界

用户指出古堡有合并在主模型内的球形天空壳，不能据该场景整体照明判断 DI 阴影正确性。原资产仅用于支持审计：全部 16 submesh（其中 12 Cutout）可登记，未产生排除。第80步为DI加入[材质排除列表](Ho-RTDI-MaterialExclusion.md)，`AuditBloodMoonCastleWithoutSkyShell`已改用该接口排除名为“天空”的源材质，不再克隆材质或修改sharedMaterials；登记15 submesh、仍含全部12 Cutout，唯一排除是天空壳。该名称选择仅用于已知fixture，生产列表按材质对象身份匹配。

审计不渲染实际古堡完整画面，也不是性能/漏光验收。自有当前姿态 Mesh 仍约 28.54 MiB，因为合并网格没有拆开；最终接收、艺术阴影控制和 GI 归属继续延期。下一项应在可控场景检查 Cutout 局部边缘、运动与 GPU 成本，结合古堡隔离区域的实际结果再决定优化，不用天空壳或合并布局造成的现象调整采样/降噪参数。

提交前源hash复核发现源场景层级顺序与Volume Profile已有外部更新（新增DI/GI Volume及debug设置），其余复制清单中的模型/材质/纹理/metadata一致。验证保留本轮开始前的隔离快照，不将旧快照回写或覆盖这些源工程更新；不宣称整个源清单仍逐字一致。

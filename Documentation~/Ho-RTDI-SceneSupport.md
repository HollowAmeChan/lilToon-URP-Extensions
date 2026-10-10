# Ho-RTDI 场景支持：血月古堡

本轮使用用户指定的血月古堡，在隔离项目里导入实际资源依赖，保留源 GUID、材质与导入设置。源工程、场景、Renderer 设置均未修改，资产不进入本仓库提交。登记审计与解析灯/遮挡测试分开：前者证明资产能否进入世界集合，后者验证实际硬件求交与 DI 输出，不把登记数量当作完整场景画质验收。

## 发现与本步取舍

实际展开后有 18 盏 Realtime 灯：9 Point、8 Spot、1 Directional，全部无 Cookie、cullingMask=-1，均在已有灯域内。主体 `1_mesh` 是带单骨骼的 SkinnedMeshRenderer，有 15 个 submesh、241,472 个三角形；另有一个 768 三角形的测试球。旧注册仅接受 MeshRenderer，因而整座古堡被排除，只登记测试球。

本步补 SkinnedMeshRenderer 的当前姿态快照与 BLAS 更新。三个不透明主体 submesh（天空 480、古堡 12,716、地板 377 三角形）进入 RT，实例总数由 1 增至 4。其余 12 个 submesh、227,899 个三角形仍是 Cutout，明确排除。台阶、石柱、吊灯、彩窗等大量几何仍缺失，不能宣称场景已经完整支持或不再漏光。名为“天空”的网格是普通不透明遮挡物，不能自动视为 HoAtmosphere 环境输入或 mesh emitter。

因此下一项优先级是 **lilToon Cutout 可见性**，不是更多灯型、ReGIR 或调 NRD。必须在实际候选三角形的 UV 上求正确 alpha，并保留材质的 cutoff/变换/采样语义；不能将当前排除的整个 submesh 当成不透明面。已核对用户参考 `Assets/Shaders/Debug/RayTracing.hlsl` 的 anyhit alpha/cutoff/IgnoreHit，以及 [RTXDI 应用桥](https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/RtxdiApplicationBridge.md) 的最终可见性接口。

## 姿态快照与成本

每个 HoRTScene 为每个参与的不透明 SkinnedMeshRenderer 持有一个当前姿态 Mesh，多个 submesh 共享。比较骨骼相对 Renderer 的矩阵、BlendShape 权重、源 Mesh 与蒙皮质量，变化时调用 `BakeMesh(mesh, true)`，对应实例 `UpdateInstanceGeometry`，在图记录之前推进 Revision。稳定姿态复用快照和 BLAS。源 Mesh 原地修改仍需 `MarkGeometryDirty`；更新、禁用、无效姿态、移除和销毁不会保留旧遮挡物。

`true` 补偿 Renderer 的缩放，由 AS 应用完整 localToWorld；最初的 `false` 原型在非均匀缩放测试中产生双重缩放，已修正并通过镜像/非均匀缩放 gate。依据 [Unity 6.3 BakeMesh](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html) 与 [UpdateInstanceGeometry](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.RayTracingAccelerationStructure.UpdateInstanceGeometry.html)。不强制修改原 Renderer 的可见性、动画或 GPU Skinning 设置。

实际古堡审计中，首次 Synchronize 两次测量约 22.75～77.29 ms，随后每次 64 次静止 Synchronize 平均约 0.038～0.041 ms，追加 Bake 数均为 0。时间包含 CPU 登记/JIT/快照工作，不包含实际 BLAS GPU build、DI dispatch、降噪或完整帧，不是生产性能保证。Unity `Profiler.GetRuntimeMemorySizeLong` 的自有 Mesh 计数为 29,924,936 B，约 28.54 MiB，包含整个姿态 Mesh，不仅是不透明三角形；不包括 AS/驱动资源。

CPU Bake 不适合无成本地扩张到大量逐帧变形角色。当前 DI/GI 各自拥有 HoRTScene，同时启用会各持有快照并分别更新，古堡快照这部分约翻倍。用户参考 `Assets/RTXPT/Scripts/Data/RtxptGeometryCache.cs` 与 `NativeRtxptBuildTlasPass.cs` 使用每个 Renderer 的 GPU 顶点缓冲和 repack；已作为后续替代路径参考，本步未搬入其 Native/PT 框架。动态 pose 清 DI/NRD 历史，不宣称动态角色时域或任意自定义顶点/布料变形已支持。

## 验证与复现

- D3D12 RenderGraph/compatibility 的世界求交：两 submesh、双骨骼与混合权重、BlendShape、独立解析距离、barycentrics/身份、非均匀和镜像缩放、离屏、稳定缓存、源 Mesh 更换、非 CPU-readable Mesh、显式几何编辑、无效骨骼、启停/销毁、图记录后的姿态变动拒绝。
- DI 两条路径：实际自动收集蒙皮遮挡物，骨骼移走恢复点灯能量，归位重新遮挡；原 SDK 采样、灯历史与独立输出 gate 通过。原刚性求交和共享 GI 两条路径回归通过。
- 两个 Win64 Mono release 构建与实际 D3D12 EXE：蒙皮阴影为 0，骨骼移开后的 raw≈0.222046，与解析点灯参考一致；原环境/VNDF、曲面、HDR、NRD、混合灯与 visibility gate 通过。D3D11 inactive 通过。Roslyn Runtime/Editor 编译通过；已有 PLR/SSGI 警告保留。
- 实际古堡仅做展开与登记审计、静止缓存/成本测量，未作其完整画面、性能或最终 NPR 接收验收。已有原平面短运动失败继续保留。Shader/NRD/native ABI 未修改。

`Tests~/RTDI/snapshot_scene.py` 默认只列依赖闭包，`--copy` 才复制，保留 .meta、glTF URI 与材质 remap；禁止源 Assets 内目标、非隔离目标和源码/plugin 复制。需要先准备隔离项目 manifest，包含当前管线、lilToon 和匹配的 UnityGLTF 导入器。本次复制的导入器为 UnityGLTF 2.19.5、来源 commit `ce6a5dce952d84cfd47c81755e7e1b0bc69fefe5`，以及本地 lilToon UnityGLTF 扩展。包 GUID 由该 manifest 提供，缺失的无关脚本不会作为场景完整支持证明。

```powershell
python Tests~/RTDI/snapshot_scene.py --source D:/Unity_Project/BREAK_URP --scene 'Assets/mmd场景测试/血月古堡/血月古堡.unity' --destination D:/Unity_Fork/lilToon-URP-Extensions/.codex-research/RTInputValidation --copy
```

复制清单 `rtdi-scene-snapshot.json` 记录 38 个资产及各文件/metadata 的源 SHA-256；复制后重新运行登记审计通过，源文件 hash 无变化。Unity 6000.3.15f1 用 `-batchmode -force-d3d12`，不使用 `-nographics`：

| executeMethod | 验收 |
| --- | --- |
| `lilToon.URP.Extensions.Editor.RTDI.HoRTDISceneValidation.AuditBloodMoonCastle` | 实际复制场景登记与静止成本，输出项目根目录 `rtdi-blood-moon-castle-audit.json` |
| `lilToon.URP.Extensions.RayTracing.HoRTSceneValidation.RunSkinned` / `RunSkinnedCompatibility` | 硬件求交与姿态生命周期 |
| `lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunSkinnedBlocker` / `RunSkinnedBlockerCompatibility` | DI 与骨骼遮挡更新 |

Player 继续使用 `HoRTDIPlayerValidation.Prepare/Build/BuildCompatibility` 与已有实际 EXE 运行流程，fixture 的遮挡物改为蒙皮网格并增加移走/归位 gate。本轮日志前缀 `rtdi-skin26-*`；测量、复制资源和工程留在 `.codex-research/RTInputValidation`，没有新增 Renderer Feature。

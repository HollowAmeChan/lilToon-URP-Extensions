# HoRTScene：网格求交基建

`HoRTScene` 是 Runtime 内部服务，不是新的 Renderer Feature。首版使用 Unity 原生 AS 和 inline ray query，登记一份与观察相机无关的世界几何集合，提供命中身份与 CPU 材质关联。没有引入 RTXDI SDK、native 插件、降噪、灯光或画面合成。

能力条件为 D3D12 + `SystemInfo.supportsInlineRayTracing`。不支持时不创建 AS/GPU metadata buffer，`IsAvailable` 为 false，登记报告给出原因。它没有屏幕空间或软件 BVH 回退。Unity 提供原生 [inline query 绑定](https://docs.unity3d.com/cn/6000.0/ScriptReference/Rendering.CommandBuffer.SetRayTracingAccelerationStructure.html) 与 [能力检查](https://docs.unity3d.com/cn/6000.0/ScriptReference/SystemInfo-supportsInlineRayTracing.html)，本轮以本地 6000.3.15f1 的实际编译/执行为准。

## 登记与身份

调用 `Synchronize(IReadOnlyList<Renderer>, mask)`，传入世界集合，不传相机 culling results。每个支持的 Renderer/submesh 单独加入 AS，CPU 保存 Renderer、Mesh、submesh、Material 与 RT instance ID 的关系；GPU metadata 为 `uint4(submesh, materialId, primitiveCount, valid)`。

- 支持 MeshRenderer 与 SkinnedMeshRenderer 的不透明三角形 submesh，要求一材质对应一 submesh。源 Mesh 不必开启 CPU Read/Write；刚性路径直接使用源 Mesh，蒙皮路径持有一份当前姿态 Mesh。
- 排除 disabled、inactive、forceRenderingOff、static batch、additional vertex stream、其他 Renderer、缺失材质和非三角形。默认调用仍排除 alpha-test/透明。DI 提供明确的 Cutout 支持 predicate 与材质 hit group 时可登记已知裁切面，见 [Cutout 最终可见性](Ho-RTDI-Cutout.md)；未知/复杂 alpha 仍记录 `Rejections`。
- 这是明确的 opaque、双面几何参考。蒙皮快照包含骨骼与 BlendShape，不执行任意顶点 shader、自定义 GPU 变形、布料、Terrain 转换、fur 或描边壳。不能因此宣称与任意材质的画面几何完全一致。

蒙皮使用 `BakeMesh(mesh, true)` 补偿 Renderer 缩放，由 AS 的 `localToWorld` 应用一次变换。骨骼相对矩阵、BlendShape、源 Mesh 或蒙皮质量变化时更新快照并刷新对应 BLAS；稳定姿态复用。缺失/非有限骨骼姿态移除旧实例，禁用、移除、显式 `MarkGeometryDirty` 与服务销毁释放自有 Mesh。源 Mesh 原地编辑仍须调用 `MarkGeometryDirty`。快照发生在图记录之前，姿态变化推进 Revision，DI/NRD 保守清历史，不提供动态角色 temporal。CPU 蒙皮及完整 Mesh 副本有成本，详见 [场景支持与验收](Ho-RTDI-SceneSupport.md)。

启用 Cutout 的 DI 调用中，inline `HoRTTraceScene` 是只求 opaque 的保守查询，最终 alpha 可见性另由原生材质射线计算；不要用该保守查询冒充最终裁切命中。默认/GI 只登记 opaque，原查询含义与支持域不扩张。
- 不因 `shadowCastingMode=Off` 或材质旧 cast 接收关闭而删除显式登记的 RT 几何；AS 由本服务的集合与 mask 控制，不依赖 Renderer 的旧 rayTracingMode 设置。
- RT instance ID 从 1 分配，在同一服务 Lifetime 内不复用；0 保留。变换、mask、材质替换保留几何身份；移除/重新加入、Mesh 替换、显式几何刷新得到新 ID。
- Material ID 也是本服务内的身份，不是 Unity InstanceID、OB ID 或 GPU 材质参数索引。当前只关联 Material 对象，不上传材质颜色/贴图。

`TryResolve(instanceId, out instance)` 用于 CPU 命中诊断。`Lifetime` 区分服务销毁后重建，`Revision` 标识登记/变换/关联变化；未来历史不能只比较会在新 Lifetime 重新从 1 开始的 ID。metadata table 按已分配 ID 扩容，删除项置零，不立即压缩重用。

## 更新与调度

变换与 mask 在同步时比较并更新；材质替换只更新关联表。集合不变、顺序变化或输入重复不会重新 Build。`BuildCount` 统计已记录的 AS Build 命令，不代替 GPU 时间测量。

Mesh 内容修改调用 `MarkGeometryDirty()`，再重新同步。该操作使全部现有几何身份失效并重新登记。Native 配置使用 `DynamicGeometryManualUpdate` 并显式通知几何 dirty；仅清 TLAS 再加入同一个静态 Mesh 会复用旧 BLAS，本轮 GPU 验证实际发现并修正了这个问题。它不是自动的变形追踪器。

所有操作在 Unity 主线程执行；Synchronize/几何刷新应在 graph recording 之前完成。RenderGraph 消费者使用 **非 async 的 unsafe pass**：

```csharp
data.scene = scene.Read(graph, builder);
builder.AllowGlobalStateModification(true);
builder.AllowPassCulling(false);
// 对本 pass 的其他输入和输出另行声明依赖。
// 在执行回调内：
var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
data.scene.BuildAndBind(cmd, computeShader, kernel);
cmd.DispatchCompute(computeShader, kernel, groupsX, groupsY, 1);
```

`Read` 仅接受 unsafe builder，并登记 GPU metadata 的读写依赖。AS 没有对应的原生 RenderGraph 句柄，Build、上传和 trace 必须在同一 graphics queue 的明确顺序中执行。兼容模式使用 `ReadCompatibility().BuildAndBind(...)`。同一服务可供多个消费 pass 复用，只在 dirty 时记录 Build。

输入快照捕获 Revision；record 后修改服务会在执行时拒绝，避免资源/映射偷偷换成另一份。执行完成前不能 Dispose 服务。`Dispose` 可重复调用，释放 AS 和 GPU buffer；不存在全局 AS 单例或跨相机的隐式绑定。

## HLSL 命中契约

消费者声明 `#pragma require inlineraytracing`，引入 `Runtime/RayTracing/Shaders/HoRTSceneQuery.hlsl`，调用：

```hlsl
HoRTSceneHit hit = HoRTTraceScene(originWS, directionWS, tMin, tMax, mask);
```

方向会归一化，distance、tMin/tMax 以世界距离计。返回 status：0 miss、1 hit、2 metadata 错配、3 无效射线、4 输入不可用；失败 payload 清零。Hit 含 distance、positionWS、instanceId、submesh、materialId、submesh 内的 primitiveIndex、triangle barycentrics 与 frontFace。

**当前不返回顶点 UV、法线或已着色 radiance。** 它们需要后续 GPU 顶点属性读取与 ray-hit 材质求值。世界空间 miss 也尚未自动接 Ho-Atmosphere。两种光照 feature、主表面 bridge、灯光与 ReSTIR 均未被这个服务接管。

## 验证

Unity 6000.3.15f1，D3D12 RenderGraph / compatibility 的真实 GPU query 均通过；D3D11 的 unsupported 分支通过。入口为 `HoRTSceneValidation.Run` / `RunCompatibility` / `RunUnsupported`，辅助 feature 从添加菜单隐藏。

测试覆盖屏幕外几何、一个 Renderer 的两个 submesh/材质、CPU 重心重建与 GPU 命中点一致、双面与 frontFace、mask、非单位方向、无效方向、变换保持 ID、材质替换不重建 AS、禁用/恢复不复用旧 ID、cutout 排除、Mesh 内容更新、非 readable Mesh、空集合、销毁/重建、零缩放、Mesh/Renderer 销毁和过期输入拒绝。Compute 编译警告/错误会使验证失败。

日志保存在隔离工程 `.codex-research/RTInputValidation/rt-scene-*.log`，不提交工程缓存。尚未做 Player 验收、GPU 性能测量、动态角色和完整材质可见性验证。

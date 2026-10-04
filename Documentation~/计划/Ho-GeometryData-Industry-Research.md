# Ho-GeometryData：业内分层设计调查

日期：2026-10-04。性质：设计研究，供 [实施规划](Ho-GeometryData-Plan.md) 使用；不是已实现能力清单。

## 1. 调查结论与证据边界

适合 Ho 的方向是：**专用模块输出契约 + 组件生产者 + 实例资源管理 + 调度适配器 + 消费适配器**。`HoGeometryDataRendererFeature` 是 URP 接入点，整个系统的生命周期不能由每个相机的 Feature 独占。GD 不采用全局最终属性命名空间、效果覆盖链或统一来源裁决。

这个结论是依据下列系统作出的设计归纳，并非某个引擎存在一套恰好叫 GeometryData、覆盖所有模拟和渲染的统一框架。

**Ho 目标架构校正（2026-10-04）**：用户明确 AC 消费 GB，并产出屏幕后处理所需的遮罩与 ID；这一部分尚未开始实现。当前 AC 的 OB/SB 实现范围只是现状，不代表 AC 的长期输入边界。GD 的几何生产与 AC 的屏幕组合分别承担职责。

**生产与选择校正（同日）**：对象上的组件控制初始化、Play 前和手动触发，描边修正不默认在资产导入时 bake。每个模块生产专用完整结果，跨效果共用的数据由共同模块生产；消费者自主选择 GD 或 GB/SB 等来源。法线独立发布，UV 暂缓。动态参考系配合相机参数即时求值，取消默认维护每相机角度结果表；多对象的基础参考系寻址仍保留。

| 调查对象 | 能确认的事实 | 成熟度与适用边界 | 对 Ho 的借鉴 |
| --- | --- | --- | --- |
| Frostbite FrameGraph | 以 pass 与资源依赖组织可解耦的渲染功能 | EA 工程师的正式 GDC 架构分享；不是几何属性协议 | 用现有 RenderGraph 表达执行依赖 |
| Unreal Skin Cache | Compute 生产蒙皮位置与法线/切线，缓存结果，经 passthrough Vertex Factory 绘制 | 引擎正式渲染路径，有容量、LOD、回退和调试机制 | 缓存属于实例/场景资源系统，消费者经适配层读取 |
| Unreal Mesh Drawing Pipeline | SceneProxy、MeshBatch、MeshDrawCommand 分离场景表示与绘制绑定 | 引擎基础绘制架构 | 注册对象不应知道自己会在哪些 pass 绘制 |
| Unreal RDG | 根据已声明资源处理寿命、barrier、裁剪和异步调度 | 正式渲染基础设施 | 逻辑字段依赖最终必须落成 GPU 资源读写声明 |
| Unreal Deformer Graph | 数据接口、绑定、执行域、Setup/Update/Trigger、输出接口分离 | 官方页面仍标 Beta；作为参考设计，不称为成熟上线标准 | 借鉴触发、数据域和输出接口；不采用通用效果叠加控制台 |
| OpenUSD Primvars / Hydra | 属性域、数据角色、组合覆盖、失效通知、消费者隔离 | 影视/跨渲染器场景架构；不是实时 GPU 帧调度器 | 借鉴域、语义与失效；不据此在 GD 实现覆盖机制 |
| Unity Digital Human SkinAttachment | 预建绑定数据，读取当前蒙皮 GPU buffer，执行 GPU attachment resolve | Unity Demo Team 的公开实现；不等于 URP17 的时序保证 | 借鉴绑定资产、空间转换和 GPU 传递；单独验证版本 |
| Unity SMR / URP RenderGraph API | 获取当前/历史蒙皮 buffer，请求 Raw target，导入并声明 Compute buffer 访问 | 当前项目可以使用的官方 API；原生 skinning 不是用户 RG 节点 | 来源适配器承担 native → 用户图的边界 |

## 2. Frostbite：解耦靠依赖声明，而不是 Feature 列表排序

EA 的 FrameGraph 分享明确以所有 render pass 和资源构成的图解决跨游戏扩展问题。它支持“功能模块各自描述需求、调度系统处理共同执行”的组织方式。[GDC 原始分享](https://gdcvault.com/play/1024045/FrameGraph-Extensible-Rendering-Architecture-in)、[作者的说明](https://kayru.org/publications/)。

**本项目推论**：Tension、Outline、NormalTransfer 应描述自己实际读写的资源。每个模块内部可有明确多步子管线，共用资源由明确共同模块提供；URP RenderGraph 处理真实执行依赖。不重写底层 GPU FrameGraph，也不为 GD 新建统一属性覆盖/解析图。

## 3. Unreal：Skin Cache 与绘制适配层是最直接的参照

Skin Cache 将蒙皮计算输出为缓存 vertex buffer，再交给 `GPUSkinPassThroughVertexFactory`。它有场景级显存预算、逐 LOD 使用策略、容量不足的替代路径；Hair 与 Ray Tracing 能消费相应变形结果。法线/切线重算另分 triangle 和 vertex 两步，并明确存在 chunk 边界接缝限制。[Skeletal Mesh Rendering Paths](https://dev.epicgames.com/documentation/en-us/unreal-engine/skeletal-mesh-rendering-paths-in-unreal-engine)。

**本项目推论**：

- 位置、法线、张力可以共享生产基础，但结果的容量、有效性、版本和 LOD 必须有管理者。
- 选择使用 OutlineData 的材质读取该模块的结果契约，来源适配器封装 Unity 原始 skinned buffer。
- 邻接必须有全 mesh / chunk / submesh 边界策略；不能把“GPU 法线重算”视为天然无接缝。
- 多消费者复用不意味着永久保存每种中间量。只有被使用、要跨帧或要调试的产物才应物化。

Mesh Drawing Pipeline 中，`FMeshBatch` 使 `FPrimitiveSceneProxy` 无需知道自己将在哪些 mesh pass 绘制；pass 再构建包含资源绑定的 `FMeshDrawCommand`。[Mesh Drawing Pipeline](https://dev.epicgames.com/documentation/en-us/unreal-engine/mesh-drawing-pipeline-in-unreal-engine)。

**本项目推论**：这是 Unity 侧消费桥的参照。我们没有相同的 C++ Vertex Factory 扩展点，因此以模块 HLSL 接口、draw-local 关联和实际消费 pass 的资源声明实现相应职责，不能声称两者能力完全相同，也不据此要求所有 Feature 采用 GD 的 normal。

## 4. Unreal RDG：模块需求与 GPU 执行依赖分开

RDG 通过 pass 参数里的资源依赖管理 transient 资源、裁剪、barrier 和 async compute；跨图资源以外部注册/提取方式连接。[Render Dependency Graph](https://dev.epicgames.com/documentation/en-us/unreal-engine/render-dependency-graph-in-unreal-engine)。

**本项目推论**：

1. `Tension needs Position` 是字段依赖。
2. `TensionPass reads PositionHandle, writes TensionHandle` 是执行依赖。
3. `DrawObjectsPass reads TensionHandle` 才闭合真正的生产/消费链。

只做前两条，再在 shader 里通过一个全局变量访问第三条，并不等于图已经知道第三条。模块调试应显示自己实际依赖与消费 pass；资源调度不承担效果优先级或数据源选择。

## 5. Deformer Graph：值得借鉴，但要如实标注 Beta

官方文档仍写明 Beta。其参考价值在于：Setup 在初始化运行；Update 每帧运行；Trigger 按请求排队；component binding、typed resource、Vertex/Triangle data domain 与 Write Skinned Mesh 接口各自独立。[Deformer Graph](https://dev.epicgames.com/documentation/en-us/unreal-engine/deformer-graph-in-unreal-engine)。

Groom 适配又使用 Curve / Control Point 执行域和独立的 Groom 输入/输出接口。[Groom Deformer Graph](https://dev.epicgames.com/documentation/en-us/unreal-engine/setting-up-a-groom-deformer-graph-in-unreal-engine)。

**本项目推论**：

- Static/Dynamic 是更新分类；初始化、Play 前、手动和逐样本触发单独配置，相机依赖也单独声明。Static 不等于导入回调。
- 数据域要与更新频率正交。HeadFrame 是 Instance 域的 Dynamic；不是 Vertex 域，也不是第三种“Semantic 生命周期”。
- 毛发扩展时增加 Curve/Root 域适配器，而不是把毛发控制点强塞进 Mesh vertex 索引。
- 第一版用专用功能组件、输出接口与可选共享预设；不设计覆盖优先级或通用属性图编辑器。

## 6. OpenUSD / Hydra：属性、存储、覆盖、失效各有名字

Primvars 区分 constant、uniform、vertex/varying、faceVarying 等属性域；faceVarying 可以表达同一点在不同面上的 UV / 法线不连续。[Primvars](https://openusd.org/release/user_guides/primvars.html)。

Hydra Scene Index 提供具名数据源、组合覆盖和 `PrimsDirtied` 失效通知。其外部计算适配甚至可以把计算结果发布成普通 primvars，使后续消费者不必理解计算机制。[Hydra 2.0 Getting Started](https://openusd.org/dev/api/_page__hydra__getting__started__guide.html)。

**本项目推论**：

- NormalData 的 normal 角色、GPU 的 float3/offset/count、来源空间与 SampleRevision 分别描述，消费者不依赖内部存储。
- Unity render vertex 因 UV/硬法线而拆分，不能等同于拓扑上的唯一 point。需要显式映射与按用途定义的 weld group。
- 改变组件状态、参考姿势或 mesh 后，失效本模块的相关缓存；是否重新生产遵循组件触发策略。
- Hydra 的组合覆盖是该系统的事实，不是本项目采用它的理由。这里只借鉴语义、域、消费者隔离和失效，不引入 USD 或 GD 通用覆盖链。

## 7. Unity 官方实现：对“圆柱/代理面传递”更有价值的证据

Unity Demo Team 的 `SkinAttachmentTarget` 将静态 attachment 绑定上传 GPU，再读取 SMR 当前 buffer 进行 resolve；源码包含三角顶点绑定、frame delta、stride/offset、rootBone 空间处理和可请求的完成 fence，并在 `beginFrameRendering` 路径执行 GPU 更新。[SkinAttachmentTarget 源码](https://github.com/Unity-Technologies/com.unity.demoteam.digital-human/blob/master/Runtime/SkinAttachmentTarget.cs)。

**本项目推论**：圆柱法线组件可采用解析代理场或固定绑定驱动的 proxy transfer，完整发布自己的 NormalData。绑定在组件准备、Play 前或手动请求时建立，缓存资产是可选保存方式；运行时求值。该源码是参照，不直接作为 Unity 6000.3 / 本地 URP17 / 所有 Graphics API 的兼容性认证。

## 8. Unity API 能保证什么，不能据此承诺什么

| API / 文档 | 已确认的能力 | 需要在本项目单独证明的部分 |
| --- | --- | --- |
| [GetVertexBuffer](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SkinnedMeshRenderer.GetVertexBuffer.html) | 当前帧 GPU vertex buffer；开启 skinned motion vectors 时存在双缓冲交替 | 取得时机、实际输出布局、坐标空间、可见性/LOD 条件 |
| [GetPreviousVertexBuffer](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SkinnedMeshRenderer.GetPreviousVertexBuffer.html) | 获取历史 buffer 的接口 | 当前配置中是否有有效历史，以及它与我们 SampleId 的对应 |
| [vertexBufferTarget](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SkinnedMeshRenderer-vertexBufferTarget.html) | 可增加 Raw 等访问用途 | 请求后资源重建、wrapper 寿命、跨 API 验证 |
| [URP Compute Pass](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/render-graph-compute-shader-run.html) | AddComputePass、ImportBuffer、UseBuffer、DispatchCompute | native skinning 的 ready 边界和后续普通绘制 pass 的读取声明 |
| [BakeMesh](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html) | CPU 蒙皮快照，不论 GPU Skinning 开关 | 仅适合基准捕获/参考验证；不能据此描述为必然 GPU → CPU readback |
| [MaterialPropertyBlock](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MaterialPropertyBlock.html) | 可逐 renderer 绑定 buffer / 参数 | 官方注明不兼容 SRP Batcher；不能未经评估作为正式绑定策略 |

原讨论里的“ImportBuffer 后 RenderGraph 自然排好 skinning”需要纠正：外部资源 import 只声明我们的图如何使用它，不能凭空将引擎原生 skinning 变成这个图的 producer 节点。

## 9. 当前代码调查：迁移要基于实际状态

调查时工作树：Extensions `6cd33b5`，HoUrp17.3.0 `50c4e04`，lilToon `ff00f7a`。这只是本轮读取时的版本记录。

| 实际入口 | 调查结果 | 对规划的约束 |
| --- | --- | --- |
| `Runtime/ObjectBuffer/HoObjectBufferGroup.cs` | `faceBone`、轴向配置、组级/部件级 `TryGetWorldFacing` 位于 OB 组件 | 迁移数据归属和查询接口；不能宣称当前正在逐像素存三个方向 |
| `Runtime/CharacterSpecialization/HoCharacterEyeAngleTable.cs` | CPU 每相机计算组级 yaw/pitch，上传小型查询纹理 | 目标改成动态 FrameData + ViewContext 即时求值，不默认保留这类角度结果表 |
| `Runtime/ObjectBuffer/Shaders/HoRendererIdentity.hlsl` | RSUV 低 16 位是身份；先 setup instance 再读取 | GD 实例索引不能复用 partId；同部件可包含多个 renderer |
| `lil_common_appdata.hlsl` | 已有受 `LIL_APP_VERTEXID` 控制的 SV_VertexID | 要逐 pass 审计启用、复制、submesh/baseVertex 对齐 |
| `lil_common_vert.hlsl` | 自定义顶点入口、TBN、描边、历史位置有现成顺序 | 属性解析需要公共入口，当前/历史分别处理 |
| `lil_common_functions.hlsl` | 描边方向来自 normal、方向贴图或 vertex color | 新方向场接入后仍保留明确的旧来源与宽度策略 |
| `lil_pass_geometry_buffer.hlsl` | GB 当前输出可包含法线贴图参与的表面法线 | GB 正常屏幕来源与 GD 专用 NormalData 保留各自含义，实际消费者自行选择 |
| HoUrp `UniversalRendererRenderGraph.cs` | BeforeRendering、BeforeRenderingShadows 在主阴影前有实际记录入口 | GD 尽早插入；同 event 内顺序和 offscreen 路径仍需验证 |
| HoUrp `Passes/DrawObjectsPass.cs` | 有 UseAllGlobalTextures；没有替 GD 声明的 buffer read | 全局贴图依赖不能推导全局 buffer 依赖 |
| 本地 UnityGraphics Core `IRenderGraphBuilder.cs` | global texture 与 UseBuffer 分开；AllowGlobalStateModification 建立顺序同步点并禁止裁剪 | 保守发布可以用于起步，长期仍应显式声明每个实际消费者的 buffer read |
| `Documentation~/Ho-AttributeComposite.md` 与本轮目标校正 | 旧说明将输入冻结为 OB/SB；用户明确目标还包括 GB 与屏幕后处理遮罩/ID 产出 | 将旧边界标为当前实现范围；规划按 GB/OB/SB → AC → 屏幕消费者组织，未实现部分明确标注 |

## 10. 采用与暂缓

采用：模块类型化契约、多数据域、来源适配器、实例缓存、显式输入输出、按需生产、模块专用完整结果、当前/历史样本、独立调度与绘制绑定、容量回退和诊断。

这里的发布是每个专用模块发布自己的完整结果，不是把所有效果解析为一个最终 normal。共用数据由共同模块一次完成；相机参数在相应求值位置使用。

不采用：GD 中控台、通用属性覆盖链、全局最终 Normal/UV 命名空间、组件顺序决定效果优先级、默认导入描边 bake、默认每相机角度结果表。

暂缓：无消费者的 UV 模块、自建底层 RenderGraph、通用 GPU bindless 属性字典、完整节点编辑器、全面自研 skinning、物理 solver、每帧最近点搜索、把全部几何状态永久复制一份。

下一步先验证 Unity 来源与消费链，再实现真实消费者。调查支持分层方向，但 GPU 时序、布局、索引和美术效果必须通过项目原型验收。

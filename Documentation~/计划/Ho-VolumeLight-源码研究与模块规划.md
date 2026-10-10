# Ho-VolumeLight：VLB 源码研究与模块规划

日期：2026-10-10。

状态：已完成本地解包与源码研究，并实现 v0.1 Box 大气介质 + 太阳 HD 丁达尔。当前范围见 [运行用法](../Ho-VolumeLight.md)，GPU 证据见 [v0.1 验证记录](Ho-VolumeLight-v0.1-验证记录.md)；本文后续局部 SD/HD、粒子、SDF 资产与 AC/AOV 仍属规划。

当前建议的第一个实施闭环与验收见 [v0.1 启动计划](Ho-VolumeLight-v0.1-启动计划.md)：HoAtmosphere Region + Medium 发布 + 场景已有太阳的 HD 丁达尔，先验证最常用地编流程。

**最新基建定位**：用户明确 **HoAtmosphere Feature 已固定开启，未来与 GD/GB/OB/SB/AC 同属底层基建**。区域 SDF、空间介质属性、动态密度与共享灰尘数据归 HoAtmosphere，通过内部 Regions 数据层组织，由基础 Feature 统一准备/发布；环境 Cache 与区域生命周期分开。VolumeLight 单向消费，不接管共享场。此前按可选 Feature 作出的外部独立发布建议由此替代。详细边界见 §14，源码审查与基建化后的接入见 §15。

## 1. 用户约束与模块边界

本轮用户决定：

1. 新模块叫 **VolumeLight**，提供 **SD 与 HD** 两条实现。
2. 服务当前 **HoURP（约等于默认 URP 17）**，工具简单、直白、显式。
3. 用于场景布置的局部光柱，核心是好用、可调、自动软边、自动裁切。
4. HD 必须考虑窗框、多孔结构；遮挡输入需要光源空间深度。
5. 模块具有自己的组件、Shader、绘制资源和调度；最新补充允许显式消费 HoAtmosphere 的共享空间介质契约，不依赖其他系统的内部实现。
6. 本轮先研究参考工程和源码组织，再决定实现，不立即修改运行时功能。
7. 后续补充：需要动态空间灰尘/密度扰动；同一空间的多盏灯共享介质，考虑 AC 统一查询与未来 Nuke ID/AOV 输出。
8. 后续补充：主要美术流程还应支持场景现有太阳方向光照亮整片区域，以空间遮罩限制丁达尔；无需逐窗添加局部灯。灯光挂件应是可选快捷入口，灯光参数同步规则要明确。最新候选流程见 §13。
9. 最新补充：SDF 区域定义共享大气属性，归 HoAtmosphere；包括体积光激活权重、灰尘纹理/颜色/密度、动态噪声。区域范围也可为 HD 捕获提供接收区域参考。
10. 源码复核：当前 Feature 实际仅有天空环境输入；空间区域需新增数据层与契约，现有天空 Cache 和 GI 的环境 revision 保持原语义。
11. 最新用户前提：HoAtmosphere 固定开启并基建化，与 GD/GB/OB/SB/AC 同级；适合统一发布环境和空间介质。轻量来自按需准备/缓存，而不是拒绝共同数据职责；光束渲染仍在 VolumeLight。

因此初版只设计 HoURP 17 的固定 Shader 与 RenderGraph 路径。Built-in、HDRP、旧 LWRP、旧 Unity 兼容、跨管线自动探测和 Shader 生成器不进入本模块。非 RenderGraph 路径不列为首版承诺；以后有明确需求再补。

模块基础依赖为 Unity/URP 的相机、深度、渲染接口、物体标准深度材质 pass，以及明确的 HoAtmosphere 空间介质数据。渲染核心无需 OB/SB/AC/GB、HoShadowCast、RTDI/RTGI、角色特化或 OIT 的资源。AC 作为可选的命名/效果查询与导出接入层，详见 §12；它不承担体积渲染。绑定 Light 是真实照明来源；独立的手工美术光柱也可以直接使用明确的光束参数。

HoAtmosphere 管共享环境/空间介质，VolumeLight 管光束照明、阴影捕获与积分；不把整个光束 Renderer 塞进大气环境捕获。现有 ScreenProcess 的 SkyTyndall 不作为输入。

## 2. 包来源与工程结构

本地参考放在 `research~/VolumeLight`，沿用仓库的 Unity 忽略目录约定。现有 `.git/info/exclude` 已覆盖整个 `research~`；参考包不进入生产包资源。

用户包：`E:/BaiduSyncdisk/工具与包/Unity组件包/Volumetric Light Beam 2.2.9 u6000.6.unitypackage`。

外层 7 个 pathname 记录是安装器，内嵌另一个 `.unitypackage`。源码展开后 171 个 pathname 记录、325 个文件，其中非 `.meta` 文件 154 个。外层 12、内层 325 个恢复文件均与归档内容哈希一致。完整哈希/路径见本地 manifests 与 `extraction-verification.json`。

源码版本由 `Scripts/Version.cs` 的 `Current=20209` 确认为 2.2.9。根 package.json 仍标 2.0.0，是旧元数据。

```text
Assets/VolumetricLightBeam/
  Scripts/                   48 个 C#，包含 editor 条件编译的 ShaderGenerator
    SD/                      SD 组件、几何、射线/深度遮挡、偏斜工具
    HD/                      HD 组件、几何、Shadow、Cookie、质量与灯光跟踪
    Config / MaterialManager / MeshGenerator / SRPHelper / 通用辅助
  Editor/                    34 个 C#，包含 SD/HD Inspector、手柄与配置工具
  Shaders/                   10 个 cginc/hlsl、5 个模板、ShaderGenerator.asset
  Resources/                 3D noise、blue noise、dither、尘粒 prefab/material
  Samples/                   6 个 C# 与 4 个场景，包括三种管线及压力场景
```

没有 `.asmdef`。没有打包生成后的 `.shader`；编辑器根据 Config 和模板创建 Shader 文件。因而只寻找 `.shader` 会漏掉真正算法。

详细阅读索引：[SOURCE-INDEX](../../research~/VolumeLight/SOURCE-INDEX.md)。下文 S01–S33 对应该索引。

## 3. 值得参考的职责划分

VLB 的核心分成三层：

| 层 | 职责 | 源码 |
| --- | --- | --- |
| 用户光束组件 | 保存几何与外观参数、可选绑定 Light、验证范围、标记更新 | S01、S03、S04 |
| 生成几何/绘制对象 | MeshRenderer/MeshFilter、共享代理网格、材质、bounds、逐相机参数 | S02、S05–S08 |
| 可选能力 | 遮挡、Cookie、尘粒、闪烁、脉冲、LOD 等 | S17–S19、S24、S31–S33 |

SD 与 HD 共享抽象层和辅助设施，但各自有用户组件、几何对象和 Shader 主体。改颜色/亮度不等于重建 Mesh，换 variant 不等于重新采集遮挡。

HD 有按属性分类的 dirty flags；更新物体参数和相机参数分开。共享几何在 vertex shader 中按光柱参数变形，CPU 必须同步正确 bounds，避免移动/放大光柱后被错误剔除。

几何子对象还处理场景可见性、复制粘贴后的孤儿、禁用、Prefab Undo/Revert 和销毁。参考包明确在编辑器延后销毁深度相机/RT，处理 Prefab revert 的同步销毁问题。这些属于生产组件生命周期，不是光照算法。

它在透明渲染里绘制自己的 MeshRenderer，不提供独立 ScriptableRendererFeature 或 RecordRenderGraph 实现。SRP 相机识别通过 begin/endCameraRendering 栈与 OnWillRenderObject 完成（S02）。这套办法可以理解其独立性，但我们应以 HoURP 的明确 pass 调度实现资源依赖。

## 4. SD 如何获得好用的假体积感

SD 以锥体/截锥表面模拟可见光束，无沿视线体积积分。

### 4.1 软边与视角

S13 的角度权重不是简单贴一个透明渐变：根据锥面法线与视线计算权重，再加入对长光柱、沿轴观察的修正，以及内外面混合与 glare。轮廓变淡、中心保持可见；相机进入光柱时另有内部亮度和端盖处理。

有三种不同柔化，必须分别理解：

| 柔化 | 输入与用途 |
| --- | --- |
| 光束侧边 | 形状/视角权重，消除硬锥体轮廓 |
| 物体交界 | 主相机场景深度，淡化表面网格与地面/墙的接缝 |
| 相机交界 | 相机 near plane 距离，避免穿过网格时突然切面 |

主相机深度软交界不是灯光遮挡。单靠它不能保证光柱不穿墙，也不能生成多孔窗内部的体积阴影。

### 4.2 自动裁切

SD 的物理射线模式根据中心与辅助射线命中结果求出**一个裁剪平面**，传给 Shader，并可在平面前淡出（S17）。它适合整面地板/墙的近似终止，不描述窗格后分别亮暗的体积。

SD 的深度模式额外拍光源空间深度，对**锥体表面的着色位置**做投影、比较和 smoothstep 裁切（S15、S18）。它能有一定局部遮挡，但没有采样光柱内部。一个处在锥体中央的小窗框可能没影响锥体外壳；因此“有深度图”不代表达到 HD 的能力。

拟议首版 SD 保持侧边/交界/相机柔化，遮挡选择 Off 或 RaycastPlane。SD LightDepth 可作为后续明确的近似功能，首版不把它作为多孔窗解决方案。

## 5. HD 的核心其实是局部光束积分

HD 用代理网格限定需要运行的像素，在光束局部空间里进行 Ray Marching；无需全屏体积网格、环境光/GI 输入或硬件光追。

S20–S23 的顺序是：

1. 对当前像素构造透视或正交相机视线。
2. 求视线与截锥交点，得到入口 tIn；网格背面提供出口 tOut。
3. 相机在体积内时入口从相机开始；用主相机场景深度截短出口。
4. 用蓝噪声和随时间变化的 jitter 分散低步数条带。
5. 沿可见区间采样距离衰减、径向侧边柔度、3D noise、Cookie 和光源深度遮挡。
6. 累加、取平均、乘可见路径长度与强度，再透明混合。

默认质量只用 **5 / 10 / 20 步**，默认 10（S09）。这是其低成本局部光束的一项关键取舍；数值不能直接当作我们的性能承诺。

HD soft edge 是采样点相对当前截面边界的密度权重。场景交界由积分路径变短自然减弱，再配合 near-camera fade。整体是面向外观的局部积分：本次看到的核心公式为平均贡献乘路径长度，未包含完整 Beer–Lambert 消光/多重散射，也未建立历史重投影缓冲。我们首版可以沿用这一产品定位，不引入全局雾系统。

### 5.1 两张深度图的职责

| 深度图 | 观察方向 | 在 HD 中使用的位置 |
| --- | --- | --- |
| SceneDepth | 主相机看场景 | 终止当前像素的视线积分，防止计算物体后面的空气 |
| LightDepth | 光束源头看场景 | 每个体积采样点判定是否被窗框/墙挡住，形成内部体积阴影 |

窗格多孔效果要求两者配合。LightDepth 中保留孔洞只是第一步，HD 必须在光柱内部逐点比较深度，才能看见孔洞延伸出去的明暗束。

参考实现的 HD 阴影比較是逐点硬深度比较乘 strength（S23），径向柔边不等于 PCF 阴影软化。我们的“自动软边”需要给出稳定侧边、端部与场景交界默认值；软阴影过滤单独定义，不能把两个概念混在同一滑条。

### 5.2 VLB 怎么采集 LightDepth

每束附加 `VolumetricShadowHD` 后，会创建一个禁用的子 Camera 和自己的 Depth RenderTexture。透视视图用于发散光束，正交视图用于平行形状；视图覆盖范围与光束几何相匹配（S25、S26）。

可选 LayerMask、分辨率、更新频率与 occlusion culling。HD 用 Forward 路径以处理 alpha cutout；SD 深度相机则使用 VertexLit 路径。源码在 SRP 场景将 Camera.Render 延后到 Update/LateUpdate，避免递归渲染，并限制一帧重复采集（S18、S24）。

HoURP 方案优先研究独立深度 pass，不为每束跑完整的嵌套相机渲染。

## 6. 精简工程时保留与省去的部分

| 参考设计 | VolumeLight 决策 |
| --- | --- |
| 参数组件、绘制实现、遮挡职责分开 | 保留。用户只面对一个简单组件，内部仍然分 SD/HD。 |
| 同时支持所有管线/旧 Unity | 服务 HoURP 17，使用固定的 URP Shader includes。 |
| 根据项目设置生成 Shader 与 define | 使用明确 Shader 资产，不生成源文件，不修改项目 define。 |
| 全局 Config 自动搜索/创建、平台覆盖 | 单个显式 RendererFeature 配置；首版无需另建必需配置资产。 |
| 几何、材质缓存、dirty flags、生命周期 | 保留最小必要机制，确保改普通参数不重建网格。 |
| 每束子 Camera 与延后 Camera.Render | 优先改为模块独立 LightDepth 捕获；具体接口先做小验证。 |
| 阴影/Cookie 使实现退出 GPU Instancing | 首版不承诺有阴影时的批处理；先正确实现，未来再评估 atlas/array。 |
| 尘粒、密度动态扰动 | 按新增需求纳入共享介质设计；噪声与实体颗粒分别处理，详见 §12。 |
| 2D、Trigger、脉冲、LODGroup | 后续按需求增加；首版聚焦场景光柱。 |
| 场景视图手柄与 Undo | 保留，参数、操作都显式。 |

“独立”仍然允许调用 Unity 标准 DepthOnly/ShadowCaster 材质 pass；这属于几何输入契约。它不要求物体登记 Ho 语义或参与其他 Feature。

## 7. 我们的组件与操作方式

**建议公开一个 `HoVolumeLight` 组件，面板明确选择 SD / HD；两套 renderer 与 Shader 内部分开。** 这样切模式能保留形状、颜色、绑定等共同参数，无需删除旧组件再迁移配置。此为规划建议，尚未冻结序列化 API。

正常操作：

1. 在 Renderer Data 明确添加一次 `HoVolumeLightRendererFeature`。
2. 从创建菜单添加光柱，或在现有对象上加 `HoVolumeLight`。
3. 直接选择模式与形状，拖动长度/截面手柄。
4. 选择遮挡方式与遮挡层，得到软边与自动裁切。
5. 可选绑定一个 Light；面板明示哪些参数由 Light 提供。

不自动替用户修改 Renderer Asset、URP Asset、材质、Layer 或项目设置。缺 Feature/输入时显示准确状态与对应操作，避免悄悄切另一种实现。组件负责运行时代理几何与材质，用户无需手工维护 Mesh/Material；面板可以显示生成资源归属。

### 7.1 面板保持直白

| 分组 | 常用项 |
| --- | --- |
| 模式与形状 | SD / HD；Cone / Rectangular；长度与起点/终点截面 |
| 外观 | 颜色、亮度、边缘柔度、端部淡出、交界柔度 |
| 遮挡 | Off / RaycastPlane（SD）/ LightDepth（HD）；遮挡层 |
| HD 质量 | 步数档位；深度图分辨率；更新策略 |
| 可选灯光绑定 | 明确的 Light 引用与绑定开关；亮度倍率 |

共享介质明确引用或查询 `HoAtmosphereRegion`，统一噪声尺度、风速、时间与灰尘属性。每盏灯的颜色、强度、范围、遮挡仍由光柱控制。Cookie、bias、更新间隔、内部视角修正属于折叠的高级项。没有启用相关功能时，不显示一长列无效参数。

一键创建只是创建当前光柱对象并设置明确默认值，不顺带扫描全场景、安装其他模块或更改管线配置。

### 7.2 形状与窗户

VLB 核心以圆截面锥体为中心；我们是地编工具，建议提供 Cone 和 Rectangular 两种基本形状。Rectangular 定义起始/结束截面，能表示矩形平行束或发散束，光源深度投影需对应正交/透视视图；几何求交与 Shader 密度边界也必须匹配。

先用 Cone 跑通 HD 深度链，再加 Rectangular 并用真实窗格验收。矩形功能属于我们提出的扩展，不应描述成参考包已有完整矩形体积积分。

## 8. 内部模块组织建议

```text
Runtime/VolumeLight/
  Ho.VolumeLight.Runtime.asmdef       Unity Core/URP + 大气共享契约；实际 assembly 拆分另定
  HoVolumeLight.cs                    用户组件与序列化参数
  HoVolumeLightShape.cs               明确形状参数/边界/光源投影
  HoVolumeLightRegistry.cs            启用组件集合，camera/frame 生命周期
  HoVolumeLightRendererFeature.cs     仅模块调度、深度请求与明确设置
  Common/
    Geometry / MaterialCache / ShaderConstants / FrameData
  SD/
    SDRenderer / RaycastPlaneOcclusion
  HD/
    HDRenderer / LightDepthProvider / DepthCache / DepthCapturePass
  Shaders/
    HoVolumeLightSD.shader
    HoVolumeLightHD.shader
    Common / Shape / SceneDepth / LightDepth / SD / HD 的 hlsl

Editor/VolumeLight/
  Ho.VolumeLight.Editor.asmdef
  HoVolumeLightEditor.cs              一个 Inspector，按模式展示有效选项
  HoVolumeLightSceneHandles.cs        手柄、Undo、Prefab 参数编辑
  HoVolumeLightRendererFeatureEditor.cs
  HoVolumeLightCreateMenu.cs

Runtime/Atmosphere/Regions/           规划新增，当前尚未存在
  HoAtmosphereRegion.cs               空间形状、SDF、范围与边缘权重
  MediumProperties / RegionRegistry / SharedSampling
```

这是职责草图，不要求按表中的每个名词拆出文件。实现时小而稳定的代码可以放在同一文件；不为 SD/HD 建两套重复的公共框架。

局部资源生命周期：组件禁用/销毁释放注册与专有资源；可见光束才考虑更新；没有启用光源深度就不分配 RT。保持共享网格/必要材质缓存，独立纹理首版使用明确 per-beam 资源，atlas 优化不先行扩大复杂度。

## 9. HoURP 接入的优先技术验证

### 9.1 独立光源视锥剔除

这是必须先解决的接口问题。主相机没看见的墙/窗框，也可能挡住光柱。现有 `HoShadowCastPass.cs:260` 的 renderer list 使用主相机 `cullResults`；直接消费它不能保证光源视图完整。

拟议方向：在能取得当前 ScriptableRenderContext 的明确准备阶段，按光束视图采集独立 CullingResults；RendererFeature 记录模块自己的深度 raster pass 与绘制依赖。`beginCameraRendering` 是候选准备入口，不从 GPU render function 里执行 CPU 剔除，也不让 CullingResults 跨 frame/context 存活。是否能安全满足当前 HoURP RenderGraph 生命周期，要用最小场景确认后冻结。

可以使用不渲染的 Camera 帮助构造视图/剔除参数，但它只是模块内部视图描述，不运行 Camera.Render 的完整管线。

### 9.2 保留遮挡物的真实孔洞

默认处理不透明与 alpha cutout，使用原材质的标准深度 pass，保留顶点形变、alpha clip、材质 Cull。

本地 lilToon cutout 已有 DepthOnly（`ltspass_cutout.shader:1622`）与 alpha fragment 分支（`lil_pass_depthonly.hlsl:90`）。该 DepthOnly 也带材质 stencil/depth 状态，因此独立采集要明确覆盖或支持这些状态；不能在没有原场景 stencil 的 RT 上盲用材质比较。

使用统一 override depth material 会容易把带孔贴图重新画成实心板，应避免将它作为默认。ShadowCaster 作为兼容入口时，要另外处理 bias、LightDirection/LightPosition 等标准契约。

首版不推导半透明玻璃的透射/彩色体积阴影；透明材质是否遮挡由明确支持的深度 pass/过滤策略决定。未提供深度 pass 的 shader 给出可检查的支持范围。

### 9.3 深度/视图状态与资源声明

- 统一 light projection、world-to-light、GPU projection、reversed-Z、near/far 与场景深度距离单位。
- 为独立视图准备必要的相机 shader 常量，并恢复观察相机状态；仅 SetViewProjectionMatrices 不足以承诺所有材质变形兼容。
- pass 使用 `UseRendererList`、depth attachment、`UseTexture` 明确声明依赖，不依赖上一帧全局纹理残留。
- camera type/stack policy 显式：Game/SceneView 首版必测；Preview、Reflection、Overlay、XR 的支持按验证结果声明。

### 9.4 透明顺序

绘制光柱用标准命名事件。候选默认是 BeforeRenderingTransparents：opaque depth 已就绪，输出线性 HDR，后续 Bloom 能自然消费；主相机 depth 获取的准确时机需实测。

普通透明和 OIT 的空间交互必须做场景验证。当前 OIT 在 BeforeRenderingTransparents 累加、AfterRenderingTransparents 合成，同事件按 Feature 列表顺序执行（见顺序契约）。本模块不读取 OIT buffers，不承诺仅靠改 event 就能正确解决光束与所有透明表面的前后关系。首版定义为不透明/alpha cutout 场景中的局部光束，透明边界按已支持行为说明。

## 10. 遮挡更新与性能范围

SD/HD 本身都按可见像素运行；HD 成本约随屏幕覆盖、步数与重叠光束数增长。LightDepth 还增加独立视图剔除、遮挡物绘制、RT 内存与采集频率。

明确的更新选项：首次可见必须采集；Manual、OnBeamChange、EveryNFrames 各自明示。仅跟随光束移动不足以响应移动窗帘/人物，因此动态遮挡场景默认选周期更新。缓存有效性还需覆盖形状、投影、mask、分辨率、禁用/重启、场景切换等变化。

先实现同帧有效的 LightDepth；未有效采集的第一帧不假装纹理有效。更新预算不足或资源缺失时，显示明确状态并采用事先定义的行为，不默默退回 SD/无阴影。

参考的 5/10/20 步与 128 深度分辨率只作为起始实验数据；我们最终档位由窗格、相机运动、重叠与目标设备的实测决定。首版不引入全局 froxel、TAA 历史、GPU light clustering 或跨模块共享 shadow atlas。

## 11. 实施顺序与可验收结果

| 阶段 | 工作 | 完成标准 |
| --- | --- | --- |
| P0 天光与深度链验证 | 现有主方向光 + 区域内 HD 积分 + 世界空间级联阴影采样；局部独立 LightDepth 接口验证 | 一个太阳照亮多个窗户，无额外窗边灯；多孔窗形成内部光束；保留画面外遮挡与 alpha cutout |
| P1 SD | 共享代理几何、视角柔边、场景/相机交界、RaycastPlane | 整面墙/地板自动终止；进入光柱无明显端盖/硬切面；无 HD RT |
| P2 HD 与共享介质 | 完整局部积分、形状一致性、共享世界噪声/jitter、明确步数与更新；共享尘粒 | 相机内外、前景遮挡、移动遮挡、质量变化行为正确；多灯重叠不重复产生尘粒 |
| P3 地编工具 | 单组件 SD/HD、Cone/Rectangular、场景手柄、明确 Light 绑定 | Undo/Redo、复制、Prefab Apply/Revert、多人场景与编辑模式预览可靠 |
| P4 性能与可选外观 | 缓存/资源释放、实际档位、Cookie 等 | 无无效常驻资源，报告单束/重叠/深度更新成本；可选功能关闭即无该输入成本 |

首个技术场景必须包含：实体窗格、多孔贴图、画面外遮挡物、薄墙、斜墙、光柱内相机、正交相机、移动遮挡、多相机。同一验证场景再补普通透明/OIT 与 MSAA/dynamic resolution，结果用于确定支持边界。

本轮完成的交付为参考源码展开、逐文件校验、源码索引与本规划。下一步应先做 P0，而非先堆完整 Inspector 或复制参考包的跨管线框架。

## 12. 动态空间灰尘、多灯共享与 AC 输出补充

本节记录本轮后续用户要求；仍为方案研究，没有实现运行时接口。

### 12.1 参考包的两种“灰尘”

**连续密度扰动**：`Noise3D.cs` 绑定一张全局 Texture3D；`ShaderUtils.cginc:87` 用 world/local position、scale、velocity 和共同时间计算采样坐标。scale/velocity/intensity 仍是逐光柱属性，Config 只是可选共同默认值。共享同一贴图不自动保证同一位置、同一时刻得到相同密度。

**可见颗粒**：`VolumetricDustParticles.cs:127` 为每束实例化 ParticleSystem，144 把粒子对象挂在光束下，149 克隆材质。253 附近直接由该光束颜色/渐变设置粒子出生色；299 附近设置移动，316 附近按光束生成 ConeVolume 发射区域。这一模块没有汇总多灯照明或按空间合并粒子池。

因此参考的逐束尘粒是装饰效果；不能据此认为整个房间里已经存在一批独立于灯光、被所有灯共同照明的灰尘。其官方文档也描述 local simulation 会随光束移动，移动光束时有表现限制。

### 12.2 最终用户形态：灯光与共享空间各一个组件

拟议公开形态：

```text
Room / DustSpace
  HoAtmosphereRegion
    范围、密度、噪声尺度、扰动强度、风向/风速、稳定 seed
    可见尘粒开关、颗粒密度/大小、模拟时间

WindowBeam / SpotlightA / SpotlightB
  HoVolumeLight
    SD/HD、形状、颜色/亮度、软边、遮挡、质量
    Atmosphere Region → 明确拖入同一个 Room 的组件
```

共享介质作为 HoAtmosphere 的独立数据域，不强制制造一套全局浓雾系统。一个空间只维护一份噪声坐标定义与共享灰尘状态。独立手工光柱可以显式使用均匀密度；避免悄悄寻找或创建隐藏的大气区域。

首版同一位置只支持一个明确的 Medium；多个 Medium 重叠如何混合不自动猜测。不同房间可引用不同组件。光柱与 Medium 的共同范围内才有贡献。

固定 Texture3D 加平移采样即可产生流动扰动，不需要逐帧生成或模拟完整三维网格。可以概念化为：`rho(p,t)=baseDensity * Noise((p-origin)*frequency - wind*t)`。world origin、frequency、wind、时间、seed、纹理与强度都归同一 Medium；灯光运动不带着噪声跑。SD 用同一函数近似采样外壳，HD 在内部采样点使用它，两者不会承诺相同精度。

### 12.3 多盏灯照的是同一份介质

同一采样点只定义一次密度，然后加各灯的照明贡献：`source(p)=rho(p) * sum(lightContribution_i(p))`，每项包含该灯的颜色、衰减、形状边界、遮挡与方向权重。

稀薄、以加亮为主的光柱可以逐灯画并相加；重叠处更亮是多灯照明结果。正确使用共同密度时，灯数增长不意味着空气密度增长。每束用不同 world-noise 坐标/速度，会表现为两套空气互相穿过，应该通过共享 Medium 消除。

如果以后加入真实背景消光，`scene*T` 只能按共享介质路径计算一次，再加各灯散射；不能让每盏灯重复乘自己的一层雾 alpha。首版维持局部稀薄加亮定位，不因为共享噪声就引入浓雾消光系统。介质与照明拆分符合 [PBRT 的吸收/散射模型](https://www.pbr-book.org/4ed/Volume_Scattering/Volume_Scattering_Processes)。

尘粒同样只在 Medium 中产生/模拟一次。粒子根据其移动后的 world position 求相关 HoVolumeLight 的照明和遮挡，叠加颜色后绘制一次。灯关掉改变照明，灰尘继续漂浮；增加灯数量不增加粒子数量；移动灯表现为光束扫过已有颗粒。共享粒子 Shader 需要显式的本模块光源数据与 LightDepth 输入，不能仅取每束出生色。

### 12.4 Feature 的必要性来自资源生产与声明

SD 外壳、噪声与常规颗粒本身可以用透明 Shader 的正常绘制路径，无需为了它们再建一种全屏后处理。

HD 自动多孔遮挡依旧需要 LightDepth 来源。Shader 自带 pass 只描述如何绘制，不会自动准备光源视图、执行独立剔除、生成/更新/释放深度图、声明 RenderGraph 依赖。建议保留一个薄的 `HoVolumeLightRendererFeature`，统一这些工作，并在需要时发布本模块光源数据与 AOV。

普通透明绘制能否直接消费模块生成的深度资源还要验证明确 GPU 依赖。当前 HoURP `DrawObjectsPass.cs:301` 只在 opaque 路径启用 `UseAllGlobalTextures(m_IsOpaque)`；不能假设把 Texture 塞进材质就等于 RenderGraph 已知道透明 pass 读取它。若无法在标准路径声明依赖，使用模块自己的薄 draw pass，并保持标准透明阶段顺序，避免隐藏资源依赖。

VolumeLight 的渲染/捕获仍只需一个效果 Feature；共享介质由固定 HoAtmosphere 基础 Feature 从 Region/Registry 准备并发布，VolumeLight 按契约读取与绑定 GPU 输入。每个区域不再建 Feature。只有请求效果遮罩/AOV 时才增加相应输出工作，颜色绘制使用已有 Shader 主计算。

### 12.5 AC 与 Nuke：登记身份还需要实际效果范围

用户希望通过 AC 中控获得 ID/遮罩，这个逻辑入口可以保留；渲染核心通过可选适配层向 AC 发布命名效果和资源，不读取 AC 来决定能否显示光束。

当前 AC 的身份资源来自 OB ranked 表面身份池；其查询表达可见前表面 coverage。光柱代理网格的轮廓不等于被噪声、软边、阴影削弱后的体积贡献，不能直接把它当准确体积遮罩。多根相加光柱也能同时影响同一像素，不能用一个最近表面 ID 完整表达。

规划的输出区分：

| 输出 | 用途与定义 |
| --- | --- |
| 稳定光束/灯组名称与 ID | AC catalog/导出元数据可识别的效果来源，不自动占用现有 OB packed ID |
| VolumeLight RGB AOV | 线性、曝光/tone-map 前的实际光束贡献；Nuke 单独调光束亮度/颜色 |
| Dust RGB AOV | 可见颗粒贡献；可与连续光束分别调节 |
| Effect matte | 与实际绘制同步的软效果权重，包含密度、形状、可见区间和遮挡；明确归一化约定 |
| 按灯组的 RGB/matte | 请求时输出；重叠贡献保留各组，不通过最近表面 winner 抹掉其他组 |

对 additive 光柱，强度/亮度贡献与 alpha coverage 含义不同；effect matte 不冒充当前 OB coverage 或现成标准 Cryptomatte。合规 Cryptomatte 编码/manifest 与 OpenEXR 写出仍属于导出层，当前 AC 尚未实现这些导出功能。

主张效果 RGB 与遮罩尽可能复用同一次 raymarch 的中间结果，避免单独画一个实心锥体作为遮罩；是否同 pass MRT、指定灯组重绘或另记录贡献，按输出数量与请求决定，常规显示不常驻全部导出 RT。

时序也要单独处理：当前 AC 主 resolve 默认在 BeforeRenderingOpaques；VolumeLight 在 opaque depth 就绪后绘制。后者不能倒流进同帧的早期表面身份 resolve。适配层必须明示晚阶段效果资源/发布时点，不移动或重定义已有早期 AC 输入。连接 AC 是可选输出契约，不让 AC 接管 LightDepth 捕获与体积积分。

### 12.6 新增验收

同房两束灯交叠，检查同一世界位置的噪声连续；移动灯不拖动噪声；灯数变化不改变颗粒数；红/蓝灯共同照亮同一粒子；关闭灯仍保留粒子轨迹；窗框同时遮挡光束与颗粒照明。再检查两个灯组 AOV 的贡献能重建合计结果，遮罩遵循软边/孔洞，不使用代理外壳的实心覆盖。

离线导出需要统一时间输入、稳定 seed 与可重现粒子模拟；同一导出帧的颜色/AOV/matte 使用同一模拟状态。可复现性在正式实现时验证，当前没有提供重播或导出代码。

## 13. 天光主流程、区域约束与灯光绑定补充

本节把当前需求从局部 Spotlight 光柱扩展为“太阳方向光 + 区域介质”的地编流程；涉及 UI/API 的内容仍为候选设计。§7/§12 的局部光束入口保留，但不要求每个窗户创建光柱或额外灯。

### 13.1 光源、介质与遮罩各自的作用

这里把用户的“天光丁达尔”按有方向的太阳/主 Directional Light 理解。仅有均匀环境辐射不能提供同样明确的平行光束方向；太阳已有方向、颜色、强度与物体遮挡，是首版主要照明来源。

| 来源 | 回答的问题 |
| --- | --- |
| Light / 手工光束参数 | 光从哪里来、是什么颜色、强度多大 |
| Medium | 哪片空间有介质、密度与噪声如何分布、灰尘怎么移动 |
| 物体光源深度/阴影 | 窗框、树叶、墙对照明的遮挡 |
| 体积形状/密度遮罩 | 美术希望效果存在在哪些区域、边界怎么淡出 |
| 屏幕效果遮罩/AOV | 当前相机中的后期选区与贡献查询 |

体积形状约束只乘在密度/贡献上，不自动代替窗框的光源遮挡；二维 AC 遮罩可以用于画面选择，不能代替随相机移动仍稳定的三维介质范围。

成熟参考：[HDRP Local Volumetric Fog](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/create-a-local-fog-effect.html) 将局部介质表示为有朝向的盒子，并支持 Density Mask 3D 纹理；[属性说明](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/local-volumetric-fog-volume-reference.md) 还提供 Size、Blend Distance、Scroll Speed、Tiling 等地编控制。这只作为组织方式参考，不适配 HDRP。

### 13.2 推荐的实际工作流

默认地编流程：

1. Renderer Data 明确启用一次 VolumeLight Feature。
2. 创建一个“空间大气区域”（`HoAtmosphereRegion`），设置共享介质属性。
3. 在 VolumeLight 的来源设置明确引用场景已有太阳 Directional Light，选择 HD；一个太阳可照亮多个区域。
4. 设置全局受限距离或拖出房间/树林的 Box 范围，调密度、边缘淡出和动态噪声。
5. 原场景窗框/树叶的几何阴影自然切出光束；通过区域遮罩进一步控制美术范围。
6. 需要时开启这个 Medium 的共享可见尘粒。

这样一个房间可以只新增一个区域组件。局部 Spotlight/手电筒/舞台灯则可选在现有 Light 上添加 `HoVolumeLight`，引用同一大气区域，并选择局部 SD/HD 表现。VolumeLight 的来源列表和灯光挂件描述同一来源时，内部按来源去重，避免重复贡献。Light 引用/SD-HD 质量不放进通用大气属性中。

手工装饰光柱仍能作为独立对象创建；它显式使用手工参数。统一 source descriptor 允许组件挂在 Light 同物体、作为 Light 子级或独立引用 Light，摆放位置不决定绑定规则。

太阳大范围 HD 使用区域/相机视线积分；局部 HD 仍可用代理光柱限制像素，SD 继续提供局部假光柱。大范围太阳区域不能直接套用 VLB 的单锥体 SD/HD 形状。首版不承诺太阳 SD 全屏 God Rays 与区域 HD 同等的空间遮挡能力。

### 13.3 灯光同步规则必须明确

挂在 Light 上的创建菜单可把 source reference 明确设为该 Light 并选 FollowLight，不通过运行时“寻找最近灯”或隐藏扫描决定光源。独立区域的来源也必须可见、可编辑。

| 属性 | FollowLight 的行为 |
| --- | --- |
| 启用与方向/位置 | 跟随指定 Light 的有效状态、方向与适用的位置 |
| 颜色/色温/强度 | 通过 HoURP 实际解析的灯光参数，避免重复换算或重复乘 intensity |
| range、Spot angle | 对局部 Spot 适用；Direction Light 不暴露无意义的照射锥长度 |
| 阴影 | 跟随明确选择的 URP 阴影输入；独立 LightDepth 是另一项显式选项 |
| 体积亮度倍率 | 用户自己的 volume scattering multiplier，调整它不改 Light 表面照明 |
| 密度、噪声、尘粒 | 留在共享 Medium，Light 不覆盖这些参数 |
| 区域范围与柔边 | 留在区域组件，独立于太阳 Transform |

Manual 模式使用明确的美术参数；不用保存时从 Light 抄一次后失去跟踪，也不偷偷把体积参数反写进 Light。灯属性被动画修改时，FollowLight 持续同步其有效参数；不把这件事变成另一个必需的跟踪组件。

### 13.4 是否需要 SDF

需要的是**三维范围约束 + 可调软边**。初版用户选择 Box/Sphere、拖手柄、设置 Blend Distance，就能解决房间、局部薄雾、指定光束区等常见场景。

Box/Sphere 可在 Shader 内使用解析 signed distance 计算软权重，但面板直接叫“形状/范围/边缘淡出”；无需用户制作 SDF 资产。这与提供完整 Mesh→SDF 烘焙/编辑工具是不同工作量。

建议由 HoAtmosphere 提供统一空间介质求值，密度为 `baseDensity * worldNoise * regionMask`，使未来 3D Density Mask / SDF Texture 可以接入。VolumeLight 只调用契约，不自行复制 SDF/密度实现。首版先做 Box/Sphere。任意房间形状、隧道、复杂排除区产生明确需求后，再做 Texture3D/SDF 资产支持；不提前建立通用 SDF 图形编辑器。

简单排除区也可以先用明确 Box/Sphere 的扣除操作，但组合规则与执行顺序必须显示。多个空间仍遵守明确的 Medium 绑定，禁止不透明的自动合并。

### 13.5 HoURP 太阳路径的新增验证点

主太阳优先复用本管线的主方向光 shadow map，调用入口可参考 `ShaderLibrary/RealtimeLights.hlsl:81` 与 `Shadows.hlsl:342`。独立局部光源继续使用 §9 的模块 LightDepth 路径；不能把所有光源都强制做额外相机捕获。

体积采样必须按采样点 world position 选 cascade 并查原始光源阴影。HoURP 当前 `TransformWorldToShadowCoord` / `MainLightRealtimeShadow` 在 `_MAIN_LIGHT_SHADOWS_SCREEN` 且非透明分支会读取屏幕阴影；这种图描述可见表面，不是任意空气采样点的遮挡。体积实现必须显式选世界空间 atlas 路径，验证 Screen Space Shadows 打开时结果仍正确。

明确校验被引用太阳与实际 mainLightIndex 的对应关系；未经验证不能把任意 Directional Light 都套用主光 shadow map。级联边界、shadow distance、阴影远端淡出也影响区域内的体积表现，需要测试超出范围时的明确行为。

新增实际验收：同一太阳透过多个窗户/树隙，在一个大区域里自动形成丁达尔；移动太阳后束方向跟随；缩放/移动区域只改变介质范围；没有窗边新增 Light；打开屏幕空间阴影不改变体积采样语义。局部灯同时参与时，共享噪声和尘粒保持连续，来源列表与灯挂件不重复计光。

## 14. HoAtmosphere 共享空间属性：最新模块边界

### 14.1 SDF 定义空间，区域定义属性

用户最新意图是可跨系统消费的空间大气定义。候选组件 `HoAtmosphereRegion` 归 Atmosphere，不保留 VolumeLight 私有的 Medium 数据源。

SDF 给出某世界位置到区域边界的 signed distance；区域将该距离转换成软权重，再关联大气属性。密度、颜色、灰尘纹理并不是 SDF 数值本身。Box/Sphere 的解析距离与任意 Texture3D SDF 都通过同一边界求值契约表达。

| HoAtmosphere 区域数据 | 用途 |
| --- | --- |
| Transform、SDF/解析形状、保守 Bounds、边缘淡出 | 空间归属、区域查询、消费者工作范围 |
| 密度与空间密度纹理 | 连续介质浓度，不随灯数倍增 |
| 介质颜色/散射参数 | 各消费者共同解释的介质属性 |
| 世界噪声尺度、风、时间、稳定 seed | 动态场的一致性与可复现性 |
| 灰尘外观纹理、灰尘颜色、颗粒密度/尺度 | 可见灰尘的共享空间属性；与连续介质密度分别标明 |
| 体积光参与权重 | 美术定义此区域是否/多强参与体积光 |
| 稳定区域 ID、优先级、混合/排除规则与版本 | 多区域确定性求值、缓存失效与输出关联 |

连续密度纹理通常是空间 Texture3D；灰尘颗粒外观可以是 Texture2D/atlas。面板明确纹理的用途，不能把两者当同一个槽位。颜色可初版采用一个统一 tint，并在有需要时提供明确的介质/颗粒独立项。

区域重叠规则由大气数据层统一定义，消费者不得各自选择不同混合逻辑。纹理共享与粒子模拟也按区域身份维护；观察灯或相机数不使状态复制。

### 14.2 单向消费，保持渲染独立

```text
HoAtmosphere 固定基础 Feature
  环境输入域                     当前已有，天空 radiance 捕获/发布
  空间介质输入域                 规划新增，内部 Regions 数据层准备/发布
    SDF、密度/颜色、噪声/风、灰尘属性/共享状态
          ↓ 明确的数据与采样契约
VolumeLight
  灯光绑定、SD/HD、可见性与遮挡、深度捕获、积分/绘制
          ↓ 可选效果输出
AC / 后续 AOV 导出层
```

其他效果可以通过同一基础设施消费空间属性，无需依赖 VolumeLight。Regions 的组件/Registry 管世界状态，固定 HoAtmosphereRendererFeature 管按相机准备/发布，二者有明确生命周期。大气数据层不保存 VLB 兼容属性、raymarch 步数、灯的 range/spotAngle、LightDepth RT 或 VolumeLight 材质。

共享可见灰尘的状态/模拟由独立的空间 runtime 管理，不放进现有天空 Feature 的 AddRenderPasses 或环境 Cache。实际照明与绘制明确指定一个生产者执行一次。首个使用者可由 VolumeLight 的灰尘 renderer 消费；未来其他效果引用同一状态不能再次复制生成。最终 producer 的组件/文件位置在实现时确定，本轮尚未实现或冻结模拟接口。

计划的 C#/HLSL 契约应回答“某 world position/time 的介质属性与权重”，并提供区域 bounds、资源引用/索引、有效性和 revision。以下仅为逻辑名称，不是已实现 API：`HoAtmosphereSampleMedium(positionWS,time)`、`HoAtmosphereRegionBounds(regionId)`。

环境辐射与空间区域有独立资源有效性和 revision，由同一个固定基础 Feature 发布。区域使用不需要开启 RTDI/RTGI，也不强制重捕获 Cubemap 或启用环境积分。区域只定义属性不意味着必须先烘焙一整张三维 RT；Feature 可以从 Registry 获取区域表、变换与纹理，按需上传/引用，消费者通过共享契约直接求值。

当前 `HoAtmosphereRenderGraphResources.IsValid` 只检查 radiance，不应用来判断未来区域有效性。共享数据域需要单独资源/状态，避免区域风速改变导致天空重新捕获。

### 14.3 区域可帮助 HD 捕获，但不能裁掉外部投影者

SDF 区域的保守 Bounds 可以提供工作范围、光源视图的接收范围参考、投影覆盖、深度分辨率参考，以及区域变更后的缓存失效条件。用户进一步明确两层关系：SDF 只让效果在区域内显示，光源锥体/照亮范围与独立遮挡始终完整维护。HD 捕获可以参考通常覆盖很大的区域，不能拿区域交集直接替代完整光锥。

**接收区域与遮挡物范围不同。** 屋檐、外墙、树枝可能位于介质区外，却沿光线投影到区内。不能只绘制 SDF 内的几何，也不能只用区域 AABB 剔除所有 caster；应把接收范围沿光源上游扩展，或使用已有完整 URP 方向光阴影的覆盖结果。

任意 SDF 的表面距离不替代 LightDepth 深度比较。SDF 控制介质权重，LightDepth/URP shadow map 负责真实物体遮挡；区域 metadata 给深度捕获提供参考，RT 与视图仍由 VolumeLight 所有。

### 14.4 初步使用形态

地编创建 HoAtmosphere Region，直接调范围、软边、密度、灰尘纹理/颜色与动态参数；VolumeLight 明确选择参与的大气区域或大气全局场，并配置已有太阳/局部灯。多个灯消费同一份区域数据，保持共同噪声和灰尘。

组件命名也与现有 `HoAtmosphereVolume` 区分：后者是 Unity Volume 框架中的环境输入覆盖项；Region 是场景世界空间属性，不复用逐相机 Volume 权重来表达物理空间密度。

新增验收：基础 Feature 固定存在时，缺失环境来源不影响有效 Region；移动/改区域不刷新天空缓存；两个消费者在同一位置获得一致属性；多灯不复制密度/灰尘；区域外的屋檐仍能挡住区内光束；关闭 VolumeLight 后基础大气输入继续有效，无需求时不分配效果专用 RT。

## 15. 对现有轻量 HoAtmosphere 的职责复核

本轮重新检查实际 Runtime/Editor、HLSL、RTGI 消费者，以及新增环境输入提交的原文。源码事实是当前只实现环境输入。用户随后明确基础 Feature 固定开启，因而最新接入结论是：**HoAtmosphere 统一承担共享环境/空间介质数据准备和发布，内部按域分开；VolumeLight 保留效果渲染职责。** 不把“目前代码轻量”当成阻止基建扩展的边界。

### 15.1 已有代码的实际工作

| 已有代码 | 实际职责 | 与空间区域的差异 |
| --- | --- | --- |
| `HoAtmosphereSettings.cs:22` | 天空盒、指定材质、Cubemap、常量来源与捕获设置 | 没有世界区域、SDF、密度或灰尘参数 |
| `HoAtmosphereCache.cs:40` | 输入来源缓存、六面捕获、材质/纹理/太阳变化检查 | 不管理场景区域集合或时间推进的粒子状态 |
| `HoAtmosphereRendererFeature.cs:38` | 按 Base Game/SceneView 相机安排环境输入；100 的 pass 在 AfterRenderingPrePasses | 空间属性生命周期不能依赖某相机是否执行天空 pass |
| `HoAtmosphereRenderGraphResources.cs:8` | 单个 radiance TextureHandle、方向旋转/染色/强度、revision | IsValid 检查环境来源，不表示空间场有效 |
| `HoAtmosphereSampling.hlsl:14` | `SampleRadiance(directionWS)` | 新职责是 `SampleMedium(positionWS,time)`，查询域不同 |
| `HoRTGIRendererFeature.cs:156` | 消费既有环境资源；176 把环境 revision 纳入历史签名 | 灰尘/区域变更不应作为环境内容变更影响 GI 历史 |

现有天空来源的缓存命中只安排输入发布；材质来源首次或变脏时才捕获六面。区域表、每帧空间噪声与粒子模拟没有这套缓存/更新语义，直接塞进现有 Cache 不会产生合理复用。

提交 `0745e1d` 的原始文档同样明确本阶段仅提供环境输入，未来体积积分另行定义。现有验证覆盖方向/HDR/天空来源/缓存/相机域，没有空间字段或粒子验证。新职责需要新增实现与验收，不能描述成现有 Feature 已具备的大气系统。

### 15.2 基建化后的内部拆分

1. 保留现有天空 Cache、环境 resources/采样的语义；固定 HoAtmosphere Feature 的输入发布扩展为空间介质域。
2. Atmosphere 下新增内部 `Regions`：场景组件、属性、Registry、CPU 描述与共享 HLSL 求值；不依赖观察相机维持世界状态。
3. 基础 Feature 按需要准备并发布区域表/纹理/版本；VolumeLight 从其公开资源集读取，显式绑定与声明依赖。
4. 灰尘模拟通过独立 scene/runtime 对象维护，多个相机只观察；不在天空发布 pass 里推进模拟。
5. AC 的效果输入桥接仍是可选输出路径。

无需新增第二个“大气场 RendererFeature”，也不因为固定开启就常驻体积 RT、全局 atlas 或执行完整模拟。环境捕获、区域上传、灰尘模拟分别按变化/需求运行。环境来源有效性不能当所有大气数据的总有效性，空区域与缺失环境也分别声明。

消费者输入契约应区分 Environment 与 Medium 的 availability/revision；已有 `IsValid` 的环境语义及 RTGI 调用先保留，介质增加自己的有效性。不让动态灰尘不断使 GI 的环境历史失效。新的 debug/Inspector 也按域显示状态，而不把 VolumeLight 的效果参数塞进底层面板。

### 15.3 本轮决定与尚未冻结的部分

用户确定 HoAtmosphere 的固定基建定位。后续 v0.1 已实现 `HoAtmosphereRegion`、独立 Medium 资源与共享 CPU/HLSL 求值，保留原有环境输入接口；当前沿用包的 Runtime/Editor assembly。任意 SDF、更多区域与粒子接口继续后续实施。若以后区域属性扩展到与大气无关的通用空间字段，再评估抽取公共区域基础，当前不提前建设通用平台。

# Ho Shadows 重构执行计划

日期 2026-11-27。状态为实施中。本页是 `lilToon-URP-Extensions`、`HoUrp17.3.0`、`HoShader` 和 `BREAK_URP` 的共同开发计划。当前行为仍以各模块实现说明为准，用户使用文档在默认切换验收后同步。

## 1. 已确定的目标

- 受管 Ho Renderer 默认启用一个 **Ho Shadows** Renderer Feature。它负责各相机的阴影预算、来源选择、Pass 编排和屏幕结果发布。当前 ShadowCast、Character Shadow、RT Shadow 的公开 Feature 入口在迁移完成后收口到这里。
- **光栅软阴影采用 PCF。** 原生 URP 图集使用原生比较与过滤。角色局部图集采用有界预算的固定 PCF，保留图集边界、偏移和区域过渡。退役 Ho PCSS 的 blocker search、可变半影、质量枚举和相关配置。接受 PCF 不再重现按遮挡距离增长的半影。
- 退役 **独立 Ho ShadowCast 场景图集**。普通方向主灯、点光及聚光复用原生 URP 阴影设置、剔除和图集。场景只有一套原生主方向光级联，`Full` 可使用原生点光与聚光 atlas。角色自阴影作为主光局部精细化，按需分配角色图块。
- **Main / Full 表示接收范围。** Main 为原生主方向光 + 角色局部精细化。Full 在此基础上接收逐灯阴影，原生 URP 点光／聚光是默认来源。沿用 `Main=1`、`Full=2` 的序列化值，迁移现有材质的有效行为。
- **RT Shadow 默认关闭。** 开启后才允许为合格灯、相机和接收表面发布可替换的逐灯结果。材质选 Full 只表达取样范围。它不发起资源分配、AS 请求、射线派发或重建。世界空间 DXR 继续作为可选来源，未来的 SSRTS 可使用同一屏幕结果接口。
- 受管路径的材质都从**同一个屏幕可见度入口**取实时阴影。Forward、角色 Capture 和 OIT 的受支持组合只编译查找与合成。PCF、DXR 和后续 SSRTS 各自编译在管线 Pass。迁移期间保留隔离对照，最终发布清理材质里的直接 atlas 路径和旧切换逻辑。

这里的主光是 URP 选中的主方向灯，通常是场景的 Sun。烘焙阴影、探针、环境光和 AO 仍由各自光照流程解释，阴影迁移时单独核对其现有效果。

## 2. 实施前的证据和基线

| 项目 | 已有证据 | 对执行的影响 |
| --- | --- | --- |
| 原生灯光 | 本机 URP 17.3 的 `GetAdditionalLight` 带 shadow mask 的重载已取原生逐灯衰减。Skin、Eye、Cloth、Environment 的逐像素路径已经使用它。旧 Main 可接原生附加灯，旧 Full 在命中 Ho 额外图集时替换该灯。 | Full 的第一套逐灯来源直接接原生 atlas。新版 Main 的严格主光语义需要迁移。 |
| 场景图集 | BREAK_URP 的 PC 管线资产允许原生光栅主光和附加灯投影。历史工作区快照里，PC Renderer 同时启用了独立 Ho ShadowCast 和可见灯自动收集。 | 先按实际镜头记录生效的灯和图集，再退掉额外生产者。无特殊灯组件的序列化记录也需要实际画面对照。 |
| 角色精细化 | 当前角色图集可按完整 renderer 身份拆组和部件、检查局部盒，并在边缘混合回主光。固定滤波现有预算仍可能很高。 | 采样搬到管线时，身份、局部混合和偏移属于画质合同。重新定 PCF 采样预算。 |
| RT 消费 | RTShadow 实际执行世界空间 DXR 并发布屏幕可见度。生产端有逐灯产品。工作区查到的常规材质只消费主灯绑定，逐灯结果已经有调试和验证用户。 | 保留逐灯产品的身份和状态，单独接入 Full。旧主灯全局读法随着消费迁移退役。 |
| 编译测量 | `HoShader/docs/material-platform/19-ho-shadow-compile-cost.md` 记录 Skin、Cloth、Environment 默认 D3D11 片元中移除完整 ShadowCast 接收链后的耗时降幅，分别为 81.7%、88.1%、84.2%。另有 Skin 高负载样本达 15/16 sampler 的记录。 | 这些数字是移除整条接收链的对照。单改 PCF 与把解析移出材质需要分别测量。 |

开工时记录各仓提交、包锁、Renderer、Volume 栈、相机和材质需求，留存隔离工程的依赖哈希和画面基线。此前实盘检查发现生产 Renderer 和默认 Volume 可能有并行修改，后续按实际序列化差异迁移。

### 2.1 当前主要验证入口

- [P0 生产盘点和隔离 A/B](../../Tests~/HoShadows/P0/README.md) 记录本轮基线和可重放的旧 ShadowCast 夹具。
- [P1a 原生 Main 屏幕解析](../../Tests~/HoShadows/NativeMain/README.md) 记录原生级联和 PCF 的 GPU 对照及当前相机范围。
- [P1b 角色固定 PCF](../../Tests~/HoShadows/CharacterPcf/README.md) 和 [P1c 角色屏幕解析](../../Tests~/HoShadows/CharacterResolve/README.md) 记录滤波预算、接收域和隔离 GPU 对照。
- [P2b 原生附加灯屏幕解析](../../Tests~/HoShadows/Additional/README.md) 记录 Forward 和 Forward+ 的逐灯对照与索引复用。
- [P3 分层 Weighted OIT](../../Tests~/HoShadows/WeightedOit/README.md) 记录真实 Cloth、图层顺序、主光、点光及聚光的复跑方式。
- [P3 私有 Character Capture](../../Tests~/HoShadows/Capture/README.md) 记录 Face／Eye 的实际受管材质、私有深度、主光及 Spot 对照。
- [P4a 托管 RT 请求](../../Tests~/HoShadows/RayTraced/README.md) 记录默认关闭、相机 Volume 和独立 RT 客户端的生命周期验证。
- [P5 迁移盘点和隔离试切](../../Tools~/HoShadows/README.md) 记录候选材质、生产文件签名、工具引用与隔离回退。
- `HoShader/Skin_URP/Tests~/SkinShaderCompileBenchmark.cs` 可复用为导入和逐 Pass 请求入口。槽位和字节码审计需要记录确切的片元关键字组合并解析 DXBC 声明。
- `HoShader/Environment_URP/Tests~/EnvironmentGraphicsSmoke.cs` 已有两盏不同颜色原生点光的对照，可扩成新 Main 和 Full 的逐灯门槛。
- 现有 [RT 多灯产品验证与接入说明](../Ho-RT.md) 覆盖按 `LightId` 查询、状态、主灯绑定和 RG／Compatibility。独立产品通过只能证明生产能力，材质着色接入另有验收。
- 现有 [角色自阴影说明](../Ho-CharacterShadow.md)、[ShadowCast 历史实现说明](../Ho-ShadowCast-PCSS.md) 和 [阴影互操作审计](Ho-Lighting-Shadow-Interop-Audit-20261102.md) 提供旧规则与可重放的对照。

## 3. Renderer 和模块职责

```text
Ho Shadows  [默认启用]
├─ Main  [默认启用]
│  ├─ URP 主方向光 CSM → PCF
│  └─ 角色局部图块 → PCF 与边缘混合
├─ Full
│  ├─ URP 原生逐灯阴影 → PCF
│  └─ RT Shadow  [默认关闭，逐灯启用]
└─ 调试  [可见度、来源、有效状态、接收面、GPU 阶段计时]
```

| 所在位置 | 持有的职责 |
| --- | --- |
| `HoUrp17.3.0` | `UniversalRenderer` 生产原生主光与附加光 atlas，提供相机深度和正确的可见灯索引。为受管屏幕阴影提供资源接入和绘制阶段的依赖。普通 URP/Lit 的覆盖范围在其光照桥接处显式验证。 |
| `lilToon-URP-Extensions` | Ho Shadows Feature、角色局部 atlas、独立 PCF 解析、逐灯数据映射、可选 RT 来源、每相机发布和调试。阴影输入图集和消费结果由同一调度管理，各 Pass 按资源依赖执行。 |
| `HoShader` | 包内共享的轻量接收合同、`Main / Full` 在各家族的着色接线、原生与三段响应、Editor 本地化、材质契约和编译 benchmark。用现有 ShaderLibrary 同步工具管理新合同，按包核对各家族原有的混合规则。 |
| `BREAK_URP` | Renderer Data、URP Asset、Volume、场景组件、旧材质／Variant、舞台生成与审计工具迁移，代表镜头的图像和性能回归。 |

Light 仍保存原生投射属性，角色组件继续定义接收组、部件、局部边界和过渡。Feature 管镜头和资源预算，Volume 负责已允许设置的相机或区域覆盖。RT 的 Renderer 总开关先检查，默认关闭时 Volume 与材质选择不能把它打开。RT 场景底座可有 RTDI 等独立请求，RT Shadow 的关闭条件只约束自己的成本。

受管 Renderer 的屏幕解析由 Ho Shadows 统一调度，切换后由这一个 Feature 管理该相机的解析。内置 Screen Space Shadows 适合作为主方向光解析的代码参考，其现有纹理和关键字只覆盖主光不透明路径。对现有独立 UPM 安装和第三方 `URP/Lit`，先查明已承诺的行为及资产范围。若旧安装要接入统一消费，提供实际工作的 Renderer 配置或适配入口并测试。额外的 URP/Lit 适配要覆盖实际宣称支持的灯型和 Pass。

## 4. 屏幕阴影合同

### 4.1 表面、灯和结果身份

| 维度 | 必需信息 |
| --- | --- |
| 视图 | 当前相机、眼、viewport 与纹理尺寸。处理 Render Scale、动态分辨率和同一帧的多个相机。 |
| 接收表面 | 与绘制对应的深度和覆盖。已登记普通透明材质按完整 owner 和眼深查找。无组的普通透明材质可按同域覆盖和眼深读取原生主光或逐灯阴影。角色局部、RT、Capture 和 OIT 保留完整身份检查。 |
| 光源 | Main 可直接映射主光。Full 每项携带当帧 URP 实际灯索引与结果位置。Forward 对 per-object light slot 做原有换算，Forward+ 使用并验证正确的全局索引。RT 的稳定 `LightId` 在当帧映射到同一盏灯。 |
| 值和状态 | 连续可见度，及可判定的有效性、来源和缺失原因。成功采到全亮与缺源分开记录。失败时使用**该表面同灯**的已解析原生结果，或者按已声明的范围降级到中性可见度。 |

主光可按表面域存成一张结果图。Full 是逐灯结果和索引的集合，材质不需要分别认识 atlas、DXR 或 SSRTS。先实现当帧映射和有效性。二维数组、tile 打包、精度及预算根据实测选定，避免用固定四灯作为材质协议。溢出行为要确定、可统计，并有可重现的降级结果。灯改变排序、销毁重建或离开视口时，旧 slot 不得命中下一盏灯。

### 4.2 合成规则

- **Main** 从 URP 主方向光 realtime 可见度开始。在合法角色表面上求角色局部值和边缘权重。当前路径是先取原生混合结果，再 `lerp(sceneCast, localCast, blend)`。如果烘焙值在提前解析时还不可用，屏幕结果携带恢复这项次序需要的数据，由统一光照入口完成混合。
- **Full** 增加 URP 原生逐灯实时可见度。继续按灯保留原生阴影强度、距离 fade 和 ShadowMask 的既有组合，Cookie、Rendering Layers、方向和颜色按原来的光照输入使用。对现有 `GetMainLight`／`GetAdditionalLight` 的着色调用，取光源数据与读取实时阴影需要解耦，避免先在材质查 atlas 再覆盖结果。实时可见度在合成中只应用一次。当前主光 RT 有效时，接收函数直接返回 RT 衰减，失效才进入原生与角色的边缘混合。P4 用现有主光对照固定这个替换语义，再明确新增逐灯 RT 的合成位置。涉及烘焙组合次序的差异单独核验。
- **新版 Main 的行为变化**。附加灯继续照明，实时附加灯投影由 Full 消费。旧 Main 中依赖原生附加灯投影的材质迁到 Full。第二方向灯的专用 Ho atlas 与额外灯参与主光乘数的能力一同退役，迁移报告逐场景列出影响。原生点光和聚光照常使用 Unity 的 Light 参数。
- **材质语义**。`_UseShadow`、接收强度、接收遮罩和每段 Cast 权重用于决定艺术响应。删除独立的 Ho 来源开关和旧 atlas 接收逻辑时，逐项核对同名但属于艺术响应的属性。`Native / ThreeBand` 继续是着色模式，`Main / Full` 是实时投影接收范围。

### 4.3 RenderGraph 执行顺序

1. `UniversalRenderer` 发布 `UniversalResourceData.mainShadowsTexture` 和 `additionalShadowsTexture`。依赖这两份 atlas 的解析发生在其生产之后。Main 原型先复用公开纹理句柄。Full 如需 CPU 侧的投影灯和 slot 映射，由 HoURP 从原生 `AdditionalLightsShadowCasterPass` 暴露当相机的只读数据，Extensions 通过资源接口消费。
2. 主相机深度和需要的 Geometry／Object／Surface 结果先就绪。角色 atlas 可按现行时序预生成，注意相机剔除与角色局部剔除的先后。RT 生产者依赖同相机主表面，在启用并有合格需求时才执行。
3. Ho Shadows 记录 PCF 和来源合成 Pass。前层结果必须在 opaque 或 ForwardOnly 消费前可用。透明、Capture、OIT 分别在其匹配的表面准备之后、着色之前解析并绑定。相同事件下按 [RendererFeature 顺序契约](../Ho-RenderFeatureOrdering.md) 确保记录顺序。
4. 每个 Pass 显式声明输入、读写标志和输出。PassData 在本次记录时完整赋值，执行入口使用 `static`，跨 Pass 由当前帧句柄传递。描述符从对应表面资源派生并明确所需的格式、尺寸与 MSAA 调整。
5. 仅实际需要全局绑定的着色产品通过 `SetGlobalTextureAfterPass` 发布。消费 Pass 明确声明读依赖，透明侧显式绑定对应域的资源句柄。`ConfigureInput` 只请求实际依赖的相机输入。关闭、跳过、无阴影灯、相机切换、分辨率变化都要重置可见度有效性。检查 RenderGraph Viewer 和 Frame Debugger 中的资源寿命、屏幕结果发布点和额外深度预处理开销。

## 5. 接收表面的发布范围

| 路径 | 实施要求 | 放行条件 |
| --- | --- | --- |
| 普通不透明、裁切 | 复用与实际绘制一致的前层深度。裁切重放原覆盖规则。角色和 RT 读取时验证 owner、深度和范围。独立 GBuffer/AOV 的私有深度要先验证与相机前层一致。 | 裁切孔、互相交叠、相同 owner 的不同深度、多相机与缺输入对照通过。 |
| 透明 Eye | 等效复现 `EyeDisplayAlpha`、阈值、队列与 Cull，在自己的接收输入中记录深度和完整身份。相机深度的写入行为由原绘制规则控制。 | ZWrite 启停、两层 Eye、轮廓孔、同 owner 不同深度均能读取正确结果。 |
| Face／Eye Capture | 使用 Capture 的私有深度、尺寸和过滤条件准备接收表面。Face→Eye 的深度与 stencil 语义需要保持，避免在同趟写入时同时采样附件。现在的辅助 MRT 是着色后结果，不能作为同趟接收输入。 | Face／Eye 各路径、不同 Capture 尺度、错误标签清理、第二相机复用和动态对象通过。D3D12 的旧 MRT 留值现象用基线场景单独复核。 |
| Cloth 加权 OIT | 当前累积色和透过率不足以重建每层接收面。先做可测量的分层原型，确保分层的覆盖、相机深度测试、Cull 和 stencil 与实际透明片元一致。记录有限层预算和溢出行为。 | 至少有交叠双层、交换绘制顺序、遮挡物、同 owner 多深度和超预算对照。任意多层的支持取决于成像方案。首层通过只放行首层覆盖。 |

透明和 Capture 缺少匹配时，严格路径通过同一查找接口使用匹配的原生解析数据或中性值。任何无法保持生产画面的降级在默认切换前列为待解决项。Object Buffer 的 MSAA 身份归约槽位不提供透明深度层。透明专用层预算、A2C／MSAA 和 XR 的实际支持必须记录在验收表里。

## 6. 工作分期和交付门槛

| 阶段 | 工作项 | 进入下一阶段前的证据 |
| --- | --- | --- |
| **P0 盘点与固定基线** | 锁定实际包、Renderer、Volume 和现存并行改动。盘点各家族活跃 Main／Full、RT 灯和 receiver 声明、特殊灯及舞台工具。做原样与仅停用旧 ShadowCast 的代表镜头对照。建立 D3D11／D3D12 隔离项目与性能记录。 | 实际生效的灯、图集、Pass 与资产清单有可重放来源。区分原生灯影、特殊灯退役影响和角色／RT 的变化。 |
| **P1 Main 前层** | 先用隔离对照验证固定预算 PCF，再实现统一 Feature 骨架、输入有效状态、URP 主光屏幕解析、角色图集 PCF 和角色边缘合成。选定一个 Skin 和一个 Environment 不透明／裁切样本接入轻量查找。记录 PCSS 到 PCF 的预期画面变化。 | 主光、角色/非角色、部件、局部盒过渡、接触细节、原生混合和无结果回退通过。该消费路径的 DXBC 只读取屏幕结果。 |
| **P2 Full 原生逐灯** | 直接接 URP 点光与聚光 atlas，在管线解析后按当帧光源索引发布。验证 Forward／Forward+、阴影开关、mixed、Cookie、层和预算。在 Skin、Eye、Cloth、Environment 及其他查到的受管消费端接同一合同。 | 双灯独立遮挡、错索引负例、灯重排/增删、无 RT 的 Full、Native／ThreeBand 各自的响应通过。OIT 或 Capture 可继续在隔离旧配置验证，发布支持域仍按 P3 管理。 |
| **P3 多表面覆盖** | 补透明 Eye、私有 Capture 与加权 OIT 的前置表面读取、域绑定、深度和身份校验。测层预算和溢出，并实现明确的相机退化路径。 | §5 的生产材质和负例通过。各已支持域的受管材质字节码只读取解析结果。生产覆盖缺口解决后才能切默认，范围调整需明确记录。 |
| **P4 可选 RT 归入 Full** | Ho Shadows 统一拥有 RT 生产调度。`lightId → 当前 URP 灯索引 → GPU 产品` 成立后，验证可选主光替换和额外灯消费。关 RT 时只有光栅源。RT 继续发布现有调试和公共逐灯产品所需数据。 | Renderer、Volume 和材质组合的启停矩阵通过，RT 默认关闭时没有该模块派发或分配。有效性失败只影响当盏灯，当帧回原生。已有 RT opt-in 镜头能显式恢复到同样的配置或有批准的迁移差异。 |
| **P5 切换生产并退役旧实现** | 迁移 `PC_Renderer`、Volume、材质、舞台工具与文档。替换 `HoShadowCast`、独立 Character 和 RT Feature 的挂载。清理运行时 PCSS、重复场景图集、旧 Ho 采样入口及同步副本。分批切生产资产并验证可回退性。 | 使用单 Feature 的真实场景通过回归。旧类型、GUID、属性、Shader HLSL、工具和发布声明的引用扫描通过。运行中只有原生场景 atlas 以及实际需要的角色图块和屏幕资源。 |
| **后续 SSRTS** | 复用当前接收表面做有界屏幕深度追踪，发布逐灯可见度及命中／不确定状态。先作为近距离遮挡增强。在有效命中合成下实测自阴影偏差和稳定性，出屏、遮挡层未知或预算不足时用原生结果。 | 独立比较原生 PCF、PCF + SSRTS、可选 DXR。验证离屏遮挡、视角移动、锯齿、灯数和射线步数。SSRTS 的实测与默认参数由它自己的交付决定。 |

P1 和 P2 可以在隔离分支并行开发，消费合同和光照混合对照需先对齐。P5 迁移前应协调共享工作树中 RenderGraph、shader、材质和项目资产的所有者，按仓分别提交，直到生产切换完成才销毁旧序列化源与回退材料。中间测试以独立 Unity 工程、单独缓存和明确的包源运行。

### 2026-11-27 执行状态

- P0–P4 的受管主路径已提交。隔离工程覆盖原生主方向光、角色固定 PCF、Forward 与 Forward+ 的 Point／Spot、透明 Eye、私有 Capture、Weighted OIT，以及默认关闭的可选 RT。屏幕逐灯预算 `0`、`1`、`4`、`16` 与 URP 原生图集容量分别测试。预算为 `0` 时停发屏幕逐灯结果，URP 仍有原生图集。
- Skin 与 Environment 首批 D3D11 片元对照中，旧硬投影片元的直接 atlas 比较指令降为 `0`。Skin 的单变体时间由 `24.76 s` 降到 `0.52 s`，Environment 由 `16.87 s` 降到 `0.42 s`。两者的 sampler 均从 `11` 减到 `9`。Skin 的 SDF、Tension、Cookie 等高负载组合使用 `13` 个 sampler。数据以固定变体、固定依赖的编译请求为单位。
- P5 已验证在试验工程追加独立的受管 Renderer，旧默认索引为 `0`，新索引为 `1`。旧 Renderer 哈希一致。保留实际绑定的 13 个 RT 程序或材质引用，统一 RT 总开关仍关闭。Hair 和 Effect 的不透明／裁切预览已用实际材质验证。
- P5 的早队列透明 Hair 在隔离项目完成 D3D11 Forward 和 Forward+ 分层验证，覆盖原队列、同 owner 跨域、层遮罩及切换后清理。D3D12 的 Eye 与 OIT 六项回归逐像素一致。仍需在生产快照验证实际 Hair 材质和 owner，确认 Hiro 的七个 Full 候选确实收到预期投影，并核对具名 RT 镜头与相机栈。现有生产 Renderer 和旧实现继续支撑尚未迁移的接收端。逐项结果和复跑方法见 [场景迁移与 GPU 测量](../../Tools~/HoShadows/README.md)。

- WTD 的保存机位已确认可选 RT 主灯产品。RTX 4060／D3D12 的三组诊断中，开关只改变候选 Renderer 的 RT 主控，关闭两组均为 `0` 个产品，开启后得到 `1` 个产品。旧路径优化编译未在 `420 s` 内产出可比较帧，具名镜头的旧画面等价仍待验证。数据见 [具名 RT 镜头试切](../../Tools~/HoShadows/README.md)。
- 普通透明 Cloth、早队列 Eye 已有 D3D11 Forward+ 的独立深度和队列验证。Hiro 保存机位的可比较角色像素没有提供将七份候选转为 Full 的可见投影依据。长门保存场景已读回两域的四层无组普通透明接收面，实际 RGB 各组一致。数据见 [真实镜头的隔离对照](../../Tools~/HoShadows/README.md)。

## 7. BREAK_URP 迁移

1. 盘点 `Assets/Settings/PC_Renderer.asset`、`PC_RPAsset.asset`、默认 Volume、镜头/角色局部 Volume 与独立 Renderer 资产。按保存值和实际相机栈分别记录。现存 RT 配置可以是开启，新默认要显式迁为关闭，保留有意启用 RT 的具名场景或测试配置。
2. 在新 Renderer 中保持 URP 主光和逐像素附加灯阴影的原生配置，利用计划的 Full 消费。迁移掉 HoShadowCast 的自动收灯、图集预算、特殊方向灯行为和旧调试入口。当前 URP 原生附加光图集只承担受支持的 Point／Spot，遇到确实需要第二方向灯投影的镜头时单独决策。
3. 材质迁移先干跑。检查独立材质、Variant 承袭、prefab 与对象覆盖、脚本或动画写入以及使用的 Shader Pass。对旧 `Main` 中实际依赖原生附加灯的材质迁成 `Full=2`，保留 `Main=1` 和 `Full=2` 的存储映射。旧值显式关闭阴影响应的材料单独列账，避免误把未生效范围当成已启用。
4. 替换舞台生成和审计中查找旧 Feature 的调用。重点核对 `HoMmdStageLighting.cs` 的 Cast 容量写入、`HoMmdStageAudit.cs` 对旧 Feature 的 `.Single()` 和 Feature 顺序清单，以及 RT 诊断对主灯别名的依赖。新审计检查实际需要的原生阴影配置、统一 Feature 和 RT 策略。
5. 实际写入前保存待变更文件、旧配置、依赖哈希及待迁移清单。以可重复运行的迁移入口比较首轮和二次结果。生产默认见证验证完后，移除旧 Feature、PCSS 序列化参数和历史兼容逻辑。将旧文档显式标成历史，另外维护当前用户说明和手工配置示例。

## 8. 验收、计时和回退

### 8.1 编译和资源

- 保留可对照的三组隔离样本。旧 PCSS 直接接收、固定预算 PCF 直接接收、固定预算 PCF 屏幕解析。第二组只用于测量算法更换的增益。
- 同一依赖哈希下测 D3D11 默认 Forward、裁切／Face／Tension 重组合、Forward+、Eye 透明、角色 Capture 和 Cloth OIT。分别保存阶段、关键字、编译请求时间、缓存状态、DXBC 和真实 sampler 槽。另用目标 D3D12 构建核对路径和 RT 组合。
- 受管消费端反射应显示只读解析结果。直接 atlas、比较过滤循环和已退役的 PCSS 绑定不得被编入其材质片元。改动 PCF 内核后用依赖和复编记录证明成本集中在独立解析程序。
- 验证开启与关闭、输入有效与失效的路径，包括无投影灯、资源丢失、错 owner／深度、灯重排、溢出、相机回退。RT 关闭时检查该模块的派发、重建、纹理与历史资源。

### 8.2 画面和 GPU

- 另存静态和动态参考帧。覆盖原生主光、第二方向光退役差异、两盏不同颜色的灯只挡其一、肢体/发片遮挡、角色盒边缘、Mixed ShadowMask、Receive 关闭、PCF 固定软边、黑白遮罩和不同三段权重。
- 用实际代表场景记录每帧 GPU 的原生 atlas、角色 atlas、深度和身份准备、主光解析、逐灯解析、可选 RT、透明/Capture 和总帧时间，另报显存、峰值目标、分辨率、灯数与每域覆盖。保持质量、相机与分辨率相同，分开测少灯和多灯、低覆盖和全屏主灯。
- 在 P0 定代表项目的性能预算，以对应镜头的基线和实际屏幕结果成本验证。编译耗时的降幅与运行时帧耗分别报告。高频灯与多层透明需有各自的预算明细。
- 默认发布条件包括：光栅 PCF 与计划接受的画面变化已确认，Main 与 Full 的基线可解释，透明/Capture/OIT 的生产使用范围已通过，原生逐灯阴影生效，RT 默认关闭，旧双套场景图集确实退出 Frame Debugger。

### 8.3 回退和定位

P5 前保留可复跑的旧 Renderer／资产快照和跨仓版本清单。若视觉或 GPU 验收失败，先回退失败镜头的 Renderer 配置和依赖组合，用当前步骤的可见度与身份调试定位差异。回退以明确的测试或发布版本完成。材质中的运行时重滤波回退会重新引入旧编译耦合，统一入口内只读取当前帧已解析的合法结果。

## 9. 参考入口

- [Ho Shadows 使用说明](../Ho-Shadows.md) 提供材质范围、手工镜头、Cast 遮罩和 RT opt-in 的配置示例。
- [RendererFeature 顺序契约](../Ho-RenderFeatureOrdering.md)与 [Surface Buffer](../Ho-SurfaceBuffer.md) 说明当前资源调度、覆盖和身份合同。
- [当前 Ho-RT 实现](../Ho-RT.md)、[逐灯研究与长期规划](Ho-RTShadow-ManyLight-Research-Plan.md) 提供当前 RT 产品和历史研究。新版 Full 使用可查询逐灯可见度。大量灯的聚合研究按其独立任务推进。
- [Unity 内置 Screen Space Shadows](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/renderer-feature-screen-space-shadows.html) 提供原生主方向光和相机深度的实现参考。Full 的逐灯屏幕结果与各透明域需要 Ho 自己明确覆盖。
- [Unity HDRP 屏幕接触阴影](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/Override-Contact-Shadows.md) 可作为后续 SSRTS 的有界屏幕深度追踪参考。

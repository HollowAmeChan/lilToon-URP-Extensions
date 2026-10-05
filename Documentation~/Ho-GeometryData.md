# Ho-GeometryData

状态：ReferenceFrame、OutlineCorrection 与 Tension 首版代码已实现。GD 发布专用数据，消费者自己选择来源；不建立通用属性覆盖链。Tension 的正式 GPU / 材质验证记录见下文。

组件类名统一包含 `GeometryData`：`HoGeometryDataReferenceFrame`、`HoGeometryDataOutlineCorrection`、`HoGeometryDataTension`。lilToon 的默认关闭 `Tesion`、拉伸/挤压两组 BaseColor + NormalMap 契约见 [Tesion 设计](计划/Ho-GeometryData-Tesion-Plan.md)。

## 1. 参考朝向与眼透

在原 OB 组所在对象添加 `Rendering/Ho-GeometryData ReferenceFrame`，将骨骼或参考空物体赋给“参考朝向”，配置前/右/上轴。OB 的“GD 参考朝向”可以显式关联该组件；未指定关联时使用同对象上的组件。

OB 只负责身份关联。旧 `faceBone` / 轴配置、`HoFaceAxis` 和 `TryGetWorldFacing` 已删除，不做旧配置兼容或自动迁移；场景/预制件由用户自行调整。

ReferenceFrame 提供世界原点和轴，随 Transform 动态更新。眼透保持既有角度与衰减公式，在 shader 中以当前 pass 捕获的相机位置求值。没有每相机 yaw/pitch 纹理；不同相机的视角参数分开，参考系内容未变时不重复上传。

未提供、禁用或无效参考系时角度因子回到 1。`EyeAngleFactor (16)` 看衰减；`ReferenceFrameView (17)` 看当前求值的 yaw/pitch，编号 17 不变。部件参考系由 GD 组件按部件名配置，眼透目前仍按角色组读取默认参考系。

该消费者自己记录 frame buffer 的 RG 读依赖，因此只使用眼透朝向时无需额外添加 GD RendererFeature。其他消费者使用 ReferenceFrame 的 CPU 输出或自己的 GPU 绑定接口。

## 2. 描边修正

1. 在 URP Renderer Data 添加 `HoGeometryDataRendererFeature`。
2. 在 MeshRenderer / SkinnedMeshRenderer 对象上添加 `Rendering/Ho-GeometryData OutlineCorrection`，指定目标 Renderer。
3. 按需要启用“初始化时准备”“Play 前准备”，或点击“生成 / 更新描边修正”。
4. lilToon 描边的来源选择 `GD (Direction + Width)`，值为 3。原值 0/1/2 保持原语义。

原算法来自本机 Blender 4.5 HoTools 的 `SOLIDIFY_RAW2SMOOTH`：按边的两面角累计方向、Laplacian 判断曲率符号、角点加权求 shell 因子，再生成 TBN RGB 和以 0.5 为中性值的 A 补偿。专用输出不写 Mesh 顶点色。

为了保持既有 RGBA 来源的画面，GD 把自己的数据交给同一个方向/宽度消费路径；A 仍按旧 lilToon 路径乘宽度，没有擅自改成另一种 shell 解码。准备动作与保存结果发生在组件上，不在 AssetPostprocessor 中触发。

结果缓存在组件中，可以跨 Play/序列化重建复用。修改模型或准备参数后使用按钮更新。基准使用原始 Mesh；运行时由现有 TBN 跟随蒙皮，首版不每帧重算邻接与 shell。

新来源使用专用材质关键字 `_HO_GD_OUTLINE`，只该变体要求 SM4.5。通过材质 Inspector 选择时同步关键字；脚本选择时使用 `HoGeometryDataOutlineCorrection.SelectMaterialSource(material, true)`。原来源保留原编译目标与消费行为。

## 3. 数据与资源边界

- 数据按具体 Renderer 与原 Mesh vertex ID 寻址。RSUV 低 16 位继续为 OB 身份，高 16 位为共享 GD Renderer 槽；描边与张力通过同一槽查询各自的专用表。公共 transport 写入保留另一个位域。
- 槽在组件启用或显式准备时建立，先于相机剔除，避免首帧绘制拿到 0 号槽。Inspector 参数改变仅在 OnValidate 标记，后续主线程 Update 更新关联；这遵循 [Unity 的 OnValidate 线程约束](https://docs.unity3d.com/kr/current/ScriptReference/MonoBehaviour.OnValidate.html)。
- GD Feature 在绘制前发布外部持久只读 buffer，使用明确的全局状态同步点；首版没有 async compute 或 transient 输出。
- 停用某个组件只撤销它自己的关联；最后一个生产者离开时才收回 GD 槽。无数据/无发布时 GD 来源回退已有材质行为。参考系、描边与张力是独立输出。
- 描边构建需要可读 Mesh、法线和切线，或已准备的组件缓存。仅支持三角形/四边形。静态合批改变索引，首版不发布给已静态合批的 Renderer。
- 同位置连接需要位置、骨骼权重及 morph 位置轨迹一致。完全重合而实际应分离的表面可关闭连接；不能仅靠导入数据保证重建任意 DCC 原始拓扑。

## 4. 验证记录

验证代码、隔离包/工程、Blender fixture、日志与截图在本地忽略目录 `research~/GeometryData/`，不进入生产 Runtime/Editor。目标为 Unity 6000.3.15f1 / D3D11、D3D12。

ReferenceFrame 正式代码通过 148 项检查，包含真实眼透 shader 对照、真实 GB/OB/AC/角色特化调用、多相机上传复用、动态转头、禁用、资源重建和正交旧行为。OutlineCorrection 通过 22 项检查：6 组 HoTools 角点数据最大误差约 6.67×10⁻⁸；实际 lilToon 新旧来源图像差 0；蒙皮 + 形态键与旧来源对照通过。具体结果见本地 `research~/GeometryData/Production-Report.md`。

## 5. Tension 与材质 Tesion（2026-10-05）

1. 在 URP Renderer Data 添加 `HoGeometryDataRendererFeature`。
2. 在 SkinnedMeshRenderer 对象上添加 `Rendering/Ho-GeometryData Tension`，指定目标 Renderer。
3. 通过初始化、Play 前或“生成 / 更新张力参考状态”按钮准备原 Mesh 的参考姿势。编辑器可用 MeshUtility 导入数据快照准备 Read/Write 关闭的模型，不改变 importer 的开关。修改几何/拓扑后手动更新参考；不靠资产导入触发。Player 使用已保存的参考缓存，或可读源 Mesh。
4. lilToon 完整 URP 材质的“额外属性 → Tesion”默认关闭。启用后分别指定拉伸与挤压的 BaseColor / NormalMap、范围、强度。特殊 Lite/Fur/Gem/Hair/Liquid 材质暂不开放。

组件仅持有参考位置、拓扑、测量权重及 GPU 资源，不修改材质、UV、normal 或顶点色。每帧在 native skinning 后取得 Raw 位置，转换到 Renderer-local 空间，再计算三角面面积/角点角变化与 CSR 顶点 gather。输出为 float4：拉伸、挤压、角变化、有效性。没有逐帧 CPU BakeMesh。

动态资源按实例分开，Time.frameCount 未改变且测量参数未变时，多相机复用已生产的结果。离屏持续测量需要 Renderer 的 Update When Offscreen；组件不强行改变这个 Renderer 设置。Shader 数据缺失时维持原颜色/法线。

Tesion 的 BaseColor 仅混合 RGB，保持原 alpha/clip。普通 normalmap 组合完成后，在切线空间与额外 NormalMap 混合，再进入世界法线。两路同时响应时按对称权重归一化，没有后者覆盖前者；未指定的某类贴图不参与该类混合。拉伸/挤压每路共享该路 BaseColor 的 UV0 tiling/offset。

脚本设置材质开关或纹理后调用 `HoGeometryTensionMaterial.Synchronize(material)`，同步专用本地关键字和贴图存在标志；Inspector 自动同步。关闭 `_HO_GD_TENSION` 时不引入新增 varying、纹理采样或 SM4.5 编译要求，材质常量缓冲布局保持一致。

GPU Extract / Triangle / Gather 显式声明输入、临时读写和输出 buffer；发布 pass 读取结果并绑定全局表。当前使用同步 graphics queue，未开启 async compute。RenderGraph 的 `ImportBuffer` / `UseBuffer` 方式依据 [Unity Compute Shader 输入资源文档](https://docs.unity3d.com/cn/6000.0/Manual/urp/render-graph-compute-shader-input.html)。

`HoGeometrySkinnedSource.Prepare/TryAcquire` 提供原生蒙皮 GPU 缓冲的借用、实际 stride 与 Renderer-local 分析空间转换。当前支持范围为已验证的 D3D11 / D3D12、有效 rootBone、stream0/offset0 的 float32 三维位置。调用方必须在 native skinning 后取得样本，并在 GPU 消费完成后释放；接口不负责强行推进 Unity 的蒙皮。

此目标的 GPU 位置已包含缩放，却使用根骨骼位置/旋转参考系；转换使用 `renderer.transform.worldToLocalMatrix * TRS(rootBone.position, rootBone.rotation, Vector3.one)`，避免将根缩放再应用一次。24 组骨骼/morph/缩放样本对解析参考的最大误差约 9.10×10⁻⁷。

research 中的边长、面积、角点角变化 Compute 原型通过 8 组 CPU/GPU 对照，最大误差约 3.65×10⁻⁶。正式组件、调度与 lilToon 消费的验证结果单独记录在本地 `research~/GeometryData/Results/production-tension-results.json`，不与原型结果混用。显式捕获当前 GPU 姿势作为基准、其他 graphics API / skin 布局和特殊材质仍留待后续。

正式 Tesion 验证通过 90 项检查：native skin / morph / 多实例测量、双相机复用、描边共存、真实 GB NormalMap、两路颜色响应、缺图/缺组件回退、Cutout alpha、资源重建。GPU 数据对 CPU 最大误差约 3.65×10⁻⁶，关闭后的像素误差为 0。场景为合成几何；真实角色的美术响应和性能需要目标场景验证。

## 6. 在编辑器里调试

在相机作用范围内的 Global / Local Volume Profile 中添加 **Post-processing → Ho-GeometryData → 几何数据**。调试只在 Volume；Feature 保留运行兜底与只读数据源状态，不显示调试选项。

1. **Volume → 调试**：勾选调试模式的 Override，先选 Tesion 有效性，检查绿色 / 品红。然后改为拉伸 / 挤压 / 角变化或描边数据视图。
2. **Scene / Game**：Volume 中分别控制，默认值均开启，但默认调试模式关闭。Layer Mask 只过滤预览，热图满量程默认 0.2，不改变测量或材质。
3. **Feature → 数据源**：查看场景的参考系 / 描边 / Tesion 组件，点“定位”打开组件自己的准备按钮。这里显示缓存、槽、版本、生产帧 / 次数与失败原因。
4. **Feature → 运行**：检查当前 Renderer 是否曾执行此 Feature。Volume 的“启用”勾选 Override 后覆盖运行兜底；未勾选则使用 Feature 运行值。
5. **参考朝向**：选择 HoGeometryDataReferenceFrame，在 Scene 查看前蓝 / 右红 / 上绿；眼透观察角继续由 CharacterSpecialization 的 ReferenceFrameView / EyeAngleFactor 检查。

| 数据预览 | 读法 |
| --- | --- |
| GD 绑定槽 | 彩色表示共享 GD 绑定，深灰表示未绑定 |
| 描边方向 | 世界方向编码 RGB = direction × 0.5 + 0.5 |
| 描边厚度 | 灰度为原算法输出 A，0.5 中性；不是最终屏幕像素宽度 |
| Tesion 拉伸 / 挤压 / 角变化 | 黑色是零，蓝→青→黄→红表示信号变强 |
| Tesion 有效性 | 绿色有效，品红无来源/未生产/无效顶点；也显示未绑定的模型 |

其它数据视图的品红同样表示该对象缺少所选专用数据。Tesion 预览不需要开启材质 Tesion；让角色变形，观察拉伸 / 挤压视图即可核查生产结果。

这是几何数据的独立预览：替换所选视图画面，使用自己的深度，跳过材质 alpha clip 与描边位移。普通模型在绑定视图显示灰色，在有效性视图显示品红，其它模式只显示有 GD 槽的模型。后续屏幕处理仍可能处理预览，必要时调整预览绘制时机或暂时关闭相应后处理。

数据预览通过隔离 Unity 项目的 36 项检查，包含 CustomEditor 选择、配置字段、首帧有效性、Renderer 槽、描边方向/厚度、三通道、Game 开关、Layer Mask、组件停用、Feature 停用与预览切换不重复生产。材质 Tesion 的 90 项回归继续通过。验证脚本与日志仍在本地忽略的 `research~/GeometryData/`。

### D3D12 / Read/Write 关闭模型验证

原 D3D11-only 检查已在验证后扩展到 D3D12：24 组 native skin / morph / 非均匀缩放位置，对解析参考最大误差约 9.10×10⁻⁷。D3D12 Volume 预览与覆盖 / 回退通过 38 项检查。

用户 GD 测试目录关联的 potato.fbx 保持 Read/Write 关闭，在隔离工程中实际导入并验证其上衣网格（11962 顶点、17470 三角面）。编辑器准备、D3D12 生产、Volume 绿色有效性与骨骼缩放后的红色拉伸热图全部通过，共 11 项检查。测试文件、FBX 副本与截图仅在 research~，不进入生产包。

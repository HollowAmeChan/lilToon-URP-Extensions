# Ho-GeometryData

状态：ReferenceFrame 与 OutlineCorrection 首版实现。GD 发布专用数据，消费者自己选择来源；不建立通用属性覆盖链。Tension、皮肤褶皱和其他几何模块后置。

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

新来源使用专用材质关键字 `_HO_GD_OUTLINE`，只该变体要求 SM4.5。通过材质 Inspector 选择时同步关键字；脚本选择时使用 `HoOutlineCorrection.SelectMaterialSource(material, true)`。原来源保留原编译目标与消费行为。

## 3. 数据与资源边界

- 描边数据按具体 Renderer 与原 Mesh vertex ID 寻址。RSUV 低 16 位继续为 OB 身份，高 16 位为描边槽；公共 transport 写入保留另一个位域。
- GD Feature 在绘制前发布外部持久只读 buffer，使用明确的全局状态同步点；首版没有 async compute 或 transient 输出。
- 停用组件后描边槽收回；无数据/无发布时 GD 来源回退材质原法线与宽度。参考系和描边是独立输出。
- 构建需要可读 Mesh、法线和切线，或已准备的组件缓存。仅支持三角形/四边形。静态合批改变索引，首版不发布给已静态合批的 Renderer。
- 同位置连接需要位置、骨骼权重及 morph 位置轨迹一致。完全重合而实际应分离的表面可关闭连接；不能仅靠导入数据保证重建任意 DCC 原始拓扑。

## 4. 验证记录

验证代码、隔离包/工程、Blender fixture、日志与截图在本地忽略目录 `research~/GeometryData/`，不进入生产 Runtime/Editor。首个目标为 Unity 6000.3.15f1 / D3D11。

ReferenceFrame 正式代码通过 148 项检查，包含真实眼透 shader 对照、真实 GB/OB/AC/角色特化调用、多相机上传复用、动态转头、禁用、资源重建和正交旧行为。OutlineCorrection 通过 22 项检查：6 组 HoTools 角点数据最大误差约 6.67×10⁻⁸；实际 lilToon 新旧来源图像差 0；蒙皮 + 形态键与旧来源对照通过。具体结果见本地 `research~/GeometryData/Production-Report.md`。

## 5. Tension 基础接口进展（2026-10-05）

`HoGeometrySkinnedSource.Prepare/TryAcquire` 提供原生蒙皮 GPU 缓冲的借用、实际 stride 与 Renderer-local 分析空间转换。当前支持范围明确限定 D3D11、有效 rootBone、stream0/offset0 的 float32 三维位置。调用方必须在 native skinning 后取得样本，并在 GPU 消费完成后释放；接口不负责强行推进 Unity 的蒙皮。

此目标的 GPU 位置已包含缩放，却使用根骨骼位置/旋转参考系；转换使用 `renderer.transform.worldToLocalMatrix * TRS(rootBone.position, rootBone.rotation, Vector3.one)`，避免将根缩放再应用一次。24 组骨骼/morph/缩放样本对解析参考的最大误差约 9.10×10⁻⁷。

research 中的边长、面积、角点角变化 Compute 原型通过 8 组 CPU/GPU 对照，最大误差约 3.65×10⁻⁶；切线方向褶皱响应的基准、开关与拉伸/挤压验证通过。Tension 组件、全链路调度和 lilToon 正式皮肤褶皱组分尚未完成，不能将这些原型验证当作完整功能。进展见本地 `research~/GeometryData/Tension-Progress.md`。

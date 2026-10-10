# Ho-VolumeLight v0.1：太阳 HD 丁达尔

面向 HoURP 17 RenderGraph。读取固定 HoAtmosphere 的空间介质输入与场景已有主方向光，在 SDF 激活区域内显示体积光；无需逐窗添加灯。

## 使用

1. 保持基础 `HoAtmosphereRendererFeature` 开启，在 Renderer Data 添加 `HoVolumeLightRendererFeature`。
2. 通过 `GameObject > Rendering > Ho-Atmosphere 大气区域` 创建区域，也可在对象上添加该组件。
3. 调整 Transform 和区域大小，覆盖房间/树林；选中区域可用场景手柄改大小。
4. 调节边缘淡出、介质密度/颜色、体积光参与权重；可选指定 Texture3D 密度纹理。
5. 使用场景已有太阳作为 URP 主方向光并启用所需阴影。VolumeLight 自动跟随其方向、颜色、色温解析结果和强度。
6. 在 Feature 兜底设置或 `Ho-VolumeLight/太阳 HD` Volume 中调体积亮度倍率、最大照亮距离、步数和散射方向性。Volume 未覆盖时使用 Feature 值。

默认 Shader 位于本模块 Resources，随包保留，不要求修改项目的 Always Included Shaders。高级 Shader 覆盖须兼容默认 Shader 的效果和调试两条 pass。

## 两层职责

- **HoAtmosphere Region**：提供世界空间激活权重、介质属性与保守 Bounds。
- **VolumeLight**：保留自己的照亮范围、相机可见区间、照明与遮挡。每个采样点将大气激活权重乘到实际光束贡献上。

只在 SDF 区域内显示，区域不替代光源范围或改造太阳阴影视图。区域外屋檐/树枝仍能把阴影投射到区内。v0.1 太阳路径直接读取 URP 原始主光级联图，即使开启 Screen Space Shadows 也不使用表面屏幕阴影代替空气遮挡，并遵循太阳阴影距离淡出。

相机深度负责停止物体后方的视线积分；太阳阴影负责空气采样点是否被照亮。二者独立。输出为线性 HDR 稀薄介质加亮贡献，Bloom 可自然消费。

## 大气区域参数

| 参数 | 含义 |
| --- | --- |
| 区域大小 | 局部盒形尺寸，支持 Transform 旋转、缩放；Bounds 是保守世界包围盒 |
| 边缘淡出 | 区域内部距边界的世界距离（米），缩放后保持该距离语义 |
| 体积光参与 | 显示权重；0 时不产生效果，不改变光源或其阴影 |
| 介质密度/颜色 | 每米散射强度与介质 HDR 染色 |
| 空间密度纹理 | 可选 Texture3D，R 通道调制密度；为空使用均匀介质 |
| 纹理扰动强度 | 在均匀密度与纹理密度之间插值 |
| 纹理世界尺度 | 纹理重复长度（米）；采样坐标固定于世界 |
| 风速 | 纹理沿世界方向移动的米/秒速度 |

纹理循环采样，应使用可平铺的密度内容。灯光、相机或区域移动不会拖着噪声移动。编辑器与运行时使用共同世界时间；`HoAtmosphereMediumClock.TimeOverride` 可在脚本中指定确定的采样时间，默认留空。

## 调试与状态

- HoAtmosphere Volume 提供“介质密度切片”“介质激活切片”：查看区域局部 XY 中央切片。
- Ho-VolumeLight Volume 提供“体积贡献”“无阴影贡献”，在后处理后独立显示。
- HoAtmosphere Feature 单独显示环境与空间介质状态，VolumeLight Feature 显示输入/太阳状态。
- HoAtmosphere Volume 的“环境输入启用”仅控制环境辐射，不关闭有效的区域介质。

## 数据接口

CPU 可从 `HoAtmosphereRegionRegistry.Capture()` 获取只读快照，读取 `valid / bounds / revision / sampleTime`，并通过 `ActivationAt(positionWS)` 查询空间权重。

RenderGraph 消费者读取 `HoAtmosphereMediumResources`，检查 `IsValid`，对实际采样的 `densityTexture` 声明 `UseTexture(Read)`；用 `HoAtmosphereMediumBindings.Bind` 绑定（声明全局状态修改）。资源句柄限当前相机 graph。HLSL 入口是 `Runtime/Atmosphere/Shaders/HoAtmosphereMedium.hlsl` 中的 `HoAtmosphereSampleMedium(positionWS,time)`，分别返回 activation、density、tint。

区域 revision 只描述静态数据/变换/纹理内容，时间单独发布。它不复用 `HoAtmosphereRenderGraphResources` 的环境 revision 或 `IsValid`。区域变化不会导致天空重捕获或 GI 的环境历史失效。

## v0.1 边界

### Cutout 与连续灰度透光

已额外 GPU 验证 lilToon Cutout 的 `_MainTex.a` 和独立 `_AlphaMask`：原 ShadowCaster 的孔洞能进入太阳 atlas，再影响体积光。二值 cutout 不依赖 SB/AC；Renderer 必须投射阴影，太阳阴影及采样距离/精度也需要有效。

这表示按 `_Cutoff` 判定孔洞/实心，并不表示 alpha=0.5 会按比例透过 50% 的光。细碎叶片在远端可能受纹理 mip、阴影分辨率、PCF 和体积步数影响；仅看到细节不明显不能认定贴图没有被读取。

用户确定后续需要连续灰度透光；彩色透光与高级玻璃实现先搁置。灰度需要光源空间的透光率与深度输入：只有光源到采样点之间的层参与，多个层的透光率相乘。当前尚未实现此输入。

建议通过 Renderer 挂件显式提供透光纹理/强度，避免要求所有材质增加新属性。原有 cutout 保留二值路径；参与灰度覆盖的对象不能先被二值 shadow map 当成全遮挡再尝试相乘，需要独立/分离的 opaque visibility 捕获或等价处理，避免 0 阴影无法恢复。SB/AC 可以提供对象选择/材质参数契约，实际遮挡仍在光源空间生产；主相机表面图不能覆盖画面外投影者或空气中任意点。具体接入在灰度透光阶段实现。

当前支持一个启用的 Box 区域与太阳 HD。没有区域发布空介质；多个启用区域明确报告 v0.1 容量限制并发布空介质，不随机选一个。

实际绘制是全分辨率、无历史的视线采样；还没有自适应步进、降采样上采样或 froxel。可调 8–256 步，默认 48；小区域与硬阴影可能需要更高步数。关闭效果释放其材质和调试资源；基础大气输入继续有效。

仅声明 HoURP 17 RenderGraph 的 Base Game/SceneView 相机路径。透视、正交、相机在区域内/外、半分辨率与 MSAA4 均有 GPU 验证；Overlay/Reflection/Preview/XR 和 Solar compatibility 未列入支持。大气原有环境输入 compatibility 路径保持原职责。

不透明/alpha cutout 提供场景深度截止与光源遮挡；当前不模拟透明玻璃透射、浓雾消光/多重散射。局部灯 SD/HD、独立局部 LightDepth、多区域规则、可见灰尘颗粒以及 AC/AOV 输出按后续阶段接入。

## 验证

隔离工程执行入口：`lilToon.URP.Extensions.VolumeLight.HoVolumeLightValidation.Run`，需有实际 GPU，不使用 `-nographics`。

验证直接在 graph 中采样/readback，覆盖：

- Region Inspector 类型、Undo/Redo、默认 Shader 资源。
- 中心/边缘/区域外权重、旋转缩放后世界米淡出、世界纹理流动与跨相机一致性。
- 环境/介质有效性与 revision 独立、区域变更不重捕获天空、空/禁用/多区域回退。
- 实际太阳强度/颜色、SDF 参与开关、动态噪声 RGB 变化。
- 实体窗框、alpha cutout、画面外/区域外投影者。
- SCREEN 阴影 keyword 确实执行，并与原 atlas 结果一致；阴影距离外恢复照明。
- 相机内部/外部、正交、中心及非中心前景深度、半分辨率、MSAA4。
- 调试贡献、关闭/重启效果、RendererFeature 禁用和 Dispose 的输出/材质释放。

结果与 EXR/PNG 位于隔离工程 `VolumeLightValidation`；日志与需求逐项证据见 [v0.1 验证记录](计划/Ho-VolumeLight-v0.1-验证记录.md)。

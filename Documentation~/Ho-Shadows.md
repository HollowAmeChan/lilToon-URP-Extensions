# Ho Shadows 使用说明

> 状态：隔离试用（2026-11-27）

Ho Shadows 供使用 HoURP 的 Renderer 输出主光和逐灯的屏幕阴影。场景投影由 URP 的主光级联和附加灯图集生成。角色近身自阴影可用局部图块加细，材质继续用原有的阴影色、接收强度和遮罩塑形。

本页对应现有隔离试用配置。包版本和镜头验证入口见 [场景试用工具](../Tools~/HoShadows/README.md)。

## 主光与 Full

| 材质范围 | 屏幕结果 |
| --- | --- |
| Main | 主方向光的原生投影。角色启用局部阴影且接收者匹配时，通过角色图块细化。 |
| Full | 主光加指定预算内投影的 Point／Spot 灯。启用 RT 的 Renderer 可为符合条件的灯和接收者使用可选逐灯结果。 |

灯的 `Shadows`、阴影强度、范围和渲染层继续在 Unity Light 上设置。把材质切到 Full 之后，附加灯还要通过该材质的接收强度、该相机的 URP 逐像素灯和阴影配置。屏幕预算为 `0` 时停发 Full 的光栅逐灯可见度。每灯的颜色、Cookie 和距离衰减仍由对应灯决定。

## 配置试用镜头

1. 使用带 HoURP 17.3 与对应 HoShader 接收 Pass 的隔离 Renderer。保留原 URP 主光阴影和逐像素附加灯阴影，添加一次 `Ho Shadows`。保留现有的 Object／Surface／Geometry 等输入生产端。需要私有角色 Capture 或 Weighted OIT 时，按实际 Pass 顺序接在相应输入准备之后。顺序和隔离 Renderer 的设置见 [RendererFeature 顺序](Ho-RenderFeatureOrdering.md) 和 [试切说明](../Tools~/HoShadows/README.md)。
2. 给一盏 Directional Light 开启 Soft Shadows，再为两盏不同颜色的 Spot 开启投影。让每盏灯各有一个独立遮挡体，并在相机中同时覆盖一块接收面。给同一接收面做 Main 和 Full 两份材质副本，用同一相机切换比较。材质若启用了三段阴影，也要给用于对比的段开启 Cast 接收。
3. 验证角色时，为角色配置 [角色局部阴影](Ho-CharacterShadow.md) 中的范围、接收部位和 ObjectBuffer 组。启用受管角色阴影，核对同一个角色在局部盒内外的交接。布料的角色屏幕阴影需要相机深度、SurfaceBuffer 深度和接收者 ID 对应同一个可见面。
4. 验证透明时保留材质原队列。`0–2500` 范围内的透明绘制使用独立早队列解析，`2501–5000` 范围内的普通透明绘制使用常规透明解析。加权 OIT 和角色 Capture 各有专用域。开启对应 Renderer 选项，并用叠放接收面检查深度、可见层和 owner。早队列当前在 Forward／Forward+ 且关闭 Depth Priming 时可用。

## Cast 接收遮罩

支持 `_ShadowReceiveMask` 的材质以线性 R 通道控制投影接收。可先制作一张左黑右白的灰度图。关闭该贴图的 sRGB 导入，把贴图放到 Cast 接收遮罩槽，让阴影横跨黑白边界，并确保选中的三段阴影权重允许接收。黑区减少投影接收，白区保留材质设定的投影接收。阴影色、渐变、AO 和材质 SDF 仍按家族自身的用途设置。

## 具名镜头启用 RT

RT 默认关闭。具名镜头使用专属 Renderer，显式开启它及对应的 RT Shadow Volume，并保留已有的 RT 灯、接收域和资源引用。使用 Full 材质检查每盏灯的屏幕结果，分别比较 RT 开启和关闭。RT 资源、灯索引或接收者匹配失败时，该灯使用可用的原生结果。设置 RT 程序、SIGMA 资源和灯接收条件见 [Ho RT](Ho-RT.md)。

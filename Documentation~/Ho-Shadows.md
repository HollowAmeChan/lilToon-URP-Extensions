# Ho Shadows 使用说明

Ho Shadows 是 HoURP 的屏幕阴影 Renderer Feature。场景投影复用 URP 的主光级联与附加灯图集。角色局部阴影补充主光细节，图集解析统一使用固定 PCF。材质控制投影接收强度、接收遮罩和阴影色。

## 主光与 Full

| 材质范围 | 阴影来源 |
| --- | --- |
| Main | 主方向光的原生投影与匹配接收者的角色局部阴影。 |
| Full | Main，以及相机预算内的 Point／Spot 投影。支持 RT 的具名 Renderer 可以给符合条件的灯发布 RT 可见度。 |

继续在 Unity Light 上设置投射、强度、范围、Cookie 和渲染层。Full 的逐灯光栅接收需要开启 URP 逐像素附加灯和附加灯阴影。屏幕逐灯预算为 `0` 时停发该组可见度。

## 配置 Renderer

1. 在 HoURP Renderer 上添加一次 `Ho Shadows`。保留相机需要的 Geometry、Object、Surface 输入生产端，把 Ho Shadows 放在对应输入之后。角色 Capture 与 Weighted OIT 使用各自的接收通道和身份匹配。按实际依赖配置 Feature 顺序。[RendererFeature 顺序](Ho-RenderFeatureOrdering.md)
2. 给 Directional Light 开启投影，再增加两盏不同颜色的 Spot。给同一接收面分别使用 Main 和 Full。三段风格材质需要让用于对照的阴影段有 Cast 接收权重，才能观察对应投影。
3. 给角色设置局部投影范围、接收部位和 ObjectBuffer 组。用跨越局部范围的角色姿势检查交接。布料的角色屏幕阴影需要相机深度、SurfaceBuffer 深度和接收者 ID 匹配同一可见面。[角色局部阴影](Ho-CharacterShadow.md)
4. Hair、Cloth 和 Eye 的合格普通透明面按队列接收。`0–2500` 使用早队列，`2501–5000` 使用常规透明队列。覆盖和深度参与原生阴影匹配。角色局部阴影、Weighted OIT 和 Capture 还校验身份。早队列支持 Forward／Forward+，相机须关闭 Depth Priming。

HoURP 的合格材质自动读当前相机的屏幕结果。相机缺少有效结果时，保留烘焙阴影，实时可见度为中性。标准 URP 独立安装继续使用原生阴影。Environment 透明、Effect 描边和特殊混合、Hair 的 OIT 与 Fur 当前保留原生接收。

## Cast 接收遮罩

支持 `_ShadowReceiveMask` 的材质以线性 R 通道控制投影接收。可先制作一张左黑右白的灰度图。关闭 sRGB 导入，把贴图放入 Cast 接收遮罩槽，再让投影横跨黑白边界。为需要对照的阴影段开启 Cast 接收。黑区减少投影接收，白区保留材质设定的接收强度。阴影色、渐变、AO 与材质 SDF 继续分别设置。

![左半为黑色、右半为白色的 Cast 接收遮罩测试图](Images/Ho-Shadows-Cast-Receive-Mask.png)

## 具名镜头启用 RT

RT 默认关闭。给确实需要 RT 的镜头选用专属 Renderer，显式开启 Ho Shadows 的 RT 总开关与对应 RT Shadow Volume。保留该镜头的 RT 灯、接收组与程序资源，在 Full 材质上分别检查开关前后的投影。RT 结果缺失或接收匹配失败时，使用可用的原生结果。[Ho RT](Ho-RT.md)

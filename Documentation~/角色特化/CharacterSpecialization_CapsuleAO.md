# Capsule AO / Cross 接收面叠加

状态：现行。2026-11-25。已按实际使用反馈改为独立接收面叠加，取代写入公共 HoAO 的首版设计。

Capsule/Cross 属于现有角色特化 Feature。它额外重绘登记的接收 Renderer，计算私有遮蔽，再将结果叠加到不透明颜色。**不读写 HoAO，不要求开启 HoGTAO 或 GB，不经过 lilToon 的 AO 来源、阴影和压暗设置。** GTAO 的 AO 纹理保持原样，两项可以分别开关与调强度。

## 使用

1. Renderer Data 中保留 HoCharacterSpecialization。胶囊 AO 不再要求 GB → GTAO → 角色特化的同事件顺序；其它依赖 GB/GTAO 的效果照旧配置。
2. 脚骨添加 **Rendering / Ho Capsule AO Emitter**，校准中心、轴、半径、总长度。组件只定义几何，不需要物理 Collider。
3. 地板 MeshRenderer 对象添加 **Rendering / Ho Capsule AO Receiver**，无需填参数或换材质。
4. 角色特化 Volume 的“胶囊 AO（接收面叠加）”启用后，全局选择 Capsule / Cross，调强度、影响距离与十字形状。材质 AO 关闭也能显示。

已有组件和 Volume 的模式、强度、几何配置继续使用。旧“与 HoAO 混合”选项已移除；现在只在私有遮蔽内取最强遮挡，再对颜色附加一次。HoGTAO 的“仅发布 AO 纹理”仍保留，但本项不需要它；如需正常 GTAO，请保持正常计算模式。

## 算法与执行

- Capsule：胶囊最近球的投影盘遮蔽近似，使用接收点位置、法线、半径与距离衰减。没有宣称与 ZMD Log-SH 等价。
- Cross：在接收面切平面内形成两道扁平软暗带，每道向两头渐隐、横向独立羽化。朝向跟随脚轴，并受全局旋转和夹角控制。半臂长、全宽与软度采用世界单位。
- Cross 高度：按胶囊端点相对接收面法线的距离衰减，允许脚骨中心在地面、胶囊部分穿地；只有整个胶囊处于接收面背后或离面太远才消失。旧核“胶囊底部低于地面 2 cm 就裁掉”的条件已经移除。
- 多胶囊取最大遮蔽；重叠 Receiver 先合并私有可见度，再统一压暗一次，不会重复相乘。

执行阶段固定为 **BeforeRenderingTransparents**：不透明与天空完成后，透明绘制前。它不属于原五项角色颜色合成的排序循环。系统向 URP 请求相机深度，用它拒绝被前景挡住的接收面；不额外要求 GB 法线／深度，也不读取或重绑 HoAO。

接收重绘写一张清白的私有 R8 可见度，Min 混合合并覆盖；第二趟固定功能 Multiply 写相机颜色的 RGB，保留 alpha，不采样正在写入的颜色目标。既有材质无需添加属性、Pass 或关键字，也不会被克隆、改资产或设置 MaterialPropertyBlock。

这是对接收面已着色结果的整体压暗，包含该表面的反射和自发光；不再沿用材质 AO 对 Toon 阴影等分量的单独响应。它仍会与已经呈现在颜色中的 GTAO 同时变暗，但两份源数据和开关互不污染。

## 参数与支持范围

强度默认 0.75，影响距离默认 0.5；十字半臂长 0.25、全宽 0.06、夹角 90°、软度 0.02。相机胶囊预算默认 32，上限 64，按距离和实例 ID 稳定选择。组件的非均匀缩放／父级剪切使用保守横向半径，无效几何跳过。

支持普通不透明 MeshRenderer + MeshFilter，包括静态合批、接收面倾斜和随脚移动。重绘采用 mesh 几何和法线，不复制原材质的自定义顶点位移／复杂 alpha clip，也不支持蒙皮接收面。完全共面物体无法仅靠深度容差区分，需避免此类歧义接收布局。

Windows D3D11 / D3D12 的 Editor 中验证 RG 正交、RG 透视、RG 4× MSAA 和 Compatibility 正交。XR、Overlay、非全 viewport、动态分辨率明确跳过；Player、其它 API、复杂角色美术与大规模性能未验收。有效时有一张私有 R8、接收面重绘和一次颜色合成，并可能需要 URP 深度复制；无 GPU 毫秒承诺。

## 验证与审计

[接收面叠加验收记录](../../Tests~/CapsuleAO/ReceiverOverlay/README.md) 包含最终颜色对照、无 GB/GTAO、材质 AO 关闭、公共 GTAO 纹理不变、穿地 Cross、倾斜地板、静态合批、前景拒绝及多相机用例。

原首版 [HoAO 附加规划](../计划/Ho-CapsuleAO-HoAO-Plan.md) 和 [首版测试](../../Tests~/CapsuleAO/README.md) 只作历史记录，其发布依赖和材质消费约束不再适用于本项。

# Capsule AO / Cross

状态：现行。2026-11-25。Capsule AO 是角色特化内的前置 HoAO 附加项，默认关闭。

它把脚部胶囊产生的遮蔽写入**当前相机已经发布的同一张 HoAO**，由原材质的实时／混合 AO 路径消费。不新增材质属性、贴图槽、关键字或材质 Pass，不修改材质资产，不直接乘相机颜色。内部有独立计算 Shader，位于 `Resources/HoCapsuleAO.shader`；用户无需给地板更换 Shader。

## 使用

1. Renderer Data 按同事件顺序排列 **HoGeometryBuffer → HoGTAO → HoCharacterSpecialization**。三者的相关生产 Pass 均在不透明绘制前执行，角色特化原有颜色合成保持原阶段。
2. 保持 HoGTAO 总开关开启。需要原 GTAO 时正常计算；只需要脚边遮蔽时勾选 HoGTAO 的“仅发布 AO 纹理”，提供每帧清为 1 的可写载体，跳过 GTAO 追踪、Motion、滤波和历史分配。
3. 脚骨对象添加 **Rendering / Ho Capsule AO Emitter**，校准中心、轴、半径、总长度。默认轴 Z、半径 0.06、总长度 0.22，单位随 Transform 转为世界单位；不需要 CapsuleCollider。
4. 地板 MeshRenderer 对象添加 **Rendering / Ho Capsule AO Receiver**。组件没有效果参数，只登记本对象的接收表面。
5. 角色特化 Volume 中添加／展开“胶囊 AO（前置）”，启用容器覆盖与胶囊 AO，选择 Capsule 或 Cross。外观与预算统一由 Volume 控制，不在每只脚上重复设置。

地板需要已经使用现有实时／混合 AO 消费路径。材质 AO 关闭、实时权重为零或 AO Mask 屏蔽时，挂组件不会强制开启材质 AO。

Feature Inspector 的“胶囊 AO”状态可查看跳过原因、活动胶囊数和接收面数。仅开 Capsule 不要求眼睛／脸捕获或 AC 颜色合成资源。

## 模式和参数

| 项目 | 含义 |
| --- | --- |
| Capsule | 胶囊最近点球体的投影盘遮蔽近似，结合接收法线半球和有限距离衰减；不是 ZMD Log-SH 的等价实现 |
| Cross | 在世界 XZ 平面上的两道扁平软暗带交叉，沿长度从中心向两头连续渐隐，横向独立羽化；跟随脚的位置和水平轴，全局角度、旋转决定十字／X 形状 |
| 强度 | 0–1，作用于本项遮蔽；默认 0.75 |
| 影响／高度衰减距离 | 世界单位；Capsule 按离胶囊表面距离衰减，Cross 按脚底离接收点高度衰减 |
| 十字臂长／宽度／软度 | 世界单位；臂长是中心到臂端的半长，宽度是全宽 |
| Min | 默认。原可见度 G、本项可见度 C，输出 `min(G,C)`，避免与 GTAO 简单累乘加深 |
| Multiply | 输出 `G*C`；允许更强的叠加遮蔽 |
| 胶囊预算 | 默认 32，上限 64。按相机距离、实例 ID 稳定选择；组件开关只移除对应几何输入 |

多个胶囊先取最大遮蔽，十字交叉的两条暗带也取最大值。先将 Receiver 覆盖合并，再对原 AO 附加一次，重叠接收面不会在 Multiply 下反复乘暗。

Cross 是水平地面风格化图案；向上的接收法线参与衰减，脚底以上超过小容差的表面不接收，不按局部斜面重新投影成等宽十字。胶囊随 Transform 更新；非均匀缩放和父级剪切使用保守的横向半径，负缩放允许，无效几何跳过。

## 资源与支持边界

附加路径只借用发布者授权的可写可见度目标，通过固定功能 Min／Multiply 混合原地更新。它不采样该目标，不重新发布 `_HoAOTexture`／`_HoAOAvailable`，不写 GTAO 时间历史。后续 typed handle 和全局消费者读取同一资源的更新内容。

接收面只支持普通不透明 MeshRenderer + MeshFilter，包含静态合批。私有接收覆盖重绘几何，并与当前 GB 原始深度比较，拒绝被前景遮挡的地板；不复制原材质的顶点位移、复杂 alpha clip 或蒙皮。深度容差不能区分完全共面的不同物体，复杂共面层叠需要另行验收。

| 路径 | 本轮验证 |
| --- | --- |
| RenderGraph | 白色 HoAO 与正常 GTAO 均可附加；透视／正交、多相机、奇数尺寸、1×／4× MSAA |
| Compatibility | 白色 HoAO 发布 + Capsule／Cross；原有正常 GTAO 没有 Compatibility 计算实现，本项不补建它 |
| 图形 API | Windows D3D11 / D3D12，Unity 6000.3.15f1 + 当前 Ho URP 17.3 fork |
| 明确跳过 | XR、Overlay camera、非全 viewport、动态分辨率、尺寸／格式不匹配、无当前发布者或 GB |
| 未覆盖 | 其它图形 API、Player 实机构建、复杂裁剪／位移／蒙皮接收面、完整角色美术与批量性能基准 |

无有效发布者、胶囊或接收面时不创建本项覆盖纹理、不录制附加绘制。有效时增加一张私有单通道接收覆盖和两趟绘制；CPU 先做影响 bounds／视锥筛选，GPU 限制到影响区的屏幕 scissor。这里说明的是资源结构，尚无可对外承诺的 GPU 毫秒数据。

## 验证与源码

自动化覆盖白色输出合同、同一目标原地混合、接收面重叠、前景拒绝、抬脚／移动、静态合批、多相机、Volume 覆盖、实际 lilToon AO 开关与材质资产不变。最终 D3D11 与 D3D12 各 165/165 项全仓 EditMode 回归通过，其中本项及 GTAO 为各 31 项；复现命令和原始报告见 [Capsule AO 验证记录](../../Tests~/CapsuleAO/README.md)。

- [组件与调度](../../Runtime/CharacterSpecialization/CapsuleAO/HoCapsuleAOPass.cs)
- [内部 Shader](../../Runtime/CharacterSpecialization/Resources/HoCapsuleAO.shader)
- [HoGTAO 发布模式](../Ho-GTAO.md)
- [设计与审计边界](../计划/Ho-CapsuleAO-HoAO-Plan.md)

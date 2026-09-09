# Ho-GTAO / HTrace 对齐工作表

> 用途：记录 Ho-GTAO 与本机 HTrace GTAO 的逐阶段对齐状态、证据和下一步。每次改动先更新对应行，再做画面对照，避免把算法、输入、去噪和显示曲线混在一起判断。

## 固定对照条件

| 项目 | 固定值 |
|---|---|
| 对照场景 | `D:\\Unity_Project\\BREAK_URP\\Assets\\mmd场景测试\\朱木古堂\\New Scene` |
| HTrace 源码 | `D:\\Unity_Project\\BREAK_URP\\Assets\\HTraceAO` |
| Ho 源码 | `D:\\Unity_Fork\\lilToon-URP-Extensions\\Runtime\\GTAO` |
| 追踪模式 | 基线 A：Visibility Bitmasks (`GTAOTracingMode=1`)；基线 B：HorizonSearch (`GTAOTracingMode=0`) |
| 去噪 | SpatioTemporal |
| 分辨率 | Full |
| 密度基线 | Slice 4 / Step 32 |
| 历史上限 | 12 帧（HTrace 内部为 `g_HTemporalSamplecountAO * 2`） |
| 空间滤波 | Box，按 HTrace 步长 `4 -> 2 -> 1` |
| 深度输入 | Ho-GeometryBuffer raw depth attachment；不使用 URP `CameraDepthTexture` |
| AO Debug | 两边都先用 `pow=1` 看原始灰阶，再恢复 HTrace 对照指数 |

### 当前工程资产快照

| 资产 | 实际字段快照 | 结论 |
|---|---|---|
| HTrace `Global Volume Profile.asset`（本轮已更新） | GTAO 相关字段全部 `m_OverrideState=1`；GTAO、Full、Bitmask、World=5、Screen=64、Thickness=.2、Slice4/Step32、SpatioTemporal、12 帧、Rejection=.8、Linear、Box3、Double Sample | 作为 Ho-GTAO 最高质量对照基线；Intensity=3.06、DirectLightingOcclusion=1 保持场景观感基线 |
| 工程 `DefaultVolumeProfile.asset` | `TracingMode=1`、Full、Slice4、Step32、TemporalRejection=0.8、Box2 | 可作为 Bitmask 高质量基线 |
| Ho `Global Volume Profile.asset` | High、Full、World=3.46、Screen=15、Thickness=.202、Slice4/Step32、12 帧、Rejection=1、Box3 | 仍是独立 Ho 参数；对比时先固定同值，再单独看算法差异 |

| 对照顺序 | 规则 |
|---|---|
| 第一轮 | 把 `TracingMode`、`World/Screen Radius`、`Thickness`、`Slice/Step`、`TemporalRejection`、滤波类型/趟数全部复制为同值，比较 Generate/Temporal/Spatial |
| 第二轮 | 使用本表记录的双方最高质量配置，比较最终观感；不要把配置差异归因于算法 |

> 必须区分两种比较：**同算法对比**把 HTrace 也锁为 Bitmasks，用来验证 Ho 的数值实现；**画面观感对比**保留 HTrace 默认 HorizonSearch，用来判断连续积分是否是方向感差异的来源。不能把两条基线的截图混成一个“质量档”结论。

## 状态定义

| 状态 | 含义 |
|---|---|
| `已对齐` | 结构、单位、格式和顺序均与 HTrace 一致，并有 Frame Debugger/画面证据 |
| `部分对齐` | 主路径存在，但仍有明确的采样、拒绝、格式或输出差异 |
| `仅 Ho 实现` | Ho 有实现，但不能称为 HTrace 等价 |
| `未开始` | 尚未实现 |
| `不适用` | 当前 GeometryBuffer/公共 AO 契约明确不迁移 |

## 阶段矩阵

| ID | 阶段 | HTrace 证据 | Ho 当前实现 | 状态 | 当前表现/风险 | 下一步 |
|---|---|---|---|---|---|---|
| G0 | 几何法线/覆盖 | `HMain.hlsl`、HTrace GBuffer | `HoGeometryBuffer.normalDepthTexture`，真实法线 + coverage | `部分对齐` | 法线语义一致；GeometryBuffer 与 GTAO 同事件时需保持列表顺序 | 固定 GeometryBuffer 为唯一几何真值 |
| G1 | raw depth | `HDepthPyramidAO.compute:53-125` | `HoGeometryBuffer.depthTexture` 直接进入 Ho 金字塔 | `已对齐` | 已消除 fp16 线性深度反解造成的远处波纹；天空由 coverage 屏蔽 | 保留 raw depth，不回退 alpha 反解 |
| G2 | depth pyramid | `HDepthPyramidAO.compute`，raw max 归约 | 4 张 RDG mip 纹理，2x2 max | `部分对齐` | mip 数据正确，但当前是独立纹理 + 手动 LOD blend，非 HTrace 单纹理 `SAMPLE_LOD` | 先用 Frame Debugger 对比 mip/LOD；必要时改为 point nearest LOD |
| T0 | 时域重投影 | `HTemporalFilterGTAO.compute:64-295` | 单个 Ho Temporal pass 内四 tap history，保存 previous view 与 depth-to-view 参数 | `部分对齐` | 四 tap、屏内有效性和 previous-view 重建已有；仍缺独立 reprojected data 与 render-scale 补偿 | 保存 previous scale/VP，拆出 HTrace reprojected data |
| T1 | 深度拒绝 | HTrace view-alignment + linear depth threshold | raw depth 线性化 + view-alignment + pixel-spread 阈值 | `部分对齐` | 阈值公式已对齐；仍缺 HTrace 的 motion-mask relax | 引入 motion mask/delta 后补齐动态物体分支 |
| T2 | 平面/法线拒绝 | HTrace `PLANE_DISOCCLUSION` + normal threshold 0.5 | 每个 history tap 使用 previous-view plane + normal reject | `部分对齐` | previous-camera plane 已接入；previous ZBuffer 参数和 motion relax 仍缺 | 保存 previous projection/ZBuffer 参数，补动态物体 relax |
| T3 | motion/命中速度 | HTrace motion mask/delta + per-hit velocity | Ho 在 GeometryBuffer 深度上生产对象 Motion Mask/Delta，并在命中处比较对象位移方向/幅度；仍保留 URP 相机 MV | `部分对齐` | HTrace 的独立 motion 语义已接入；尚未用 Frame Debugger 验证 skinned/刚体两类对象的数值 | 验证 moving hit 的符号深度与命中速度，确认静止表面不被误标 |
| T4 | history accumulation | HTrace 12 帧 + 5x5 clamp | Ho 12 帧 + `AO/velocity/count` history + velocity-driven clamp；加入 object depth-delta 与无 delta 放宽 | `部分对齐` | Temporal 拒绝链已覆盖对象运动；仍是单 pass 合并实现，未拆出 HTrace 独立 reprojected buffer | 只测静止/相机旋转/移动物体三种收敛曲线，不调整质量参数 |
| R0 | Bitmask tracing | `HRenderGTAO.compute:134-304` | `HoGTAOCompute` 32-bin bitmask | `部分对齐` | 已修正 signed horizon cosine 与 HTrace 的差异；仍需数值样本确认 | 对比 Generate 原始 AO，再验证接地感/方向性 |
| R0H | HorizonSearch tracing | `HRenderGTAO.compute:243-275` | Ho 暂未启用连续 arc 分支 | `未开始` | 若 HTrace 画面对比用默认 HorizonSearch，Bitmask 量化会被误判为 Ho 算法错误 | 先做 HTrace mode A/B；必要时把连续 arc 作为 Ho 可选后备 |
| R1 | horizon/切片方向 | HTrace rotations/noise `HRenderGTAO.compute:160-220` | 4 slices + interleaved gradient noise | `部分对齐` | 方向感重，可能来自切片量化、LOD 选择或 Box 轴向结构 | A/B：固定 frame/noise，分别禁用 bitmask、禁用 spatial |
| S0 | Box spatial | `HSpatialFilterGTAO.compute:101-151` | 3 趟 `4/2/1`，plane/normal 权重 | `部分对齐` | 步长已对齐；需确认 final visibility 极性和 raw depth history 使用 | 对比每一趟输出，确认最后一趟才转换 visibility |
| S1 | plane weighting | HTrace `PlaneWeighting` | Ho 使用等价指数，但当前自行重建位置 | `部分对齐` | 平面边缘/远处仍可能有不一致 | 用同一 raw depth + 同一 view-space plane 做数值对照 |
| O0 | 输出语义 | HTrace AO 0..1 visibility | `_HoAOTexture` 0..1 visibility | `已对齐` | Debug 可见度指数会放大对比，不代表 producer 数值 | producer/debug 分离验证，pow=1 优先 |
| D0 | GTAO Debug | `HDebugAO.compute` AO 输出 | Ho feature-local debug pass | `部分对齐` | 能输出，但展示曲线和天空策略需保持可比 | 增加 raw AO/visibility 选项，记录 pow |
| D1 | Temporal Debug | HTrace sample count × velocity | Ho accepted/rejected + age；Motion 模式显示 HTrace 组合语义 | `部分对齐` | SceneView 不再消费不稳定的 URP 原生 MV；Motion 可直接确认对象 mask/delta | 用 Frame Debugger 和 Motion 模式共同重验 velocity |
| P0 | 公共材质接收 | HTrace BeforeOpaque / `_HTraceBufferAO` | Ho BeforeOpaque / `_HoAOTexture` | `部分对齐` | 受 Renderer Feature 列表顺序约束 | 保持 GeometryBuffer 在 Ho-GTAO 前；Frame Debugger 固定验收 |

## 问题归因记录

### 方向感偏重

优先怀疑顺序：

1. 当前基线是否把 HTrace 和 Ho 锁在同一个 tracing mode；
2. Bitmask 的 32-bin 角度量化与 4 slice 组合；
3. Box 的 cardinal/diagonal 轴向结构；
4. depth pyramid 的 LOD 选择/独立 mip 手动混合；
5. Debug pow 放大了原本连续输出中的低幅差异。

验证时必须先看 `Generate` 原始 AO，再看 Temporal，再看每一趟 Spatial；不要只看最终 Debug。

### 接地感不足

优先怀疑：

- temporal 平面拒绝缺失导致接触阴影被历史平均冲淡；
- 空间滤波使用了不完全一致的深度/视空间位置；
- HTrace 的当前 profile 与 Ho 的 world/screen radius、thickness 没有逐项固定；
- 最终显示指数只改变对比度，不会真正增加接触遮挡。

### 收敛/稳定性不足

当前最明确的差异是 Ho 缺少 HTrace 的：

- 独立 Temporal Reprojection 数据；
- motion mask / motion delta；
- 每个 hit 的 velocity rejection；
- view-alignment depth threshold；
- plane disocclusion；
- render-scale / previous camera matrix 补偿。

这些项应先于继续调整 Slice、Step、Box 趟数。

## A/B 验证协议

1. 固定相机、灯光、材质和 Volume；分别只启用 HTrace 或 Ho-GTAO。
2. `pow=1` 截图：`Generate`、`Temporal`、`Spatial pass 0/1/2`、最终 AO。
3. 静止 1/4/12/24 帧分别截图，记录噪声方差与接触区域灰阶。
4. 只旋转相机 1 次，观察 Temporal Disocclusion、拖影和恢复帧数。
5. 只移动一个遮挡物，验证旧阴影是否被拒绝、静止区域是否继续收敛。
6. 同一像素记录 HTrace/Ho 的 raw depth、linear depth、horizon、bitmask count、history count。
7. 只有单项通过后才改下一项；质量档参数不作为算法差异的替代解释。

## 变更记录

| 日期 | 变更 | 证据 | 结论 |
|---|---|---|---|
| 2026-09-09 | Ho 改为直接消费 GeometryBuffer raw depth，history 改 R32 raw | `df01fad`；Unity GTAO 编译通过 | 解决远处深度量化波纹；需继续验证时域/空间链 |
| 2026-09-09 | 建立本工作表 | 本文件 | 后续按阶段矩阵推进 |
| 2026-09-09 | 修正退化 slice 处理 | `HoGTAO.shader`：不再直接丢弃 projected normal 过小的 slice | 消除极端视角下的方向性归一化偏置 |
| 2026-09-09 | 对齐 Temporal 拒绝 | `HoGTAO.shader`：view-alignment、plane、normal、屏内 tap 有效性 | 减少跨平面历史串入，下一步补 motion velocity |
| 2026-09-09 | 接入 HTrace 风格命中速度 | `HoGTAO.shader`：origin/hit motion divergence，history `AO/velocity/count`，velocity-driven clamp | 收敛链路开始具备运动自适应；GeometryBuffer motion mask/delta 仍待接入 |
| 2026-09-09 | 对齐 previous-view rejection | `HoGTAORendererFeature.cs` 保存上一帧 view/depth-to-view；`HoGTAO.shader` 做 previous-view plane | 相机旋转时历史深度比较更接近 HTrace；render-scale/previous-Z 参数仍登记 |
| 2026-09-09 | 修正 Bitmask horizon 积分 | `HoGTAO.shader` 保留 signed horizon cosine（仅 clamp 到 [-1,1]），并恢复 HTrace 的 reciprocal screen-radius min step | 消除 `saturate` 折叠负半球造成的相机方向偏置；下一轮只验证算法观感，不调参数 |
| 2026-09-09 | 接入对象 Motion Mask/Delta | `HoGTAOMotion.shader` + `HoGTAORendererFeature.RecordObjectMotionPasses`；以 GeometryBuffer depth 作 Equal 深度测试 | 补齐 HTrace 对象运动语义，Temporal 使用 signed depth delta，命中速度使用方向/幅度拒绝 |
| 2026-09-09 | 移除额外整像素 normal vote | `HoGTAO.shader` Temporal 只保留逐 tap normal reject | 避免 silhouette 上重复拒绝历史，缩短静止收敛时间并与 HTrace 宏路径一致 |
| 2026-09-09 | 稳定 SceneView 时域输入与调试显示 | SceneView Temporal 禁用不稳定的 URP 原生 MV；Motion/Temporal debug 改为 HTrace 的组合语义 | 静止编辑器视图不应因旧 MV 每帧流动；Motion 黑屏与 Temporal 常态流动问题转为可诊断输出 |
| 2026-09-09 | 复用 lilToon MotionVectors pass | Motion Mask renderer list 不再覆盖自定义材质，仅 Motion Delta 使用 Ho override | 保留 lilToon 自身写入的运动向量，避免自定义 mask pass 清成纯黑 |
| 2026-09-09 | 修正 Motion/Temporal debug 颜色语义 | Debug shader 仅对 AO/Off 取 R 灰阶，保留 Motion/Temporal 的 RGB | Temporal 拒绝红色不再被显示层变成纯白，Motion 组合颜色可直接观察 |
| 2026-09-09 | 按 Camera 隔离 GTAO history | Renderer Feature 为每个 camera 实例持有独立 history，并在 history 内保存尺寸/分辨率配置 | SceneView/GameView 交替渲染不再反复清空 history，Temporal 不应再长期停留在全红首帧 |
| 2026-09-09 | 分离 Temporal debug 输出与 history 写入 | Temporal 增加独立 debug attachment；诊断颜色不再污染 AO/normal history | 红色拒绝只表示诊断结果，不会让下一帧法线/深度验证永久失败 |

## 当前下一步

1. 用 `pow=1` 对比静止场景的 Generate/Temporal/Spatial 三层，确认 signed horizon 修正后的原始 AO。
2. 用 Frame Debugger 检查 `Ho-GTAO Object Motion Mask/Delta` 是否只在移动对象写入，并观察 Temporal 是否稳定收敛。
3. 只在 motion 链通过后继续处理剩余的历史重投影差异；不再用质量参数替代算法验证。

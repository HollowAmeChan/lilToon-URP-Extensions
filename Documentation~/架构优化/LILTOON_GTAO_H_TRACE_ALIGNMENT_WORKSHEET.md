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
| T0 | 时域重投影 | `HTemporalFilterGTAO.compute:64-295` | 单个 Ho Temporal pass 内四 tap history | `部分对齐` | 有四 tap，但缺少 reprojected AO/velocity 独立阶段和 render-scale/相机矩阵补偿 | 拆出 HTrace 风格 depth/plane/normal 有效性与 reprojected data |
| T1 | 深度拒绝 | HTrace view-alignment + linear depth threshold | raw depth 线性化 + view-alignment + pixel-spread 阈值 | `部分对齐` | 阈值公式已对齐；仍缺 HTrace 的 motion-mask relax | 引入 motion mask/delta 后补齐动态物体分支 |
| T2 | 平面/法线拒绝 | HTrace `PLANE_DISOCCLUSION` + normal threshold 0.5 | 每个 history tap 做 view-space plane + normal reject | `部分对齐` | 静态边缘已更稳定；previous-camera plane 仍是当前视空间近似 | 保存 previous view/matrix，替换为 HTrace previous-plane 公式 |
| T3 | motion/命中速度 | HTrace motion mask/delta + per-hit velocity | 仅消费 URP `_MotionVectorTexture` | `仅 Ho 实现` | 相机/物体运动时无法区分 origin 与 hit，收敛和 disocclusion 不稳定 | Geometry/Metadata 提供 motion mask/delta，或明确记录降级语义 |
| T4 | history accumulation | HTrace 12 帧 + 5x5 clamp | Ho 12 帧 + 5x5 clamp | `部分对齐` | 累积公式接近，但输入拒绝和 sample count 语义尚未完全一致 | 对齐 `TemporalWeight`、velocity 混合和 count 更新 |
| R0 | Bitmask tracing | `HRenderGTAO.compute:134-304` | `HoGTAOCompute` 32-bin bitmask | `部分对齐` | 公式、厚度、衰减和整数 LOD 已对齐；仍需数值样本确认 | 与 HTrace 逐行做数值样本对照 |
| R0H | HorizonSearch tracing | `HRenderGTAO.compute:243-275` | Ho 暂未启用连续 arc 分支 | `未开始` | 若 HTrace 画面对比用默认 HorizonSearch，Bitmask 量化会被误判为 Ho 算法错误 | 先做 HTrace mode A/B；必要时把连续 arc 作为 Ho 可选后备 |
| R1 | horizon/切片方向 | HTrace rotations/noise `HRenderGTAO.compute:160-220` | 4 slices + interleaved gradient noise | `部分对齐` | 方向感重，可能来自切片量化、LOD 选择或 Box 轴向结构 | A/B：固定 frame/noise，分别禁用 bitmask、禁用 spatial |
| S0 | Box spatial | `HSpatialFilterGTAO.compute:101-151` | 3 趟 `4/2/1`，plane/normal 权重 | `部分对齐` | 步长已对齐；需确认 final visibility 极性和 raw depth history 使用 | 对比每一趟输出，确认最后一趟才转换 visibility |
| S1 | plane weighting | HTrace `PlaneWeighting` | Ho 使用等价指数，但当前自行重建位置 | `部分对齐` | 平面边缘/远处仍可能有不一致 | 用同一 raw depth + 同一 view-space plane 做数值对照 |
| O0 | 输出语义 | HTrace AO 0..1 visibility | `_HoAOTexture` 0..1 visibility | `已对齐` | Debug 可见度指数会放大对比，不代表 producer 数值 | producer/debug 分离验证，pow=1 优先 |
| D0 | GTAO Debug | `HDebugAO.compute` AO 输出 | Ho feature-local debug pass | `部分对齐` | 能输出，但展示曲线和天空策略需保持可比 | 增加 raw AO/visibility 选项，记录 pow |
| D1 | Temporal Debug | HTrace sample count × velocity | Ho accepted/rejected + age | `部分对齐` | 能看红色拒绝，但不代表 HTrace velocity 语义 | 在 UI 标明“Ho fallback/validated”并补 velocity 后重验 |
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

## 当前下一步

1. 先用 `pow=1` 对比 Generate/Temporal/Spatial 三层，确认方向性来自追踪还是滤波。
2. 补齐 HTrace motion mask/delta、hit velocity 和 previous-camera plane 语义，优先解决收敛/拖影。
3. 做 HTrace HorizonSearch/Bitmask 与整数 LOD A/B，再调接地感曲线。

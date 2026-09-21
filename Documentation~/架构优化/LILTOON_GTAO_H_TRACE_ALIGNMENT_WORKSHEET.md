# Ho-GTAO / HTrace 对齐工作表：结论与坑

> 状态：**已收敛（2026 文档审核）**。本文原为逐阶段推进的工作表，现压缩为「对齐口径 + 已知差异 + 坑 + A/B 协议」。
> Ho-GTAO 已落地并默认接在 GB 之后、opaque 之前（`Runtime/GTAO/`）；HTrace GTAO 只作对照参考（`Assets/HTraceAO`）。

## 1. 对齐口径（不能混的比较）

| 项目 | 固定值 |
| --- | --- |
| 对照场景 | `D:\Unity_Project\BREAK_URP\Assets\mmd场景测试\朱木古堂\New Scene` |
| 追踪模式 | **基线 A**：Visibility Bitmasks（`GTAOTracingMode=1`）；**基线 B**：HorizonSearch（`=0`） |
| 去噪 / 分辨率 | SpatioTemporal / Full |
| 密度 | Slice 4 / Step 32（空间滤波 Box，步长 `4 → 2 → 1`） |
| 历史上限 | 12 帧 |
| 深度输入 | **GeometryBuffer raw depth attachment**，不使用 URP `CameraDepthTexture` |
| Debug | 两边都先 `pow = 1` 看原始灰阶，再恢复 HTrace 对照指数 |

> **必须区分两种比较**：**同算法对比**把 HTrace 也锁为 Bitmasks，用来验证 Ho 的数值实现；**画面观感对比**保留 HTrace 默认 HorizonSearch，用来判断“连续积分是否带来方向感差异”。两条基线的截图不能混成一个“质量档”结论。
> 第一轮把 `TracingMode`、`World/Screen Radius`、`Thickness`、`Slice/Step`、`TemporalRejection`、滤波类型/趟数**全部复制为同值**，再比 Generate/Temporal/Spatial；第二轮才比双方最高质量配置。**不要把配置差异归因于算法。**

## 2. 结论

- **已对齐**：raw depth 输入（消除 fp16 线性深度反解造成的远处波纹；天空由 coverage 屏蔽）、输出语义（`_HoAOTexture` = 0..1 visibility，与 HTrace 同）。
- **部分对齐（结构一致、仍有差异）**：几何法线/覆盖、depth pyramid（Ho 是 4 张 RDG mip + 手动 LOD blend，非 HTrace 单纹理 `SAMPLE_LOD`）、时域重投影、深度/平面/法线拒绝、命中速度、history accumulation、Bitmask tracing、horizon/切片方向、Box spatial、plane weighting、Debug 显示。
- **明确未做**：**HorizonSearch 的连续 arc 分支**。Ho 只有 Bitmask 路径——所以拿 HTrace 默认 mode 直接对比，会把 **Bitmask 的 32-bin 角度量化**误判成 Ho 的算法错误。
- **不需要迁移的**：HTrace 自带的几何/身份语义；几何真值统一由 GB 提供（GTAO 与 GB 同事件时靠 Renderer Feature 列表顺序保证 GB 在前）。

## 3. 已知差异的归因顺序

**方向感偏重**（按怀疑优先级）：① 两边是否真的锁在同一 tracing mode；② Bitmask 32-bin 角度量化 × 4 slice；③ Box 的 cardinal/diagonal 轴向结构；④ depth pyramid 的 LOD 选择/手动混合；⑤ Debug pow 放大了原本连续输出里的低幅差异。
→ 必须先看 `Generate` 原始 AO，再看 Temporal，再看每一趟 Spatial，**不要只看最终 Debug**。

**接地感不足**：temporal 平面拒绝缺失导致接触阴影被历史冲淡；空间滤波用了不完全一致的深度/视空间位置；两边 world/screen radius 与 thickness 没有逐项固定；**显示指数只改对比度，不会真正增加接触遮挡**。

**收敛/稳定性不足**：Ho 相对 HTrace 仍缺**独立 Temporal Reprojection 数据**、motion mask/delta 的完整分支、每 hit 的 velocity rejection、view-alignment depth threshold、plane disocclusion、render-scale / previous camera matrix 补偿。这些项**应先于**继续调 Slice / Step / Box 趟数。

## 4. 踩过的坑

1. **Bitmask 的 horizon 积分必须保留 signed cosine**（只 clamp 到 `[-1,1]`，不要 `saturate`）：`saturate` 会折叠负半球，造成**随相机方向变化的亮度偏置**。同时要恢复 HTrace 的 reciprocal screen-radius min step。
2. **不要丢弃 projected normal 过小的 slice**：直接丢弃会在极端视角下产生方向性归一化偏置。“退化 slice”要参与归一化。
3. **深度必须用 raw depth**：用 alpha 通道反解线性深度会在远处产生量化波纹，且会把天空算进来（天空应被 coverage 屏蔽）。history 也存 R32 raw。
4. **每个 camera 要有独立 history**，并在 history 内保存尺寸/分辨率配置；否则 SceneView 与 GameView 交替渲染会互相清空 history，表现为“Temporal 永远停在全红首帧”。
5. **Temporal 的 debug 输出必须与 history 写入分离**（独立 attachment）：否则诊断用的红色会污染 AO/normal history，让下一帧的法线/深度验证永久失败。
6. **Motion Mask 的 renderer list 不要覆盖自定义材质**，只让 Motion Delta 用 Ho override：否则会把 lilToon 自己写入的运动向量清成纯黑。Motion Delta 以 GB depth 做 `Equal` 深度测试来定身份。
7. **SceneView 需要自己的 camera motion producer**：URP 原生 MV 在 SceneView 不稳定，会让静止画面每帧“流动”；且静止时零向量显示黑是 HTrace 的**预期**行为，要移动镜头/物体才能验证 producer。
8. **Debug 的颜色语义要分档**：仅对 AO/Off 取 R 灰阶，Motion/Temporal 保留 RGB——否则 Temporal 拒绝的红色会被显示层变成纯白。
9. **移除多余的整像素 normal vote**：Temporal 只保留逐 tap normal reject，避免轮廓上重复拒绝历史、缩短静止收敛时间，并与 HTrace 的宏路径一致。
10. **质量参数不能替代算法验证**：每轮只改一项、只测三种收敛曲线（静止 / 相机旋转 / 移动物体），单项通过再改下一项。

## 5. A/B 验证协议

1. 固定相机、灯光、材质和 Volume，分别只启用 HTrace 或 Ho-GTAO。
2. `pow = 1` 截图：`Generate` → `Temporal` → `Spatial pass 0/1/2` → 最终 AO。
3. 静止 1/4/12/24 帧分别截图，记录噪声方差与接触区域灰阶。
4. 只旋转相机一次：观察 Temporal Disocclusion、拖影与恢复帧数。
5. 只移动一个遮挡物：验证旧阴影被拒绝、静止区域继续收敛。
6. 同一像素记录双方 raw depth、linear depth、horizon、bitmask count、history count。
7. 用 Frame Debugger 检查 `Ho-GTAO Camera Motion`、`Ho-GTAO Object Motion Mask/Delta`：静止相机应为零 MV，移动镜头只改变 camera pass，移动物体只改变对象 pass。

## 6. 关键文件

| 责任 | 文件 |
| --- | --- |
| Ho 侧实现 | `Runtime/GTAO/HoGTAO.shader`、`HoGTAORendererFeature.cs`、`HoGTAOMotion.shader`、`Shaders/Debug/HoGTAODebug.shader` |
| 深度/几何输入 | `Runtime/GeometryBuffer/`（raw depth attachment；由 Ho-GTAO 直接消费） |
| HTrace 对照源码 | `Assets/HTraceAO`（`HRenderGTAO.compute`、`HDepthPyramidAO.compute`、`HTemporalFilterGTAO.compute`、`HSpatialFilterGTAO.compute`、`HDebugAO.compute`） |
| MSAA 轮廓白线的机理与修法 | `LILTOON_GTAO_MSAA_SILHOUETTE_INVESTIGATION_LOG.md` |
| 参数含义对照 | `LILTOON_HTRACE_GTAO_QUALITY_REFERENCE.md` |

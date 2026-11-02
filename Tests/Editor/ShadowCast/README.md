# HoRP 阴影接入回归

2026-11-02，Unity 6000.3.15f1 / D3D12 / RTX 4060，在隔离工程 `D:/HoBakeAudit26` 通过 **13/13**（0 skipped）。不打开、保存或切换 BREAK_URP 的工作场景。

- 六项 CPU 用例：无阴影灯不参与但颜色/强度不变；零强度/Baked 排除；Realtime/Mixed Hard/Soft 保留；显式 Main 声明不绕过资格；Lighting 与 Shadow Layers 分开；每灯 Culling Mask 和空遮罩保留。
- 一项材质用例：全部包含多光源属性的生成 lilToon 变体必须提供 `_HoRPShadowMode` 且默认 Main。该项覆盖生成模板，不是只 grep 临时生成文件。
- 六项 GPU 用例：普通 Hard/Soft 灯只进入 Full；显式 Main Hard/Soft 灯也进入 Main；回读浮点渲染目标断言遮挡可见度，未使用黑帧或截图文件存在性作为通过条件。深度 1 为现有图集的空值哨兵，模拟遮挡应选有效深度。

测试筛选：`lilToon.URP.Extensions.Tests.HoShadowCastLightEligibilityTests;lilToon.URP.Extensions.Tests.HoRPShadowModeTests`。GPU 用例需真实图形设备，`-nographics` 会跳过，不能计为 GPU 通过。

包测试需由测试工程显式发现。此次仅在物理复制的隔离包里给已有 ScreenProcess 测试程序集加未定义的审计约束以排除其 Unity 6.3 兼容模式编译问题；生产包、主工程 manifest、全局 scripting defines 均未因此修改。也可以仅将本目录的测试复制到独立测试工程 Assets 中，避免启用其他包测试。

固定机位补充证据在本机 `Temp~/LightingBakeAudit-20261102/HoRPShadowMode/Verified1/main.png` 与 `Verified2/main.png`，同一画面 83 个材质槽。覆盖必须在 `HoMmdStageCapture.Begin` 的 configure 回调内施加，因为 capture 会重新加载 Scene。原材质未写入。

这些结果验证本轮阴影路由与每灯资格，不证明 URP 原生 additional atlas / Shadowmask 混合或未来 RT Full 池已被接管，也不是 HoShadowCast 退役的性能结论。

## 2026-11-03：注册 Shader 耗时回归

BREAK_URP 启动约十分钟停留在 Shader 注册阶段。进程采样确认编译器持续使用 CPU、编译结果持续返回，并非死锁。新增材质模式在分支两侧分别内联 Main / Full 的完整 PCSS 调用图，既放大编译开销，也触发 `X4000: use of potentially uninitialized variable (HoShadowCastAttenuation)`。

修复只计算一个 `mainOnly` 条件并传入同一套采样调用图，保留 Main / Full、缺失值回退 Main 和既有灯光 / 采样预算。没有增加材质 keyword，也没有降低采样数。测试 Shader 使用 `include_with_pragmas` 正确载入采样文件的声明，消除测试入口原有的 unknown pragma 警告；这不等于已验证所有生产调用端的容量变体接入。

本机独立 D3DCompile 对照：`ps_5_0`、flags=0、Low 容量、reversed Z、动态像素位置与材质模式；同一编译器各运行三次，表中耗时为平均值。Unity 专用的 `multi_compile` pragma 在这个独立 HLSL 夹具中移除，未修改采样逻辑。

| 版本 | 编译耗时 | 字节码 | 阴影分支警告 |
| --- | ---: | ---: | --- |
| 路由加入前（676a90e3） | 2.29 s | 68,188 bytes | 无 |
| 双路径路由（7ec633e1） | 8.83 s | 133,696 bytes | X4000 |
| 单路径修复 | 2.61 s | 68,864 bytes | 无 |

此夹具耗时下降约 70%，字节码缩小约 48%；不是整工程启动或 GPU 帧时间的测量，也不承诺修改共用 include 后无需重新编译。原始夹具与 JSON 在本机忽略目录 `Temp~/ShaderCompileAudit-20261103/`。

同一隔离工程 Unity 6000.3.15f1 / D3D12 的更新回归为 **17/17，0 failed、0 skipped**：六项 CPU、一个生成属性用例、十项 GPU 回读。新增覆盖缺失 / 零模式、Full 接收已声明 Main 的灯，并在回读后断言测试 Shader 无警告与错误。正式 sampler 与验证副本仅相差 metadata 说明注释。报告为本机 `Temp~/ShaderCompileAudit-20261103/unity-tests-verified.xml`。

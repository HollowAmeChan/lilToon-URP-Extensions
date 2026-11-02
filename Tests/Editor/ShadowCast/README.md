# HoRP 阴影接入回归

2026-11-02，Unity 6000.3.15f1 / D3D12 / RTX 4060，在隔离工程 `D:/HoBakeAudit26` 通过 **13/13**（0 skipped）。不打开、保存或切换 BREAK_URP 的工作场景。

- 六项 CPU 用例：无阴影灯不参与但颜色/强度不变；零强度/Baked 排除；Realtime/Mixed Hard/Soft 保留；显式 Main 声明不绕过资格；Lighting 与 Shadow Layers 分开；每灯 Culling Mask 和空遮罩保留。
- 一项材质用例：全部包含多光源属性的生成 lilToon 变体必须提供 `_HoRPShadowMode` 且默认 Main。该项覆盖生成模板，不是只 grep 临时生成文件。
- 六项 GPU 用例：普通 Hard/Soft 灯只进入 Full；显式 Main Hard/Soft 灯也进入 Main；回读浮点渲染目标断言遮挡可见度，未使用黑帧或截图文件存在性作为通过条件。深度 1 为现有图集的空值哨兵，模拟遮挡应选有效深度。

测试筛选：`lilToon.URP.Extensions.Tests.HoShadowCastLightEligibilityTests;lilToon.URP.Extensions.Tests.HoRPShadowModeTests`。GPU 用例需真实图形设备，`-nographics` 会跳过，不能计为 GPU 通过。

包测试需由测试工程显式发现。此次仅在物理复制的隔离包里给已有 ScreenProcess 测试程序集加未定义的审计约束以排除其 Unity 6.3 兼容模式编译问题；生产包、主工程 manifest、全局 scripting defines 均未因此修改。也可以仅将本目录的测试复制到独立测试工程 Assets 中，避免启用其他包测试。

固定机位补充证据在本机 `Temp~/LightingBakeAudit-20261102/HoRPShadowMode/Verified1/main.png` 与 `Verified2/main.png`，同一画面 83 个材质槽。覆盖必须在 `HoMmdStageCapture.Begin` 的 configure 回调内施加，因为 capture 会重新加载 Scene。原材质未写入。

这些结果验证本轮阴影路由与每灯资格，不证明 URP 原生 additional atlas / Shadowmask 混合或未来 RT Full 池已被接管，也不是 HoShadowCast 退役的性能结论。

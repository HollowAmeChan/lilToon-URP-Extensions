# 托管 RT Shadow 请求与屏幕候选

夹具在 `HoShadowsRendererFeature` 下检查 RT Shadow 的开关，同时让另一个独立客户端正常向 Ho-RT 请求并获取它自己的 View。覆盖默认关闭、Volume 开但总开关关、无 receiver、有效 opt-in、Volume 关但总开关开、恢复和关闭后退休。每个阴影关闭用例只有 companion 的请求和 View，且跨用例保持同一 Scene Lifetime。

在 opt-in 帧读回 DXR Raw 中心像素，检查当前 owner 和有效状态。还检查对应主灯的 `LightId`、正在使用的 SurfaceBuffer `SourceDepth`、捕获的 `LightShadowStrength` 和 dispatch serial；托管 producer 始终不发布旧主灯全局别名。

候选探针排在 Ho Shadows 后，仍在 `BeforeRenderingOpaques`。它读取实际全局候选纹理与槽位，核对完整 owner、阴影强度、未声明接收者、深度不符和关闭后的中性值。另用同一场景的 Point 和 Spot 读取各自 DXR Raw，验证 P2 光源索引映射到各自的候选槽。

## 重放

将同目录的 `.cs` 和 `.shader` 复制到独立 Unity `6000.3.15f1` 工程的 `Assets/Editor`。工程需要当前 HoURP 17.3、lilToon URP Extensions、HoShader Environment URP 与有效 Render Graph。创建新的输出目录并设置 `HO_SHADOWS_MANAGED_RT_OUTPUT`。

```powershell
unity run $project --editor-version 6000.3.15f1 --timeout 300 --no-tail `
  --log-file "$env:HO_SHADOWS_MANAGED_RT_OUTPUT/unity.log" -- `
  -force-d3d12 --burst-disable-compilation -executeMethod HoManagedRTShadowProbe.Run
```

在支持 native 和 inline DXR 的设备上运行。成功时日志写 `HO_SHADOWS_P4A_MANAGED_OK`，输出 `managed-rt-shadow-report.json`。

P4a 用 NVIDIA GeForce RTX 4060、D3D12 通过 7 个用例。独立客户端在全部帧保持相同的 Scene Lifetime。关闭和无 receiver 时共有 1 个 Host 请求与 1 个 View，均属于独立客户端。两次 opt-in 各增加 1 个 Shadow 请求、View、输出和有效产品，Raw 中心状态为 `1`、owner 为 `6144`，产品引用同一帧的 SurfaceBuffer 深度并捕获了 `0.63` 的灯光阴影强度。

P4b 在同一设备通过 9 个用例。主光 Raw 中心为遮挡、状态 `1`、owner `6145`，屏幕候选读回 `(0.369873, 1, 24, 1)`。未声明接收者的 Raw 未请求。仅进入相机深度的前景下，Raw 仍为状态 `1`、owner `6145`，屏幕候选为 `(1, 0, 0, 0)`。关闭后即使保留上帧编码槽位，实际候选仍为中性值。三灯对照中，Point 的 URP 索引 `1` 对应 P2 槽 `1` 和 RT 编码槽 `3`，读回无遮挡 `1`。Spot 的 URP 索引 `0` 对应 P2 槽 `0` 和 RT 编码槽 `2`，读回 `0.219971`，对应其 Raw 遮挡和 `0.78` 阴影强度。

Ho Shadows 改为直接持有 RT 生产器后，同一设备的 9 个 D3D12 用例均通过。只有启用且有接收者的四个用例登记了以统一 Feature 为 owner 的请求。关闭、Volume 禁用或无接收者时，Host 仍只有独立客户端的一个请求。主方向光候选仍为 `(0.369873, 1, 24, 1)`，前景深度不匹配和关闭时返回中性值。

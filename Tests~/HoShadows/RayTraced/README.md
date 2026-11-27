# P4a 托管 RT Shadow 请求与生命周期

夹具在 `HoShadowsRendererFeature` 下检查 RT Shadow 的开关，同时让另一个独立客户端正常向 Ho-RT 请求并获取它自己的 View。覆盖默认关闭、Volume 开但总开关关、无 receiver、有效 opt-in、Volume 关但总开关开、恢复和关闭后退休。每个阴影关闭用例只有 companion 的请求和 View，且跨用例保持同一 Scene Lifetime。

在 opt-in 帧读回 DXR Raw 中心像素，检查当前 owner 和有效状态。还检查对应主灯的 `LightId`、正在使用的 SurfaceBuffer `SourceDepth`、捕获的 `LightShadowStrength` 和 dispatch serial；托管 producer 始终不发布旧主灯全局别名。

## 重放

将 `HoManagedRTShadowProbe.cs` 复制到独立 Unity `6000.3.15f1` 工程的 `Assets/Editor`。工程需要当前 HoURP 17.3、lilToon URP Extensions、HoShader Environment URP 与有效 Render Graph。创建新的输出目录并设置 `HO_SHADOWS_MANAGED_RT_OUTPUT`。

```powershell
unity run $project --editor-version 6000.3.15f1 --timeout 300 --no-tail `
  --log-file "$env:HO_SHADOWS_MANAGED_RT_OUTPUT/unity.log" -- `
  -force-d3d12 --burst-disable-compilation -executeMethod HoManagedRTShadowProbe.Run
```

在支持 native 和 inline DXR 的设备上运行。成功时日志写 `HO_SHADOWS_P4A_MANAGED_OK`，输出 `managed-rt-shadow-report.json`。

实际用 NVIDIA GeForce RTX 4060、D3D12 通过 7 个用例。独立客户端在全部帧保持相同的 Scene Lifetime。关闭和无 receiver 时共有 1 个 Host 请求与 1 个 View，均属于独立客户端。两次 opt-in 各增加 1 个 Shadow 请求、View、输出和有效产品，Raw 中心状态为 `1`、owner 为 `6145`，产品引用同一帧的 SurfaceBuffer 深度并捕获了 `0.63` 的灯光阴影强度。

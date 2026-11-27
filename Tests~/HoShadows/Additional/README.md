# P2b 原生逐灯屏幕阴影

夹具在没有主方向光的场景中创建 Point、Spot 和两个只投影的遮挡物。它用当前 Render Graph 的同一份表面深度重建世界位置，逐像素比较各灯的 Ho 屏幕切片和 Unity `AdditionalLightRealtimeShadow`。比较的是应用灯的阴影强度后的实时可见度。距离淡出与 baked ShadowMask 的混合由材质接入阶段验证。

一次运行会分别创建 Forward 和 Forward+ Renderer。每个 Renderer 都检查两盏灯各自的遮挡区域、原生 atlas 的灯索引和分配切片、身份到屏幕切片的往返映射、PCF 与 Spot 阴影强度、相机切换。关闭排在前面的灯后，夹具要求另一盏灯换到新的 shader index 和 slot 并保有自己的遮挡。另检查关闭灯投影、关闭 Feature 和恢复后的资源状态。原生 Point 的面掩码如有缺失，屏幕产品不得发布该灯的切片。

## 重放

将同目录的 `.cs` 和 `.shader` 复制到独立 Unity `6000.3.15f1` 工程的 `Assets/Editor`。工程需要当前 HoURP 17.3、lilToon URP Extensions 和 Render Graph。为每次运行选择新的输出目录并设定 `HO_SHADOWS_ADDITIONAL_OUTPUT`。

```powershell
unity run $project --editor-version 6000.3.15f1 --timeout 300 --no-tail `
  --log-file "$env:HO_SHADOWS_ADDITIONAL_OUTPUT/unity.log" -- `
  -force-d3d11 --burst-disable-compilation -executeMethod HoAdditionalShadowProbe.Run
```

换用 `-force-d3d12` 可复跑另一图形 API。需要图形设备。输出目录中的 `additional-shadow-report.json` 记录每灯的索引、切片、遮挡与误差，日志中的 `HO_SHADOWS_P2B_OK` 表示两种 Renderer 均通过。

## 实测

Unity `6000.3.15f1` 在 D3D11 和 D3D12 下各通过 12 个用例。Forward 和 Forward+ 的硬投影与原生比较误差为 0，软 Spot 全图最大绝对误差为 `0.000439`。两灯各自的遮挡中心为 0、对方遮挡区为 1。软 Spot 的阴影强度设为 `0.65` 时，其遮挡中心为 `0.35`。关闭原先排在前面的 Spot 后，两种 Renderer 的 Point 均从 shader index 和屏幕 slot `1:1` 切到 `0:0`。

实测 Point 的立方体六面掩码为 `0x3f`。夹具设有面不全时的排除断言，这组真实场景未触发该断言。

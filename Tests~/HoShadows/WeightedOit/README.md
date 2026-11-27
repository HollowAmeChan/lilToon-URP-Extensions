# Weighted OIT 分层阴影接线回归

夹具运行实际 `WeightedOITRendererFeature`、`HoShadowsRendererFeature` 和 Cloth `HoOIT` pass。程序创建两片透明接收面、原生硬光源和只投影的遮挡体，读回实际 OIT 合成后的相机颜色。接收材质启用 `_HO_SHADOWS_SCREEN_PREVIEW` 和第一档的投影权重。

| 主方向光用例 | 结果 |
| --- | --- |
| 两片注册接收面 | 阴影区 RGB 约为 `(0.002625, 0.002218, 0.001907)`，亮区约为 `(0.616884, 0.513830, 0.434495)`。 |
| 反转两片接收面的渲染队列 | 同一后端的整帧 RGB 最大差为 0。 |
| 两片接收面的 owner 清零 | 阴影探针恢复为亮区同值，亮区不变。 |
| 恢复 owner 后关闭 `WeightedOitShadowsEnabled` | 原阴影探针保持中性，未继承上一帧的分层结果。 |

Full 模式用例关闭主方向灯，分别启用原生 Point 和 Spot。两种光源的注册遮挡探针 R 均约为 `0.009`，owner 清零后均恢复到 `2.171`。Point 和 Spot 各自的亮区 R 保持 `2.048` 和 `1.937`。Point 的六面阴影由测试使用的 URP 资源自动适配到 `1024×1024` 逐灯 atlas。

八个用例在 NVIDIA GeForce RTX 4060 上用 D3D11、D3D12 验证，八对 PNG 文件哈希一致。线性 EXR 文件哈希随 API 变化。视图为全视口、全分辨率、单采样正交 Base Camera 和默认 stencil。Eye 同相机、非 canonical OIT 配置、多个逐灯同时投影和其他相机类型超出夹具范围。

## 复跑

将 `HoWeightedOitIntegrationProbe.cs` 复制到独立 Unity `6000.3.15f1` 工程的 `Assets/Editor`。安装当前 HoURP 17.3、Extensions 和 HoShader `Cloth_URP`，使用 RenderGraph 和线性色彩空间。输出目录每次运行应新建。

```powershell
$env:HO_OIT_INTEGRATION_OUTPUT = 'D:/Temp/HoWeightedOitIntegration/d3d11'
New-Item -ItemType Directory -Path $env:HO_OIT_INTEGRATION_OUTPUT | Out-Null
unity run $project --editor-version 6000.3.15f1 --timeout 300 --no-tail --no-accelerator `
  --log-file "$env:HO_OIT_INTEGRATION_OUTPUT/unity.log" -- `
  -force-d3d11 --burst-disable-compilation -executeMethod HoWeightedOitIntegrationProbe.Run
```

D3D12 使用另一个结果目录和 `-force-d3d12`。通过时日志包含 `HO_OIT_INTEGRATION_OK`，`oit-integration.json` 的 `passed` 为 `true`。每个场景保存线性 EXR、显示用 PNG 和探针数值。

## Eye 与 Weighted OIT 同相机

`HoEyeOitScopeProbe.cs` 使用同一台相机、水平方向相同的原生硬阴影和上下分布的两块透明接收面。上方走真实 Eye Forward，下方走真实 Cloth Weighted OIT。夹具分别创建 OIT 在前和 Ho Shadows 在前的 Renderer Feature 声明顺序。每种顺序依次开启两路、单独关闭 OIT 阴影、单独关闭 Eye 阴影。关闭一路时比较另一路整个半帧，随后比较两种声明顺序的整帧 RGB。

在隔离工程另外安装含透明 Eye 分层接收面的 Skin_URP，复制 `HoEyeOitScopeProbe.cs` 到 `Assets/Editor`。用相同 Unity CLI 参数，将入口换为 `-executeMethod HoEyeOitScopeProbe.Run`，并使用 `HO_EYE_OIT_SCOPE_OUTPUT` 指定新的输出目录和日志。通过时日志包含 `HO_EYE_OIT_SCOPES_OK`，`eye-oit-scopes.json` 的 `passed` 为 `true`。输出同样包含 EXR 和 PNG。

RTX 4060 上 D3D11 和 D3D12 各通过六个场景，六对 PNG 的文件哈希一致。在两个后端，交换声明顺序后每种启用状态的整帧 RGB 最大误差均为 0。关闭 OIT 阴影使下半帧遮挡探针恢复亮区值，上半帧 RGB 最大误差为 0。关闭 Eye 阴影时反向验证得到相同结论。此夹具覆盖原生主方向光、全分辨率、单采样正交 Base Camera 和默认 stencil。

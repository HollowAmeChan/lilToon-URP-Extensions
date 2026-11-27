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

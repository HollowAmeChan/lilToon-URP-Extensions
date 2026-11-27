# P1c 角色屏幕阴影接线验证

夹具在一次性 Unity 工程内创建前向 Renderer，并按 `HoCharacterShadowRendererFeature`、`HoSurfaceBufferRendererFeature`、`HoShadowsRendererFeature` 的顺序启用实装组件。接收面使用 Environment_URP 的 `HoSurfaceBuffer` pass，角色和部件 ID 来自 `HoObjectBufferGroup`。URP Lit 遮挡体启用 `ShadowsOnly`。另一张只写常规相机深度的 URP Lit 前景板提供深度不匹配样本。

读回 pass 声明对 `HoNativeMainShadowFrameData.CharacterVisibility` 的 RenderGraph 读取依赖，将原始 RGBA 写入线性浮点目标。R 是折入光源阴影强度的局部可见度，G 是局部边界混合，B 和 A 分别是整数角色组与部件 ID。无效像素应为 `(1, 0, 0, 0)`。

| 验证项 | 通过条件 |
| --- | --- |
| 遮挡和局部边界 | 同一精确 owner 出现暗部、亮部和连续 G 边缘。软阴影扩大 R 的过渡区域。改变阴影强度后暗区 R 随之变化。 |
| 深度关联 | 前景板覆盖处返回无效像素。关闭前景板后，在相同像素恢复身后的 owner。 |
| 组和部件 | 只在选定部件返回该部件的完整 ID。切换到同组另一部件及另一角色组时，旧身份被拒绝。 |
| 缺少生产者 | 分别禁用 SurfaceBuffer 和 Character Shadow 后，角色产品缺失，原生主光使用的深度仍可用。 |
| 单相机帧有效期 | 不同相机和分辨率读取各自产品。空 cull 视图和无主光投影的视图返回无效像素，随后原相机恢复相同统计。 |

## 复跑

在 Unity `6000.3.15f1` 的独立工程中接入 HoURP 17.3、本包和 HoShader `Environment_URP` 的依赖，将目录中的 `.cs` 和 `.shader` 复制到 `Assets/Editor/HoCharacterResolveP1c`。夹具需要线性色彩空间、RenderGraph 和可用的图形设备。

```powershell
$env:HO_SHADOWS_CHARACTER_RESOLVE_OUTPUT = 'D:/Temp/HoCharacterResolveProbe/d3d11'
New-Item -ItemType Directory -Path $env:HO_SHADOWS_CHARACTER_RESOLVE_OUTPUT | Out-Null
unity run $project --editor-version 6000.3.15f1 --timeout 300 --no-tail --no-accelerator `
  --log-file "$env:HO_SHADOWS_CHARACTER_RESOLVE_OUTPUT/unity.log" -- `
  -force-d3d11 --burst-disable-compilation -executeMethod HoCharacterResolveProbe.Run
```

D3D12 复跑时改用新的输出目录和 `-force-d3d12`。通过时日志包含 `HO_SHADOWS_P1C_OK`，`character-resolve-report.json` 的 `passed` 为 `true`。每个样本记录纹理可用性、目标格式、像素统计和完整 RGBA 见证值，并保存对应线性 EXR。EXR 中的 B 和 A 存储原始整数身份值，查看图像时需要读取数值。

在同一台测试机上，D3D11 与 D3D12 的 12 份 EXR 对应文件哈希一致。主相机有 19,812 个匹配像素。关闭前景板后增加 494 个匹配像素，切换为软阴影后出现 592 个过渡像素。切换部件 `31:0` 和角色组 `77:0` 分别得到 8,094 和 8,378 个匹配像素。

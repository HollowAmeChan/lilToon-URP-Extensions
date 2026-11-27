# P1a 原生 Main 屏幕解析

`HoShadowsRendererFeature` 的首个产品为 `HoNativeMainShadowFrameData`。它在 `BeforeRenderingOpaques` 读取当前相机深度、URP 主光 atlas 和已经装载的原生阴影常量，将 realtime 可见度写入 `R16_SFloat`。结果包含灯光的阴影强度。距离淡出、ShadowMask 混合与角色局部阴影由后续步骤接入。

接入范围为 RenderGraph 下的单采样全视口 Base 相机，支持 Game、Scene View 和 Reflection 相机类型。缺少主方向灯、主光投影或有效输入时，帧数据为空。该夹具通过相机帧内句柄读回原生主光产品。

## 复跑 GPU 对照

在独立 Unity 6000.3.15f1 工程接入 HoURP 17.3 和本包，将本目录的 `.cs` 和 `.shader` 复制到工程的 `Assets/Editor`，设置 `HO_SHADOWS_NATIVE_MAIN_OUTPUT` 为新建空目录。需要图形设备。

```powershell
unity run $project --editor-version 6000.3.15f1 --timeout 220 --no-tail `
  --log-file "$env:HO_SHADOWS_NATIVE_MAIN_OUTPUT/unity.log" -- `
  -force-d3d11 --burst-disable-compilation -executeMethod HoNativeMainShadowProbe.Run
```

将图形参数换为 `-force-d3d12` 后用新的输出目录重跑。以 `HO_SHADOWS_P1A_OK` 和 `native-main-report.json` 的 `passed` 为准。夹具在同一相机中同时记录 URP 的 Screen Space Shadows 和本次输出，另关闭内置 Feature 验证 Ho 解析自身独立工作。

| 对照 | GPU 覆盖 |
| --- | --- |
| 单级联硬阴影和软阴影 | 同时覆盖受遮挡、全亮和边缘像素。PCF 的部分遮挡像素从 691 增至 1391。 |
| 四级联 | 画面实际覆盖 3 个级联区域。与内置 `R8_UNorm` 输出在参与对比的像素上最大绝对差不超过 0.001961。 |
| 相机和无投影 | 不同尺寸的两台相机交替执行，验证关闭投影、关闭灯和恢复后帧数据按相机更新。 |
| Scene View 和 Reflection | 通过相机渲染请求分别覆盖两种类型，D3D11 和 D3D12 最大绝对差均为 0.001953。 |
| 内置 Feature 关闭 | 相同参数得到相同的全亮、受遮挡和过渡像素数。 |

独立解析原型另在原版 URP 17.3 的 D3D11 下通过对照。完整 Extensions 继续依赖 HoURP，直接安装到原版 URP 会在既有 `ImageProcessRendererFeature` 缺少 `IHoPostProcessingProvider` 时停止编译。

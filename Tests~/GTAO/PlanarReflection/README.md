# GTAO / PLR 跨相机回归

## 问题与修复

PLR 用 `UniversalRenderPipeline.RenderSingleCamera` 捕获 Reflection 相机。该接口不触发相机开始 / 结束回调，GTAO 的常规全局状态重置因此不会执行；同时 GTAO 与 GeometryBuffer 不为 Reflection 相机生产输入。修复前，反射材质会把外层相机的 `_HoAOTexture` 当成自己的屏幕空间 AO，可能导致反射发黑。

`HoAOStandaloneCameraScope` 在捕获前保存 AO 纹理、有效标记和 Compatibility 发布记录，绑定无遮挡的中性状态；结束时恢复外层状态。CPU 全局值与 GPU 命令流均显式绑定，`using` 保证异常路径也执行恢复。没有增加反射视角的 GTAO 计算。

## 夹具范围

`PLRAOValidation.cs` 创建临时 URP 管线、相机、方块和平面，调用**生产 PLR 捕获路径**，读取生产反射 RT 的像素。`AOConsumer.shader` 按 lilToon 的公共实时 AO 契约读取全局纹理，是简单的契约消费者，不是完整 lilToon 材质。

- 在外层相机注入黑色、0.25 灰色、白色 AO，分别开关 oblique 裁剪，共 6 个捕获用例。
- 修复前应逐项复现反射峰值 0 / 0.25 / 1；修复后均应为 1。
- 反射结束后，主相机仍应读到自己注入的 0 / 0.25，验证 GPU 状态恢复。
- 外层 AO 不可用时保留中性回退。
- 主相机开启 GTAO 的“仅发布 AO 纹理”模式时，新的白色输出仍能抵达不透明材质。

只在隔离的 `Temp~` 批处理项目运行，使用内存对象，不保存场景或生产资产。这里检查 RenderGraph 路径；不据此声称已验证完整角色场景、所有原生 lilToon 变体、独立反射 GTAO、Compatibility 渲染、XR 或性能。Compatibility 发布记录的保存 / 恢复在生产代码中实现，未单独运行 Compatibility 图形路径。

## 2026-11-25 结果

Unity 6000.3.15f1，URP 17.3.0：

| 版本 | API | 反射峰值（外层 AO 为 0 / 0.25 / 1） | 检查 |
| --- | --- | --- | --- |
| 修复前 `995742bb` | D3D12 | 0 / 0.25 / 1 | 18，通过预期缺陷复现 |
| 修复后 | D3D12 | 1 / 1 / 1 | 18，通过 |
| 修复后 | D3D11 | 1 / 1 / 1 | 18，通过 |

两种裁剪设置结果相同。过滤后的结果日志、输入源文件 SHA-256 和示例输出保存在 [Results-20261125](Results-20261125)。源文件指纹同时核对了工作区与隔离工程实际使用的文件。

## 重跑

使用带有本包的隔离 URP 项目，例如仓库下的 `Temp~/DebugTileCompile7`。将夹具复制到该项目的 `Assets/Editor`：

```powershell
$project = Join-Path (Get-Location) 'Temp~/DebugTileCompile7'
Copy-Item -LiteralPath 'Tests~/GTAO/PlanarReflection/PLRAOValidation.cs' -Destination (Join-Path $project 'Assets/Editor/PLRAOValidation.cs')
Copy-Item -LiteralPath 'Tests~/GTAO/PlanarReflection/AOConsumer.shader' -Destination (Join-Path $project 'Assets/Editor/PLRAOConsumer.shader')
unity run $project --editor-path 'C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe' --timeout 180 --log-file (Join-Path $project 'Temp/PLRAO-d3d12.log') --no-tail -- -force-d3d12 -job-worker-count 2 -executeMethod PLRAOValidation.Run -hoCsPerf
```

换成 `-force-d3d11` 检查另一图形 API。`-hoCsPerf` 仅用来阻止该既有隔离工程的旧编译检查入口提前退出，不是测试逻辑的依赖。图片输出在隔离项目的 `TestResults/HoPLRAOValidation/<API>`；不写入每次启动会被 Unity 清理的 `Temp` 目录。

若要复现修复前结果，只在隔离工程中用 `995742bb` 的 `HoPlanarReflectionSurface.cs` 替换修复版，并额外传入 `-plrAOBaseline`；其余夹具保持一致。不要用基线模式验收修复版本，因为基线明确要求反射继承错误的 AO。

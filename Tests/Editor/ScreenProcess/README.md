# ScreenProcess GPU 回归

这些 EditMode 测试直接运行出货 shader 与 GB/AC/SP feature。颜色、coverage 和高度雾解析值是独立的预期结果，测试不复制 shader 的算法。

在安装本包、URP 和 Unity Test Framework 的测试项目中，将本包加入 `Packages/manifest.json` 的 `testables`：

```json
"testables": ["jp.lilxyzw.liltoon.urp.extensions"]
```

使用 Unity Test Runner 的 EditMode 页运行 `ScreenProcessRegressionTests`，或调用 Editor：

```powershell
& $unityEditor -batchmode -projectPath $testProject -runTests -testPlatform editmode -testFilter lilToon.URP.Extensions.Tests.ScreenProcessRegressionTests -testResults $resultXml -logFile $logFile -force-d3d11
```

D3D12 将末尾参数换为 `-force-d3d12`。不要添加 `-nographics`；不能运行 GPU 的环境应视为未验证。测试用临时预览场景和隔离 layer，结束后恢复 pipeline、shader 编译模式和 active scene。

需要改描边深度、coverage、AC 转接、景深 gather 或 SP 资源绑定时，必须运行这组回归。至少覆盖两个图形 API 和 RG/Compatibility 转接；仅 shader 编译成功、数学模型 PASS 或 mask debug 正常都不足以验证颜色渗色。

合焦描边用红色独立标记，纯背景中的红色峰值不得超过 `0.0005`（0.05%），同时验证描边本身保持合焦。失焦描边必须实际变模糊，防止“禁用描边模糊”伪修复。显式 coverage=0 和缺失输入也必须覆盖，防止深度有效性或旧全局纹理绕过 coverage。

回归还覆盖 AC 的视觉身份/语义继承和物理域隔离，native lilToon 轮廓、重叠角色、语义贴图、相机移动与 stencil 复制。时间残留检查允许当前 outline/OB AA 的窄边缘，但不允许历史落在当前轮廓边缘外。

owner 清理的独立负对照：仅在隔离包副本中将 GB owner attachment 的 `AccessFlags.Write` 改回 `WriteAll`。D3D12 的移动测试应失败；该负对照曾在第二帧检测到 341 个历史像素。检查两个 API、保留 negative-control 结果，不能只测试静止相机或 writer 存在的位置。

验证测试有效性的负对照：仅在隔离测试项目的包副本中换回修复前的 DoF / DepthFog shader，运行同一测试。合焦描边污染和高度雾应失败；随后恢复当前包。不要在用户打开的场景或主工作区替换生产 shader 做负对照。

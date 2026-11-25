# GTAO Volume 关闭与中性回退

## 证据与边界

单个普通相机直接通过 Volume 关闭时，旧版夹具中的相机内部像素已经是白色中性值。确认的缺陷是：**有 AO 产出的相机结束后，RenderGraph 将全局纹理解绑为黑色，但 `_HoAOAvailable` 仍为 1**；相机外或其它独立绘制因此会读到有效的全黑 AO。另一个确认的缺陷是 Volume 关闭没有使该相机的时间历史失效。

修复在相机开始 / 结束都恢复系统白纹理和有效标记 0，CPU 与上下文命令流一起绑定。Volume 关闭仍跳过 GTAO 追踪和可写纹理发布，不创建全分辨率白色 RT，同时使旧历史失效。PLR 不触发相机回调，其独立相机作用域继续负责捕获期间的隔离与外层状态恢复。

## 用例

`HoAODisableValidation` 用真实场景全局 Volume、生产 GTAO / GeometryBuffer Feature 和 `RenderPipeline.StandardRequest` 更新、解析 Volume 栈。`SingleCameraRequest` 不更新 Volume 栈，不能用来验证这项覆盖。

`HoAODisableProbe.shader` 同时输出 `(原始 AO 纹理值, 有效标记, 按标记回退后的可见度)`。在不透明材质内部，以及渲染请求结束后的独立 GPU 绘制中分别读回；中性值必须为 `(1, 0, 1)`。材质是公共契约探针，不是完整原生 lilToon 材质。

14 个连续渲染用例包括：

- 新建 Volume 即覆盖关闭、开启白色发布、开启后关闭、重复关闭。
- 取消启用 Override 后读取 Feature 回退、Feature 设置关闭、Volume 覆盖开启 / 关闭。
- 正常 GTAO 缺少几何输入、补齐 GeometryBuffer 后实际追踪。
- 实际追踪后通过 Volume 关闭、确认历史失效、重新开启。
- Renderer Feature 取消激活，以及恢复白色发布。

2026-11-25，Unity 6000.3.15f1 / URP 17.3.0：D3D12、D3D11 各 14 个用例、70 项检查通过。修复前专项使用 `995742bb` 的 GTAO Feature，保留“图结束后黑纹理 + 有效标记 1”和关闭后历史仍有效的预期缺陷复现。记录与源文件指纹见 [Results-20261125](Results-20261125)。

这里验证状态生命周期、覆盖与历史失效，不验证 AO 质量、性能、XR、完整用户角色场景或所有原生材质变体。正常追踪用例启用了生产 GeometryBuffer 和时间累积；Compatibility 图形路径未单独运行。

## 重跑

只在包含本包的隔离项目（仓库 `Temp~` 下）批处理运行。将 `.cs` / `.shader` 夹具放到 `Assets/Editor`，执行：

```powershell
unity run '<repo>/Temp~/DebugTileCompile7' --editor-path 'C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe' --timeout 180 --log-file '<repo>/Temp~/AODisable-d3d12.log' --no-tail -- -force-d3d12 -job-worker-count 2 -executeMethod HoAODisableValidation.Run -hoCsPerf
```

将图形参数换成 `-force-d3d11` 检查另一 API。`-hoCsPerf` 用于阻止既有隔离工程的旧自动检查入口提前退出；不是夹具本身的依赖。基线复现只在隔离工程中替换为旧 GTAO Feature 并增加 `-aoDisableBaseline`；该模式要求保留已确认的错误行为，不用于验收修复版本。

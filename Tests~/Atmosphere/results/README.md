# Bundled atmosphere density presets

2026-10-20，Unity 6000.3.15f1，HoURP 17.3.0，当前本地包，隔离 GPU 工程。

入口：`lilToon.URP.Extensions.VolumeLight.HoVolumeLightValidation.RunDensityPresets`，分别使用 `-force-d3d11` / `-force-d3d12`，不用 `-nographics`。日志位于本地隔离工程 `.codex-research/RTInputValidation`：`atmosphere-density-presets-d3d11.log`、`atmosphere-density-presets-final-d3d12.log`。

CSV 为实际加载包内 `.asset` 后的结果：64³/R8_UNorm、无 mip、线性过滤/XYZ Repeat；检查原生 CPU 数据的范围/均值、每张纹理三个体素中心的 GPU 密度，以及 XYZ 各一对循环边界的 GPU 连续性。另断言预设多对象赋值、只修改纹理引用、Undo/Redo 均通过。不是只检查 PNG 预览。

`source-stats.json` 来自确定性 Python 生成源，便于与烘焙资产的分布比较；PNG 预览为 XY 中央切片，不代表最终光束外观。

生成源：`Tests~/Atmosphere/generate-density-presets.py <输出目录>`。维护时将 `.raw` 放到隔离 Unity 工程的 `AtmospherePresetSource/`，调用 `lilToon.URP.Extensions.Editor.Atmosphere.HoAtmosphereDensityPresetBake.Run`，产物在 `Assets/GeneratedHoAtmosphereDensity`。发布到包内时保留既有资产 GUID；不要在同一 Unity 工程保留相同 GUID 的两份导入资产。正常使用直接选内置预设，无需生成。

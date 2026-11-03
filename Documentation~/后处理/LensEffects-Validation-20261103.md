# ScreenProcess 镜头效果验收（2026-11-03）

## 实现与提交

- `31ea4a57`：本次范围与公共镜头参数延期规划。
- `f16b95e2`：独立 ScreenProcess 光斑变焦、景深内可选光圈散景增强与回归。
- `f044f7b4`：旧 ImageProcess 入口收口、显式迁移、字段映射与持久化/撤销测试。
- 最终验收提交：补充空项目 VolumeManager 初始化、Inspector 行高、GPU 预览导出与本记录。

公共镜头 Volume / Camera 跟焦组件没有实施；光斑变焦保留独立配置。使用说明与字段见 `LensEffects.md`。

## 环境与范围

Unity 6000.3.15f1，NVIDIA RTX 4060，实际 D3D11 / D3D12 图形设备；没有使用 `-nographics`。测试项目启用 `URP_COMPATIBILITY_MODE`。包依赖从本机已有缓存解析，镜头代码与耦合的 lilToon / shadow 输入冻结成隔离快照，不修改用户正在打开的场景。

最终测试过滤器：

```text
ScreenProcessRegressionTests;LensMigrationTests;HoPostProcessingOwnershipTests;PostProcessEnableRegressionTests;ObjectSelectionRegressionTests
```

| 测试组 | 每个 API 通过数 |
| --- | ---: |
| ScreenProcess GPU / 深度 / 遮罩 / 原生描边回归 | 74 |
| 旧镜头迁移（含撤销、资产持久化） | 4 |
| 后处理接管 | 2 |
| 主开关 | 2 |
| 对象选择组 | 6 |
| 合计 | **88** |

结果：**D3D11 88/88、D3D12 88/88，全通过，无跳过**。新增集成用例在每个 API 下覆盖：光斑变焦 / 景深散景 × RG / Compatibility × GeometryBuffer / 相机深度回退，共 8 个组合。

镜头用例验证独立跟焦与备用距离、前后景可禁用、合焦区域保护、真实径向拖影、HDR/alpha、亮部阈值和增益、暗部保护、描边 coverage 1/4、1/2、完整覆盖，以及色散时的逐颜色邻点约束。既有合焦描边背景污染上限 `0.0005` 保持。

## 负对照与恢复

仅在隔离包副本中引入两个故障：

1. 光斑变焦的失焦响应强制为 1，绕过对焦与前后景作用。
2. 景深光斑采样直接接受颜色，不检查源 CoC。

两种 API 下，同一组 **6 项全部按预期失败**：合焦颜色变化、径向拖影出现在合焦位置、合焦描边向背景泄漏（含色散）。两个负对照运行都没有 C# / shader 编译错误，失败来自像素断言。

随后按原始 SHA-256 恢复副本的两个 shader，确认内容与出货源码等价（仅换行差异），并在 D3D12 复测同组 **6/6 通过**。用户工作区的生产 shader 从未换成故障版本。

## 证据与边界

本机证据保存在忽略目录 `.codex-research/ScreenProcessLensValidation/`：

- `d3d11-final.xml` / `d3d12-final.xml` 与同名 `.log`。
- `d3d11-negative.xml` / `d3d12-negative.xml`，以及 `d3d12-restored.xml`。
- `negative-positive-hashes.json` 与原始副本备份。
- `previews/lens-effects-preview.png`：从实际 GPU 回归输出导出的对照图，HDR 仅为显示做映射，不作为数值断言的输入。

这不是对用户当前场景的艺术参数验收。仍沿用现有景深的有限采样和单趟 gather 边界，没有近/远景分层重建、隐藏背景恢复或时间历史重建；大半径与很小的高亮可能显出有限采样痕迹。后续公共镜头输入继续按规划文档单独设计。

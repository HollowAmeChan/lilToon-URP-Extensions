# 血月古堡体积光阴影范围取证

过程与复核：`Documentation~/计划/Ho-VolumeLight-血月古堡遮挡排查.md`。

- `solar.txt`：原始实时配置，DX12 SceneView。
- 四份 `.float`：256×176、row-major、little-endian float32 的物理体积贡献 R；未色调映射，不是最终合成图。
- `raw-statistics.json`：逐个读取上述原始值验证 finite/nonnegative 后的均值、峰值和 SHA256。
- `comparison.txt`：原始采集记录。只使用 mean/max；deltaMean/reductionVsBase 无效，原因见过程文档。
- `LiveShadowDistanceComparison.cs.txt`：一次性实际编辑器采集脚本；归档版已修正 baseline Clone。需放在隔离/受控工程 Editor 下，路径按运行环境调整。它临时改阴影距离与主光阴影状态，结束/重载时恢复；不是产品功能。
- `solar-validation.txt`：现有 HoVolumeLightValidation.Run 的 D3D11 完整 PASS 摘录。`LinearLightBaseline.cs.txt` 明确设置该用例要求的线性灯光强度。
- `PC_RPAsset.change.patch` / `PC_RPAsset.sha256.json`：项目资产唯一一行 50→100 的实际 diff 及前后 SHA256。原始备份位于 `.codex-research/SkyOcclusionLive/PC_RPAsset.before.asset`；Unity YAML 自带空白行尾，归档 patch 避免重复引入整份资产。项目配置修改没有混入包内代码。

没有测量或宣称性能收益。更远阴影可能增加 caster 渲染，且同一 atlas 精度分配到更大范围。100 m 是本次对照选择，150 m 仅作诊断，没有保留。

# Ho-RTDI：稳定首轮 RGB guide 实验收尾

2026-10-15，第106步。接手原聊天时实验代码尚未提交，四次 Unity 进程实际已经结束；核对原聊天记录的退出码、日志及独立 JSON 后收尾。本步不继续衍生滤波参数实验。

唯一变量是在第105步 A 的固定首轮 Atrous 颜色 guide 上，将 `abs(L(center)-L(sample))` 改为 `L(abs(centerRGB-sampleRGB))`。当前传播 variance、phi、归一化、其他权重、采样和实际输出传播保持原样。替换仅发生于隔离工程的 index10 compute；生产 Runtime 与 NRD 派生生成器没有改动。

| 检查 | 结果 | 实际进程退出码 |
| --- | --- | --- |
| 正交环境，RenderGraph | 4/4 通过 | 0 |
| 透视环境，RenderGraph | 2/4 通过，mixed 两组失败 | 1 |
| 透视环境，compatibility | 2/4 通过，数值与 RG 一致 | 1 |
| 单样本动态镜面，RenderGraph | slow 通过，rapid/step 失败 | 1 |

透视 mixed 的 filtered specular 红/蓝比值：无复用 `1.021820 / 1.050368`，时间+空间复用 `1.018655 / 1.064133`。相较第105步 A 有改善，仍没有通过原5%门槛。

动态 slow/rapid/step 的 filteredFrameL1 分别为 `0.112260 / 0.383257 / 0.402277`；后两项超过原 `0.35` 门槛，且劣于第105步 A 的 `0.344653 / 0.348361`。不能通过放宽门槛接纳此方案。

每环境组 `704 captures / 2816 latePasses`，每动态组 `448 / 1792`；日志确认首轮快照及 RGB 分支实际启用。原 compile 日志 errors=0，无 shader error/warning；本轮重新执行 Roslyn 也为0错误，固定 NRD 派生重建检查通过。

原日志为 `.codex-research/rgbguide106-RunRgbFirstGuide{Environment,Perspective,PerspectiveCompatibility,Motion}.log`。四份 JSON 位于隔离工程 `.codex-research/RTInputValidation/`：

- `rtdi-runtime78-env-baseline-stableguide104-first105-rgb106-rg.json`
- `rtdi-runtime78-env-baseline-perspective-stableguide104-first105-rgb106-{rg,compat}.json`
- `rtdi-firstguide105-rgb106-motion-rg.json`

结论：拒绝生产化，保留可复现实验入口。最新实验已收尾，**原生产 mixed 镜面问题仍未解决**。下一项改为[完成度审计](Ho-RTDI-CompletionAudit-2026-10-15.md)，不再自动追加一个新的 guide/variance/phi 变体。

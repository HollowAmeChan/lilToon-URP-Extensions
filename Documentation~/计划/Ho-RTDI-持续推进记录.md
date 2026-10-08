# Ho-RTDI 持续推进记录

开始日期：2026-10-09。用户授权持续推进 DI；每一步先评审必要性与参考，再实现、验证、复查并独立过滤提交。只提交本步自己的改动，保留其他任务的工作区内容。最终 lilToon PBR/NPR 接收/合成、通用 PT 框架不在当前范围；GI 仅做共享代码的必要兼容与回归。

## 0. 起点复查

起点提交 dd0ea39 已提供静态世界中的动态解析灯历史快照与双向索引映射，前后灯数据由实际渲染轮推进；删除/替换灯映射为 -1。重采样保持原 SDK。现有验证覆盖旧参数、GPU 双向映射、时间阶段 M/age、动态能量与遮挡，不把最终 visibility 更新后重置的 age 错当成未复用。

仍有的实际缺口：全分辨率 raw 输出有方差；局部灯候选均匀采样，灯数/功率跨度大时可能退化；无降噪、生产性能与 Player 验收；不透明刚性支持域和灯光链接限制仍在。工作重点是提升 DI 本身，不把最终合成提前混进来。

## 1. 局部灯 / 方向灯分区采样

**动手前评审：应做。** 原实现把方向灯放在局部灯的均匀池里。局部灯数量大时，方向灯被选中的概率低，既浪费候选，也影响首帧出光。这属于已有 SDK 适配缺口，可在不增加 pass/buffer/Feature 的情况下修复，收益可单独验证。

**参考核对：**

- 用户参考工程 Assets/RTXDI/Scripts/PrepareLight/LightScene.cs 的 GetLightBufferParameters 分别填写 localLightBufferRegion / infiniteLightBufferRegion。
- 同工程 Rtxdi/DI/ReSTIRDIContext.cs 默认使用 8 个 local 候选与 1 个 infinite 候选。
- 原 SDK DI/InitialSampling.hlsl 的 RTXDI_SampleLightsForSurface 已调用 RTXDI_SampleInfiniteLights 并合并估计；继续复用这段算法，不重写采样合并。
- [NVIDIA RTXDI 集成说明](https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/Integration.md) 明确区分局部与无限灯光。在线资料仅用于核对契约，实现仍使用本地已校验的 SDK 版本。

**实现：** 把当前灯数组排列成 point/spot 在前、directional 在后的连续分区，传入 local count 与 infinite offset/count。存在方向灯时独立生成一个 infinite 候选，局部候选数沿用原预算。灯跨分区换类型时继续按引用身份映射历史索引。空 local region 仍保留非零 local 参数，避免 SDK ComputeInitialSamplingMisData 的零 MIS 计数；SDK 自身跳过空 region。当前没有环境灯、BRDF 候选，因此这项处理不改变其他采样项。

**验证结果：** D3D12 RenderGraph / compatibility，Gamma / Linear 均通过；D3D11 不支持分支与 GI 两条路径回归通过。64 盏无贡献局部灯加一盏太阳、局部候选数为 1、时空复用关闭时，首帧太阳贡献正确。混合有效局部灯的统计均值 0.36409，数学参考 0.36480。还核对了方向灯改点灯导致的 GPU 索引重排、旧灯型快照和历史保留。Compute 警告/错误作为失败条件。

**完成后复查：** 改动限定在 DI 灯分区、初始采样参数、设置说明和验证。SDK 代码未改；没有增加全屏纹理、GPU buffer、pass、原生 DLL 或合成行为。收益是降低方向灯遭局部灯稀释的方差，不是消除所有噪声。多个方向灯仍从该分区抽一个候选；局部灯仍均匀采样。initialSamples 现在明确表示局部灯候选预算，方向灯候选为额外固定预算。

## 2. 下一步取舍

| 项目 | 当前判断与推进条件 |
| --- | --- |
| 局部灯功率采样 | 下一项先做可重复的方差与成本基准，比较少数强灯/大量弱灯。收益成立后，沿参考 PdfTexture / Presample 与 SDK POWER_RIS 接入；均匀采样保留为对照。功率提案不保证解决同功率但距离差很大的灯，不能当作 ReGIR 的替代品。 |
| NRD 降噪 | 实际可用性需要推进，但先核对信号输入和本地接入路线。当前 RGB 已含接收 BRDF，alpha 是有效性，不能直接冒充 NRD radiance / hit-distance 输入。优先明确解调、选中灯距离、normal/roughness/viewZ、motion 与历史状态，再决定最小实现。 |
| ReGIR | 需要世界网格与更多采样资源；先证明较小的局部提案改进仍不足，再评估引入收益。 |
| 最终材质合成 / PT 扩展 | 维持现有范围边界。 |

NRD 参考：[官方集成 README](https://github.com/NVIDIA-RTX/NRD)，以及用户参考工程 Assets/Scripts/NativePlugin/NRD 的 NrdDenoiserHelper / NrdFrameData。官方资料要求对应的辐射、命中距离与表面/运动 guides；本地参考还涉及 NRI/native ABI。后续应以可验证的输入契约和实际接入为单位推进，不能通过搬入整套原生框架或对 raw 输出简单模糊就宣称 NRD 已接好。

本记录随每步评审更新。后续步骤若改变以上优先级，应记录发现、依据和替代选择。

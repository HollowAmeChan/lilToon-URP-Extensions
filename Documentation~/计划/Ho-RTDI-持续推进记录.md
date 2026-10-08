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

## 2. 局部灯功率提案

**动手前评审：应做，先量化。** 仅分离方向灯不能解决强弱局部灯混采的问题。基准使用同位置/同颜色的一盏强灯和 63 盏弱灯，关闭时空复用、局部预算 1，以逐像素数学参考归一化，避免把几何梯度当噪声。先测得均匀均值 1.003284、方差 55.813219，证明能量正确但候选选择方差很大。

**参考核对：** 用户工程的 PdfTexture.compute、Presample.compute / PresampleLightsPass；原 SDK PresamplingFunctions.hlsl、LocalLightSelection 的 POWER_RIS 分支；[NVIDIA 的 PDF mip / RIS 集成说明](https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/Integration.md)。仅追加一个原 SDK header，29 个原文件的 SHA-256 与 include 之外正文均核对不变。

**实现：** 增加可选 Power 模式，Uniform 保持默认。GPU 写 Morton R32F power PDF，原生 box mip 后调用 RTXDI_PresampleLocalLights，复用 SDK 的逆 PDF、RIS tile 选择与原初始/时间/空间重采样。点灯使用参考功率表达式；spot 按当前平方 cone 模型积分得到提案权重。compact light data 可选项关闭，不复制另一份灯数据。固定 64×256 RIS 条目，每相机 128 KiB，另有随灯数增长的 PDF/mips；无局部灯或格式不支持时使用 Uniform。

**验证结果：** Power 归一化均值 0.999520，方差在 F6 打印精度下为 0；合并灯能量、全黑 PDF、强灯身份交换后保持历史、mip 完整性、资源切换释放、动态灯与分区、两条路径及 Gamma/Linear、D3D11 禁用和 GI 回归通过。基准时长包含 CPU、读回同步及首次使用，不是 GPU kernel benchmark；预采样确有固定开销，未据此切换默认模式或承诺生产 FPS。

**完成后复查：** padding 每轮清零，PDF/RIS 用当前灯顺序与参数；过去的提案概率已包含在 reservoir weight 内，不错误重用当前 PDF 作为过去的概率。2D PDF dispatch 避免大图超过单轴 dispatch group 上限。模式切回 Uniform 释放完整资源，保留 8 B dummy 以满足已编译 shader 的绑定。GI 编译宏保持 presampling 关闭。参考算法和原有输出含义不变。

一次 compatibility 运行在打印 PASS、完成清理后，Unity 的 WorkerManagerASIO 退出栈发生原生崩溃；旧日志保留为 rtdi-power-full-compat-d3d12-exit-crash.log。已重新执行，最终验证要求 PASS、无 Shader 诊断且进程 exit=0，未把仅有 PASS 的崩溃进程计为通过。

**限制：** 此基准中提案和目标几何因子匹配，所以降方差极明显。功率忽略距离、可见性和接收 BRDF；同功率远近灯、被遮挡强灯仍可退化。当前每轮重建 PDF/mips 并预采样，尚未做目标分辨率的 GPU budget、静态 PDF 缓存或自适应 tile 配置。

## 3. 下一步取舍

| 项目 | 当前判断与推进条件 |
| --- | --- |
| 局部灯功率采样 | 可选路径与可重复基准已交付；后续按目标场景 GPU budget 决定缓存和 tile 规模，保留 Uniform 对照。不能用此结果代表任意灯光空间分布。 |
| NRD 降噪 | 下一项优先核对信号输入和本地接入路线，再落实可验证的 DI denoiser 输入/调度。当前 RGB 已含接收 BRDF，alpha 是有效性，不能直接冒充 NRD radiance / hit-distance 输入。明确解调、选中灯距离、normal/roughness/viewZ、motion 与历史状态；不先改最终合成。 |
| ReGIR | 需要世界网格与更多采样资源；先证明较小的局部提案改进仍不足，再评估引入收益。 |
| 最终材质合成 / PT 扩展 | 维持现有范围边界。 |

NRD 参考：[官方集成 README](https://github.com/NVIDIA-RTX/NRD)，以及用户参考工程 Assets/Scripts/NativePlugin/NRD 的 NrdDenoiserHelper / NrdFrameData。官方资料要求对应的辐射、命中距离与表面/运动 guides；本地参考还涉及 NRI/native ABI。后续应以可验证的输入契约和实际接入为单位推进，不能通过搬入整套原生框架或对 raw 输出简单模糊就宣称 NRD 已接好。

本记录随每步评审更新。后续步骤若改变以上优先级，应记录发现、依据和替代选择。

# Ho-RTDI Cutout 漫反射滤波

第30步沿用[真实双灯质量审计](Ho-RTDI-MultiLightQuality.md)的独立平面、两盏非零红/蓝点灯、CPU逐像素参考和原质量门槛。古堡的合并天空球只用于资产支持审计，没有修改源场景、模型或材质。

## 对照与取舍

核对用户参考工程 `UnityPathTracing-6000.3.15/Assets/Scripts/NativePlugin/NRD/NrdSettings.cs`、固定版本 `NRDSettings.h` 和 `RELAX_Atrous/AtrousSmem`。参考默认 diffusePhiLuminance=2、5次 Atrous；亮度权重含 `1/(phi*sqrt(variance))`，减小 phi 会更强地阻止不同亮度之间的混合。[官方集成契约](https://github.com/NVIDIA-RTX/NRD/blob/master/README.md)仍用于核对输入与运动，未修改 vendor shader、派生算法或 native DLL。

隔离入口固定原 phi=2，再分别改一项：Atrous 5→1、漫反射历史→8帧、phi→.5。以下为静态无复用的 filtered 结果：

| 对照 | 红能量比例 | 蓝能量比例 | mean L1 | frame L1 |
| --- | ---: | ---: | ---: | ---: |
| 第29步原配置 | 1.143309 | 1.028107 | .129259 | .143761 |
| Atrous=1 | 1.089945 | .997401 | .061127 | .113769 |
| 历史=8 | 1.129416 | 1.037498 | .138811 | .147803 |
| phi=.5 | 1.004673 | .980782 | .057620 | .109765 |

延长历史未解决问题；减少 Atrous 仍红色偏高。phi=.5 的静态、静态TS和全透光TS三个对照都通过原门槛。诊断入口在设置覆盖前多渲染一帧，不宣称与原基线逐样本相同；最终生产对照使用原八组调度，raw 分通道比例与原配置一致。

全局收紧的候选让普通不透明降噪测试 filtered MSE 升到约 .004099，因此没有采用全局参数。最终只在 DI 已分配 Cutout 最终可见性时使用 phi=.5，无 Cutout 时保留 phi=2。每帧检查切换并更新 native settings；单灯精确路径仍直接发布 raw。该选择作用于此相机整张 diffuse 图，即使 Cutout 不在屏幕内也会使用紧配置；任意 Cutout 场景的噪声取舍仍需实测，不是逐像素分类。

Editor 专用 `ValidationSettings` 用于可复现的单项A/B，不增加序列化配置或产品开关。生产路径不使用该委托，不增加资源或滤波 pass，不做亮度补偿，不改变 specular gate、历史预算、五次 Atrous、ReSTIR、guide ABI 或最终材质合成。

## 最终八组结果

两条硬件路径一致，全部 raw 分通道能量仍在3%内。filtered 门槛保持：各通道能量5%、mean L1<.15、frame L1<.35。

| 模式 | 红能量比例 | 蓝能量比例 | mean L1 | frame L1 | 通过 |
| --- | ---: | ---: | ---: | ---: | --- |
| 静态无复用 | 1.004658 | .980818 | .057627 | .109750 | 是 |
| 相机运动TS | .993814 | .993022 | .059636 | .135050 | 是 |
| caster运动TS | .999468 | .998736 | .064818 | 1.059944 | 否 |
| 静态T | 1.000107 | .986889 | .067246 | .130610 | 是 |
| 静态S | .983325 | .996612 | .082865 | .121376 | 是 |
| 静态TS | .985093 | .990818 | .083784 | .129092 | 是 |
| 全透光无复用 | .992889 | 1.006155 | .035311 | .080640 | 是 |
| 全透光TS | .974042 | 1.028092 | .052700 | .088389 | 是 |

总体 `qualityPass=false` 保留：caster 每帧 scene revision 清空全部历史，均值正确但逐帧噪声仍超限。这一步改善静态/相机运动的特定测试，不宣称移动遮挡物已修，也没有降低正式门槛。

Cutout→无Cutout→Cutout 的实际配置/输出回归，两条路径 phi=.5/2/.5，filtered 能量比例 .997938/.999160/.989462；普通不透明 NRD raw MSE .050401、filtered MSE .000183，保留原降噪表现。完整部署检查与其他回归见持续推进记录第30步。

## 复现和下一项

`HoRTDIValidation.RunCutoutNrdAtrousOne/RunCutoutNrdHistoryEight/RunCutoutNrdPhiHalf/RunCutoutNrdOriginal` 固定三个静态控制；完整入口仍为 `RunCutoutMultiQuality` / `RunCutoutMultiAudit` 及兼容版本。当前完整 JSON/CSV 为隔离工程 `rtdi-cutout30-current-rg/compat.*`，A/B 为 `rtdi-cutout30-{atrous1,history8,phiHalf,original}.*`。历史第29步文档/日志保留失败数值；早期候选曾覆盖旧同名前缀的JSON/CSV，因此当前报告使用新前缀。

下一项先区分 ReSTIR 可见性复用失效与 NRD 主表面历史失效，核对参考的运动/梯度/confidence处理，再用移动caster和遮挡出现/消失的逐帧参考评审。不能直接取消所有世界revision重置来换低噪声；必须检验拖影与过期阴影。GPU成本仍待有效计时，现有稀疏批处理样本和超时 RenderDoc 尝试不足以给预算结论。

---
name: ho-mmd-scene-rendering
description: Build and adjust editable PMX stages in HoURP, assemble VMD performances, and render stills or videos with explicit quality presets through CLI while preserving authored lighting and Volume settings.
---

# Ho PMX 场景制作与调整

用于“新 PMX 从零搭建”“现有场景调灯、材质、滤镜”“后台多角度渲图 review”“VMD 影片分档录制”。默认工程 `D:/Unity_Project/BREAK_URP`。当前手工版本是工作起点，这是一套制作指南和动态经验库，不是固定场景验收标准。

## 置顶：影片播放倍率与调试接管

- 搭建 MMD 影片或自动化预览/录制入口时，**显式指定播放倍率**（常规为 `1×`，有特定要求则用指定值），不要继承用户调速面板上一次的残留倍率。
- 场景可配置 `HoUnityTools/播放速度/自动化倍率`（`HoPlaySpeedOverride`，`Speed` 与 `LockPanel`）；脚本入口使用 `HoPlaySpeedControl.SetOverride(this, speed, lockPanel: true)`，结束时 `Release(this)`。
- Play 后调速面板的「锁定」开启，info 必须显示请求脚本/对象与倍率。交付说明中显式指出：**要调试可手动关闭「锁定」；本次播放的自动化重复请求不会抢回，再次锁定才恢复自动化。**
- 锁定/手动接管是运行时状态，不回写场景或 Prefab；开启「播放结束自动复位」时，退出 Play 恢复进入前的面板倍率，下一次 Play 重新按自动化配置锁定。
- 直接写 `Time.timeScale` 的未知脚本无法可靠自动归因；需要可显示来源、可手动接管的入口应调用上述接口。外部逐帧 `ExternalTime` 采样仍由录制器决定，面板倍率不替代离线采样时钟。

## 工作方式

- **当前都是验证阶段。** 默认输出用验证档 `360p`、竖屏 `9:16`、`30 fps`；需要细查才显式升档。不默认输出 2160p，不称作最终渲染。脚 IK、表情仍在返工时不把一次预览当成动作验收。
- **日常渲视频也必须明确质量档、比例、帧率。** 验证 360p / 预览 720p / 精查 1080p / 高分 2160p，横屏 16:9 / 竖屏 9:16 / 4:3 / 正方形，帧率 30 / 60；根据本次用途选档并报告实际像素尺寸，不能因旧输出为高分而沿用。
- GUI 在 `HoUnityTools/快速渲染` 选档；MMD CLI 使用 `--quality`、`--aspect`、`--fps`。两者当前共用档位含义，**并未共用录制后端**；自动化不必强行调用 GUI，选择能正确推进动作、运镜、音乐与 AA 历史的入口。档位只调整输出大小，不偷偷关闭光照和滤镜。
- **新建、定点修改、只渲染分开执行。** 初始场景创建后，不为预览重跑旧配方，不把用户灯光、材质、相机和滤镜恢复成历史数值。
- 默认只处理指定场景/参数。不每次检查全体 H2CU、固定效果清单/机位数或旧哈希；历史截图不是艺术要求。遇到实际故障才调用相关诊断。
- 使用 Ho PMX importer 和魔改 Ho lilToon，抽出材质，保持 PMX 贴图相对路径。按场景整理 Source/Materials/Rendering/Renders。默认使用已有 PC_RPAsset/PC_Renderer，通过场景 Volume、灯光、材质调效果，不顺手重置共享 Feature。
- 用户禁用 SSGI；需要 GI 时使用 RTGI，可配 RELAX。其他效果按需求选择，不要求每场景开满。
- **图像效果全部优先使用 Ho。** ImageProcess 的「仅使用 Ho 后处理」默认开启；URP 内置曝光、ACES、Bloom、景深等会被执行层跳过，保留 AA / 升采样 / 显示编码。需要调色、发光、映射、颗粒等直接调整 Ho 图层，不再额外叠 URP 效果，也不为解决预览差异强行降低 Glow 阈值。新场景模板只建 Ho 滤镜。用户要求清理旧内置组件时，用 `strip-native-post` 定点清理当前场景的局部 Profile，核对 Ho 参数指纹不变，不顺手清其它场景；只有明确兼容旧链时才关闭接管。
- 重点区域用真实光源塑形，后处理负责风格。多角度比较帮助调整，不是固定配方的通过/失败审查。
- CLI 优先。打开的工程用文件队列；不启动第二个同项目 Editor，不丢弃未保存内容。Logo 等资产定点修改不需要切换或保存场景。

## 按当前任务阅读

| 任务 | 指南 |
| --- | --- |
| 资源整理、组合部件、导入/抽材质、创建起点 | [pmx-and-assets.md](references/pmx-and-assets.md) |
| 去哪里调灯、AO、反射、体积、天空、景深、调色和 Logo | [lighting-and-rendering.md](references/lighting-and-rendering.md) |
| CLI、只渲染、三机位、隐藏批处理、输出、按需排错 | [validation-and-cli.md](references/validation-and-cli.md) |
| 将角色、VMD 动作/表情/运镜/BGM 组装为影片；指定片段的 IK 排错 | [mmd-motion-review.md](references/mmd-motion-review.md) |
| 遇到具体历史问题才查 | [experience-log.md](references/experience-log.md) |

## 常用循环

1. 看作者预览/说明，确认主体、追加件、改色版与可选件。
2. 保留相对贴图目录导入并抽材质；用 HoMmdSceneWorkshop.Create 或手工创建初始层级。
3. 按空间调整主/观察机位，再做主光、环境与局部补光、材质。初始包围盒相机只是起点。
4. 按风格增加 AO、斑驳投影、局部体积、PLR、RTGI、天空与后处理；只改需要的部分。
5. 用 render --scene 拍单张，或 review --scene 拍三机位。看构图、亮暗、过曝、远景、反射和角度稳定性，继续调整。
6. 有可复用发现时补入指南/经验库。普通调色打灯不要求全工程审计或重新生成旧基线。

通用工具：Tools~/MmdStages/Unity/Editor 下的 HoMmdSceneWorkshop、HoMmdStageBranding、HoMmdStageCapture，以及 Tools~/MmdStages/stage-cli.py。旧 StageDetail/StageLighting/StageAdvanced/StageRemaining 是首次 H2CU 制作配方，只有明确要求重新应用配方时使用。

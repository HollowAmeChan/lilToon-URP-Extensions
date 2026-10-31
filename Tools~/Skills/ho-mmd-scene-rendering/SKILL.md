---
name: ho-mmd-scene-rendering
description: Import a new static PMX stage into HoURP, extract Ho lilToon materials, build an editable scene, adjust lighting and Volume filters, and render selected cameras through CLI without reapplying authoring recipes.
---

# Ho PMX 场景制作与调整

用于“新 PMX 从零搭建”“现有场景调灯、材质、滤镜”“后台多角度渲图 review”。默认工程 `D:/Unity_Project/BREAK_URP`。当前手工版本是工作起点，这是一套制作指南和动态经验库，不是固定场景验收标准。

## 工作方式

- **新建、定点修改、只渲染分开执行。** 初始场景创建后，不为预览重跑旧配方，不把用户灯光、材质、相机和滤镜恢复成历史数值。
- 默认只处理指定场景/参数。不每次检查全体 H2CU、固定效果清单/机位数或旧哈希；历史截图不是艺术要求。遇到实际故障才调用相关诊断。
- 使用 Ho PMX importer 和魔改 Ho lilToon，抽出材质，保持 PMX 贴图相对路径。按场景整理 Source/Materials/Rendering/Renders。默认使用已有 PC_RPAsset/PC_Renderer，通过场景 Volume、灯光、材质调效果，不顺手重置共享 Feature。
- 用户禁用 SSGI；需要 GI 时使用 RTGI，可配 RELAX。其他效果按需求选择，不要求每场景开满。
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

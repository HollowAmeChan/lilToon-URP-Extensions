# 已调舞台中的批量动作影片

## 场景搭建与渲染分开

同一舞台集中放在一个目录，例如 `Assets/00动画/MMD动画场景/花吐电気通用动画/`。
`Scenes/<动作>.unity` 与 `制作说明/<动作>.json` 按动作分开；舞台 Prefab、`Rendering/`、
`Materials/` 和 `Renders/` 共用。角色只引用 `Assets/00动画/角色/WTD/WTD-BUILD.prefab`。

同舞台添加动作时，优先复制一个已有动作 Scene，替换 Sequence 素材并点击“更新素材（保留适配）”。
共用灯光改舞台 Prefab，共用滤镜改共享 Profile。特殊版本复制 Scene 后做 Prefab override；
需要独立滤镜时再复制对应 Profile。演出根节点统一为 `20 演出`，让共享景深跟焦路径保持有效。

首次为新舞台创建演出起点时才使用 `compose-stage`。其 `stageScene` 可以是已调好的舞台，
也可以是已完成的影片模板。目标已有演出时拒绝重建，后续调灯和换装直接编辑 Scene。
首次组合会：

- 复制本地 `Rendering/`、`Materials/`，重连场景和 Volume 的引用。
- 保留作者已经调好的景深参数，仅更新新演出头骨的跟焦路径。
- 保留原动作的骨骼、表情、IK 和运镜适配，不重新生成映射。
- `enableParts` / `disableParts` 可以在新场景中持久化衣装开关；录制 CLI 也支持临时覆盖。
- 共享角色、环境网格、原始贴图、VMD 和现有 BGM，继续使用公共 PC 管线。

人工卡点使用 Sequence 的 **BGM 时间偏移（秒）**，正值延后、负值提前，不移动动作和镜头。
录制报告保存实际偏移，合音优先使用报告值，避免拿后来修改的组件参数去合旧视频。

## 批次渲染

`render-video-batch.py` 读取批次 JSON，串行调用已有 `video-cli.py record` 和 `mux`。
每个条目包含 `name`、`scene`、`config`、`output`、`status`；
批次明确声明 `qualityPreset`、`aspectPreset`、`width`、`height`、`fps`、
`antialiasing`、`enableParts` 和 `disableParts`。

同一清单可以混合衣装：每条影片可指定自己的 `enableParts` / `disableParts`，
省略时才沿用批次全局值。条目名和输出路径必须唯一，例如 `EX-HAPPY_泳装`、
`EX-HAPPY_巫女服`，因为中间静音视频也按条目名区分；不要让两版复用同一份中间文件。
衣装只在录制 Play 期间切换，不写回 Scene 或共享角色 Prefab。

批次会等待已有的舞台队列、播放或编译完成后再开录，不抢占正在执行的机位捕获。

```powershell
python Tools~/MmdStages/render-video-batch.py `
  --manifest "D:/Unity_Project/BREAK_URP/Assets/00动画/MMD动画场景/花吐电気通用动画/制作说明/巫女服中质量制作清单.json"
```

指定一支先检查：

```powershell
python Tools~/MmdStages/render-video-batch.py --manifest "<批次 JSON>" --only EX-HAPPY
```

完成条目再次运行会跳过；新的一轮拍摄应使用新的 `batch`、输出文件名和
`scene-ready` 状态，不覆盖既有视频。中间静音视频留在 `Library/HoMmdVideo/<batch>/`。
一次只向当前 Unity 工程发送一个录制请求；超时后先检查现有请求，不能重复提交。

每支完成后生成 MP4、四个代表时刻的抽帧图和 `.render.json`，并核对：

- 实际分辨率、帧率、完整帧数、音轨和完整解码。
- TAA 历史尺寸及推进、Ho 后处理的实际执行。
- 录制期间实际启用的 Parts，而不只检查命令行请求。

批次 JSON 是续跑记录；场景中的组件和本地渲染资产仍是美术调整的起点。
镜头与表情的质量仍需人工查看抽帧或视频，技术检查不能替代视觉判断。

## 版本控制

先提交当前工程基线，再提交搭建后的场景，最后提交视频与制作清单。
项目的 FBX、贴图、音频和视频由既有 Git LFS 规则管理；`Library` 和 `Temp~`
只保存中间文件，不进入版本控制。工程里的 UPM 本地包仍由各自仓库维护。

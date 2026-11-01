# 已调舞台中的批量动作影片

## 场景搭建与渲染分开

先为每个动作准备独立的影片目录和 `制作说明/制作配置.json`。
`compose-stage` 的 `stageScene` 可以是已调好的舞台，也可以是已完成的影片场景。
仅当目标场景尚不存在时，工具才会从影片模板中移除旧演出并转移新的已适配演出。
已有演出场景始终拒绝重建，后续调灯和换装应直接编辑它。

使用影片模板时：

- 复制本地 `Rendering/`、`Materials/`，重连场景和 Volume 的引用。
- 保留作者已经调好的景深参数，仅更新新演出头骨的跟焦路径。
- 保留原动作的骨骼、表情、IK 和运镜适配，不重新生成映射。
- `enableParts` / `disableParts` 可以在新场景中持久化衣装开关；录制 CLI 也支持临时覆盖。
- 共享角色、环境网格、原始贴图、VMD 和现有 BGM，继续使用公共 PC 管线。

## 批次渲染

`render-video-batch.py` 读取批次 JSON，串行调用已有 `video-cli.py record` 和 `mux`。
每个条目包含 `name`、`scene`、`config`、`output`、`status`；
批次明确声明 `qualityPreset`、`aspectPreset`、`width`、`height`、`fps`、
`antialiasing`、`enableParts` 和 `disableParts`。

```powershell
python Tools~/MmdStages/render-video-batch.py `
  --manifest "D:/Unity_Project/BREAK_URP/Assets/Hollow/MMD影片/花吐电気_巫女服中质量制作清单.json"
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

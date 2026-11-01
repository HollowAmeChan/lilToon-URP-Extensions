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

### 首帧衣物准备

首帧大幅抬腿等姿态可能让裙子来不及碰撞。Sequence Inspector 可设置
“首帧姿态过渡（秒）”和“首帧稳定等待（秒）”：前者从播放开始时的目标骨骼姿态平滑进入
求解后的动作首帧，后者保持首帧让物理继续稳定。过渡默认 0，保持既有场景行为；
有实际穿插时再针对该动作调节，例如 DISCOTHECUE 使用 1 秒过渡 + 1 秒等待。

MMD CLI 录制读取 Scene 的两项设置，模拟这段准备时间并从编码帧区间中跳过；
动作、表情、运镜、BGM 的正式时间仍从 0 开始，不修改 VMD 关键帧或音画偏移。
预热至少一秒，报告记录 `warmupFrames`、`poseTransitionSeconds`、`settleSeconds`。
这也用于编辑器正常 Play；直接调用 `Sample(t)` 的定点采样仍立即取样，不隐式插入过渡。

CLI 定点设置可在动作配置写 `poseTransitionSeconds`，然后调用
`video-cli.py configure-preparation --config <配置>`；只更新目标 Scene 的过渡时长，
保留原稳定等待与其他手调参数。普通换素材不会重置这项设置。

Recorder 依赖可见 Game View 的帧结束回调。CLI 使用临时独立录制视图承接捕获，
结束自动关闭，避免用户切到主窗口的 Scene 页签后挂起编码；不反复抢占主窗口焦点。

### 已有舞台增加一个动作

CLI 的 `clone-performance --config <新动作.json>` 对应“复制已有动作 Scene → 更新素材（保留适配）”：
配置指定 `sourceScene`、全新的 `scene`、`motion`、`facial`、`camera`、`audio`、
`durationSeconds`、`soundtrackTimeOffset`，以及可选衣装开关。已有目标 Scene 会被拒绝覆盖。
`update-performance` 仅针对配置指定的已有 Scene 更新这些演出素材。

两者都保留目标角色、现有参考骨架、手指校准、舞台、灯光、Volume 与材质；`sourceRig`
若与实际参考不同会报错，需要另行显式适配，不能只更换字段假装已经更新源尺寸。
配套作者 sizing PMX 可以在制作配置中作为 `authorSizingPmx` 记录，不能误报为正在使用。

拆分的脸、眼、眉等网格可能都包含同名形态键。此入口对实际使用的表情建立精确名称 /
Unicode 规范化名称的一对多本地绑定，保存在组件内；不创建额外映射资产，也不按近似语义
猜不存在的表情。`Library/HoMmdVideo/Variants/<动作>.json` 记录实际缺项和适配是否保留。

### 固定机位改竖屏后的构图跟随

角色横移或弯腰后出画时，可在演出相机的 `VMD Camera Player` 指定“构图跟随目标（可选）”，
并调“跟随权重（横向 / 纵向）”与“跟随中心偏移（右 / 上）”。目标可为头部、骨盆或自建控制点；
各轴 0 使用作者原构图、1 跟随该目标，中心偏移沿相机坐标轴，以世界米计。
不设置目标时，原镜头行为保持不变。跟随会覆盖所选轴的构图，保留旋转、景别与关键帧时序，
不是避障或全身自动取景，也不改变角色动画。

`clone-performance` / `update-performance` 可选以下字段，例如《好きすぎて滅》的竖屏构图修订：

```json
{
  "overrideFramingFollow": true,
  "framingFollow": true,
  "framingFollowBone": "Head",
  "framingFollowWeight": { "x": 1, "y": 1 },
  "framingFollowOffset": { "x": 0, "y": 0.05 }
}
```

`framingFollowBone` 使用当前角色的 Humanoid 骨名，不依赖骨骼路径。
仅横移有问题时可用 `Hips` 与 `(1, 0)`；弯腰幅度较大时应看头部/胸部构图是否仍有效。
`overrideFramingFollow` 为 false / 省略时保留已有手工设置；显式 true 且 `framingFollow` 为 false 清除目标。
默认不在所有影片中启用，避免覆盖作者有意的偏轴或扫出主体镜头。

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

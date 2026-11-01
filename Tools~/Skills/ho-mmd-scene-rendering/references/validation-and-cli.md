# CLI 制作与多角度渲染 review

默认：改需要的内容 → 渲图 → 看图 → 继续调。无需匹配旧 H2CU 配方、固定效果清单或资产哈希，不默认跑全场景审计。

## 工程打开：文件队列

同步 Tools~/MmdStages/Unity/Editor 到项目 Assets/00动画/MMD场景/_制作工具/Editor。现有队列可以触发编译：

```powershell
python Tools~/MmdStages/stage-cli.py status
python Tools~/MmdStages/stage-cli.py refresh
```

请求/结果在 Library/HoMmdStages。一次一个任务；超时只代表观察超时，先看 status/running/capture-status，不直接重复提交或启动第二个同项目 Editor。--project 可指定其他安装了队列的工程。

## 定点修改 Logo

```powershell
python Tools~/MmdStages/stage-cli.py logo --logo-y .1 --logo-size .3
python Tools~/MmdStages/stage-cli.py logo --folder "Assets/00动画/MMD场景/作者/新场景" --logo-y .1 --logo-size .3
```

只改找到的 Hollow 图层 Y/size；保留 X、颜色、贴图、启用状态与其他滤镜。不打开/重建场景，只保存目标 Volume。后续想变尺寸时显式运行，不在每次渲图时强制恢复。

## 只渲染当前效果

```powershell
python Tools~/MmdStages/stage-cli.py render --scene "Assets/00动画/MMD场景/作者/新场景/新场景.unity" --width 1280 --height 720 --warmup 16
python Tools~/MmdStages/stage-cli.py review --scene "Assets/00动画/MMD场景/作者/新场景/新场景.unity" --width 1280 --height 720 --warmup 32
python Tools~/MmdStages/stage-cli.py review --scene "Assets/00动画/MMD场景/作者/新场景/新场景.unity" --motion "Assets/动作/动作.vmd" --motion-frame 240 --width 1280 --height 720 --warmup 32
```

单张使用 MainCamera。review 使用主机位、左侧观察相机、右侧观察相机，先在 Unity 按空间摆好；starter 创建初始机位。输出默认 Library/HoMmdStages/Review/<场景>/<时间>/main.png,left.png,right.png，不覆盖正式渲图。单张 --output 传 PNG 文件，review --output 传目录。

不调用调灯配方，不改 Volume，不强制 GI/雾/滤镜，默认关闭依赖旧 H2CU 层级的 diagnostics。采用 URP StandardRequest、跨帧预热与 HDR GPU 读回，没有 Environment 组件也可以渲染普通 URP 场景。

捕获需要切场景时，先保存当前场景。工具拒绝自动保存/丢弃你的编辑，完成后恢复布局与管线；这是防止丢修改，不是审查艺术参数。活 Editor 的文件队列渲图会占用该 Editor 一段时间。真正隐藏批处理在工程关闭时运行。

review 按需求看主体照明、亮部层次、地板/远景可读性、阴影和光束落点、侧面可读性、调色/景深的角度稳定性。发现问题只修对应灯、材质或图层，不要求固定机位数作为完成条件。

场景中挂有 `HoVmdMotionPlayer` / `HoVmdCameraPlayer` 时，`--motion` 加 `--motion-frame` 会在三个观察镜头中应用同一个动作帧。可以专门检查脚底 IK、toe、表情 morph 和 VMD 镜头在侧面是否仍然成立。

动作 review 会把 VMD 的 morph 依赖对照播放器 `ModelRoot` 下的 BlendShape，并优先使用 `HoVmdBindingProfile.morphs`。JSON 写入 `Library/HoMmdStages/MorphAudit/`，其中 `exactMatchNames`、`normalizedMatchNames`、`profileMappedNames` 是可用命中，`missingNames` 是目标角色没有的形态键，`conflictNames` 是重复或失效映射。看到缺失时先补 Profile 或检查动作 README；不要为了让审查变绿而给角色资产随意加同名 BlendShape。

## 工程关闭：隐藏批处理

请求 JSON 示例：

```json
{"action":"review","scenePath":"Assets/00动画/MMD场景/作者/新场景/新场景.unity","width":1280,"height":720,"warmup":32}
```

```powershell
$editor = 'C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe'
Start-Process -FilePath $editor -WindowStyle Hidden -ArgumentList @('-batchmode','-force-d3d12','-projectPath','D:/Unity_Project/BREAK_URP','-executeMethod','HoMmdSceneWorkshop.RunBatch','-hoRequest','D:/Requests/review.json','-logFile','D:/Requests/review.log')
```

捕获不带 -quit 或 -nographics，异步任务完成后入口自行退出。D3D12 用于 RTGI/DXR。create、logo 也支持 JSON 入口。

## 可选排错

缺图/透明异常查单个 PMX report 与材质；反射异常才跑 PLR debug/on-off/mip；体积异常查对应 Source/Volume；怀疑底层依赖才看管线总览，不批量重置共享 Renderer。

HoMmdStageAudit.RunBatch、verify-detail-lighting.py、旧 source/hash manifests 与固定机位数量属于早期批量制作的可选回归工具，不再是日常完成条件。只有明确复现旧批次、查回归、验证底层改动时才选用需要的部分。

资源重排遇到 `Access denied` / `Moving file failed` 时先查文件占用；资源管理器打开的旧 `Renders/` 目录也会阻止父目录移动。保留 `.meta`，不反复触发原生重试窗口，不强退 Unity。迁移时还要确认目标实际文件存在，单独看到 GUID 已迁移不足以证明原文件移动完整；清理残留副本前只对该残留范围检查内容和引用。这些检查用于目录迁移，不作为日常调灯或渲染门槛。

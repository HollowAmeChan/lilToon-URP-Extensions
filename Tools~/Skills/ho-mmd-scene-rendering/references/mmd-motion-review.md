# MMD 动作影片与局部排错

新影片优先使用 `HoUnityTools/MMD/VMD Sequence Player`，指定主 Humanoid、动作 VMD、表情、镜头、音乐和作者 sizing PMX / 参考模型，生成一次适配。内部映射留在组件中，普通换动作使用“更新素材（保留适配）”，换角色才重新适配。参考 PMX 只提供骨架尺寸，不要求最终角色采用 PMX 或启用物理。

WTD 驱动 `Armature-Swimsuit`；`Armature-Miko` 由既有合骨架流程处理。Humanoid 的静止旋转已清零，蒙皮和原始控制变换可能在 MCH 中；骨骼数据正确不等于运行时蒙皮、辅助骨与衣物时序已经验证。

## 动作片段有问题时

- 优先取得“动作名 + 时间范围 + 部位”，先复现该窗口，必要时前后多取少量帧。VMD 默认 30 fps；8–10 秒对应 240–300 源帧，60 fps 输出还应测中间半帧。
- 同时比较源参考骨架和最终角色：源 IK 自己就未达到目标时，不要先改目标角色 hips 或脚控制点来掩盖。
- 分开看效应器位置、控制点、膝盖弯曲平面、脚掌朝向和蒙皮。源/目标比例不同时，直接比较两者脚尖坐标或静止向量会产生误报。
- 当前重定向器采用“源 toe IK 决定脚掌旋转，目标 ankle IK 修正比例”。目标 toe 求解器会关闭；闲置目标点距离大不能直接当作脚尖动画误差。若要改成目标 toe 再求解，须同时设计脚型与脚底接触约束，避免双重倾斜。
- 反复跳到同一动作帧也要得到相同姿态。源与目标采用确定姿态时，目标 IK 不应再追随另一份交互式 Pole 平滑历史。直接拖动独立 IK 控制点仍可保留平滑。
- 单轴膝盖大幅屈曲用带符号的四元数轴角检查；Unity 的等价 Euler 分解可在 X 超过 90° 后出现 Y/Z=180°，不能把这两个值钳回零后当作原来的膝角。

## 表情与音乐

使用 Sequence Inspector 的“检查实际使用的表情”，先处理有非零关键帧的缺失项；只有零权重键的占位名称不必强配。嘴、眼、眉和左右方向不能仅靠相似名称猜映射。组件可一对多映射既有 BlendShape；没有对应形态键时明确记录缺口。

确认 Sequence 的 BGM 字段与内部 AudioSource 都引用正确音频。后期给 MP4 合过音乐，不等于编辑器播放已经有音乐；离线录制与实时预览各测试一次。参考资料和许可与影片资源放在同一目录，片段/输出各自独立。

## 已适配角色放入已有舞台

使用 `HoMmdStagePerformance` / `video-cli.py compose-stage --config ...` 做一次组合。配置指定 `stageScene`、已有可播放的 `animationScene`、独立输出 `scene` / `folder`，以及可选 `performancePosition` / `performanceYaw`。舞台副本保留已制作的灯光，局部复制并重连 Rendering、Materials 与 Volume 子资产；WTD 继续保留共享 Prefab 链接，原始环境网格/贴图及 VMD/BGM 继续共享。公共 PC Pipeline/Renderer 不复制。

从原影片只转移 Sequence、共享角色实例及其演出相机/音源，不转移摄影棚地面、旧补光、旧 Volume 或多余主相机。角色、参考骨架与相机收在同一演出根节点下；不要分别修改三者坐标。组合不会重新生成适配、重置手指偏移或写回共享角色。

舞台拍静态环境用的固定焦距不能直接套到运镜角色。组合时将本地 ScreenProcess 景深改为头骨路径跟焦；路径保存在本地 Volume 中，不能将场景 Transform 直接写进资产引用。重命名演出根节点/骨骼后同步更新路径。电影画幅、Logo 按影片要求关闭；不要只按图层名称判断类型。

先运行时截短片确认衣装合骨架与最终蒙皮，再渲完整预览。编辑模式 `render-performance` 只适合检查机位与姿态，不推进衣装合并或物理，不能拿它的缺衣装画面当成最终效果。日常换动作/调光只更新现有组件与本地 Volume，不重跑组合。

## 影片 AA 与预热

当前迭代默认使用 `--quality draft --aspect portrait --fps 30`，实际输出 360×640；`preview` / `review` / `high` 对应短边 720 / 1080 / 2160。比例可选 `landscape`、`portrait`、`4:3`、`square`，帧率仅 30 / 60。`video-cli.py plan` 不进入 Unity，只打印本次尺寸；`presets` 列出组合。保留 `--width` 与 `--height` 成对提供的高级兼容接口。所有档位只改尺寸，不关闭作者的 RTGI、灯光、材质、Volume。

日常视频与单帧都按用途选质量档，并在开录前说明实际尺寸、比例和帧率；这不是只用于排错的一次性选项。验证阶段不再默认录制整段 2160p，不将当前 IK、表情或画质视作最终完成。

人工入口为 `HoUnityTools/快速渲染` 主面板的质量 / 比例 / fps 行。自动化 MMD 视频仍使用 `video-cli.py record` → 项目录制桥 → Unity Recorder → ffmpeg 合音乐；快速渲染 GUI 使用另一套捕获后端。目前只是档位语义一致，不应宣称两者录制行为已经统一。选择后端以动作时钟、运镜、音频、AA 预热和可靠清理为准，不要求 CLI 操作 GUI。指定相机的视频支持应实测，不能仅因 GUI 有相机选项就认定整段录制可用。

Scene View 的 AA 与演出 Camera 的 AA 独立。先检查实际相机，不将“没用 TAA”误报为“没有 AA”。`video-cli.py record --aa taa` 仅在本次 Play 的录制相机上启用 TAA Very High，关闭该相机的 MSAA/动态分辨率；省略 `--aa` 继承保存的相机设置，不改场景、灯光或 Volume。也可明确选择 `smaa`、`fxaa`、`none`。

Recorder 现在先建立最终输出分辨率，再通过录制帧区间跳过一秒预热，避免先按小视图预热、开录时切大尺寸而丢失历史。TAA 录制会检查实际 `TaaHistory` 的纹理尺寸与版本是否推进，旁存 `.render.json`；只有请求选项不能证明 TAA 生效。帧数仍须由成片实际流验证。

本机 Ho ScreenProcess / ImageProcess 排在 URP 后处理之后，后加效果产生的新边缘不会回到前面的 TAA；这与“不支持后处理”不是一回事。TAA 需要后处理、运动矢量、单 Base Camera 和 MSAA=1。发丝、衣袖、运镜切换要看动态拖影与细节，不能只看静帧。开场预热不等于每输出帧增加 RT 收敛采样，也不启用 Recorder 仅供 HDRP 的 Accumulation。

## 后处理与相机的定点排错

2026-11-01 花吐电気实测：演出相机已开 URP 后处理、Volume Mask 为全部，ScreenProcess 实际运行 3 层、ImageProcess 8 层。另建的 `HO测试摄像机` 虽勾选后处理，但 Mask 只有 Default，不能读到位于第 31 层的全局 Volume；将 Mask 加上该层后可读取相同效果。不要把相机勾选框、Volume Mask、Feature 安装、实际执行四件事混为一谈。

PC_Renderer 的两个旧显示名称分别仍映射正确的 ScreenProcess / ImageProcess 脚本 GUID，实际 Feature 均已安装。判断类型应读运行时 `GetType()` 或脚本 GUID，不按资产 `m_Name` 或历史 `m_EditorClassIdentifier` 判断缺失，不因此重跑 Renderer 初始化。

两组栈曾在 `IsActive()` 中忽略自身 `Enable.value`，导致总开关无法关闭现有图层，现已修复；ImageProcess 的 RenderGraph 跳过分支也会刷新诊断，避免沿用同相机上一阶段的 Ready。360×640 原生像素对照已验证：关 Image 时仅 Image 归零，关 Screen 时仅 Screen 归零，实际像素均明显改变。

`video-cli.py diagnose-render --config ...` 输出当前相机、Mask、适用 Volume 与真正 Feature 类型；`proof-post --config ... --quality draft --aspect portrait --output <目录>` 在当前已加载场景上做静态开关对照，不切场景、不推进动作、不保存临时参数，可保留用户未保存的修改。它不是运行时衣装或 IK 验证。VMD Camera Inspector 也提供显式的“启用本相机后处理并匹配场景 Volume”按钮。

相机组件禁用后不会自动执行正常相机渲染，但其它脚本仍可独立运行，显式 SubmitRenderRequest/反射渲染是另一回事。场景视图及额外启用的 Game Camera 也有开销；默认由用户决定各相机/脚本的开关，不为录制批量改变其启用状态。

### Scene View 与 Game View 的发光差异

比较滤镜前，检查 **Scene View 自己的 URP 后处理开关**，不能只看 Ho Volume 的「场景视图」。目前 Ho ScreenProcess / ImageProcess 独立判断是否在 Scene View 运行，即使 Scene View 关闭内置后处理，Ho 栈仍可能继续执行，此时缺少 URP 曝光、ACES/Neutral、Bloom，图像亮度域与 Game View 不同。Volume Inspector 会提示此状态，并提供开启当前 Scene View 完整后处理的按钮；CLI `enable-scene-post` 只修改编辑器预览开关，不保存场景。

ImageProcess「发光」在 **URP 色调映射之后**提取当前图像的亮部，不是场景 HDR Bloom。开启 ACES/Neutral 后，阈值通常应从约 0.7 开始调整；阈值 7.5 不能因为在未色调映射的预览中有效就照搬到 Game View。需要按 HDR 亮度发光时使用 URP Bloom；需要星芒等 HDR 光学效果则应另行设计映射前的效果，不把图像域参数伪装成 HDR 参数。新建发光图层默认阈值 0.7、平滑 0.5；旧图层不自动迁移，Inspector 提供提示与显式调整按钮。

本机 2026-11-01 花吐电気实测：两边同样运行 8 层 ImageProcess、读取同一 Volume，但 Scene View 内置后处理关、演出相机开，发光阈值为 7.5。游戏相机「发光开/关」平均像素差仅 0.013/255；仅在临时副本中改阈值至 0.65、平滑至 0.5 后为 17.23/255，证明图层执行了但原阈值几乎提取不到亮部。校正 Scene View 完整后处理后，旧阈值在该视图也近乎无效。不同机位不能直接当成逐像素相机一致性测试。

`proof-glow --config ... --quality draft --aspect portrait --output <新目录>` 单独比较原设置 / 发光关 / 临时低阈值；`--camera-name '$scene-view'` 使用实际 Scene View 相机。每组内部冻结同一姿态，恢复临时 Profile、阈值与相机设置，不保存作者参数。`diagnose-render` 同时报告 Scene View 与 Game Camera 的后处理开关、Mask、色调映射与发光参数；栈 Ready 或图层计数并不能证明某一层有视觉贡献。

## 只在需要时用专项审查

`HoUnityTools/Tests~/MmdMotion/VmdRetargetAudit.cs` 的 `Run` 会在 `OpenPreviewScene` 加载已保存的五支影片，记录控制器、表情与跳帧数据，不保存场景。`RunSour` 可导出既有 Sour 姿态 prefab 的同帧骨骼。Blender 独立对照脚本是同目录 `blender_sour_foot_audit.py`，需本机 mmd_tools；其 `margin=0` 仍使用 Blender 帧 `VMD帧+1`。

已有验证类可能已安装在项目内；建立临时测试宿主前先查重。编译失败后旧程序集仍可能回答文件队列，必须核对这次脚本编译成功与输出 schema，不能只凭“refresh 返回成功”认定执行的是新测试。测试资产放独立临时 Editor 目录，完成后删除本次创建的文件及 `.meta`。

数学骨架审查不推进物理；需要看 MCH/HoAux、衣物与最终画面时，再用运行时录制和指定时间窗复核。复现之后只改根因，不重跑制作配方、不覆盖用户打灯/滤镜。详细本轮证据见 `Tools~/MmdStages/MotionAudit-20261031`。

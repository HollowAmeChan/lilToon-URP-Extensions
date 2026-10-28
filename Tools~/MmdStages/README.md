# H2Cu 场景本地资源准备

`prepare-stage-sources.py` 将本机已整理的八个 H2Cu 场景包复制到 BREAK_URP 的各场景 `Source/`。保留 PMX 相对路径和作者说明，不移动源素材，不复制压缩包、Blender 中间文件或其他工程的 `.meta`。

~~~powershell
python Tools~/MmdStages/prepare-stage-sources.py
~~~

默认源目录：`E:/BaiduSyncdisk/大师/_完整场景/H2Cu阿相`。

默认目标目录：`D:/Unity_Project/BREAK_URP/Assets/mmd场景测试/H2Cu阿相`。

每场景各自保存一套源资源。脚本复制后比较 SHA256 并检查源文件长度/修改时间未变，报告写到 `Temp~/MmdStages/source-copy.json`。再次运行时同内容复用，目标存在不同内容时明确拒绝覆盖，保留手工修改。

参数 `--source-root`、`--target-root`、`--evidence` 可指定其他本地路径。此工具不分发模型与贴图，也不运行 Unity。

## 组合选择

不要把包内所有 PMX 都叠放：

| 场景 | 推荐组合 |
| --- | --- |
| 花吐电気 | H2CU式stage06·花吐电気.pmx |
| 乐园·深命 | 主体.pmx |
| 圣诞万花镜 | 主体.pmx + 吊饰AL+.pmx + 圣诞树&礼物AL+.pmx；普通吊饰与AL+版二选一 |
| 茉莉庭院 | 主體 + 吊飾串珠 + 地面花叢 + 綢帶窗簾；不要同时叠加合并版 |
| 六角花台绣球 | 主体.pmx；单独PMX是同几何拆件，原包子目录的相对贴图缺失 |
| 琉璃万花镜 | 主体·琉璃万花镜 + 吊饰（AL+）·琉璃万花镜 |
| 古典机械剧场 | 主体 + 吊饰 + 地面装饰，使用原版而非黑蓝白改色版 |
| 拟花十字之棺 | 场景.pmx |

花吐电気的灌木有极少数点远离主体，古典机械剧场的台座向地下延伸；摄像机应针对可见舞台构图，不能仅根据完整网格包围盒。

AL+ 命名或附加 UV Morph 是 MME 控制约定，不等于源文件包含 Unity 灯光。当前场景搭建需要独立设置材质 Emission、Bloom 和真实灯光。

## 已完成的本机搭建

2026-10-29 已在上述目标目录保存八个 .unity 场景、200 个外置 Ho lilToon 材质和 107 盏灯。每个场景独立拥有 Pipeline、Renderer、天空、Volume 与主相机。运行时唯一共享的项目组件是 _制作工具/Runtime 中的活动场景环境选择器；场景不引用其他舞台的模型、材质或 Volume。

八份 1280×720 主机位 PNG 位于各自 Renders/，来自本机 Unity 6000.3.15f1、HoURP 与 Ho lilToon 的实际渲染。主目录的“场景索引.md”连接场景和截图，“Rendering-check.json”记录图片哈希、灯光数和场景检查。

本机使用实时光照，不依赖共享 Lightmap。五个场景带独立地面平面反射。第二轮为八个场景补了独立天空球、全部模型投影、摄影灯光、Ho 太阳体积光与尘粒、Ho 空气透视和 Bokeh 景深，以及 Bloom、分离色调、暗角、轻微色差与颗粒。原始 MME 程序没有在 Unity 中执行，AL+ 灯具以 Ho lilToon 自发光和真实局部灯重新制作。

Ho ImageProcess 的末尾依次是柔光、2.39:1 电影边框和 Hollow Logo。日景为白边黑标，夜景为黑边白标；用户指定的透明 PNG 每场景单独保存，Logo 位于底部正中。导入保留 NPOT 尺寸，防止宽字标被纹理导入器压扁。所有效果都在本场景的后处理 Inspector 修改，图层可独立关闭。

花吐电気以高亮窗景为重点，三盏“窗外晴日”灯只照室外六类网格（Rendering Layer 2），室内保留原照明组；天空 HDR、太阳体积光、Bloom、空气透视和 Ho SkyTyndall 一起控制晴日空气感。该场景 GB 开启天空缓存，并从 1500 队列开始收集，避免背景天空球把窗景天空信号遮住。

圣诞万花镜新增十三盏点光，十二盏位于低处，为地面、礼物和树脚补光，一盏照树冠。树光直接以导入后的树网格 bounds 定位，避免源 PMX 坐标翻转后照错位置。ShadowCast 使用 Medium 容量以容纳这些有阴影的局部灯；灯饰自发光仍受单独限制。

第三轮进阶版：八个场景统一使用独立 Ho-RT host、RTGI 一跳漫反射和 RELAX diffuse 降噪，明确把各自天空球材质排除出 RT 求交。五份地面 PLR 提高到 1024；按场景加入 Oklab 渐变映射、基于法线的主体分区塑形、贴地空气层、扩散柔光和星芒，三个日景带轻微镜头光晕。电影边框与 Logo 始终最后叠加，保持纯色边框与对比色字标。

用户明确要求：**禁止使用 SSGI，因为会破坏角色渲染；GI 只允许 RTGI。** 本套 Renderer 和 Volume 中已彻底移除 SSGI，包括 inactive/orphaned subassets；制作工具不提供 SSGI 分支，验收要求其 Feature/Volume 数量都为零。无 DXR 的设备不回落到 SSGI。

RTGI 使用 D3D12 DXR；本机实拍也以 D3D12 运行。各自 Rendering/进阶效果.json 保存本轮配方，后处理与 Feature 均直接存在本场景资产中，不依赖先运行脚本才能显示。可在 Inspector 关闭某层，或调低 GI / 调色 / 雾 / 柔光的份额。花吐电気的三盏室外专用艺术灯只影响 raster 照明，不参与尚未支持该灯光链接的 RTGI 解析灯采样。

## Unity 制作工具

Unity/ 是本机 Assets/mmd场景测试/_制作工具 的源码快照，不包含第三方模型或贴图：

- HoMmdStageBuilder：初建、调整入口及场景初始配方。
- HoMmdStagePolish：本套场景的美术配方。重跑会重设相关材质和灯光，日常使用请直接修改场景。
- HoMmdStageLighting：第二轮光影配方，读取各自 Rendering/光影精调.json；初始局部灯强度保存在原灯光记录.json，以免重复运行后强度不断缩小。新增摄影灯位于“06 摄影灯光”。重跑会重设这套配方控制的材质、灯光和后处理，日常直接编辑场景/Volume。
- HoMmdStageAdvanced：只叠加进阶配方，不重新执行灯光配方。重复执行只替换名称以“进阶 · ”开头的后处理层，保留现有灯位、曝光、窗景、品牌层。JSON 持久保存美术取值；本工具的 GI 读回是 Editor 诊断，通过反射读取本相机的持久结果，不以可能被反射相机重置的 shader global 当作证据。
- HoMmdStageCapture：Single 场景隔离、跨帧 StandardRequest、HDR 异步 GPU 读回、在写 PNG 前执行一次线性到 sRGB 编码。
- HoMmdStageAudit：真实 Unity 依赖闭包、相机、材质、环境、全部模型投影、天空球、景深、底部 Logo/边框对比色与独立配置检查。背景天空球使用专用天空 shader，模型继续全部使用 Ho lilToon。
- HoMmdStageEnvironment：按活动场景临时选择 Quality Pipeline 和环境，离开/重载时恢复。

当前目标工程正在打开时，用 stage-cli.py 与 Library/HoMmdStages 文件队列通信；不需要鼠标键盘操作。命令超时仅表示观察超时，先检查真实进程和 running.json，不要直接重复提交。

~~~powershell
python Tools~/MmdStages/stage-cli.py status
python Tools~/MmdStages/stage-cli.py render 花吐电気
~~~

工程未打开时，可通过 Unity CLI 运行以下静态入口；同一个项目不能同时启动两个 Editor：

~~~text
-batchmode -nographics -executeMethod HoMmdStageBuilder.BuildRemainingBatch
-batchmode -nographics -executeMethod HoMmdStagePolish.RunBatch
-batchmode -nographics -executeMethod HoMmdStageLighting.RunBatch
-batchmode -force-d3d11 -executeMethod HoMmdStageLighting.RefineAndRenderBatch
-batchmode -force-d3d12 -executeMethod HoMmdStageAdvanced.ApplyAndRenderBatch
-batchmode -force-d3d11 -executeMethod HoMmdStageBuilder.RenderAllBatch
-batchmode -nographics -executeMethod HoMmdStageAudit.RunBatch
~~~

构建入口只创建缺失场景。Polish 只修改本套场景，截图前拒绝自动丢弃未保存的场景。截帧完毕会恢复原场景布局和管线。不要使用 Camera.Render() 替代 SRP 截帧入口；本机最初的直接路径出现过原生渲染断言崩溃，后续 StandardRequest 已完成八场景输出。

当前主视角使用 32 个独立帧预热体积与 GI 历史；Library/HoMmdStages/*-lighting-state.json 记录实际提交的体积状态、SP/IP 写入图层数、焦点和曝光，*-advanced-state.json 保存主相机 GI 的实际尺寸、线性 HDR 读回统计及 RTGI/RELAX 状态。RenderOneBatch 支持 -hoNoVolume -hoOutputSuffix without-volume，便于在 Library 输出体积光开关对比而不替换正式 PNG。截图从 HDR float 读回后只进行一次 sRGB 编码。

只更新指定场景时，RefineAndRenderBatch 可传 -hoStages 花吐电気,圣诞万花镜。重新制作前先保存手工修改；该入口会按制作配方重设所选场景的受控参数。

进阶版只更新指定场景同样传 -hoStages；用 HoMmdStageAdvanced.RenderSelectedBatch -hoNoGi -hoOutputSuffix without-gi 可以只做关闭 GI 的对照，不重设制作配方、不覆盖正式 PNG 或 GI 诊断。-hoGiOnly 输出独立 GI 调试图，-hoWidth 960 供制作预览。正式八场景仍为 1280×720。

本机详细证据放 Temp~/MmdStages/，不进入包资源或 Git。当前验证是场景搭建与主相机输出，不是 MME 逐像素还原或性能认证。

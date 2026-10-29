# H2Cu 场景本地资源准备

## 当前制作入口

日常制作改用 [Ho PMX 场景制作 skill](../Skills/ho-mmd-scene-rendering/SKILL.md)：新 PMX 导入/抽材质、创建初始层级、定点调整灯光/Volume、只渲染多角度 review。手工场景是当前版本，不每次跑旧全场景审计或恢复配方。下文的固定批次验收和全量哈希是历史记录，不作为日常门槛。

```powershell
python Tools~/MmdStages/stage-cli.py create --folder "Assets/mmd场景测试/作者/新场景" --pmx "Assets/mmd场景测试/作者/新场景/Source/原始包/主体.pmx"
python Tools~/MmdStages/stage-cli.py logo --logo-y .1 --logo-size .3
python Tools~/MmdStages/stage-cli.py review --scene "Assets/mmd场景测试/作者/新场景/新场景.unity" --warmup 32
```

先将完整包保持相对贴图路径复制进 Source。create 不覆盖已有场景；review 不调用制作配方，默认把三机位 PNG 写入 Library/HoMmdStages/Review/<场景>/<时间>。单张使用 render --scene，Logo 默认 Y=.1、大小=.3。只保存目标 Logo Profile，不顺手保存用户场景。任意场景 review 入口不要求固定雾、GI、景深、灯数、天空或材质参数。

旧的 StageDetail/Lighting/Advanced/Remaining 会重新应用早期艺术配方，仅在明确需要重置该配方时调用。新通用入口为 HoMmdSceneWorkshop、HoMmdStageBranding，捕获底层为 HoMmdStageCapture。

PLR 像素回归与当前 CLI 入口见 [PlrRegression-20261030.md](PlrRegression-20261030.md)。场景现已共用固定 PC Renderer/Pipeline；下文早期十九场景的独立 Renderer 描述已由共享方案取代，Volume/材质/模型仍按场景独立。

`prepare-stage-sources.py` 将本机已整理的十九个 H2Cu 场景及月昔梅补回包复制到 BREAK_URP 的各场景 `Source/`。保留 PMX 相对路径和作者说明，不移动源素材，不复制压缩包、Blender 中间文件或其他工程的 `.meta`。

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

## 当前本机搭建

现有 19 个独立场景，每场景均已保存主、左侧、右侧三个机位和实际 1280×720 渲染，共 57 张 PNG。各自 Renders/三机位总览.jpg 便于对比；主目录场景索引.md 连接全部场景与机位，Rendering-check.json 保存逐机位 RTGI/RELAX、局部体积光状态与文件哈希。

Ho lilToon 模型材质统一开启 ShadowCaster 和 AO 艺术增强，每场景都配置 1024 PLR、天空球、景深、电影边框与底部 Hollow Logo。模型、贴图、材质、Pipeline、Renderer、Volume 均按场景独立维护。使用实时光照，不依赖共享 Lightmap；源文件复制清单的 283 个文件及 E 盘源文件哈希均核对一致。

局部 Point/Spot HD 体积光挂在真实灯物体上，使用独立光源深度与材质 ShadowCaster。主体重点、轮廓与远景由世界空间灯光塑形，已移除屏幕 PostLighting 与 CenterColorCorrection。其他全局调色、Bloom、轻微颗粒和镜头柔光保留。夜景削弱太阳散射与层叠灰雾；乐园·墟实保留原有废墟雾光风格，百鸟溯归增加两侧书墙与远书架补光，保留远处细节。

花吐电気保留明亮晴日窗景和室外 Rendering Layer 2 专用照明；圣诞保留中央树、礼物与地面低位点光补光。月昔梅补回完整包后，由主体、树、花枝、灯四份 PMX 组成黄梅庭院；补回包独立保存在 Source/完整场景，维持贴图相对引用。原团扇保留为独立 prefab 和默认关闭的配套道具，临时展台已撤除。云蜃马戏团·有识月按用户要求跳过，朱木古堂与血月古堡未被本套脚本覆盖。

用户要求：**禁止使用 SSGI，GI 只允许 RTGI。** Renderer/Volume 的 inactive/orphaned SSGI 子资源也被移除，审计要求两者数量均为零。RTGI 使用 D3D12 DXR + RELAX；各场景天空球材质排除出 RT 求交。无 DXR 不回落到 SSGI。太阳 HD 和局部体积光的 D3D11/D3D12 单独功能验证见 Tests~/VolumeLight/Local，本套实际场景机位验收为 D3D12，不是帧率认证。

## Unity 制作工具

Unity/ 是本机 Assets/mmd场景测试/_制作工具 的源码快照，不包含第三方模型或贴图：

- HoMmdStageDetail：本轮细节灯光制作与三个机位捕获。细节灯光基线.json 防止多次执行叠乘；细节灯光.json 保存可调取值。09 细节灯光是新增真实灯与遮片；10 观察机位保存两台关闭的辅助相机，原主相机继续启用。
- HoMmdYueximeiComplete：补回月昔梅完整包的一次性装配，保留原场景 GUID 与团扇；Run 之后进入标准三机位细节灯光捕获，AuditBatch 核对四份模型、外置材质和相对贴图。
- HoMmdStageRemaining：新增十一场景的模型组合、HDR、地面、材质与局部灯配方。
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
-batchmode -force-d3d12 -executeMethod HoMmdStageBuilder.RenderAllBatch
-batchmode -force-d3d12 -executeMethod HoMmdStageDetail.Run
-batchmode -nographics -executeMethod HoMmdStageAudit.RunBatch
~~~

构建入口只创建缺失场景。Polish 只修改本套场景，截图前拒绝自动丢弃未保存的场景。截帧完毕会恢复原场景布局和管线。不要使用 Camera.Render() 替代 SRP 截帧入口；本机最初的直接路径出现过原生渲染断言崩溃，后续 StandardRequest 已完成八场景输出。

当前主视角使用 32 个独立帧预热体积与 GI 历史；Library/HoMmdStages/*-lighting-state.json 记录实际提交的体积状态、SP/IP 写入图层数、焦点和曝光，*-advanced-state.json 保存主相机 GI 的实际尺寸、线性 HDR 读回统计及 RTGI/RELAX 状态。RenderOneBatch 支持 -hoNoVolume -hoOutputSuffix without-volume，便于在 Library 输出体积光开关对比而不替换正式 PNG。截图从 HDR float 读回后只进行一次 sRGB 编码。

只更新指定场景时，RefineAndRenderBatch 可传 -hoStages 花吐电気,圣诞万花镜。重新制作前先保存手工修改；该入口会按制作配方重设所选场景的受控参数。

进阶版只更新指定场景同样传 -hoStages；用 HoMmdStageAdvanced.RenderSelectedBatch -hoNoGi -hoOutputSuffix without-gi 可以只做关闭 GI 的对照，不重设制作配方、不覆盖正式 PNG 或 GI 诊断。-hoGiOnly 输出独立 GI 调试图，-hoWidth 960 供制作预览。正式十九场景的三个机位均为 1280×720。

HoMmdStageDetail.Run 同样接受 -hoStages 子集，默认为每场景连续捕获三个机位；-hoApplyOnly 1 只应用受控参数。不要带 -quit，否则 Unity 会在异步捕获队列执行前退出。-hoWidth 960 -hoOutputSuffix preview 将三个预览写到 Library，避免覆盖正式图。

python Tools~/MmdStages/verify-detail-lighting.py 汇总 Unity 原生审计、57 张图、三机位实际 GI/局部体积光状态和源文件哈希，并生成索引与机位总览。最终证据放 Temp~/MmdStages/DetailLighting-20261029/。当前验证是场景搭建与三个机位输出，不是 MME 逐像素还原或性能认证。

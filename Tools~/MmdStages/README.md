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

本机使用实时光照，不依赖共享 Lightmap。五个场景带独立地面平面反射，其余按各自材质/空间需要保留大气、GTAO、软阴影、透明 OIT、Bloom 和调色配置。原始 MME 程序没有在 Unity 中执行，AL+ 灯具以 Ho lilToon 自发光和真实局部灯重新制作。

## Unity 制作工具

Unity/ 是本机 Assets/mmd场景测试/_制作工具 的源码快照，不包含第三方模型或贴图：

- HoMmdStageBuilder：初建、调整入口及场景初始配方。
- HoMmdStagePolish：本套场景的美术配方。重跑会重设相关材质和灯光，日常使用请直接修改场景。
- HoMmdStageCapture：Single 场景隔离、跨帧 StandardRequest、HDR 异步 GPU 读回、在写 PNG 前执行一次线性到 sRGB 编码。
- HoMmdStageAudit：真实 Unity 依赖闭包、相机、材质、环境与独立配置检查。
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
-batchmode -force-d3d11 -executeMethod HoMmdStageBuilder.RenderAllBatch
-batchmode -nographics -executeMethod HoMmdStageAudit.RunBatch
~~~

构建入口只创建缺失场景。Polish 只修改本套场景，截图前拒绝自动丢弃未保存的场景。截帧完毕会恢复原场景布局和管线。不要使用 Camera.Render() 替代 SRP 截帧入口；本机最初的直接路径出现过原生渲染断言崩溃，后续 StandardRequest 已完成八场景输出。

本机详细证据放 Temp~/MmdStages/，不进入包资源或 Git。当前验证是场景搭建与主相机输出，不是 MME 逐像素还原或性能认证。

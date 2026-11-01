# H2Cu 旧场景 Ho 后处理整理与阶梯大球调灯

完成日期：2026-11-02。工程：`D:/Unity_Project/BREAK_URP`。

## 后处理整理

- 范围为之前制作的 27 个 H2Cu 源场景 Profile，及五个动画场景共用的一份 Profile。用户手工维护的其他舞台不在本次范围内。
- 源场景共移除 189 个 URP 原生图像后处理组件；28 份 Profile 均显式序列化「仅使用 Ho 后处理」的值与 override。最终清单中原生组件为 0，丢失组件为 0。
- 清理阶段保留原有 Ho 艺术参数，迁移前后指纹一致。后续有意增加 Ho 色调映射及定点景深/体积修改，不能把整个最终 Profile 宣称为完全未变。
- 26 份启用中的源场景 ImageProcess 使用 Ho ToonMap / ACES；乐园·墟实原本关闭的 ImageProcess 保持关闭。共享动画 Profile 保留另一个制作任务的滤镜设置，只调整接管声明。
- 丝苇楼梯、百鸟溯归减轻过度景深；虫无、虫染按实际远景距离调整焦点。未重跑旧的全场景打灯配方。

实际 GPU 对照覆盖花吐电気、孔雀月令、共享动画场景：Ho 接管开启时，无论相机原生后处理开关或原生曝光/Bloom 压力参数怎样改变，执行状态均为 `hoOnly=true`、`nativeColorLut=false`，FXAA 保留。原图与原生压力测试的 RGB 平均绝对差为 0.0129 / 0.0245 / 0.0225（0–255）；关闭 Ho 图层后差异显著，显式退出接管后原生 LUT 才执行。微小非零差异不当作逐像素确定性的保证。

## 虫无 / 虫染：大球轮廓

作者随包预览的上半是无渲染图，下半是 RAY 效果图。大球是 PMX 的 `001_球` 网格，中心约 `(0,24.64,-61.86)`，半径约 14.98m。包内未找到对应 MME `.fx`，不能从预览图推断这些效果已随 PMX 导入。

旧灯主要位于前景、范围 18–25m，照不到距主相机约 77m 的大球；体积远距只有 70m，空气区域顶部约 42m，也不足以包住球后上方的观察射线。

本次调整保存在两个舞台各自的 Scene、`Materials/001_球.mat`、`Rendering/后处理.asset` 和 `Rendering/灯具补光.json`：

| 层级 `11 实景灯具/大球 · 轮廓与光晕` | 作用 |
| --- | --- |
| 大球 · 后上轮廓 | 实光塑形球体上缘 |
| 大球 · 掠射高光 | 侧前方表面高光 |
| 大球 · 背景光晕 | 朝后照亮球后上方空气 |
| 大球 · 左后空气 / 右后空气 | 扩展两侧背景光晕 |

- 两版分别保持蓝色、红色调。球材质启用直射高光、光滑度 0.45，原贴图和关闭的自发光保留。
- 三盏背光采用 HD 独立遮挡、96 步、1024 阴影深度；扩大光源柔化半径，控制光源附近亮点。表面照明与局部体积倍率分别平衡，减轻背景几何的过曝。
- 球后光锥朝后发射，避免前向光柱盖住暗球。空气区域顶部扩至约 74m，体积远距 140m，全局体积倍率由 3.08 调至 1。
- 关闭两盏旧前景灯的局部体积组件，保留它们的表面照明。未改共享 Renderer 或开启 SSGI。
- 保持三台相机位置与 FOV，朝向改为 `(0,13,-60)`，让球体完整入画；Ho 景深焦点 70m、f/8、最大模糊半径 3px。
- 新灯组已补 vHierarchy 颜色。可以直接在 Inspector 改灯；JSON 只在显式执行 `practicals` 时重应用，不会在普通渲图时复位。

这是一版突出暗球轮廓的 Ho 灯光风格，不是完整复制作者的 RAY/MME 链。作者图中的全部浮尘、粒子、镜头效果没有逐项复刻。

## 渲图与后续操作

交付检查图为 27 个主机位；其中七个代表舞台有三个角度。两版阶梯最后一轮为 960×540、48 帧预热、主/左/右三视图；其他舞台采用 640×360 验证图。这些是制作预览，不是高质量离线影片。

本机证据位于：

```text
D:/Unity_Fork/lilToon-URP-Extensions/Temp~/HoOnlyReview-20261101/
  h2cu-final.json / performance-final.json
  proof-summary.json
  final/<舞台>.png
  angles/<舞台>/{main,left,right}.png
  sphere-reference-comparison.jpg
  sphere-final-three-views.jpg
  stage-review-{1,2,3}.jpg
  sphere-before/
```

迁移前 Profile 备份保存在 `BREAK_URP/Library/HoMmdPostReview/Backup/`；阶梯修改前的 Scene、球材质、计划和截图另存于上述 `sphere-before/`。这些本机临时证据不作为跨机器资源依赖。

只拍当前效果，不重新打灯：

```powershell
python Tools~/MmdStages/stage-cli.py review `
  --scene "Assets/00动画/MMD场景/H2Cu阿相/虫无阶梯/虫无阶梯.unity" `
  --width 960 --height 540 --warmup 48
```

新增 `post-*` 定点工具保留在制作脚本中；`stage-survey` 可调查全部几何、灯光与机位。闭 Editor 批处理也支持这些动作。修正了后台三机位停在首张的问题，以及从空场景启动批处理时恢复 SceneSetup / 卸载最后场景的清理问题。最后的 Unity 批处理均编译并正常退出；旧 Persistent 分配警告不属于本次已解决范围。

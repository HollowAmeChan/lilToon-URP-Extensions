# 渐变映射（GradientMap）

`ImageProcessEffect.GradientMap` / 面板名「渐变映射」/ shader `Hidden/lilToon/URP/ImageProcess/GradientMap`。

它做的是**亮度驱动的颜色映射**：先用画面里的某个量（默认 Rec.709 亮度）算出 0–1 的索引，再用这个索引去查一条 4 色标的颜色带，最后按图层混合模式与不透明度合成回画面。位置无关——和「渐变」（`Gradient`，屏幕空间位置驱动）是两件事，这也是它被做成独立效果而不是 Gradient 的新模式的原因（决策记录见 `GradientInvestigation.md` 第 6 节）。

预览图（由 `.codex-research/gradient_map_sim/sheet.js` 生成，不是 Unity 截图）：

- `Images/GradientMap/looks.png`：16 个预设套在合成测试图上的接触表，每格下方是该预设的色标条。
- `Images/GradientMap/spaces.png`：逐项控制演示（插值空间、色阶数、反转、输入窗口、输入选择、明度混合）。
- `Images/GradientMap/source.png`：未处理的测试图。

## 参数

| 控件 | 含义 | 默认 |
| --- | --- | --- |
| 输入 | 用来索引色标的量：亮度（Rec.709 0.2126/0.7152/0.0722）、红、绿、蓝、最大值、平均值、饱和度（max−min） | 亮度 |
| 黑场 / 白场 | 输入窗口：黑场映射到色标 1，白场映射到色标 4，区间外被截断 | 0 / 1 |
| 颜色 1–4 | 四个色标的 RGBA；alpha 为 0 的色标不会改变画面 | 黑 → 1/3 灰 → 2/3 灰 → 白 |
| 色标位置 | 四个色标在 0–1 上的位置，拖动时不会越过相邻色标 | 0 / 1/3 / 2/3 / 1 |
| 插值空间 | 相邻色标之间的插值空间：显示空间（默认）、线性光、Oklab | 显示空间 |
| 色阶数 | 0 或 1 = 关闭；N ≥ 2 时先把索引量化成 N 级再查表，得到平涂色块 | 0 |
| 反转 | 反转色标方向：输入黑场对应色标 4（即 Photoshop 的 Reverse） | 关 |
| 抖动 | 输出端三角噪声，单位是 8-bit 码：0.5 = ±0.5/255 | 0 |
| 混合模式 | 图层通用字段，24 种；`正常` = 用色标替换颜色，`颜色` = 只换色相饱和度，`明度` = 只换亮度 | 正常 |
| 不透明度 | 图层通用字段（标题行的滑条），与色标 alpha 相乘 | 1 |

标题行还带预设按钮：`默认`（重置成黑白线性色标）+ 5 组共 16 个 look。

参数槽位对照（写预设/排查用）：

| 槽位 | 含义 |
| --- | --- |
| `p0` | (输入, 黑场, 白场, 抖动) |
| `p1` | (色标 1 位置, 色标 2 位置, 色标 3 位置, 色标 4 位置) |
| `p2`–`p5` | 色标 1–4 的 RGBA |
| `p6` | (插值空间, 反转, 色阶数, 预留) |
| `p7`–`p12` | 预留（例如以后加每个色标的中点/曲线、更多色标） |

## 语义与边界情况

- **默认值就是"亮度转灰度"**：黑白线性色标 + `正常` 混合，等于把画面换成它自己的亮度。这也是 Photoshop 里默认黑白渐变的 Gradient Map 给人的第一印象，所以把它当作新图层的默认状态。
- **区间端点重合**（两个色标同位置）读作硬切，不产生 NaN。
- **黑场 = 白场**时窗口退化，按"≥ 白场取色标 4、否则色标 1"的二值阈值处理，不做除零。
- **色标非单调**（手改 profile 或旧数据）不会崩：shader 用 `max(分母, 1e-6)` 保护，UI 在每次绘制时会先把序列修回单调。
- **线性光 / Oklab 插值可能产生色域外的中间色**，结果是按 `[0,1]` 简单裁剪（不是 CSS 那种 gamut mapping）。显示空间插值不会越界。
- **抖动用的是输出端三角噪声**，和「渐变」的抖动实现同一套（`Hash12` + `sign(noise)*(1-sqrt(1-|noise|))`），单位一致：1 = ±0.5/255。
- **alpha 语义**：色标 alpha 与图层不透明度相乘，最终 `lerp(原图, 混合结果, alpha)`；输出 alpha 沿用原图（图像域效果保持相机颜色的 alpha）。

## 与 Photoshop / AE 的关系（来源可信度）

- 「亮度索引一条 1D 色带」这个做法本身有可核实的一手来源：
  - GPU Gems 1 第 22 章（NVIDIA 官方在线版）给的是 `float grayscale = dot(float3(0.222,0.707,0.071), inColor); OutColor = tex1D(ColorCorrMap, grayscale);`，即用亮度索引 1×256 的颜色校正贴图（权重是 Rec.709 亮度的一种舍入，本效果用标准 0.2126/0.7152/0.0722）。
  - Godot 官方文档源码（`godot-docs` 仓库 `environment_and_post_processing.rst`）：`Color Correction` 用一条 1D 渐变，"leftmost part of the gradient represents black … rightmost part represents white"，并且"a linear black-to-white gradient like the following one will produce no effect"——这正是本效果默认值的语义。
- **Adobe 的正文没有核实过**：Photoshop「Gradient Map」帮助页正文抓不到（导航体积吞掉正文），AE 的 Gradient Ramp / Colorama 同理，详见 `GradientInvestigation.md` 第 7 节的工具限制说明。所以本文档**只把 `Reverse` / `Dither` 当作命名习惯**，不声称 Adobe 的具体行为（例如"Photoshop 是按亮度还是按通道索引"本文档不下结论，本效果则把选择权交给「输入」下拉）。
- Oklab 插值：矩阵与推导见下一节，来源是 Björn Ottosson 2020 的 Oklab 与 W3C CSS Color 4 的示例转换代码。

## 预设

| 组 | 名称 | 做了什么 | 颜色来源 |
| --- | --- | --- | --- |
| 双色调 | 棕褐 | 暗部暖褐、亮部奶油，Oklab 插值 | 手写配方 |
| 双色调 | 冷夜 | 压暗偏蓝 + 冷青亮部 | 手写配方（"白天拍夜戏靠压暗+偏蓝"是通行做法） |
| 双色调 | 青橙 | 暗部青、亮部橙 | 手写配方（商业片常见的冷暖对撞） |
| 双色调 | 铜色 | 黑→铜褐→浅铜 | matplotlib `copper` 控制点（`_cm.py`）按同一套分段线性插值取样 |
| 胶片 | 高对比黑白 | 印片曲线式 S 形灰阶 | 手写配方 |
| 胶片 | 褪色 | 黑位抬到深灰、白位压到米白 | 手写配方（"褪色胶片 / Faded Film"一类） |
| 胶片 | 漂白旁路 | 灰阶色标 + `明度` 混合，只改影调不改色彩 | 手写配方（银盐保留式的做法） |
| 胶片 | 负片 | 反转 + 灰阶 | 手写配方 |
| 风格 | 日落 | 紫罗兰→洋红→橙金 | 手写配方 |
| 风格 | 赛博霓虹 | 近黑紫→紫→洋红→青 | 手写配方 |
| 风格 | 海报平涂 | 青橙色标 + 色阶数 6 | 手写配方 |
| 风格 | 红外假色 | 以红通道索引（输入 = 红） | 手写配方；**是风格化近似，不是红外胶片的色彩科学** |
| 数据可视化 | 红外热图 | 黑→紫→橙→近白的热像仪 ironbow | 第三方 MIT 反推的 FLIR 风格色表（`MickTheMechanic/FLIR-style-thermal-color-palettes`）；**FLIR 官方只发布渲染图、没有可核实的数值表，所以这不是 FLIR 官方数据** |
| 数据可视化 | 光谱 | 深蓝紫→青→黄→暗红的伪彩 | Google 官方 `turbo_colormap.cc`（Apache-2.0，Copyright 2019 Google LLC），取 256 项字节表 |
| 数据可视化 | 岩浆 | 黑→紫红→橙→淡黄 | matplotlib `inferno`（`_cm_listed.py` 的 256 项表） |
| 数据可视化 | 叶绿 | 深紫→蓝绿→绿→黄 | matplotlib `viridis`（同上） |

采样规则（`palette_looks_check.js` 与之一致）：LUT 型色表取 `index = round(x * (N-1))`，色标位置报 `index/(N-1)`，因此 256 项与 433 项的表都正好落在 0、1/3、2/3、1；控制点型色表（copper）用 matplotlib 自己的分段线性插值。**4 个色标是原色表的 4 点线性近似**——色标之间是直线，和 256 项原表在中间位置会有极小偏差（显示空间插值时最大偏差量级是原表相邻两项的斜率变化）。

> 手写配方的颜色没有"官方出处"，只为观感负责；带色表来源的 5 个 look 的每个数字都能用 `node palette_looks_check.js` 从保存下来的原始文件重新算出来（见下节）。

## 数值验证

- **Oklab 常量**：`.codex-research/gradient_map_sim/oklab_check.py`（Python，11 项）从 W3C CSS Color 4 的官方示例代码矩阵（线性 sRGB↔XYZ 的精确分数、XYZ→LMS、LMS′→Lab 及其逆）**推导**出 shader 里用的合并矩阵，再检查：往返误差 3.6e-15、白点恰好映射到 (1,0,0)、黑点 (0,0,0)、sRGB 三原色坐标与公开值一致（误差 < 8e-8）、线性部分互逆（5e-16）、本轮推导与最初手写的常数一致（5.5e-9 / 5.2e-8）、10 位小数舍入误差 9.8e-10；负对照（把矩阵某个元素改 1e-3）会让往返误差升到 3.7e-3，证明检查不是恒绿。
- **ramp 数学**：`.codex-research/gradient_map_sim/gradient_map_check.js`（Node，14 项）在 shader 的 1:1 复刻上验证：默认色标在灰阶上是恒等（1.1e-16）、输入窗口的截断/重映射、反转镜像、色阶数 6 恰好 6 级且首末为 0/1、零宽色标是硬切、退化窗口是阈值、alpha=0 色标不动画面、线性光与 Oklab 中点与**独立实现**的差（0 与 2.0e-10）、Luminosity 混合把色标的亮度搬到原图上、16 个 look 在 5 个探针 × 8 组参数下都有限且合法；负对照（扰动 Oklab 矩阵、或去掉色标 alpha 的补全）会让对应检查失败（1.2e-3 / NaN）。
- **色表取样**：`.codex-research/gradient_map_sim/palette_looks_check.js` 用自己写的解析器，从 `.codex-research/gradient_map_research/sources/` 里保存的**原始文件**（matplotlib `_cm_listed.py`/`_cm.py`、Google `turbo_colormap.cc`、FLIR 风格 `flir_IRONBOW.c`）重新采样 5 个色表 look 的 4×3 个分量，与 look 表逐值比对（1e-9）；负对照（把"光谱"第 2 个色标改 1/255）会 FAIL。研究目录自己的 `verify_palette.js` 另外做了 22 项溯源检查（含 15 个源文件的 SHA-256、turbo 浮点表与字节表 768/768 通道一致、ironbow 单调性与蓝通道升降形状），并有自己的负对照。
- **shader 与复刻不漂移**：同一个脚本按顺序抽取 shader 两个 Oklab 函数体与复刻里的十进制字面量逐一比对（39 个常量），并把变换函数阈值作为存在性检查；把 shader 里任一常量改掉就会 FAIL（已实测）。
- **HLSL 编译**：`.codex-research/shader-check/`（用 Unity 自带的 `D3DCompiler_47.dll`，`ps_5_0`，`FragGradientMap`）**编译通过**；不需要开 Unity 就能挡下语法/类型/未声明标识符错误。
- **C# 编译**：独立 Roslyn 编译 Editor + Runtime 两个程序集，**0 error**（只剩仓库里原有的 11 条 CS0649 警告）。
- **C# 预设表**：`.codex-research/gradient_map_sim/verify_csharp.js` 检查两件事——重新生成能逐字节复现已提交的 `ImageProcessStackVolumeEditor.GradientMapLooks.cs`，以及把生成文件里的 `new GradientMapLook(...)` 参数**解析回来**与 `looks.js` 逐字段按 `System.Single` 精度比对（防止生成器把两个字段写反或掉精度）。

## 如何新增/修改一个 look

1. 改 `.codex-research/gradient_map_sim/looks.js`（手写配方可以写 sRGB hex；采样自色表的 look 写**精确浮点三元组**，用 `node palette_looks_check.js --emit-js` 打印后粘贴，带 `source` 字段记录色表与许可）。
2. `cd .codex-research/gradient_map_sim` 后 `node gen_csharp.js` → 覆盖 `Editor/PostProcessing/ImageProcess/ImageProcessStackVolumeEditor.GradientMapLooks.cs`。
3. `node verify_csharp.js`（漂移 + 逐字段比对）、`node gradient_map_check.js`（含"所有 look 有限且合法"）、`node palette_looks_check.js`（带 `source` 的 look 与原始色表比对）。
4. 想更新文档图就 `node sheet.js`。
5. C# 侧不需要别的改动：预设菜单通过 `AddImageProcessGradientMapLookMenuItems` 遍历整张表。

改 UI 控件时注意：`Filters/GradientMap.cs` 里 `GradientMapBodyLineCount` 必须等于 `DrawGradientMapElement` 在折叠行之后实际绘制的行数，否则层列表的行高会错位（其它效果用同样的约定）。

## 尚未验证 / 已知限制

- **没有 Unity 实机画面**：HLSL 只做了编译验证，C# 只做了静态编译，"参数 → 画面"的结论全部来自 JS 复刻。需要在实机确认的清单：
  1. 控制台没有 shader 编译错误，层正常显示；
  2. 面板折叠行之后依次是：输入、黑场、白场、色标条预览、颜色 1–4、色标位置标题、位置 1–4、插值空间、色阶数、反转、抖动、混合模式（行高不错位）；
  3. 色标条的 64 段预览与竖线标记随拖动实时跟随；
  4. 加入效果后画面变成自身的亮度（默认黑白色标），换成「颜色」混合时只掉色相饱和度；
  5. 5 组 16 个预设的观感，以及「海报平涂」的 6 级平涂是否需要在 8-bit 目标上补抖动。
- **JS 复刻只覆盖 look 用到的混合模式**（`正常`、`明度`）——其余 22 种混合模式在 shader 里是从 `Gradient.shader` 复制的同一份实现，但没有针对本效果再跑一遍数值回归。
- **色表型 look 是 4 点近似**：色标之间线性插值，和原始 256/433 项色表在中间位置有极小偏差；而 `looks.png` 里的色表 look 用的是同一套 4 点复刻。
- **预览图是复刻渲染，不是 Unity 截图**，也没有模拟抖动（抖动是 ±0.5/255 的噪声）。
- **色标上限是 4 个**：这是"参数打包、Volume profile 自包含"的取舍（见 `GradientInvestigation.md` 5.3/6 的两条路线对比）。再多色标要么走贴图（复用 `_LayerTexture`），要么扩 `p7`–`p12`。
- **线性光 / Oklab 的具体观感**在实机上未确认；Oklab 的定位是"宽色标不发灰"，这一点在 `spaces.png` 的对照行里能看出来，但那是复刻渲染。

# 渐变映射（GradientMap）

> 状态：**现行功能说明（2026 文档审核核对）**。ImageProcess 的亮度驱动颜色映射；预览图是离线模拟（`.codex-research/*` 为本机私有脚本，不在仓库内），实机效果需自行确认。

`ImageProcessEffect.GradientMap` / 面板名「渐变映射」/ shader `Hidden/lilToon/URP/ImageProcess/GradientMap`。

它做的是**亮度驱动的颜色映射**：先用画面里的某个量（默认 Rec.709 亮度）算出 0–1 的索引，再用这个索引去查一条色标，最后按图层混合模式与不透明度合成回画面。位置无关——和「渐变」（`Gradient`，屏幕空间位置驱动）是两件事，这也是它被做成独立效果而不是 Gradient 的新模式的原因（决策记录见 `GradientInvestigation.md` 第 6 节）。

预览图（由 `.codex-research/gradient_map_sim/sheet.js` 生成，不是 Unity 截图）：

- `Images/GradientMap/looks.png`：16 个预设套在合成测试图上的接触表，每格下方是该预设的色标条。
- `Images/GradientMap/spaces.png`：逐项控制演示（插值空间、色阶数、反转、输入窗口、输入选择、明度混合、Fixed 硬台阶）。
- `Images/GradientMap/source.png`：未处理的测试图。

## 色标用的是 Unity 渐变 + 一张小 ramp 贴图

这一版把「4 个参数打包色标」换成了 Unity 原生类型与贴图采样，原因是原方案既占参数又不自由：

1. **数据是 `Gradient`**：图层上新增一个 `ramp` 字段（`ImageProcessLayer.ramp`，`[Serializable]`，跟着 Volume profile 一起序列化）。因此支持 Unity 渐变的全部能力——最多 **8 个颜色键 + 8 个透明度键**、每个键独立位置、`Blend` / `Fixed`（硬台阶）两种模式，并且 Inspector 里直接用 Unity 自己的渐变编辑器（可复制粘贴渐变色、可用 `Fixed` 做平涂台阶）。
2. **渲染读的是一张 1×256 的 ramp 贴图**：运行时按需把 `Gradient` 烘焙成 `Texture2D`（`ImageProcessGradientRampBaker` 负责数学，`ImageProcessGradientRampCache` 负责缓存与生命周期），shader 里就是**一次采样**（复用 `MaterialGradient` 模块的 `HoSampleGradient`）。
3. **参数因此少了很多**：原来 `parameters1`（4 个位置）+ `parameters2..parameters5`（4 个颜色）共 5 个 Vector4 被释放，只剩 `parameters0` 与 `parameters6` 两个标量块。
4. **插值空间挪到烘焙**：显示空间 / 线性光 / Oklab 现在是 CPU 端逐采样插值（`ImageProcessGradientRampBaker`），shader 不再做 Oklab 矩阵运算；代价为零，精度还更高（可以对整条曲线采样，而不是只能两点插值）。
5. **profile 仍然自包含**：贴图是运行时生成的（`HideFlags.HideAndDontSave`，随图层缓存，图层消失一段时间后销毁），不产生资源文件，不需要像 `MaterialGradient` 那样把渐变编码进贴图名字。

烘焙细节：256 个采样（`ImageProcessLayer.RampResolution`），显示空间 RGBA，`TextureFormat.RGBAHalf`（不支持时回落 `RGBA32`），`wrapMode = Clamp`，`filterMode` 在 `Blend` 模式是 `Bilinear`、`Fixed` 模式是 `Point`（对齐 `MaterialGradient` 的做法）。

> **半纹素拉伸**：烘焙把第 i 个采样放在 `t = i / (N-1)`，而纹理双线性采样假设纹素中心在 `(i + 0.5) / N`，两者差半个纹素（≈0.002 的色标位移）。shader 因此把坐标拉伸成 `t*(1 - 1/N) + 0.5/N`（`_LayerRampTexelSize` 由渲染端绑定），让采样**精确**复现烘焙结果，端点也精确落在首尾纹素上。这个问题是 JS 复刻的恒等检查发现的：修正前默认黑白色标在灰阶上的最大误差是 1.9e-3，修正后是 1.1e-16。

## 参数

| 控件 | 含义 | 默认 |
| --- | --- | --- |
| 色标 | Unity 渐变（≤8 颜色键 + ≤8 透明度键，Blend/Fixed）。透明度键参与合成：某处的 alpha 越小，原图保留得越多 | 黑 → 白 |
| 输入 | 用来索引色标的量：亮度（Rec.709 0.2126/0.7152/0.0722）、红、绿、蓝、最大值、平均值、饱和度（max−min） | 亮度 |
| 黑场 / 白场 | 输入窗口：黑场映射到色标起点，白场映射到终点，区间外被截断 | 0 / 1 |
| 插值空间 | 烘焙色标时用的空间：显示空间（与 Unity 渐变条一致）、线性光、Oklab | 显示空间 |
| 色阶数 | 0 或 1 = 关闭；N ≥ 2 时先把索引量化成 N 级再查表（shader 端，2 条指令） | 0 |
| 反转 | 反转色标方向：输入黑场对应色标末端 | 关 |
| 抖动 | 输出端三角噪声，单位是 8-bit 码：0.5 = ±0.5/255 | 0 |
| 混合模式 | 图层通用字段，24 种；`正常` = 用色标替换颜色，`颜色` = 只换色相饱和度，`明度` = 只换亮度 | 正常 |
| 不透明度 | 图层通用字段（标题行的滑条），与色标 alpha 相乘 | 1 |

标题行还带预设按钮：`默认`（重置成黑白渐变）+ 5 组共 16 个 look。面板里的渐变条就是 Unity 渐变编辑器本身（显示的是"作者写下的渐变"，即显示空间 + Blend 的解释）；插值空间换成线性光/Oklab 或切到 Fixed 之后，实际渲染的色标与这条渐变条会有细微差别，看画面即可（效果是实时渲染的）。

参数槽位对照（写预设/排查用）：

| 槽位 | 含义 |
| --- | --- |
| `p0` | (输入, 黑场, 白场, 抖动) |
| `p6` | (插值空间, 反转, 色阶数, 预留) |
| `p1`–`p5` | **已归还为预留**（旧版本用它们放 4 个色标的位置与颜色；见下面的迁移说明） |
| `p7`–`p12` | 预留 |

> **迁移说明**：`p1`–`p5` 的旧含义已废弃。用旧版本存过的 GradientMap 图层读到新版本时会拿到默认的黑白渐变（`ramp` 为空 → UI 与运行时都用默认值），也就是变成"亮度转灰度"，不会黑屏或报错；重新套一次预设即可。

## 语义与边界情况

- **默认值就是"亮度转灰度"**：黑白渐变 + `正常` 混合，等于把画面换成它自己的亮度。
- **区间端点重合**（两个键同位置）在 `Blend` 模式下是"一个纹素宽的过渡"（双线性采样的固有结果，≈1/255 的色标宽度），`Fixed` 模式下是精确台阶。
- **黑场 = 白场**时窗口退化，按"≥ 白场取色标末端、否则取起点"的二值阈值处理，不做除零。
- **键非单调**（手改 profile）不会崩：烘焙前按时间稳定排序并夹到 [0,1]（C# 与 JS 两边的实现都这样做）。
- **线性光 / Oklab 插值可能产生色域外的中间色**，烘焙时按 `[0,1]` 简单裁剪（不做 gamut mapping）。
- **alpha 语义**：色标 alpha 与图层不透明度相乘，最终 `lerp(原图, 混合结果, alpha)`；输出 alpha 沿用原图。
- **没有贴图就直通**：若某帧拿不到烘焙贴图（例如 `ramp` 为空），shader 直接返回原图，不会让整条链断掉。

## 与 Photoshop / AE 的关系（来源可信度）

- 「亮度索引一条 1D 色带」这个做法本身有可核实的一手来源：
  - GPU Gems 1 第 22 章（NVIDIA 官方在线版）给的是 `float grayscale = dot(float3(0.222,0.707,0.071), inColor); OutColor = tex1D(ColorCorrMap, grayscale);`，即用亮度索引 1×256 的颜色校正贴图（权重是 Rec.709 亮度的一种舍入，本效果用标准 0.2126/0.7152/0.0722）。
  - Godot 官方文档源码（`godot-docs` 仓库 `environment_and_post_processing.rst`）：`Color Correction` 用一条 1D 渐变，"leftmost part of the gradient represents black … rightmost part represents white"，并且"a linear black-to-white gradient like the following one will produce no effect"——这正是本效果默认值的语义。
- **Adobe 的正文没有核实过**：Photoshop「Gradient Map」帮助页正文抓不到（导航体积吞掉正文），AE 的 Gradient Ramp / Colorama 同理，详见 `GradientInvestigation.md` 第 7 节的工具限制说明。本文档只把 `Reverse` / `Dither` 当作命名习惯，不声称 Adobe 的具体行为。
- Oklab 插值：矩阵与推导见下节，来源是 Björn Ottosson 2020 的 Oklab 与 W3C CSS Color 4 的示例转换代码。
- Unity 渐变的 `Fixed` 模式语义按"每个键的颜色保持到下一个键"实现（与 Unity 编辑器里 Fixed 渐变条的观感一致）。这一点**没有和 native `Gradient.Evaluate` 逐点比对过**（本仓库外无法执行 Unity 原生渐变），见"尚未验证"。

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
| 数据可视化 | 光谱 | 深蓝紫→青→黄→暗红的伪彩 | Google 官方 `turbo_colormap.cc`（Apache-2.0，Copyright 2019 Google LLC） |
| 数据可视化 | 岩浆 | 黑→紫红→橙→淡黄 | matplotlib `inferno`（`_cm_listed.py`） |
| 数据可视化 | 叶绿 | 深紫→蓝绿→绿→黄 | matplotlib `viridis`（同上） |

**采样规则**：色表型 look 用 **8 个键**，位置按色表的**累计 RGB 弧长**均匀取（`palettes.js#arcLengthPalette`），也就是在色表走得快的地方多放键；每个键的颜色仍是色表里某一个索引的**精确值**（位置就是该索引自己的 `index/(N-1)`），所以溯源关系没有变。

保真度（`bake_core_check` 实测，单位是 8-bit 码，取"烘焙结果 vs 完整色表"的平均/最大偏差）：

| 色表 | 4 键均匀 | 8 键均匀 | 8 键弧长（**出货用的**） |
| --- | --- | --- | --- |
| copper (256) | 1.4 / 25.4 | 0.2 / 10.1 | **0.2 / 5.0** |
| ironbow (433) | 19.8 / 114.2 | 5.0 / 42.2 | **5.2 / 30.6** |
| turbo (256) | 26.5 / 130.2 | 6.3 / 26.8 | **5.9 / 26.8** |
| inferno (256) | 12.1 / 77.9 | 2.9 / 24.0 | **2.8 / 18.5** |
| viridis (256) | 7.0 / 26.4 | 1.8 / 19.7 | **1.8 / 11.5** |

手写配方的颜色没有"官方出处"，只为观感负责；带色表来源的 5 个 look 的每个数字都能用 `node palette_looks_check.js` 从保存下来的原始文件重新算出来。

## 数值验证

- **烘焙核心（C#，真机可执行）**：`.codex-research/gradient_map_sim/bake_core_check` 是一个 `dotnet` 控制台工程，用 `<Compile Include>` 直接链接**出货的那份 C# 源文件**（不是复制），共 11 项检查：
  - 黑→白渐变的恒等（0.0）、越界/未排序时间键的夹取与排序、重复时间键不产生 NaN、无键回落不透明白、单键铺满整条色标、`Fixed` 台阶位置、透明度键独立插值（解析期望值误差 0）、Oklab 白点与逆变换、**与独立双精度参考实现的逐点比对**（60 组随机渐变 × 256 采样，最大误差 2.9e-6），
  - **色标保真度**：从上表可以看出 8 键明显优于 4 键，弧长取键把最大偏差又压低约 2 倍，
  - **C# 与 JS 复刻逐值一致**（16 个 look，最大误差 1.4e-6）。
  - 负对照（`dotnet run -- --negative-control`）会破坏独立参考矩阵与 JS 侧的一个采样值，两项检查立刻 FAIL（9.0e-3 / 1.0e-2），证明检查不是恒绿。
- **渲染路径（JS）**：`.codex-research/gradient_map_sim/gradient_map_check.js` 共 12 项，跑在烘焙 + shader 的 1:1 复刻上：默认色标恒等（1.1e-16）、输入窗口截断/重映射、反转镜像、色阶数 6 恰好 6 级（0/0.2/…/1）、重复键的台阶（Blend 一个纹素宽、Fixed 精确）、退化窗口阈值、alpha 0 键不动画面、Fixed/Blend 的采样差异、明度混合、**shader 结构漂移检查**（必须引用 `_LayerRampTex` 与 `HoSampleGradient`、保留亮度权重、不得再出现 Oklab 常量）、16 个 look 在 5 探针 × 8 组参数下有限、`inputs.json` 与 `looks.js` 同步。
- **色表溯源**：`.codex-research/gradient_map_sim/palette_looks_check.js` 用 `palettes.js` 的解析器从原始文件重算 5 个色表 look 的全部键（颜色逐通道 1e-9、位置与索引一致）；负对照（把"光谱"第 2 个键改 1/255）会 FAIL。研究目录自己的 `verify_palette.js` 另外做了 22 项溯源检查（含 15 个源文件的 SHA-256、turbo 浮点表与字节表 768/768 通道一致、ironbow 单调性与蓝通道升降形状）。
- **C# 预设表**：`.codex-research/gradient_map_sim/verify_csharp.js` 检查"重新生成能逐字节复现"以及把 `new GradientMapLook(...)` 与 `MakeGradientMapRamp(...)` 解析回来逐字段比对（含每个颜色键与透明度键、按 `System.Single` 精度）；把一个颜色键改 1 ULP 就会 FAIL（已实测）。
- **HLSL 编译**：`.codex-research/shader-check/` 用 Unity 自带的 `D3DCompiler_47.dll` 以 `ps_5_0` 编译 `FragGradientMap` **通过**（该脚本会把包内 include 内联进来，所以 `HoSampleGradient` 也是真的被编译到的）。
- **C# 编译**：独立 Roslyn 编译 Editor + Runtime 两个程序集，**0 error**（只剩仓库里原有的 11 条 CS0649 警告）。

  > 这一轮里检查抓到过两个真问题：半纹素偏移（恒等检查 1.9e-3 → 修正后 1.1e-16），以及更早的"色表 look 的三元组颜色缺 alpha"（当时导出的预览图整片黑）；后者现在被"所有 look 有限且合法"这一项挡住，并有负对照。

## 如何新增/修改一个 look

1. 改 `.codex-research/gradient_map_sim/looks.js`：`keys: [{ at, color, alpha? }]`（≤8 个，`color` 可以写 sRGB hex 或精确浮点三元组），以及 `mode` / `space` / `input` / `window` / `bands` / `reverse` / `dither` / `blend`。
2. `cd .codex-research/gradient_map_sim` 后 `node gen_csharp.js` → 覆盖 `Editor/PostProcessing/ImageProcess/ImageProcessStackVolumeEditor.GradientMapLooks.cs`。
3. `node verify_csharp.js`、`node gradient_map_check.js`、`node palette_looks_check.js`、`node dump_inputs.js`（后者给 C# 检查提供输入），然后 `cd bake_core_check && dotnet run`。
4. 想更新文档图就 `node sheet.js`。
5. 色表型 look 的键要从原始色表取样：`node palette_looks_check.js --print` 可以打印各色表的 4/8 键弧长采样（含索引），改完用 `node resample_looks.js` 统一重采样也行。

改 UI 控件时注意：`Filters/GradientMap.cs` 里 `GradientMapFixedBodyLineCount`（固定 9 行）+ 渐变编辑器自身占的行数（用 `EditorGUI.GetPropertyHeight` 动态算）必须等于 `DrawGradientMapElement` 实际绘制的行数，否则层列表的行高会错位。

## 尚未验证 / 已知限制

- **没有 Unity 实机画面**：C# 烘焙核心是**真的执行过**的（dotnet 工程 11 项检查），但"参数 → 画面"的其余部分（贴图绑定、RenderGraph 路径、Inspector 布局）只做了静态编译与结构检查。需要在实机确认：
  1. 控制台没有 shader 编译错误，层正常显示；
  2. 渐变编辑器占几行、层列表行高是否与 `GetPropertyHeight` 一致（这是唯一需要目测布局的地方）；
  3. 面板控件顺序：渐变编辑器（无标题，占它自己需要的行数）→ 输入 → 黑场 → 白场 → 插值空间 → 色阶数 → 反转 → 抖动 → 混合模式；
  4. 5 组 16 个预设的观感，以及"海报平涂"的 6 级平涂在 8-bit 目标上是否需要补抖动；
  5. `Fixed` 模式的台阶位置与 Unity 编辑器渐变条是否一致；
  6. 运行时 ramp 贴图在 RenderGraph 路径下正常绑定（Material 绑定路径与兼容路径共用 `ApplyLayerRamp`），以及切换图层效果后贴图会被回收。
- **JS 复刻只覆盖 look 用到的混合模式**（`正常`、`明度`）——其余 22 种混合模式在 shader 里是从 `Gradient.shader` 复制的同一份实现，但没有针对本效果再跑一遍数值回归。
- **预览图是复刻渲染，不是 Unity 截图**，也没有模拟抖动（抖动是 ±0.5/255 的噪声）。
- **色标上限 8 键**（Unity `Gradient` 的限制）；键上限内可以随意加键，超过就得改用贴图/自定义效果。
- **`Fixed` 模式的语义**按"键的颜色保持到下一个键"实现，未与 native `Gradient.Evaluate` 比对。
- **烘焙时机**：贴图在主线程的运行时层构建阶段烘焙（`ImageProcessRuntimeLayerBuilder`），按渐变内容 + 插值空间做哈希，内容不变就不重烘；改渐变会销毁旧贴图并重烘一张。

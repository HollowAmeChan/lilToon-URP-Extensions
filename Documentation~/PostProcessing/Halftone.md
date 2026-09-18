# 网点（Halftone）— 规划稿（未实现）

> 状态：**只规划，没有代码**。本文确定名字、定位、参数与验证方式；实现按 §8 的清单走。

## 0. 名字：`Halftone` / 中文标签「网点」

| 候选 | 结论 |
| --- | --- |
| **`Halftone` + 「网点」**（推荐） | 枚举名用行业通用词（与 `DitheringCustom`、`BlueNoise` 同类命名），中文标签短、和面板里既有的「集中线」「调色」「颗粒」一个风格；「网点」在中文语境里就是指印刷/漫画的半调网点 |
| 「漫画网点」 | 更明确，但把效果框死在漫画；这个效果也能做波普、丝网印、报纸 |
| 「半调」/「半调网点」 | **不建议单独叫「半调」**：`SkyTyndall` 的「抖动模式」里已经有一项叫「半调」（`Editor/PostProcessing/ScreenProcess/Filters/SkyTyndall.cs:18`，shader 侧是 `SkyTyndall.shader:196-200` 的光线点阵增益）。面板里出现两个不同含义的「半调」会分不清 |
| `ScreenTone` / `ComicDots` | 语义偏窄；`ScreenTone` 易和 ScreenProcess 混 |

名字同时决定 shader 文件名：`ImageProcessEffectDescriptor` 的 shader 名是按枚举名拼的（`ImageProcessEffectDescriptor.cs:15,49-70`），所以枚举定成 `Halftone`，shader 就是 `Runtime/ImageProcess/Shaders/ImageProcess/Halftone.shader`。

## 1. 它和包内既有"点点"效果的分工（避免重复造）

| 效果 | 现在做什么 | 和网点的区别 |
| --- | --- | --- |
| `DitheringCustom`（视频游戏） | 阈值来自噪声/贴图（`DitheringCustom.shader:48-60`：`Hash12` 或 `_LayerTexture`），目的是**减少色阶**（`Quantize` + 阴影/中间调/高光三段上色） | 它的图案是随机/贴图噪声，不是几何网屏；它关心"量化到几阶"，不关心"一个单元里印多少墨" |
| `BlueNoise`（蓝噪色块） | 单元马赛克：按单元取色 + 玻璃/海报化 + 边线（`BlueNoise.shader:44-163`） | 空间马赛克，没有覆盖率/墨量概念，点的大小和亮度无关 |
| `SkyTyndall` 的「半调」 | 光线上的点阵增益（`SkyTyndall.shader:196-200`） | 只是丁达尔光线的图案修饰 |
| `ToonMap`（色调映射） | Neutral/ACES 色调映射（`ToonMap.shader:26-41`） | 与图案无关 |
| `GradientMap`（渐变映射） | 用 ramp 决定**是什么颜色** | 网点决定**每个单元印多少墨**（覆盖率）。两者正交互补，见 §2 |

结论：几何网屏（拜耳有序抖动 + AM 网点）在包内还没有，是一个独立效果。

## 2. 和 `GradientMap`（ramp）的配合 —— 这是这个效果的重点

**顺序就是图层列表顺序**：运行时按 `runtimeLayers` 的列表顺序逐层执行（`ImageProcessPass.cs:163,239`），而新增图层是**追加到末尾**（`ImageProcessStackVolumeEditor.cs:872`）。所以"先点渐变映射、再点网点"天然就是 ramp → 网点 的正确顺序，也随时可以拖动调整。

分工：**ramp 管颜色，网点管墨量**。

- `GradientMap` 的「色阶数」把画面压成有限个平涂色阶（`GradientMap.shader:208-213`）；
- 网点再把每个色阶变成一组大小固定的点 → 就是漫画那种"阴影是一层一层网点"的观感。

两种典型用法：

| 用法 | 图层链 | 说明 |
| --- | --- | --- |
| 彩色漫画 | 渐变映射（定色板，色阶 4~6）→ 网点（**遮罩原色**模式） | 颜色来自 ramp，网点只在画面里压出印刷质感，不换色 |
| 黑白漫画 / 波普海报 | 渐变映射（黑白两点 ramp 或直接不用）→ 网点（**双色**模式，墨=黑、纸=白） | 网点用两种颜色重画画面；此时 ramp 是可选的 |

**网点单独也能用**：不依赖 ramp，靠自己的「输入亮度 + 黑场/白场 + 覆盖率曲线」就能出图。这是必须保证的（用户要求"单走也能使用"）。

## 3. 「背后要不要垫一个颜色」——需要，但只有一个槽是必填的

网点的画面由两种颜色构成，但**不强制**都暴露：

- **墨色**（点本身的颜色）= 复用图层通用的 `_LayerColor`（`ImageProcessLayer.cs:36`），面板上就是图层已有的「颜色」那一行；
- **纸色**（点以外的底色）= 一个参数槽，默认白。

然后用「合成方式」决定纸色到底怎么参与 —— 这就是"要不要垫色"的答案：

| 合成方式 | 公式（`c` 覆盖率、`img` 原图） | 用途 |
| --- | --- | --- |
| **遮罩原色**（默认，单走最稳） | `img * lerp(纸色, 1, c)` | 保留原图颜色，只在暗部压出网点；纸色只作为"未上墨处的压暗程度"，设成白就是纯遮罩 |
| **双色替换** | `lerp(纸色, 墨色, c)` | 经典波普/黑白漫画，画面只剩两种颜色 |
| **加墨** | `lerp(img, 墨色, c)` | 丝网印：墨色叠在照片上（彩色分色也走这个） |
| **乘算** | `img * lerp(纸色, 墨色, c)` | 印刷叠印感，保留原图明暗层次 |

## 4. 模式（v1 建议 5 种）

| 模式 | 数学 | 观感 |
| --- | --- | --- |
| **拜耳渐变** | 有序抖动：`c = step(B[i,j], tone)`，`B` 是 2×2 / 3×3 / 4×4 / 8×8 拜耳矩阵（0..n²-1 归一化） | 复古渐变抖动（用户说的"拜耳图案这种渐变"），放大能看到棋盘状阈值坡 |
| **圆点** | AM 网屏：单元内 `d = length(frac(p) - 0.5)`，半径 `r = 0.5 * sqrt(tone)`（**面积正比于 tone**） | 标准印刷网点，50% 时相邻点相切 |
| **方点** | `d = max(|frac(p).x - 0.5|, |frac(p).y - 0.5|)`，边长 `= sqrt(tone)` | 波普/丝网，50% 时连成方格 |
| **菱形（椭圆）** | `d = |dx| + |dy|`（菱形）或 `length(float2(dx * k, dy))`（长圆） | 50% 时点连成链，传统印刷的"玫瑰斑"来源 |
| **线条** | `c = step(frac(p.y), tone)`（可调角度 → 斜线排线） | 铜版画/木刻排线 |

共用的东西：**输入源**（亮度/R/G/B/最大值/平均/饱和度，与 `GradientMap` 同一套编码）、**黑场/白场**、**反相**、**单元大小**（像素或 LPI）、**网屏角度**、**形状柔化**（抗锯齿）、**单元错位抖动**、**输出抖动**、**强度上限**。

不在 v1 里、但记下来的：

- **分色**（RGB / CMYK 各自一个角度的网屏叠加，印刷套色的摩尔纹是特色）→ 见 §8 的问题 3；
- **误差扩散**（Floyd–Steinberg 那一类）—— 属于另一个家族，且会破坏"规则网点"的印刷感，不在本效果里；
- **用 ramp 做覆盖率曲线**（网点自己的 `ramp` 字段当 transfer curve，做网点扩大补偿）→ 见 §8 的问题 4。

## 5. 参数槽位（草案）

`ImageProcessLayer` 有 `parameters0..12` 十三个 Vector4（`ImageProcessLayer.cs:69-107`），预算很宽，先按用途分组：

| 槽 | 内容 |
| --- | --- |
| `color` | 墨色 |
| p0 | x 模式, y 输入源, z 黑场, w 白场 |
| p1 | x 单元大小(px), y 网屏角度, z 形状柔化(抗锯齿), w 单元错位抖动 |
| p2 | xyz 纸色, w 反相 |
| p3 | x 合成方式, y 浓度上限, z 拜耳矩阵尺寸(0=2×2,1=3×3,2=4×4,3=8×8), w 拜耳单元缩放 |
| p4 | x 长圆比例(菱形/椭圆用), y 线条粗细比, z 输出抖动, w 随机种子（错位用） |

## 6. 数学不变量（为验证做准备，§7 会用上）

1. **面积律**：圆点半径 `0.5*sqrt(tone)`、方点边长 `sqrt(tone)`、线条宽度 `tone` ⇒ 单元内平均覆盖率 = `tone`（解析成立；实现里再用数值积分复核，容差 1%）；
2. **单调性**：`tone` 增大覆盖率不减；`tone ≤ 0` ⇒ 全覆盖纸色，`tone ≥ 1` ⇒ 满格（圆点在 100% 时半径刚好 0.5 相切，需要 `1.02` 系数或 clamp 才能真的盖满，实现与检查都要写死这个约定）；
3. **拜耳矩阵**：生成的 `n×n` 矩阵必须是 `0..n²-1` 的**排列**（双射，每个阈值恰好出现一次）—— 这是最容易写错又最容易检查的一条；
4. **角度无关**：旋转网屏角度后，单元内平均覆盖率不变（各向同性）；
5. **抗锯齿**：覆盖率对屏幕像素的过渡宽度必须 ≥1 像素，否则高频网点在运动时会爬行/闪烁（预设的单元大小要落在 4~12 px @1080p 这个安全区）。

## 7. 验证计划（照仓库既有做法）

| 检查 | 内容 |
| --- | --- |
| `.codex-research/halftone_sim/halftone_math_check`（dotnet，链接出货的纯 C# 核心） | 面积律（解析 + 数值积分）、单调性、端点、拜耳矩阵双射与四种尺寸、角度不变性、四种合成方式在 `c=0/1` 的边界、负对照（改坏 3 处期望值必须 FAIL） |
| `.codex-research/halftone_sim/*.js` | 拜耳矩阵与 **Kino `Recolor.cs:138-187`** 的 2×2/3×3/4×4/8×8 表交叉验证（第二来源）；出 PNG 对比图（拜耳 5 阶渐变 / 圆点 5 阶 / 方点 / 线条），肉眼确认观感 |
| `.codex-research/shader-check/check_all.ps1` | 把 `Halftone` 加进扫描列表（现在 10 个 shader 全过），HLSL `ps_5_0` 编译通过 + 负对照 |
| UI 行数检查 | 照 `depth_fog_sim/check_screenprocess_fog_ui.js` 写一个 ImageProcess 版：`GetHalftoneLineCount` 的声明行数 == 绘制函数实际增量 + 收尾行，分支条件集合一致，负对照会 FAIL |
| Roslyn 独立编译 | 0 error（仅剩仓库原有 11 条 CS0649 警告） |

## 8. 已拍板（2026-09-15）

1. **名字**：`Halftone` + 「网点」。
2. **默认垫色行为**：默认「遮罩原色」（单走不换色、最不容易吃坏画面），「双色替换」交给预设。
3. **v1 不做分色**：只有五种单网屏模式（拜耳 / 圆点 / 方点 / 菱形 / 线条），RGB/CMYK 多角度网屏留到之后单独加。
4. **先抽共享 include**：把 24 种混合模式从 `Gradient.shader` / `GradientMap.shader` / `LayerBlit.shader` 抽成 `ImageProcessBlend.hlsl`，新 shader 直接用，不再产生第 4 份拷贝；抽完用"文本块与 HEAD 版逐字节相同 + `check_all.ps1` 四个 shader 编译通过"作为等价证明。

## 9. 实现接入点清单（照 `Documentation~/PostProcessing/README.md` 的「新增效果接入规则」）

1. `ImageProcessEffect` 追加 `Halftone`（排在 `GradientMap` 之后，**不要插在中间**）；
2. `ImageProcessEffectDescriptor`：`Add(catalog, SinglePass(ImageProcessEffect.Halftone));`
3. `Runtime/ImageProcess/Renderer/Effects/ImageProcessPass.Halftone.cs`（`ApplyHalftoneLayer` + `RecordHalftoneLayer`）；
4. `ImageProcessPass.EffectDispatch.cs` 注册（照 `:172` 那行）；
5. `Shaders/ImageProcess/Halftone.shader`；
6. `Runtime/ImageProcess/Shaders/ImageProcess/HalftoneMath.hlsl`（若抽共享数学）+ 纯 C# 核心 `Runtime/ImageProcess/Renderer/Halftone/ImageProcessHalftoneMath.cs`（与 shader 一一对应，供 dotnet 检查直接链接）；
7. `Editor/PostProcessing/ImageProcess/Filters/Halftone.cs`（参数 UI + `GetHalftoneLineCount`）；
8. `ImageProcessStackVolumeEditor.cs` 四处：`VisibleEffectOrder`（图标）、`EffectDisplayNames`（**按枚举顺序、新值追加末尾**）、绘制分发、`GetElementLineCount` 分支，以及 `ResetEffectDefaults`；
9. `ImageProcessStackVolumeEditor.Presets.cs` 挂入口 + 新建 `ImageProcessStackVolumeEditor.HalftonePresets.cs`（预设 5 组，含一组「配合渐变映射」）；
10. 新文件手写 `.meta`：UTF-8 **无 BOM**、LF、32 位唯一 GUID；
11. 跑 §7 的全部检查，更新 `Documentation~/PostProcessing/README.md`（效果清单加一条 + 一个 `Halftone.md` 实现说明）。

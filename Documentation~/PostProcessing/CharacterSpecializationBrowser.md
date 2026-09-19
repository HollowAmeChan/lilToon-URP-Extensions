# 角色特化后处理 UI（与 ImageProcess / ScreenProcess 对齐）— 规划稿（未实现）

> 三块后处理语义相同，UI 与配置模型都应当一致：同一套 **搜索栏 + 左侧图标侧栏 + 右侧内容**、
> 同一套无底控件与行样式、同样的"要用才开、不开就关"的配置方式。
> 本文只规划；`EffectBrowser.md` 是那套外壳的说明。

## 0. 现状

### 0.1 没有图层，也没有顺序

| | ImageProcess / ScreenProcess | 角色特化 |
| --- | --- | --- |
| 右侧内容 | 图层列表：可增删、可拖排序 | **固定的 5 个区段**，顺序写死、不可增删不可排序 |
| 有"层"吗 | 有 | **没有**（没有数组、没有顺序、也没有材质/纹理覆盖） |
| 每个效果的开关 | 图层的 `enabled` | 一个 `*Enabled` bool |
| 参数规模 | 每层十几到二十行 | 5 段共 **约 69 行**（眼透 13 / 前发投影 12 / 前发漫反射 9 / 主体描边 20 / 增强描边 15）+ 5 条 `HelpBox` |
| 编辑器 | 已经换成浏览器 | `HoCharacterSpecializationVolumeEditor.cs`（255 行）：HelpBox + 一行「抗锯齿宽度」+ 5 个区段 |
| 折叠状态 | 每实例 | 每个区段一个 **`private static bool`** ⇒ 两个 Inspector 会串台 |

所以这块借的是**外壳**（搜索 + 图标定位 + 统一行样式），两侧语义按"开关 + 定位"来：
侧栏不是素材库（没东西可加），右侧也不是图层列表（没有层、没有顺序）。

### 0.2 配置模型现在是"保底 + 假覆盖"（要改的核心）

现在有两份**重复**的效果参数：

| 位置 | 形态 | 作用 |
| --- | --- | --- |
| `HoCharacterSpecializationSettings`（RendererFeature 的 Settings 资产） | 69 个**普通字段**（`eyeRevealStrength = 0.05f` …） | 名义上是"保底默认值" |
| `HoCharacterSpecializationVolume`（Volume 组件） | 69 个 **`VolumeParameter`**，构造函数里逐个 `overrideState = true` | 名义上是"逐参数覆盖" |

运行时（`HoCharacterSpecializationRendererFeature.cs:184-196`）：

```csharp
runtimeSettings.CopyFrom(settings);        // 先用 Settings 资产当底
volume.ApplyTo(runtimeSettings);           // 再让 Volume 覆盖上去
return runtimeSettings;
```

而 `HoCharacterSpecializationVolume.ApplyTo()`（`:452` 起，69 行一一赋值）**无条件复制每个参数**：

```csharp
target.eyeRevealEnabled = EyeRevealEnabled.value;   // 没有 if (overrideState) 判断
target.eyeRevealStrength = EyeRevealStrength.value;
…
```

⇒ 结论（这就是你觉得别扭的地方，而且比"别扭"更糟）：

1. **Settings 资产里那 69 个效果字段永远会被 Volume 覆盖**，所谓"保底"根本不生效（每帧都被冲掉）；
2. **Volume 上那 69 个 override 勾选是假的**——取消勾选不会让"保底值"透出来，因为 `ApplyTo` 根本不看
   `overrideState`；面板上呈现的"继承/覆盖"两态与运行时行为不一致；
3. 于是同一份参数在两处各自维护，改一边没用，UI 上还多出 69 个无意义的勾选框。

**这块要的就是"用的时候开、不用就关"，不要保底、也不要逐参数覆盖。**

### 0.3 顺带发现的死字段

Volume 里的 `LayerMask`/`MinRenderQueue`/`MaxRenderQueue`/`PassEvent`/`RenderScale` 也没有任何读取点
（运行时只读 `settings.*`），`Enable`/`ShowInSceneView` 同样无人读（启用走 `VolumeComponent.active`）。

## 1. 目标：单一来源 + 无覆盖（方案 A）

采用**你在后处理那两块看到的模式**：

| | 现在 | 改成 |
| --- | --- | --- |
| 效果参数的唯一来源 | Volume（但名义上是"覆盖层"） | **Volume**（就是唯一来源，没有底） |
| Settings 资产 | 一份永远被覆盖的重复字段 | **只留管线/捕获/调试**（`passEvent`、`renderScale`、`layerMask`、队列、`debugMode`…），删掉 69 个效果字段 |
| 运行时 | `CopyFrom(settings)` + `volume.ApplyTo(...)` | 直接把 Volume 的配置交给 pass（`ResolveSettings` 只做"有没有、能不能用"的判断） |
| 参数形态 | 69 个 `VolumeParameter` + 假 override 勾选 | **普通 `[Serializable]` 字段**（一个效果一个设置块），Inspector 上是普通控件、**没有任何 override 勾选** |
| 语义 | "保底 + 逐参数覆盖、可跨 Volume 混合" | "最近的那个 Volume 说了算"（与 IP/SP 完全一致），要用就开、不用就关 |
| `*Enabled` | BoolParameter（且被强制 override） | 普通 bool，开关就是开关 |

- **能保住现有效果的原因**：字段名不变（`eyeRevealStrength` → 同一个名字），换的是承载方式和 UI；
  参数值在 Volume profile 里是按名字序列化的，保持名字就能保住用户已经调好的数值。
- **会失去的能力**（明确记下来）：跨 Volume 的逐参数混合/继承。对"一组开关 + 一组参数"的效果来说
  这是好事——现在这套混合本来也没生效（§0.2），而 IP/SP 同样是"最近 Volume 整体生效"。
- **会删掉的东西**：`ApplyTo`（69 行）、Settings 里 69 个效果字段、`CopyFrom` 里对应行、
  Feature 编辑器里 5 个效果区段（参数挪回 Volume 后它们没东西可画）、Volume 里 5 个死字段。

## 2. UI：与另外两块一致

```
┌──────────────────────────────────────────────────────────────┐
│ [⊞] 🔍 [搜索效果（中文名或枚举名）………………]              ×   │  搜索栏（同 IP/SP）
├───────────────┬──────────────────────────────────────────────┤
│ ◀    1/1    ▶ │  ▶ 眼透                    开 0.85  [☑] [×]  │  区段行（同 IP/SP 的皮，无 override 勾选）
│ ┌──┐┌──┐┌──┐  │    └ 展开后：原来的参数行                        │
│ │  ││  ││  │  │  ▶ 前发投影                开 0.65  [☑] [×]  │
│ ├──┤├──┤├──┤  │  …                                            │
│ 眼透 前发 漫反 │                                               │
│ （3 列 × 6）  │                                               │
└───────────────┴──────────────────────────────────────────────┘
```

- 侧栏 **6 条**：眼透 / 前发投影 / 前发漫反射 / 主体描边 / 增强描边 + 「语义遮罩抗锯齿」；
  图标全部用现成资源（`icon_Glow_SelectColor_v1`、`icon_DropShadow_v1`、`icon_Blur_v1`、
  `icon_OutLine_v1`、`icon_RimLight_v1`、`icon_Settings_v1`）。
- 侧栏 **6 行**（图标档 3 列 × 6 = 18 格、名字档 1 列 × 6 = 6 格，两档同高 168px、不用翻页）。
  这需要给 `EffectBrowserCatalog` 加**一个可选的 `Rows`**（IP/SP 不传，保持 3×20）——本次唯一动共享代码的地方。
- 交互（与 IP/SP 同一套）：左键点图标 = 开/关该效果（绿色 = 已启用）；右键菜单 = 启用/停用、
  恢复默认、展开该区段、清空搜索；搜索只过滤侧栏，右侧区段标题行高亮；`Esc` 清空；
  悬停页码看「侧栏命中 n · 已启用 m 个效果」。
- 右侧区段行：`▶ 名字  摘要（开 0.85 / 关）  [启用 ☑]  [×]`，**没有 override 勾选**；
  `×` = 关掉该效果；"恢复默认"在右键菜单里；每段那条 `HelpBox` 收进标题行 tooltip；
  折叠状态改 `SessionState` 每实例；顺序写死、不提供调整入口。

## 3. 两边的 RDG 实现差在哪（为什么 UI 语义不同）

| | ImageProcess / ScreenProcess | 角色特化 |
| --- | --- | --- |
| RDG 形态 | **相机颜色上的 blit 链**：每个图层一趟 pass，ping-pong 两张 RT，`AddBlitPass`/`AddRasterRenderPass` | **固定的多 pass DAG**：捕获（脸/眼 RT）→ 各效果的 Source pass → 一趟 Composite（`RecordRenderGraph` `:486` 起，5 组 `AddRasterRenderPass`） |
| 驱动 | 有序的**图层列表**（顺序=执行顺序，可拖） | 写死的顺序 + 每效果的开关；**没有顺序概念** |
| 每单元的资源 | 每层的 shader/material/纹理/参数 | 没有"每层资源"；共用 Feature 的材质与 RT 池（`renderScale` 决定分辨率） |
| 兼容路径 | 有（`Execute`） | 也有（`Execute` `:414`） |
| 配置来源 | Volume 里的图层列表（普通字段） | Settings 资产 + Volume 覆盖（§0.2，要改成 Volume 单一来源） |
| 观感差异 | 效果是"叠在画面上的层" | 效果是"一组开关 + 每个开关一组参数" |

⇒ 所以这块的 UI **不该**有"素材库/图层列表"的语义，只借外壳：侧栏 = 开关与定位，右侧 = 固定区段。
（顺带说明：它**并不是**没有 RDG，只是 RDG 的形态是固定 DAG 而不是可叠的链。）

## 4. 代码风格对齐（本次主要工作量）

| 现在 | 改成 |
| --- | --- |
| 5 个静态类各带一份 `DrawVolume(13 个参数…)`，编辑器逐个调用 | 编辑器持有一张 **6 条的 `EffectBrowserEntry` 目录**（枚举名 = 参数前缀，中文标签与图标见 §2），右侧由各区段画参数行，**标题行统一由编辑器画**（`DrawSectionRow(效果, 展开状态)`） |
| 折叠状态 `private static bool` | `SessionState` 每实例 |
| 没有图标/调色板 | 与 IP/SP 同款的目录表 |
| 无底控件各写各的 | 统一用 `EffectBrowserView.DrawChromeLessButton` |
| 行高/边距各写各的 | 与 IP/SP 相同的行高常量与 `LineSpacing` 习惯 |
| 文件组织 | 与 IP/SP 相同：`…VolumeEditor.cs`（主）+ `…Browser.cs`（目录与回调），5 个区段文件只负责参数行 |

## 5. 验证

| 检查 | 内容 |
| --- | --- |
| 新增 `.codex-research/character_specialization_sim/check_cs_browser_ui.js`（照 `check_browser_ui.js`） | 6 个条目与 6 组参数一一对应；图标文件存在（精确大小写）；目录声明 6 行并与绘制一致；标题行的「启用」写的是对应字段；`×` 只关效果、不碰其它参数；「恢复默认」清的是效果自己的字段；折叠状态不再是 `static`；搜索只过滤侧栏；不得出现 `miniButton`/`GUI.Button`；**不得再出现 `overrideState`**（这是本次的核心不变量）；负对照若干条全部生效 |
| Runtime 侧检查 | `ApplyTo` / Settings 的 69 个效果字段 / `CopyFrom` 对应行都应消失（用 grep 断言 + 结构检查钉住）；`ResolveSettings` 只做 gating；`IsActive`/`IsActiveForCamera` 用普通字段实现 |
| 既有检查必须继续通过 | `check_compile.ps1`（Roslyn 0 error）、`effect_browser_sim/check_browser_ui.js`（8/8 负对照）、`browser_search_check`（dotnet 100 项，共享代码加了可选 `Rows` 后要复跑） |
| 实机清单 | Volume profile 里**已有数值是否保住**（字段名不变 ⇒ 应保住）；同一个场景两个 Volume 的表现（最近者整体生效）；关闭 Volume 组件后效果完全停（无残留 RT/无 pass）；侧栏开关与右侧「启用」双向同步；亮/暗主题 |

## 6. 已定取舍

1. **方案 A：Volume 是效果参数的唯一来源**，删掉 Settings 的重复字段与 `ApplyTo`，不再有保底与逐参数覆盖；
2. 侧栏 6 条（含「语义遮罩抗锯齿」）；
3. 区段行 `×` = **关掉效果**（不是恢复默认）；
4. 共享代码只加 **一个可选 `Rows`**，其余一律不动；
5. 死字段（§0.3）随本次一起删；
6. RendererFeature 编辑器：效果区段挪走后只剩管线/捕获/调试，**这次不接浏览器**；
7. CS 预设（两段描边等）不在本次。

**备选方案 B（记录用）**：效果参数只留在 Settings 资产（Feature 级），Volume 退化成纯开关。
运行时最省事、也没有重复，但**同一个项目内无法按场景/角色分别调参**——与"跟后处理一样、用的时候才开"
的诉求不符，所以不采用；如果以后确定 CS 参数就是全局一套，再切到 B 也很容易（字段名一致）。

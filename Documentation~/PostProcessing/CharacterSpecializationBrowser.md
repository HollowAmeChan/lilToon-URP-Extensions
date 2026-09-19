# 角色特化后处理 UI（与 ImageProcess / ScreenProcess 对齐）— 规划稿（未实现）

> 三块后处理语义相同，UI 与代码风格应当一致：同一套 **搜索栏 + 左侧图标侧栏 + 右侧内容**、
> 同一套无底控件与行样式、同样的持久化习惯。本文只规划；`EffectBrowser.md` 是那套外壳的说明。
>
> 范围刻意收小：**不改浏览器的能力，只把角色特化接上去并对齐写法**。

## 0. 现状与差异（一句话版）

| | ImageProcess / ScreenProcess | 角色特化 |
| --- | --- | --- |
| 右侧内容 | 图层列表：可增删、可拖排序 | **固定的 5 个区段**，顺序写死、不可增删不可排序 |
| 有"层"吗 | 有 | **没有**（没有数组、没有顺序、也没有材质/纹理覆盖） |
| 每个效果的开关 | 图层的 `enabled` | 一个 `*Enabled` 的 `BoolParameter` |
| 参数规模 | 每层十几到二十行 | 5 个区段共 **约 69 行**（眼透 13、前发投影 12、前发漫反射 9、主体描边 20、增强描边 15）+ 5 条 `HelpBox` |
| 现在的编辑器 | 已经换成浏览器 | `HoCharacterSpecializationVolumeEditor.cs`（255 行）：HelpBox + 一行「抗锯齿宽度」+ 5 个区段，无图标无搜索 |
| 折叠状态 | 每实例 | 每个区段一个 **`private static bool`** ⇒ 两个 Inspector 同时开会互相串 |
| 图标 | 有调色板 | 一块也没有 |

所以这块**借的是外壳**：搜索 + 图标定位 + 统一的行样式；两侧语义按"开关 + 定位"来（没有"素材库"，
也没有"图层列表"）。

**顺带查到（不属本次范围）**：Volume 里的 `LayerMask`/`MinRenderQueue`/`MaxRenderQueue`/`PassEvent`/`RenderScale`
没有任何读取点，运行时只读 Settings 资产（`HoCharacterSpecializationRendererFeature.cs:1071/1102/1103/1111/1120`，
全是 `settings.*`），编辑器也没画它们 —— 迁移遗留，建议之后单独清理。

## 1. UI：与另外两块一致

```
┌──────────────────────────────────────────────────────────────┐
│ [⊞] 🔍 [搜索效果（中文名或枚举名）………………]              ×   │  搜索栏（同 IP/SP）
├───────────────┬──────────────────────────────────────────────┤
│ ◀    1/1    ▶ │  [override ✓] ▶ 眼透          开 0.85  [☑] [×]│  区段行（同 IP/SP 的皮）
│ ┌──┐┌──┐┌──┐  │               └ 展开后：原来的参数行          │
│ │  ││  ││  │  │  [override ✓] ▶ 前发投影      开 0.65  [☑] [×]│
│ ├──┤├──┤├──┤  │  …                                            │
│ 眼透 前发 漫反 │                                               │
│ （3 列 × 6）  │                                               │
└───────────────┴──────────────────────────────────────────────┘
```

- 侧栏 **6 条**：眼透 / 前发投影 / 前发漫反射 / 主体描边 / 增强描边 + 「语义遮罩抗锯齿」
  （现在孤零零挂在最上面的那一行，收进侧栏更整齐）。
- 图标全部用现成资源：`icon_Glow_SelectColor_v1`、`icon_DropShadow_v1`、`icon_Blur_v1`、
  `icon_OutLine_v1`、`icon_RimLight_v1`、`icon_Settings_v1`。
- 侧栏行数按这块的条目数走：**6 行**（图标档 3 列 × 6 = 18 格、名字档 1 列 × 6 = 6 格，两档同高 168px，
  都不需要翻页）。这需要给 `EffectBrowserCatalog` 加**一个可选的 `Rows`**（IP/SP 不传，保持 3×20）——
  这是本次唯一动到共享代码的地方。
- 交互（与 IP/SP 同一套）：左键点图标 = 开/关该效果（绿色 = 已启用）；右键菜单 = 启用/停用、
  恢复默认（清掉该效果的 override）、展开该区段、清空搜索；搜索只过滤侧栏，右侧区段标题行加高亮；
  `Esc` 清空、悬停页码看「侧栏命中 n · 已启用 m 个效果」。

## 2. 右侧区段：同一套行样式

每个区段一行，与 IP/SP 图层行同高同皮：

```
[override ✓]  ▶ 眼透            开 0.85      [启用 ☑] [×]
              └ 展开后：参数行（PropertyField + 中文标签，保持不变）
```

- 最左的勾是 **Volume 参数的 override 状态**，必须保留（它决定覆盖还是继承）；
- 中间是折叠标题 + 摘要（沿用 `FloatSummary` 的"开 0.85 / 关"，颜色条保留）；
- 「启用」放在标题行右侧：不展开就能开关，就是侧栏图标点的那个参数；
- `×` = **关掉该效果**（`*Enabled` 置 false、保留 override）；"恢复默认"在右键菜单里，两者分开；
- 每段现在那条 `HelpBox` 收进标题行的"?"tooltip，省掉 69 行里夹 5 条灰条；
- 折叠状态改成**每实例**（`SessionState`，按效果名），顺手修掉静态 bool 串台的问题；
- 顺序就是现在 `OnInspectorGUI` 的调用顺序，**不提供调整入口**。

## 3. 代码风格对齐（本次的主要工作量）

| 现在 | 改成 |
| --- | --- |
| 5 个静态类各带一份 `DrawVolume(13 个参数…)`，由编辑器逐个调用 | 编辑器持有 **6 条 `EffectBrowserEntry` 的目录**（枚举名用参数前缀，中文标签与图标见 §1），右侧仍由各区段绘制参数行，但**标题行统一由编辑器画**（`DrawSectionRow(enabled, 标题, 摘要, 展开状态)`） |
| 折叠状态 `private static bool` | `SessionState` 每实例 |
| 没有图标/调色板 | 与 IP/SP 同款：`new EffectBrowserEntry(值, "眼透", "EyeReveal", "icon_Glow_SelectColor_v1")` 式的一张表 |
| 无底控件各写各的 | 统一用 `EffectBrowserView.DrawChromeLessButton`（`×`、清空、翻页、样式开关） |
| 行高/边距各写各的 | 与 IP/SP 相同的行高常量与 `LineSpacing` 习惯 |
| 文件组织 | 与 IP/SP 相同：`Editor/CharacterSpecialization/` 下放 `…VolumeEditor.cs`（主）+ `…Browser.cs`（目录与回调），5 个区段文件保留但只负责"参数行" |

## 4. 验证（沿用现有两套检查的写法，规模小）

| 检查 | 内容 |
| --- | --- |
| 新增 `.codex-research/character_specialization_sim/check_cs_browser_ui.js`（照 `check_browser_ui.js`） | 6 个条目与 6 个参数一一对应；图标文件存在（精确大小写）；目录声明 6 行、与实际绘制一致；标题行的「启用」写的是对应 `*Enabled`；`×` 只关效果、不碰其它参数；恢复默认清的是 override；折叠状态不再是 `static`；搜索只过滤侧栏；不得出现 `miniButton`/`GUI.Button`；负对照若干条全部生效 |
| 既有检查必须继续通过 | `check_compile.ps1`（Roslyn 0 error）、`effect_browser_sim/check_browser_ui.js`（8/8 负对照）、`browser_search_check`（dotnet 100 项）——共享代码只加了一个可选 `Rows`，这三套都要复跑 |
| 实机清单 | Volume 的 override 勾选是否正确（继承/覆盖、多 Volume 叠加）；两个 Volume 同时打开折叠状态互不串；侧栏开关与右侧「启用」双向同步；亮/暗主题下的行与高亮；6 条侧栏在 3 列里的观感 |

## 5. 已定的取舍

1. 侧栏放 **6 条**（含「语义遮罩抗锯齿」）；
2. 区段行 `×` = **关掉效果**（保留 override），恢复默认放右键菜单；
3. 共享代码只加 **一个可选 `Rows`**，其余一律不动；
4. 死字段清理、RendererFeature 编辑器改造、CS 预设 —— **都不在本次**，之后单独提。

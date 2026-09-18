# 效果浏览器（后处理 UI 换代）

`ImageProcess` / `ScreenProcess` 两个 Volume 编辑器的顶部 UI：**搜索栏 + 左侧可翻页图标侧栏 + 右侧原有图层列表**，
两个编辑器共用同一份实现。旧的"图标墙"（按面板宽度自动流式换行、只能悬停认名、没有搜索）已经删掉。

## 0. 为什么换（现状量化）

| 事实 | 数据 |
| --- | --- |
| 效果数量 | ImageProcess 面板 **41 项**；`ImageProcessEffect` 枚举共 59 项，其余 18 项不上面板（4 个 `RemovedEffectSlot*` 旧序列化槽位 + 14 个遗留效果：`CustomMaterial`、`DownScaleResolution`、`GateWeave`、`LensDistortionCustom`、`MotionTrail`、`RGBBlur`、`SharpenAfter`、`RetroLookPro*Custom` ×4、`LED`、`CameraSwitcher`、`TransparentBackground`）。混合模式是另一个枚举（`ImageProcessBlendMode` 24 项），从来没有面板图标。ScreenProcess 8 项 |
| 图标 | 面板只用 **32 张**，5 张被多个效果共用：`icon_Flare_Ray_v1` 一张四个（集中线/天空神光/光斑变焦/镜头光晕）、`icon_ScreenEffects_v1` 三个、`icon_Distortion_v1` 三个、`icon_Grain_v1`、`icon_RGBSplit_v1` 各两个 |
| 资源目录 | `Editor/ImageProcessIcons/` 有 135 张 png，但剩下多是相机/UI 图标（`icon_CameraSwitchButton_01..07` 等），当效果图标没意义 ⇒ **一人一图做不到** |
| 旧交互 | 自动流式排布、无分组/分页/搜索；辨识只有 tooltip；移除的唯一手势是"再点一次同一个图标"，所以"关掉一个效果"必须先悬停找出它是哪一个 |
| 代码 | 两个编辑器各复制了一份 ~120 行面板代码，差别只有枚举类型 |

## 1. 布局

```
┌──────────────────────────────────────────────────────────────┐
│  🔍 [搜索效果（中文名或枚举名）]      侧栏命中 7 · 列表高亮 2  × │  搜索栏（整宽）
├───────────────┬──────────────────────────────────────────────┤
│ ◀  1/3  ▶     │  ┌ 图层列表（沿用原来的 ReorderableList）    │
│ [⊞][≣]        │  │ [✓] 渐变映射   强度 ▓▓▓▓░   [预设] [×]    │
│ ┌───┐┌───┐    │  │▌[✓] 网点       强度 ▓▓▓░░   [预设] [×]    │
│ │ 🌈││ 网│    │  │ [✓] 调色       强度 ▓▓░░░   [预设] [×]    │
│  …    …       │  └ …                                          │
│ （2 列 × 10） │                                              │
└───────────────┴──────────────────────────────────────────────┘
```

- 侧栏顶部工具条：`◀ 页码/总页数 ▶` + 两个样式按钮（`⊞` 纯图标 / `≣` 图标+名字），当前样式的按钮高亮。
- 样式 A（默认）：**纯图标 2 列 × 10 行 = 20 个/页**（41 个效果 → 3 页）。
  样式 B：**图标 + 名字 1 列 × 10 行 = 10 个/页**（→ 5 页）。
- 已在列表里的效果图标画成绿色；名字过长截断，tooltip 给 `标签 (枚举名)`。
- **可用宽度 < 320px 时侧栏折到列表上方**（`MinSplitWidth`），避免把右侧列表挤到没法用。
- 右侧列表仍是原来的 `ReorderableList`：行高（`GetElementLineCount`）、参数 UI、预设按钮、
  层级清理都没动，只是折叠行末尾多了一个 `×`。

## 2. 搜索

- **只匹配效果本身**：中文标签（`网点`）或枚举名（`Halftone`），子串匹配、大小写不敏感、忽略首尾空白；
  空查询 = 全部，并按目录顺序返回（不会因为查询而重排）。**不匹配预设名与拼音**（已拍板）。
- `Matches` 对**原始查询**也是安全的：内部按需归一化（先做几个字符的廉价判断，正常路径不分配），
  所以"只传空格"等于"全部匹配"、大小写会被折叠。这一点是 dotnet harness 先把"未归一化的查询会静默不匹配"
  这个坑标出来之后补上的——现在忘了调用 `Normalize` 也不会得到错误答案。
- **只过滤侧栏陈列**；右侧列表**不过滤、不重排**，命中效果的行加蓝底 + 左侧 2px 强调条。
- 搜索栏右侧显示 `侧栏命中 n · 列表高亮 m`；m = 0 时写"列表里没有"——顺带回答"我到底加没加过它"。
- 搜索时侧栏页码回到第 1 页并按命中集合重算；无命中显示"无匹配"。
- `Esc`（搜索框聚焦时）清空并失焦；`×` 按钮同样清空。`Ctrl/Cmd+F` 未做（见 §6）。
- 输入框空且未聚焦时画一行灰色占位提示（`EditorGUI.DisabledScope` + miniLabel）。

## 3. 关闭 / 移除效果

| 手势 | 行为 |
| --- | --- |
| 左键点侧栏图标 | 添加 / 移除该效果（绿色 = 已在列表里） |
| 右键点侧栏图标 | 菜单：第一行是 `标签 (枚举名)`（不必悬停），然后「添加为图层」或「移除该图层（n 个）」、「重置为默认参数」，搜索中再加一条「清空搜索」 |
| 图层行的 `×` | 只移除**这一行**（`RemoveLayerAt`），与 `RemoveLayer`（移除该效果的**全部**图层）区分开 |
| 图层行的启用勾选 | 保持不变（临时关闭但留参数） |
| 搜索 | 输入名字定位并移除，这是"完全关掉一个功能"最直接的路径 |

## 4. 实现

新增 `Editor/PostProcessing/EffectBrowser/`：

| 文件 | 职责 |
| --- | --- |
| `EffectBrowserEntry.cs` | 纯数据：`Effect`（int，枚举值）、`Label`、`EnumName`、`IconName`、`Tooltip` |
| `EffectBrowserSearch.cs` | **纯 C#、无 UnityEngine**：`Normalize` / `Matches` / `Filter` / `PageSize(20,10)` / `Columns` / `Rows` / `PageCount` / `ClampPage` / `PageRange` / `FormatPageLabel` / `FormatCounts` |
| `EffectBrowserState.cs` | 搜索文本 + 样式，按编辑器类型用 `SessionState` 持久化（`lilToon.EffectBrowser.<Key>.search` / `.iconOnly`）；页码只在会话内 |
| `EffectBrowserCatalog.cs` | 目录（主表 + 旧实现表）与四个回调 `IsPresent` / `Toggle` / `ResetToDefaults` / `LayerCount`，另有 `CountHighlightedLayers` 与 `LayerEffectMatches`（侧栏与列表高亮共用同一份匹配） |
| `EffectBrowserView.cs` | 画搜索栏、侧栏工具条、图标网格、翻页、右键菜单、窄宽度回退；`LoadIcon`（唯一图标入口，带缓存，先包路径后 `Assets/Editor/ImageProcessIcons`）；`CurrentQuery` 供列表高亮读取 |

两个编辑器的接入点（各约 130 行新代码，删掉约 120 行重复代码）：

- `EnsureEffectBrowser()`：惰性创建 state 与 catalog；
- `BuildBrowserEntries(EffectToggleEntry[])`：把**原有的调色板表**（`VisibleEffectOrder` / `LegacyEffectOrder`）
  转成浏览器条目，枚举名用 `System.Enum.GetName` 取——**永不手写，也就不会和枚举漂移**；
- `CountLayersForEffect` / `ResetEffectToDefaults` / `RemoveLayerAt` / `GetLayerArrayIndex` / `DrawLayerHighlight`；
- `OnInspectorGUI`：`场景视图开关 → EffectBrowserView.Draw(catalog, state, DrawLayerList)`；
- `DrawElement` 开头 `DrawLayerHighlight(rect, element)`；
- 折叠行末尾 `×` → `RemoveLayerAt(GetLayerArrayIndex(element))`（索引从 `propertyPath` 的 `Array.data[i]` 解析，避免改所有绘制函数的签名）。

保留 `EffectToggleEntry` 表本身：它是**作者手写的面板顺序与图标**，也是既有静态检查的锚点
（`effect_enum_check` 检查面板是否覆盖全部枚举成员、`check_halftone_ui` 检查 `new EffectToggleEntry(…)`）。

被删掉的重复代码（两个编辑器各一份）：`DrawEffectIconToggles`、`DrawEffectIconRow`、`DrawEffectIconButton`、
`GetEffectIconContent`、`EffectIconContents`、`EffectIconSize`、`EffectIconSpacing`、`LoadEffectIcon`。

## 5. 验证

先把**真实面板数据**跑了一遍分页与匹配（脚本直接读两个编辑器的调色板表）：

| 场景 | 结果 |
| --- | --- |
| ImageProcess 纯图标（20/页） | 41 项 → 3 页；第 3 页恰好 1 项（网点） |
| ImageProcess 图标+名字（10/页） | 41 项 → 5 页 |
| 搜「网点」（中文标签） | 1 项命中 |
| 搜「halftone」（枚举名，全小写） | 1 项命中（枚举名匹配大小写不敏感） |
| 搜「雾」在 ImageProcess | 0 项命中（深度雾属于 ScreenProcess，符合预期） |
| ScreenProcess 搜「光」 | 3 项命中（边缘光 / 后期打光 / 天光丁达尔） |

| 检查 | 内容 | 结果 |
| --- | --- | --- |
| `.codex-research/effect_browser_sim/browser_search_check`（dotnet，链接出货的 `EffectBrowserSearch.cs` + `EffectBrowserEntry.cs`） | 100 项：匹配（中文标签/枚举名/大小写/空白/无命中/空条目）、过滤顺序与复用缓冲区（同一 destination 连续两次查询不会累加）、每页 20/10、页数（0/20/21/40/41 → 1/1/2/2/3，样式 B → 1/2/4/5）、`ClampPage` 边界、各页 `PageRange`（41 项纯图标第 3 页 → start 40 / count 1）、`FormatPageLabel`/`FormatCounts`、以及 ceil/分区/值域等性质扫描 | **100/100 通过**；`--negative-control` 让 4 项 FAIL（页数用 floor、页码用 1-based、标签用 0-based、`Normalize` 先 trim 再小写） |
| `.codex-research/effect_browser_sim/check_browser_ui.js` | 六个检查组：两个编辑器的接线（`EnsureEffectBrowser` / `EffectBrowserView.Draw` / `DrawLayerHighlight` / `×`→`RemoveLayerAt(GetLayerArrayIndex(element))` / 8 个 helper 都在）、被删图标代码无残留、**51 个图标引用（37 个不同名）全部在 `Editor/ImageProcessIcons/` 里存在**（精确大小写比对）、`EffectBrowserSearch` 常量自洽（读出常量重算 2×10=20 / 1×10=10，并与类注释和两个样式按钮的 tooltip 交叉核对）、`EffectBrowserView` 的搜索/占位/清空/翻页/两档样式/右键菜单/窄宽度回退/`CurrentQuery`/每帧 `Save`、`Matches` 只读 `Label`+`EnumName`（并断言两个纯 C# 文件不引 UnityEngine） | **PASS，8/8 负对照全部生效**：删掉高亮调用、放回本地 `LoadEffectIcon`、把行数常量改成 8（重算出 16≠20）、图标名写错、`Matches` 读 `PresetName`、加第三个样式按钮、`×` 用错下标、清空按钮不清空 |
| 既有检查（必须继续通过） | `effect_enum_check/check_effect_enum_coverage.js`（面板覆盖全部枚举成员、六个 switch、registry→shader、无字面量夹枚举下标）、`halftone_sim/check_halftone_ui.js`（行数与 `EffectDisplayNames` 下标） | 两个都 exit 0 全过（前者含 4 个负对照） |
| Roslyn 独立编译（Editor + Runtime） | 0 error，仅剩仓库原有 11 条 CS0649 警告 | 通过 |

## 6. 尚未验证（实机清单）

1. 真实 Inspector 宽度下的观感：320px 折叠阈值、两档侧栏宽度（64 / 150px）是否合适；
2. 长标签截断与 tooltip；
3. 搜索框焦点行为与 `Esc`；`Ctrl/Cmd+F` 聚焦**没有做**（Inspector 里能否抢到需要实机试）；
4. 列表行高亮在 Unity **亮/暗主题**下与选中态、拖拽态底色是否打架；
5. `SessionState` 持久化的实际手感（切换 Inspector / 重开 Unity 后样式与搜索是否保留）；
6. 右键菜单在 Inspector 里的弹出位置；
7. 折叠到上方时的排布（窄面板下侧栏 20 格是否太占地方）。

## 7. 已拍板（2026-09-15）

1. **搜索范围：只搜效果名**（中文标签 + 枚举名）；不搜预设名与拼音。
2. **搜索只作用于左侧陈列，右侧列表零过滤**（当日修订）：列表一行不隐藏、顺序不动，命中行只加高亮；
   因此列表照旧走 `ReorderableList` 原路径，拖拽排序始终可用，高亮不参与任何布局计算。
3. **不做角标档**：只保留两档样式。代价是纯图标模式下共用图标的成员仍只能靠 tooltip 分辨，
   所以纯图标档定位为"我已经知道它在哪一页"的快速点击区，"找效果"交给搜索与"图标+名字"档。
4. **图层行只加 `×` 移除**，不加复制图层。
5. 窄面板：侧栏折到列表上方，不引入横向滚动。

## 8. 后续可做

- `Ctrl/Cmd+F` 聚焦搜索框（若 Inspector 允许）；
- 搜索命中高亮具体字符；
- 侧栏"最近使用"分组或置顶常用效果；
- 预设名参与搜索（本次明确不做）。

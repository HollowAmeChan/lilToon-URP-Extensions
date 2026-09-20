> **已过时（R6/R7）**：本文写作时 MetadataBuffer 还在。它已在 R6（摘槽）／R7（消费者换源 + 整块删除）中删掉：`maskId` 与自定义通道归 OB + AC，surface 族归 SB。当前架构以 `Documentation~/架构优化/Ho-*.md` 与 `CHANGELOG.md` 为准。

# 角色特化后处理 UI（与 ImageProcess / ScreenProcess 对齐）— 规划稿 + v1 落地记录

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
| 编辑器 | 已经换成浏览器 | `HoCharacterSpecializationVolumeEditor.cs`：5 个区段，没有全局行（语义位平面不再有开关） |
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

### v1 实现说明（已落地，2026-09-15）

| 改动 | 落点 |
| --- | --- |
| 新设置块 | `Runtime/CharacterSpecialization/HoCharacterSpecializationEffects.cs`：70 个**普通字段**（保留 `[InspectorName]`/`[Tooltip]`；`ClampedFloatParameter` 的区间变成 `[Range]`） |
| Volume 单一来源 | `HoCharacterSpecializationVolume.Effects = new HoCharacterSpecializationEffectsParameter(new HoCharacterSpecializationEffects(), true)`（`VolumeParameter<…>` 承载整块，Inspector 上是普通控件）；删掉构造里 58 行**逐参数**的假 `overrideState = true`、69 个参数、7 个死字段（`Enable`/`ShowInSceneView`/`LayerMask`/队列/`PassEvent`/`RenderScale`）。容器自己那**一个** override 必须留着 —— 漏掉它是 v1 的回归，见下面 §1.1 |
| 运行时 | `ApplyTo` → `CopyEffectsTo`（普通字段 → 运行时载体）；Feature 调用点同步改名 |
| Settings | 70 个效果字段标 `[NonSerialized]`（**不再进资产**），`CopyFrom` 只剩管线/捕获/调试 17 行 —— 它们现在只是**运行时载体**，不是第二份数据源 |
| 编辑器 | 5 个区段新增 `DrawEffects(SerializedProperty effects)`（只画参数行，由现有 `DrawSettings` 生成）；Volume 编辑器改用共享的「效果浏览器」（顶栏搜索 + 左侧 6 条图标侧栏 + 右侧区段行 + 搜索高亮 + 每实例折叠） |

### v1 复查修复（A/C，2026-09-15）

v1 落地后跑行覆盖检查发现两类问题，都已修：

- **A（功能回归）**：`DrawEffects` 是照着 `DrawSettings` 手写的，只抄了每个区段的一部分行 —— 眼透差 5 行、
  主体描边差 15 行、增强轮廓差 10 行，共 **30 个字段在面板上没有入口**（只能靠 Volume profile 或脚本改）。
  修法：把各区段的 `DrawVolume`（它本来就画全）**机械改造成 `DrawEffects`** —— 形参替换成
  `Find(effects, "<字段>")`（字段名取自重构前编辑器的 `OnEnable` 映射 + 调用点实参顺序，不做顺序假设），
  只去掉外壳（`summary`、`DrawSectionHeader` 折叠头、`HelpBox`、启用开关那一行、`VerticalScope`）；
  按模式分支的参数组 helper 一并改成 `SerializedProperty` 版
  （`DrawModeParameters→DrawModeProperties`、`DrawHeightFadeParameters→DrawHeightFadeProperties`、
  `DrawFogParameters→DrawFogProperties`），模式判定复用文件里已有的 `GetFillMode/GetHeightFadeMode(SerializedProperty)`。
  **结果 64 行 = 70 字段 − 5 个启用开关 − 1 个第六侧栏项**，检查里是硬断言（少一行就报错）。
- **C（死代码）**：`DrawVolume`、`DrawSettings`、legacy `Draw*Settings`、`DrawParameter(SerializedDataParameter, …)`、
  `showVolume/showSettings`、只给旧标题用的颜色常量，全部已无调用者 —— 删除（Roslyn 0 error）。

**迁移代价（要记着）**：序列化路径从 69 个顶层参数变成 `Effects.m_Value.*`，所以**旧 Volume profile 里已调好的效果值会回到默认值**
（Settings 资产里的旧值本来每帧都被覆盖，无所谓）。字段名保持不变，将来真要写迁移脚本也能一一对上。

**与规划的一处差异**：规划说"删掉 Settings 的 69 个效果字段"，实现改成标 `[NonSerialized]` 保留为运行时载体 ——
因为 pass 代码有几百处 `activeSettings.eyeRevealXxx` 读取，删字段会把 1377 行的 Feature 全部翻一遍；
`[NonSerialized]` 达到了同样目的（资产里不再有第二份数据），代价只是这些字段仍留在类里。

### 1.1 v1 回归：容器 override 被一起删掉了（2026-09-19 修）

**症状**：脸色扩散、主体描边、增强描边三个效果"失效"——在 Volume 里手动打开也没反应；
眼透、前发投影看起来正常。

**根因**：`HoCharacterSpecializationRendererFeature.ResolveSettings` 读的是
`VolumeManager.instance.stack.GetComponent<HoCharacterSpecializationVolume>()`，而
`VolumeParameter.Override` 只复制**源参数 `overrideState == true`** 的值。旧实现里 69 个参数各自
`X.overrideState = true;`（那 58 行"假 override"），所以整块值都会被复制进 stack；
换成单容器后我把这些 override 全删了，容器默认 `overrideState = false` ⇒ **stack 里永远是默认构造的那一份**。
于是：默认 `true` 的眼透/前发投影看着"活着"（用的还是默认值，不是场景里调的值），
默认 `false` 的脸色扩散/主体描边/增强描边怎么点都没反应 —— 而且改 profile 里的**值**也没用，
因为 `overrideState` 始终是 0。

**修复**：
1. 代码：`HoCharacterSpecializationEffectsParameter` 的 `overrideState` 默认值改成 `true`，
   `Effects` 字段初始化显式传 `true`；逐参数的假 override 依然是删掉的（整块只有这一个 override）。
2. 数据：老 profile 里 `Effects.m_OverrideState` 是 0，且 `Effects.m_Value` 还不存在（序列化路径变了，
   Unity 不认旧键）⇒ 用 `.codex-research/cs_config_split/migrate_volume_profiles.py` 把旧扁平键
   （PascalCase，69 个）搬进 `Effects.m_Value`（lowerCamelCase）并顺手把容器 override 置 1。
   实际执行：**9 个文件搬了 439 个值 + 10 个文件修了容器 override**，删掉 63 个死字段键；
   写前逐文件备份到 `.codex-research/cs_config_split/backup/`，脚本幂等（重跑报告 0 个文件）。
3. 检查：`check_cs_browser_ui.js` 新增两条 gated 不变量（容器默认值必须是 `true`、`Effects` 字段初始化必须显式传 `true`），
   各带一条负对照；扫描时会忽略注释，所以文档可以解释这套机制。
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

| 检查 | 内容 | 结果 |
| --- | --- | --- |
| `.codex-research/cs_config_split/audit_migration.py`（一次性，对旧 Volume 的备份比对） | 70 个效果参数是否 1:1 迁到新的 Effects 类：字段名（PascalCase → lowerCamelCase）、类型映射、**默认值文本**、`ClampedFloatParameter` 的区间是否变成 `[Range]`、`[InspectorName]`/`[Tooltip]` 是否保留 | **70/70 通过**（不丢字段、不改默认值） |
| `.codex-research/character_specialization_sim/check_cs_browser_ui.js`（新） | 目录 ↔ 字段一一对应（无孤儿/无重复覆盖）、图标文件存在、行内 `×` 只把启用字段置 false、折叠走 `SessionState` 而非 `static`、**逐参数 `overrideState` 不得回来**（只剩 `VolumeParameter<T>` 构造必需的装箱）、**但容器那一个 override 必须是 true**（默认值 + 字段初始化两处都钉住，见 §1.1）、`ApplyTo` 与 7 个死字段已删、Settings 的 70 个 `[NonSerialized]` 与"CopyFrom 只剩管线"、Feature 调 `CopyEffectsTo`、无底按钮规则、5 处 `DrawEffects` 各一次、每行都必须画得出来（统计沿 `DrawXProperties(effects, …)` 递归） | 见下 |
| 行覆盖（**已升级为 gated**，本次修 A） | 5 个区段的 `DrawEffects(effects)` 必须画出该区段目录里拥有的每一行；行允许由同文件的参数组 helper（`DrawModeProperties`/`DrawHeightFadeProperties`/`DrawFogProperties`，按模式分支）代画，所以统计沿调用关系递归 | **70 = 64 行 + 5 个启用开关 + 1 个第六侧栏项**；修前 30 个字段在面板上没有任何入口 |
| 行覆盖 + 摘要字段的负对照（本次新增 4 条，共 **22/22**） | ①helper 里删掉一行 ②`DrawEffects` 不再调用某个 helper ③摘要字段改名成不存在的 ④摘要字段借用别的区段的 —— 四种改法都必须被抓住 | 全过 |
| 摘要字段（本次修 D） | `EffectSection` 增加 `SummaryField`，折行摘要读它（原实现读 `"<prefix>Strength"`，对前发投影这类没有该字段的区段永远显示"开"）；检查断言该字段存在且属于本区段的 prefixes | 5/5 通过 |
| 死代码清理（本次修 C） | 5 个区段文件里已无调用者的 `DrawVolume`/`DrawSettings`/`Draw*Settings`/`DrawParameter(SerializedDataParameter, …)`、`private static bool showVolume/showSettings`、只给旧标题用的 `SectionColor` 等颜色常量 | 已删；Roslyn 0 error |
| 既有检查（重构后复跑） | `effect_browser_sim/check_browser_ui.js --negative-control`（8/8 负对照）、`browser_search_check`（dotnet 100 项）、`effect_enum_check`、`halftone_sim/check_halftone_ui.js`、`shader-check`（11 个 shader） | **全过** |
| Roslyn 独立编译（Editor + Runtime） | 0 error，仅剩仓库原有 11 条 CS0649 警告 | 通过 |

（下面是重构前写的检查计划，保留备查。）

| 检查 | 内容 |
| --- | --- |
| 新增 `.codex-research/character_specialization_sim/check_cs_browser_ui.js`（照 `check_browser_ui.js`） | 6 个条目与 6 组参数一一对应；图标文件存在（精确大小写）；目录声明 6 行并与绘制一致；标题行的「启用」写的是对应字段；`×` 只关效果、不碰其它参数；「恢复默认」清的是效果自己的字段；折叠状态不再是 `static`；搜索只过滤侧栏；不得出现 `miniButton`/`GUI.Button`；**逐参数 `overrideState` 不得回来**（这是本次的核心不变量；构造必须的装箱不算用法，容器那一个 override 必须为 true，见 §1.1）；负对照若干条全部生效 |
| Runtime 侧检查 | `ApplyTo` / Settings 的 69 个效果字段 / `CopyFrom` 对应行都应消失（用 grep 断言 + 结构检查钉住）；`ResolveSettings` 只做 gating；`IsActive`/`IsActiveForCamera` 用普通字段实现 |
| 既有检查必须继续通过 | `check_compile.ps1`（Roslyn 0 error）、`effect_browser_sim/check_browser_ui.js`（8/8 负对照）、`browser_search_check`（dotnet 100 项） |
| 实机清单 | Volume profile 里**已有数值**（注意：序列化路径变了 ⇒ 会回默认值，见 §1 的实现说明）；同一个场景两个 Volume 的表现（最近者整体生效）；关闭 Volume 组件后效果完全停（无残留 RT/无 pass）；侧栏开关与右侧「启用」双向同步；亮/暗主题 |


## 6. 已定取舍

1. **方案 A：Volume 是效果参数的唯一来源**，删掉 Settings 的重复字段与 `ApplyTo`，不再有保底与逐参数覆盖
   —— 已确认**需要逐场景调**，所以更不能走"纯 Feature 级配置"的备选 B；
2. 侧栏 6 条（含「语义遮罩抗锯齿」）；
3. 区段行 `×` = **关掉效果**（不是恢复默认）；
4. **顺序保持固定、不提供排序入口**：侧栏与右侧区段都按写死的顺序（眼透 → 前发投影 → 前发漫反射 →
   主体描边 → 增强描边）。顺序在代码里是**一张表**（数据），只是 UI 不给改的入口 —— 以后想放开只需加一个开关，
   不用改结构；
5. 共享代码只加 **一个可选 `Rows`**，其余一律不动；
6. 死字段（§0.3）随本次一起删；
7. RendererFeature 编辑器：效果区段挪走后只剩管线/捕获/调试，**这次不接浏览器**；
8. **三块的能力对齐是共同待办**（§8）：现在连另外两块自己都没把逐效果能力全部启用，目标统一为一套能力 + 一套 UI；
9. **遮罩等 AC**（§10）：不在 AC 之前基于 MetadataBuffer 通道实现 IP 的遮罩，UI 文案也不许出现旧名；
10. **遵守 `Ho-UI_风格规范.md`**，并记录三处有意偏离（§11）。

**备选方案 B（记录用）**：效果参数只留在 Settings 资产（Feature 级），Volume 退化成纯开关。
运行时最省事、也没有重复，但**同一个项目内无法按场景/角色分别调参**——与"跟后处理一样、用的时候才开"
的诉求不符，所以不采用；如果以后确定 CS 参数就是全局一套，再切到 B 也很容易（字段名一致）。

## 7. 图像链化（v2，已确认要做）

前提问题已有答案，结论从"待评估"变成"确认做"：

| 问题 | 回答 | 含义 |
| --- | --- | --- |
| 需要逐场景/逐相机调参吗？ | **需要** | 方案 A（Volume 单一来源）是前提，v1 必须先行 |
| 需要排序 / 同一效果多份吗？ | **暂时不需要，固定顺序更好** | 链化时顺序仍是一张固定表；**不做拖拽**，也不做同效果多实例 |
| 近期会加很多角色效果吗？ | **绝对会加很多** | 链化最大收益：新效果从"改 830 行/206 uniform/57 分支的大片元"降到"一个小 shader + 约 28 行 partial + 1 行注册" |
| 多几趟全屏 pass 的带宽能接受吗？ | **能** | 管线里开销最大的是**最高质量的 GTAO**，其余效果都不算卡；CS 这几趟全屏 pass 与之相比可忽略，而且这个 feature 定位是管线特色、有最高资源优先级 |

### 建议形态（不变）

```
[capture 序幕：脸部 RT / 眼部 RT + 各效果 Source + 语义遮罩模糊 + 降分辨率]   ← 留在 Feature（约 40% 代码不动）
        ↓ 产出具名资源
[链：一个效果一个全屏图层，读上面的资源；顺序 = 固定表，不做拖拽；开关/强度/混合/遮罩]  ← 复用链式基础设施
```

- **顺序固定**在链里体现为"图层按 `PreferredOrder` 常量插入"（ScreenProcess 的 `GetPreferredLayerOrder`
  就是这个做法的先例），UI 不画拖拽把手；
- 新效果如果只需要屏幕空间输入（相机颜色 + 元数据/几何缓冲），**不用碰 Feature**，加 shader + partial + 注册即可；
  只有需要**新的捕获**（几何重绘到 RT）时才动序幕，这类预期是少数；
- 兼容路径 `Execute` 的去留随链化一起决定（链式自己有兼容路径）。

### 仍然要认的代价

1. 捕获序幕（`DrawRendererList` 几何重绘 + clear + `renderScale`）不能进链，必须留在 Feature；
2. 中间资源图要扩：链式图层需支持"内部多趟 + 资源请求 + 降分辨率中间 RT"
   （IP 的 `MultiPass` 档与 ScreenProcess 的 `ResourceRequest` 是基础）；
3. 迁移本质是把 5 个效果从 830 行的 Composite 里拆成独立小 shader；
4. **明确不做**：不要把 CS 塞进 `ImageProcess`/`ScreenProcess` 的现有链——缺"几何重绘式捕获"图层类型，
   `renderScale`/Settings 资产也不匹配；CS 用自己的链实例（共享基础设施，而不是共享那份图层表）。

## 8. 三块能力对齐矩阵（共同待办）

现状（编辑器侧实测）：

| 能力 | ImageProcess | ScreenProcess | 角色特化 | 缺口 / 待办 |
| --- | --- | --- | --- | --- |
| 启用开关 | ✓ | ✓ | ✓（本次新做） | — |
| 强度 `intensity` | ✓ | ✓ | ✗ | CS 链化后白拿 |
| 颜色 `color` | ✓ | ✓ | ✗（各效果自带颜色参数） | 语义不同，保持各自参数 |
| 混合模式 | **24 种** | **只有 4 种**（Normal/Add/Screen/Multiply） | ad-hoc 2 个（`hairShadowBlendMode`/`faceHairDiffuseBlendMode`） | **SP 补到 24**：共享 include `ImageProcessBlend.hlsl` 已抽好，SP 直接复用；CS 链化后白拿 |
| 规则遮罩 | **✗（0 处引用）** | ✓（只留层遮罩：每层开关 + MetadataBuffer 覆盖率） | ✗ | **AC 落地前不做**：遮罩的来源将来只有一个 = AC（见 §10）；SP 原来的 20 个 source 与规则列表**已作为未使用功能删除**（不是迁移），R5 时 SP 图层作为**新工作**直接吃 AC 具名遮罩 + `HoAC_*`，现在照 MetadataBuffer 通道再实现一套会白做 |
| 预设菜单 | ✓ | ✓ | ✗ | CS 链化后按 IP/SP 的写法补（两段描边最需要） |
| 行样式 / 无底控件 / 浏览器 | ✓ | ✓ | 本次对齐 | — |

目标：三块都是 **[启用 + 强度 + 颜色 + 24 混合模式 + 规则遮罩 + 预设] + 同一套行样式与浏览器**。
这张表就是"UI 与功能统一"的待办清单；其中 **SP 的混合模式** 与 CS 无关，可独立排期（复用已抽好的 `ImageProcessBlend.hlsl`）。

## 9. 执行顺序

1. **v1（本次）**：CS 配置模型清理（Volume 单一来源、删 `ApplyTo` 与重复字段、删死字段）+ UI 对齐
   （浏览器外壳、6 条侧栏、区段行换皮、每实例折叠、搜索高亮、固定顺序）；
2. **能力补齐（可与 v1 并行/随后）**：SP 混合模式补到 24；IP 补规则遮罩；
3. **v2**：CS 图像链化（捕获序幕保留 + 合成链化 + 固定顺序表），随后按 IP/SP 的写法补 CS 预设；
4. 之后每加一个角色效果的成本 = 一个小 shader + 约 28 行 partial + 1 行注册（+ 预设条目）。

## 10. 与 AC（Ho-AttributeComposite）的关系

`Documentation~/架构优化/Ho-AttributeComposite_规划.md` 已经把 AC 的边界与接口**冻结**了，其中两条直接管到这块 UI：

1. **遮罩只有一个来源 = AC**：消费者不许直接读 OB/SB 的原始图，也不许自己再攒一套语义图
   （AC 文档 §1）；查询形状是 `HoAC_Mask(id)` / `HoAC_Group(groupId)` / `HoAC_Slot(slot)` /
   `HoAC_Attribute(attr)` / `HoAC_Coverage()`，C# 侧由 feature **声明名字**、经 manifest 解析，解析不到要报诊断
   （不许静默）。
2. **只吃 AC 的消费者**里明确列了 **ScreenProcess 与角色特化**；排期上 R4 = AC 落地，
   **R5 = 消费者输入切换**（SP 图层作为**新工作**接入 AC 具名遮罩——原来的 20 个 rule source 已作为未使用功能删除，没有旧配置要迁移；
   角色特化 → AC（组 / 物体位 / 覆盖率）+ SB（表面色））。

对本文档的影响：

| 事项 | 结论 |
| --- | --- |
| IP 补规则遮罩 | **取消**（§8）：现在按 MetadataBuffer 通道做一套，AC 一到就全废；等 R5 直接吃 `HoAC_*` |
| SP 现有遮罩 UI | 现在只剩**每层开关 + 反转 + debug**（rule-source 下拉与规则列表已删除）；R5 接 AC 时换成"具名条目 + 登记表 + 解析失败可见"，不要在此基础上加功能 |
| CS v2 链化的资源绑定 | CS 现在直接读 `_HoMetadataBuffer*`（Composite shader `:52` 等）正是 AC 边界里**禁止**的最终形态 ⇒ 链化时按已冻结的 `HoAC_*` 形状设计（组 / 物体位 / 覆盖率 + SB 表面色），并与 R5 对齐排期 |
| CS v1（本次） | **不依赖 AC**，可以先做：它只动配置模型与 UI 外壳，不碰语义读取 |
| UI 文案 | 规范禁止在 UI 上出现 `_HoMetadataBuffer*`、`custom0` 这类旧名；侧栏/区段文案要按 AC 的用语（身份池 / 语义槽 / 组 ID / 部件 ID / 标记 / 物体位） |

## 11. 与 `Ho-UI_风格规范.md` 的关系

**这套 UI 自成一套，不受那份规范约束**（已确认：IP/SP 的浏览器是有意特殊设计，不必与规范统一）。
规范已在开头写明适用范围（OB / SB / AC 及后续通道型 feature），并指出后处理三块不要照它"统一"；
`EffectBrowser.md` 顶部也加了同样的归属说明。

所以角色特化跟着 **IP/SP 那套**走，而不是跟着规范走：

| 项 | 做法 |
| --- | --- |
| 效果行 | 浏览器统一的窄行（`▶ 名字 摘要 [启用] [×]`、无底控件、悬停高亮），**不用** `DrawSectionHeader` |
| 侧栏 | 与 IP/SP 同一套（搜索 + 图标 + 翻页 + 右键菜单） |
| 折叠状态 | `SessionState` 每实例（**UI 状态不进资产**，也避免两个 Inspector 串台）；IP/SP 的展开状态是存在图层元素上的，这边没有"层"，所以用会话状态 |
| 参数形态 | 一个 `VolumeParameter` 包一组普通字段（IP/SP 的图层列表写法），不是"每参数一个 `VolumeParameter<T>` + `Interp`" |
| 仍然沿用规范里通用且合理的部分 | 中文标签 + 英文原名括号；颜色常量集中在类顶；可用性不只靠颜色；UI 不出现 `_HoMetadataBuffer*`/`custom0` 这类旧名（用 AC 的用语）；解析失败/不一致要可见 |

CS 里若以后出现**通道/设置性质**的分节（例如捕获范围、渲染缩放这类不是"效果"的东西），
那些分节仍按规范写（`DrawSectionHeader` + 语义色板）——两种样式在同一面板里共存，
判据是"这是不是一个可开关的效果"。
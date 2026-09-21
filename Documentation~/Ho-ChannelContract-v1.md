# lilToon 通道契约 v1（Channel Contract v1）

> 状态：**已冻结 · v1（2026 文档审核按 OB/SB/AC 实现重校生产端）**
> 冻结规则：`AOV=冻结` 的通道，命名与编码**只增不改**；修订须升 v2 并记录变更。
> 基线：`mmd场景测试\朱木古堂\New Scene.unity`（**写作时的文件名**；2026 文档整理时该目录里只剩 `HIRO.unity`，实机以工程为准）（40 灯 / GTAO / SSGI / ScreenProcess + ImageProcess 栈）；渲染器 `PC_Renderer.asset`（14 项 feature；另有 4 类已删脚本的 Missing Script 死条目与若干同名重复条目，属资产卫生）。
> 原则：按需纸面契约（非固定编码）；无消费者不登记；RenderGraph transient 声明；AOV 命名冻结。
>
> v1 是当前 Runtime 的 bridge 契约，不是三轴长期归属。[`Ho-管线总览.md`](Ho-管线总览.md) 冻结了 GB / ObjectBuffer / SurfaceBuffer / AttributeComposite 的目标边界（已完成）；反射字段迁移只在 `ReflectionPipelineDesign.md` 维护。
> **本轮修正的最大一处**：`MetadataBuffer` 已在 R6/R7 删除，通道表的“生产端”一列从 MB 改为 **OB（身份/覆盖率）+ SB（表面数值）+ AC（语义遮罩查询）**。

---

## 1. 通道表

状态：`✅` 已实现 / `◻` 已登记待实现 / `⚠️` 生产端待替换（HTrace→自研） / `❌` 不输出（排除位）。

| 通道 | 生产端 | 消费端 | 编码 | AOV | 状态 |
| --- | --- | --- | --- | --- | --- |
| `beauty` | ImageProcess 终链 | 相机输出 | RGBA hdr | （主图） | ✅ |
| `surfaceColor` | 材质 → **SB `Color`**（数值 pass MRT0） | SSS / 角色特化 / 反射 / AOV | RGBA16F（RGB=线性 HDR 表面色，不钳制；**A 不再是 coverage**——覆盖率只有 OB/AC 一个来源） | `diffuse_albedo` | ✅ |
| `normal` | GeometryBuffer | SSS / AO / GI / 反射 / AOV | RGBA(oct) + depth.a | `normal` | ✅ |
| `depth` | GeometryBuffer | AO / GI / SSS / 反射 | R16f | `depth` | ✅ |
| `maskId` | **OB 身份池**（`Ho-ObjectBuffer` 的 4 层身份 + 覆盖率；经 **AC** 查询） | 角色特化 / AOV | 4×RGBA8（每层两条 `(IdentityId, coverage)`） | `id_object`, `id_group` | ✅ |
| `surfaceData` | 材质 → **SB `Classification` + `Material.b`** | SSS | RGBA（thickness/curvature/material/transmittance） | — | ✅ |
| `reflectionMaterial` | 材质 → **SB `Material` / `Reflection`** | PLR / SSR / Probe | RGBA16F（R=perceptualRoughness，G=metallic，B=reflectance，A=PLR strength，由反射总开关与 PLR 开关共同门控） | — | ✅ |
| ~~`objectCustom0/1`~~ | **已由下一行的 `objectSemantic.low/high` 取代**（旧名仅存在于历史文档与 UI 兼容文案） | — | — | — | ❌ |
| `objectSemantic.low/high` | Ho-CharacterSpecialization 语义打包 pass（`HoCharacterObjectSemantic.shader`：读 **OB 身份池 + 覆盖率**，按部件行表的标签位逐层累加） | 角色特化（前发投影的接收面与眼透区域、脸色扩散、眼睛透过、主体/增强轮廓） | R8G8B8A8_UNorm ×2（逐通道与从前的 `objectCustom0_3` / `objectCustom4_7` 同索引；值为该语义的**覆盖率 0..1**，不是 bit） | 不导出 | ✅ |
| `shadow.main` | URP 主光阴影 | 材质 toon 门控 / AOV | R8f | `shadow_main` | ✅ |
| `shadow.add0..N` | ShadowCast cast 组（每组一张 atlas；N≤8，组≠灯） | 材质 / ScreenProcess / AOV | R8f | `shadow_add0..N` | ◻ |
| `ao` | `Ho-GTAO`（自研，已实现） | 材质采样 + ScreenProcess | R8f | `ao` | ✅ |
| `aointent` | 材质（`_UseScreenSpaceAO`/`_SSAO*`） | ScreenProcess.AO | R8f | — | ◻ |
| `gi` | `Ho-SSGI`（自研，已实现；当前由 fullscreen composite 叠加） | ScreenProcess.GI | RGB hdr（+`gi.gamma` R8f） | `gi` | ✅ |
| `gisexclude` | 材质（outline/非物理表面） | GI / AO 生产端 | 单 bit R8（1=排除） | — | ◻ |
| `sss` | Ho-SubsurfaceScattering | ScreenProcess 合成 | RGBA | `sss` | ✅ |
| `sssprofile` | 材质 / profile | SSS / AOV | R8 | `sssprofile` | ✅ |
| `reflection` | PLR / SSR resolve | ScreenProcess / AOV | RGBA HDR | `reflection` | ◻ |
| `eyecolor` / `eyedata` | Ho-CharacterCapture | 角色特化 | RGBA | — | ✅ |
| `emission` | 材质（全部发光，HDR 强度） | AOV | RGB hdr | `emission` | ◻ |
| `motion` | （占坑） | AOV / temporal | RG | `motion` | ◻ |

> 说明：`gisexclude` 为描边/非物理表面排除位（描边白边已知 bug 的正式解，见 `Ho-已知问题-描边SSGI白边.md`）；`aointent`/`gi`/`gisexclude`/`emission`/`motion` 已登记，随对应系统落地实现。
>
> ~~`maskcoverage.*`~~ **已废弃**：那条“把 `objectCustom` 位图整体过一遍盒核滤波”的抗锯齿副本路线已经被 **AC 的覆盖率**取代——OB 身份池的每条 lane 天生带 `(IdentityId, coverage)`，`HoCharacterObjectSemantic.shader` 直接把覆盖率按语义累加进两张位平面（见上表 `objectSemantic.low/high`），不再需要单独一趟模糊，也不再需要“读覆盖率版还是原始 bit”的勾选项。**ID 与材质分类仍然不可平均**（`IdentityId` / `materialClass` 一经插值即无意义）——这条纪律没有变。

---

## 2. AOV 导出清单 v1（Nuke，命名冻结）

```text
+rgba=beauty            最终画面
diffuse_albedo          surfaceColor
normal                  几何法线（oct）
depth                   几何深度（R16f）
shadow_main             URP 主光阴影
shadow_add0..N          ShadowCast cast 组阴影（N≤8 组）
ao                      AO 因子（Ho-GTAO 生效后）
gi                      屏幕 GI（Ho-SSGI 生效后）
sss                     SSS 扩散结果
sssprofile              SSS profile id
reflection              平面反射结果
emission                发光（全部发光材质）
id_object               maskId.object
id_group                maskId.group
matte_hair / matte_face / matte_eye   objectCustom bits（以组件语义为准）
motion                  占坑（动态模糊 / Nuke）
```

- 命名：Arnold 全小写下划线；容器 = OpenEXR 单 part 多通道（`layer.channel` 点分；`+rgba` 主图）。
- 冻结：导出后只增不改。
- 实现：独立 AOV 输出层（Editor/批处理二次渲染或专用 camera）；先以 `FullScreenPassRendererFeature` / `RenderObjects` 起 2–3 通道原型，再封装 EXR 装配工具（不先写导出框架）。

---

## 3. 声明模板 & 新增流程

```text
通道名: <小写点分，如 ao、beauty.sss>
生产端: <mod/pass/producer>
消费端: <consumer 列表>
编码:   <format + 细节（R8f 0..1 / byte / oct / hdr）>
生命周期: <transient / 帧持久 / 场景持久>
AOV:    <导出名 | 不导出>
debug:  <debug tile 名 / 直出分支>
冻结:   <是 / 否>
```

```
新增流程：登记本行 → 生产端输出 → 消费端消费 → debug 可见 → （需要时）登记 AOV → 冻结。
无消费者不登记、不输出；被 AOV 导出后不再改命名/编码。
```

---

## 4. 冻结决议（v1）

1. **matte bits**：沿用 **`Ho-ObjectBuffer Group`** 的部件条目语义（组 ID / 部件 ID / 标签位 / 物体位）；位含义以组件 Inspector 为准（原 `HoMetadataBufferGroup/Subject` 已删除）。
2. **motion**：转正占坑（RG → AOV `motion`）；先登记不实现。
3. **编码**：`ao`/`aointent` = R8f(0..1)；`gi` = RGB hdr + `gi.gamma` R8f；`gisexclude` = 单 bit R8(1=排除)。
4. **SurfaceColor**：producer 不钳制 RGB；需要 `[0,1]` 的消费者自行显式钳制。**A 不是 coverage**——覆盖率只有 OB/AC 一个来源。
5. **ReflectionMaterial**：已从“MetadataBuffer Target5 迁移桥”完成迁移到 **SB `Material` / `Reflection`**；R/G/B 保持 perceptualRoughness / metallic / reflectance，A 同时服从 `_UseReflection` 与 `_UsePlanarReflection`。长期 `reflection` 字段仍按具名通道实现，不从通用 Custom0 猜参数。
6. **emission**：所有发光材质（HDR 强度），选区在 AOV 端。
7. **shadow 拆分**：`shadow.main`（URP 主光）+ `shadow.add0..N`（ShadowCast cast 组，N≤8，N 指组非灯；每组一张 atlas）；专用组（脸/远平面）只规划不占 AOV。

---

## 5. 变更记录

| 版本 | 日期 | 变更 | 说明 |
| --- | --- | --- | --- |
| v1 | — | 冻结 | 初始契约 |
| v1-bridge | 2026-09-14 | 反射字段标记为迁移桥 | 长期归属改由 v2 SurfaceBuffer/AttributeComposite 定义 |
| v1-R7 | 2026 文档审核 | 生产端列由 MetadataBuffer 改为 OB/SB/AC | `surfaceColor`→SB `Color`（A 不再表 coverage）、`maskId`→OB 身份池、`surfaceData`→SB `Classification`+`Material.b`、`reflectionMaterial`→SB `Material`/`Reflection`；`objectCustom0/1` 行标记为被 `objectSemantic.low/high` 取代；`maskcoverage.*` 段落废弃（AC 覆盖率原生带 AA）；`ao`/`gi` 状态改为已实现 |

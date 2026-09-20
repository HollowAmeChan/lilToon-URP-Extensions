> **已过时（R6/R7）**：本文写作时 MetadataBuffer 还在。它已在 R6（摘槽）／R7（消费者换源 + 整块删除）中删掉：`maskId` 与自定义通道归 OB + AC，surface 族归 SB。当前架构以 `Documentation~/架构优化/Ho-*.md` 与 `CHANGELOG.md` 为准。

# lilToon 通道契约 v1（Channel Contract v1）

> 状态：**已冻结 · v1**
> 冻结规则：`AOV=冻结` 的通道，命名与编码**只增不改**；修订须升 v2 并记录变更。
> 基线：`mmd场景测试\朱木古堂\New Scene.unity`（40 灯 / GTAO / SSGI / ScreenProcess + ImageProcess 栈）；渲染器 `PC_Renderer.asset`（12 项）。
> 原则：按需纸面契约（非固定编码）；无消费者不登记；RenderGraph transient 声明；AOV 命名冻结。
>
> v1 是当前 Runtime 的 bridge 契约，不是三轴长期归属。`LILTOON_FORMAL_PIPELINE_DRAFT_V2.md` 已冻结 GB / ObjectBuffer / SurfaceBuffer / AttributeComposite 的目标边界；反射字段迁移只在 `ReflectionPipelineDesign.md` 维护。

---

## 1. 通道表

状态：`✅` 已实现 / `◻` 已登记待实现 / `⚠️` 生产端待替换（HTrace→自研） / `❌` 不输出（排除位）。

| 通道 | 生产端 | 消费端 | 编码 | AOV | 状态 |
| --- | --- | --- | --- | --- | --- |
| `beauty` | ImageProcess 终链 | 相机输出 | RGBA hdr | （主图） | ✅ |
| `surfaceColor` | 材质 → MetadataBuffer SurfaceColor pass | SSS / 角色特化 / 反射 / AOV | RGBA16F（RGB=线性 HDR 表面色，不钳制；A=coverage 0..1） | `diffuse_albedo` | ✅ |
| `normal` | GeometryBuffer | SSS / AO / GI / 反射 / AOV | RGBA(oct) + depth.a | `normal` | ✅ |
| `depth` | GeometryBuffer | AO / GI / SSS / 反射 | R16f | `depth` | ✅ |
| `maskId` | 材质 / 对象 | 角色特化 / AOV | R8G8B8A8(byte) | `id_object`, `id_group` | ✅ |
| `surfaceData` | 材质 | SSS | RGBA（thickness/curvature/material/transmittance） | — | ✅ |
| `reflectionMaterial` | 材质 → MetadataBuffer Target5 | PLR / SSR / Probe | RGBA16F（R=perceptualRoughness，G=metallic，B=reflectance，A=PLR strength，由反射总开关与 PLR 开关共同门控） | — | ✅ |
| `objectCustom0/1` | 材质 / 对象（Group/Subject） | 角色特化 / AOV matte | RGBA(bits) | `matte_*` | ✅ |
| `objectSemantic.low/high` | Ho-CharacterSpecialization 语义打包 pass（`HoCharacterObjectSemantic.shader`：读 **OB 身份池 + 覆盖率**，按部件行表的标签位逐层累加） | 角色特化（前发投影的接收面与眼透区域、脸色扩散、眼睛透过、主体/增强轮廓） | R8G8B8A8_UNorm ×2（逐通道与从前的 `objectCustom0_3` / `objectCustom4_7` 同索引；值为该语义的**覆盖率 0..1**，不是 bit） | 不导出 | ✅ |
| `shadow.main` | URP 主光阴影 | 材质 toon 门控 / AOV | R8f | `shadow_main` | ✅ |
| `shadow.add0..N` | ShadowCast cast 组（每组一张 atlas；N≤8，组≠灯） | 材质 / ScreenProcess / AOV | R8f | `shadow_add0..N` | ◻ |
| `ao` | `Ho-GTAO`（自研，单 feature） | ScreenProcess.AO | R8f | `ao` | ⚠️ |
| `aointent` | 材质（`_UseScreenSpaceAO`/`_SSAO*`） | ScreenProcess.AO | R8f | — | ◻ |
| `gi` | `Ho-SSGI`（自研，单 feature） | ScreenProcess.GI | RGB hdr（+`gi.gamma` R8f） | `gi` | ⚠️ |
| `gisexclude` | 材质（outline/非物理表面） | GI / AO 生产端 | 单 bit R8（1=排除） | — | ◻ |
| `sss` | Ho-SubsurfaceScattering | ScreenProcess 合成 | RGBA | `sss` | ✅ |
| `sssprofile` | 材质 / profile | SSS / AOV | R8 | `sssprofile` | ✅ |
| `reflection` | PLR / SSR resolve | ScreenProcess / AOV | RGBA HDR | `reflection` | ◻ |
| `eyecolor` / `eyedata` | Ho-CharacterCapture | 角色特化 | RGBA | — | ✅ |
| `emission` | 材质（全部发光，HDR 强度） | AOV | RGB hdr | `emission` | ◻ |
| `motion` | （占坑） | AOV / temporal | RG | `motion` | ◻ |

> 说明：`gisexclude` 为描边/非物理表面排除位（描边白边已知 bug 的正式解，见 `LILTOON_KNOWN_ISSUE_OUTLINE_SSGI_GLOW.md`）；`aointent`/`gi`/`gisexclude`/`emission`/`motion` 已登记，随对应系统落地实现。
>
> `maskcoverage.*` 是 `objectCustom` 的**抗锯齿副本**，用来解决"单采样 0/1 语义位当轮廓用时边缘只能是硬边"：两张 `objectCustom` 纹理整体过一次盒核滤波（逐通道独立，不串道），产出每语义的覆盖率。生产端不改动原位图，消费端按各自的勾选读覆盖率版或原始 bit；模糊宽度是 feature 级别的一个参数，五个勾选项全关时这趟 pass 不跑。**不要把这条规则套到 `maskId` / `surfaceData` / `custom0` / `eyeData` 上**（ID 与材质分类平均后无意义）。

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

1. **matte bits**：沿用 `HoMetadataBufferGroup/Subject` 组件语义；位含义以组件 Inspector 为准。
2. **motion**：转正占坑（RG → AOV `motion`）；先登记不实现。
3. **编码**：`ao`/`aointent` = R8f(0..1)；`gi` = RGB hdr + `gi.gamma` R8f；`gisexclude` = 单 bit R8(1=排除)。
4. **SurfaceColor**：producer 不钳制 RGB；需要 `[0,1]` 的消费者自行显式钳制，A 始终为 coverage。
5. **ReflectionMaterial（bridge）**：当前 MetadataBuffer Target5 仅作为 SurfaceBuffer 迁移桥；R/G/B 保持 perceptualRoughness / metallic / reflectance，A 同时服从 `_UseReflection` 与 `_UsePlanarReflection`。长期 `Reflection` 字段按 v2 具名通道实现，不再从通用 Custom0 猜测参数。
6. **emission**：所有发光材质（HDR 强度），选区在 AOV 端。
7. **shadow 拆分**：`shadow.main`（URP 主光）+ `shadow.add0..N`（ShadowCast cast 组，N≤8，N 指组非灯；每组一张 atlas）；专用组（脸/远平面）只规划不占 AOV。

---

## 5. 变更记录

| 版本 | 日期 | 变更 | 说明 |
| --- | --- | --- | --- |
| v1 | — | 冻结 | 初始契约 |
| v1-bridge | 2026-09-14 | 反射字段标记为迁移桥 | 长期归属改由 v2 SurfaceBuffer/AttributeComposite 定义 |

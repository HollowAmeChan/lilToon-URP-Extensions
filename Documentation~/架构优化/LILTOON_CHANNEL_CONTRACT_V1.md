# lilToon 通道契约 v1（Channel Contract v1）

> 状态：**已冻结 · v1**
> 冻结规则：`AOV=冻结` 的通道，命名与编码**只增不改**；修订须升 v2 并记录变更。
> 基线：`mmd场景测试\朱木古堂\New Scene.unity`（40 灯 / GTAO / SSGI / ScreenProcess + ImageProcess 栈）；渲染器 `PC_Renderer.asset`（12 项）。
> 原则：按需纸面契约（非固定编码）；无消费者不登记；RenderGraph transient 声明；AOV 命名冻结。

---

## 1. 通道表

状态：`✅` 已实现 / `◻` 已登记待实现 / `⚠️` 生产端待替换（HTrace→自研） / `❌` 不输出（排除位）。

| 通道 | 生产端 | 消费端 | 编码 | AOV | 状态 |
| --- | --- | --- | --- | --- | --- |
| `beauty` | ImageProcess 终链 | 相机输出 | RGBA hdr | （主图） | ✅ |
| `surfaceColor` | 材质 → MetadataBuffer | SSS / 反射 / AOV | RGBA（a=subject 覆盖） | `diffuse_albedo` | ✅ |
| `normal` | GeometryBuffer | SSS / AO / GI / 反射 / AOV | RGBA(oct) + depth.a | `normal` | ✅ |
| `depth` | GeometryBuffer | AO / GI / SSS / 反射 | R16f | `depth` | ✅ |
| `maskId` | 材质 / 对象 | 角色特化 / AOV | R8G8B8A8(byte) | `id_object`, `id_group` | ✅ |
| `surfaceData` | 材质 | SSS | RGBA（thickness/curvature/material/transmittance） | — | ✅ |
| `objectCustom0/1` | 材质 / 对象（Group/Subject） | 角色特化 / AOV matte | RGBA(bits) | `matte_*` | ✅ |
| `shadow.main` | URP 主光阴影 | 材质 toon 门控 / AOV | R8f | `shadow_main` | ✅ |
| `shadow.add0..N` | ShadowCast cast 组（每组一张 atlas；N≤8，组≠灯） | 材质 / ScreenProcess / AOV | R8f | `shadow_add0..N` | ◻ |
| `ao` | `Ho-GTAO`（自研，单 feature） | ScreenProcess.AO | R8f | `ao` | ⚠️ |
| `aointent` | 材质（`_UseScreenSpaceAO`/`_SSAO*`） | ScreenProcess.AO | R8f | — | ◻ |
| `gi` | `Ho-SSGI`（自研，单 feature） | ScreenProcess.GI | RGB hdr（+`gi.gamma` R8f） | `gi` | ⚠️ |
| `gisexclude` | 材质（outline/非物理表面） | GI / AO 生产端 | 单 bit R8（1=排除） | — | ◻ |
| `sss` | Ho-SubsurfaceScattering | ScreenProcess 合成 | RGBA | `sss` | ✅ |
| `sssprofile` | 材质 / profile | SSS / AOV | R8 | `sssprofile` | ✅ |
| `reflection` | Ho-PlanarReflection composite | ScreenProcess | RGBA | `reflection` | ✅ |
| `reflectionintent` | 材质（写 MetadataBuffer） | PlanarReflection 合成 | RGBA | — | ✅ |
| `eyecolor` / `eyedata` | Ho-CharacterCapture | 角色特化 | RGBA | — | ✅ |
| `emission` | 材质（全部发光，HDR 强度） | AOV | RGB hdr | `emission` | ◻ |
| `motion` | （占坑） | AOV / temporal | RG | `motion` | ◻ |

> 说明：`gisexclude` 为描边/非物理表面排除位（描边白边已知 bug 的正式解，见 `LILTOON_KNOWN_ISSUE_OUTLINE_SSGI_GLOW.md`）；`aointent`/`gi`/`gisexclude`/`emission`/`motion` 已登记，随对应系统落地实现。

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
4. **emission**：所有发光材质（HDR 强度），选区在 AOV 端。
5. **shadow 拆分**：`shadow.main`（URP 主光）+ `shadow.add0..N`（ShadowCast cast 组，N≤8，N 指组非灯；每组一张 atlas）；专用组（脸/远平面）只规划不占 AOV。

---

## 5. 变更记录

| 版本 | 日期 | 变更 | 说明 |
| --- | --- | --- | --- |
| v1 | — | 冻结 | 初始契约 |

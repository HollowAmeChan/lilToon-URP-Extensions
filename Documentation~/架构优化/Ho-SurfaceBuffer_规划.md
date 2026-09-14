# Ho-SurfaceBuffer（SB）规划

表面数值 buffer：回答"表面是什么样"。**几何在 GB，身份与逐物体属性在 OB，合成在 AC。**

> **状态：字段、纹理名、材质侧参数名全部冻结，无待定，可开工。** 语义以 `LILTOON_FORMAL_PIPELINE_DRAFT_V2.md` §3.1 为准。

---

## 1. 字段（冻结）

| 纹理 | 通道 | 语义 | 格式 | 生产端 | 消费端 |
| --- | --- | --- | --- | --- | --- |
| `Color` | `rgb` | linear HDR base color，producer 不钳制；**无 coverage 语义** | RGBA16F | 材质 | 角色特化·脸色扩散、SSS、PLR、AOV `diffuse_albedo` |
| `Normal` | `rg` | `octa(shadingNormal)`——**含着色法线（含法线贴图）**；`ba` 备用 | RGBA8 | 材质 | PBR 光照、SSR 反射方向、一切吃法线贴图的效果 |
| `Material` | `r` | **`perceptualRoughness`**（不是 linear roughness） | RGBA8 | 材质 | PLR / SSR / Probe |
| | `g` | `metallic` | | | PLR / PBR |
| | `b` | `thickness` | | | SSS / 透射 |
| | `a` | 备用 | | | |
| `Reflection` | `r` | `reflectance`（F0 的标量近似） | RGBA8 | 材质 | PLR / SSR / Probe |
| | `g` | `plrStrength`（= `_PlanarReflectionStrength`，材质侧 0..1） | | | PLR |
| | `ba` | 备用 | | | |
| `Classification` | `r` | `materialClass` | RGBA8 | 材质 | SSS / ScreenProcess（经 AC） |
| | `g` | `curvature` | | | SSS |
| | `b` | `transmittanceHint` | | | SSS |
| | `a` | 备用 | | | |
| `Selection` | 成对 | **固定语义槽**（槽号由 OB 单方声明：默认 4 槽、可配 8 / 16）；每槽 `R=id0, G=cov0, B=id1, A=cov1`，**每张 2 槽**；**SB 写材质覆盖**（**运行时遮罩面**，不做导出源） | RGBA8 ×N（N ≤ 8） | 材质 | 只进 AC，由 AC 叠出下游消费的图 |
| `Emission` | — | **占位**：契约里登记名字，**lilToon 侧没有 pass 写它 ⇒ 不分配通道** | — | 无 | 无 |

- `Selection` = **运行时遮罩面**（材质逐像素的具名遮罩 + 不许丢），不做导出源；导出/点选走 OB 的**身份池**（Cryptomatte 语义面）。OB 的 `Selection` 与 SB 是**同一批固定语义槽**（**槽号 = 语义**，由 OB 单方声明、帧间不变：默认 4 槽 = 今天的材质位 0~3，可配 8 / 16），**名字里不带 Cryptomatte**。**SB 只写材质侧的覆盖率、按槽覆盖 OB**，AC 按 `object < surface` 叠；**布局固定 ⇒ 天然对齐，SB 不参与同步、不得自造槽或 ID**（见 `Ho-ObjectBuffer_规划.md` §1.3）。
- `Normal` 与 GB 的几何法线是**并列的两个量**：GB 出几何法线（遮挡、阴影偏移、描边），SB 出着色法线（PBR、SSR）。
- `Emission` 的字段语义等 lilToon 侧有 pass 写它时再冻结。

### 1.1 换算与开关（冻结）

```text
roughness = perceptualRoughness²
F0        = lerp(reflectance, saturate(baseColor), metallic)
_UseReflection = 0                → 所有 reflection strength 归零
_UsePlanarReflection             → 只在总开关打开时生效
```

### 1.2 约束（冻结）

- **不发布深度、不发布身份**；SB 的 depth-stencil 只服务自己的两段式深度（opaque/cutout 写深度、transparent 只 ZTest 后叠加）。
- **GB / OB / SB 并列，producer 互不读**（交叉 gate 必须显式登记）。
- **不与别的语义打包**：几何 → GB，效果调参 → 材质轻量参数，屏幕空间产物 → 各效果自己的通道。
- 采样 **Point**（材质值属于最近的那个面）。
- `Color.a` 不承载覆盖率；覆盖率只有 OB/AC 一个来源。

---

## 2. 名字（冻结）

### 2.1 全局纹理名

| 名字 | 桥接期对应什么 |
| --- | --- |
| `_HoSurfaceBufferColorTexture` | `_HoMetadataBufferSurfaceColorTexture` |
| `_HoSurfaceBufferNormalTexture` | 无（今天没有着色法线；GB 只有几何法线） |
| `_HoSurfaceBufferMaterialTexture` | `_HoMetadataBufferReflectionMaterialTexture` 的 R/G（`perceptualRoughness` / `metallic`）+ `_HoMetadataBufferSurfaceDataTexture` 的 R（`thickness`） |
| `_HoSurfaceBufferReflectionTexture` | `_HoMetadataBufferReflectionMaterialTexture` 的 B/A（`reflectance` / `plrStrength`） |
| `_HoSurfaceBufferClassificationTexture` | `_HoMetadataBufferSurfaceDataTexture` 的 G/B/A |
| `_HoSurfaceBufferSelectionTexture` | 无（新增） |

契约通道名走契约 v2 的点分家族名，不在这里定；契约 v2 需按 §3 模板登记本表，并在 §5 变更记录里记下两条 v1 冻结条款的修订（`SurfaceColor.a = coverage` 与 `ReflectionMaterial` 的 Target5 拆分），标为桥接期。

### 2.2 材质侧属性名

| SB 字段 | 材质侧名字 | 状态 |
| --- | --- | --- |
| 平滑度 / 金属度 | `_Smoothness` / `_Metallic`（及贴图） | 已有 |
| 反射总开关 / 平面反射开关 | `_UseReflection` / `_UsePlanarReflection` | 已有 |
| `Reflection.g` = `plrStrength` | `_PlanarReflectionStrength`（`Range(0,1)`，默认 1） | 已有 |
| 反射其余调参 | `_PlanarReflectionMinSmoothness` / `EdgeFade` / `FadeStart` / `FadeEnd` / `Tint` / `FlipY` | 已有；**是材质轻量参数（shading 时用），不进 SB 通道** |
| `Material.b` = `thickness` | `_SSSThicknessMap.r` → `_SSSThicknessInvert` → `pow(·, _SSSPower)` → `× _SSSStrength × _HoSSSThicknessScale`，再与 `saturate(_HoMetadataBufferThickness)` 取 max | 已有 |
| `Classification.r` = `materialClass` | `_HoSSSProfileId`（byte 编码 profile id） | 已有 |
| `Classification.b` = `transmittanceHint` | `_HoSSSTransmissionStrength` | 已有 |
| `Classification.g` = `curvature` | `_HoSurfaceCurvature` | **新增** |
| `Color.rgb` | `fd.col`——lilToon 主色链（主色 × `_MainTex`，两/三层叠加已在其中），**不是单独采样 `_MainTex`** | 已有 |

### 2.3 命名规则（冻结）

- SB 的材质参数一律用 **`_HoSSS*` / `_HoSurface*` 前缀**；**不再新增 `_HoMetadataBuffer*`**，那套随 MetadataBuffer 一起退役。
- **不用 MPB**（决策 13：MPB 破坏 SRP Batcher）。曲率、`materialClass`、`transmittanceHint` 今天靠 `HoMetadataBufferSubject` 组件 MPB 逐 renderer 覆盖，全部改为材质参数。

---

## 3. 执行

| 步骤 | 内容 | 验收 |
| --- | --- | --- |
| **1** | 名字进契约 v2（§3 模板 + §5 变更记录） | 六张纹理名与材质侧属性名登记完毕 |
| **2** | 建 feature 骨架：`HoSurfaceBuffer*` + 自用深度 + RG/兼容两条路径 | 空跑不报错，debug 视图可见 |
| **3** | **先落地 `Color` + `Material` + `Reflection`**（反射优先），再 `Normal` | 与桥接源逐像素一致（可 A/B 对比）；PLR/SSR 切过去后行为不变 |
| **4** | 反射侧停止新增 Target5 消费者 → 桥接残留清干净 | `_HoMetadataBufferReflectionMaterialTexture` 无消费者 |
| **5** | 迁 `Classification`（SSS 的 thickness/curvature/class/transmittance） | SSS 行为不变 |
| **6** | 与 OB/AC 一起进 R6：删 MetadataBuffer 的 surface 族 | 无 `_HoMetadataBuffer` surface 族引用 |

---

## 4. 冻结决议

1. `Material` 与 `Reflection` **不并**（各自一张 RT）。
2. `Normal` **只存着色法线**（`octa`，含法线贴图）；几何法线留在 GB。
3. `Emission` **登记为占位**：契约留名，不分配通道、不阻塞本轮。
4. `Selection` = **与 OB 同一批固定语义槽**（`R=id0,G=cov0,B=id1,A=cov1`，每张 2 槽；**槽号 = 语义**，由 OB 单方声明，**默认 4 槽、可配 8 / 16、上限 8 张**）；**SB 写材质覆盖、按槽覆盖 OB**，AC 按 `object < surface` 叠；**布局固定 ⇒ 天然对齐，SB 不参与同步**。名字表容量与 OB 同量级（≤256 具名）。
5. `materialClass` **留在 SB 的 `Classification`**（不进 OB 的表）。
6. `plrStrength` 材质侧是 `Range(0,1)` ⇒ **8 bit 足够**。
7. 表面色来源 = **`fd.col`**（lilToon 主色链）。
8. **去掉 `* subjectValid` 预乘**（表面色与四类值都带）：SB 的输出是该像素的表面真值，valid 由 OB/AC 表达。
9. **去掉"四值全 0 就不写"的 gate**：改成照常写。0 是**合法值**（曲率 0 = 平的），拿 0 兼任"没元数据"会让消费者分不清"平"和"没写"；需要"未指定"时用显式位。
10. **六张纹理名冻结**：`_HoSurfaceBuffer{Color,Normal,Material,Reflection,Classification,Selection}Texture`。
11. **材质侧参数**：复用 `_HoSSSProfileId` / `_HoSSSThicknessScale` / `_HoSSSTransmissionStrength`，**只新增 `_HoSurfaceCurvature`**；**不用 MPB**。
12. `target` 序与 `Target5` 这类 slot 号是**桥接期**叫法，落定后一律改用 SB 纹理名。

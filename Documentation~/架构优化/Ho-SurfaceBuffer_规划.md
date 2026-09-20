# Ho-SurfaceBuffer（SB）规划

表面数值 buffer：回答"表面是什么样"。**几何在 GB，身份与逐物体属性在 OB，合成在 AC。**

> **状态：五张数值 RT、SurfaceOwner validity、Classification 四通道、同 SemanticId 的 surface sample 协议与 4/8/16 lane batching 已冻结。**

---

## 0. 流水线复核与 P0 勘误（2026-09-20）

### 0.1 SB 用 SurfaceOwner 表达显式 validity

规划同时声明“0 是合法表面值”、“不写 `subjectValid`”、“valid 由 OB/AC 表达”，但 GB/OB/SB producer 又被定义为互不读。这要求必须冻结一条可验证的对齐契约：

- OB 身份 layer 0 与 SB 表面值必须代表同一个前表面，使用同一 camera descriptor、layer/render-queue filter、clip/dissolve/cull 和深度胜出规则。
- SB 数值 pass 与五张数值 RT 同时写一张 **internal `_HoSurfaceBufferOwnerTexture`**（R16_UINT），内容是该前表面的 16-bit IdentityId。0 = 无 writer；数值 RT 中的 0 始终是合法值。
- AC 只在 `SurfaceOwner == OB ranked layer 0 IdentityId` 时接受 SB 数值；不匹配使用属性 fallback 并写 alignment diagnostic。
- Texture-level valid（这张图本帧是否存在）与 pixel-level valid（这个像素是否有 writer）必须分开。

### 0.2 Classification 四通道冻结

当前 shader 在 SSS 启用时把 `_HoSSSProfileId` 写入 MB `surfaceData.b`，否则才写 `_HoMetadataBufferMaterialClass` 的 hash-like 标量。而 SSS 消费端明确把该 byte 当 profile ID 精确比较。因此 SB 不能把 `Classification.r` 同时冻结为通用 `materialClass` 和 SSS profile。

冻结为：`R=sssProfileIdByte`、`G=curvatureHint`、`B=transmittanceHint`、`A=materialClassIdByte`。profile 与通用 class 都是精确 byte ID，采样后用 `round(v*255)` 还原；G/B 是连续 UNORM 值。

### 0.3 “不用 MPB”与旧 `_HoMetadataBuffer*` 来源相互矛盾

- thickness 规划仍与 `_HoMetadataBufferThickness` 取 max；curvature/transmittance/materialClass 的当前来源也是 `HoMetadataBufferSubject` MPB。
- 如果 SB 禁止 MPB 并且不再新增 `_HoMetadataBuffer*`，就至少要冻结新的材质侧 `_HoSurfaceThickness`、`_HoSurfaceCurvature`、`_HoSurfaceTransmittanceHint`，以及 classification/profile 的最终名字。
- `_HoSSSThicknessScale` 是缩放参数，不是独立 thickness fallback 的替代品。

新材质侧名字冻结为 `_HoSurfaceThickness`、`_HoSurfaceCurvature`、`_HoSurfaceTransmittanceHint`、`_HoSurfaceMaterialClassId`；SSS profile 复用 `_HoSSSProfileId`。

### 0.4 SB surface semantic sample 与 batching

- SB semantic pass 与数值 pass 分开，因为 semantic 保留 MSAA sample，不能与单采样数值 RT 混用附件。
- 每个 lane 逐 sample 写 `(SemanticId,value)`；ID=0 是未写，ID=声明值且 value=0 是显式 0。一张 RGBA8MS 存两个 lane。
- 每个 batch 同时写 `_HoSurfaceSemanticOwnerMS`（R16_UINT MSAA），AC 与 OB 的 per-sample IdentityId 校验后再合成。
- 4 lane = owner + 2 RT；8 lane = owner + 4 RT；16 lane = 两个 8-lane batch，每 batch 都重写 owner + 4 RT。每趟最多 5 MRT。
- 中间图命名为 `_HoSurfaceSemanticOwnerMS`、`_HoSurfaceSemanticLane{0..7}MS`；都是 internal RenderGraph 句柄，不发布给业务消费者。

### 0.5 透明表面必须单独定义

“opaque/cutout 写 depth，transparent 只 ZTest 后叠加”不足以定义逐像素表面真值：多层透明的 roughness/normal/classification 不能用普通 alpha blend 得到唯一表面。SB 必须为每个字段选择一个策略：前表面近似、按 alpha 预乘合成，或对 transparent 不生产。在此之前不得把 SB 语义泛化为“最终画面的表面真值”。

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
| `Classification` | `r` | `sssProfileIdByte` | RGBA8 | 材质 | SSS（经 AC） |
| | `g` | `curvature` | | | SSS |
| | `b` | `transmittanceHint` | | | SSS |
| | `a` | `materialClassIdByte` | | | ScreenProcess / 其他分类消费者（经 AC） |
| `SurfaceSemantic` | 成对 | 每 lane `(SemanticId,value)`，两 lane / RGBA8MS；另有 per-batch owner ID | RGBA8MS + R16_UINT MSAA | 材质 | 只给 AC semantic resolve |
| `Emission` | — | **占位**：契约里登记名字，**lilToon 侧没有 pass 写它 ⇒ 不分配通道** | — | 无 | 无 |

- SurfaceSemantic 承接材质逐像素的具名 SemanticId/value，不做导出源；SB 只使用 `HoSemanticSchema` 声明的 ID/lane。AC 在 sample 级合成后才产生对外 Selection coverage。
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
| `_HoSurfaceBufferOwnerTexture` | 无（新增；internal validity/alignment key） |
| `_HoSurfaceSemanticOwnerMS` / `_HoSurfaceSemanticLane{0..7}MS` | 无（新增；internal MSAA semantic source） |

契约通道名走契约 v2 的点分家族名，不在这里定；契约 v2 需按 §3 模板登记本表，并在 §5 变更记录里记下两条 v1 冻结条款的修订（`SurfaceColor.a = coverage` 与 `ReflectionMaterial` 的 Target5 拆分），标为桥接期。

### 2.2 材质侧属性名

| SB 字段 | 材质侧名字 | 状态 |
| --- | --- | --- |
| 平滑度 / 金属度 | `_Smoothness` / `_Metallic`（及贴图） | 已有 |
| 反射总开关 / 平面反射开关 | `_UseReflection` / `_UsePlanarReflection` | 已有 |
| `Reflection.g` = `plrStrength` | `_PlanarReflectionStrength`（`Range(0,1)`，默认 1） | 已有 |
| 反射其余调参 | `_PlanarReflectionMinSmoothness` / `EdgeFade` / `FadeStart` / `FadeEnd` / `Tint` / `FlipY` | 已有；**是材质轻量参数（shading 时用），不进 SB 通道** |
| `Material.b` = `thickness` | `_SSSThicknessMap.r` 链 + `_HoSSSThicknessScale`；无贴图/非 SSS fallback 改为 `_HoSurfaceThickness`，不再读 `_HoMetadataBufferThickness` | `_HoSurfaceThickness` **新增** |
| `Classification.r` = `sssProfileIdByte` | `_HoSSSProfileId` | 已有 |
| `Classification.b` = `transmittanceHint` | `_HoSurfaceTransmittanceHint`；是否由 `_HoSSSTransmissionStrength/Radius` 派生作为 bridge 需单独记录 | **新增** |
| `Classification.g` = `curvatureHint` | `_HoSurfaceCurvature` | **新增** |
| `Classification.a` = `materialClassIdByte` | `_HoSurfaceMaterialClassId` | **新增** |
| `Color.rgb` | `fd.col`——lilToon 主色链（主色 × `_MainTex`，两/三层叠加已在其中），**不是单独采样 `_MainTex`** | 已有 |

### 2.3 命名规则（冻结）

- SB 的材质参数一律用 **`_HoSSS*` / `_HoSurface*` 前缀**；**不再新增 `_HoMetadataBuffer*`**，那套随 MetadataBuffer 一起退役。
- **不用 MPB**（决策 13：MPB 破坏 SRP Batcher）。曲率、`materialClass`、`transmittanceHint` 今天靠 `HoMetadataBufferSubject` 组件 MPB 逐 renderer 覆盖，全部改为材质参数。
- Volume：**`HoSurfaceBufferVolume`**（调试入口）；UI 按 `Ho-UI_风格规范.md`——**调试在 Volume，feature 只放高级设置 + 兜底默认值**。

---

## 3. 执行

| 步骤 | 内容 | 验收 |
| --- | --- | --- |
| **1** | 名字进契约 v2（§3 模板 + §5 变更记录） | 五张数值纹理 + internal SurfaceOwner + semantic owner/lane MSAA 句柄登记完毕 |
| **2** | 建 feature 骨架：单采样数值 pass + 独立 MSAA semantic pass/batches + 自用深度 + RG/兼容两条路径 | 4/8/16 lane 分别是 1/1/2 趟 semantic 几何 pass；owner 与 OB sample ID 可校验 |
| **3** | **先落地 `Color` + `Material` + `Reflection`**（反射优先），再 `Normal` | 与桥接源逐像素一致（可 A/B 对比）；PLR/SSR 切过去后行为不变 |
| **4** | 反射侧停止新增 Target5 消费者 → 桥接残留清干净 | `_HoMetadataBufferReflectionMaterialTexture` 无消费者 |
| **5** | 迁 `Classification`（R=profile ID、G=curvatureHint、B=transmittanceHint、A=materialClass ID） | SSS profile 精确 byte 比较不变；通用 class 不再与 profile 混用 |
| **6** | 与 OB/AC 一起进 R6：删 MetadataBuffer 的 surface 族 | 无 `_HoMetadataBuffer` surface 族引用 |
| **全程** | 五张数值图、SurfaceOwner/对齐错误、每个 semantic lane 的 ID/value/written 都进 `HoDebugViewRegistry` / DebugTile | 显式 0、未写、owner mismatch 和非法 SemanticId 都能区分 |

---

## 4. 冻结决议

1. `Material` 与 `Reflection` **不并**（各自一张 RT）。
2. `Normal` **只存着色法线**（`octa`，含法线贴图）；几何法线留在 GB。
3. `Emission` **登记为占位**：契约留名，不分配通道、不阻塞本轮。
4. SurfaceSemantic 逐 sample 写 `(SemanticId,value)` + owner IdentityId；AC 在 resolve 前与 object 语义合成。ID=0 是未写，ID!=0/value=0 是显式 0。
5. Classification 冻结为 `sssProfileIdByte / curvatureHint / transmittanceHint / materialClassIdByte`。
6. `plrStrength` 材质侧是 `Range(0,1)` ⇒ **8 bit 足够**。
7. 表面色来源 = **`fd.col`**（lilToon 主色链）。
8. 数值 RT 不预乘 `subjectValid`；用 `_HoSurfaceBufferOwnerTexture` 的 16-bit owner 校验 pixel validity 与 OB layer-0 对齐。
9. 0 是合法值；数值 pass 用 owner=0 表示未写，semantic pass 用 SemanticId=0 表示未写。
10. 对外只发布 `Color/Normal/Material/Reflection/Classification`；SurfaceOwner 与 semantic owner/lane MSAA 纹理是 AC 依赖的 internal ContextItem 句柄。
11. **材质侧参数**：禁止新 `_HoMetadataBuffer*` 与 MPB 路径；新增 `_HoSurfaceThickness`、`_HoSurfaceCurvature`、`_HoSurfaceTransmittanceHint`、`_HoSurfaceMaterialClassId`，profile 复用 `_HoSSSProfileId`。
12. `target` 序与 `Target5` 这类 slot 号是**桥接期**叫法，落定后一律改用 SB 纹理名。
13. **调试与登记是落地的一部分**：五张数值图、owner alignment 与每个 surface semantic lane 都有 debug 视图。
14. **UI 按 `Ho-UI_风格规范.md`**：调试入口在 **`HoSurfaceBufferVolume`**，feature 里只放高级设置 + 兜底默认值。

# Ho-SurfaceBuffer（SB）规划

表面数值 buffer：回答"表面是什么样"。**几何在 GB，身份与逐物体属性在 OB，合成在 AC。**

> **状态：五张数值 RT、SurfaceOwner validity、Classification 四通道、同 SemanticId 的 surface sample 协议与 4/8/16 lane batching 已冻结。**

> **落地状态（R3-sb，数值面 + 语义 lane 本轮）**：SB 的**数值面**已经可以跑：
> - 已落地：`Runtime/SurfaceBuffer/`（feature + 数值 pass + 语义 lane pass + 资源集 + 格式工具 + Volume/调试直出）、材质侧 `lil_pass_surface_buffer.hlsl` / `lil_pass_surface_semantic.hlsl`（两者共用 `lil_pass_surface_common.hlsl` 的几何 + alpha clip 前半段）与 22 个 URP lilblock 的 `LightMode=HoSurfaceBuffer` / `HoSurfaceSemantic` 两个 pass、5 个新材质属性（`_HoSurfaceThickness` / `_HoSurfaceCurvature` / `_HoSurfaceTransmittanceHint` / `_HoSurfaceMaterialClassId` / `_HoSemanticWeight`）。
> - **owner 用两个字节（RGBA8 的 R/G）承载 16-bit IdentityId**（规划写的是 `R16_UINT`，此处按可采样性改）：0..65535 逐值精确，且消费端仍是普通浮点采样；`R16_UINT` 需要整数纹理通道，会污染所有消费端。语义 lane 的 owner 同布局。
> - **透明不生产**：队列上限压在上不透明段末尾（`GeometryLast`），对应 §0.5 的第三种策略（"对 transparent 不生产"）；其余两种策略等定了再放开。
> - **语义 lane（§0.4 的 8-lane 档）已落地**：材质侧 `HoSurfaceSemantic` 逐 sample 写 `owner + 8 条 (SemanticId, value)`（4 张 RGBA8MS，5 MRT + 自用 MSAA 深度）；**自建 MSAA，与相机 AA 解耦**（请求 4x，实际值问平台，跟 OB 的同一条决策），AC 按 `_HO_SURFACE_SEMANTIC_MSAA_2/_4` 逐 sample Load。**SB 只覆盖 OB 语义**——材质先按 palette 表读自己 renderer 的物体位，只写它真有的那几位，lane → SemanticId / 物体位掩码由 `HoSemanticSchema` 上传，材质侧只有一个 `_HoSemanticWeight`（0..1，默认 1）。16-lane 分批、材质侧遮罩贴图、语义 lane 的调试视图（MSAA 要 Load）都还没做。
> - **还没落地**：`Classification` 的消费者迁移（SSS 仍读 MB 的 `surfaceData`）、DebugTile 登记（需要给 Debug 轴加 `HoDebugViewRenderKind`，SB 自带整屏调试不受影响）、语义 lane 的调试视图（MSAA 要 Load）。**AC 的 surface 合成已落地**（逐 sample 的五种 sourceMode，见 AC 规划 §落地状态）。
> - **已知待办**：SB / 语义 pass 的材质参数（`_HoSurface*` / `_HoSSS*` / `_HoSemanticWeight`）目前是**全局声明**、不在 `UnityPerMaterial` 里 ⇒ 生成的 lilToon shader 对 SRP Batcher 不兼容（`lil_common_input.hlsl` 的 CBUFFER 才是 SRP Batcher 认的那一份）。要单独做一次迁移把它们并进 CBUFFER。
> - **验收口径**：五张图与桥接源（MB 的 `SurfaceColor` / `ReflectionMaterial` / `SurfaceData`）逐像素 A/B 一致；owner 视图（调试模式 6）绿 = 与 OB 层 0 对齐、红 = 不一致或没人写、洋红 = OB 没产出。

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
| 语义 lane 的值（权重） | `_HoSemanticWeight`（标量 0..1，默认 1）× `_HoSemanticWeightTex`（遮罩，R 通道；不填 = 白） | **新增**（规划 §2.3 的"材质可写语义"落点；**不新增语义名字**，lane 用物体位那 8 条） |
| `Color.rgb` | `fd.col`——lilToon 主色链（主色 × `_MainTex`，两/三层叠加已在其中），**不是单独采样 `_MainTex`** | 已有 |

### 2.3 命名规则（冻结）

- SB 的材质参数一律用 **`_HoSSS*` / `_HoSurface*` 前缀**；**不再新增 `_HoMetadataBuffer*`**，那套随 MetadataBuffer 一起退役。
- **不用 MPB**（决策 13：MPB 破坏 SRP Batcher）。曲率、`materialClass`、`transmittanceHint` 今天靠 `HoMetadataBufferSubject` 组件 MPB 逐 renderer 覆盖，全部改为材质参数。
- Volume：**`HoSurfaceBufferVolume`**（调试入口）；UI 按 `Ho-UI_风格规范.md`——**调试在 Volume，feature 只放高级设置 + 兜底默认值**。

---

## 3. 执行

| 步骤 | 内容 | 验收 |
| --- | --- | --- |
| **1**（部分） | 名字进契约 v2（§3 模板 + §5 变更记录） | 五张数值纹理 + internal SurfaceOwner 已按 §2.1 定名落地；semantic owner/lane MSAA 句柄待 semantic pass |
| **2**（部分） | 建 feature 骨架：单采样数值 pass + 自用深度 + RG/兼容两条路径 | ✅ 数值 pass 已落地；语义 lane pass 已落地（§0.3.7 的 8-lane 档：owner + 4 张 RGBA8MS，采样数跟相机）；16-lane 分批未做 |
| **3**（部分） | 落地 `Color` + `Material` + `Reflection` + `Normal` + `Classification`（一次写全，省得把同一个 pass 改三遍） | ✅ 五张一起写了；**A/B 一致待实机验证** |
| **4** | 反射侧停止新增 Target5 消费者 → 桥接残留清干净 | `_HoMetadataBufferReflectionMaterialTexture` 无消费者 |
| **5** | 迁 `Classification`（R=profile ID、G=curvatureHint、B=transmittanceHint、A=materialClass ID） | SSS profile 精确 byte 比较不变；通用 class 不再与 profile 混用 |
| **6** | 与 OB/AC 一起进 R6：删 MetadataBuffer 的 surface 族 | 无 `_HoMetadataBuffer` surface 族引用 |
| **全程** | 五张数值图、SurfaceOwner/对齐错误、每个 semantic lane 的 ID/value/written 都进 `HoDebugViewRegistry` / DebugTile | ✅ 五张 + owner 有整屏调试；DebugTile 登记待 Debug 轴加 RenderKind |

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

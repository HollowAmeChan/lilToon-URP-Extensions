# Ho-SurfaceBuffer（SB）规划

表面数值 buffer：回答"表面是什么样"。**几何在 GB，身份与逐物体属性在 OB，合成在 AC。**

---

## 1. 设计

### 1.1 冻结字段（语义以 `LILTON_FORMAL_PIPELINE_DRAFT_V2.md` §3.1 为准）

| 上游名 | 通道 | 语义 | 格式 | 生产端 | 消费端 |
| --- | --- | --- | --- | --- | --- |
| SB `Color` | `rgb` | linear HDR base color，producer 不钳制；**无 coverage 语义** | RGBA16F | 材质 | 角色特化·脸色扩散、SSS、PLR、AOV `diffuse_albedo` |
| SB `Normal` | `rg` | `octa(shadingNormal)`——**含法线贴图**；`ba` 备用 | RGBA8 | 材质 | PBR 光照、SSR 反射方向、后续吃法线贴图的效果 |
| SB `Material` | `r` | **`perceptualRoughness`**（不是 linear roughness） | RGBA8 | 材质 | PLR / SSR / Probe |
| | `g` | `metallic` | | | PLR / PBR |
| | `b` | `thickness` | | | SSS / 透射 |
| | `a` | 备用 | | | |
| SB `Reflection` | `r` | `reflectance`（F0 的标量近似） | RGBA8 | 材质 | PLR / SSR / Probe |
| | `g` | `plrStrength`（= `_PlanarReflectionStrength`，材质侧 0..1；受 `_UseReflection` × `_UsePlanarReflection` 门控） | | | PLR |
| | `ba` | 备用 | | | |
| SB `Classification` | `r` | `materialClass` | RGBA8 | 材质 | SSS / ScreenProcess（经 AC） |
| | `g` | `curvature` | | | SSS |
| | `b` | `transmittanceHint` | | | SSS |
| | `a` | 备用 | | | |
| SB `Selection` | 成对 | `R=id0, G=cov0, B=id1, A=cov1`；**本 feature 自己的具名选择** | RGBA8 ×N | 材质 | 只进 AC，由 AC 叠出下游消费的图 |
| SB `Emission` | — | **占位**：契约里登记了 `emission`，但 **lilToon 侧还没有 pass 写它** | — | 无 | 无 |

（`Selection` 与 OB 的 selection 同构，**名字里不带 Cryptomatte**。`Emission` 只是占坑：**没有生产端就不分配通道**，字段语义等 lilToon 侧有 pass 时再冻结。）

### 1.2 冻结的换算与开关

```text
roughness = perceptualRoughness²
F0        = lerp(reflectance, saturate(baseColor), metallic)
_UseReflection = 0                → 所有 reflection strength 归零
_UsePlanarReflection             → 只在总开关打开时生效
```

### 1.3 约束

- **不发布深度、不发布身份**；SB 的 depth-stencil 只服务自己的两段式深度（opaque/cutout 写深度、transparent 只 ZTest 后叠加）。
- **GB / OB / SB 并列，producer 互不读**（交叉 gate 必须显式登记）。
- **不与别的语义打包**：几何 → GB，效果调参 → 材质轻量参数，屏幕空间产物 → 各效果自己的通道。
- 采样 **Point**（材质值属于最近的那个面）。
- `Color.a` 不承载覆盖率；覆盖率只有 OB/AC 一个来源。

---

## 2. 参数名冻结（本次 review）

### 2.1 全局纹理名（待你点头后写进契约）

| 提议 | 今天对应什么（桥接） |
| --- | --- |
| `_HoSurfaceBufferColorTexture` | `_HoMetadataBufferSurfaceColorTexture` |
| `_HoSurfaceBufferNormalTexture` | 无（今天没有着色法线；GB 只有几何法线） |
| `_HoSurfaceBufferMaterialTexture` | `_HoMetadataBufferReflectionMaterialTexture` 的 R/G（`perceptualRoughness` / `metallic`）+ `_HoMetadataBufferSurfaceDataTexture` 的 R（`thickness`） |
| `_HoSurfaceBufferReflectionTexture` | `_HoMetadataBufferReflectionMaterialTexture` 的 B/A（`reflectance` / `plrStrength`） |
| `_HoSurfaceBufferClassificationTexture` | `_HoMetadataBufferSurfaceDataTexture` 的 G/B/A |
| `_HoSurfaceBufferSelectionTexture` | 无（新增） |

### 2.2 材质侧 shader 属性名（跨仓）

| 用途 | 名字 | 状态 |
| --- | --- | --- |
| 平滑度 / 金属度 | `_Smoothness` / `_Metallic`（及贴图） | 已有（反射文档 §4 冻结按 glTF/PBR 习惯解释） |
| 反射总开关 / 平面反射开关 | `_UseReflection` / `_UsePlanarReflection` | 已有 |
| 厚度 `/` 曲率 `/` 透射提示 `/` 材质分类 | **今天不是材质参数**：四个都是 shader 全局量（`lil_pass_metadata_buffer.hlsl` 第 64/70/71/72 行声明），由 `HoMetadataBufferPass` 每趟置 0、再由 **`HoMetadataBufferSubject` 组件用 MPB 逐 renderer 覆盖** | ⏳ **SB 必须决定它们从哪来**（见下） |

**这四类值的真实来源（已核对 lilToon + 本仓库）**：

| 值 | 今天的算法 | 材质侧有没有源 |
| --- | --- | --- |
| `thickness` | `saturate(_HoMetadataBufferThickness)` 与 **SSS 厚度贴图链取 max**：`_SSSThicknessMap.r` → `_SSSThicknessInvert` → `pow(·, _SSSPower)` → `× _SSSStrength × _HoSSSThicknessScale` | ✅ **有**：`_SSSThicknessMap` / `_SSSThicknessInvert` / `_SSSPower` / `_SSSStrength` 都是 lilToon 材质参数 |
| `curvature` | `saturate(abs(_HoMetadataBufferCurvature))` + 一个 boost（`lilHoMetadataBufferResolveCurvatureBoost()`） | ❌ 只有组件/MPB |
| `transmittanceHint` | `saturate(_HoMetadataBufferTransmittanceHint)` | ❌ 只有组件/MPB |
| `materialClass` | `lilHoMetadataBufferEncodeScalar(_HoMetadataBufferMaterialClass)`（编码标量） | ❌ 只有组件/MPB |

另有两个必须一起处理的细节：

1. **四者都被 `* subjectValid` 预乘**（第 453-456 行），与表面色同一个模式 ⇒ 迁移到 SB 时要一起去掉。
2. **有一个"全 0 就不写"的 gate**（第 300 行）：四个值全为 0 时 `hasSurfaceMetadata = 0`，也就是**今天的 `surfaceData` 对没有元数据的对象是不写的**。SB 要么保留这个语义（用 valid 表达），要么改成"总是写"。

**已定：加材质侧参数（不再走 MPB）**。lilToon 侧的 Ho 前缀材质属性**已经存在**（见 `Documentation~/HoAOVTrueSSSDesign.md` §材质接口），所以不是从零加：

| SB 字段 | 材质侧名字 | 状态 |
| --- | --- | --- |
| `Material.b` = `thickness` | `_SSSThicknessMap` / `_SSSThicknessInvert` / `_SSSPower` / `_SSSStrength`（贴图链）+ **`_HoSSSThicknessScale`** | ✅ 全部已有 |
| `Classification.r` = `materialClass` | **`_HoSSSProfileId`**（byte 编码的 profile id） | ✅ 已有（`surfaceData.b` 今天就写它） |
| `Classification.b` = `transmittanceHint` | **`_HoSSSTransmissionStrength`** | ✅ 已有 |
| `Classification.g` = `curvature` | **`_HoSurfaceCurvature`** | ➕ **新增**（今天只有 `_HoMetadataBufferCurvature` 走 MPB） |

**同时决定的两件迁移细节**：

1. **去掉 `* subjectValid` 预乘**（表面色与四类值都带，第 453-456 行）——SB 的输出是该像素的表面真值，valid 由 OB/AC 表达。
2. **去掉"四值全 0 就不写"的 gate**（第 300 行）——改成照常写。理由：参数化之后 0 是**合法值**（曲率 0 = 平的），用 0 兼任"没元数据"会让消费者分不清"平"和"没写"。需要"未指定"时用显式位。

**规则**：SB 的材质参数一律用 **`_HoSSS*` / `_HoSurface*` 前缀**，**不再新增 `_HoMetadataBuffer*` 挂名**；`_HoMetadataBuffer*` 那套随 MetadataBuffer 一起退役。**不用 MPB**（决策 13：MPB 破坏 SRP Batcher）。
| PLR 强度 | **`_PlanarReflectionStrength`**（lilToon 侧 `Range(0,1)`，默认 1）——已在 lilToon 仓库核对 | ✅ 冻结 |
| PLR 其它材质参数 | `_PlanarReflectionMinSmoothness` / `_PlanarReflectionEdgeFade` / `_PlanarReflectionFadeStart` / `_PlanarReflectionFadeEnd` / `_PlanarReflectionTint` / `_PlanarReflectionFlipY` | ✅ 已有；**它们是材质轻量参数（shading 时用），不进 SB 的通道** |
| 表面色 | **`fd.col`**——lilToon 的主色链（主色 × `_MainTex`，两/三层叠加也已在里面），**不是单独的 `_MainTex` 采样** | ✅ 已核对 `lil_pass_metadata_buffer.hlsl`：`lilHoMetadataBufferResolveSurfaceColor(fd.col)`（`.rgb` 不钳制、`.a` 走 coverage 解析），最后 `return half4(surfaceColor * subjectValid)` |

### 2.3 本次 review 查出的错名

| 错在哪 | 现状 | 正确 |
| --- | --- | --- |
| `Material.r = roughness` | 我写的 | **`perceptualRoughness`**；linear roughness = 它的平方 |
| `_Surface` / `_Surface.a` | 我在 CB/SB 规划里当简写用 | 真实名是 `_HoMetadataBufferSurfaceColorTexture`；SB 落地后是 `_HoSurfaceBufferColorTexture` |
| `surface.color` / `surface.material` / `surface.reflection` / `surface.classification` | 我提的契约通道名 | **未冻结**，待契约 v2 定（可沿用点分家族名） |
| `Target5` | V2/反射文档里的 slot 号 | 那是**桥接期**的叫法；落定后应改用 SB 的纹理名 |
| `_HoSurfaceBufferId0/Id1/Coverage` | 我写的（SB 也存身份） | **作废**：SB 只存 `Selection`（+ 表面数值），身份在 OB |
| `_HoSurfaceBufferLobes` | 我预留的高级叶瓣 | **未冻结**：V2 §3.1 只认 `Normal` / `Emission`；`Lobes` 不进本轮 |
| `_HoSurfaceBufferEmission` | 我写的 | V2 §3.1 的"新增"行里有 `Emission`，但**字段语义未冻结** → 暂列待定 |

---

## 3. 执行

| 步骤 | 内容 | 验收 |
| --- | --- | --- |
| **1** | 冻结 §1.1 的字段与 §2.1 的纹理名 + §2.2 的材质侧属性名 | 名字进契约 v2（走 §3 模板 + §5 变更记录） |
| **2** | 建 feature 骨架：`HoSurfaceBuffer*` + 自用深度 + RG/兼容两条路径 | 空跑不报错，debug 视图可见 |
| **3** | **先落地 `Color` + `Material` + `Reflection`**（反射优先），再 `Normal` | 与桥接源逐像素一致（可 A/B 对比）；PLR/SSR 切过去后行为不变 |
| **4** | 反射侧停止新增 Target5 消费者 → 桥接残留清干净 | `_HoMetadataBufferReflectionMaterialTexture` 无消费者 |
| **5** | 迁 `Classification`（SSS 的 thickness/curvature/class/transmittance） | SSS 行为不变 |
| **6** | 与 OB/AC 一起进 R6：删 MetadataBuffer 的 surface 族 | 无 `_HoMetadataBuffer` surface 族引用 |

## 4. 已定 / 待定

**已定**：

1. `Material` 与 `Reflection` **不并**（各自一张 RT）。
2. `Emission` **登记为占位**：契约留名，**lilToon 侧还没有 pass 写它 ⇒ 不分配通道、不阻塞本轮**。
3. `Selection` 层数与 OB **一致**（8 层/像素）。
4. `materialClass` **留在 SB 的 `Classification`**（不进 OB 的表）。
5. `plrStrength` 材质侧是 `Range(0,1)` ⇒ **8 bit 足够**（原先担心的 ">1" 不成立）。
6. 表面色来源 = **`fd.col`**（lilToon 主色链）；**桥接期的 `* subjectValid` 预乘必须去掉**。

**待定**：

1. **厚度 / 曲率 / 透射提示 / 材质分类的材质侧属性名**：今天挂 `_HoMetadataBufferThickness` 这套 + Subject 组件，SB 要用自己的名字（并查清它们今天**从哪读**：材质参数 / 贴图 / Subject 写入）。
2. **`Selection` 的名字表容量**（OB 侧是 ≤256 具名；SB 是否同量级）。

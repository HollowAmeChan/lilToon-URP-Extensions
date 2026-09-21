# 正式管线草案 v0.1（定稿）

> ⛔ **本文已归档（2026 文档审核）**：三轴输入 + 属性合成层的重新串联后来落到
> [`Ho-管线总览.md`](../Ho-管线总览.md)（并已实现），本文只作历史记录：它记下了当时的事实基线、
> 实机 12 项 feature 清单、ShadowCast 多光策略与 OIT 难点，这些内容已被吸收进总览或各 feature 文档。
> 当时认为已被取代的部分（层模型 / 帧序 / 旧→新映射 / 命名分析 / 决策落点）见总览。

> 状态：**方案定稿**（待实现推进）。通道契约冻结见 `LILTOON_CHANNEL_CONTRACT_V1.md`（v1，独立文档）。
> 事实基线：**正式场景 = `D:\Unity_Project\BREAK_URP\Assets\mmd场景测试\朱木古堂\New Scene.unity`**；渲染器 = `Assets\Settings\PC_Renderer.asset`（12 项挂载）。
> 角色材质域参考 = `Hollow\Hiro\Hiro_M_*.mat`（14 个）。
> 关联文档：`LILTOON_RENDER_PIPELINE_REVIEW_AND_PLAN.md`（评审与边界）、`lilToon-URP-Extensions/Documentation~/RPComponentRework/RPComponentRework_验收文档.md`（组件边界）。
> 定位：**按需纸面契约**（非 HDRP 式固定 GBuffer 编码）。通道随需求登记、无消费者不输出、AOV 命名冻结。
> 反射章节已被 `Documentation~/ReflectionPipelineDesign.md` 取代；本文只保留总体帧序和跨系统背景，不再作为 PLR/SSR 输入契约。

---

## 0. 摘要

- 场景实际使用的能力域：**toon 阴影（投影+接收）＋ 屏幕空间 AO ＋ fake SSS（皮肤/身体）＋ 平面反射 ＋ rim/emission ＋ 描边 ＋ 角色特化（眼透/发影/脸色）＋ 加权 OIT（透明）＋ 屏幕 GI（HTrace SSGI，待自研替换）**。
- 目标拓扑：**前向着色为权威 ＋ 按需语义通道（CharacterBuffer/ScreenGeometryBuffer）＋ ScreenProcess 统一施加 ＋ ImageProcess 图像链 ＋ 独立 AOV 输出层 ＋ DebugTile**。**不做完整 GBuffer/延迟光照**。
- **HTrace AO/SSGI 是唯一占位符**：通道契约已预留（`ao`/`gi`/`gisexclude`），生产端将来可插拔替换；材质侧意图参数不动。
- 推进铁律：**先做 7 个系统（OIT/GI/shadow/反射折射透射/AO/SSS/多光），后收敛 lilToon 暴露面**；每系统落地时登记材质接口脚印。

---

## 1. 实机渲染特性（✅ 已核对 PC_Renderer.asset，12 项）

| # | 实机名 | 目标栈名 | 状态 | 参数 |
| --- | --- | --- | --- | --- |
| 1 | Ho-GeometryBuffer | `Ho-GeometryBuffer` | ✅ | normal/depth |
| 2 | Ho-MetadataBuffer | `Ho-MetadataBuffer` | ✅ | 语义通道 |
| 3 | HTrace AO | **占位（自研 AO）** | ✅ | asset 值 `Downsample=0, Intensity=3, Direct=0.25, Radius=0.035, Samples=1, BlurQuality=0`（参考；最终以场景 profile 为准） |
| 4 | Ho-PlanarReflection | `Ho-PlanarReflection` | ✅ | PLR source；opaque ForwardLit PBR 消费，特殊 composite 默认关闭 |
| 5 | Ho-WeightedOIT | `Ho-WeightedOIT` | ✅ | |
| 6 | Ho-ShadowCast | `Ho-ShadowCast` | ✅ | PCSS 档见 §8 |
| 7 | Ho-SubsurfaceScattering | `Ho-SubsurfaceScattering` | ✅ | `radius=24` |
| 8 | Ho-CharacterSpecialization | `Ho-CharacterSpecialization` | ✅ | 眼透+发影+脸色 |
| 9 | HTrace SSGI | **占位（自研 GI）** | ✅ | ⚠️ 描边白边已知 bug（`gisexclude` 为正式解） |
| 10 | Ho-ScreenProcess | `Ho-ScreenProcess` | ✅ | 底层语义屏幕效果 |
| 11 | Ho-ImageProcess | `Ho-ImageProcess` | ✅ | 最终图像链 |
| 12 | Ho-DebugTile | `Ho-DebugTile` | ☐ 禁用 | 调试时开启 |

> **与收口文档"推荐顺序"的差异**（正式化时定夺是否重排）：实机 = Geometry → Metadata → AO → PlanarReflection → OIT → ShadowCast → SSS → CharSpec → SSGI → ScreenProcess → ImageProcess；推荐 = ShadowCast → Metadata → Geometry → SSS → OIT → CharSpec → ScreenProcess → ImageProcess → DebugTile。
> 差异点：① Geometry/Metadata 谁先；② AO 在 SSS 前；③ OIT 在 ShadowCast 前；④ SSGI 在 CharSpec 后。
> 策略：**正式模板先按实机排**（实机这套是调满意的），差异点列入 §7 待确认。

### 1.1 资产"旧名"说明（已核实，非旧栈）

- 12 个挂载 feature 的脚本 GUID **全部**解析到当前新类；Inspector 显示正常。旧名串（如 `HoPostProcessRendererFeature`）只是 ScriptableObject `m_Name` 实例名，创建时写入、不随类改名——**纯展示串，Unity 靠 GUID 加载**。
- 资产里 2 个 `guid 42fcb77b`（旧 HoAov）= 已删除类的**死引用孤儿**（不在挂载列表，无害，建议在 Unity 删掉对象本身）。
- Unity 只重写有 dirty 标记的对象——"旧名不刷新"属正常，非保存失败。
- Hiro 场景 profile 存在 `HoPostProcessStackVolume` ×1、`ShoostPostProcessStackVolume` ×2 **Missing-Script** 组件（当前代码已无此二类）——该场景已不是基线，清理与否不影响正式基线；建议顺手删。

---

## 2. 场景基线（朱木古堂 / New Scene.unity）

- **灯光：40 盏** = 1×Directional（强度 1.0，软阴影 shadowType=2）+ 39×Point（强度 1.92~4.65，**全部无阴影**）——多光 + ShadowCast 收集的用例场景。
- **Volume Profile**（guid c58d3be7）：**当前新栈、无 Missing 组件**——`HTraceSSGIVolume ✅ / HTraceAOVolume ✅ / HoCharacterSpecializationVolume ✅ / ScreenProcessStackVolume ✅ / ImageProcessStackVolume ✅`。
- **HTrace SSGI 参数（场景 profile，自研替换位默认档）**：
  `Enable=1 DebugMode=0; ExcludeCasting/ReceivingMask=0(Nothing); FallbackType=1(天空) SkyIntensity=1; DenoiseFallback=1 Multibounce=1 AmbientOverride=0; ViewBias=0.1 NormalBias=0.25 SamplingNoise=0.1; BackfaceLighting=0.811 MaxRayLength=max ThicknessMode=0 Thickness=0.512; Intensity=5 Falloff=0 RayCount=3 StepCount=32 RefineIntersection=1; FullResolutionDepth=1 Checkerboard=1 RenderScale=1; BrightnessClamp=0 MaxValue=8.6 MaxDeviation=3; HalfStepValidation=1 SpatialOcclusion=1 TemporalLighting=1 TemporalOcclusion=1; SpatialRadius=0.6 Adaptivity=0.9; RecurrentBlur=0 FireflySuppression=1 ShowBowels=0`（`(-)`项 = 未 override，用 HTrace 默认）。
- **HTrace AO 参数（场景 profile，自研替换位默认档）**：`Enable=1, AmbientOcclusionMode=1(GTAO), Intensity=3.06, GTAOWorldSpaceRadius=5, DirectLightingOcclusion=1`；其余 HTrace 默认（GTAOThickness 0.2、ScreenSpaceRadius 25、Slice 2、Step 16、Denoising 3、Temporal 8、FilterRadius 0.5、Adaptivity 0.5）。renderer asset 里的 feature 级值是旧参考，**以场景 profile 为准**。
- **材质**：舞台 55 个材质，shader = `df12117e`×49 + `85d6126c`×4 + `e66172d3`×2；前两个与角色 **共用** lilToon 变体（角色/场景一套主材质家族，+1 场景专用变体）。
- **角色材质域**（Hiro 14 材质）：toon 阴影接收（Body/Head/Cloth×5/Hair/Blush；Hair 与 Eye/眉=0）、屏幕 AO（几乎全部，Hair_DropShadow 除外；多数同时有旧 `_UseSSAO=1` 需合并清理）、fake SSS（Body/Head）、反射（Body/Cloth×5/Hair）、rim（ClothDown×2/ClothUp/Hair）、emission（Eye/Eyebrow）、outline（全部，宽 0.08~1，ClothUp=1 特殊）。

---

## 3. 管线框架（目标栈）

### 3.1 层模型（5 层）

```text
[L0 灯光/阴影]   ShadowCast（附加灯 atlas + URP 主光阴影）
[L1 语义输入]    CharacterBuffer(Metadata)（对象/mask/surface）+ ScreenGeometryBuffer（normal/depth）
[L2 屏幕效果]    ScreenProcess：AO / GI / SSS / SSR/特殊反射 resolve / 角色特化 / OIT
[L3 图像链]      ImageProcess（只读 camera color）
[L4 输出/调试]   AOV 导出层（多通道 EXR） + DebugTile
```

### 3.2 帧序（目标）

```text
[1]  URP 主光阴影（PCF 软档；URP17 无内置 PCSS）
[2]  Ho-ShadowCast（附加灯 cast 组）                 → shadow.main / shadow.add0..N
[3]  Ho-CharacterBuffer(MetadataBuffer)             → 语义（对象/mask/surface/objectCustom bits）
[4]  Ho-ScreenGeometryBuffer(GeometryBuffer)        → normal/depth
[5]  Ho-GTAO（独立 feature）                        → ao / aointent
[6]  Ho-SSGI（独立 feature；读 gisexclude）          → gi
[7]  Ho-SubsurfaceScattering                        → sss
[8]  Ho-WeightedOIT（透明合成，最难搞）              → 见 §8
[9]  Ho-PlanarReflection（PLR source；特殊 composite 可选） → reflection
[10] Ho-CharacterSpecialization（眼透/发影/脸色）    → eyecolor/eyedata
[11] Ho-ScreenProcess（其余语义效果）
[12] Ho-ImageProcess（最终图像链）
[13] AOV 导出层（Editor/批处理，可选）
[14] DebugTile（调试时最后）
```

> 次序依据：PLR source 在主相机渲染前更新并由 opaque ForwardLit 消费；AO/GI/SSR 需要 depth/normal 与 opaque 后颜色；SSS 合成须早于透明/OIT；特殊透明反射 resolve 才放在透明阶段。精确 pass event 以实机 Frame Debugger 为准。

### 3.3 旧→新映射（一次性资产迁移）

```text
HoAovRendererFeature           -> Ho-MetadataBuffer
HoPostProcessRendererFeature   -> Ho-ScreenProcess
ShoostPostProcessRendererFeature -> Ho-ImageProcess
（顺序按 §3.2；重复项删掉）
```

---

## 4. 通道 / AOV / 声明模板

**→ 见 `LILTOON_CHANNEL_CONTRACT_V1.md`（已冻结）**。本草案不再重复通道表、AOV 导出清单与声明模板；新增/变更通道一律走该文档的登记流程（无消费者不登记、不输出）。

---

## 5. HTrace → 自研替换位（唯一占位区）

1. **AO**：生产端可插拔。当前 lilToon 材质侧意图参数收敛为 `_UseScreenSpaceAO`/`_SSAOStrength`/`_SSAORemap`/`_SSAOContrast`/`_SSAOMask`；只消费公共 `_HoAOTexture`，生产端由 Ho-GTAO 提供。
2. **GI**：自研 SSGI **必须**读 `gisexclude`（描边/非物理表面 → 排除 receiver/caster）——描边白边已知 bug 的**正式解决方案位**；材质侧只加 `giStrength/mask` 意图，生产端换 HTrace。
3. **验收口径**：替换后材质/场景零改动（除开关）；Frame Debugger 可见输入/输出；debug tile 直接可见。

---

## 6. 设计决策（写死进契约）

> 一句话：**先做系统、后收敛材质暴露面；效果由管线/材质预设提供，用户只留轻量调参。**

### 6.1 理由

- lilToon 是 VRC 血统，**暴露面太宽**（渲染模式、render queue、stencil、混合、UV/顶点色用途都能被用户乱改）；VRC 时代眼透靠硬改模板/测试——问题多、不可控、不可审计。
- 本管线已原生支持（眼透/发影/脸色 = 材质只给对象语义，管线合成），**用户不应再需要、也不应被允许去改结构性项**。
- **但不能现在就裁剪**：OIT/GI/shadow/反射折射透射/AO/SSS 会决定"材质接口的正确数量"。先定系统接口，后收敛，避免裁了又开。

### 6.2 排序铁律

```text
7 个系统做稳（OIT / GI / shadow / 反射折射透射 / AO / SSS / 多光）
→ 每系统落地时登记"材质接口脚印"到通道契约 + AOV
→ 最后统一收敛 lilToon 暴露面（VRC 式乱改项 → 管线/preset/隐藏）
```

### 6.3 暴露面三分类

| 类别 | 谁决定 | 例子 |
| --- | --- | --- |
| 管线/Preset 决定（用户不可碰） | 管线 feature / 材质预设 | 渲染模式、queue、stencil/模板测试、混合模式、pass 结构、UV/顶点色用途、是否进 OIT、反射来源 |
| 材质轻量参数（用户可调） | 材质 | 各效果强度/颜色/遮罩/范围/混合权重 |
| 用户不可见 | 管线 | 内部 RT、临时通道、debug 触发器 |

### 6.4 材质接口脚印（各系统落地时照此登记）

| 系统 | 管线/preset 决定 | 材质轻量参数（用户可调） |
| --- | --- | --- |
| OIT | 是否进 OIT、accumulation/revealage 结构 | 透明权重/响应（可并入预设） |
| GI | 是否生成 GI、`gisexclude`（描边/非物理排除）、自研质量档 | `giStrength` / `giMask` |
| Shadow | 附加灯收集/PCSS/atlas、主光阴影 | toon 门控现有项（border/blur/ramp，本就属于材质） |
| 反射 | PLR/SSR/Probe/Sky 来源与消费边界 | 见 `ReflectionPipelineDesign.md` |
| 透射/折射 | camera color/透明资源契约、折射路径 | 厚度/吸收/强度/菲涅尔（收成预设） |
| AO | Ho-GTAO 生产端、toon remap 档 | `_SSAOStrength/Remap/Contrast/Mask`，`_UseScreenSpaceAO` 为材质开关 |
| SSS | profile 列表、扩散/透射 kernel、quality | sss strength/mask/tint（收进 profile preset） |

> 注：AO/SSS 意图参数在材质里**已存在**（`_SSAO*`、`_UseSSS`/`_SSS*`）——系统做完后，lilToon 收敛 = 保留这 7 组轻量参数，其余 VRC 式开关收进 presets/隐藏，**是收敛不是重写**。

---

## 7. 待确认

1. **帧序差异**（§1）：Geometry/Metadata 先后、AO 在 SSS 前、OIT 在 ShadowCast 前、SSGI 在 CharSpec 后——是否保留实机顺序（默认保留）。
2. **ShadowCast PCSS 档**：`pcssEnabled=1` 已知，其余参数待最终确认（PCSS 本体暂不做，见占位文档）。
3. **灯光接管范围**：ShadowCast 是否接管全部 39 点光（其价值所在），或仅部分。
4. **matte bits 分配**：objectCustom 0-7 位语义命名（frontHair/face/eye/body/…）——按 §9.4，位含义沿用 `HoMetadataBufferGroup/Subject` 组件语义，通道契约只登记"以组件 Inspector 为准"。
5. **反射后续**：只在 `ReflectionPipelineDesign.md` 维护，本文不重复登记参数默认值。
6. **组内容量**（每 atlas slice 上限，应对灯数变态）默认值。

---

## 8. ShadowCast 多光收集策略（基于 40 灯基线）

> 目的：把"39 点光 + 1 平行光"做成可预期、可调、可导出的多光方案。**先定策略，不动代码。**

### 8.1 现状

- 40 盏算少的，未来可能上百/上千；URP Forward 附加光每盏实时阴影默认关闭；toon 材质对"哪盏灯、多强、有没有阴影"没有统一门控；AOV 无法按灯分开。
- **URP17 无内置 PCSS**（源码 0 命中，只有 PCF `_SHADOWS_SOFT_LOW/MEDIUM/HIGH`）→ 自研，本轮不做（占位文档）。
- **ShadowCast 现状 = 两张 atlas**：`AtlasTexture`（附加灯/punctual，灯按 slice/block 排布）+ `SecondDirectionalAtlasTexture`（第二方向光）——**这就是"cast 分组"模型，不是每灯一张**。
- **容量现状（已实现）**：`Light Capacity` 档位（Low/Medium/High = 12/24/48 盏）只约束"同时采样的附加灯数"，也就是逐像素采样循环上限；切片数**不按档位写死**，而是由图集尺寸与分辨率算出（`floor(atlasSize / resolution)^2`，混合光型由装箱器决定），硬上限是固定数组长度 `HO_SHADOW_CAST_ARRAY_SLICES` = 128 片。数组长度固定是 Unity 的硬约束（全局数组槽位长度在会话内被缓存且只允许变小，调大需重启编辑器）。数值契约：`Runtime/ShadowCast/HoShadowCastShaderContract.cs` + `Runtime/ShadowCast/Shaders/HoShadowCastShaderContract.hlsl`，一致性由 `Editor/ShadowCast/HoShadowCastShaderContractValidator.cs` 守住。

### 8.2 收集策略（cast 分组，非每灯一槽）

1. **主光 cast 单独一路**：URP 主光阴影（PCF 软档）一路；ShadowCast 的路**永不与主光合并**。
2. **ShadowCast = cast 组列表**：每组 = 一张 atlas，组内灯按 slice/block 排布（现有实现即是）。组字段：`id/用途（main-additional|second-directional|character-face|tilted-far|custom）｜光源类型｜分辨率/过滤/层掩码/强度｜组内容量（slice 上限，按组内 atlas 几何算出，见 §8.1 容量现状）`。
3. **首版 2 组**（现状维持）；**组上限 N = 8**。
4. **专用组（只规划）**：① 角色脸部高精度 cast 组；② 灯光特倾斜的远平面 cast 组——"新分组蓝图"（占位文档），加组≠改框架。
5. **灯数变态策略**：组内容量有限 → 超出按距离/强度/重要性留最近/最强 N 盏投影，其余仅光照；组固定、灯可替换。
6. **shader 接口**：分组索引/强度/半径常量数组；材质按 mask 选组，**不知道灯数**；toon 门控（border/blur/ramp）照旧。
7. **AOV**：`shadow.main`（URP 主光）+ `shadow.add0..N`（N≤8 **组**，不是灯）分开导出；不做逐灯分光（YAGNI）。
8. **降级/回退**：超容量/不可见/无掩码灯 → 跳过；关闭 ShadowCast = 附加灯无投影但仍照亮。

### 8.3 验收

1. 39 点光下：不爆 atlas、材质阴影不串色、近处 2-4 盏有投影且过渡可调。
2. Frame Debugger 可见 atlas 写入与衰减读取；`shadow` 通道 debug tile 可见。
3. AOV `shadow.main`/`shadow.add0..N` 在 Nuke 可分开调。
4. 关闭 ShadowCast → 附加灯无投影仍正常照亮。

---

## 9. OIT（最难搞）

- **难点**：① MSAA/RenderGraph 下 accumulation/revealage 的 clear/绑定；② 与"透明材质接收 SSS/反射/GI"的顺序交互（OIT 合成前 vs 后）；③ 与后处理、MotionVectors、DepthNormals 的透明语义一致性；④ 半透 + 描边/轮廓排序。
- **策略（首版）**：只做 **Weighted 合成**一条路（不做 depth peeling）；透明材质 `OIT mode: Off/Weighted` 可配；SSS/反射/GI 先按 opaque 域处理，透明域在 OIT 之后再做语义效果（帧序 [7][8][9]）。
- **验收**：头发/玻璃多层透明无排序爆闪；Frame Debugger 可见 accumulation/revealage；关 OIT 回退普通透明。
- 首版不做：depth peeling、per-object 排序增强、MSAA×OIT 完整组合（渲染环境可接受 offscreen + 2x）。

---

## 10. 命名分析：CharacterBuffer / ScreenGeometryBuffer

| Buffer | 语义定位 | 内容 | 主服务对象 | 消费方 |
| --- | --- | --- | --- | --- |
| `MetadataBuffer` | 材质/对象/语义（forward-style，随材质 pass 写） | maskId(object/group/features)、surfaceData、custom0、objectCustom0-1、SurfaceColor、MBufferDepth | **角色/对象语义**（face/hair/eye/groupId/objectCustom bits） | 角色特化、SSS、AOV |
| `GeometryBuffer` | 屏幕可见几何（deferred-style） | normal、depth | **全屏几何**（不只角色） | SSS/AO/GI/反射/角色特化/AOV |

**结论（采纳"角色是大头"直觉）**：

1. `MetadataBuffer` → **`CharacterBuffer`**（角色/对象语义，直观）。
2. `GeometryBuffer` → **`ScreenGeometryBuffer`**（强调屏幕几何，与 CharacterBuffer 并列、语义一眼区分；不带"角色"名，因为它是全屏消费）。
3. 两者**并列、禁止互读**；改名属迁移（收口文档、`Ho-*` 渲染器名、shader 常量 `_HoGeometryBuffer*`、debug tile/tag）——**v1 冻结时批量做**，避免中途两套名。
4. **matte bits 不另定**：`objectCustom0-1` 位含义沿用 `HoMetadataBufferGroup/Subject` 组件语义；通道契约只登记"位含义以组件 Inspector 为准"。

---

## 11. 决策落点（汇总）

- **GTAO**：第一版只做一个独立 `Ho-GTAO` RendererFeature；参数以自研后为准（HTrace 值仅供参考，不绑定契约）。
- **SSGI**：第一版只做独立 `Ho-SSGI`（替换 HTrace；`gisexclude` 位必做）。
- **motion**：✅ 转正占坑（通道 `motion` + AOV；动态模糊/Nuke 要用，先登记不实现）。
- **PCSS**：暂不做（`LILTOON_SHADOW_PCSS_PLACEHOLDER.md`）。
- **专用 cast 组（角色脸/远平面）**：只规划（`LILTOON_SPECIAL_CAST_PLACEHOLDER.md`），首版不做。
- **ShadowCast 分组模型**：cast 分组（每组一张 atlas、灯按 slice 排布）；首版 2 组、上限 N=8；`shadow.add0..N` 的 N 指"组"不是"灯"。
- **Hiro Missing-Script 清理**：可选（该场景不再是基线）。
- **推进顺序**：Ho-GTAO（独立 feature）→ Ho-SSGI（含 `gisexclude`）→ 其余系统。

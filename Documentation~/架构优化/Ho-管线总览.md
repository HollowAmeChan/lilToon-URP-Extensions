# Ho 管线总览

> **状态：现行总览**（2026 文档审核时把原来的两份草案合并到这里；**三轴 producer + AC 合成器已经落地**，
> 本文描述的是完成形态，历史草案见 `_归档/LILTOON_FORMAL_PIPELINE_DRAFT_v0.1.md`）。
> 分轴细节：`Ho-ObjectBuffer.md`（OB）/ `Ho-SurfaceBuffer.md`（SB）/ `Ho-AttributeComposite.md`（AC）/
> `GeometryBuffer.md`（GB）；边界：`架构边界/MSAA.md`、`架构边界/语义掩码.md`；UI：`Ho-UI_风格规范.md`。
> 事实基线：正式场景 = `D:\Unity_Project\BREAK_URP\Assets\mmd场景测试\朱木古堂\New Scene.unity`，
> 渲染器 = `Assets\Settings\PC_Renderer.asset`。

## 0. 摘要

- **拓扑**：前向着色为权威 + 按需语义通道 + 运行时合成 + 屏幕效果 + 图像链 + 独立 AOV 导出 + DebugTile。
  **不做完整 GBuffer / 延迟光照**（按需纸面契约，通道随需求登记、无消费者不输出、AOV 命名冻结）。
- **三条输入轴并列，互不读取**：**GB**（几何在哪、朝向、几何覆盖）/ **OB**（这是谁、占多少）/
  **SB**（表面长什么样）。**AC** 是唯一的语义与属性合成器（读 OB + SB），消费者只经 `HoAC_*` 查询。
- **`Cryptomatte` 在 GB/OB/SB/AC 里一个名字都不用**：合规的 `crypto_*` 导出（float 位重解释 + manifest + 32 bit）
  由以后的独立 AOV/export feature 负责，经 AC 资源集读 OB 身份池与 runtime catalog。
- **唯一占位符是外部 AO/GI**：通道契约已备好（`ao` / `gi` / `gisexclude`），生产端已由自研 `Ho-GTAO` /
  `Ho-SSGI` 接上，材质侧意图参数不动。
- **推进铁律**：先做系统（OIT / GI / shadow / 反射折射透射 / AO / SSS / 多光），后收敛 lilToon 暴露面；
  每个系统落地时登记材质接口脚印。

---

## 1. 层模型（6 层）

```text
[L0 灯光/阴影]   ShadowCast（附加灯 atlas + URP 主光阴影）                    → shadow.main / shadow.add0..N
[L1 输入缓冲]    GB（几何轴） / OB（逐物体轴） / SB（表面轴）                  → 见 §3 的归属表
[L2 属性合成]    AC：typed ID 解压 + 逐像素 object/surface 语义合成 + `constant < surface` 数值属性
[L3 屏幕效果]    GTAO / SSGI / SSS / PLR·SSR / 角色特化 / OIT                 → ao, gi, sss, reflection, eyecolor/eyedata …
[L4 图像链]      ImageProcess（只读 camera color）
[L5 输出/调试]   AOV 导出层（多通道 EXR）+ DebugTile
```

**与最早那版（v0.1）的差别**，也是这轮地基的形状：

| v0.1 | 现在 | 理由 |
| --- | --- | --- |
| L1 只有 `CharacterBuffer(Metadata)` + `ScreenGeometryBuffer` | L1 是**三轴并列**：GB / OB / SB | 一个 buffer 不能同时回答"这是谁"和"表面是什么样" |
| 没有属性合成层 | **L2 = AC** | 多来源（纯值 / object / surface）需要一条明确的覆盖链 |
| 语义效果直接读 MetadataBuffer 的五张图 | 语义效果经 **L2** 拿遮罩与属性 | 原来的 20 个 rule source 已删；"没有合成层"的代价就在这儿 |

命名现状：`GeometryBuffer` **名字不改**；原 `CharacterBuffer` 计划落地成了 **`ObjectBuffer`**；
`MetadataBuffer` 已整块删除（R6/R7，见 `CHANGELOG.md`）。

---

## 2. 帧序

| 位置 | 谁 | 前置条件 |
| --- | --- | --- |
| **opaque 之前** | 阴影 → GB / OB / SB → **AC SemanticResolve + AttributeComposite** → **GTAO** | AC 只读 OB/SB，把语义/属性理顺；GTAO 与 opaque ForwardLit 可在登记后查询 AC |
| **opaque 之后** | **SSGI** → SSS → OIT → PLR → 角色特化 → ScreenProcess | SSGI 需要 opaque color；后续消费者读 pre-opaque 产生的 AC 资源 |
| **图像链** | ImageProcess | 只读 camera color |
| **最后** | AOV 导出（可选） → DebugTile | 调试最后 |

```text
[1]  URP 主光阴影
[2]  Ho-ShadowCast（附加灯 cast 组）                        → shadow.main / shadow.add0..N
[2.5] PLR source update（`beginCameraRendering`；不占用 raster pass）
        ── opaque 之前 ──────────────────────────────────────────────
[3]  GB：Ho-GeometryBuffer                                  → 几何法线 / depth / 几何覆盖率（+ 描边视觉壳 / sky 可选）
[4]  OB：Ho-ObjectBuffer                                    → 4 层身份 + 覆盖率（自建 MSAA resolve）+ 朝向
[5]  SB：Ho-SurfaceBuffer                                   → 五张数值图 + owner + 语义 lane（单采样）
[6]  AC：SemanticResolve + AttributeComposite               → Selection 池（每张两条 `(SemanticId, coverage)`）
[6.5] Ho-GTAO（独立 feature）                               → ao / aointent   ★ 必须在 opaque 之前
        ── URP opaque / cutout 常规绘制 ──────────────────────────────
[7]  Ho-SSGI（独立 feature，读 gisexclude）                  → gi              ★ 需要 opaque 后的颜色
[8]  AC 资源由 after-opaque 消费者直接读，不在这里重做一趟合成
[9]  Ho-SubsurfaceScattering                                 → sss
[10] Ho-WeightedOIT（透明合成）
[11] PLR 透明/特殊 resolve（如启用）在 OIT 之后；opaque PLR 已在 ForwardLit 消费
[11.5] URP 常规 transparent 绘制（在 `BeforeRenderingTransparents` 之后）
[12] Ho-CharacterSpecialization（眼透 / 发影 / 脸色）         → eyecolor / eyedata
[13] Ho-ScreenProcess（其余语义效果）
[14] Ho-ImageProcess（最终图像链）
[15] AOV 导出层（可选）
[16] DebugTile（调试时最后）
```

**必须成立的偏序关系**（比线性列表更重要，改 pass event 时照这个检查）：

| 约束 | 为什么 |
| --- | --- |
| `Ho-ShadowCast → 所有效果`（**含 OIT**） | 实机确认 OIT 在 ShadowCast 之后，所以"材质/效果都吃阴影"成立 |
| `GB ↮ OB`、`GB ↮ SB`、`OB ↮ SB` | 三个生产轴默认互不读取；跨轴 gate 只能由具体消费者显式声明 |
| **`GTAO → opaque`** | 材质 forward 里采样 AO |
| **`opaque → SSGI`** | GI 要 opaque 后的颜色 |
| `{OB, SB} → AC → GTAO → opaque` | AC 只读 OB/SB（GB 不喂 AC） |
| `AC → GTAO / opaque ForwardLit / SSS / OIT / PLR / 角色特化 / ScreenProcess` | AC 在 pre-opaque 产出语义与属性；消费者按登记的需求查询 |

> GB/OB/SB/AC/GTAO 默认都在 `BeforeRenderingOpaques`，同事件内的先后由 Renderer Feature 列表表达
> `{GB, OB, SB} → AC → GTAO`。这些 `passEvent` 不是可任意打破偏序的自由项。

**实机校准过的三条顺序**（以实机 Frame Debugger 为准，与推导冲突时按这里）：

| 实机事实 | 它修正了什么 |
| --- | --- |
| **OIT 在 `Ho-ShadowCast` 之后** | "阴影 → 所有效果"含 OIT，OIT 可以吃附加灯 atlas |
| **SSGI 在角色特化之前** | 角色特化不能成为 SSGI 的输入；GI 要用角色相关的排除只能走 `gisexclude` |
| **GB 在 OB 之前** | 这是执行顺序事实，不代表 OB 读 GB；三轴仍互不读取 |

**反射相关偏序**（细节见 `ReflectionPipelineDesign.md` 与 `PlanarReflection.md`）：

```text
PLR source → {OB, SB} → AC → opaque ForwardLit → SSR → OIT → PLR 透明/特殊 resolve
```

Probe/Sky 是 source miss 时的材质 fallback，不另占 fullscreen pass。`_UseReflection = 0` 时所有 reflection
strength 归零，`_UsePlanarReflection` 只能在总开关打开时生效；`roughness = perceptualRoughness²`，
`F0 = lerp(reflectance, saturate(baseColor), metallic)`。

---

## 3. 数据归属与"谁能读谁"

| feature | 回答什么 | 输出 | 不许做 |
| --- | --- | --- | --- |
| **GB**（GeometryBuffer） | 几何在哪、朝向如何、几何覆盖多少 | 几何法线 / depth / 几何覆盖率 / 描边视觉壳 / sky | 不发表面数值、不发身份、不发着色法线 |
| **OB**（Ho-ObjectBuffer） | 这是谁、占多少 | 4 层 IdentityId + coverage、朝向（layer 0）、部件行表标签位 | 不发表面数值 |
| **SB**（Ho-SurfaceBuffer） | 表面是什么样 | 五张数值图 + owner；语义 lane `(SemanticId, value)` + semantic owner | 不定义物体身份；owner 只是 AC 对齐键 |
| **AC**（Ho-AttributeComposite） | typed ID 怎么解压、同 SemanticId 怎么合成 | Selection 池 + typed API + runtime catalog；身份池引用 | 不画几何/表面、不写 EXR |

**三条读的规矩**：

1. **三者并列、禁止互读**：需要跨轴的量由消费端在一次读取里各取一份；只有登记过的具体 gate 才能形成额外依赖。
2. **遮罩只有一个来源 = AC**：任何屏幕效果都不许自己再攒一套语义图。
3. **消费者读合成结果，不读原始通道**；`crypto_*` ID/manifest 只属于 AOV 导出层。

**法线分家**（已定论）：**GB = 几何法线**（AO/GI 遮挡、shadow bias、物理遮挡、描边）；
**SB = 着色法线**（PBR 光照、SSR 反射方向，以及后续一切吃法线贴图的 feature）。两者平行、不互为来源。

### 3.1 最小契约（现状）

| 轴/字段 | 冻结语义 | 现状 |
| --- | --- | --- |
| GB `NormalDepth.rgb` | world geometric normal，编码 `normal * 0.5 + 0.5` | ✅ |
| GB `NormalDepth.a` | linear eye depth；`a > 1e-4` 才算 physical coverage | ✅ |
| GB `Depth.r` | raw/device depth；只供明确登记的 AO/Hi-Z 消费者 | ✅ |
| OB `Id0` / `Id1` / `Coverage` | 4 层 ranked `(组 8 bit, 槽位 8 bit)` + 4 层覆盖率；ID 点采样 + `round(v*255)`；覆盖率线性、不归一化。请求 N=4，实际 4/2/1；只承诺"每 sample 唯一前表面 ID"的 `K≥N` 不丢 | ✅（自建 MSAA，与相机 AA 解耦） |
| AC `Selection` | `HoSemanticSchema` 把 8-bit SemanticId 绑到最多 16 个 lane；AC 按 IdentityId 解压 object membership，与 SB 的 `(SemanticId, value)` 按 `sourceMode` 合成，再 resolve 成 `(SemanticId, coverage)` | ✅（4 张 RGBA8 = 8 lane；16-lane 分批未做） |
| SB `Color.rgb` | linear HDR base color；producer 不钳制；`Color.a` 不承载覆盖率 | ✅ |
| SB `Material.rgba` | `perceptualRoughness / metallic / thickness / reserved` | ✅ |
| SB `Reflection.rgba` | `reflectance / plrStrength / reserved / reserved` | ✅ |
| SB `Classification.rgba` | `sssProfileIdByte / curvatureHint / transmittanceHint / materialClassIdByte`；R/A 以 `round(v*255)` 还原 | ✅ |
| SB `SurfaceOwner` | 16-bit IdentityId；0 = 未写；AC 与 OB layer 0 比较后才接受数值属性 | ✅ |
| SB `Normal.rgba` | `octa(shadingNormal).rg` + reserved | ✅ |

`SurfaceColor.a = coverage` 这条旧约定**已随 MetadataBuffer 删除**；覆盖率只有 OB/AC 一个来源。

---

## 4. 材质接口脚印的"载体"

v0.1 的脚印表只写了"管线决定 / 材质轻量参数"，**没写这些数据存在哪** —— 这正是"多出一个 buffer 却没人预料到"的原因。补上载体列：

| 系统 | 数据载体 | 遮罩来源 |
| --- | --- | --- |
| OIT | 无（accumulation / revealage 在管线内） | AC（可选） |
| GI | `gisexclude`（独立通道） | AC |
| Shadow | `shadow.main` / `shadow.add0..N` | — |
| 反射 / PLR | **SB**（roughness / metallic / reflectance / PLR strength） | AC |
| 透射 / 折射 | **SB**（thickness / 吸收 …） | AC |
| AO | GTAO 产物（屏幕空间）+ 材质意图 `_SSAO*` | AC |
| SSS | **SB**（thickness / curvatureHint / 表面色）+ `sssProfileIdByte` | AC |
| 角色特化（眼透 / 发影 / 脸色 / 轮廓） | **OB**（身份池：组 / 部件 / 物体位 / 覆盖率；朝向）+ GB（几何门控） | AC |
| AOV / 导出 | **OB 身份池**（Cryptomatte 语义面）+ SB（albedo） | 合规 `crypto_*` 导出归 AC 直出或以后的独立 feature |

---

## 5. 契约变更（v1 → 现状）

| 契约 v1 的条目 | 现状 |
| --- | --- |
| `maskId`（Target0） | → **OB 身份池**（`Id0` 组 / `Id1` 槽位 / `Coverage`，ranked、常开） |
| `objectCustom0/1`（Target3/4，8 位语义） | → **OB 部件行表的标签位**，不再是两层图；位含义条款作废 |
| `custom0`（Target2，未登记） | → SB 的语义 lane + AC 的 Selection 池 |
| `surfaceData`（Target1） | → **SB** `Material.b` + `Classification`，用 owner 表达显式 validity |
| `reflectionMaterial`（Target5） | → **SB** 具名 `Material` + `Reflection` |
| `surfaceColor` | → **SB** `Color`；`A=coverage` 作废 |
| `sssprofile` | → SB `Classification.r`；**`sssProfileIdByte` 与通用 `materialClass` 已拆开** |
| `motion` | 不变（占坑） |
| 新增 | OB 身份池 / 朝向 / 标签位；SB owner + 单采样语义 lane；AC Selection 池 + typed query + runtime catalog |
| 导出档位 | 现状（UINT 通道）保留；合规 `crypto_*`（float 位重解释 + manifest + 32 bit）仍属未做的独立 AOV/export feature |

契约 v2 走 `LILTOON_CHANNEL_CONTRACT_V1.md` §3 的登记模板 + §5 变更记录；AOV 名只增不改，编码变更必须标明。

---

## 6. Renderer Feature 清单

> 顺序见 §2；这里只登记各 feature 的现状。`Ho-MetadataBuffer` 已删除，不在清单内。

| # | Feature | 现状 |
| --- | --- | --- |
| 1 | Ho-GeometryBuffer | ✅ 几何轴（normal / depth / 几何覆盖率；描边视觉壳与 sky 为可选输出） |
| 2 | Ho-ObjectBuffer | ✅ 身份轴（4 层 `(IdentityId, 覆盖率)` + 朝向；自建 MSAA resolve，与相机 AA 解耦） |
| 3 | Ho-SurfaceBuffer | ✅ 表面轴（五张数值图 + owner + 语义 lane；单采样） |
| 4 | Ho-AttributeComposite | ✅ 语义遮罩与合成属性的唯一逻辑入口（Selection 池 + typed 查询门面 + runtime catalog） |
| 5 | Ho-GTAO | ✅ 自研 AO（原 HTrace AO 占位已替换） |
| 6 | Ho-PlanarReflection | ✅ PLR source；opaque ForwardLit 消费，特殊 composite 默认关闭 |
| 7 | Ho-WeightedOIT | ✅ |
| 8 | Ho-ShadowCast | ✅ 附加灯 cast 组（自带 PCSS，默认开；专用 cast 组只规划） |
| 9 | Ho-SubsurfaceScattering | ✅ |
| 10 | Ho-CharacterSpecialization | ✅ 眼透 / 发影 / 脸色 / 轮廓 |
| 11 | Ho-SSGI | ✅ 自研 GI（含 `gisexclude`；描边白边是已知问题） |
| 12 | Ho-ScreenProcess | ✅ 语义屏幕效果（遮罩吃 AC/OB 的覆盖率） |
| 13 | Ho-ImageProcess | ✅ 最终图像链 |
| 14 | Ho-DebugTile | 调试时开启 |

另有一个独立入口 `Ho-Transparent`：通用透明绘制调度器（接入的 shader 用 `_HoTransparentActive` 跳过自身的 `UniversalForward`，避免重复绘制；它不是 OIT resolve），细节见 `TransparentPass.md`。

**场景基线**（朱木古堂 / `New Scene.unity`）：40 盏灯 = 1×Directional（软阴影）+ 39×Point（全无阴影），
是多光 + ShadowCast 收集的用例场景；Volume Profile 走当前新栈无 Missing；舞台 55 个材质与角色共用 lilToon 变体家族；
角色 14 个材质覆盖 toon 阴影 / 屏幕 AO / fake SSS / 反射 / rim / emission / outline 七类。
（历史上资产里的"旧名"只是 ScriptableObject 实例名，Unity 按 GUID 加载；Hiro 场景已不是基线。）

**ShadowCast 多光策略**（细节见 `PostProcessing/README.md`）：主光阴影与 ShadowCast 永不合流；
ShadowCast = **cast 组**（每组一张 atlas，灯按 slice 排布，首版 2 组、上限 8 组）；
`Light Capacity` 档位只约束"同时采样的附加灯数"，切片数由图集尺寸与分辨率算出
（`floor(atlasSize / resolution)^2`，硬上限 128 片）；全局数组长度固定（Unity 会缓存全局数组槽位长度，
调大需重启编辑器）；数值契约由 `HoShadowCastShaderContract.cs` + hlsl 唯一持有并由校验器守一致性。

---

## 7. 每个 feature 都要做的"调试与登记"

**不是可选项**：没有 debug 视图与登记，通道就等于没落地。OB / SB / AC 三项都已做完，新 feature 照抄：

1. **自己的 debug pass + debug 视图**（每个池 / 每张图一条，能单独看）；
2. **注册视图**：`HoDebugViewInfo` + 进 `HoDebugViewRegistry.AllViews`；
3. **新增 render kind**：`HoDebugViewRenderKind` 加枚举值（每个 feature 一个）；
4. **让 DebugTile 接得上**：`HoDebugTileRendererFeature` 的可用性判定 + `BuildTiles` 过滤 + `ResourceNeeds`；
   `HoDebugTile.shader` 加 slice；`LilUrpDebugShaderValidator` 的收集表；
5. **契约登记的 debug 列**：`LILTOON_CHANNEL_CONTRACT_V1.md` §3 模板逐条填；
6. **失败可见**（不静默）：声明与实际 RT 张数不一致 / 未声明 ID / 一像素 ID 溢出 / 非法槽 /
   消费者声明的名字解析不到 → 视图里标出 + 告警；
7. **UI 按家规写**：调试入口在 Volume（`HoXxxVolume` 的「调试」分组），feature 只放高级设置 + 兜底默认值；
   分节走 `LilUrpEditorSectionGui`，色板与摘要格式见 `Ho-UI_风格规范.md`。

---

## 8. 路线图（已完成 / 剩余）

| 阶段 | 内容 | 状态 |
| --- | --- | --- |
| **R0** | 本文 + 最小契约冻结 + 在旧草案上标注取代关系 | ✅ |
| **R1** | OB 骨架：改名、自建 MSAA 身份池 + 朝向、部件行表标签位、lilToon `HoObjectBuffer` pass | ✅ |
| **R2** | OB 的朝向与消费端切换（眼透角度表等） | ✅ |
| **R3** | SB 落地：五张数值图 + owner + Classification 四通道 + 语义 lane | ✅（lane 为**单采样**；逐 sample 细分以后做） |
| **R4** | AC 落地：schema + typed API + runtime catalog + SemanticResolve + 属性合成 | ✅ |
| **R5** | 消费者输入切换：ScreenProcess（遮罩→覆盖率）、角色特化（→AC）、SSS / PLR（→SB + AC） | ✅ |
| **R6** | 删 MetadataBuffer，契约出 v2 | ✅（见 `CHANGELOG.md` 的 R6/R7 记录） |
| **剩余** | ① 合规 `crypto_*` AOV/export feature；② 透明 PLR 的 receiver/source-id RT（多平面时才立项）；③ AC 的 16-lane MRT 分批（词表 > 8 位才需要）；④ SB 语义 lane 的逐 sample 细分（形态：SB 自渲染 MSAA → 自己 resolve 成单采样再发布）；⑤ ScreenProcess 的"按语义名选遮罩"；⑥ AOV 导出层 | — |

**推进顺序的历史决策**：`Ho-GTAO`（独立 feature）→ `Ho-SSGI`（含 `gisexclude`）→ 其余系统；
R1–R6 属于"其余系统"里的地基工程，与 AO/GI 并行且互不读对方产物。

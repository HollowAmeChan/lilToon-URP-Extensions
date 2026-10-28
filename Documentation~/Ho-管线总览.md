# Ho 管线总览

> **状态：现行总览**（2026 文档审核时把原来的两份草案合并到这里；**三轴 producer + AC 合成器已经落地**，
> 本文描述的是完成形态，历史草案见 `归档/LILTOON_FORMAL_PIPELINE_DRAFT_v0.1.md`）。
> 分轴细节：`Ho-ObjectBuffer.md`（OB）/ `Ho-SurfaceBuffer.md`（SB）/ `Ho-AttributeComposite.md`（AC）/
> `Ho-GeometryBuffer.md`（GB）；边界：`架构边界/MSAA.md`、`架构边界/语义掩码.md`；UI：`Ho-UI_风格规范.md`。
> 事实基线：正式场景 = `D:\Unity_Project\BREAK_URP\Assets\mmd场景测试\朱木古堂\New Scene.unity`（**写作时的文件名**；2026 文档整理时该目录里只剩 `HIRO.unity`，实机以工程为准），
> 渲染器 = `Assets\Settings\PC_Renderer.asset`。

> 2026-10-29 导航补充：角色局部栅格投影见 [Ho-CharacterShadow](Ho-CharacterShadow.md)。[Ho-RT 公共底座与 RTShadow](Ho-RT.md) 已实现公共资源宿主、DI/GI 借用、独立逐灯可见度、SIGMA 与 NPR 主光接收；Ho-RT 放在消费者前，GB / SB 先于 RTShadow，输出经材质 ramp 再到最终颜色。逐灯产品、Volume / 调试和有界 GPU / Player 资格只在该入口维护，本文不复制新参数表；RTDI / RTGI 的原质量与运动支持域仍以模块总文档为准。

## 0. 摘要

- **拓扑**：前向着色为权威 + 按需语义通道 + 运行时合成 + 屏幕效果 + 图像链 + 独立 AOV 导出 + DebugTile。
  **不做完整 GBuffer / 延迟光照**（按需纸面契约，通道随需求登记、无消费者不输出、AOV 命名冻结）。
- **三条输入轴并列，互不读取**：**GB**（几何在哪、朝向、几何覆盖）/ **OB**（这是谁、占多少）/
  **SB**（表面长什么样）。**AC** 是唯一的语义与属性合成器（读 GB + OB + SB），消费者只经 `HoAC_*` 查询。
- **`Cryptomatte` 在 GB/OB/SB/AC 里一个名字都不用**：合规的 `crypto_*` 导出（float 位重解释 + manifest + 32 bit）
  由以后的独立 AOV/export feature 负责，经 AC 资源集读 OB 身份池与 runtime catalog。
- **唯一占位符是外部 AO/GI**：通道契约已备好（`ao` / `gi` / `gisexclude`），生产端已由自研 `Ho-GTAO` /
  `Ho-SSGI` 接上，材质侧意图参数不动。
- **推进铁律**：先做系统（OIT / GI / shadow / 反射折射透射 / AO / SSS / 多光），后收敛 lilToon 暴露面；
  每个系统落地时登记材质接口脚印。

---

## 1. 层模型（6 层）

```text
[L0 栅格灯影]    ShadowCast / CharacterShadow + URP 主光阴影                → shadow.main / shadow.add0..N / 局部 CS
[L1 输入缓冲]    GB（几何轴） / OB（逐物体轴） / SB（表面轴）                  → 见 §3 的归属表
[L2 属性合成]    AC：typed ID 解压 + 逐像素 object/surface 语义合成 + `constant < surface` 数值属性
[L3 照明/效果]   RTDI / RTGI / GTAO / SSGI / SSS / PLR·SSR / 角色特化 / OIT   → 光照或各自效果产品
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
| **CPU 准备 / 相机开始** | GeometryData、Ho-RT 的请求 epoch、PLR source 更新 | Ho-RT 必须在 RT 消费者之前处理 pre-cull；这部分不能只用 GPU Pass Event 排序 |
| **opaque 之前** | 栅格阴影 → GB / OB / SB → **RTShadow**、**AC SemanticResolve + AttributeComposite** → **GTAO** | RTShadow 借 GB / SB 与 host AS，先提供 NPR Cast；AC / GTAO 提供语义与 AO，均在 Forward 前就绪 |
| **opaque 之后** | SSGI 生产；RTDI / RTGI、SSS、体积光、OIT、PLR、角色特化按各 pass 时机执行 | RTDI / RTGI 目前在后续 pass 合成照明，RTShadow 在 Forward 前提供可见度；具体列表与时机看 §2.1，不能把一个多 pass Feature 当单个时间点 |
| **图像链** | ImageProcess | 只读 camera color |
| **最后** | AOV 导出（可选） → 公共调试显示 | 调试最后，不依赖 feature 顺序 |

```text
[1]  URP 主光阴影
[2]  Ho-ShadowCast（附加灯 cast 组）                        → shadow.main / shadow.add0..N
[2.5] PLR source update（`beginCameraRendering`；不占用 raster pass）
        ── opaque 之前 ──────────────────────────────────────────────
[3]  GB：Ho-GeometryBuffer                                  → 几何法线 / depth / 几何覆盖率（+ 描边视觉壳 / sky 可选）
[4]  OB：Ho-ObjectBuffer                                    → 4 层身份 + 覆盖率（自建 MSAA resolve）+ 朝向
[5]  SB：Ho-SurfaceBuffer                                   → 五张数值图 + owner + 语义 lane + Scalar V1 单采样 W/V
[5.5] Ho-RTShadow（借 Ho-RT + GB / SB）                     → 逐灯 V；实际主光 V 在下方 Forward 的 NPR ramp 前接收
[6]  AC：SemanticResolve + AttributeComposite               → Selection 池（每张两条 `(SemanticId, coverage)`）
[6.5] Ho-GTAO（独立 feature）                               → ao / aointent   ★ 必须在 opaque 之前
        ── URP opaque / cutout 常规绘制 ──────────────────────────────
[7]  Ho-SSGI（独立 feature，读 gisexclude）                  → gi              ★ 需要 opaque 后的颜色
[7.5] Ho-RTDI / Ho-RTGI（可选）                           → 独立直接 / 间接光；正常合成在透明前，纯产品可在后处理前
[8]  AC 资源由 after-opaque 消费者直接读，不在这里重做一趟合成
[9]  Ho-SubsurfaceScattering                                 → sss
[10] Ho-WeightedOIT（透明合成）
[11] PLR 透明/特殊 resolve（如启用）在 OIT 之后；opaque PLR 已在 ForwardLit 消费
[11.5] URP 常规 transparent 绘制（在 `BeforeRenderingTransparents` 之后）
[12] Ho-CharacterSpecialization（眼透 / 发影 / 脸色）         → eyecolor / eyedata
[13] Ho-ScreenProcess（其余语义效果）
[14] Ho-ImageProcess（最终图像链）
[15] AOV 导出层（可选）
[16] 模块调试捕获 → 公共最终显示（DebugTile 仅外部系统）
```

**必须成立的偏序关系**（比线性列表更重要，改 pass event 时照这个检查）：

| 约束 | 为什么 |
| --- | --- |
| `Ho-ShadowCast / Ho-CharacterShadow → 对应材质消费者`（**含 OIT**） | 这里指栅格 shadow-map；RTShadow 自己还需要 GB / SB，不能被安排在全部输入缓冲之前 |
| `GB ↮ OB`、`GB ↮ SB`、`OB ↮ SB` | 三个数值生产轴独立；显式例外：OB raw/ranked → SB Scalar V1 关联归约 → AC |
| **`GTAO → opaque`** | 材质 forward 里采样 AO |
| **`opaque → SSGI`** | GI 要 opaque 后的颜色 |
| `{GB, OB, SB} → AC → GTAO → opaque` | AC 消费三轴输入；几何与身份覆盖范围独立，按 typed 查询读取 |
| `Ho-RT → {RTShadow, RTDI, RTGI}` | pre-cull 请求在消费者声明前打开；宿主 Add 准备 / 冻结后消费者才可借 view |
| `{GB, SB} → RTShadow → opaque Forward` | 当前 RTShadow 的位置、法线、深度和 owner 输入来自同域主表面；不能读取尚未 Record 的 SB，也不能挪到 Forward 之后 |
| `Atmosphere → {RTDI, RTGI, VolumeLight}`（启用相应天空输入时） | 借当前相机的天空 HDR / 参数快照；缺失按各模块的显式规则停用天空贡献，不借旧相机结果 |
| `AC → GTAO / opaque ForwardLit / SSS / OIT / PLR / 角色特化 / ScreenProcess` | AC 在 pre-opaque 产出语义与属性；消费者按登记的需求查询 |

> GB/OB/SB/AC/GTAO 默认都在 `BeforeRenderingOpaques`，同事件内的先后由 Renderer Feature 列表表达
> `GB → OB → SB → RTShadow / AC → GTAO`。RTShadow 与 AC 是同阶段的两条消费支路，两者互不依赖，表中采用 RTShadow 在 AC 前的推荐摆法。这些 `passEvent` 不是可任意打破偏序的自由项。

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

### 2.1 推荐 Renderer Feature 列表

下面是默认设置下的推荐列表，未使用的模块可省略。**列表顺序和 Pass Event 必须同时满足**：URP 先按 `renderPassEvent` 稳定排序，同事件保留 Enqueue 顺序，通常就是 Renderer Feature 列表顺序及该 Feature 内的 Enqueue 顺序。CPU 的 pre-cull / source callback 另有阶段约束，不受这个 GPU 排序代替。一个 Feature 有多个 pass 时，主生产、合成与调试分别在自己的时机执行。

| 推荐位置 | Feature | 当前主要阶段 / 默认时机 | 必须先就绪的输入与后续消费者 |
| --- | --- | --- | --- |
| 1 | Ho-GeometryData | 发布与 Tension 为 `BeforeRendering` | 为后续几何 writer、描边与材质提供公共几何数据 |
| 2 | Ho-RT | pre-cull 开 epoch → Add 准备场景 → `AfterRendering` 完成 fence | 列表上在 RTShadow / RTDI / RTGI 前；只管公共资源，不输出最终照明 |
| 3 | Ho-CharacterShadow | `BeforeRenderingShadows` | 局部栅格 Cast 在 Forward 接收前完成 |
| 4 | Ho-ShadowCast | `BeforeRenderingPrePasses`，实际限制为不早于该事件 | 附加灯 atlas 在材质和 OIT 消费前完成 |
| 5 | Ho-Atmosphere | 环境产品 `AfterRenderingPrePasses` | 天空 HDR / 太阳参数供后续 RTDI / RTGI / VolumeLight；具体合成按该模块的 pass |
| 6 | Ho-PlanarReflection | source 在相机开始回调更新；可选特殊 resolve 默认 `BeforeRenderingPostProcessing` | source 在主相机材质消费前完成；特殊 resolve 在 OIT composite 后，不能把列表早放误当所有 pass 都早执行 |
| 7 | Ho-GeometryBuffer | `BeforeRenderingOpaques` | 为 SB 关联检查、RT 主表面和 AC 等生产几何输入 |
| 8 | Ho-ObjectBuffer | `BeforeRenderingOpaques` | 身份与覆盖先于 SB Scalar V1 关联和 AC；并不因此使普通 SB 数值面依赖 OB 纹理 |
| 9 | Ho-SurfaceBuffer | `BeforeRenderingOpaques` | 为 RTShadow / RTDI / RTGI 和 AC 提供表面、深度与 owner；RT 当前要求与 GB 同分辨率、单采样 |
| 10 | Ho-RTShadow | 固定 `BeforeRenderingOpaques` | **同事件时必须在 GB / SB 下方**；逐灯 V、主光 Cast alias 在 opaque / cutout Forward 的 NPR ramp 前可用 |
| 11 | Ho-AttributeComposite | `BeforeRenderingOpaques` | GB / OB / SB 后，GTAO、Forward 与后续语义消费者前 |
| 12 | Ho-GTAO | 固定 pre-opaque `BeforeRenderingOpaques` | AC 后，Forward 接收 AO 前 |
| 13 | Ho-SSGI | 生产默认 `AfterRenderingOpaques`；合成默认 `BeforeRenderingPostProcessing` | 生产要 opaque color 和已就绪 AC；不要把“生产在 SSS 前”误写成“最终合成也一定在 SSS 前” |
| 14 | Ho-RTDI | 正常合成启用时生产 / 合成为 `BeforeRenderingTransparents`；纯产品模式生产为 `BeforeRenderingPostProcessing` | Ho-RT + GB / SB，天空按需借 Atmosphere；若消费其产品，必须检查所选模式的实际生产时机 |
| 15 | Ho-RTGI | 时机同上，拥有独立算法与历史 | 建议在 DI 后叠加；使用 DI 替换模式时，GI 加法应在替换之后，避免被后续替换擦掉 |
| 16 | Ho-SubsurfaceScattering | composite 默认 `BeforeRenderingTransparents` | AC / SB 与要处理的当前 camera color 先就绪 |
| 17 | Ho-VolumeLight | 体积光和 dust 默认 `BeforeRenderingTransparents` | 太阳 / 天空参数、相机深度先就绪；若希望 OIT 使用含体积光的 opaque copy，放在其 copy pass 之前并核实际事件 |
| 18 | WeightedOIT | opaque copy 在 accumulation 前；accumulation 默认 `BeforeRenderingTransparents`、composite 默认 `AfterRenderingTransparents` | 阴影、材质与 opaque 输入先就绪；composite 在角色特化前。opaque copy 默认事件 449，前一行仅调整列表不能让事件 450 的体积光进入该副本 |
| 19 | Ho-CharacterSpecialization | 默认 `AfterRenderingTransparents` | AC / GB / 身份和透明合成先就绪；同事件放在 OIT 下方 |
| 20 | Ho-ScreenProcess | 固定 `AfterRenderingPostProcessing` | 读取已完成语义与正常场景颜色，在最终图像链之前 |
| 21 | Ho-ImageProcess | 固定 `AfterRenderingPostProcessing + 1` | ScreenProcess 之后执行最终图像链；自定义后处理也须声明与其先后关系 |
| 最终显示 | 模块 debug / Ho-DebugTile | debug 捕获默认 `AfterRenderingPostProcessing`，统一 presenter 为 `AfterRendering + 4` | 诊断绕开正常颜色合成，在最终 AA / blit 后呈现；多个模块同时开启按公共确定性规则选择，列表排序不是调试选择器 |
| 按导出需求 | AOV / export 消费者（可选） | 在所需产品完成后、句柄释放前；最终 beauty 则在图像链后读取 | 必须逐输出说明采样时机；不能凭 Feature 名称统一放到一个事件 |

这些位置描述的是组合关系，不要求给独立分支添加不存在的依赖。外部 Feature（例如 Shoost 或自定义后处理）接入时也要声明自己读取的颜色阶段、替换 / 加法语义和对应事件；未声明前，不把某个项目当前列表当通用排序依据。

### 2.2 顺序错误的判定

- **同事件、列表排反**：把消费者放到生产者下方。实际例子为 `RTShadow → SB` 且两者事件均 250，RTShadow Record 时拿不到 SB；即使显示“生产 1”，它也只是已调度，像素仍会全部 `InvalidSurface`，调试状态为红、接收身份为黑。正确为 `SB → RTShadow`。
- **生产事件更晚**：先修 `passEvent`，单独拖列表无效。例如 SB 在 `AfterRenderingOpaques`、RTShadow 在 `BeforeRenderingOpaques`，后者必定先读。RTShadow 要给 Forward 提供 Cast，不能移到 opaque 后来迁就晚到的输入。
- **CPU 准备顺序错误**：例如 Ho-RT 位于 RT 消费者之后，epoch / view 请求会失败；更改某个 GPU pass 的事件不能补这个缺口。
- **生产域不一致**：GB Half / Quarter、MSAA resolve 与 SB 单采样等会使中心采样域不同；排好顺序仍须满足消费者的尺寸、采样和相机合同。

RenderGraph 的资源依赖不能补救尚未创建或发布的 ContextItem 句柄，也不自动替你移动 Feature。现行 RTShadow Inspector 会检查启用状态、GB Full、Ho-RT pre-cull 顺序、GB / SB 的事件与同事件相对位置；这是告警，排序仍由 Renderer 资产显式表达。

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

已登记的数值输入例外：公共 `HoRTPrimarySurfaceInputs` 为 RTShadow / RTDI / RTGI 直接借同相机 GB / SB，执行深度关联和采样域校验，提供世界位置、两种法线及可选 owner。这是原生 RT 查询的主表面数值输入，不另做一套语义遮罩；语义 Selection / 属性覆盖仍走 AC。此例外的生产 / 消费顺序必须遵守 §2。

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
| SB `Material.rgba` | `perceptualRoughness / metallic / thickness / meshNormalAvailable`；A 标记 BA 网格法线是否可供原生 RT 消费 | ✅ |
| SB `Reflection.rgba` | `reflectance / plrStrength / reserved / reserved` | ✅ |
| SB `Classification.rgba` | `sssProfileIdByte / curvatureHint / transmittanceHint / materialClassIdByte`；R/A 以 `round(v*255)` 还原 | ✅ |
| SB `SurfaceOwner` | 16-bit IdentityId；0 = 未写；AC 与 OB layer 0 比较后才接受数值属性 | ✅ |
| SB `Normal.rgba` | `octa(shadingNormal).rg` + `octa(interpolatedMeshNormal).ba`；RT 需求时请求高精度，不把它当 flat triangle normal | ✅ |

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

契约 v2 走 `Ho-ChannelContract-v1.md` §3 的登记模板 + §5 变更记录；AOV 名只增不改，编码变更必须标明。

---

## 6. Renderer Feature 清单

> 推荐顺序与实际事件见 §2.1；下面的编号只是登记号，不是 Renderer 排序。`Ho-MetadataBuffer` 已删除，不在清单内。

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
| 14 | Ho-DebugTile | APV 等外部系统调试；模块调试走自己的入口 |
| 15 | Ho-GeometryData | ✅ 公共描边 / Tension 数据，在几何绘制前发布 |
| 16 | Ho-CharacterShadow | ✅ 角色局部栅格 Cast；与 RTShadow 独立配置 |
| 17 | Ho-Atmosphere | ✅ 天空 HDR 与太阳参数的公共生产者 |
| 18 | Ho-VolumeLight | ✅ 独立体积太阳光与 dust，借已有天空 / 深度 |
| 19 | Ho-RT | ✅ 公共 RT 资源宿主、请求 epoch、共享姿态及 fence 寿命 |
| 20 | Ho-RTShadow | ✅ 逐灯几何可见度、SIGMA 连续 V 与 NPR 主光接收；有界支持域见 Ho-RT |
| 21 | Ho-RTDI | ✅ 独立直接光输出及参考 PBR 替换 / 加法合成；自身历史 |
| 22 | Ho-RTGI | ✅ 独立一跳间接光输出及加法合成；自身历史 |

**场景基线**（朱木古堂 / `New Scene.unity`）：40 盏灯 = 1×Directional（软阴影）+ 39×Point（全无阴影），
是多光 + ShadowCast 收集的用例场景；Volume Profile 走当前新栈无 Missing；舞台 55 个材质与角色共用 lilToon 变体家族；
角色 14 个材质覆盖 toon 阴影 / 屏幕 AO / fake SSS / 反射 / rim / emission / outline 七类。
（历史上资产里的"旧名"只是 ScriptableObject 实例名，Unity 按 GUID 加载；Hiro 场景已不是基线。）

**ShadowCast 多光策略**（细节见 `后处理/README.md`）：主光阴影与 ShadowCast 永不合流；
ShadowCast = **cast 组**（每组一张 atlas，灯按 slice 排布，首版 2 组、上限 8 组）；
`Light Capacity` 档位只约束"同时采样的附加灯数"，切片数由图集尺寸与分辨率算出
（`floor(atlasSize / resolution)^2`，硬上限 128 片）；全局数组长度固定（Unity 会缓存全局数组槽位长度，
调大需重启编辑器）；数值契约由 `HoShadowCastShaderContract.cs` + hlsl 唯一持有并由校验器守一致性。

---

## 7. 每个 feature 都要做的"调试与登记"

**不是可选项**：没有 debug 视图与登记，通道就等于没落地。OB / SB / AC 三项都已做完，新 feature 照抄：

1. **自己的 debug pass + debug 视图**（每个池 / 每张图一条，能单独看）；
2. **注册视图**：`HoDebugViewInfo` + 进 `HoDebugViewRegistry.AllViews`；
3. **复用模块内 All**：调用 `HoDebugGrid`，与单视图共用所属模块 shader；AC 保持双参数入口；
4. **最终显示**：由 `HoDebugRenderPass` 捕获独立调试目标并入队 companion presenter；不写回正常 cameraColor，不增加 DebugTile 资源分支。详见 `Ho-Debug.md`；
5. **契约登记的 debug 列**：`Ho-ChannelContract-v1.md` §3 模板逐条填；
6. **失败可见**（不静默）：声明与实际 RT 张数不一致 / 未声明 ID / 一像素 ID 溢出 / 非法槽 /
   消费者声明的名字解析不到 → 视图里标出 + 告警；
7. **UI 按家规写**：调试入口在 Volume（`HoXxxVolume` 的「调试」分组），feature 只放高级设置 + 兜底默认值；
   分节走 `LilUrpEditorSectionGui`，色板与摘要格式见 `Ho-UI_风格规范.md`。
8. **声明全管线位置**：每个新 Feature 必须按下节登记推荐列表位置、实际 hook / pass 事件、输入就绪条件和后续消费者；只写“BeforeRenderingOpaques”或“放在最后”不算完成接入。

### 7.1 新 Feature 的排序声明（接入必填）

新建 Feature、增加 pass、增加生产者依赖或改变生产时机时，必须同步更新本文 §2.1 / §6 和该模块的现行使用入口。推荐排序集中维护在本文，模块文档链接回来并说明自身特例，不各自复制一份会漂移的全管线清单。

| 声明项 | 必须写清的内容 |
| --- | --- |
| 全管线位置 | 属于输入、查询、语义合成、Forward 前接收、后续照明 / 图像合成还是最终输出；推荐紧邻谁之前 / 之后 |
| 逐 pass / hook 时机 | pre-cull / beginCamera 等 CPU 阶段，以及主生产、resolve、composite、退休 fence、debug 各自的实际 `RenderPassEvent`；可配置值要写默认、限制和模式分支 |
| 必需 / 可选输入 | 谁生产、读取哪个当前相机产品、何时就绪；缺失是停用、报错还是有明确语义的可选分支 |
| 同事件顺序 | 必须在谁下方、谁上方；多 pass 的 Enqueue 顺序；不能只依赖偶然的资产排列 |
| 后续消费者 | 材质、Feature 或导出在哪个阶段读；产品是借用当前 graph、持久纹理还是独立历史；替换与加法合成是否会擦除前一结果 |
| 有效域与失败诊断 | 相机 / 分辨率 / MSAA / 身份与深度关联要求；顺序错、输入缺失时能看到具体原因，不能只写“已生产” |
| 验证 | 涉及依赖 / 时机的改动需检查正确顺序、缺失生产者、同事件排反 / 过晚事件及修复后的实际消费；CPU 请求与 GPU 排序分别验证 |

资源宿主这类不直接产颜色的 Feature 也必须声明自己的请求与寿命阶段；不能强行给它造一个无意义的颜色调试图。未登记位置与依赖的新 Feature 不计为已接入当前管线。

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
| **剩余** | ① 合规 `crypto_*` AOV/export feature；② 透明 PLR 的 receiver/source-id RT（多平面时才立项）；③ AC 的 16-lane MRT 分批（词表 > 8 位才需要）；④ SB 语义 lane 的逐 sample 细分（形态：SB 自渲染 MSAA → 自己 resolve 成单采样再发布）；⑤ AC 一般化逐 lane 权重与其他采样域（Scalar V1 sample/owner 关联已接入）；⑥ AOV 导出层 | — |

**推进顺序的历史决策**：`Ho-GTAO`（独立 feature）→ `Ho-SSGI`（含 `gisexclude`）→ 其余系统；
R1–R6 属于"其余系统"里的地基工程，与 AO/GI 并行且互不读对方产物。

> 2026-10-05 AC 接入：ScreenProcess 已支持语义、组、完整身份、几何、描边与全屏选择，以及指定范围内反选；2026-10-06 已接 Scalar V1 sample/owner 关联；逐 lane 独立权重、其他采样域与按需求裁剪尚未实现。详见 `计划/Ho-AttributeComposite-Plan.md`。

> **2026-10-06 关联接入**：OB 发布当前域的 raw IdentityMS；SB 专用 capture/reduce 显式读取 OB 并发布 owner 排序的 W/V/status；AC 使用单采样统计合成 Selection。支持 RG + D3D11/12 + 2D 完整 viewport + N=1/2/4，其他域与旧 writer 明确近似降级。原有 SB 数值面不依赖 OB。见 [关联契约](计划/Ho-AC-SemanticPrecision-Contract.md)。

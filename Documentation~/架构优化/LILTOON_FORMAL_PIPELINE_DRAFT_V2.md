# 正式管线草案 v2（重新串联）：三轴输入 + 属性合成 + 屏幕效果

> 状态：**草案，供串联讨论**。
> 关系：本文**取代** `LILTOON_FORMAL_PIPELINE_DRAFT.md`（v0.1）的 **§3.1 层模型 / §3.2 帧序 / §3.3 旧→新映射 / §10 命名分析 / §11 决策落点**；v0.1 的 **§6.2 排序铁律 / §6.3 暴露面三分类 / §6.4 材质接口脚印**继续有效（本文 §5 是它的续写）。
> `LILTOON_RENDER_PIPELINE_REVIEW_AND_PLAN.md`（评审与规划）里的**功能域盘点、AOV 方案、§7 业界对照**仍然有效；但它的 **"MetadataBuffer 承担语义"这一前提已作废**——那正是这轮大改的起点。
> 为什么改：v0.1 把"对象/mask/**surface**"全塞在同一个 buffer 槽位里，从没注意 MetadataBuffer 本身就是**杂糅**的（身份 + 覆盖率 + 表面数值）。这轮把它拆成三轴 + 一层合成。

---

## 1. 层模型 v2（6 层）

```text
[L0 灯光/阴影]   ShadowCast（附加灯 atlas + URP 主光阴影）                    → shadow.main / shadow.add0..N
[L1 输入缓冲]    GB（几何轴） / ObjectBuffer（逐物体轴） / SurfaceBuffer（表面轴）→ 见 §3 的归属表
[L2 属性合成]    Ho-Cryptomatte：纯值 → object → surface 递进覆盖 + 具名选区    → 合成属性 / 遮罩
[L3 屏幕效果]    GTAO / SSGI / SSS / PLR·SSR / 角色特化 / OIT                 → ao, gi, sss, reflection, eyecolor/eyedata …
[L4 图像链]      ImageProcess（只读 camera color）
[L5 输出/调试]   AOV 导出层（多通道 EXR）+ DebugTile
```

**与 v0.1 的差别（三处）**：

| v0.1 | v2 | 理由 |
| --- | --- | --- |
| L1 只有 `CharacterBuffer(Metadata)` + `ScreenGeometryBuffer` | L1 是**三轴并列**：GB / ObjectBuffer / SurfaceBuffer | 一个 buffer 不能同时回答"这是谁"和"表面是什么样" |
| 没有属性合成层 | **新增 L2 `Ho-Cryptomatte`** | 多来源（纯值/object/surface）需要一条明确的覆盖链；消费者不该各自解释 |
| 语义效果直接读 MetadataBuffer 的五张图 | 语义效果经 **L2** 拿遮罩 | 今天 ScreenProcess 有 20 个 rule source，就是"没有合成层"的代价 |

---

## 2. 帧序 v2（按"每趟的前置条件"排，不是凭直觉排）

> ⚠ **上一版把 GTAO 排在 opaque 之后、CM 排在 opaque 之前，两处都错**。真正决定位置的是**前置条件**：

| 位置 | 谁 | 前置条件（这就是理由） |
| --- | --- | --- |
| **opaque 之前** | 阴影 → GB → OB → SB → **GTAO** | **GTAO 必须在 opaque 之前**：材质在 forward 里就采样 `_HoAOTexture`（v0.1 §5 的意图参数）——AO 放到 opaque 之后，这一帧的材质就只能读到空图/上一帧。GB/OB/SB 也在这里（它们画自己的几何，不需要 opaque 的颜色） |
| **opaque 之后** | **SSGI** → **CM** → SSS → OIT → PLR → 角色特化 → ScreenProcess | **SSGI 需要 opaque 之后的颜色**；**CM 必须等 opaque**——它合成的是"最终画面上每像素是谁、表面是什么样"，早于 opaque 就没有最终归属可言 |
| **图像链** | ImageProcess | 只读 camera color |
| **最后** | AOV 导出（可选） → DebugTile | 调试最后 |

```text
[1]  URP 主光阴影
[2]  Ho-ShadowCast（附加灯 cast 组）                        → shadow.main / shadow.add0..N
        ── opaque 之前 ──────────────────────────────────────────────
[3]  GB：Ho-ScreenGeometryBuffer（现名 GeometryBuffer）      → 几何法线 / depth / 几何覆盖率（+ 描边视觉壳 / sky 可选）
[4]  OB：Ho-ObjectBuffer（原 CharacterBuffer）               → ID0/ID1/Coverage + 具名选择 + 朝向（逐物体辅助量）
[5]  SB：Ho-SurfaceBuffer                                    → 表面色 / 着色法线 / roughness / metallic / thickness / …
[6]  Ho-GTAO（独立 feature）                                 → ao / aointent   ★ 必须在 opaque 之前
        ── URP opaque / cutout / 透明 常规绘制 ───────────────────────
[7]  Ho-SSGI（独立 feature，读 gisexclude）                   → gi              ★ 需要 opaque 后的颜色
[8]  CM：Ho-Cryptomatte（属性合成 + 遮罩）                    → 合成属性 / 具名遮罩  ★ 必须等 opaque
[9]  Ho-SubsurfaceScattering                                 → sss
[10] Ho-WeightedOIT（透明合成，最难搞）
[11] Ho-PlanarReflection（PLR source / 特殊 composite）
[12] Ho-CharacterSpecialization（眼透 / 发影 / 脸色）          → eyecolor / eyedata
[13] Ho-ScreenProcess（其余语义效果）
[14] Ho-ImageProcess（最终图像链）
[15] AOV 导出层（可选）
[16] DebugTile（调试时最后）
```

**必须成立的偏序关系**（比线性列表更重要，改 pass event 时照这个检查）：

| 约束 | 为什么 |
| --- | --- |
| `阴影 → 所有效果` | 材质/效果都要吃阴影 |
| `GB → OB`、`GB → SB` | OB/SB 绘制时可能要用 GB 的深度做门控 |
| `OB ↮ SB`（互不依赖） | 谁先都行，但都必须在 **CM** 之前 |
| **`GTAO → opaque`** | 材质 forward 里采样 AO |
| **`opaque → SSGI`** | GI 要 opaque 后的颜色 |
| **`opaque → CM`**、`{OB, SB, GB} → CM` | CM 合成的是"最终归属" |
| `CM → SSS / OIT / PLR / 角色特化 / ScreenProcess` | 它们的遮罩只有一个来源 = CM |
| **推论：GTAO 吃不到 CM 的遮罩** | 它在 CM 之前——所以 AO 的"谁参与"只能靠 layer mask / 材质意图，不能靠具名选择。这条要写进 GTAO 的文档，否则以后一定有人想给它接 CM |

> 每个 feature 的 `passEvent` 都是**设置项**（GB/OB 默认 `BeforeRenderingOpaques`、GTAO 也在此档、SSGI/CM 在 opaque 之后），所以上面是**默认意图 + 必须成立的偏序**；**精确 pass event 以实机 Frame Debugger 为准**（v0.1 已有此注）。

---

## 3. 数据归属与"谁能读谁"

| feature | 回答什么 | 输出 | 不许做 |
| --- | --- | --- | --- |
| **GB**（现 GeometryBuffer，将改名 ScreenGeometryBuffer） | 几何在哪、朝向如何、几何覆盖多少 | **几何法线** / depth / 几何覆盖率 / 描边视觉壳 / sky | 不发表面数值、不发身份、不发着色法线 |
| **OB**（Ho-ObjectBuffer） | 这是谁、占多少 | ID0/ID1/Coverage（K=N=4 无损）+ 具名选择（8 层/像素，按需）+ **逐物体辅助量 ≤2 张**（朝向 forward+side） | 不发深度、不发表面数值、不定义属性语义 |
| **SB**（Ho-SurfaceBuffer） | 表面是什么样 | 表面色 / **着色法线** / roughness / metallic / thickness / reflectance / PLR / …（**+ 未来 PBR**） | 不发深度、不发身份 |
| **CM**（Ho-Cryptomatte） | 多来源怎么合成、抠哪一块 | 合成属性 + 具名遮罩 + manifest + 导出档位 | 不自己画几何/表面（它读三轴） |

**三条读的规矩**：

1. **三者并列、禁止互读**（v0.1 §10 第 3 条延续）：GB / OB / SB 互不读对方的产物；需要跨轴的量由消费端**在一次读取里各取一份**。
2. **遮罩只有一个来源 = CM**：任何屏幕效果都不许自己再攒一套语义图（这条让 ScreenProcess 的 20 个 source 收成 1 族）。
3. **消费者读合成结果，不读原始通道**（CM 规划 §3.5）：否则会重新长回"各读各的"。

**法线分家**（已定论）：**GB = 几何法线**（AO/GI 遮挡、shadow bias、物理遮挡、描边）；**SB = 着色法线**（PBR 光照、SSR 反射方向、以及后续一切吃法线贴图的 feature）。两者平行、不互为来源。

---

## 4. 材质接口脚印的"载体"（v0.1 §6.4 的续写）

v0.1 的脚印表只写了"管线决定 / 材质轻量参数"，**没写这些数据存在哪**——这正是"多出一个 buffer 却没人预料到"的原因。补上载体列：

| 系统 | 它的数据载体 | 它的遮罩来源 |
| --- | --- | --- |
| OIT | 无（accumulation/revealage 结构在管线内） | CM（可选） |
| GI | `gisexclude`（独立通道） | CM |
| Shadow | `shadow.main` / `shadow.add0..N` | — |
| 反射 / PLR | **SB**（roughness / metallic / reflectance / PLR strength） | CM |
| 透射 / 折射 | **SB**（thickness / 吸收 …） | CM |
| AO | GTAO 的产物（屏幕空间）+ 材质意图 `_SSAO*` | CM |
| SSS | **SB**（thickness / curvature / 表面色）+ profile（分类，倾向进 OB 的表） | CM |
| 角色特化（眼透 / 发影 / 脸色 / 轮廓） | **OB**（ID / 覆盖率 / 朝向）+ GB（几何门控） | CM |
| AOV / 导出 | CM（ID + 覆盖率 + manifest）+ SB（albedo） | — |

---

## 5. 契约与 AOV 的变更清单（v2 要动的地方）

| 契约 v1 的条目 | v2 的动作 |
| --- | --- |
| `maskId` | → **OB 的 `object.idcoverage`**（名字待契约 v2 定） |
| `objectCustom0/1`（8 位语义） | → **OB 的表（类别 / 标签）**；位含义条款作废 |
| `custom0`（未登记） | → **不迁**：具名遮罩由 **CM** 提供 |
| `surfaceData` | → **SB**（`Material.b` + `Classification`，待定） |
| `reflectionMaterial` | → **SB**（`Material.rg` + `Reflection`）；**v1 冻结条款要改**（§4 第 5 条） |
| `surfaceColor` | → **SB**（`Color`）；**`A=coverage` 的冻结条款要改**（§4 第 4 条） |
| `sssprofile` | → 待定（进 SB 或 OB 的表） |
| `motion` | 不变（占坑） |
| 新增 | OB 的 `Selection` / `Facing`；SB 的 `Normal` / `Emission`（PBR 用）；CM 的合成输出（若导出） |
| 导出档位 | 现状（UINT 通道）+ **合规 `crypto_*`**（float 位重解释 + manifest + 32 bit）两档 |

> 契约 v2 必须走 `LILTOON_CHANNEL_CONTRACT_V1.md` §3 的登记模板 + §5 变更记录；**AOV 名只增不改**（v1 §2 冻结）。

---

## 6. 路线图（串联后的顺序）

| 阶段 | 内容 | 为什么在这个位置 |
| --- | --- | --- |
| **R0** | 本文 + 契约 v2 提案（含两条冻结条款的修订）+ 在 v0.1 上标注取代关系 | 文档先行：三轴的边界先立住，代码才有依据 |
| **R1** | **OB 改名搬迁**（`Runtime/CharacterBuffer` → `Runtime/ObjectBuffer`；常量、feature、组件、调试、编辑器）；**选择层保留**并扩到 8 层 | 代码已有 90%，改名的同时把"选择层是核心"落实 |
| **R2** | OB 的**朝向图**（forward+side，octahedral 打包进一张 RGBA8）+ 调试视图 | 它同时验证"逐物体辅助量"这条可写通道的机制 |
| **R3** | **SB 落地**：按系统逐个开通道（先 `Color`+`Material` 给 SSS，再 `Reflection` 给反射，`Normal` 跟 PBR/SSR） | SB 是 PBR 的地基，也是 SSS/PLR 数值的新家 |
| **R4** | **CM 落地**：递进覆盖链（纯值 < object < surface）+ 具名遮罩 + 消费者统一入口 | 它是"遮罩只有一个来源"的实现；没有它，消费端迁移无从谈起 |
| **R5** | 消费者迁移：GTAO/SSGI 不变；**角色特化 → OB/CM**；**ScreenProcess → 只吃 CM**；**SSS/PLR → SB（数值）+ CM（遮罩）** | 迁移顺序按"依赖面从小到大" |
| **R6** | 删 MetadataBuffer；契约出 v2 | 全仓库无 `_HoMetadataBuffer` 引用 |

**与 v0.1 §11 推进顺序的关系**：v0.1 定的是 `GTAO → SSGI → 其余系统`。GTAO/SSGI 已经在做，**R1-R4 属于"其余系统"里的地基工程**，与它们并行不冲突（互不读对方的产物）。

---

## 7. 待确认

1. **几何法线的措辞**：`Documentation~/GeometryBuffer.md` 要把"几何法线"写明；lilToon 侧 `fragGeometryBuffer` 实际写的是哪一种要核对（本仓库两处证据指向几何法线）。
2. **契约 v2 的两条冻结条款**（`SurfaceColor.a`、`ReflectionMaterial`）何时提、一起提还是分开。
3. **分类的归属**：`materialClass` / profile 进 OB 的表（"这是什么"）还是 SB 的图（逐像素）——倾向进表。
4. **OB 与 SB 的表/表结构**（组表 + 条目表的命名与行宽）、组件命名。
5. **CM 的合成粒度**（按属性 / 按遮罩）与"纯值"的载体。
6. **是否现在就改 GB 的名字**（`GeometryBuffer` → `ScreenGeometryBuffer`）：v0.1 §10 说"改名并进 v1 冻结时批量做"，那一次要不要把 OB/SB/CM 一起并进去。
7. **v0.1 / 评审文档的标注方式**：在原文加"本文 §x 已被 v2 取代"的注记，还是只在 v2 里声明（我倾向**两处都做**，因为别人可能先打开旧文档）。

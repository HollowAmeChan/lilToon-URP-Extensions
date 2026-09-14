# Ho-CharacterBuffer 规划：用 ID + Coverage 取代 MetadataBuffer 的 bit 位掩码

> 状态：**设计已定，待开工 P1**（决策全部锁定见 §1，无开放问题）。
> 前因：`Documentation~/架构边界/语义掩码.md`、`CHANGELOG.md` 0.2.0。
> 目标：彻底解决掩码抗锯齿，并把"角色之间互不干扰"变成结构保证。

---

## 0. 摘要

MetadataBuffer 把 8 个语义压进 RSUV 低字节的 8 个 bit，语义因此只能是 0/1、per-pixel 覆盖无处可存，抗锯齿只能靠消费端滤波补救（详见 §3）。

新 feature `Ho-CharacterBuffer` 改用业界标准模型（§4）：**per-pixel 只存 ID 与覆盖率，其余一切按 ID 查表**。抗锯齿由真实覆盖率给出亚像素相位，角色隔离成为 ID 的天然属性，带宽同时下降。

---

## 1. 决策（全部锁定）

**结构与产品决策**

| # | 决策 | 一句话后果 |
| --- | --- | --- |
| 1 | **K = 4**（层数 = ID pass 采样数） | 逐位无损，不需要"该保留谁"的规则 |
| 2 | **部件上限 4096** | ID = 16 bit/层（角色 8 + 槽位 8）；4096 是注册校验预算，编码上限 256 角色 × 256 槽位 |
| 3 | **组件形态沿用** | 单组件 `HoCharacterBufferGroup` 挂角色/渲染器上；"8 个固定勾选"升级成最多 4096 条命名条目；不拆 Group + Part |
| 4 | **旧资产不兼容** | 不做 `objectCustom` 位映射；ScreenProcess 规则资产直接重新设计 |
| 5 | **命名** | feature `Ho-CharacterBuffer` / 组件 `HoCharacterBufferGroup`；调试沿用"颜色 = 部件显示色"的 picker 语义 |
| 6 | **ZWrite 沿用 `ZWrite On`** | "透明材质也占满网格面积"保留；将来要改的是 ID pass 的 per-sample 写入（alpha-to-coverage / clip），不是 ZWrite |
| 7 | **覆盖率来源：自建 MSAA，与 GeometryBuffer 逐件对齐** | 复刻 `HoGeometryBuffer*` 三件套与 `_HO_GEOMETRY_BUFFER_MSAA_2/4/8` 关键词分支；封顶 4x，平台不支持则回退 1 sample |
| 13 | **RSUV 只当 palette 索引**（不再塞语义位） | ID pass 一次画完、不干扰合批（Unity 官方推荐用法）；低 16 bit = 角色 8 + 槽位 8，高 16 bit 预留并登记；不支持 RSUV 的 renderer 才退回逐部件设全局绘制 |

| 14 | **per-pixel 材质通道按"管线 + 用户"两族预留** | `Material0`(RGBA8,管线逐像素:roughness / metallic / thickness / 备用)+ `Material1`(RGBA16F,用户自定义 0~3);两者**按需分配**;旧 system/custom write mask 变成 palette 字段 |
| 15 | **`_Surface` 沿用 SurfaceColor 的两段式深度策略** | opaque/cutout 先写深度确立归属，transparent 只 ZTest 后 alpha 叠加；因为 `.a` 的语义有四个消费端（§2.4），策略变了它们会静默变味 |
| 16 | **几何数据唯一来源 = GeometryBuffer** | 法线 / 线性深度 / 深度 / 覆盖率一律读它发布的通道；CharacterBuffer **不发布任何几何通道**，其内部 depth 附件只服务于自身 draw 的占用判定（永不发布、永不跨来源比较） |
| 17 | **ID 的 MSAA 目标 = 每样本一个 16 bit ID** | `R16_UInt` + 4x = **8 B/px 瞬态**（一个样本只属于一个部件，逐样本存 4 层格式是 6 倍浪费）；resolve 读 4 个样本数票写出 4 层 |
| 18 | **描边不写 ID** | 与 GeometryBuffer 的"物理几何 / 视觉壳层"分离一致；需要"连描边一起算角色"时用 GB 的 outline coverage 补，不往 CB 里混 |

**工程默认**（按推荐值执行；改动成本低，随时可推翻）

| # | 决策 | 依据 |
| --- | --- | --- |
| 8 | **palette 传 StructuredBuffer<HoCharacterPartData>**，后端不支持时退化为一组 float4 常量数组（按 256 行分块） | 最省、最灵活；不做 1D 纹理——材质有自己的 ID，不需要采样 palette |
| 9 | **标签 32 位** | 一个 `uint` 与 palette 行对齐；旧 8 语义 + 24 个自定义位足够 |
| 10 | **ScreenProcess 规则 source 改成 palette 字段枚举** | 部件 ID / 角色 ID / 类别 / 标签 / materialClass / thickness / curvature / transmittance / **用户自定义通道 0~3** / surfaceColor / coverage；运算符、容差、组合、反相结构不变 |
| 11 | **palette 按 4096 行容量设计，v1 全量上传**（≈512 KB/帧） | 接口预留分块 / 脏行，实测有压力再上 |
| 12 | **ID pass 由 feature 自己绘制** | 只画部件表里的 renderer（可选再用 layerMask / renderQueue 附加过滤），不含描边、特效件；默认一次 `DrawRenderers` 画完（RSUV 携带索引），只有兜底路径才逐部件绘制 |

---

## 2. 现状调查

### 2.1 通道清单

| 通道 | 格式 | 内容 |
| --- | --- | --- |
| `maskId` | R8G8B8A8_UNorm | r = **coverage/权重**、g = 角色 ID、b = 部件 ID、a = flags |
| `surfaceData` | R16G16B16A16_SFloat | thickness / curvature / material class / transmittance hint |
| `custom0` | R16G16B16A16_SFloat | 材质自定义 0~3（可来自纹理，per-pixel） |
| `objectCustom0/1` | R16G16B16A16_SFloat ×2 | 8 个语义**各 1 bit**：CharacterFull / Face / FrontHair / Eye / EyeRevealArea / Accessory / CharacterBody / Reserved7 |
| `reflectionMaterial` | R16G16B16A16_SFloat | perceptualRoughness / metallic / reflectance / PLR strength |
| `surfaceColor` | R16G16B16A16_SFloat | RGB = 线性 HDR 表面色、A = coverage |
| `depth` / `mBufferDepth` | 深度附件 | **不是数据通道**：`depth` = 元数据 MRT pass 自己的 depth-stencil（决出元数据归谁，透明件也写；连全局 ID 都没有）；`mBufferDepth` = SurfaceColor pass 的 depth-stencil（opaque/cutout 写、transparent 只 ZTest 后叠加；仅有 debug 视图读） |

外加 `_HoMetadataBufferActive`（启用）与 `_HoMetadataBufferSystemChannelMask`（通道开关）两个全局标量。

### 2.2 生产端

- **lilToon 包内（跨仓库）**：`lil_pass_metadata_buffer.hlsl` 的 `fragMetadataBuffer`（MRT 主体）、`fragMetadataBufferSurfaceColor`（表面色 + coverage）、同文件的 `fragGeometryBuffer`；通道按 `_HoMetadataBufferSystemChannelMask` 逐位门控。
- **身份来源（新设计里整条替换，见 §5.2）**：`HoMetadataBufferGroup` → `PackRendererUserValue(objectCustomMask, characterId, partId, flags)` → `SetShaderUserValue`（RSUV），并用 MPB 写同名字段作为回退；`HoMetadataBufferSubject` 提供高级覆盖（maskWeight、material class、thickness、curvature、transmittance、custom0_3、debugColor）。
- **Feature 侧**：`HoMetadataBufferPass`（清屏 → fallback 材质 → 绘材质 pass；RenderGraph 与兼容两条路径）、`HoMetadataBufferFallback.shader`、`HoMetadataBufferClear.shader`、`HoMetadataBufferDebugPass`。

### 2.3 消费端（迁移映射见 §5.8）

| 消费端 | 读什么 | 需求 |
| --- | --- | --- |
| 角色特化 composite | `objectCustom0.b/.g`、`objectCustom4.r`、`maskId.g` | **覆盖率**（当前靠自己模糊补） |
| 角色特化 脸色扩散 | `objectCustom0.g`、`surfaceColor` | 覆盖率 + 颜色 |
| 角色特化 轮廓场 | `objectCustom0..7`（可选，默认 CharacterBody=6） | 覆盖率 |
| 材质侧捕获 `HoCharacterCaptureCommon.hlsl` | `unity_RendererUserValue & 255` 的 Face/Eye 位 | 只要身份 |
| ScreenProcess 规则掩码 | `maskId` 四通道、`surfaceData`、`custom0`、`objectCustom0/1`（20 source 槽） | coverage 线性（已正确）；ID 容差匹配（合法，因未被 AA） |
| SSS | `maskId.r`、`surfaceData`、`surfaceColor` | 覆盖率；现存违规见 §6 |
| PlanarReflection | `maskId.r`、`reflectionMaterial`、`surfaceColor` | 覆盖率 |
| 调试 / AOV | 全部 | 点采样；ID 与 coverage 要能分开 |

**结论**：需要覆盖率的是角色特化一族 + SSS / PLR 门控；需要身份的是 ScreenProcess 规则、材质侧捕获、角色隔离。现在把两者塞进同一个 byte，才有了这一路的坑。

### 2.4 depth 两张附件的消费者调查（"到底谁在读"）

| 附件 | 谁当附件用 | 谁当纹理读 | 结论 |
| --- | --- | --- | --- |
| `_HoMetadataBufferDepthTexture` | 元数据 MRT pass 自己（兼容 `ConfigureTarget(colorTargets, DepthTexture)`；RG `SetRenderAttachmentDepth`） | **零**。常量表里只有 `DepthTextureName`、**没有 `DepthTextureId`**（从未 `SetGlobalTexture`）；全仓库除常量定义与两处分配外，没有 shader 声明它 | 纯附件，可无痛替换为 ID pass 自己的 MSAA depth-stencil |
| `_HoMetadataBufferMBufferDepthTexture` | SurfaceColor pass：先清深度 → opaque/cutout 用 `mBufferDepthWriteStateBlock`（`DepthState(true, LessEqual)` = ZWrite On）写 → transparent 只 `ZTest` 后叠加（RG 路径把 transparent pass 声明成 `Access`=只读，是最硬的证据） | **只有两个 debug 视图**：`HoDebugTile.shader`（`MDep`）与 `HoMetadataBufferDebug.shader`（`metadata.mbuffer-depth`） | 真消费者只有 debug；但它承载的**深度策略**有隐式消费者（下表） |

**隐式消费者链（删除/改动时必须一起考虑）**：`mBufferDepthWriteStateBlock` 决定 `surfaceColor.a` 的语义，而 `.a` 被四处当 coverage 用：

| 消费者 | 用法 | 出处 |
| --- | --- | --- |
| 角色特化 · 脸色扩散 | `surfaceCoverage = saturate(surfaceColor.a)` 乘进 mask | `HoCharacterFaceHairDiffuse.shader` |
| PlanarReflection | `surfaceMask = saturate(maskId.r) * saturate(surfaceColor.a) * geometryCoverage` | `HoPlanarReflectionComposite.shader` |
| SSS | 线性采样 `sssSource`（含 `.a`） | `HoSubsurfaceScattering.shader` |
| 调试 | 表面色 / coverage 视图 | `HoDebugTile.shader` / `HoMetadataBufferDebug.shader` |

**所以：深度纹理没人读，但它的写入策略决定了 `.a` 的含义——换 buffer 时若顺手改策略，上面四个消费端会静默变味（决策 15）。**

---

## 3. 现有设计的债

1. **8 个语义共用 1 个 byte** → 没有 per-pixel 覆盖的空间（根因）。
2. **per-pixel 与 per-part 混淆**：`thickness / curvature / materialClass / transmittance / roughness / …` 本是部件级常量，却每像素存一份 16F —— 6~8 张 16F 里绝大多数比特在同一部件内部是常数。
3. **通道数量与语义耦合**：加一个语义要动 RSUV 打包、lilToon 写入端、契约文档、每个消费端。
4. **"ID 与 coverage 同通道"诱导犯错**：同一个 fetch 里 `.r` 是连续量、`.gba` 是 ID，很容易顺手 `saturate` / `step` / 插值。
5. **身份通路把 RSUV 当成了属性容器**：8 语义 + 角色 + 部件 + flags 全塞进 32 bit，用满即无路可走；正确用法是"RSUV 只当索引、属性进表"（§5.2）。
6. **跨角色判断散落在各消费端**（`SameCharacter`、"仅同角色"开关），而不是数据结构的天然属性。

---

## 4. 业界标准做法（要抄的部分）

- **Cryptomatte**（Foundry Nuke 原生节点，源自 Psyop gizmo，规范 v1.2.0）：渲染器输出 **ID matte**；Nuke 端用调色板式的 **manifest**（metadata 或 sidecar JSON）把名字解析回 ID；matte 与 alpha 的预乘关系显式处理（节点上有 **Unpremultiply**）。见 [Nuke Cryptomatte 节点文档](https://learn.foundry.com/nuke/content/reference_guide/keyer_nodes/cryptomatte.html)、[Cryptomatte Specification v1.2.0](https://raw.githubusercontent.com/Psyop/Cryptomatte/master/specification/cryptomatte_specification.pdf)、[Pixar PxrCryptomatte](https://rmanwiki.pixar.com/download/temp/pdfexport-20191207-071219-0835-8542/REN22-PxrCryptomatte-071219-0835-8543.pdf?contentType=application/pdf)。
- **ID pass + Coverage pass 分离**：Nuke 社区的标准做法是"用 ID 与 Coverage 一起生成 matte，**覆盖率放 alpha 通道**"，Coverage 单独一个 tab 调（它是软硬程度的唯一来源）。见 [Nukepedia: Color Picker ID](https://www.nukepedia.com/tools/gizmos/image/color-picker-id/)。
- **引擎侧同构**：HDRP 的 **Rendering Layers** —— renderer 最多 32 层，但"使用 Rendering Layers 的 HDRP 效果只支持前 16 层"，并提供一张**可被效果逐像素采样的 Rendering Layer Mask buffer**。见 [HDRP: Use light rendering layers](https://docs.unity.cn/Packages/com.unity.render-pipelines.high-definition@17.3/manual/Rendering-Layers.html)、[在 Shader Graph 里采样该 buffer](https://discussions.unity.com/t/how-to-use-rendering-layer-mask-buffer-in-shader-graph/1663028/4)。UE 侧对应物是 **Custom Depth + Custom Stencil**（8 bit 分类 + **per-primitive custom data**），见 [stencil mask 可见性讨论](https://forums.unrealengine.com/t/stencil-mask-can-be-seen-thru-walls-is-there-a-fix/2401752/7)、[post-process material 侧用法](https://forums.unrealengine.com/t/my-dynamic-material-instances-for-my-post-process-material-are-created-but/2742871/2)。
- **多物体像素**：Cryptomatte 用**多层（level）「ID + 覆盖率」对**表达一个像素里的多个对象 —— 这正是 §5.5 的容量模型，也是 GTAO 白圈 / 前发投影硬裁那类问题的根因所在。
- **"表 + 索引"是标准形状**：UE 的 per-primitive custom data、Cryptomatte 的 manifest 都是"像素里只有索引，属性放在表里、由 CPU 维护"。新设计同形；区别只是我们把索引做成 16 bit，并且**索引的传输由我们自己的 draw 决定**（§5.2）。

---

## 5. 设计

### 5.1 per-pixel 布局

per-pixel 只装 **ID**（身份）、**覆盖率**（多少）、以及**真正逐像素的材质值**；图的数量只由**层数 K**、**ID 位宽**与**材质通道族**决定，区域/语义/部件数量不进 per-pixel 布局。

| 图 | 格式 | 内容 | 采样 | 分配 |
| --- | --- | --- | --- | --- |
| `_HoCharacterBufferId0` | R8G8B8A8_UNorm | r/g = 层0 角色/槽位，b/a = 层1 角色/槽位 | **Point** | 常开 |
| `_HoCharacterBufferId1` | R8G8B8A8_UNorm | 层2、层3 同上 | **Point** | 常开 |
| `_HoCharacterBufferCoverage` | R8G8B8A8_UNorm | 4 层各自的覆盖率 | 线性可用 | 常开 |
| `_HoCharacterBufferSurface` | R16G16B16A16_SFloat | RGB = 线性表面色（沿用现 `surfaceColor`）、A = coverage | 颜色可线性 | 常开 |
| `_HoCharacterBufferMaterial0` | R8G8B8A8_UNorm | 管线逐像素值（见字典） | 按其语义 | **按需** |
| `_HoCharacterBufferMaterial1` | R16G16B16A16_SFloat | 用户自定义 0~3（见字典） | 默认 **Point** | **按需** |
| （ID pass 的 MSAA depth-stencil） | 深度格式 | **内部附件**：决出 sample 归属、tie-break 取更近样本 | — | 常开、**不发布** |

ID + 覆盖率合计 **3 张 RGBA8 = 12 B/px**（常开部分）；两张 `Material*` 只在被用到时分配。

**不发布深度数据通道**（与旧设计的关键差异）：消费端的几何门控继续读 `_HoGeometryBufferNormalDepthTexture` / `_HoGeometryBufferDepthTexture`。理由是 ①它们已经在用；②ID resolve 需要的是 per-sample 深度，属于 MSAA 附件、发布出去没有意义；③**发布第二份深度本身就是"1px 错位"类 bug 的温床**（两份深度不同源、采样数不同、覆盖集合不同）。旧设计那两张 depth 也都只是 pass 自己的 depth-stencil 附件（§2.4：元数据那张连全局 ID 都没有；`MBufferDepth` 虽有 ID，但真消费者只有两个 debug 视图）。debug 需要看深度时，直接采样 ID pass 的 depth 附件（深度格式纹理可采样，与 `_CameraDepthTexture` 同理），不额外发布通道。

**几何唯一来源的三条配套规则**（决策 16）：

1. **要几何只有一个地址**：`_HoGeometryBufferNormalDepthTexture`（法线 + 线性深度）/ `_HoGeometryBufferDepthTexture` / `_HoGeometryBufferCoverageTexture`。CharacterBuffer 的消费者里凡涉及法线、线性深度、几何覆盖率的一律读这里——现有消费端（角色特化 composite、SSS、PLR、ScreenProcess 各效果、GTAO、DebugTile）本来就都是这么读的，迁移**不碰几何层**。
2. **内部附件允许存在、永不发布**：ID pass 必须有自己的 depth-stencil（占用判定 + tie-break），但它不出现在任何发布清单、不进契约、不被采样。
3. **永不比较两个来源的深度**：这正是 `MSAA.md` 里那类"约一个采样间距错位"的来源。已知的**结构性例外**只有一个——眼透的 `hairInFront` 必须比较"捕获到的眼睛深度"（`HoCharacterCaptureCommon.hlsl` 里 pre-multiply 的线性深度）与 GeometryBuffer 深度：被前发遮住的眼睛在 GeometryBuffer 里根本不可见，所以这个跨来源比较无法消除，保留并在此记录。

**通道字典（per-pixel 到底装了谁的东西）**

| 图 | 格式 | 通道 | 归属 | 分配 |
| --- | --- | --- | --- | --- |
| `_HoCharacterBufferSurface` | RGBA16F | RGB = 线性表面色、A = coverage | 管线 + 材质 | 常开 |
| `_HoCharacterBufferMaterial0` | **RGBA8** | r = roughness、g = metallic、b = thickness（仅 SSS 厚度贴图时逐像素）、a = 备用 | **管线** | **按需**（开 PLR / SSS 才分配） |
| `_HoCharacterBufferMaterial1` | **RGBA16F** | **用户自定义 0~3**（= 今天的 `custom0~3`） | **用户 / 材质** | **按需**（材质勾了才分配） |

- 为什么分成两族：**管线那四个都是 0~1 归一量，8 bit 足够；用户那四个语义未知（可能不是 0~1、可能有自定义编码），给 16F 不设限。**
- 为什么"挤"消失了：今天真正抢预算的是 **RSUV 位 + system 通道开关 + custom write mask** 三件；新方案只把**真正逐像素的值**留在通道里，那三件全部变成 **palette 的 per-part 字段**（表里没有预算问题）。
- 增长路径：用户通道不够时直接再挂一张 `Material2`（4~7），**不改任何现有通道、不改契约编码**。

哪些原本看着像"每像素"、实际只是 per-part 常量（因此进 palette 而不占通道）：`materialClass`、`curvature`、`transmittanceHint`、`reflectance`、`PLR strength`；`thickness` 与 roughness / metallic 只在被贴图驱动时才需要逐像素通道。

约定：**ID 0 = 背景，不占槽位**；只存非背景 ID，背景由残差表达，因此"角色占比" = `Σcov`，与今天 `maskId.r` 一致。

16 bit/层（角色 8 + 槽位 8）的理由：**"同角色"退化成一次 byte 比较**，而前发投影的 `same`、眼透遮挡都是热路径，不该为隔离判断查表。8-bit 平坦方案更省，但要么只支持 2 层、要么把部件卡在 256。

### 5.2 ID 传输：palette 索引怎么送进 shader（决策 13）

**用 RSUV 携带 palette 索引。** 这是 Unity 对该需求给出的标准机制：官方文档明确它**不干扰合批**、**无额外 CPU 开销**、比复制材质略快而比 MPB 快得多，并且**配合 GPU Resident Drawer 收益最大**；官方 use case 表里"大量 per-renderer 自定义数据"一条推荐的正是"**RSUV 当索引，指向全局 GraphicsBuffer 里的数据结构**"（见 [Introduction to RSUV](https://docs.unity3d.com/6000.5/Documentation/Manual/renderer-shader-user-value-intro.html)、[Set and use the RSUV](https://docs.unity3d.com/Manual/renderer-shader-user-value-set-and-use.html)）。

- **32 bit 的分配**：低 16 bit = palette 索引（角色 8 + 槽位 8）；高 16 bit 由本 feature 预留（例如将来的 per-part 排除位），**并把这块分配登记进文档**——共享槽的隐患靠"登记分区"解决，而不是靠弃用机制。RSUV 只要当索引就永远够用，属性增长全在 palette 里。
- **支持范围**：`MeshRenderer` / `SkinnedMeshRenderer` / `SpriteRenderer` / `SpriteShapeRenderer` / `TilemapRenderer` 各有自己的 `SetShaderUserValue`（不在 `Renderer` 基类上，所以现有 `TrySetRendererUserValue` 只处理两种属于**代码没写全**）。角色部件的实际类型都在列表内。
- **兜底路径**：极少数拿不到 RSUV 的 renderer，退回"逐部件设全局常量再绘制"——只对这部分付出 draw call 代价。
- **删除 MPB 回退**：官方在 SRP 下不推荐 MPB（性能差、可能不生效），而且它会让该 renderer 连**主 pass** 一起失去 SRP Batcher。
- **材质侧**：从 RSUV 解出索引 → 查 palette 取 category / tags（就是官方"索引进全局 buffer"的用法），**不需要 CPU 逐 draw 设全局**。
- `HoCharacterBufferGroup` 只写 RSUV（索引）+ 维护部件表与校验。

### 5.3 调色板（palette）

- **载体**：`StructuredBuffer<HoCharacterPartData>`（决策 8；后端不支持时退化为一组 float4 常量数组并按 256 行分块）。**非 AA 数据，必须点采样。**
- **每行字段**：`characterId`、槽位号、**partCategory（枚举，取代 8 bit 语义）**、**标签位掩码（32 位，决策 9）**、`materialClass`、`thickness`、`curvature`、`transmittanceHint`、`roughness`、`metallic`、`reflectance`、`plrStrength`、**显示色**（Nuke "color picker ID" 的颜色）、名字 hash（AOV manifest）。
- **角色隔离不靠额外图**：ID 的高字节即角色；可视化 / 导出查 `displayColor` —— 即业界那套"用颜色表示 ID"，但颜色是**查表得到**而非把 hash 塞进像素（hash 进像素会让 ID 再也不能被任何滤波碰）。
- **越界兜底**：读表 clamp 到"unknown"行。
- 代价与收益：`maskId` / `surfaceData` / `reflectionMaterial` 三张 16F 消失，换成 ID 三张 RGBA8 + 一张 palette；原本塞在 `surfaceData` / `reflectionMaterial` / `custom0` 里的东西按通道字典各归其位（常量进表、逐像素进 `Material*`）；上传策略见决策 11。

### 5.4 覆盖率怎么产生

自建 MSAA，**与 GeometryBuffer 逐件对齐**（决策 7）：采样数协商 → MSAA 绘制目标 → resolve pass，RenderGraph 与兼容两条路径同构，resolve shader 用同一套多关键帧 + `Texture2DMS` 逐样本读取。

**唯一差异是 resolve 语义**：GeometryBuffer 是"挑最近样本得到该像素几何 + 写覆盖率"；这里是"**逐样本数票，取占比最高的 4 个 ID 及各自占比**"。共同点是**都不做平均**。

ID pass 的 depth-stencil 附件只服务于两件事：**决出每个 sample 归属谁**，以及计数相同时**按更近的样本 tie-break**。它不发布给任何消费端（几何判断一律走 GeometryBuffer，见 §5.1）。

有了真实覆盖率，消费端不再需要"猜"边界，也不再需要 `HoCharacterSemanticMaskBlur` 那类补偿。ID pass 的绘制范围见决策 12。

### 5.5 容量与边界（能表达什么、不能表达什么）

设 ID pass 用 Nx MSAA：一个像素的真实内容是**长度为 N 的 ID 多重集**（4x 下如 `{发, 发, 眼, 脸}`）。缓冲把它压成 K 组 `(id_i, 覆盖率_i)`，且**不归一化**：

- **K ≥ 该像素内不同 ID 的个数 ⟺ 无损**；最坏情况 N 个样本全不同 → **K = N 是无损的充要条件**。K < N 时前 K 名（按样本数）**精确**、尾部**总质量**也精确，**丢掉的只是尾部身份**。
- **归一化必须显式禁止** —— 它正是把"这里还有第三个东西"抹掉的动作；不归一化时 `1 − Σcov` 天然就是残差。
- **`K = N` 让残差语义唯一**：`1 − Σcov` 精确等于背景占比，可直接当"角色占比"用。K < N 时残差里"背景"与"没装下的第三个东西"混在一起、无法区分。
- **背景占名额**：`{发, 眼, 脸, 空}` 就是 4 个不同 ID 用满、残差 0.25 = 背景占比；只要残差 > 0 就说明有样本落在背景上、它已占名额，故能存下的"部件"最多 N−1 个。4x 下"4 个部件 + 背景"不可能（几何上可以真有 5 个面穿过该像素，但证据只有 4 份；没被任何样本命中的区域在数据里不存在 —— 比采样间隔更细的发丝会整个消失）。
- "一张覆盖率只能描述两者叠加"应改为：**它能描述任意 K 个东西的叠加，代价是丢掉第 K+1 个的身份**。"2"看着像边界，是因为它额外假设"一像素最多两个面"——这在**前发 + 眼睛 + 脸**上不成立：`{发 1, 眼 1, 脸 2}` 按质量取前 2 会把眼挤掉，眼透抗锯齿随之坏掉。K=4 让"该保留谁"这个判断彻底消失。
- 写入端约定：排序与 tie-break 只由样本计数决定（相同则取更近的样本）；**覆盖率不归一化、也不滤波**（它是已经正确的亚像素值，滤波只会糊宽、不增加信息）。
- 三条与层数无关的边界：
  1. **覆盖率说"多少"，不说"形状"** —— 斜边、角、比采样间隔更细的发丝都给不出来；这是 MSAA 本身的天花板（更高采样 / 超采样提高精度，TAA 的 jitter 跨帧累积把它变成亚像素精度）。
  2. **ID 说"谁"，覆盖率说"多少"；两者都不可过滤** —— "点采样 + 覆盖率"不是妥协，而是这两个量的定义决定的唯一合法组合。
  3. **残差必须显式保留**，消费者要能知道 `Σcov < 1` 意味着"这像素没被解释完"（GeometryBuffer 的 G = 被解析表面占有率就是这个思想，这里推广成 K 层）。

### 5.6 拓展性与硬上限

**语义 / 部件 / 属性可以随意扩张**：palette 是按 ID 索引的表，**没有 AA 概念、不会被插值**，所以标签、枚举、任意属性放进去都安全；加语义 = 加标签位或枚举值，**不动 per-pixel 布局、不动契约编码、不动身份通路**。8 个 bit 的问题不在于 bit，而在于它被存在了每像素；RSUV 的问题不在于 32 bit，而在于它被当成了属性容器。

| 轴 | 上限 |
| --- | --- |
| 语义 / 区域 / 部件数量 | 实际无限（受 palette 行数与 ID 位宽约束） |
| 每部件属性 | 实际无限（代价是每帧上传量，决策 11） |
| **per-pixel 材质通道** | **固定预留**（决策 14：4 管线 + 4 用户），不是无限；不够时按需再加图 |
| 每像素层数 | **硬上限 = 采样数 N** |
| 每像素覆盖率精度 | **1/N**（4x → 0.25，8x → 0.125） |
| 每像素形状信息 | **不存在** |
| 角色数 × 部件数 | 8+8 bit → 256 × 256 |
| ID 传输 | RSUV 不干扰合批（一次画完）；兜底路径才是一部件一 draw |
| 跨帧 | **ID 必须稳定**，否则用 ID 判断历史有效性的时序效果会抖 |

### 5.7 组件（`HoCharacterBufferGroup`，形态沿用）

- 区域从**固定 8 个 bit** 升级成**最多 4096 条命名条目**：名称（唯一，AOV manifest 用）、**类别**（枚举，单值表示"这是什么"）、**标签位掩码（32 位）**、材质侧属性（class / thickness / curvature / transmittance / roughness / metallic / reflectance / plrStrength）、**显示色**。
- 渲染器归属到条目（与今天"给渲染器打勾"同一交互）；保留角色 ID 字段；`HoMetadataBufferSubject` 的高级覆盖并进同一条目。
- 组件同时是**运行时映射的所有者**：维护"渲染器 → 条目"并把条目索引写进 RSUV（不再写 MPB）；兜底路径所需的全局常量值也由它提供。
- **标签保留的理由**：像 `CharacterFull`（= 该角色任意部件）这种"多归属"语义用标签最自然，消费端一次 `&` 即可查询。
- **校验**：一个 renderer 只属一个条目；名称唯一；palette ≤ 4096 且越界告警；renderer 类型是否支持 RSUV（不支持则走兜底路径并提示）；**ID 跨帧稳定**。

### 5.8 消费端迁移映射

| 消费端 | 新读法 |
| --- | --- |
| 角色特化 composite | `palette[char][slot].category / .tags`；同角色 = 高字节比较；覆盖率 = `_Coverage` 各层 |
| 角色特化 脸色扩散 | 同上 + `_Surface` |
| 角色特化 轮廓场 | 类别 / 标签查询 + 覆盖率 |
| 材质侧捕获 | 从 **RSUV** 解出索引 → 查 palette 取 slot / character / category / tags（不读 MPB） |
| ScreenProcess 规则 | palette 字段枚举（决策 10） |
| SSS | `_Coverage` + palette（thickness / curvature / class / transmittance）+ `_Surface` + **GeometryBuffer normalDepth**（几何门控不变） |
| PlanarReflection | `_Coverage` + palette（反射参数）+ `_Surface` + **GeometryBuffer normalDepth/coverage**（几何门控不变） |
| 调试 / AOV | ID / 覆盖率分层显示；palette `displayColor`；manifest JSON |

### 5.9 与 GeometryBuffer 的对齐（同一类问题的两份实现）

两者都是"**主动重绘一层屏幕空间事实 + MSAA resolve + 发布全局纹理**"，所以必须逐条对齐；`Documentation~/GeometryBuffer.md` 是 GB 的公共契约，CB 落地后应当有同体例的契约。

**① 可以照抄的三件套**（决策 7 的细化清单）

| 点 | GB 的做法 | CB 照抄 |
| --- | --- | --- |
| 采样数协商 | `SystemInfo.GetRenderTextureSupportedMSAASampleCount` + `bindTextureMS` 目标，不支持则回退 | 同 |
| resolve | `HoGeometryBufferResolve.shader` + `_HO_GEOMETRY_BUFFER_MSAA_2/4/8` 多关键帧 + `Texture2DMS` 逐样本读 | 同关键词模式，换归约逻辑 |
| 两条路径 | RG：`UniversalRenderer.CreateRenderGraphTexture` + `SetRenderAttachmentDepth`；兼容：`RTHandle` + `ConfigureTarget`/`SetRenderTarget` | 同构 |
| 发布与复位 | `SetGlobalTextureAfterPass` + `_HoGeometryBufferValid`；禁用/不支持/结束时复位为 black texture | 同（含 `_Valid`） |
| 懒分配 | VisualSurfaceBuffer 只在有消费者时分配（GB 文档 §11.3/11.4） | 同：没有消费者时 ID pass 不跑，`Material*` 不分配 |
| Debug | DebugView + DebugTile 注册 view id（`geometry.*`），每个通道必须有视图 | 同：`character.id` / `character.coverage` / 各层 / palette 字段 |

**② 必须一致的地方（否则产生跨来源错位）**

- **renderScale 与 GeometryBuffer 一致**：消费者会把 CB 的覆盖率与 GB 的覆盖率相乘（例如 PLR 的 `surfaceMask` 链），分辨率不同就会在轮廓处错位。GB 默认 Full，CB 也按 Full 设计；若将来要降分辨率，两者必须一起降。
- **发布/复位纪律一致**：camera begin 复位全局与 `_Valid`，生产后发布，禁用/不支持时回退 black——避免读到上一帧的陈旧资源。
- **同一帧内都在消费者之前**：CB 与 GB、MetadataBuffer 同在 `BeforeRenderingOpaques` 槽位，彼此**无硬依赖**（CB 不读 GB 的产物，GB 不读 CB 的），只要求都先于消费者。
- **DebugTile 每个通道都要有视图**（GB 文档明确把"没有视图"当作成本不可验证的风险）。

**③ 刻意不同的地方**

| 维度 | GeometryBuffer | CharacterBuffer |
| --- | --- | --- |
| 画什么 | 全场景真实几何 + 描边壳 + sky | **只画角色部件**（layerMask / renderQueue 独立过滤），不含描边 |
| 覆盖率语义 | `NormalDepth.a` 派生"有没有真实几何"；MSAA 下另有 R8G8 coverage（R=总覆盖、G=最近面占有率） | **每个 ID 的占比**（4 层），直接回答"我这个部件占多少" |
| resolve 运算 | 挑最近样本得到该像素几何 + 写覆盖率 | 逐样本数票 + 取前 4 个 ID 及占比 |
| 深度 | 有独立 `DepthTexture`（自己 ZTest + 描边可见性）并**发布**它，但契约明确"不应被当线性深度语义采样" | 同样有内部 depth-stencil 附件，但**根本不发布**（更干净） |
| sky | 有 SkyTexture（背景 radiance/贡献） | 不需要：未写入的像素就是 ID 0 = 背景 |
| 描边 | 独立 `OutlineNormalDepth`（物理/视觉分离，AO/GI 不得当真实表面） | **描边不写 ID**（决策 18），需要"连描边一起算角色"时用 GB 的 outline coverage 补 |

**④ 跨来源总规则（与 §6 禁令同源）**

- **同义量不得相乘、也不得比较**：问"这个像素被几何覆盖多少 / 最近面占多少"→ 读 GB；问"我这个部件占多少"→ 读 CB。**一次只用一个来源**。
- 深度只有一个来源（GB，决策 16），CB 的内部附件永不参与消费者运算。
- 已知例外只有眼透的 `hairInFront`（§5.1 规则 3）。

**⑤ MSAA 瞬态成本的修正（结合 GB 的格式选择后发现的）**

MSAA 阶段**每个样本只需要一个 ID**（一个样本只属于一个部件），所以 ID 的 MSAA 颜色目标只需 **16 bit/样本**，即 `R16_UInt` + 4x = **8 B/px 瞬态**——而不是把 4 层 `(id, coverage)` 格式直接开 MSAA（那会是 48 B/px）。resolve 时读 4 个样本的 ID 数票，写出 4 层结果（决策 17）。GB 的 MSAA 目标是 `R16G16B16A16_SFloat`（每样本 8 B），因为法线+深度必须逐样本存；CB 反而比它便宜。

**⑥ 常驻成本对照**（全分辨率、无 MSAA 乘数，1920×1080）

| 资源 | 约 B/px | 1080p 近似 |
| --- | ---: | ---: |
| 现状 MetadataBuffer 常驻（maskId 4 + surfaceData 8 + custom0 8 + objectCustom0 8 + objectCustom1 8 + reflectionMaterial 8 + surfaceColor 8） | 52 | 102.8 MiB |
| CB 常驻（ID 3×RGBA8 + `_Surface` 16F） | 20 | 39.5 MiB |
| CB 按需（`Material0` RGBA8 + `Material1` 16F） | +12 | +23.7 MiB |
| CB 瞬态（ID 的 MSAA 颜色 8 + depth-stencil ≈16） | 24 | 47.4 MiB |

即：**常驻降到约 38%，两张按需通道全开也仍不到现状的 2/3**；瞬态只在 ID pass 期间存在。

### 5.10 契约与 AOV

- 按 `LILTOON_CHANNEL_CONTRACT_V1.md` §3 登记：`character.idcoverage` / `character.surface` / `character.material`（含用户自定义 0~3）/ `character.palette`。**不登记深度通道**——几何信息统一由 GeometryBuffer 提供。
- AOV 导出对齐 Nuke 习惯：`id_object`、`id_group`（从 palette 解出）、`matte_*`（`coverage × (category == X)`）；**ID 不过滤、coverage 放 alpha**，附 palette manifest（JSON），Nuke 端即可像 Cryptomatte 那样点选。
- CB 落地后，`Documentation~/GeometryBuffer.md` §7 消费者契约表里仍写着 "MetadataBuffer" 的三行（CharacterSpecialization / PLR / SSS）要改成 CharacterBuffer，并在 GB 文档的公共原则里把 "MetadataBuffer = object/material semantic truth" 拆成 "CharacterBuffer = 部件身份 + 覆盖率真值 / MetadataBuffer 退役"。这一步记在 P4。

---

## 6. 非线性 AA 禁令（本次改造的"宪法"）

**允许线性作用**：`coverage`（乘、lerp、加权求和）、颜色（`_Surface.rgb`）、`custom` 里明确是连续量的通道。

**禁止**：

1. **ID**：不得双线性/三线性过滤、不得平均、不得在插值后比较。读取一律 `sampler_PointClamp`；比较在原始值上（UNORM8 用 `round(v*255)` 还原整数再比）。
2. **深度**：同上（这也是 GeometryBuffer 把法线/深度点采样、覆盖率单独出图的原因）。
3. **覆盖率**：不得 `step` / `round` / 阈值化成 0/1 再参与混合 —— 那等于把 AA 又丢了；只在乘完权重之后做"要不要硬边"的显式选择。
4. **不要把已阈值化的结果写进 buffer**：buffer 只存**身份**与**线性量**，判断留给消费端。
5. **不要把 per-part 常量塞进 per-pixel 通道**再指望它可过滤（现在的 `surfaceData` / `reflectionMaterial` 就是反例：class 被双线性插值会得到无意义的中间值）。
6. **不要依赖外部 AA**（相机 MSAA / FXAA / TAA）修这种信号；覆盖率自己产出（`语义掩码.md`）。
7. **多表面必须按层加权**：`result = Σ coverage_i × f(id_i)`；只用前层会在"前发 + 后面的脸"这类像素上重新引入硬边。

**现存违规（迁移时修掉）**：`HoSubsurfaceScattering.shader` 的 `HoSSSSurfaceDataLinear` 双线性读 `surfaceData`，其中 `.b` 是 material class byte。ScreenProcess 的 byte 容差匹配本身不违规（ID 未被 AA），但迁移后要确认它读的仍是点采样 ID。

---

## 7. 迁移分期

| 阶段 | 内容 | 验收 |
| --- | --- | --- |
| **P0（本文件）** | 调查 + 设计 + 纪律 | 已完成 |
| **P1** | `HoCharacterBuffer*` feature（ID/coverage pass + palette + 调试视图）；lilToon 侧新增 `HoCharacterBuffer` pass，与 `HoMetadataBuffer` 并存，身份走 RSUV 索引 + palette 查询 | 无 AA / MSAA 开 / 后处理 AA 开三种情况下 ID 与覆盖率都正确；部件与 palette ID 一一对应；ID pass 一次画完、不破坏合批 |
| **P2** | 角色特化三效果 + 轮廓切新 buffer，`HoCharacterSemanticMaskBlur` 退化为可选 | 前发投影边界连续、发际线无硬裁、角色互不干扰；等价 debug 视图无台阶 |
| **P3** | ScreenProcess 规则、SSS、PlanarReflection 切新 buffer | 屏幕效果行为不变或更好；SSS 不再双线性读 class |
| **P4** | 删除 MetadataBuffer（大量 16F 附件、fallback/clear/debug shader、MPB 回退、契约条目）；同步更新 `Documentation~/GeometryBuffer.md` §7 消费者契约表与公共原则（MetadataBuffer → CharacterBuffer） | 全仓库无 `_HoMetadataBuffer` 引用、无 MPB 身份写入；RSUV 只剩"palette 索引"一种含义；契约出 v2 |

跨仓库依赖：**lilToon 包内要多/改一个 pass**（新 pass 只把 RSUV 当索引用，不再解析语义位）；C# 侧 `HoCharacterBufferGroup` 为每个 renderer 分配 palette ID、写 RSUV，并登记 32 bit 的分区。

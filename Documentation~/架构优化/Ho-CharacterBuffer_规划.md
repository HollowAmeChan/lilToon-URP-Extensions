# Ho-CharacterBuffer 规划：用 ID + Coverage 取代 MetadataBuffer 的 bit 位掩码

> 状态：**设计已定，待开工 P1**（决策全部锁定见 §1，无开放问题）。
> 前因：`Documentation~/架构边界/语义掩码.md`、`CHANGELOG.md` 0.2.0。
> 目标：彻底解决掩码抗锯齿，并把"角色之间互不干扰"变成结构保证。

---

## 0. 摘要

MetadataBuffer 把 8 个语义压进 RSUV 低字节的 8 个 bit，语义因此只能是 0/1、per-pixel 覆盖无处可存，抗锯齿只能靠消费端滤波补救（详见 §3）。

新 feature `Ho-CharacterBuffer` 改用业界标准模型（§4）：**per-pixel 只存 ID 与覆盖率，其余一切按 ID 查表**。抗锯齿由真实覆盖率给出亚像素相位，角色隔离成为 ID 的天然属性，带宽同时下降。

**这不是我们独有的问题**：UE 官方文档在讲 Custom Depth 做轮廓时明说那类轮廓在 TAA 下"得不到抗锯齿"，并把它归因于 TAA 每帧的亚像素抖动；HDRP 也只能给效果一张"逐像素可采样的 Rendering Layer Mask buffer"。业界的解法都是同一个方向——**把身份与覆盖率当成一等数据产出，而不是让消费端去猜边界**（§4.4）。

---

## 1. 决策（全部锁定）

**结构与产品决策**

| # | 决策 | 一句话后果 |
| --- | --- | --- |
| 1 | **K = 4**（层数固定 4；MSAA 采样数 N ≤ K，因此封顶 4x） | 逐位无损，不需要"该保留谁"的规则 |
| 2 | **部件上限 4096** | ID = 16 bit/层（角色 8 + 槽位 8）；4096 是注册校验预算，编码上限 256 角色 × 256 槽位 |
| 3 | **组件形态沿用** | 单组件 `HoCharacterBufferGroup` 挂角色/渲染器上；"8 个固定勾选"升级成最多 4096 条命名条目；不拆 Group + Part；**面部朝向（`faceBone` + 脸前/右/上三轴）与 `TryGetWorldFacing()` 一并沿用**——眼透相机角度修正、将来的 SDF 都读它，且调用方式与 `HoMetadataBufferGroup` 同形，消费者切过来不用改代码 |
| 4 | **旧资产不兼容** | 不做 `objectCustom` 位映射；ScreenProcess 规则资产直接重新设计 |
| 5 | **命名** | feature `Ho-CharacterBuffer` / 组件 `HoCharacterBufferGroup`；调试沿用"颜色 = 部件显示色"的 picker 语义 |
| 6 | **ZWrite 沿用 `ZWrite On`** | "透明材质也占满网格面积"保留；将来要改的是 ID pass 的 per-sample 写入（alpha-to-coverage / `SV_Coverage` / clip），不是 ZWrite。**注意官方约束**：MSAA 下"the runtime shares only one coverage for all RenderTargets"，一张覆盖掩码会同时切 ID 与覆盖率两张图；且 alpha-to-coverage 本身就是为 MSAA 设计的（"intended for use with MSAA… otherwise results can be unpredictable"） |
| 7 | **覆盖率来源：自建 4x MSAA，与相机的 MSAA 开关解耦** | 结构照抄 `HoGeometryBuffer*`（`_HO_GEOMETRY_BUFFER_MSAA_2/4/8` 关键词分支 + `Texture2DMS` resolve + RG/兼容双路径），但**采样数由本 feature 自己决定（默认 4，可降到 2）**。GB 的采样数是"跟随相机"——`GetSupportedMsaaSampleCount()` 在 `cameraTextureDescriptor.msaaSamples <= 1` 时直接返回 1；CB 若照抄，"相机 AA 关掉"这个**原始 bug 场景**就会拿到二值覆盖率、等于没修。只按**平台能力**回退 4→2→1（`SystemInfo.supportsMultisampledTextures` / `GetRenderTextureSupportedMSAASampleCount`） |
| 13 | **RSUV 只当 palette 索引**（不再塞语义位） | ID pass 一次画完、不干扰合批（Unity 官方推荐用法）；低 16 bit = 角色 8 + 槽位 8，高 16 bit 预留并登记；不支持 RSUV 的 renderer 才退回逐部件设全局绘制 |
| 14 | **CB 不带任何 per-pixel 材质通道** | 原来的"用户自定义 0~3"由 §5.11 的 Cryptomatte 选择层承担；`Material0`（roughness / metallic / thickness）与 `_Surface`（线性表面色）**整族搬去 `Ho-SurfaceBuffer`（§5.12 / 决策 20）**。CB 的 per-pixel 输出只剩身份与覆盖率 |
| 15 | **`_Surface` 的两段式深度策略随它一起搬去 `Ho-SurfaceBuffer`**（决策 20） | opaque/cutout 先写深度确立归属、transparent 只 ZTest 后叠加——策略变了 `.rgb`（落到哪个面的颜色）会静默变味（§2.4）。**这条不再是 CB 的事**：SB 与 ID pass 的"全员 ZWrite On"（决策 6）本来就是互相冲突的深度策略，拆开之后各自干净。**`.a` 不再是覆盖率**——覆盖率只有一个来源（ID 层） |
| 16 | **几何数据唯一来源 = GeometryBuffer** | 全管线的**深度 / 法线 / 几何覆盖率入口只有 GeometryBuffer**（`_HoGeometryBufferNormalDepthTexture` / `_HoGeometryBufferDepthTexture` / `_HoGeometryBufferCoverageTexture`）；CharacterBuffer **不发布任何几何通道**，其内部 depth-stencil 附件只服务于自身 draw 的占用判定——**永不发布、不被任何 shader 采样（debug 也不行）、永不跨来源比较** |
| 17 | **ID 的 MSAA 目标 = 每样本一个 16 bit ID** | `R16_UInt` + 4x = **8 B/px 瞬态**（一个样本只属于一个部件，逐样本存 4 层格式是 6 倍浪费）；resolve 读 4 个样本数票写出 4 层。**整数 RT 天然不可滤波、不可混合**，正合身份语义；若平台不支持 `R16_UInt` 的 MSAA，退化成 `R16_UNorm` + 消费端 `round(v * 65535)` 还原整数（与 GB 用 UNORM8 存身份、`round(v*255)` 同法）；采样数一律先过 `SystemInfo.GetRenderTextureSupportedMSAASampleCount` |
| 18 | **描边不写 ID** | 与 GeometryBuffer 的"物理几何 / 视觉壳层"分离一致；需要"连描边一起算角色"时用 GB 的 outline coverage 补，不往 CB 里混 |
| 19 | **用户可选的"选择层"用 Cryptomatte 式 `(选择 ID, 覆盖率)` 表达，取代 `custom0~3` 这类匿名通道** | **每像素 2 个选择为默认**（一张 RGBA8，按 Cryptomatte 的 `R=id0, G=cov0, B=id1, A=cov1` 成对布局），可扩到 4 个（再加一张，+4 B/px）；选择 ID 是**独立的 8 bit 空间**（≤256 个具名选择），与部件的 16 bit ID 空间无关；选择**在 Extensions 侧注册、材质侧只引用名字**（§5.11 的跨仓协议） |
| 20 | **表面/材质数值从 CB 拆出去，独立成第三个 feature `Ho-SurfaceBuffer`** | MetadataBuffer 原本不止承担角色语义，还捎带了 roughness / metallic / reflectance / thickness / curvature / materialClass / transmittance / 线性表面色——**这些是"表面是什么样"，不是"这是谁"**，塞进 CB 会重演"一个 buffer 承担多种语义"的老毛病。拆分后各管一段：**GB 管几何**（法线/深度/几何覆盖率）、**CB 管身份与覆盖率**（ID + 覆盖率 + Cryptomatte 选择）、**SB 管表面与材质数值**。附带解决两件事：①`_Surface`（线性表面色）也归 SB，CB 的独立单采样 pass 随之取消；②"材质数值的权威归属"这个 P2 闸门**直接有了答案**——谁的值谁写（材质侧逐像素写进 SB），palette 只放身份，不再需要覆盖协议 |

**工程默认**（编号 8~12 沿用历史顺序；按推荐值执行；改动成本低，随时可推翻）

| # | 决策 | 依据 |
| --- | --- | --- |
| 8 | **palette 传 StructuredBuffer，分两级**：`HoCharacterPartData`（≤4096 行）+ `HoCharacterData`（角色表，≤256 行、常驻）；后端不支持时退化为 float4 常量数组（按 256 行分块） | 最省、最灵活；两级是为了让稀疏的 `char<<8\|slot` 能索引到稠密行（§5.3）；不做 1D 纹理——材质有自己的 ID，不需要采样 palette |
| 9 | **标签 32 位** | 一个 `uint` 与 palette 行对齐；旧 8 语义 + 24 个自定义位足够 |
| 10 | **ScreenProcess 规则 source 改成 palette 字段枚举 + 选择** | 部件 ID / 角色 ID / 类别 / 标签 / materialClass / thickness / curvature / transmittance（**这几项是材质数值，读哪里见 §5.3 的 P2 闸门**）/ **选择（§5.11，命中 = 选择 ID 相等，权重 = 覆盖率）** / surfaceColor / coverage；运算符、容差、组合、反相结构不变。**凡涉及 coverage 的一律按"多表面加权"求值**：`Σ_i cov_i · f(id_i)`（§6 第 7 条），而不是只取层0 |
| 11 | **palette 按 4096 行 + 角色表 256 行容量设计，v1 全量上传**（部件行 64~128 B → ≈256~512 KB/帧，角色表另 ≈16~32 KB） | 接口预留分块 / 脏行，实测有压力再上 |
| 12 | **ID pass 由 feature 自己绘制** | 只画部件表里的 renderer（可选再用 layerMask / renderQueue 附加过滤），不含描边、特效件；默认一次 `DrawRenderers` 画完（RSUV 携带索引），只有兜底路径才逐部件绘制 |

---

## 2. 现状调查

### 2.1 通道清单

| 通道 | 格式 | 内容 |
| --- | --- | --- |
| `maskId` | R8G8B8A8_UNorm | r = **coverage/权重**、g = 角色 ID、b = 部件 ID、a = flags |
| `surfaceData` | R16G16B16A16_SFloat | thickness / curvature / material class / transmittance hint |
| `custom0` | R16G16B16A16_SFloat | 材质自定义 0~3（可来自纹理，per-pixel）→ **新设计里由选择层取代（§5.11）** |
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

**所以：深度纹理没人读，但它的写入策略决定了 `surfaceColor.a`（今天）与 `_Surface.rgb`（将来）的含义。** 新设计里那四个消费端改为读 ID 层的覆盖率（§5.8），`.a` 退役；但**两段式深度策略本身必须保留**——否则 `.rgb` 取到的是透明件的颜色，脸色扩散那类效果会静默变味（决策 15）。

---

## 3. 现有设计的债

1. **8 个语义共用 1 个 byte** → 没有 per-pixel 覆盖的空间（根因）。
2. **per-pixel 与 per-part 混淆**：`thickness / curvature / materialClass / transmittance / roughness / …` 本是部件级常量，却每像素存一份 16F —— 6~8 张 16F 里绝大多数比特在同一部件内部是常数。
3. **通道数量与语义耦合**：加一个语义要动 RSUV 打包、lilToon 写入端、契约文档、每个消费端。
4. **"ID 与 coverage 同通道"诱导犯错**：同一个 fetch 里 `.r` 是连续量、`.gba` 是 ID，很容易顺手 `saturate` / `step` / 插值。
5. **身份通路把 RSUV 当成了属性容器**：8 语义 + 角色 + 部件 + flags 全塞进 32 bit，用满即无路可走；正确用法是"RSUV 只当索引、属性进表"（§5.2）。
6. **跨角色判断散落在各消费端**（`SameCharacter`、"仅同角色"开关），而不是数据结构的天然属性。

---

## 4. 业界成熟方案与参考（每条都实际打开核对过；写的是"它证明了什么 / 我们抄哪一条"）

> **核对方式**：以下链接都实际抓取过页面正文再引用。少数站点对本环境拒爬（Cloudflare 挑战或只返回空 body），对应条目**已就地标注**：Blender 手册经镜像核对；RenderMan `Levels` 与 V-Ray `ObjectID` 标为二手来源、只作参照；Khronos 规范页拒爬时引的是规范文本的社区镜像并已注明。

### 4.1 身份与覆盖率分离：离线合成的标准交付格式（Cryptomatte / 多层 ID matte）

- **结构：一张 RGBA 装两个 `(ID, 覆盖率)` 对**。Psyop 参考实现（v1.4.0）的取用表达式就是 `(sub_channel.red == ID ? sub_channel.green : 0.0) + (sub_channel.blue == ID ? sub_channel.alpha : 0.0)`，通道对列表 `[(.red,.green), (.blue,.alpha)]`；多个"层（level）"就是多张这样的图（`Cryptomatte00/01/02…`）。→ **我们同构**：同样按 `(ID, 覆盖率)` 成对组织，但因为 ID 是 16 bit 整数，**一张 RGBA8 装 4 个 ID、另一张装对应的 4 个覆盖率**——比它更省，也不需要每层一张图。见 [Psyop 参考实现 `cryptomatte_utilities.py`](https://raw.githubusercontent.com/Psyop/Cryptomatte/master/nuke/cryptomatte_utilities.py)、[Fusion 模块](https://raw.githubusercontent.com/Psyop/Cryptomatte/master/fusion/Modules/Lua/cryptomatte_utilities.lua)。
- **层按覆盖率降序排**（MoonRay / DreamWorks 官方文档）："The {id, weight} pairs are **sorted by max coverage**, so the geometry with the most pixel coverage will always be the first entry." → **这就是我们的排序规则**（按样本数 = 覆盖率降序；平票我们额外定义"取更近的样本"，Cryptomatte 没有这一维）。见 [MoonRay: Cryptomatte](https://docs.openmoonray.org/user-reference/how-to-guides/render-outputs/cryptomatte/)。
- **层数不够 = 掉 ID，表现为噪声**（ASWF《Deep IDs Specification》）："Cryptomatte allocates a fixed size array for IDs, imposing a maximum number of IDs in one pixel. Setting this too large requires a lot of memory; setting it too small **can cause noise if an object is selected which has been discarded from some pixels**." → §5.5 的尾部丢失就是这条；我们的 `K = N = 4` 让它在实际配置里**不可能发生**。见 [OpenEXR Deep IDs Specification](https://openexr.com/en/latest/DeepIDsSpecification.html)。
- **背景 = 全零**：Psyop 实现把背景定义为 RGBA=0000、matte 0.0 → 与我们"**ID 0 = 背景**、背景由残差表达"一致。
- **manifest = 名字 → 十六进制 ID 的映射**（EXR metadata 或 sidecar JSON；**规范推荐嵌入，见 §4.2**），且规范要求 ID 存成 FLOAT 而非 HALF、并提醒"care must be taken **not to color manage or otherwise modify them**"。→ 我们的 palette 就是 manifest 的运行时版本；因为用整数 RT 存 ID，**"别做色彩管理"这类外部约定在我们这里不需要存在**。见 [Foundry Nuke: Keying with Cryptomatte](https://learn.foundry.com/nuke/content/comp_environment/cryptomatte/keying_with_cryptomatte.html)、[Nuke Cryptomatte 节点](https://learn.foundry.com/nuke/content/reference_guide/keyer_nodes/cryptomatte.html)、[Blender 实现 `cryptomatte.cc`](https://projects.blender.org/blender/blender/raw/branch/main/source/blender/blenkernel/intern/cryptomatte.cc)。
- **我们刻意不抄的一个 trick**：Cryptomatte 把 32 bit hash **位重解释（bit-cast）成 float32**，还要修补指数位（`exp == 0x00 / 0xFF` 时 `hash ^= 1 << 23`，避开非规格化数与 NaN），并且只支持 `MurmurHash3_32` + `uint32_to_float32` 这一种转换（Nuke 文档原话："Only one hash type, MurmurHash3_32, and one conversion method, unit32_to_float32, are currently supported."）。CB 写整数 RT，**不需要位运算、不需要指数修补，也不会被任何滤波或色彩管理碰到**。
- **ID pass + Coverage pass 分离**：Nuke 社区的标准做法是"用 ID 与 Coverage 一起生成 matte，**覆盖率放 alpha 通道**"，Coverage 单独一个 tab 调（它是软硬程度的唯一来源）。见 [Nukepedia: Color Picker ID](https://www.nukepedia.com/tools/gizmos/image/color-picker-id/)。
- **"选谁"由合成端决定，而不是渲染端**：Arnold 提供 `crypto_asset` / `crypto_material` / `crypto_object` 三种 AOV——**同一份 ID 数据的三种分组口径**（与我们的"类别 + 标签"同构）；Redshift 明说"**Multiple objects can share the same Redshift Object ID number if you want to group objects together into a single matte**"（这正是我们的标签/类别语义）；Blender 的 Cryptomatte 节点是"点一下画面就选中"（`Pick` 输出 + `Matte ID` 名字列表）。见 [Arnold: Cryptomatte](https://help.autodesk.com/cloudhelp/2024/ENU/AR-Maya/files/am-Arnold_for_Maya_User_Guide/render-settings/aovs/arnold_for_maya_aovs_am_Cryptomatte_html.html)、[Redshift: Cryptomatte](https://help.maxon.net/r3d/katana/en-us/Content/html/Cryptomatte.html)、[Blender: Cryptomatte 节点](https://docs.blender.org/manual/en/latest/compositing/types/mask/cryptomatte.html)（该站对本环境拒爬，内容经镜像核对）。
- **层数是"一像素里能有几个东西"的预算，而我们只需要 N 个**。Redshift 把它定义成"how many objects can **intersect on a single pixel** and still be considered as separate mattes"。离线要更多层，是因为它表达的是**连续的面积占比**（还要覆盖运动模糊与景深的分布，对象数与层数都无上界）；而我们要表达的是 **MSAA 的逐样本归属**——一像素最多 N 个样本，所以 **K = N = 4 就已经无损**（§5.5）。**这就是不照抄"6~8 层"的理由**。
- **离线渲染器同样区分"整数（不抗锯齿）"与"抗锯齿"两种导出**：V-Ray 的 ObjectID 有 integer (no AA) 模式——"Object IDs are exported … as an Integer value, one integer per pixel"（**二手来源**，官方站拒爬）。这与我们"ID 必须点采样、AA 只能来自覆盖率"是同一条纪律的两种表述。
- **最清楚的一段散文定义**（OTOY Octane 文档）："Cryptomattes use an ID-coverage paring technique… The ID channel is one object per pixel. The coverage channel determines how much of the pixel is contributed to by the assigned object. These ID-coverage pairs are then **ranked** to add support for multiple objects per pixel… **That is why the Cryptomatte Channels are always in pairs of two**." → 我们的"ID 与覆盖率成对、按名次排层"就是它；**层数默认值业界不统一**：Octane `Bins` 默认 6（且必须偶数，"the ID channel and coverage channel must be kept together"）、Redshift `Cryptomatte Depth` 默认 8、Psyop 的 Nuke gizmo 暴露 12 个层槽、Blender legacy 节点默认 4 个层输入（可加）。见 [Octane: Cryptomatte](https://docs.otoy.com/cinema4d/Cryptomatte.html)、[Redshift Cryptomatte (Houdini)](https://help.maxon.net/r3d/houdini/en-us/Content/html/Cryptomatte.html)、[Blender legacy 节点 RST 源](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/compositing/types/mask/cryptomatte_legacy.rst)。
- **排序口径业界并不统一**：MoonRay 说"**sorted by max coverage**… the geometry with the most pixel coverage will always be the first entry"，而 Octane 说名次表示"**front to back**"的前后关系。**我们明确选按覆盖率（= 样本数）降序、平票再取更近样本**——因为它直接对应 MSAA 的样本计数，且让"层0"有稳定含义（§5.5）。
- **"ID 不能被邻居混合"是业界的原话纪律**（Psyop `docs/nuke.md`）："Cryptomatte relies on **exact values in channels**, and operations that **mix values with neighboring values will damage this information**, resulting in only being able to extract mattes on pixels containing only one object"；同页还提醒代理模式（降分辨率）"gives bad results for similar reasons"。→ **这两句直接支撑我们的两条硬规则**：ID 一律点采样（§6 第 1 条）、以及 **v1 不暴露 renderScale、固定 Full**（§5.9②）。
- **业界没有"归一化/残差"的明文规定**：核对过的来源里**没有**一条要求 `Σcov = 1`，也没有"残差"这个术语；他们只用"背景 = ID 0"（Psyop Nuke/Fusion 代码里 `BACKGROUND_MATTE_NAME = "Background (value RGBA=0000)"`）与"层数不足会掉 ID"两条间接处理。**我们把残差显式化、并禁止把层覆盖率重标定到和为 1，是有意加强**，不是抄来的——厂商侧的对应物只有"层数取小了会在选中被丢弃的对象时产生噪声"（§4.1 上一条）。
- **一个可选的未来扩展（现在是 v1 的一部分）**：Nuke 的 **Encryptomatte** 能把"任意一张 alpha"转成一个可选中的 ID+覆盖率对（并可 over/under 合并进已有 cryptomatte 层）——**这就是 §5.11 的选择层的原型**：材质侧把作者遮罩写成 `(选择 ID, 覆盖率)`，下游按名字点选。见 [Keying with Cryptomatte](https://learn.foundry.com/nuke/content/comp_environment/cryptomatte/keying_with_cryptomatte.html)。

### 4.2 逐样本身份的两极：离线 deep image 与实时 k 层缓冲

**deep image 是这个模型的"无上界"版本**（OpenEXR ≥ 2.0）："each pixel in a deep image can store **an arbitrary number of values or samples** per channel. Each of those samples is associated with a depth"、"The number of samples varies from pixel to pixel, and **any non-negative number of samples, including zero, is allowed**"；而且**每个通道都必须有配对的 alpha**（"Every color or auxiliary channel in a deep image must have an associated alpha channel"）。对象标识在 deep 里是一等公民："Deep IDs are primarily used in compositing applications to select objects in images: The 3D renderer stores multiple ids per pixel … as well a scene manifest"、"**Each pixel can contain an arbitrary number of IDs (0 included), allowing for perfect mattes**"、"Deep IDs are combined with transparency data to support anti-aliasing, motion blur and depth of field"。

→ **我们的 K = 4 就是这条曲线的有界版本**：层数封顶换来固定显存与可预测带宽；而"尾部身份会丢"这个代价，因为 **K = N** 在实际配置里并不成立（§5.5）。这也是我们不去追 6~8 层的原因——离线要的是连续面积占比，我们要的是逐样本归属。

**"我的选择占多少"的正确算法规范里就有**（Deep IDs 规范的浅层 matte 伪代码，前到后累积）：

```text
foreach sample in sorted_pixel_front_to_back:
    if id_is_in_selection(sample.id):
        mask_alpha += sample.alpha * (1 - total_combined_alpha)
    total_combined_alpha += sample.alpha * (1 - total_combined_alpha)
return mask_alpha / total_combined_alpha
```

这是 §6 第 7 条 `result = Σ coverage_i × f(id_i)` 的**遮挡正确版**（我们那条是简写，落地时按这个形式写）；也提醒两件事：①**消费端为了出 matte 做 `/ total_combined_alpha` 是合法的**，与 §5.5 禁止的"把层覆盖率重标定到和为 1"不是同一件事；②规范同时承认"Edge contamination may be observed along transparent edges of a selected object, if an object behind it is not selected"——**选了一层就会沾上后面没选中的东西**，这是覆盖率模型的固有边界（与 §5.5 的"覆盖率说多少、不说形状"同源）。见 [OpenEXR: Deep IDs Specification](https://openexr.com/en/latest/DeepIDsSpecification.html)、[Interpreting Deep Pixels](https://openexr.com/en/latest/InterpretingDeepPixels.html)。

- **身份数据天生不适合被排序/合并**：规范明说"making an image tidy loses information, and **some kinds of data cannot be represented with tidy images, for example, object identifiers**"。
- **manifest 应当嵌进文件，而不是 sidecar**：Deep IDs 用 `idmanifest` 属性（OpenEXR 3.0+，可用 `exrmanifest` 打印），sidecar"not supported by the OpenEXR library, nor are they defined here"，且"it is **strongly recommended** that the embedded manifest is used"；Blender 的 Cryptomatte 节点更直接："Cryptomatte sidecars (metadata files) are not supported"。→ **§5.10 的 palette manifest 默认嵌 EXR metadata**，sidecar 只作兜底。见 [exrmanifest](https://openexr.com/en/latest/bin/exrmanifest.html)。
- **ID 通道的命名与位宽先例**：`id` / `objectid` / `materialid` / `particleid` / `instanceid`；64 bit ID 用两个 uint32 通道加 `0`/`1` 后缀。我们是 16 bit（角色 8 + 槽位 8），AOV 沿用契约里已有的 `id_object` / `id_group` 命名（§5.10）。
- **deep 的代价也写在规范里**：任意样本数意味着内存，"Cryptomatte allocates a fixed size array for IDs… Setting this too large requires a lot of memory; setting it too small can cause noise"——**这条权衡我们选了另一头**（固定 K、但保证 K ≥ N）。

**实时一侧的同族做法**（都在做"把身份放进缓冲、把解释推迟到 resolve"）：

| 参考 | 它存什么 | 对我们的意义 |
| --- | --- | --- |
| **The Visibility Buffer**（Burns & Hunt, JCGT 2(2), 2013） | 每像素只存**可见图元的 ID**（+ 深度），着色推迟到之后按 ID 取属性 | "身份进缓冲、属性延后"的实时版原型；我们的 ID pass + palette 是它的语义化版本 |
| **Deferred Attribute Interpolation**（Schied & Dachsbacher, HPG 2015） | 为**边缘像素保留多层**图元 ID，resolve 时再插值属性 | 与"K 层 ID + resolve 归约"同形；**它保留多层的动机正是抗锯齿**，与决策 1 一致 |
| **Deep G-Buffers**（Mara 等, HPG 2016） | 每像素 **k 层**深度/法线 | 专门为减少屏幕空间效果在**轮廓处**的抖动——正是"前发投影在发际线处需要多层信息"的理论依据 |

引用状态：JCGT 文章页可访问（[jcgt.org/published/0002/02/04](https://jcgt.org/published/0002/02/04/)，PDF 在 `/paper.pdf`）；DAIS 与 Deep G-Buffers 的出版方站点（ACM DL / Eurographics diglib）在本环境被 Cloudflare 拦或 DNS 不可达，此处以稳定标识符给出——DAIS `10.1145/2790060.2790066`（HPG 2015）、Deep G-Buffers `10.2312/hpg.20161195`（HPG 2016），**标为未逐字核对**（标题、作者、会议年可查，具体页码/引文未核）。

### 4.3 覆盖率从哪来、怎么被读：MSAA 的官方规则（决定我们能做什么、不能做什么）

- **逐样本覆盖是硬件行为，像素着色器每像素只跑一次**：D3D 光栅化规则写得很直白——"a coverage test is performed for each sample location (not for a pixel center). If more than one sample location is covered, **a pixel shader runs once with attributes interpolated at the pixel center. The result is stored (replicated) for each covered sample location**"。这正是 §5.4"逐样本身份"能成立的前提：**身份必须在一次 draw 内是常量**（我们的 RSUV 部件身份正是），每个样本归谁由硬件的逐样本深度测试决出。也说明"把 ID 插值到逐样本"在定义上就是错的。见 [Rasterization Rules](https://learn.microsoft.com/en-us/windows/win32/direct3d11/d3d10-graphics-programming-guide-rasterizer-stage-rules)、[Getting Started with the Rasterizer Stage](https://learn.microsoft.com/en-us/windows/win32/direct3d11/d3d10-graphics-programming-guide-rasterizer-stage-getting-started)。
- **resolve 就是求平均，且只对颜色成立**："The final value for the pixel is calculated as **the average of all samples**. This is known as the resolving step."（GPUOpen/Vulkan-Samples）；API 层 `ResolveSubresource` 的源"Must be multisampled"、目标"**must be single-sampled**"，即**逐样本信息不会被保留**。Vulkan 规范还补了一句更狠的：整数格式 resolve 时"a **single sample's value is selected** for each pixel"（不是平均，是**任选一个样本**——对 ID 来说等于随机丢身份；该页官方站拒爬，引的是规范文本的社区镜像）。所以身份**不可能**靠 resolve 得到，只能像我们这样逐样本 `Load` 再自己归约。见 [Vulkan-Samples: MSAA](https://gpuopen-librariesandsdks.github.io/Vulkan-Samples/samples/performance/msaa/)、[ResolveSubresource](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-resolvesubresource)、[Rasterization Rules](https://learn.microsoft.com/en-us/windows/win32/direct3d11/d3d10-graphics-programming-guide-rasterizer-stage-rules)（"no resolve is required for individual samples accessed by the shader"）。
- **逐样本读取的机制**：`Texture2DMS.Load(coord, sampleIndex)` / `GetSamplePosition` / `GetRenderTargetSampleCount`（SM4+），以及 `EvaluateAttributeAtSample`（SM5+，用于把某个属性求值到指定样本）。见 [Texture2DMS](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/sm5-object-texture2dms)、[Texture2DMS::Load](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/texture2dms-load)、[EvaluateAttributeAtSample](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/evaluateattributeatsample)。
- **"不 resolve、直接绑成 MSAA 纹理"是官方支持的用法**：`RenderTextureDescriptor.bindMS` ——"If true and msaaSamples is greater than 1, the render texture will **not be resolved by default**. Use this if the render texture needs to be bound as a multisampled texture in a shader."（这正是 GB 与 CB 取逐样本数据的方式）。见 [RenderTextureDescriptor.bindMS](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTextureDescriptor-bindMS.html)。
- **采样数协商 API 本身就是"给你一个平台支持的更低值"**：`SystemInfo.GetRenderTextureSupportedMSAASampleCount(desc)` ——"Returns the given MSAA samples count [if supported]. Otherwise returns a **lower fallback** MSAA samples count value that the target platform supports."。**所以 CB 只要直接要 4、拿回 2 或 1**，不需要（也不应该）去看相机的 MSAA 设置——这把决策 7 的"与相机解耦"变成了 API 层的自然做法。见 [GetRenderTextureSupportedMSAASampleCount](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SystemInfo.GetRenderTextureSupportedMSAASampleCount.html)。
- **per-sample 写入的后手（决策 6）有官方约束**：alpha-to-coverage 是"n-step coverage mask"，且"the runtime performs an **AND** operation of this mask with the typical sample coverage"，并且"**in multisampling, the runtime shares only one coverage for all RenderTargets**"——一张掩码同时切该 pass 的所有 MRT（ID 与覆盖率会一起被切）。Unity 侧 `AlphaToMask On` 官方也标注了 URP/HDRP 支持，并警告"This command is intended for use with MSAA… otherwise the results can be unpredictable"；UE 的 Forward 渲染器把"**Alpha-to-coverage for Masked Materials**"列为受支持特性，采样数由 `r.MSAACount` 控制（`1` = 关闭）。另：像素着色器一旦输出 `SV_Coverage`，alpha-to-coverage 会被关闭。见 [D3D Blending / Alpha-To-Coverage](https://learn.microsoft.com/en-us/windows/win32/direct3d11/d3d10-graphics-programming-guide-blend-state)、[Unity: AlphaToMask](https://docs.unity3d.com/Manual/SL-AlphaToMask.html)、[Unity: Reduce aliasing with AlphaToMask](https://docs.unity3d.com/6000.0/Documentation/Manual/writing-shader-alpha-to-mask.html)、[UE: Forward Shading Renderer](https://dev.epicgames.com/documentation/en-us/unreal-engine/forward-shading-renderer-in-unreal-engine)。
- **对照：把语义压进位掩码/层掩码在引擎里是有已知代价的**：URP 文档明确"Performance impact increases more significantly when the number of Rendering Layers reaches 9, 17, 25, etc. This is because … URP adds an **extra texture channel** the GPU must access." —— 每加一个 8 bit 通道就多一次纹理访问，这是"位掩码当语义容器"的隐性账单；CB 的 per-pixel 成本只由 K 与通道族决定，**部件数量增长不进 per-pixel 成本**。见 [URP: Introduction to Rendering Layers](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/features/rendering-layers-introduction.html)。

### 4.4 引擎怎么把"身份 / 自定义数据"递给效果

- **UE：Custom Depth + Custom Stencil**。官方文档说明 Custom Depth 是"masking of certain objects by rendering them into another depth buffer… fairly cheap as we only output depth"，Custom Stencil 是在其之上"a stencil, or cutout, of your rendered object"，取值范围 0–255（8 bit），由 **per-primitive** 设置（`SetCustomDepthStencilValue`："Sets the CustomDepth stencil value (0 - 255) and marks the render state dirty"），post-process 材质可读。**最有价值的是它写下的局限**：这类轮廓在 TAA 下得不到抗锯齿（TAA 每帧把整场景移动一个亚像素），把它挪到后处理链更前面能缓解，但"outside the object we would also need to adjust the depth buffer (not done yet, costs extra performance)"。**这就是我们这次要解决的那类问题的官方版**——掩码是硬的，AA 只能靠消费端猜。见 [Post Process Materials](https://dev.epicgames.com/documentation/en-us/unreal-engine/post-process-materials-in-unreal-engine)、[Set Custom Depth Stencil Value](https://dev.epicgames.com/documentation/en-us/unreal-engine/BlueprintAPI/Rendering/SetCustomDepthStencilValue?application_version=5.1)。
- **UE：per-primitive / per-instance custom data 走 scene-wide buffer + 索引**。"Primitive Parameters … are placed in a scene-wide primitive data buffer called `GPUScene` and **indexed in the shader using `PrimitiveID`**"，材质侧用 `PerInstanceCustomData` 节点按下标取值，**每 primitive 上限 32 个 float**；官方给的收益理由与我们一致："storing data on the primitives themselves rather than with the Material Instance… **lowers the number of draw calls**"。这就是"表 + 索引"的引擎级同形物。见 [Mesh Drawing Pipeline](https://dev.epicgames.com/documentation/en-us/unreal-engine/mesh-drawing-pipeline-in-unreal-engine)、[Storing Custom Data in Materials Per-Primitive](https://dev.epicgames.com/documentation/en-us/unreal-engine/storing-custom-data-in-unreal-engine-materials-per-primitive)、[MaterialExpressionPerInstanceCustomData](https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/MaterialExpressionPerInstanceCustomData?application_version=4.27)。
- **HDRP：Rendering Layers + 一张逐像素可采样的 mask buffer**。"A Renderer can support up to **32 rendering layers**, but all HDRP effects using Rendering Layers only support the **first 16 layers**"；在 HDRP Asset 里打开 **Rendering Layer Mask Buffer** 后，ShaderGraph 可用 `HD Sample Buffer` 节点以 `RenderingLayerMask` 为源**逐像素采样该 buffer**。它证明了两件事：①引擎确实会给效果一张"逐像素身份/掩码图"；②"层数越多，效果能支持得越少"是这类方案的常见天花板（所以我们要的是"ID + 覆盖率"而不是"更多层"）。见 [HDRP: Use light rendering layers](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Rendering-Layers.html)、[Renderer.renderingLayerMask](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-renderingLayerMask.html)。
- **Unity：RSUV 的官方定位就是"索引"**。"RSUV is a custom **32-bit integer** value…"；use case 表第三行原文——"Large amounts of custom data per renderer → **Use the RSUV as an index into a larger data structure stored in a global GraphicsBuffer**"；并且"This functionality **doesn't interfere with batching**"、"introduces **no additional CPU overhead**"、"significantly faster than using Material Property Block (MPB)"（MPB 在 SRP 下"isn't recommended… has low performance at runtime and might not work as expected"）。**两个必须记住的实现约束**：受支持的是五个具体类型的方法（`MeshRenderer` / `SkinnedMeshRenderer` / `SpriteRendererDataAccessExtensions` / `SpriteShapeRenderer` / `TilemapRenderer`，**不在 `Renderer` 基类上**）；以及"**The value is not serialized**, so it is not saved to the asset and **resets when the object is reloaded**"（§5.2 的纪律就来自这一句）。见 [Introduction to RSUV](https://docs.unity3d.com/6000.5/Documentation/Manual/renderer-shader-user-value-intro.html)、[Set and use the RSUV](https://docs.unity3d.com/Manual/renderer-shader-user-value-set-and-use.html)、[MeshRenderer.SetShaderUserValue](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/MeshRenderer.SetShaderUserValue.html)。
- **渲染 ID 图的传统做法（对照，不是我们要走的路）**：Built-in 的 `Camera.SetReplacementShader` / `RenderWithShader` 按 **RenderType 标签**整体替换 shader（"Any objects whose shader does not have a matching tag value … will not be rendered"），`CommandBuffer.DrawRenderer` 可以逐个画但"the rendered mesh will not have any lighting related shader data … the results are undefined"。前者只能按标签分类、拿不到逐部件身份与覆盖率；后者是逐 draw 的兜底路径（与决策 12 的兜底同形）。见 [Shader replacement](https://docs.unity3d.com/6000.7/Documentation/Manual/SL-ShaderReplacement.html)、[Camera.RenderWithShader](https://docs.unity3d.com/ScriptReference/Camera.RenderWithShader.html)、[CommandBuffer.DrawRenderer](https://docs.unity3d.com/ScriptReference/Rendering.CommandBuffer.DrawRenderer.html)。

### 4.5 从参考直接推出的设计结论（对照表）

| 参考的做法 | 我们的对应实现 | 落在哪条决策 / 小节 |
| --- | --- | --- |
| Cryptomatte 多层 `(ID, 覆盖率)` + manifest；层按覆盖率降序 | K = 4 层 `(ID, 覆盖率)` + palette（两级表）+ AOV manifest；按样本数 = 覆盖率降序，平票取更近样本 | 决策 1 / 2 / 8，§5.1、§5.3、§5.5、§5.10 |
| 离线默认 6~8 层（表达连续面积占比，对象数无上界） | **K = N = 4**：只表达 MSAA 的逐样本归属，一像素最多 N 个样本，因此已经无损 | 决策 1 / 7 / 17，§5.5 |
| 一个 ID 可覆盖多个对象（Redshift 共享 Object ID、Arnold 的 asset/material/object 三种口径） | 类别（单值）+ 标签位掩码（32 位，多归属）；"整角色"语义放角色表那一行 | 决策 9 / 10，§5.3、§5.7 |
| Nuke：ID 不过滤、覆盖率放 alpha、分别可调 | ID 一律 `Point` + `round(v*255)` 还原；覆盖率线性、只能按 ID 匹配加权；AOV 里 coverage 进 alpha | §5.5、§6 第 1~3 条、§5.10 |
| UE `GPUScene`：属性进 scene-wide buffer、shader 内按 ID 索引 | palette（`StructuredBuffer`）由 CPU 维护，像素里只有索引 | 决策 8 / 11，§5.2、§5.3 |
| UE Custom Depth Stencil：8 bit、per-primitive、效果端硬边且 TAA 救不了 | 不再用"位/层分类"，改用 16 bit ID + 真实覆盖率；亚像素相位由自建 MSAA 给出 | 决策 2 / 7 / 17，§5.4、§5.5 |
| HDRP：给效果一张逐像素 mask buffer；层数越多效果支持越少 | 给效果 ID + 覆盖率，而不是更多"层"；per-pixel 成本与部件数量无关 | §5.1、§5.6、§4.3 末条 |
| RSUV 官方 use case = 索引进全局 buffer；不序列化 | RSUV 只当 palette 索引；OnEnable / 表变化时重写；palette 第 0 行 = unknown | 决策 13，§5.2、§5.7 |
| MSAA：像素着色器输出被复制到所有通过深度测试的样本 | 逐样本身份靠"整次 draw 身份恒定 + 硬件逐样本深度测试"，不靠插值 | §5.4 |
| resolve 只对颜色是平均、不保留逐样本信息（整数格式甚至只挑一个样本） | 自建 resolve：逐样本 `Load` → 数票 → 写 4 层；**不做平均** | 决策 17，§5.4、§5.9⑤ |
| Alpha-to-coverage 是 MSAA 的官方配套；一张掩码作用于所有 MRT | 决策 6 的后手（per-sample 写入）留给将来，且知悉"ID 与覆盖率会一起被切" | 决策 6，§5.4 |

---

## 5. 设计

### 5.1 per-pixel 布局

per-pixel 只装 **ID**（身份）、**覆盖率**（多少）、以及**真正逐像素的材质值**；图的数量只由**层数 K**、**ID 位宽**与**材质通道族**决定，区域/语义/部件数量不进 per-pixel 布局。

| 图 | 格式 | 内容 | 采样 | 分配 |
| --- | --- | --- | --- | --- |
| `_HoCharacterBufferId0` | R8G8B8A8_UNorm | r/g = 层0 角色/槽位，b/a = 层1 角色/槽位 | **Point** | 常开 |
| `_HoCharacterBufferId1` | R8G8B8A8_UNorm | 层2、层3 同上 | **Point** | 常开 |
| `_HoCharacterBufferCoverage` | R8G8B8A8_UNorm | 4 层各自的覆盖率 | **Point**；要滤波只能在 **ID 匹配的 tap** 上加权（§5.5） | 常开 |
| ~~`_HoCharacterBufferSurface`~~ | — | **搬去 `Ho-SurfaceBuffer`（§5.12 / 决策 20）**：线性表面色回答的是"表面是什么样" | — | 不在 CB |
| ~~`_HoCharacterBufferMaterial0`~~ | — | **搬去 `Ho-SurfaceBuffer`**：roughness / metallic / thickness 同理 | — | 不在 CB |
| `_HoCharacterBufferSelection` | R8G8B8A8_UNorm | **选择层**：`R=选择ID0, G=覆盖率0, B=选择ID1, A=覆盖率1`（Cryptomatte 的成对布局，§5.11） | **Point**（ID 与覆盖率都不滤波） | **按需**（组里没有选择条目就不分配；4 个选择再加一张同构的图） |
| （ID pass 的 depth-stencil） | 深度格式（MSAA 开启时是 MSAA 附件） | **内部附件**：决出 sample 归属、tie-break 取更近样本 | — | 常开、**不发布、不暴露给任何 shader（含 debug 视图）** |

ID + 覆盖率合计 **3 张 RGBA8 = 12 B/px**；选择层按需（2 个 = +4 B/px）。

**CB 到此为止：只有身份与覆盖率。** 线性表面色、roughness / metallic / thickness、reflectance / PLR、materialClass / curvature / transmittance **全部搬去 `Ho-SurfaceBuffer`（§5.12 / 决策 20）**——它们回答的是"表面是什么样"，不是"这是谁"；混在一起就会重演 MetadataBuffer"一个 buffer 承担多种语义"的老毛病。CB 因此也**没有**独立的单采样材质 pass 了。

**不发布任何几何通道**（与旧设计的关键差异）：消费端的几何门控继续读 `_HoGeometryBufferNormalDepthTexture` / `_HoGeometryBufferDepthTexture` / `_HoGeometryBufferCoverageTexture`。理由是 ①它们已经在用；②ID resolve 需要的是 per-sample 深度，属于 MSAA 附件、发布出去没有意义；③**发布第二份深度本身就是"1px 错位"类 bug 的温床**（两份深度不同源、采样数不同、覆盖集合不同）。旧设计那两张 depth 也都只是 pass 自己的 depth-stencil 附件（§2.4：元数据那张连全局 ID 都没有；`MBufferDepth` 虽有 ID，但真消费者只有两个 debug 视图）。
**连 debug 也不例外**：调试要看深度/法线就走 GeometryBuffer 已有的视图（`geometry.linear-depth` / `geometry.world-normal` / `geometry.coverage`），ID pass 的 depth 附件不出现在发布清单、不进契约、也不给 debug 采样（§5.1 规则 2）。深度与法线的入口在全管线只有 GeometryBuffer 一个。

**几何唯一来源的三条配套规则**（决策 16）：

1. **要几何只有一个地址**：`_HoGeometryBufferNormalDepthTexture`（法线 + 线性深度）/ `_HoGeometryBufferDepthTexture` / `_HoGeometryBufferCoverageTexture`。CharacterBuffer 的消费者里凡涉及法线、线性深度、几何覆盖率的一律读这里——现有消费端（角色特化 composite、SSS、PLR、ScreenProcess 各效果、GTAO、DebugTile）本来就都是这么读的，迁移**不碰几何层**。（覆盖率读法：MSAA 下 `_HoGeometryBufferCoverageTexture.r` = 总覆盖率，配 `_HoGeometryBufferCoverageTextureValid` 门控；否则 `LilHoGeometryBufferCoverage()` 从 `NormalDepth.a` 派生。**那张图的 `.g` 是"被解析面占有率"，由法线夹角 + 深度带的启发式分组算出，不能当成"某个部件的占比"**——那正是 CB 要精确回答的问题。）
2. **内部附件允许存在、永不发布**：ID pass 必须有自己的 depth-stencil（占用判定 + tie-break），但它不出现在任何发布清单、不进契约、不被采样。
3. **永不比较两个来源的深度**：这正是 `MSAA.md` 里那类"约一个采样间距错位"的来源。已知的**结构性例外**只有一个——眼透的 `hairInFront` 必须比较"捕获到的眼睛深度"（`HoCharacterCaptureCommon.hlsl` 里 pre-multiply 的线性深度）与 GeometryBuffer 深度：被前发遮住的眼睛在 GeometryBuffer 里根本不可见，所以这个跨来源比较无法消除，保留并在此记录。

**通道字典（per-pixel 到底装了谁的东西）**

| 图 | 格式 | 通道 | 归属 | 分配 |
| --- | --- | --- | --- | --- |
| ~~`_HoCharacterBufferSurface`~~ | — | 搬去 `Ho-SurfaceBuffer` | 表面 | 不在 CB |
| ~~`_HoCharacterBufferMaterial0`~~ | — | 搬去 `Ho-SurfaceBuffer` | 管线 | 不在 CB |
| `_HoCharacterBufferSelection` | **RGBA8** | `R/G = 选择0 的 (ID, 覆盖率)`、`B/A = 选择1 的 (ID, 覆盖率)`（§5.11） | **用户 / 材质** | **按需**（组里注册了选择才分配） |

- 为什么分成两族：**管线那四个都是 0~1 归一量，8 bit 足够**；用户那一族**不再是"匿名浮点通道"，而是"具名的选择"**——因为匿名通道用不了一年就会变成一堆没人知道含义的数（现在的 `custom0~3` 就是这样），而选择有名字、有颜色、能被下游点选。
- 为什么"挤"消失了：今天真正抢预算的是 **RSUV 位 + system 通道开关 + custom write mask** 三件；新方案只把**真正逐像素的值**留在通道里，那三件全部变成 **palette / 选择表 的 per-part 字段**（表里没有预算问题）。
- 增长路径：选择不够用时加第二张选择图（+4 B/px，4 个选择/像素）；**再往上不建议**——超过 4 个重叠遮罩的场景应该拆网格或用标签，而不是继续加图（§5.11）。

哪些原本看着像"每像素"、实际只是 per-part 常量（因此进 palette 而不占通道）：`materialClass`、`curvature`、`transmittanceHint`、`reflectance`、`PLR strength`；`thickness` 与 roughness / metallic 只在被贴图驱动时才需要逐像素通道。

约定：**ID 0 = 背景，不占槽位**；只存非背景 ID，背景由残差表达，因此"角色占比" = `Σcov`，与今天 `maskId.r` 一致。

16 bit/层（角色 8 + 槽位 8）的理由：**"同角色"退化成一次 byte 比较**，而前发投影的 `same`、眼透遮挡都是热路径，不该为隔离判断查表。8-bit 平坦方案更省，但要么只支持 2 层、要么把部件卡在 256。

### 5.2 ID 传输：palette 索引怎么送进 shader（决策 13）

**用 RSUV 携带 palette 索引。** 这是 Unity 对该需求给出的标准机制：官方文档明确它**不干扰合批**、**无额外 CPU 开销**、比复制材质略快而比 MPB 快得多，并且**配合 GPU Resident Drawer 收益最大**；官方 use case 表里"大量 per-renderer 自定义数据"一条推荐的正是"**RSUV 当索引，指向全局 GraphicsBuffer 里的数据结构**"（见 [Introduction to RSUV](https://docs.unity3d.com/6000.5/Documentation/Manual/renderer-shader-user-value-intro.html)、[Set and use the RSUV](https://docs.unity3d.com/Manual/renderer-shader-user-value-set-and-use.html)）。

- **32 bit 的分配**：低 16 bit = palette 索引（角色 8 + 槽位 8）；高 16 bit 由本 feature 预留（例如将来的 per-part 排除位），**并把这块分配登记进 `LILTOON_CHANNEL_CONTRACT_V1.md`**——共享槽的隐患靠"登记分区"解决，而不是靠弃用机制。RSUV 只要当索引就永远够用，属性增长全在 palette 里。
- **支持范围**（官方页面逐个点名）：`MeshRenderer` / `SkinnedMeshRenderer` / `SpriteShapeRenderer` / `TilemapRenderer` 各自的 `SetShaderUserValue(uint)`，以及 `SpriteRenderer` 的扩展方法 `SpriteRendererDataAccessExtensions.SetShaderUserValue(uint)`——**都不在 `Renderer` 基类上**，所以现有 `TrySetRendererUserValue` 只处理两种属于**代码没写全**；shader 侧统一读 `unity_RendererUserValue`。角色部件的实际类型都在列表内。
- **兜底路径**：极少数拿不到 RSUV 的 renderer，退回"逐部件设全局常量再绘制"——只对这部分付出 draw call 代价。
- **删除 MPB 回退**：官方在 SRP 下不推荐 MPB（性能差、可能不生效），而且它会让该 renderer 连**主 pass** 一起失去 SRP Batcher。
- **材质侧**：从 RSUV 解出索引 → 查 palette 取 category / tags（就是官方"索引进全局 buffer"的用法），**不需要 CPU 逐 draw 设全局**。
- **RSUV 不会被序列化**（官方原话："The value is not serialized, so it is not saved to the asset and resets when the object is reloaded"，且烘焙 lightmap/lightprobe 时 `unity_RendererUserValue` 恒为 0）。所以 `HoCharacterBufferGroup` 必须在 **OnEnable / 部件表变化 / 场景重载后重新写入**，不能只在编辑器里写一次——否则域重载后全场景的索引一起变 0。**配合一条纪律：palette 第 0 行永远留作 unknown**，索引 0 显示为"未注册部件"而不是静默变成某个真部件。
- `HoCharacterBufferGroup` 只写 RSUV（索引）+ 维护部件表与校验。

### 5.3 调色板（palette）

- **载体**：**两级表**（决策 8）——`StructuredBuffer<HoCharacterPartData>`（全局 ≤4096 行）+ `StructuredBuffer<HoCharacterData>`（角色表，≤256 行、常驻）。后端不支持 StructuredBuffer 时退化为一组 float4 常量数组并按 256 行分块。**buffer 没有过滤概念，读到的永远是本像素解析出的那个索引对应的整数行。**
  - **为什么必须两级**：像素里的 ID 是 `角色 8 + 槽位 8`（**稀疏**，编码空间 65536），而 palette 行数是**注册预算** 4096（决策 2）。单张 4096 行表没法用 `char<<8|slot` 直接索引（那样只覆盖 16 个角色），而"反向映射表（char,slot → 行号）"又多一次依赖读取、还要与像素里的 ID 保持同步。
  - 做法：`row = characterTable[charId].rowBase + slotId` —— 两次点采样、无常驻反向表。**同角色判断仍然是像素 ID 的高字节比较**（§5.1），不需要查表；标签位（如 `CharacterFull` = 该角色任意部件）放角色表那一行，更省。
- **每行字段**：部件行 = `characterId`、槽位号、**partCategory（枚举，取代 8 bit 语义）**、**标签位掩码（32 位，决策 9）**、**显示色**（Nuke "color picker ID" 的颜色）、名字 hash（AOV manifest）；角色行 = `rowBase`、角色级标签（`CharacterFull` 这类"整角色"语义放这里）。
- **材质数值既不进组件、也不进 CB 的 palette**（决策 20）：`materialClass` / `thickness` / `curvature` / `transmittance` / `roughness` / `metallic` / `reflectance` / `plrStrength` 这些都是"表面是什么样"，**归 `Ho-SurfaceBuffer`（§5.12）**——谁的值谁写（材质侧逐像素写），palette 只放身份。所以部件行现在只有：`partId` / `nameHash` / `category` / `tags` / `displayColor` = **32 B**（原来 64 B，4096 行少一半显存）。**这条也是"材质会不会被组件覆盖"的最终答案：不会，也不该——组件里根本没有这些字段。**
- **角色隔离不靠额外图**：ID 的高字节即角色；可视化 / 导出查 `displayColor` —— 即业界那套"用颜色表示 ID"，但颜色是**查表得到**而非把 hash 塞进像素（hash 进像素会让 ID 再也不能被任何滤波碰）。
- **同义量只能有一个来源**：`roughness` / `metallic` / `thickness` 既在 palette（常量缺省）又在 `Material0`（贴图驱动的逐像素值）。规则：由部件行 `flags` 里的"贴图驱动"位决定——**置位 = 只读 `Material0`、palette 对应字段无效；未置位 = 只读 palette、`Material0` 对应通道未定义**。消费端只查这个位，不许"两边取一个"、更不许相乘。
- **越界兜底**：`charId` 越界、或 `slot >= characterTable[charId].slotCount` → 返回 unknown 行。**不能简单 clamp 行号**——`rowBase + slot` 越界会落到**别的角色**的行上，读出来的属性看着合法、其实是错的（比崩溃更难查）。
- 代价与收益：旧 **7 张**（`maskId` RGBA8 + `surfaceData` / `objectCustom0` / `objectCustom1` / `reflectionMaterial` 四张 16F 直接消失，常量进 palette、身份进 ID；`surfaceColor` → `_Surface`、`custom0` → 按需的选择层 §5.11）换成 **ID 三张 RGBA8 + `_Surface` 一张 16F + 按需的 `Material0` / 选择层**；上传策略见决策 11。
- 与 palette 并列的还有一张**选择表**（§5.11）：同样是"表 + 索引"，但索引空间独立（8 bit），且只有注册了选择才分配那张图。

### 5.4 覆盖率怎么产生

自建 MSAA，**与 GeometryBuffer 逐件对齐**（决策 7）：采样数协商 → MSAA 绘制目标 → resolve pass，RenderGraph 与兼容两条路径同构，resolve shader 用同一套多关键帧 + `Texture2DMS` 逐样本读取。

**唯一差异是 resolve 语义**：GeometryBuffer 是"挑最近样本得到该像素几何 + 写覆盖率"；这里是"**逐样本数票，取占比最高的 4 个 ID 及各自占比**"。共同点是**都不做平均**。

ID pass 的 depth-stencil 附件只服务于两件事：**决出每个 sample 归属谁**，以及计数相同时**按更近的样本 tie-break**。它不发布给任何消费端（几何判断一律走 GeometryBuffer，见 §5.1）。

**为什么"逐样本身份"能成立（这不是凑合，是 MSAA 的定义决定的）**：按 D3D 的光栅化规则，多边形的覆盖判定是**逐样本**做的，但**像素着色器每像素只跑一次**，其输出会被**复制到所有通过深度/模板测试的样本**上。所以：
- 想逐样本得到**不同**的身份，唯一可行的办法就是**身份在这一次 draw 内是常量**——正是我们的做法（部件身份来自 RSUV，整次 draw 统一），于是"每个样本属于哪个部件"由**硬件的逐样本深度测试**决出，而不是靠我们算；
- 反过来，任何"把 ID 插值到逐样本"的想法都是错的（插值出来的 ID 不是任何部件的 ID）。
官方文档里"the runtime performs an AND operation of this mask with the typical sample coverage for the pixel in the primitive"与"in multisampling, the runtime shares only one coverage for all RenderTargets"也说明：若将来用 alpha-to-coverage / `SV_Coverage` 做 per-sample 写入（决策 6 的后手），**一张覆盖掩码会同时作用于该 pass 的所有 MRT**——ID pass 的 ID 与覆盖率两张图会一起被切，不能只切一张。

有了真实覆盖率，消费端不再需要"猜"边界，也不再需要 `HoCharacterSemanticMaskBlur` 那类补偿。ID pass 的绘制范围见决策 12。

### 5.5 容量与边界（能表达什么、不能表达什么）

设 ID pass 用 Nx MSAA：一个像素的真实内容是**长度为 N 的 ID 多重集**（4x 下如 `{发, 发, 眼, 脸}`）。缓冲把它压成 K 组 `(id_i, 覆盖率_i)`，且**不归一化**：

- **K ≥ 该像素内不同 ID 的个数 ⟺ 无损**；最坏情况 N 个样本全不同 → **K = N 是无损的充要条件**。K < N 时前 K 名（按样本数）**精确**、尾部**总质量**也精确，**丢掉的只是尾部身份**。
- **归一化必须显式禁止** —— 它正是把"这里还有第三个东西"抹掉的动作；不归一化时 `1 − Σcov` 天然就是残差。
- **`K = N` 让残差语义唯一**：`1 − Σcov` 精确等于背景占比，可直接当"角色占比"用。K < N 时残差里"背景"与"没装下的第三个东西"混在一起、无法区分。
- **背景占名额**：`{发, 眼, 脸, 空}` 就是 4 个不同 ID 用满、残差 0.25 = 背景占比；只要残差 > 0 就说明有样本落在背景上、它已占名额，故能存下的"部件"最多 N−1 个。4x 下"4 个部件 + 背景"不可能（几何上可以真有 5 个面穿过该像素，但证据只有 4 份；没被任何样本命中的区域在数据里不存在 —— 比采样间隔更细的发丝会整个消失）。
- "一张覆盖率只能描述两者叠加"应改为：**它能描述任意 K 个东西的叠加，代价是丢掉第 K+1 个的身份**。"2"看着像边界，是因为它额外假设"一像素最多两个面"——这在**前发 + 眼睛 + 脸**上不成立：`{发 1, 眼 1, 脸 2}` 按质量取前 2 会把眼挤掉，眼透抗锯齿随之坏掉。K=4 让"该保留谁"这个判断彻底消失。
- **K = 4 且 N ≤ 4 ⇒ 尾部丢失在实际配置里不存在**：4 个样本最多含 4 个不同 ID，正好装得下。所以"层里找不到我的 ID"就等价于"我这部件没覆盖这个像素"（覆盖率 0），消费端不需要为尾部做特殊处理。§5.5 的容量分析是**给未来改动看的**（降 K 或把 N 抬到 8 才会重新变成约束）。
- 写入端约定：排序与 tie-break 只由样本计数决定（相同则取更近的样本）；**覆盖率不归一化**。滤波只允许一种形式——**按 ID 匹配加权**：`cov(id, x) = Σ_taps w_t · [id_t == id] · cov_t`，即每个 tap 只在它的某一层 ID 等于目标 ID 时才贡献。裸双线性 / 按层序插值是错的：4 个层槽是**逐像素排序**的结果，邻居的层0 可能完全是另一个部件（这条与 `LILTOON_CHANNEL_CONTRACT_V1.md` 里"`maskcoverage` 逐通道独立滤波、不串道"同源）。
- 三条与层数无关的边界：
  1. **覆盖率说"多少"，不说"形状"** —— 斜边、角、比采样间隔更细的发丝都给不出来；这是 MSAA 本身的天花板（更高采样 / 超采样提高精度，TAA 的 jitter 跨帧累积把它变成亚像素精度）。
  2. **ID 说"谁"、绝不可过滤；覆盖率说"多少"、只能按 ID 匹配加权** —— "点采样 ID + 覆盖率"不是妥协，而是这两个量的定义决定的唯一合法组合。
  3. **残差必须显式保留**，消费者要能知道 `Σcov < 1` 意味着"这像素没被解释完"。GeometryBuffer 的 resolve 已经是这个思想的一维版（`.r` = 总覆盖、`.g` = 被解析面覆盖，`1 − .g` 就是它没解释完的部分），这里把它推广成 K 层。

### 5.6 拓展性与硬上限

**语义 / 部件 / 属性可以随意扩张**：palette 是按 ID 索引的表，**没有 AA 概念、不会被插值**，所以标签、枚举、任意属性放进去都安全；加语义 = 加标签位或枚举值，**不动 per-pixel 布局、不动契约编码、不动身份通路**。8 个 bit 的问题不在于 bit，而在于它被存在了每像素；RSUV 的问题不在于 32 bit，而在于它被当成了属性容器。

| 轴 | 上限 |
| --- | --- |
| 语义 / 区域 / 部件数量 | 实际无限（受 palette 行数与 ID 位宽约束） |
| 每部件属性 | 实际无限（代价是每帧上传量，决策 11） |
| **per-pixel 材质通道** | 管线族**固定 4 个**（决策 14）+ **选择层 2~4 个**（决策 19），都不是无限；管线通道不够时按需再加图 |
| 选择数量 | 注册上限 256，**每像素 2 个**为默认（可到 4）；再多的正确解法是换表达（§5.11） |
| 每像素层数 | **固定 K = 4** |
| MSAA 采样数 | **N ≤ K = 4**（由 feature 设置决定，默认 4、可降到 2；**不跟随相机 MSAA**）；N > K 时尾部身份会有损（所以封顶 4x） |
| 每像素覆盖率精度 | **1/N**（4x → 0.25，8x → 0.125） |
| 每像素形状信息 | **不存在** |
| 角色数 × 部件数 | 8+8 bit → 256 × 256 编码空间；真正常驻的是 palette 的 ≤256 角色行 + ≤4096 部件行（两级表，§5.3） |
| ID 传输 | RSUV 不干扰合批（一次画完）；兜底路径才是一部件一 draw |
| 跨帧 | **ID 必须稳定**，否则用 ID 判断历史有效性的时序效果会抖 |

### 5.7 组件（`HoCharacterBufferGroup`，形态沿用）

- 区域从**固定 8 个 bit** 升级成**最多 4096 条命名条目**：名称（唯一，AOV manifest 用）、**类别**（枚举，单值表示"这是什么"）、**标签位掩码（32 位）**、**显示色**。
- 渲染器归属到条目（与今天"给渲染器打勾"同一交互）；保留角色 ID 字段；**面部朝向（`faceBone` + 三轴）与 `TryGetWorldFacing()` 一并沿用**（眼透相机角度修正、将来的 SDF 读它，调用方式与旧组件同形）。
- **`HoMetadataBufferSubject` 的"高级覆盖并进同一条目"这条撤回**：那些字段是**材质数值**，组件再存一份就是两个来源（§5.3 的 P2 闸门）。Subject 的覆盖语义要么落回材质侧，要么等 §5.3 定下"CPU 从材质读一次进 palette"之后再作为**显式覆盖**引入——那时需要一条"本条目覆盖材质值"的标示，不能默认覆盖。
- 组件同时是**运行时映射的所有者**：维护"渲染器 → 条目"并把条目索引写进 RSUV（不再写 MPB）；兜底路径所需的全局常量值也由它提供。
- **标签保留的理由**：像 `CharacterFull`（= 该角色任意部件）这种"多归属"语义用标签最自然，消费端一次 `&` 即可查询；**"整角色"级语义放角色表那一行**（§5.3 的两级表），不必在每个部件行重复。
- **校验**：一个 renderer 只属一个条目；名称唯一；palette ≤ 4096 且越界告警；renderer 类型是否支持 RSUV（不支持则走兜底路径并提示）；**ID 跨帧稳定**；**RSUV 未序列化 → 每次 OnEnable / 部件表变化都要重写**（§5.2），并保留 palette 第 0 行 = unknown。
- **重复指定必须吵（不是静默取先到者）**：拖父级 + 展开子级之后，子树里某个 Renderer 很可能已经被别的条目（甚至别的角色的 group）显式指定过。裁决顺序固定为 **优先级 → 层级距离 → 条目顺序**，**每一处重复都记进 `HoCharacterBufferGroup.GetConflicts()`**：控制台按数量变化告警一次，组件 Inspector 里逐条列出（"xxx 同时属于「角色 2·头发」和「本组·前发」——对方优先级更高"），命中的部件条上还会带 `⚠ 重复 n`。

### 5.8 消费端迁移映射

| 消费端 | 新读法 |
| --- | --- |
| 角色特化 composite | `palette[char][slot].category / .tags`；同角色 = 高字节比较；覆盖率 = `_Coverage` 各层 |
| 角色特化 脸色扩散 | 同上 + `_Surface.rgb`（**覆盖率读 `_Coverage`，不再用 `_Surface.a`**） |
| 角色特化 轮廓场 | 类别 / 标签查询 + 覆盖率 |
| 材质侧捕获 | 从 **RSUV** 解出索引 → 查 palette 取 slot / character / category / tags（不读 MPB） |
| ScreenProcess 规则 | palette 字段枚举 + **选择**（决策 10 / 19，§5.11） |
| SSS | `_Coverage` + `_Surface` + **GeometryBuffer normalDepth**（几何门控不变）+ thickness / curvature / class / transmittance——**这几项读哪里取决于 §5.3 的 P2 闸门**（材质侧逐像素写，或 CPU 从材质读进 palette）；定下来之前不要迁 |
| PlanarReflection | `_Coverage` + palette（反射参数）+ `_Surface` + **GeometryBuffer normalDepth/coverage**（几何门控不变） |
| 调试 / AOV | ID / 覆盖率分层显示；palette `displayColor`；manifest JSON |

### 5.9 与 GeometryBuffer 的对齐（同一类问题的两份实现）

两者都是"**主动重绘一层屏幕空间事实 + MSAA resolve + 发布全局纹理**"，所以必须逐条对齐；`Documentation~/GeometryBuffer.md` 是 GB 的公共契约，CB 落地后应当有同体例的契约。**本节的 GB 结论一律以代码为准**：该契约文档已复核出若干与代码不符之处，见 ⑦。

**① 逐条对齐清单**（决策 7 的细化）

| 点 | GB 的做法 | CB 照抄 |
| --- | --- | --- |
| 采样数协商 | 跟随**相机**：`GetSupportedMsaaSampleCount(cameraDescriptor, …)` 在相机 `msaaSamples <= 1` 时直接返回 1，否则与 `SystemInfo.GetRenderTextureSupportedMSAASampleCount`（color 与 depth 取 min）取小 | **结构同、来源不同**：采样数来自 feature 自己的设置（默认 **4**），**不读相机**——覆盖率是产品功能，不是"跟随帧的 AA"；只看平台能力 |
| resolve | `HoGeometryBufferResolve.shader` + `_HO_GEOMETRY_BUFFER_MSAA_2/4/8` 多关键帧 + `Texture2DMS` 逐样本读 | 同关键词模式，换归约逻辑 |
| 两条路径 | RG：`UniversalRenderer.CreateRenderGraphTexture` + `SetRenderAttachmentDepth`；兼容：`RTHandle` + `ConfigureTarget`/`SetRenderTarget` | 同构 |
| 发布与复位 | `SetGlobalTextureAfterPass` + `_HoGeometryBufferValid`；禁用/不支持/结束时复位为 black texture | 同（含 `_Valid`） |
| 分配策略 | **GB 实际是无条件分配**：`ReAllocateIfNeeded` 恒分配 `normalDepth + depth + outlineNormalDepth`；MSAA 额外件（两张 MSAA color、MSAA depth、两张 R8 coverage）只在 `samples > 1` 时分配、否则 `ReleaseMsaaResolveResources()`；真正按需的只有 sky（`enableSkyBuffer`）。GB 文档 §11.4 的"按视觉消费者懒分配"是**推荐路线、尚未实现** | **CB 不照抄，要比它干净**：没有消费者时 ID pass 不跑；`Material0/1` 只在真的有字段被读时分配；不为描边/天空这类共用语义各开一张图 |
| Debug | DebugView + DebugTile 注册 view id（`geometry.*`），每个通道必须有视图 | 同：`character.id0/id1` / `character.coverage.*` / `character.surface` / `character.material0` / **`character.selection.*`（含溢出提示）** / `character.palette.*` |

**② 必须一致的地方（否则产生跨来源错位）**

- **renderScale 与 GeometryBuffer 一致**：消费者会把 CB 的覆盖率与 GB 的覆盖率相乘（例如 PLR 的 `surfaceMask` 链），分辨率不同就会在轮廓处错位。**v1 干脆不暴露 renderScale、固定 Full**——比"两处设置记得同步"可靠；将来真要为性能降分辨率，必须两者一起降并重新验证轮廓链路。
- **发布/复位纪律一致**：camera begin 复位全局与 `_Valid`，生产后发布，禁用/不支持时回退 black——避免读到上一帧的陈旧资源。
- **同一帧内都在消费者之前**：CB 与 GB、MetadataBuffer 同在 `BeforeRenderingOpaques` 槽位，彼此**无硬依赖**（CB 不读 GB 的产物，GB 不读 CB 的），只要求都先于消费者。
- **DebugTile 每个通道都要有视图**（GB 文档明确把"没有视图"当作成本不可验证的风险）。

**③ 刻意不同的地方**

| 维度 | GeometryBuffer | CharacterBuffer |
| --- | --- | --- |
| MSAA 触发条件 | 跟随相机 MSAA；相机 AA 关 → coverage 退化成 0/1（GB 的覆盖率只是"给效果做引导"，这没问题） | **与相机解耦、常开 4x**：相机 AA 关掉时覆盖率仍是 4x 亚像素精度（这正是原始 bug 的场景）；代价见 ⑥ |
| 画什么 | 全场景真实几何 + 描边壳 + sky | **只画角色部件**（layerMask / renderQueue 独立过滤），不含描边 |
| 覆盖率语义 | `NormalDepth.a` 派生"有没有真实几何"；MSAA 下另有 R8 coverage（resolve 的 `SV_Target1`：R=总覆盖、G=被解析面占有率）；**G 不按身份分组**——`HoGeometryBufferResolve.shader` 用"法线夹角小（`step(0.9, dot)`）+ 深度落在 0.2% 带内"判同组，作者注释明确"宽松方向的错误只会少报占比" | **每个 ID 的占比**（4 层），按 per-sample ID **精确分组**，直接回答"我这个部件占多少" |
| resolve 运算 | 挑最近样本得到该像素几何 + 写覆盖率 | 逐样本数票 + 取前 4 个 ID 及占比 |
| 深度 | 有独立 `DepthTexture`（自己 ZTest + 描边可见性）并**发布**它，而且**真被采样**：GTAO 在 `HoGTAO.shader` 点采样它的 `.r`（原始设备深度 / reversed-Z），再 `LinearEyeDepth(raw, _ZBufferParams)` 转线性。GB 文档 §3.2"不应被当线性深度语义采样"只对了一半——实际存在**两套深度语义**（`DepthTexture.r` = 原始设备深度、`NormalDepth.a` = 线性 eye depth），混用即错 | 同样有内部 depth-stencil 附件，但**根本不发布**（更干净）：CB 不制造第二套深度语义 |
| sky | 有 SkyTexture（背景 radiance/贡献） | 不需要：未写入的像素就是 ID 0 = 背景 |
| 描边 | 独立 `OutlineNormalDepth`（物理/视觉分离，AO/GI 不得当真实表面） | **描边不写 ID**（决策 18），需要"连描边一起算角色"时用 GB 的 outline coverage 补 |

**④ 跨来源总规则（与 §6 禁令同源）**

- **同义量不得相乘、也不得比较**：问"这个像素被几何覆盖多少 / 最近面占多少"→ 读 GB；问"我这个部件占多少"→ 读 CB。**一次只用一个来源**。
- 深度只有一个来源（GB，决策 16），CB 的内部附件永不参与消费者运算。
- 已知例外只有眼透的 `hairInFront`（§5.1 规则 3）。

**⑤ MSAA 瞬态成本的修正（结合 GB 的格式选择后发现的）**

MSAA 阶段**每个样本只需要一个 ID**（一个样本只属于一个部件），所以 ID 的 MSAA 颜色目标只需 **16 bit/样本**，即 `R16_UInt` + 4x = **8 B/px 瞬态**——而不是把 4 层 `(id, coverage)` 格式直接开 MSAA（那会是 48 B/px）。resolve 时读 4 个样本的 ID 数票，写出 4 层结果（决策 17）。

**`_Surface` 不进 ID pass 的 MRT**（这条纠正了本文件上一版的错误判断）：ID pass 开 4x MSAA 且按决策 6 对**所有**部件 `ZWrite On`（透明件也占满网格面积），而 `_Surface` 要沿用的是**两段式深度策略**（opaque/cutout 写深度、transparent 只 ZTest 后叠加）——两者是互相冲突的深度策略，塞进同一个 pass 里必然有一方变味；混合 MSAA / 非 MSAA 附件本身也不合法。所以维持旧结构：**`_Surface` 走自己的单采样 pass**（就是今天 `fragMetadataBufferSurfaceColor` 那一趟，`ReAllocateIfNeeded` 里注释写得很清楚："Metadata is sampled as a regular texture"），ID pass 只写 ID + 覆盖率。落到 lilToon 侧就是两个 LightMode：今天 `HoMetadataBuffer` / `HoMetadataBufferSurfaceColor` → 新 feature 的 `HoCharacterBuffer` / `HoCharacterBufferSurface`。

| MSAA 瞬态（4x） | 构成 | B/px | 1080p |
| --- | --- | ---: | ---: |
| CB | ID 颜色 2·4 + MSAA depth 4·4 | 24 | 47.4 MiB |
| GeometryBuffer | 两张 16F color (8+8)·4 + depth 4·4 | 80 | 158.2 MiB |

瞬态只有 GB 的 30%，因为**每样本只需要一个 ID**（决策 17）。而且 resolve 依然**不做平均**：选层0 样本（样本数最多，平票取更近）后，用它同时写出 `_Id0` 的身份与层0 覆盖率——身份与"多少"来自同一份证据。

**⑥ 常驻成本对照**（全分辨率、无 MSAA 乘数，1920×1080）

| 资源 | 约 B/px | 1080p 近似 |
| --- | ---: | ---: |
| 现状 MetadataBuffer 常驻（7 张颜色：`maskId` RGBA8 = 4 + 六张 16F = 48） | 52 | 102.8 MiB |
| 现状的 depth-stencil 附件 ×2（`depth` + `mBufferDepth`，**原表漏了**） | 8 | 15.8 MiB |
| CB 常驻（ID 3×RGBA8 = 12 + `_Surface` 16F = 8） | 20 | 39.5 MiB |
| CB 的 depth-stencil 附件 ×1（单采样，**原表漏了**） | 4 | 7.9 MiB |
| CB 按需（`Material0` RGBA8 + 选择层 RGBA8×1） | +8 | +15.8 MiB |
| CB 瞬态（ID pass 4x：ID 颜色 8 + MSAA depth 16 [+ 选择层 4×4 = 16]） | 24 / 40 | 47.4 / 79.0 MiB |

即：**常驻 24 : 60 = 40%**（只算颜色图是 20 : 52 ≈ 38%，两边都补上 depth-stencil 附件后是 40%——原表两边都漏了深度附件）；**按需通道全开也只有 32 : 60**（`Material0` + 2 个选择层）；瞬态只在 ID pass 期间存在（4x 时 CB 24 B/px、开了选择层 40 B/px；GB 80 B/px，见 ⑤）。

**注意这笔瞬态是常付的**：因为 CB 的 MSAA 与相机解耦（决策 7），相机 AA 关掉时 CB 仍然跑 4x——这正是覆盖率可用的前提。内存吃紧时唯一的旋钮是把 N 降到 2（覆盖率量子 0.5、瞬态减半），而不是跟随相机。

**⑦ GB 文档逐条复核（读代码得出；冲突处以代码为准）**

> 复核完成后 `Documentation~/GeometryBuffer.md` 已按代码修正（覆盖率来源、DepthTexture 的两套语义、生产顺序、RG 资源清单、成本表、§11.4 的实现状态）。本表保留为复核记录，读到与现行 GB 文档不一致的地方，一律以本表为准。

| `Documentation~/GeometryBuffer.md` 的说法 | 代码事实 |
| --- | --- |
| §3.1「Coverage 不是额外的第五通道，而是从 alpha 派生」，采样函数只列 4 个 | 有独立 coverage RT：`_HoGeometryBufferCoverageTexture` / `_HoGeometryBufferOutlineCoverageTexture`（R8，`CreateCoverageDescriptor` → `GetCoverageGraphicsFormat()`，**只在 MSAA resolve 时创建**），配 `_HoGeometryBufferCoverageTextureValid` / `...OutlineCoverageTextureValid` 门控。`HoGeometryBufferSampling.hlsl` 里还有三个没登记进文档的函数：`LilHoGeometryBufferCoverageAt(uv, nd)`（MSAA 下优先读 coverage RT 的 `.r`）、`LilHoGeometryBufferOutlineCoverageAt()`、`LilHoGeometryBufferEncodedNormalOrBlack()` |
| §3.1 说 coverage = `step(0.0001, a)` | 对（alpha 路径），但 MSAA 下 `.a` 是"被选中的那个最近样本的深度"，与非 MSAA 的二值 alpha 不是同一种分布——这正是 `...CoverageAt()` 存在的理由 |
| §3.2「DepthTexture 不应该被当作可以直接采样的线性深度语义」 | 方向对、表述错。GTAO **直接点采样** `_HoGeometryBufferDepthTexture.r`（原始设备深度）再 `LinearEyeDepth(raw, _ZBufferParams)`；C# 侧四处 `SetGlobalTexture(GeometryDepthInputId, …)`、两处当只读 depth attachment（`SetRenderAttachmentDepth(..., AccessFlags.Read)`）。真实风险是**两套深度语义并存**（raw vs 线性），不是"不能采样" |
| §2 生产顺序 | 漏了三件事：output pass 内部的 MSAA resolve 子 pass（RG 是独立 `AddRasterRenderPass`："Ho-GeometryBuffer MSAA Resolve" / "Outline MSAA Resolve"；兼容路径是 `ResolveGeometryBuffer()` / `ResolveOutlineNormalDepth()`）、resolve 后的 coverage-valid 全局发布、以及默认 `AfterRenderingPostProcessing` 的 Debug pass |
| §5「RenderGraph 每帧暴露 4 个 texture」 | `HoGeometryBufferRenderGraphResources` 实为 **6 个**：另有 `coverageTexture` / `outlineCoverageTexture`；且 `HasRequiredTextures` 只要求 `normalDepthTexture` —— "必需/可选"的分法与文档暗示的不同 |
| §11.3 成本表 | 没量化 MSAA 阶段（两张 MSAA color + MSAA depth + 两张 R8 coverage），而 CB 的规划正依赖这部分；`SkyTexture` 一行也没标"仅 `enableSkyBuffer` 时分配" |
| §11.4「后续可以根据视觉消费者登记做懒分配」 | 表述诚实（代码确实还没做），但**读的时候容易当成已实现**：除 sky 外全部无条件分配 |
| §6 view 列表 / §7 消费者表 | §6 的 8 个 `geometry.*` view id 与 `HoGeometryBufferDebugViewInfo.cs` 完全一致 ✓。§7 两处不准：GTAO 一行没写它真正依赖的 `DepthTexture`；SSGI 一行没写它的实际机制——`HoSSGI.shader` 里**根本没有** `_HoGeometryBufferNormalDepthTexture`，SSGI 是在 C# 里取 `geometry.normalDepthTexture` 句柄后绑成自己的 `_HoSSGIGeometry`，并用它建 `_HoSSGIDepthPyramid0-4`（这就是"消费者取句柄、用自己名字绑定"的现成范式，CB 照这个走） |
| §3.3 / §4 关于 `CustomShaderResources/URP/Default*Outline.lilblock` 的改动指引 | 本仓库内**不存在任何 `.lilblock`**：这个 workspace 只有 URP Extensions 包，lilToon 的模板/pass 生成在另一个仓库。凡"改 lilToon 模板"的步骤都不是本仓库能独立完成的（登记为跨仓库依赖） |

### 5.10 契约与 AOV

- 按 `Documentation~/架构优化/LILTOON_CHANNEL_CONTRACT_V1.md` §3 的流程登记（登记本行 → 生产端输出 → 消费端消费 → debug 可见 → 冻结）：`character.idcoverage`（含 `_Id0` / `_Id1` / `_Coverage` 三张；debug view 按张拆开命名）/ `character.surface` / `character.material`（管线 `Material0`）/ **`character.selection`（选择层，§5.11）** / `character.palette`。**不登记任何深度 / 法线通道**——全管线唯一的深度/法线入口是 GeometryBuffer（决策 16）。
- **同时要改的既有行**（契约 v1 里那些 MetadataBuffer 时代的条目，现在就该在文档里标上"待 P4 替换"）：`surfaceColor`（**去掉 `A=coverage`**——`.a` 退役，覆盖率只有一个来源）、`maskId` / `objectCustom0/1` / `surfaceData` / `reflectionMaterial`（生产者与编码整体换成 CharacterBuffer 的对应图）、以及 `maskcoverage.low/high`（它是 `objectCustom` 的抗锯齿副本，随 `objectCustom` 一起退役）。另外记一个现成的教训：**`custom0` 这张图在契约里根本没有登记**——它只在 §1 的备注里被点了一次名（"不要把逐通道滤波套到 … `custom0` … 上"），因为匿名通道没人愿意为它写契约行。这正是 §5.11 要改掉的模式。
- AOV 导出对齐 Nuke 习惯：**名字冻结不动**（契约 §2 的 `id_object` / `id_group` / `matte_*` / `diffuse_albedo` 只换生产端与编码，不改名）；`matte_*` = **`Σ_i cov_i · [cat(id_i) == X]`**（不是只取层0——与 §6 第 7 条同一条规则）；**ID 不过滤、coverage 放 alpha**，附 palette manifest（JSON）——**默认嵌进 EXR metadata，sidecar 只作兜底**（§4.2：OpenEXR 库不支持 sidecar，Blender 的 Cryptomatte 节点干脆不读），Nuke 端即可像 Cryptomatte 那样点选。**选择层另加 `matte_sel_<名字>`**（= `Σ_i selCov_i · [selId_i == S]`，§5.11）；**导出分两档**：① Deep IDs 风格的 UINT 通道；② **规范合规的 Cryptomatte `crypto_*` 层**（float 位重解释 + manifest + 32 bit）——只有第②档才在 AOV 里用 Cryptomatte 命名。manifest 里**同时列部件名与选择名**。
- CB 落地后，`Documentation~/GeometryBuffer.md` §7 消费者契约表里仍写着 "MetadataBuffer" 的三行（CharacterSpecialization / PLR / SSS）要改成 CharacterBuffer，并在 GB 文档的公共原则里把 "MetadataBuffer = object/material semantic truth" 拆成 "CharacterBuffer = 部件身份 + 覆盖率真值 / MetadataBuffer 退役"。这一步记在 P4。

### 5.11 选择层：Cryptomatte 式的"用户自定义"，取代 `custom0~3`（决策 19）

**为什么改**：`custom0~3` 是四个**匿名**浮点通道。用它的艺术家三个月后不会记得 3 号通道是什么；消费端也只能写 `custom2 > 0.5` 这种没有信息量的条件。选择层把这件事变成**具名、可点选、有覆盖率**的对象——**名字本身就是文档**。

**命名（这里有个必须避开的坑）**

| 场景 | 叫什么 | 理由 |
| --- | --- | --- |
| **UI（用户可见）** | 就叫「**Cryptomatte**」（组件里那一节标题、feature 那一节标题都用它） | 用户一眼看懂它是干什么的；"选择/Selection"这种自造词反而要解释半天 |
| 内部 RT / 代码 | **选择层 / Selection**，`_HoCharacterBufferSelection` | 我们的编码**不是** Cryptomatte 规范格式（整数 ID、无 float 位重解释），代码里不冒用规范名 |
| AOV 导出 | 提供**两档**：① 符合 Deep IDs 规范的 UINT 通道（`objectid` 等）；② **真正符合 Cryptomatte 规范的 `crypto_*` 层**（float 位重解释 ID + manifest + 32 bit） | Nuke 的原生 Cryptomatte 节点只认规范格式。**名字叫 Cryptomatte 但格式不符，比不给更糟**——所以导出档位要么合规、要么不叫这个名字 |

**数据模型：一个选择 = 一个 `(选择 ID, 覆盖率)` 对**

- **选择 ID 是独立的 8 bit 空间**（≤ 256 个具名选择），与部件的 16 bit ID 空间**无关**。不用 16 bit 是因为选择是**人工策展**的（几十个量级）。
- **成对装进同一张 RGBA8**：`R=ID0, G=cov0, B=ID1, A=cov1` —— 这正是 Cryptomatte 的"ID 与覆盖率必须成对、层数必须成偶数"的布局（§4.1）。一块内存 = 2 个选择 = 4 B/px。
- **覆盖率是线性量**：来自材质侧的作者遮罩（贴图通道 / 顶点色 / 常量）× 该样本是否被几何覆盖。

**为什么必须挂在 ID pass 上（而不是像 `_Surface` 那样独立一趟）**

一个样本属于哪个部件，是**硬件的逐样本深度测试**决定的；而"这个样本落在哪个选择里"是**同一个片元的属性**。只有放在同一个 pass、同一份样本证据里，**选择与部件才是配对的**。若放到独立的单采样 pass，边界像素上"选中的部件"与"选中的选择"会来自不同采样位置，就会出现"**脸的腮红选择配上了头发的覆盖率**"这类错配——正是本文件反复禁止的静默错误。

代价：常驻 **+4 B/px**（2 个选择；4 个则 +8）、MSAA 瞬态 **+4 B/px 每样本**（4x → +16）；resolve 复用同一套"按 ID 数票 + 覆盖率求和"。

**不变式（宪法在选择层上的具体形态）**

1. **选择 ID**：点采样、`round(v*255)` 还原整数、不平均、不插值、不与其它量相乘；
2. **选择覆盖率**：线性量，**可以**与别的覆盖率相乘（如乘部件覆盖率），但只能**按 ID 匹配加权**求和（§5.5）；
3. **作者 alpha 不得被阈值化**后再写进来（想硬边就在消费端显式选，写入端不许丢信息）；
4. **选择 ID 跨帧稳定**，否则用选择判断历史有效性的时序效果会抖；
5. 选择表和 palette 一样：**像素里只有索引，属性永远在表里**。

**跨仓协议（Extensions 侧 ↔ lilToon 侧）**

| 侧 | 提供什么 | 约定 |
| --- | --- | --- |
| **本仓库（URP Extensions）** | ① `HoCharacterBufferGroup` 增加**选择表**：`{ 名字（唯一）, 选择 ID（自动分配 0~255）, 显示色, 可选类别/标签 }`；② 选择定义的**单一真值**（改名不破资产，因为材质只引用名字）；③ `_HoCharacterBufferSelection` 的分配 / clear / 发布 / debug 视图 / AOV+manifest 导出；④ ScreenProcess 规则 source 增加"选择"一族；⑤ 把"名字 → ID"解析好写进全局常量，材质侧**不需要查表**就能写 | 注册、校验（唯一名、≤256、越界告警）、ID 分配**只在本侧发生** |
| **lilToon 包（跨仓）** | ① 材质 UI 增加 N 个**选择槽**（N = 2 或 4，来自组设置），每槽 = `{ 启用, 选择（下拉引用组里的名字）, 遮罩来源, 强度 }`；② shader 在 `HoCharacterBuffer` pass 里按上面的成对布局写 MRT；③ 槽未启用时写 0（= 无选择） | **材质只引用名字、不定义名字**；MRT 数量由"是否存在选择"和"槽数"决定（见下） |
| **遮罩来源枚举**（协议里必须冻结的部分） | `贴图 R/G/B/A`、`顶点色 R/G/B/A`、`自定义贴图1 R/G/B/A`、`常量 0~1`、`无` | 加来源 = 加枚举值，**不动通道布局、不动选择 ID 空间** |
| **变体/关键字** | 每个选择槽一对启用关键字，或直接由"槽数"决定 `#pragma` 的 MRT 数量 | **MRT 数量必须按实际需要来**：没有选择时那张图根本不分配、也不输出（否则白付带宽） |

**"加几个比较好"的答案**

- **默认 2 个**（一张 RGBA8，4 B/px）；不够就**加图不加通道**：第二张 = 4 个/像素（+4 B/px）；**再加不建议**。
- 判断标准不是"我要注册多少个选择"（可以注册到 256），而是"**同一像素里最多同时出现几个独立遮罩**"：互斥区域型的选择 1 个就够；2 个覆盖绝大多数真实用法；4 个基本到顶。
- 超过 4 个的正确解法是**换表达**：把这块拆成独立 renderer（用部件 + 标签表达，不占选择层），而不是继续加图。
- **必须有溢出可见性**：resolve 时若有选择被丢掉，debug 视图要能标出来。否则就会重演 Cryptomatte 那条"选到被丢弃的对象时出噪声"的静默失败（§4.1）。

**与旧资产的迁移**：不兼容（决策 4）。ScreenProcess 里读 `custom*` 的规则条目改成读"选择"（§5.8），契约里 `custom0` 那一行标成待替换（§5.10）。

### 5.12 `Ho-SurfaceBuffer`：表面/材质数值的新家（决策 20）

**为什么拆**：MetadataBuffer 原本一个 buffer 承担了三种语义——身份（哪些是角色/脸/前发）、覆盖率、以及**表面数值**（roughness / metallic / reflectance / thickness / curvature / materialClass / transmittance / 线性表面色）。这次只把前两者搬进 CB；后一组**不是"这是谁"，而是"表面是什么样"**，混在 CB 里会重演老毛病：加一个材质参数要动身份 buffer 的布局与契约，两边互相拖累。

**三分边界（各管一段，互不越界）**

| feature | 回答什么 | 输出 |
| --- | --- | --- |
| **GB**（GeometryBuffer） | 几何在哪、朝向如何、几何覆盖多少 | `NormalDepth` / `DepthTexture` / `CoverageTexture` / outline / sky |
| **CB**（Ho-CharacterBuffer） | **这是谁、占多少** | `Id0` / `Id1` / `Coverage`（4 层 `(ID, 覆盖率)`）+ Cryptomatte 选择层 |
| **SB**（Ho-SurfaceBuffer） | **表面是什么样** | 线性表面色 + 管线/材质逐像素数值（下表） |

⚠ **命名要避开 GB 文档里的 `VisualSurfaceBuffer`**（GB §11 那套是"描边视觉壳层"，属**几何轴**的分层）。SB 是**材质轴**，两者不是一回事；文档与 RT 名都不要复用 "VisualSurface / 视觉表面" 这套词。

**草案：通道**（写入端 = 材质，即"谁的值谁写"）

| 通道 | 格式 | 内容 | 来源 |
| --- | --- | --- | --- |
| `_HoSurfaceBufferColor` | RGBA16F | RGB = 线性 HDR 表面色（今天 `surfaceColor.rgb`）、A 保留 | 材质（逐像素） |
| `_HoSurfaceBufferMaterial` | RGBA8 | r = roughness、g = metallic、b = thickness、a = 备用 | 材质（逐像素） |
| `_HoSurfaceBufferReflection` | RGBA8 | r = reflectance、g = PLR strength、b/a 备用（今天 `reflectionMaterial` 的降精度版） | 材质（逐像素） |
| `_HoSurfaceBufferClassification` | RGBA8 | materialClass / curvature / transmittance / 备用 | 材质（逐像素，逐部件常量也照写） |

- **深度策略**：SB 自己一趟单采样 pass，沿用**两段式**（opaque/cutout 写深度、transparent 只 ZTest 后叠加）——决策 15 的内容搬到这里。
- **格式选择**：管线那几项都是 0~1 归一量，8 bit 够；表面色是线性 HDR，必须 16F。**逐部件常量也照写**：不做"常量进 palette"的优化，因为那需要 CPU 知道材质值（要么重复一份、要么走跨仓属性名协议），而 4 张图里丢掉的是 8 bit×3 的带宽、换到的是**一个明确的写入端**。
- **不变式**：这里全是**线性量**（可乘、可 lerp、可滤波），**没有任何身份**——所以它不背 §6 的 ID 禁令，只有"别把常量当 per-pixel 存"这条老账要还。反过来，**CB 的 ID 也绝不允许出现在 SB 里**。
- **与 CB 的关系**：**互不依赖**。SB 不读 ID 层，CB 不读 SB；需要"某个部件的表面值"的消费端**自己在一次读取里同时拿两边的覆盖率与数值**（并按 §6 第 7 条加权），而不是让一边去乘另一边的图。
- **成本**：4 张图按需分配。只在有消费者时开：`Color`(8 B/px) 给角色特化与 AOV；`Material`(4) 给 SSS / PLR；`Reflection`(4) 给 PLR / SSR；`Classification`(4) 给 SSS。**全开 20 B/px**，而现状 MetadataBuffer 的相关部分是 `surfaceData`(8) + `custom0`(8) + `reflectionMaterial`(8) + `surfaceColor`(8) = **32 B/px** —— 拆出来之后反而更省，因为大量项目只用得到其中两张。
- **退役映射**：`surfaceData` → `Classification` + `Material.b`；`reflectionMaterial` → `Reflection`；`surfaceColor` → `Color`；`custom0~3` → **Cryptomatte 选择层（CB）**，不是 SB。

**未定项（实施 SB 时先定）**：①名字用 `Ho-SurfaceBuffer` 还是 `Ho-MaterialBuffer`（避开 GB 的 VisualSurface 用词）；②`Classification` 是否真需要独立一张（materialClass 也许该走 palette——它是**分类**不是数值，这条要单独判）；③PLR strength 与 reflectance 是否合并进 `Material` 的备用通道（图更少但要重新排布）。

---

## 6. 非线性 AA 禁令（本次改造的"宪法"）

**允许线性作用**：`coverage`（乘、lerp、**按 ID 匹配的加权求和**，见 §5.5）、颜色（`_Surface.rgb`）、`custom` 里明确是连续量的通道。

**禁止**：

1. **ID**：不得双线性/三线性过滤、不得平均、不得在插值后比较。读取一律 `sampler_PointClamp`；比较在原始值上（UNORM8 用 `round(v*255)` 还原整数再比）。
2. **深度**：同上；且注意 CB 自己不产出任何深度/法线信号——这里的"深度"指 GeometryBuffer 的产物，它也是全管线唯一的深度/法线入口（决策 16）。（这也是 GeometryBuffer 把法线/深度点采样、覆盖率单独出图的原因。）
3. **覆盖率**：不得 `step` / `round` / 阈值化成 0/1 再参与混合 —— 那等于把 AA 又丢了；只在乘完权重之后做"要不要硬边"的显式选择。
4. **不要把已阈值化的结果写进 buffer**：buffer 只存**身份**与**线性量**，判断留给消费端。
5. **不要把 per-part 常量塞进 per-pixel 通道**再指望它可过滤（现在的 `surfaceData` / `reflectionMaterial` 就是反例：class 被双线性插值会得到无意义的中间值）。
6. **不要依赖外部 AA**（相机 MSAA / FXAA / TAA）修这种信号；覆盖率自己产出（`语义掩码.md`）。**代码级含义：CB 的采样数不读相机的 `msaaSamples`（决策 7）**——"相机 AA 关掉就退化成硬边"正是这套东西要消灭的病。
7. **多表面必须按层加权**：`result = Σ coverage_i × f(id_i)`；只用前层会在"前发 + 后面的脸"这类像素上重新引入硬边。

**现存违规（迁移时修掉）**：`HoSubsurfaceScattering.shader` 的 `HoSSSSurfaceDataLinear` 双线性读 `surfaceData`，其中 `.b` 是 material class byte。ScreenProcess 的 byte 容差匹配本身不违规（ID 未被 AA），但迁移后要确认它读的仍是点采样 ID。

---

## 7. 迁移分期

| 阶段 | 内容 | 验收 |
| --- | --- | --- |
| **P0（本文件）** | 调查 + 设计 + 纪律 | 已完成 |
| **P1** | `HoCharacterBuffer*` feature（ID/coverage pass + palette 两级表 + 选择表 + 选择层 RT + 调试视图）；lilToon 侧新增 `HoCharacterBuffer` pass（写 ID/覆盖率），与 MetadataBuffer 并存，身份走 RSUV 索引 + palette 查询 | 无 AA / MSAA 开 / 后处理 AA 开三种情况下 ID 与覆盖率都正确（**特别验证：相机 MSAA 关时覆盖率仍是 4x，不是 0/1**）；部件与 palette ID 一一对应；ID pass 一次画完、不破坏合批 |
| **P1.5**（本次架构调整的收尾） | 把 `_Surface` 与 `Material0` **从 CB 摘干净**（RT、pass、发布、调试视图、设置项、契约行），CB 只剩身份与覆盖率 | CB 的发布清单里没有表面色/材质数值；debug 视图里没有 `character.surface` / `character.material0`；`_Surface` 的空缺由 MetadataBuffer 暂时继续提供（P4 之前不能让现有消费端断供） |
| **P2** | **`Ho-SurfaceBuffer`（§5.12 / 决策 20）**：新的 feature + 4 张按需通道 + 两段式深度策略；lilToon 侧对应 pass（跨仓，从今天的 `HoMetadataBufferSurfaceColor` / 材质写入端对应过来） | 表面数值由材质侧逐像素写；现有 consumer（SSS / PLR / 角色特化脸色扩散 / AOV）改读 SB 后行为不变；**与 CB 无交叉读取** |
| **P2.5** | **Cryptomatte 选择层的跨仓落地**：Extensions 侧给出选择表的 UI / 校验 / debug 视图 / 溢出可见性；**lilToon 侧（跨仓）实现材质的选择槽 UI 与 shader 写入**（§5.11 的协议） | 一个选择能被 ScreenProcess 规则按名字引用；边界像素上"选择"与"部件"配对正确；没注册选择时那张图不分配、SHADER 不输出；溢出时有 debug 提示 |
| **P2** | 角色特化三效果 + 轮廓切新 buffer，`HoCharacterSemanticMaskBlur` 退化为可选 | 前发投影边界连续、发际线无硬裁、角色互不干扰；等价 debug 视图无台阶 |
| **P3** | ScreenProcess 规则、SSS、PlanarReflection 切新 buffer | 屏幕效果行为不变或更好；SSS 不再双线性读 class |
| **P4** | 删除 MetadataBuffer（大量 16F 附件、fallback/clear/debug shader、MPB 回退、契约条目）；**把 `HoFaceAxis` 的归属迁到 CharacterBuffer**（眼下 CB 是借用 MetadataBuffer 命名空间里的这个枚举；枚举按 int 序列化、成员顺序不变就不会丢已有场景的值）；同步更新 `Documentation~/GeometryBuffer.md` §7 消费者契约表与公共原则（MetadataBuffer → CharacterBuffer） | 全仓库无 `_HoMetadataBuffer` 引用、无 MPB 身份写入；RSUV 只剩"palette 索引"一种含义；契约出 v2 |

跨仓库依赖（**两处，都要在 lilToon 包里做**）：① 新增/改 pass（`HoCharacterBuffer` / `HoCharacterBufferSurface`，只把 RSUV 当索引用，不再解析语义位）；② **选择槽的材质 UI + shader 写入**（§5.11 的协议：槽数、遮罩来源枚举、MRT 布局、启用关键字）。C# 侧 `HoCharacterBufferGroup` 为每个 renderer 分配 palette ID、写 RSUV、维护选择表，并登记 32 bit 的分区。

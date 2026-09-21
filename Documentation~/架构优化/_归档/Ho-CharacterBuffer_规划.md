# Ho-CharacterBuffer 规划（已归档）：用 ID + Coverage 取代 bit 位掩码

> **已归档（2026 文档审核）**：本文是被取代的规划稿，已压缩为「结论 + 坑 + 仍然有效的依据」。
> 它设想的整套机制（身份 / 覆盖率 / palette / RSUV / 自建 MSAA / resolve）最终落地成了 **`Ho-ObjectBuffer`（OB）+ `Ho-AttributeComposite`（AC）**，
> 表面数值另立 **`Ho-SurfaceBuffer`（SB）**；它曾经设想的独立 `Ho-Cryptomatte` 没有做，遮罩需求由 **AC** 承担；`HoMetadataBuffer` 已在 R6／R7 整块删除。
> 现行架构以 `架构优化/Ho-ObjectBuffer.md` / `Ho-SurfaceBuffer.md` / `Ho-AttributeComposite.md` / `Ho-管线总览.md` 与 `CHANGELOG.md` 为准。
> 代码落点：`Runtime/ObjectBuffer/`、`Runtime/AttributeComposite/`、`Runtime/SurfaceBuffer/`（本文里的 `Runtime/CharacterBuffer/` 是当时的设想）。

## 1. 一句话结论

`MetadataBuffer` 把 8 个语义压进 RSUV 低字节的 8 个 bit，语义只能是 0/1、**per-pixel 覆盖无处可存**，抗锯齿只能靠消费端滤波补救。

正确模型（业界标准）：**per-pixel 只存 `(ID, 覆盖率)`，其余一切按 ID 查表**。抗锯齿由真实覆盖率给出亚像素相位，角色隔离成为 ID 的天然属性，带宽同时下降。

**这不是我们独有的问题**：UE 官方文档在讲 Custom Depth 做轮廓时明说那类轮廓在 TAA 下“得不到抗锯齿”，并把原因归给 TAA 的逐帧亚像素抖动；HDRP 也只能给效果一张“逐像素可采样的 Rendering Layer Mask buffer”。业界的解法都是同一个方向——**把身份与覆盖率当一等数据产出，而不是让消费端去猜边界**。

## 2. 仍然有效的机制结论

### 2.1 容量：`K = N` 即无损

设 ID pass 用 Nx MSAA：一个像素的真实内容是**长度为 N 的 ID 多重集**（4x 下如 `{发, 发, 眼, 脸}`）。缓冲把它压成 K 组 `(id_i, 覆盖率_i)`，且**不归一化**：

- **K ≥ 该像素内不同 ID 的个数 ⟺ 无损**；最坏情况 N 个样本全不同 → **`K = N` 是无损的充要条件**。`K < N` 时前 K 名（按样本数）精确、尾部**总质量**也精确，**丢掉的只是尾部身份**。
- **归一化必须显式禁止**：它正是把“这里还有第三个东西”抹掉的动作；不归一化时 `1 − Σcov` 天然就是残差。
- **`K = N` 让残差语义唯一**：`1 − Σcov` 精确等于背景占比，可直接当“角色占比”用。`K < N` 时残差里“背景”和“没装下的第三个东西”混在一起、无法区分。
- **背景占名额**：`{发, 眼, 脸, 空}` 就是 4 个不同 ID 用满、残差 0.25 = 背景占比；只要残差 > 0 就说明有样本落在背景上。所以能存下的“部件”最多 **N−1** 个。
- **`K = 4` 且 `N ≤ 4` ⇒ 尾部丢失在实际配置里不存在**：4 个样本最多含 4 个不同 ID。所以“层里找不到我的 ID”就等价于“我这部件没覆盖这个像素”，消费端不需要为尾部做特殊处理。
- 三条与层数无关的边界：① 覆盖率说“多少”、不说“形状”（斜边、角、比采样间隔更细的发丝都给不出，这是 MSAA 本身的天花板，TAA 的 jitter 跨帧累积才能把它变成亚像素精度）；② ID 绝不可过滤、覆盖率只能**按 ID 匹配加权**；③ **残差必须显式保留**，消费者要能知道 `Σcov < 1` 意味着“这像素没被解释完”。
- 硬上限：每像素层数固定 K = 4；MSAA 采样数 `N ≤ K`；覆盖率精度 `1/N`；形状信息**不存在**；ID 必须**跨帧稳定**（否则用 ID 判历史有效性的时序效果会抖）。
- GeometryBuffer 的 resolve 已经是这个思想的一维版（`.r` = 总覆盖、`.g` = 被解析面覆盖，`1 − .g` 就是没解释完的部分），K 层是它的推广。

### 2.2 逐样本身份为什么能成立

D3D 光栅化规则：覆盖判定**逐样本**做，但**像素着色器每像素只跑一次**，输出**复制到所有通过深度/模板测试的样本**上。因此：

- 想逐样本得到**不同**身份，唯一可行办法是**身份在这次 draw 内是常量**（身份来自 RSUV，整次 draw 统一）→ “每个样本属于哪个部件”由**硬件的逐样本深度测试**决出；
- 任何“把 ID 插值到逐样本”的想法都是错的（插值出来的 ID 不是任何部件的 ID）。

### 2.3 resolve 的语义

- **resolve 就是求平均，且只对颜色成立**：`ResolveSubresource` 的源必须 MSAA、目标必须单采样，即**逐样本信息不被保留**；Vulkan 规范更狠——**整数格式 resolve 时“任选一个样本的值”**（对 ID 等于随机丢身份）。
- 所以身份**不可能靠 resolve 得到**，只能逐样本 `Load` 再自己归约（OB 的 resolve 就是“逐样本数票 → 按票数降序取前 4 → 平票取更近的样本”，**不做平均**）。
- 逐样本读取机制：`Texture2DMS.Load(coord, sampleIndex)` / `GetSamplePosition` / `GetRenderTargetSampleCount`（SM4+），`EvaluateAttributeAtSample`（SM5+）。
- **“不 resolve、直接绑成 MSAA 纹理”是官方支持的用法**：`RenderTextureDescriptor.bindMS = true` 时“不会被默认 resolve”，正是 GB / OB 取逐样本数据的方式。
- **采样数协商 API 本身就是“给你一个平台支持的更低值”**：`SystemInfo.GetRenderTextureSupportedMSAASampleCount(desc)` 不支持时返回**更低的回退值**。所以只要直接要 4、拿回 2 或 1，**不需要也不应该看相机的 MSAA 设置**。

### 2.4 与相机 MSAA 解耦（关键决策）

GB 的采样数是“跟随相机”（`msaaSamples <= 1` 时直接返回 1）；**OB 必须照抄结构但不照抄这句**——否则“相机 AA 关掉”这个**原始 bug 场景**会拿到二值覆盖率、等于没修。OB 只按**平台能力**回退 4 → 2 → 1。（`HoObjectBufferFormatUtility.GetSupportedSampleCount(desc, requested, selectionEnabled)`。）

### 2.5 其它仍然有效的决策

| 主题 | 结论 |
| --- | --- |
| ID 存储 | 每样本一个 16 bit ID（`R16_UInt` + 4x = 8 B/px 瞬态）；整数 RT 天然不可滤波、不可混合；平台不支持时退化为 `R16_UNorm` + 消费端 `round(v*65535)` 还原 |
| 身份通路 | **RSUV 只当 palette 索引**（官方 use case 就是“index into a global buffer”，且不干扰合批、无额外 CPU 开销）；属性进 palette，不进 RSUV |
| palette | 两级表（部件行 ≤4096 + 角色表 ≤256），`StructuredBuffer`，不支持时退化常量数组；第 0 行 = unknown |
| 组件 | `Ho-ObjectBuffer Group` 单组件挂角色/渲染器，最多 4096 条命名条目（名称 / 类别 / 32 位标签 / 显示色）；面部朝向（`faceBone` + 三轴）与 `TryGetWorldFacing()` 一并沿用 |
| 描边 | **不写 ID**（与 GB 的“物理几何 / 视觉壳层”分离一致）；需要“连描边一起算角色”时用 GB 的 outline coverage 补 |
| 几何 | **全管线深度/法线/几何覆盖率的唯一入口是 GB**；OB 内部 depth-stencil 只服务自身 draw 的归属判定，**永不发布、不被任何 shader 采样** |
| 排序 | 按样本计数（= 覆盖率）降序，平票取更近样本；让“层 0”有稳定含义 |
| 滤波 | 唯一合法形式是**按 ID 匹配加权**：`cov(id, x) = Σ_taps w_t · [id_t == id] · cov_t`。裸双线性 / 按层序插值是错的：4 个层槽是**逐像素排序**的结果，邻居的层 0 可能完全是另一个部件 |

## 3. 非线性 AA 禁令（本次改造的“宪法”，与具体 feature 无关）

**允许线性作用**：`coverage`（乘、lerp、按 ID 匹配的加权求和）、颜色、明确是连续量的通道。

**禁止**：

1. **ID**：不得双线性/三线性过滤、不得平均、不得在插值后比较。一律 `sampler_PointClamp`；比较在原始值上（UNORM8 先 `round(v*255)` 还原整数）。
2. **深度**：同上（GB 的产物也是全管线唯一的深度/法线入口，所以它点采样法线/深度、覆盖率单独出图）。
3. **覆盖率**：不得 `step` / `round` / 阈值化成 0/1 再参与混合——那等于把 AA 又丢了；硬边只能在乘完权重之后做显式选择。
4. **不要把已阈值化的结果写进 buffer**：buffer 只存**身份**与**线性量**，判断留给消费端。
5. **不要把 per-part 常量塞进 per-pixel 通道**再指望它可过滤（旧 `surfaceData` / `reflectionMaterial` 就是反例：`materialClass` 被双线性插值会得到无意义的中间值——这条违规在迁移时已被修掉）。
6. **不要依赖外部 AA**（相机 MSAA / FXAA / TAA）修这种信号；覆盖率必须自己产出。**代码级含义：采样数不读相机的 `msaaSamples`**——“相机 AA 关掉就退化成硬边”正是这套东西要消灭的病。
7. **多表面必须按层加权**：`result = Σ coverage_i × f(id_i)`；只用前层会在“前发 + 后面的脸”这类像素上重新引入硬边。（消费端为出 matte 做 `/ total_combined_alpha` 是合法的，与“禁止把层覆盖率重标定到和为 1”不是同一件事。）

## 4. 踩过的坑

1. **“ID 与 coverage 同通道”会诱导犯错**：同一个 fetch 里 `.r` 是连续量、`.gba` 是 ID，很容易顺手 `saturate` / `step` / 插值。**必须拆成不同的图**。
2. **per-pixel 与 per-part 混淆**：`thickness / curvature / materialClass / transmittance / roughness …` 本是**部件级常量**，却每像素存一份 16F——6~8 张 16F 里绝大多数比特在同一部件内部是常数。→ 常量进 palette。
3. **“同义量只能有一个来源”**：某个量若既在 palette（常量缺省）又在贴图通道（逐像素）里，必须由部件行的“贴图驱动”位**唯一决定**读哪边；消费端只查这个位，**不许“两边取一个”、更不许相乘**。
4. **越界兜底不能 clamp 行号**：`rowBase + slot` 越界会落到**别的角色**的行上，读出来的属性看着合法、其实是错的（比崩溃更难查）。越界一律返回 unknown 行。
5. **RSUV 不序列化**：重启/重载后值会丢，必须在 `OnEnable` / 表变化时重写；受支持的是五个具体类型上的方法（不在 `Renderer` 基类上）。
6. **跨角色判断不该散落在消费端**（`SameCharacter`、“仅同角色”开关），它应该是数据结构的天然属性——同角色 = 高字节比较。
7. **位掩码/层掩码当语义容器有隐性账单**：URP 明确说 Rendering Layers 数量达到 9/17/25… 时性能影响显著上升，因为每加一个 8 bit 通道就多一次纹理访问。而 ID + palette 的 **per-pixel 成本与部件数量无关**。
8. **per-sample 写入（alpha-to-coverage / `SV_Coverage`）有官方约束**：运行时把该掩码与常规覆盖做 **AND**，且“**in multisampling, the runtime shares only one coverage for all RenderTargets**”——一张掩码会同时切该 pass 的所有 MRT（ID 与覆盖率两张图会一起被切）。`AlphaToMask` 本身也标着“intended for use with MSAA… otherwise results can be unpredictable”。
9. **v1 不暴露 renderScale、固定 Full**：降分辨率（代理模式）会按同样的理由给出坏结果——这正是“ID 不能被邻居混合”的推论。
10. **重复指定必须吵，不能静默取先到者**（拖父级 + 展开子级之后非常容易重复）：裁决顺序固定为 **优先级 → 层级距离 → 条目顺序**，每一处重复都记进 `GetConflicts()`，控制台按数量变化告警一次、Inspector 逐条列出。
11. **两段式深度策略决定颜色的含义**：opaque/cutout 先写深度确立归属、transparent 只 ZTest 后叠加——策略一变，`.rgb`（落到哪个面的颜色）会**静默变味**。它随表面数值一起搬去 SB；`.a` 不再是覆盖率（**覆盖率只有一个来源 = ID 层**）。
12. **删/改“没人读”的附件前要看隐式消费者**：`MBufferDepth` 作为纹理只有 debug 在读，但它的**写入策略**决定了 `surfaceColor.a` 的语义，而 `.a` 被角色特化脸色扩散、PLR、SSS 当覆盖率用。

## 5. 业界依据（仍然有效，逐条核对过的来源）

| 参考 | 它证明了什么 / 我们抄哪一条 |
| --- | --- |
| Cryptomatte（Psyop 参考实现） | 一张 RGBA 装两个 `(ID, 覆盖率)` 对，多“层”就是多张图；**我们同构但更省**——16 bit 整数 ID ⇒ 一张 RGBA8 装 4 个 ID、另一张装对应 4 个覆盖率。取用表达式 `(red==ID ? green : 0) + (blue==ID ? alpha : 0)` |
| Cryptomatte（MoonRay / DreamWorks） | 层**按覆盖率降序**排（“the geometry with the most pixel coverage will always be the first entry”）→ 我们的排序规则；平票额外定义“取更近的样本”（Cryptomatte 没有这一维） |
| Cryptomatte（Psyop `docs/nuke.md` 原话） | “relies on **exact values in channels**, and operations that **mix values with neighboring values will damage this information**” → 直接支撑两条硬规则：ID 一律点采样、v1 固定 Full 分辨率 |
| OpenEXR《Deep IDs Specification》 | 层数取小会在“选中被丢弃的对象”时产生噪声（我们的 K=N 让它不可能发生）；浅层 matte 要按前到后累积（`mask_alpha += alpha·(1−total)`，最后 `/ total_combined_alpha`）——这是“按层加权”的遮挡正确版；manifest **强烈建议嵌进文件**而非 sidecar；规范承认“选了一层会沾上后面没选中的东西”，这是覆盖率模型的固有边界 |
| 离线 deep image | 无上界版本（任意样本数 + 每通道配对 alpha）；**我们的 K=4 是它的有界版**；离线要 6~8 层是因为表达**连续面积占比**，我们要的是**逐样本归属**，所以不照抄层数 |
| Visibility Buffer（JCGT 2013）/ Deferred Attribute Interpolation（HPG 2015）/ Deep G-Buffers（HPG 2016） | 实时一侧同族做法：身份进缓冲、属性延后按 ID 取；**保留多层的动机正是抗锯齿**；Deep G-Buffers 专为减少轮廓处抖动 |
| UE Custom Depth + Custom Stencil | 8 bit、per-primitive、post-process 可读；**官方写下的局限**：这类轮廓在 TAA 下得不到抗锯齿，且“outside the object we would also need to adjust the depth buffer (not done yet, costs extra performance)” |
| UE `GPUScene` | per-primitive 自定义数据进 scene-wide buffer、shader 内按 `PrimitiveID` 索引（每 primitive ≤32 float）——“表 + 索引”的引擎级同形物，且能降低 draw call |
| HDRP Rendering Layer Mask Buffer | 引擎确实会给效果一张逐像素身份/掩码图；但“层数越多、效果能支持的越少”是这类方案的常见天花板（所以我们要 ID + 覆盖率，而不是更多“层”） |
| Unity RSUV | 官方 use case：“**Use the RSUV as an index into a larger data structure stored in a global GraphicsBuffer**”；不干扰合批、无额外 CPU 开销、显著快于 MPB；**值不序列化** |
| Nuke 社区惯例 | ID 不过滤、覆盖率放 alpha、两者分别可调（覆盖率是软硬程度的唯一来源） |
| Arnold `crypto_asset/material/object`、Redshift 共享 Object ID | “选谁”由合成端决定而不是渲染端；多个对象可共享一个 ID 以成组——对应我们的“类别 + 标签位掩码” |

## 6. 迁移落点（当时的分期 → 实际结果）

| 原计划 | 实际落点 |
| --- | --- |
| P1：`HoCharacterBuffer` feature（ID/coverage pass + palette + 选择层 + 调试） | 身份与覆盖率 → **`Ho-ObjectBuffer`**（4 层身份 + 覆盖率 + 自建 MSAA resolve + 朝向） |
| P1.5：把表面色 / 材质数值从 CB 摘干净 | 表面数值 → **`Ho-SurfaceBuffer`**（五张数值图 + owner + 语义 lane，单采样） |
| P2：`Ho-SurfaceBuffer` 独立 feature | ✅ SB 已独立；`surfaceData` → `Classification` + `Material.b`，`reflectionMaterial` → `Reflection`，`surfaceColor` → `Color` |
| P2.5 / 决策 21：Cryptomatte 选择层移出，独立 `Ho-Cryptomatte` | **该 feature 未实现**；遮罩与合成属性由 **`Ho-AttributeComposite`（AC）** 承担（Selection 池 + typed 查询门面 + runtime catalog），ScreenProcess 改吃 AC 遮罩 |
| P3：SSS / PlanarReflection 切新 buffer | ✅ 两者改读 AC（覆盖率/遮罩）+ SB（表面数值）+ GB（几何） |
| P4：删除 MetadataBuffer、迁移 `HoFaceAxis` 归属 | ✅ R7 已整块删除；`HoFaceAxis` 迁到 `Runtime/ObjectBuffer/HoFaceAxis.cs`（按 int 序列化，数值未动） |

**唯一被放弃的设想**：`custom0~3` 这类“匿名 per-pixel 材质通道”与 Cryptomatte 式具名选择层。今天的口径是 AC 的 typed 查询 + SB 的语义 lane，**不再提供匿名通道**。

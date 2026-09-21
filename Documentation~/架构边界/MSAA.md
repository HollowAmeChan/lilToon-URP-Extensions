# 架构边界：MSAA

> **状态：现行架构边界说明**（结论 2026-09-14 定稿，2026 文档审核时按当前代码校正）。
> 我们对 MSAA 的底线与"哪些地方必须处理它"仍然有效；文中的实现指路已从 MetadataBuffer 换到 GB / OB / SB / AC。
> 过程记录与实测数据见 `Documentation~/归档/LILTOON_GTAO_MSAA_SILHOUETTE_INVESTIGATION_LOG.md`。

## 结论

MSAA 可以开。我们对它只有一条底线：**开着不能坏** —— 不报错、不出现功能性错误、不出现成片的明显崩坏。

画质允许退化。MSAA 只作用于几何边缘，而本管线大量依赖后处理与时序特性，抗锯齿质量本来就不该由它承担；交给 TAA / FXAA / SMAA，或内部高分辨率加超采样上采样（DLSS / FSR 类）都更合适。**逐 sample 的精确性不在要求之内**，那是额外的工程，见文末。

## MSAA 在我们管线里到底意味着什么

本管线的数据主干是：GeometryBuffer 提供每像素一个的几何事实（法线 / 深度 / 覆盖率），屏幕空间特性产出每像素一个的信号，时序特性再把它跨帧累积。MSAA 提供的却是逐 sample 的覆盖与逐 sample 一致的颜色。两者在**部分覆盖像素**（同一像素里既有前景又有背景）上必然对不上：一个逐像素的 AO 值乘到逐 sample 平均过的颜色上，误差与两侧的遮挡差、颜色差成正比，而且宽度恰好一个像素、落在几何硬边界上 —— 表现就是"紧贴物体一圈发白、边界很硬、不像经过 MSAA"。深色物体压在亮背景上时误差为正，所以最先在发丝压皮肤这种地方暴露。

还有一层：MSAA 下的几何解析只能从若干 sample 里挑一个（我们挑最近的），于是"前景表面的未遮挡值"被用到了"颜色主要由背景构成"的像素上。这不是某段代码写错，而是"逐像素单值"这个前提本身的边界。

所以管线对 MSAA 的态度不是忽略它，而是**在唯一入口处把它解析成单采样事实，并把覆盖比例留给消费者补偿**：`HoGeometryBufferResolve.shader` 读取 MSAA 的法线 / 深度，挑最近表面作为该像素几何，并同时写出两件事 —— 总覆盖率（R）与被解析表面占有的比例（G）。屏幕空间特性据此知道"这个像素的颜色里有多少是别人贡献的"，从而在轮廓处把背景的遮挡补回来。

## 现在管线里哪些地方在处理 MSAA

**这一整块都要保留，不要因为"看起来是多余的兼容代码"而删。** 它们正是"开着不坏"的实现。

**必需的解析入口。** `HoGeometryBufferRenderTargets`（MSAA 色/深度目标的分配与采样数协商）、`HoGeometryBufferPass`（MSAA 绘制目标 + Resolve pass，RenderGraph 与兼容两条路径）、`HoGeometryBufferResolve.shader`（多关键帧的 `Texture2DMS` 读取与单采样写出），以及覆盖率纹理本身。少了它们，MSAA 下几何就退化成"最近 sample"，屏幕空间特性会立刻出现整圈硬边 —— 这正是修复前观察到的情况。

**自建 MSAA 的第三条路。** `Ho-ObjectBuffer` 的身份池**不跟随相机 MSAA**：它按平台能力协商自己的采样数（`HoObjectBufferFormatUtility.GetSupportedSampleCount` + `_HoObjectBufferRequestedSamples` / `_HoObjectBufferActualSamples`），逐样本写 16 bit 身份后自己 resolve 成"最多 4 层身份 + 每层覆盖率"。这是"掩码自带亚像素覆盖"的正解（见 `语义掩码.md`），代价是自成一套目标与 resolve。

**必需的补偿。** 屏幕空间特性的覆盖率加权：目前是 GTAO `Spatial` 末段的轮廓复合，以及 `RecordBlit` 里把发布给材质的纹理固定为单采样。

**有意跟随相机 MSAA 的地方。** OIT 的主目标在满分辨率分支上沿用相机采样数（`WeightedOITRendererFeature`），GeometryBuffer 的绘制目标同理。**这是刻意的**，不要顺手统一成单采样：它们和相机颜色是同一张多重采样目标，必须同采样数。

**必须单采样的地方。** 所有会被材质或后处理采样、或者要被重采样 / 重投影 / 跨帧重投影的 RT：各特性的 history 与屏幕空间输出（GTAO history、SB 的五张数值图与语义 lane、SSGI、SSS、PlanarReflection、CharacterSpecialization、ScreenProcess、ImageProcess、ShadowCast 等）。理由只有两条，但都是硬的：多重采样资源不支持线性过滤（重投影与上采样都依赖 bilinear），以及多次采样的 sample 索引没有时间语义、无法跨帧对应。

> SB 的语义 lane 曾经按 MSAA 渲染、让 AC 用 `Texture2DMS` + `Load` 读，**结果是拖影**（坐标 / 采样数 / `bindMS` 任一处理解错就会静默读到邻域或旧 sample）。
> 现在 lane 是**单采样**，读端只做普通采样；逐 sample 细分将来由 SB 自己 resolve 成单采样再发布 —— 与这里"会被采样的 RT 一律单采样"是同一条纪律。

**诊断。** `HoGeometryBufferRendererFeature` 在 resolve shader 缺失时会打 warning，保留 —— 它提示的正是"MSAA 下几何会退化成最近 sample"。

## 不能出现的崩坏

开启 MSAA 后，以下任一项出现都算 bug，必须修：控制台报错或平台校验失败（例如把多重采样资源当普通 `Texture2D` 采样、绑定为 `Texture2DMS` 的纹理被当普通纹理读）；AO / SSGI / 反射 / SSS / OIT 这类结果全黑、全白、成片错位或闪烁；轮廓处出现整圈白线或接触阴影被吃掉一圈；时域历史爆掉、永久拒绝或产生明显拖影。

个别**极尖锐、高频**的位置（发丝尖端这类）允许残留极少量痕迹 —— 那是"从邻居借背景遮挡"这个屏幕空间近似的分辨率下限，不是崩坏。

## 新特性要遵守的

新增任何屏幕空间 / 时序特性时：不要假设"这里一定是单采样"或"覆盖率一定是 1"，需要时显式读覆盖率并加权；历史与累积缓冲一律单采样；不要把多重采样资源当普通纹理采样或过滤；不要依赖 sample 位置或 per-sample 身份（D3D 之外没有可移植的取法）；发布给材质的全局纹理要显式声明读取依赖，并至少用 MSAA 开关各跑一遍。

另外要知道消费端的边界：材质侧没有逐 sample 能力（lilToon 全仓库检索 `Texture2DMS` / `Texture2DMSArray` / `EvaluateAttributeAtSample` / `SV_Coverage` 零命中），屏幕空间信号对材质而言永远是"每像素一个值"。

"用 1 bit 语义位当轮廓"这类坑见配套文档 `语义掩码.md`：语义位表达**归属**而不是**覆盖率**，当轮廓用时必然是硬边；现在覆盖率统一从 OB 身份池拿，不再从 bit 里挤。

## AA 怎么选

TAA 最适合本管线 —— 它和后处理、时序链天然同源，只需要运动矢量。不想上时序就用 FXAA / SMAA，便宜但几何边缘质量弱。真正的质量提升来自内部高分辨率 + 上采样（DLSS / FSR 类），它同时改善几何与着色走样。alpha-to-coverage 只解决"裁剪出来的边"，和 AO、时序无关（lilToon 有 `_AlphaToMask` 属性）。至于"MSAA + 自己逐 sample 算 AO / 重投影"：技术上可行，但成本与平台分支都高，只在明确需要精确性时评估。

## 遗留

尖刺处的少量残渣可以通过扩大借样范围或放宽判据继续压，属于可选优化。若哪天要求"MSAA 下也精确"，优先级是：先做**材质按自身所属表面取 AO**（成本集中在两处，且不需要平台化的 sample 位置），其次才是自建 per-sample 求解。

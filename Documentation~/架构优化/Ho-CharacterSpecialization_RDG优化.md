# Ho-CharacterSpecialization（CS）RenderGraph 逐趟清单与优化方案

对象：`Runtime/CharacterSpecialization/` 的 RDG 主路径（`HoCharacterSpecializationPass.RecordRenderGraph`，`Runtime/CharacterSpecialization/HoCharacterSpecializationRendererFeature.cs:486-1067`）。
本文是**清单 + 方案**，不是施工单；**没有改任何代码/Shader/检查器/文档**。
行号以当前工作区为准；**外部仓库证据**（lilToon 材质侧）单独标注，不在本工作区，需另行复核。

## 硬约束（前置，不推翻、不在本文档里讨论）

| # | 约束 | 落到本文档的含义 |
| --- | --- | --- |
| C1 | **不降分辨率**。半分辨率/四分之一分辨率中间纹理一律不采用（"降分辨率其实没啥意义，甚至他就是不能降"） | 所有"候选刀"必须在 `renderScale = Full` 下成立；`HoCharacterRenderScale`（`HoCharacterRenderScale.cs:5-13`，Full=1/Half=2/Quarter=4）只作为"现状可配项"记录，不作为建议 |
| C2 | **不降质量**。不换更省的模糊核、不做改变观感的近似 | 每把刀都要给"数学是否等价"的判据；给不出判据的一律标 `待验证` 并写验证方法 |
| C3 | 允许的刀只有**纯架构**：合趟、去冗余工作、断假依赖、资源卫生 | 与 §4 的刀一一对应 |
| C4 | **宽/柔化雾气填充模式保持原样**（`HoCharacterSubjectOutlineFillMode.SoftFog = 2`，`HoCharacterSpecializationSettings.cs:33-34`） | 它的开销在**主体轮廓那一趟的采样**里（半径大 → 同一趟 65 个 tap 的缓存命中差），**不额外增加 pass**；本文档不动它，也不提议改它的核 |
| C5 | 只产出本文档一个文件 | `git status` 只有一个新增未跟踪文件 |

---

## 0. 第 0 步：测量口径（先做完这一步，再决定动哪把刀）

### 0.1 用什么测

| 工具 | 取什么 | 注意 |
| --- | --- | --- |
| Unity GPU Profiler（Profiler → GPU 模块） | 逐 marker 的 GPU 时间 | **本 feature 的 13 趟共用同一个 sampler 名 `Ho-CharacterSpecialization`（`:288`）** → 你会看到一串同名条目，只能按顺序数；先做一遍"逐趟开关"实验把顺序钉死（关掉三支 → 只剩 4 个 marker，正好对应 §1.1 的 #1/#2/#3/#13） |
| Render Graph Viewer（Window → Analysis → Render Graph Viewer） | pass 列表与顺序、每趟的 resource 读写、**Resource List 里的 transient 显存峰值**、每个资源的存活区间 | 这是唯一能把 pass 名与 §1.1 的表逐行对上、并直接看到假依赖是否缩短存活区间的地方 |
| RenderDoc 抓帧 | 每趟的 region 名、逐 pass 的 texture fetch/带宽、attachment 的 load/store action | 用来验证 §6-2（`AccessFlags` 语义）、§6-4（destination 的 sample count）、以及"某趟到底采了哪些纹理" |
| 编辑器外 | 同机同分辨率、固定相机与角色、固定参数，跑 3 次取中位数 | 参数必须一起记录（composite 的 tap 随参数变，§1.3） |

### 0.2 要带回的数（缺一项就别下结论）

1. **capture 两趟各自的 ms**（#1、#2）；
2. **SemanticMask Blur 的 ms**（#3）；
3. **三支各自的**：source 趟、`FastGaussian 1`、`FastGaussian 2` 的 ms（#4-#12 九趟，按 §1.1 的顺序对应）；
4. **Composite 的 ms**（#13），并记录当时的参数：`semanticMaskBlurRadiusPixels`、`hairShadowSoftnessPixels`、`hairShadowSpreadPixels`、`eyeRevealFeatherPixels`、`eyeRevealDilationPixels`、三支半径；
5. **总帧时间**（GPU frame time）+ 相机分辨率 + `renderScale`（CS / MB / GB 三个都对一遍）；
6. **RDG Resource List 的 transient 峰值**（CS feature 自己的资源 vs 整帧的对比）；
7. 可选但很值：RenderDoc 里各趟的 **fetch 字节**（尤其脸色扩散两趟 blur 的 depth 采样、以及 composite）。

### 0.3 判据表（哪把刀取决于哪个数）

| 数 | 判据 | 结论 |
| --- | --- | --- |
| capture 两趟 ms 占比 | ≥ 15% | K1 优先（眼透关掉即兑现）；同时 K6（两趟合一）值得先做 V1 实验 |
| | < 5% | K1/K6 降级，先把 K4/K3/K5 做完 |
| Composite ms 占比 | ≥ 25% 且 RenderDoc 显示该趟是采样受限 | K11（81 → 18 tap）划算；否则不做 |
| 脸色扩散支 ms 占比 | ≥ 10% | K4 立刻做（它同时砍分配、写、读，且可证等价） |
| RenderDoc 里脸色扩散 blur 的 depth fetch 字节 | 占该趟 > 40% | 同上，K4 的收益上限基本由这个数封顶 |
| RDG transient 峰值 | CS 资源占整帧 > 15% | K9（池化）与 F7/F8 断边（别名）才有意义；否则只做 K8 的卫生 |
| 合成趟 ms 是否随模糊对存在与否变化 | 断掉 F4/F5/F6 前后 composite ms 不变 | 证明那些是纯生命周期假依赖（K3 只省显存/图规模，不要写成性能收益） |
| 单支启用的场景占比 | 占比高 | K10（融合）才值；否则收益面太窄 |

---

## 1. 这一帧的 pass 清单

计数口径：**一个 RDG pass = 一次 `AddRasterRenderPass`**（名字取 `renderGraph.AddRasterRenderPass<...>("...")` 的第一个参数，即 RDG 视图里显示的名字）。捕获两趟是**几何 pass**（`DrawRendererList`），其余是全屏 raster pass（`Blitter.BlitTexture` 全屏三角形）。

> **默认值的真源**：运行时生效的效果值走 Volume（`HoCharacterSpecializationVolume.cs:193-270` 的 `CopyEffectsTo`，由 `ResolveSettings` `:184-195` 调用，且 `Settings.CopyFrom` 在它之前），所以**默认值以 `HoCharacterSpecializationEffects.cs` 为准**；`HoCharacterSpecializationSettings.cs` 里那套同值默认只服务 feature 资产（两套默认并存，改默认值要改两处，属卫生项）。下表"默认"列一律按 `Effects.cs`。

### 1.1 主表（全开 = 13 趟；默认 = 4 趟）

"读"只列**实际被 shader 采样**的面；假依赖（声明了但没采样）在 §3.2 单列，不进本表。

| # | RDG pass 名 | 录制位置（file:line） | render func / shader 入口 | 读（面） | 写（面） | 进图条件 | 迭代 | tap/px |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `Ho-CharacterSpecialization CaptureFace` | `HoCharacterSpecializationRendererFeature.cs:557-584` | 内联 lambda `:573-583`；材质侧 `LightMode=HoCharacterCapture`（外部仓库） | 0（纯几何） | eyeColor + eyeData + captureDepth（`WriteAll`，`:568-570`）+ 清屏（`:577`→`ClearCaptureTargets` `:1088-1098`） | **无条件**（`:557` 不在任何 `if` 内）；只要整个 feature 记录就会进图 | 1（一次 `DrawRendererList`） | 材质侧，未确认（§6-1） |
| 2 | `Ho-CharacterSpecialization CaptureEye` | `:586-613` | 内联 lambda `:602-612` | 0（纯几何） | 同上 3 面（`ReadWrite`，`:597-599`），无清屏 | **无条件**（`:586`） | 1 | 材质侧，未确认（§6-1） |
| 3 | `Ho-CharacterSpecialization SemanticMask Blur` | `:634-642` → `Effects/HoCharacterSpecializationPass.SemanticMaskBlur.cs:55-89` | `Shaders/HoCharacterSemanticMaskBlur.shader` pass 0（`Frag:43-76`） | objectCustom0_3、objectCustom4_7 | semanticLow、semanticHigh（MRT×2） | `RequiresSemanticMaskBlurTextures`（`SemanticMaskBlur.cs:17-25`：5 个"读取抗锯齿掩码"任一勾选，默认前 3 个为 true，`Effects.cs:21,24,27`）∧ `hasMetadataObjectCustom0/1` ∧ 材质非空（`:619-622`） | 1 | 2 × taps²；taps = `clamp(round(2·max(r,1)+1),3,8)`（`SemanticMaskBlur.shader:50-51`），默认 r=1（`Effects.cs:18`）→ **18** |
| 4 | `Ho-CharacterSpecialization FaceHair Source` | `:677-701` | `Shaders/HoCharacterFaceHairDiffuse.shader` pass 0（`Frag:38-54`） | objectCustom0_3、surfaceColor、normalDepth（3；另有 1 条假读，§3.2-F1） | FaceHairDiffuseSourceColor + FaceHairDiffuseSourceDepth | `RequiresFaceHairDiffuseTextures`（`FaceHairDiffuse.cs:11-33`：`faceHairDiffuseEnabled`（默认 false，`Effects.cs:113`）或 debug∈{5,6,7,8}）∧ `hasMetadataSurfaceColor` ∧ 材质（`:651`） | 1 | **3**（`.g`:43、surfaceColor:44、normalDepth:45） |
| 5 | `Ho-CharacterSpecialization FaceHair FastGaussian 1` | 循环 `:706-729`（i=0），写 temp | `Effects/…FaceHairDiffuse.cs:85-117` → shader pass 1（`Frag:81-116`） | FaceHairDiffuseSourceColor + SourceDepth（2） | TempColor + TempDepth | 同 #4 | 1 | **82**（中心 2 + 40 tap × 2 张纹理；`FaceHairDiffuse.shader:73,90-108`） |
| 6 | `Ho-CharacterSpecialization FaceHair FastGaussian 2` | 同循环 i=1，写 final | 同上 | TempColor + TempDepth（2） | FaceHairDiffuseColor + FaceHairDiffuseDepth | 同 #4（`FaceHairDiffuseBlurIterationCount = 2`，`:289`） | 1 | **82** |
| 7 | `Ho-CharacterSpecialization SubjectOutline Source` | `:749-793` | `Shaders/HoCharacterSubjectOutline.shader` pass 0（`Frag:91-112`） | objectCustom0_3、objectCustom4_7、GB depthTexture（3）＋ 模糊对（2，仅本效果自己勾了 AA 时才真采）＋ 1 条假读（§3.2-F2） | SubjectOutlineSource | `RequiresSubjectOutlineTextures`（`SubjectOutline.cs:14-36`：`subjectOutlineEnabled`（默认 false，`Effects.cs:140`）或 debug∈{9,10,11,12}）∧ `hasGeometryDepth` ∧ 材质（`:735`） | 1 | **2**（低/高两张对象通道各 1）+ 0..1（depth，仅 `mask>0.0001` 时采，`shader:100-109`） |
| 8 | `Ho-CharacterSpecialization SubjectOutline FastGaussian 1` | 循环 `:797-816`（i=0） | `Effects/…SubjectOutline.cs:193-218` → shader pass 1（`Frag:132-162`） | SubjectOutlineSource（1） | SubjectOutlineTemp | 同 #7 | 1 | **65**（中心 1 + 64；`shader:130,141-158`） |
| 9 | `Ho-CharacterSpecialization SubjectOutline FastGaussian 2` | 同循环 i=1 | 同上 | Temp（1） | SubjectOutlineTexture | 同 #7（`SubjectOutlineBlurIterationCount = 2`，`:290`） | 1 | **65** |
| 10 | `Ho-CharacterSpecialization EnhancedOutline Source` | `:836-880` | 同 #7 的 shader pass 0（同材质、换 `sourceParams.x` 通道，`:847`） | objectCustom0_3、objectCustom4_7、GB depthTexture（3）＋ 模糊对（2，条件同 #7）＋ 1 条假读 | EnhancedOutlineSource | `RequiresEnhancedOutlineTextures`（`SubjectOutline.cs:38-59`：`enhancedOutlineEnabled`（默认 false，`Effects.cs:204`）或 debug∈{13,14,15}）∧ `hasGeometryDepth` ∧ `subjectOutlineMaterial`（`:822`） | 1 | **2** + 0..1（同 #7） |
| 11 | `Ho-CharacterSpecialization EnhancedOutline FastGaussian 1` | 循环 `:884-903`（i=0） | 同 #8 | EnhancedOutlineSource（1） | EnhancedOutlineTemp | 同 #10 | 1 | **65** |
| 12 | `Ho-CharacterSpecialization EnhancedOutline FastGaussian 2` | 同循环 i=1 | 同 #8 | Temp（1） | EnhancedOutlineTexture | 同 #10 | 1 | **65** |
| 13 | `Ho-CharacterSpecialization Composite` | `:914-1064` | 内联 lambda `:999-1063` → `Shaders/HoCharacterSpecializationComposite.shader` pass 0（`Frag:608-823`） | 15 面（清单见 §1.3） | `_lilHoCharacterCompositeColor`（= 新相机颜色，`:1066`） | **无条件**（`:914`），但整条链前提是 `:489` 与 `:527-536` 全过且 `backBufferActive == false` | 1（内嵌多个运行期循环，见 §1.3） | 默认参数 ≈ **126**；随参数可变，上限 ~700+（§1.3） |

**趟数结论**：全开 **13 趟**；默认参数（眼透 + 前发投影开、三支全关、语义抗锯齿前 3 项开）**只有 4 趟**：`CaptureFace` + `CaptureEye` + `SemanticMask Blur` + `Composite`。其中 **3 趟无条件进图**（#1、#2、#13），其余 10 趟分别由三个效果的开关或它们的 debug 模式拉起来。

### 1.2 目标纹理（分辨率 / 格式 / 创建处）

`CreateTextureDesc`（`:1114-1141`）统一定义了 CS 全部非深度纹理：分辨率 = **相机目标 ÷ (int)renderScale**（`:1120-1123`），因此**只有 `renderScale = Full` 时才与相机目标同尺寸**；格式取传入的 `GraphicsFormat`，`MSAASamples.None`（`:1131`）、`clearBuffer = true`（`:1132`）、Bilinear/Clamp（`:1134-1135`）。

| 纹理（常量名，`ShaderConstants.cs:14-32`） | 分辨率 | 格式 | 创建处 |
| --- | --- | --- | --- |
| eyeColor | 相机 ÷ renderScale | `GetHdrGraphicsFormat()` = `R16G16B16A16_SFloat`（`:1178-1182`；不支持时回落 `SystemInfo` LDR / `R8G8B8A8_UNorm` / `B8G8R8A8_UNorm`，`:1198-1212`） | `:539` |
| eyeData | 同上 | `GetDataGraphicsFormat()` —— **与 HDR 版逐字相同的实现**，今天也是 `R16G16B16A16_SFloat`（`:1184-1188`） | `:540` |
| captureDepth | 相机 ÷ renderScale（`CreateDepthDescriptor` `:1296-1313`，同 divisor） | 相机 `depthStencilFormat`（可用时）→ `CoreUtils.GetDefaultDepthStencilFormat()` → `GetDepthStencilFormat(24)` → `D32_SFloat`（`:1315-1336`） | `:541-547` |
| semanticLow / semanticHigh | 相机 ÷ renderScale | `GetSemanticMaskGraphicsFormat()` = `R8G8B8A8_UNorm`（`:1192-1196`） | `:630-632` |
| FaceHairDiffuse SourceColor / TempColor / Color | 相机 ÷ renderScale | `R16G16B16A16_SFloat` | `:654-673` |
| FaceHairDiffuse SourceDepth / TempDepth / Depth | 同上 | `GetDataGraphicsFormat()` = `R16G16B16A16_SFloat`（`:659-675`） | 同上 |
| SubjectOutline Source / Temp / 最终 三张 | 同上 | 同上（`:738-747`） | 同上 |
| EnhancedOutline Source / Temp / 最终 三张 | 同上 | 同上（`:825-834`） | 同上 |
| `_lilHoCharacterCompositeColor`（destination） | **相机目标尺寸**（desc 从 `source` 抄，`:906`） | `EnsureHdrTextureDesc` → 16F；`clearBuffer = false`（`:908`） | `:906-911` |

输入侧（CS 不创建，只读）：

| 输入 | 分辨率 | 格式 | 生产处 |
| --- | --- | --- | --- |
| MB maskId | 相机 ÷ **MB 自己的** renderScale（默认 Full） | `R8G8B8A8_UNorm` | `MetadataBuffer/HoMetadataBufferPass.cs:222`、`HoMetadataBufferFormatUtility.cs:9` |
| MB objectCustom0_3 / objectCustom4_7 / surfaceColor | 同上 | `R16G16B16A16_SFloat` | `HoMetadataBufferPass.cs:225-228`、`HoMetadataBufferFormatUtility.cs:15` |
| GB normalDepth | 相机 ÷ **GB 自己的** renderScale（默认 Full） | `R16G16B16A16_SFloat` | `GeometryBuffer/HoGeometryBufferPass.cs:230-234`、`HoGeometryBufferFormatUtility.cs:9` |
| GB depthTexture | 同上 | 深度格式 | `HoGeometryBufferPass.cs:235-241` |

> **单位陷阱（代码事实，不是待验证）**：composite 里所有"像素半径"都用 `MetadataTexelSize()` = `_HoMetadataBufferMaskIdTexture_TexelSize`（`Composite.shader:72-75`），即 **MB 的 texel**；而三支的模糊半径用各自 `_BlitTexture_TexelSize`（`FaceHairDiffuse.shader:88`、`SubjectOutline.shader:139`），即 **CS 纹理的 texel**。两个 renderScale 是两个独立设置（`HoMetadataBufferSettings.cs:30`、`HoGeometryBufferSettings.cs:30`、CS 的 `Settings.cs:132`），默认都是 Full，所以默认下一致；一旦其中之一改成 Half，同一个"像素"数字会指向不同的物理尺寸，比对画面时不要误判成 bug。

### 1.3 composite 的采样构成（为什么它的 ms 必须连着参数一起记）

`Composite.shader:608-823` 是唯一"一趟里塞了全部效果"的 pass，tap 数**随参数变化**：

| 项 | tap/px | 依据 |
| --- | --- | --- |
| 固定 | source 1 + eyeColor 1 = **2** | `:613`、`:619` |
| 眼透（默认开） | maskId 1 + normalDepth 1 + eyeData 1 + frontHair 1 + revealArea 1 + `SampleEyeAlpha` **81** ≈ **86** | `:248-250,252,254`；81 = `SampleEyeAlpha` 的 9×9（扩张 8 邻域+中心 = 9，羽化对 9 个偏移各再算一次 9 → `:198-216,228-238`），默认扩张 2px / 羽化 1px（`Effects.cs:43,46`） |
| 眼透角度修正（默认关） | +2（eyeData 1 + 角度表 1） | `:275-277`，仅在 `_HoCharacterEyeAngleParams.x > 0.0001` 时采 |
| 前发投影（默认开） | 柔化 5 → taps=5 → **25** + receiver 1 + maskId 2 + normalDepth 1 ≈ **29** | `:324`（box 循环 `:158-166`）、`:328`、`:330-331`、`:299` |
| 前发投影·扩散像素 > 0 | 每个 box tap 再 ×9（`SampleSemanticSpread` 的 3×3 max，`:112-130`）→ 25×9 = **225** | `:121-128,164` |
| 脸色扩散 | **5** | 采样点 `:370-371`（frontHair、blurredColor）、`:379-380`（normalDepth、blurredDepth）＋ `:393`（取色，仅 amount>0 时） |
| 主体轮廓 | **2**（源 mask 1 + 模糊 1） | `:418,428` |
| 增强轮廓 | **2** | `:438,448` |
| 理论最大值 | 柔化 8（taps=8 → 64）× 扩散>0（×9）= 576 + 眼透拉满 + 其余 ≈ **700+** | `:143,151,164`、`Effects.cs:104,107`（默认柔化 2 / 扩散 0） |

**读写的 15 面清单（#13）**：source(camera color)、maskId、normalDepth、objectCustom0_3、objectCustom4_7、eyeColor、eyeData、semanticLow、semanticHigh、FaceHairDiffuseColor、FaceHairDiffuseDepth、SubjectOutlineSource、SubjectOutlineTexture、EnhancedOutlineSource、EnhancedOutlineTexture（+ debug 5 时 FaceHairDiffuseSourceColor，`:981` 声明、`:681` 采样）。

### 1.4 三个容易搞错的点

1. **所有 13 趟共用同一个 ProfilingSampler**：`:288` 只 `new ProfilingSampler("Ho-CharacterSpecialization")` 一次，13 处 `AddRasterRenderPass` 全传它（`:557,586`、`SemanticMaskBlur.cs:65`、`:677,717`、`:749,807`、`:836,894`、`:914`）。→ **GPU Profiler 里你看到的是 N 个同名 marker**，只能按顺序数，或改用 RDG 视图 / RenderDoc 的 pass region 区分（§0.1 的测量口径必须建立在这点上）。
2. **捕获两趟不是全屏 pass**：它们是 `DrawRendererList`（`:581,610`），片元里靠 `clip()` 按物体自己的 Face/Eye 位决定要不要画（`Shaders/HoCharacterCaptureCommon.hlsl:56-64,68-69`），所以**两趟都会对全部被捕获物体跑完整顶点链**，只是片元被裁掉。附加成本是明确的：`CaptureFace` 还额外画一次清屏（`:1091-1094`）。
3. **`captureDepthTexture` 在本仓库里没有任何采样者**（全仓库 grep 只有创建/绑定，没有 `SAMPLE`）：它唯一的用途是捕获两趟自己的深度测试（材质侧 `ZWrite On / ZTest LEqual`，见 §3.1-T13 与 §3.3-V1 的外部证据）。

---

## 2. 每趟的读写量估算

> **口径声明**：以下是**按代码结构推出的事件计数与字节数**，不是 ms，也不是实测 DRAM 流量。
> 计数规则：一趟里**每张被采样的纹理算 1 次满屏读**（tap 数在第 1 节单列），每个渲染附件算 1 次满屏写。
> 基准备份：1920×1080 = 2,073,600 px；CS/MB/GB 的 renderScale 都取 Full（默认）。8 B/px = RGBA16F = 16.59 MB/面；4 B/px = RGBA8 或深度 = 8.29 MB/面。

### 2.1 逐趟（全开）

| # | pass | 读（面 → MB） | 写（面 → MB） |
| --- | --- | --- | --- |
| 1 | CaptureFace | 0 | 3 面（8+8+4 B/px）= **41.5** ＋ 清屏满屏 2 MRT = 额外 **33.2** |
| 2 | CaptureEye | 0 | 3 面 = **41.5**（ReadWrite，几何覆盖面积） |
| 3 | SemanticMask Blur | 2 × 8 B = **33.2** | 2 × 4 B = **16.6** |
| 4 | FaceHair Source | 3 × 8 B = **49.8** | 2 × 8 B = **33.2** |
| 5 | FaceHair FastGaussian 1 | 2 × 8 B = **33.2** | 2 × 8 B = **33.2** |
| 6 | FaceHair FastGaussian 2 | 2 × 8 B = **33.2** | 2 × 8 B = **33.2** |
| 7 | SubjectOutline Source | 3 面 = **41.5**（＋自己勾 AA 时再 2 面 = **74.8**） | 1 × 8 B = **16.6** |
| 8 | SubjectOutline FastGaussian 1 | 1 × 8 B = **16.6** | **16.6** |
| 9 | SubjectOutline FastGaussian 2 | **16.6** | **16.6** |
| 10 | EnhancedOutline Source | **41.5**（＋AA = **74.8**） | **16.6** |
| 11 | EnhancedOutline FastGaussian 1 | **16.6** | **16.6** |
| 12 | EnhancedOutline FastGaussian 2 | **16.6** | **16.6** |
| 13 | Composite | 12 面 × 8 B + 3 面 × 4 B = **224.0** | 1 面 = **16.6** |

### 2.2 合计

| 配置 | 事件计数 | 字节数（1080p） |
| --- | --- | --- |
| **全开（13 趟，含双轮廓读模糊对）** | **36 读 + 21 写 = 57** | 读 ≈ **589 MB**，写 ≈ **315 MB**（含清屏 33 MB）→ 合计 ≈ **0.92 GB/帧** |
| **默认（4 趟）** | **9 读 + 9 写 = 18** | 读 ≈ 124 MB，写 ≈ 149 MB → 合计 ≈ **0.27 GB/帧** |

**这些数不能当 GPU 时间用**，差在三处（写清楚，避免误判）：① 逐趟只算 1 次面读，而实际 fetch 数是第 1 节的 tap（65 tap 的模糊 = 同一批纹素被取 65 次，绝大多数命中 L1/L2，不产生 65 倍 DRAM 流量）；② 捕获两趟的写只发生在角色覆盖面积上，但顶点/片元成本是几何+材质侧决定的；③ 相机颜色 `source` 的格式由 URP 决定（HDR 常见 4 B/px 打包格式，LDR 4 B/px），本表按 8 B/px 计，实际要按抓帧看到的格式换算。

---

## 3. 依赖表 / 依赖真伪

判定依据一律给到"读哪个文件的哪一段"。

### 3.1 真依赖

| # | 边（生产者 → 消费者） | 依据 | 少了它会怎样 |
| --- | --- | --- | --- |
| T1 | MB.objectCustom0_3/4_7 → #3 | `SemanticMaskBlur.cs:74-75`＋`SemanticMaskBlur.shader:66-67` | 语义抗锯齿副本变成清屏值，所有勾了"读取抗锯齿掩码"的效果边缘消失 |
| T2 | MB.objectCustom0_3 → #4 / #7 / #10 | `FaceHairDiffuse.shader:43`、`SubjectOutline.shader:96`（`SampleObjectCustomChannel`） | 脸/轮廓的源遮罩全 0 |
| T3 | MB.objectCustom4_7 → #7 / #10 | 同上（通道 4..7 走高纹理；主体轮廓在特性里写死 `CharacterFull=0`（`:760`），增强轮廓默认 `CharacterBody=6`（`Effects.cs:207`）） | 增强轮廓（默认通道 6）直接黑掉 |
| T4 | MB.surfaceColor → #4 | `FaceHairDiffuse.shader:44,51` | 扩散颜色与 coverage 全 0 |
| T5 | GB.normalDepth → #4 | `FaceHairDiffuse.shader:45,52`（线性深度进 depth 通道） | 深度门失效 |
| T6 | GB.depthTexture → #7 / #10 | `SubjectOutline.shader:102-107`（只有这里读几何深度，用来算高度渐隐的 worldY） | 高度渐隐退化成"无高度"（`ResolveSubjectOutlineHeightFade` 的 `hasHeight=0` → 返回 1，`Composite.shader:495-502`） |
| T7 | #4 → #5 → #6 → #13 | `:721-724`（blur 输入接上一趟输出）、`:981-983` | 颜色/深度链断，脸色扩散与深度门同时失效 |
| T8 | #7 → #8 → #9 → #13 | `:811-812`、`:987-988` | 同上（外扩场断） |
| T9 | #10 → #11 → #12 → #13 | `:898-899`、`:992-993` | 同上 |
| T10 | GB/MB（全部上表输入）→ 每个 source pass | `:685-688`、`:762-765`、`:849-852` | 通不过 RDG 的输入校验 |
| T11 | #1 的写 → #13 的 eyeColor/eyeData 读（眼透/角度/调试开时） | `:972-973`＋`Composite.shader:619,250,275,645` | 眼透读清屏值 |
| T12 | MB.maskId → #13 | `Composite.shader:248`（眼透、前发投影的 sameCharacter）、`:330-331`（前发投影） | 同角色判定失效；另外它的 `_TexelSize` 还是所有语义半径的单位（`:72-75`）→ **不能简单不绑** |
| T13 | 场景几何（材质侧 `HoCharacterCapture` pass）→ #1 / #2 | `:559,588` `CreateRendererList`＋`CaptureCommon.hlsl:56-64`；材质侧深度状态见 §3.3-V1 | 捕获为空 |
| T14 | #13 → 下游（后处理/后续 pass） | `:1066` `resourceData.cameraColor = destination` | 整条链白做 |
| T15 | eyeAngleTable（CPU 上传的 `_lilHoCharacterEyeAngleTable`）→ #13 | `HoCharacterSpecializationRendererFeature.cs:89`＋`Composite.shader:277,665` | 角度修正读到旧表/空表（见 `Documentation~/CharacterSpecialization_EyeReveal.md` §3.1 的坑） |

### 3.2 假依赖（声明了但没采样）

| # | 边 | 依据（为什么是假的） | 什么时候是假的 |
| --- | --- | --- | --- |
| F1 | `source` → #4 | `:685` 声明读；`FaceHairDiffuse.shader:38-54` 的 Frag **从没采样 `_BlitTexture`**（全仓库 `_BlitTexture` 采样点只有 `SubjectOutline.shader:141,156`、`FaceHairDiffuse.shader:90,107`、`Composite.shader:613`，后两个是**各自的 blur pass**） | **恒假** |
| F2 | `source` → #7 | `:762` 声明；`SubjectOutline.shader:91-112` 的 Frag 只采对象通道 + 几何深度，不采 `_BlitTexture` | **恒假** |
| F3 | `source` → #10 | `:849` 声明；同 #7 的 shader | **恒假** |
| F4 | semanticLow/High → #7 | `:766-770` 只按"副本是否存在"声明；而该 pass 自己把 `_HoCharacterSemanticMaskBlurValid` 设成 `ready && settings.semanticMaskBlurSubjectOutline`（`:783-784`），shader 只有 `> 0.5` 才采（`SubjectOutline.shader:43-52`） | `semanticMaskBlurSubjectOutline == false`（默认就是 false，`Effects.cs:30`） |
| F5 | semanticLow/High → #10 | `:853-857` ＋ `:870-871`；同 F4 | `semanticMaskBlurEnhancedOutline == false`（默认 false，`Effects.cs:33`） |
| F6 | semanticLow/High → #13 | `:974-978` 只按 `ready` 声明；采样条件是 `_HoCharacterSemanticMaskOptions.y/z/w`（`Composite.shader:92`），而这三位只由 前发投影/脸色扩散/眼透 三个开关决定（`SemanticMaskBlur.cs:48-52`） | 三个开关全不勾、只有两个轮廓勾了（此时副本是给轮廓用的） |
| F7 | FaceHairDiffuseSourceColor → #13 | `:981` 声明；shader 只有 debug 5 采它（`Composite.shader:681`，声明在 `:63`）——**这张纹理因为这条边要活到最后一趟**，挡住 RDG 别名 | `debugMode != FaceHairDiffuseSourceMask(5)` |
| F8 | eyeColor/eyeData → #13 | `:972-973` **无条件**声明；采样全部有 gate：`_HoCharacterOptions.x <= 0.5` 时 `ResolveEyeRevealMask` 在采样前 return 0（`Composite.shader:241-246`），`ResolveEyeAngleFactor` 在 `strength<=0.0001` 时 return 1（`:268-271`） | `eyeRevealEnabled == false` ∧ 角度修正常数 0 ∧ debug ∉ {1,2,3,16,17} |
| F9 | （不是边，是死 pass）#1 / #2 在眼透全关时仍在图里 | `:557,586` 无条件录制；`AllowPassCulling(false)`（`:572,601`）让 RDG **不能**按"输出没人用"剔掉它；输出唯一消费者 #13 又因为 F8 变成了假读 | 同 F8 |

### 3.3 待验证的边

| # | 边/问题 | 不确定点 | 验证方法（具体到函数与操作） |
| --- | --- | --- | --- |
| V1 | #1 → #2（同一个 captureDepth 的 WAW/RAW：`:570` WriteAll → `:599` ReadWrite） | 两趟画的是**互斥**物体集（Face 位 vs Eye 位，`CaptureCommon.hlsl:59-63`），但材质侧是 `ZWrite On / ZTest LEqual`（`ltspass_opaque.shader:1162-1163`，**外部仓库证据**，其余 pass 变体未逐个核对）→ 若眼物体在脸物体之后画，会被脸写入的深度剔除 | 交换 `:557` 与 `:586` 两段的录制顺序（或临时把 CaptureEye 的深度附件改成 `WriteAll` 不读），看 debug 2（眼睛 Alpha）与 debug 17（表）是否变化：变了 ⇒ 深度互测真起作用，两趟不能合；不变 ⇒ V1 是惰性边，可继续走 §4-K6 |
| V2 | RDG 是否允许 #4/#7/#10 与 #3 并行（F4/F5 断掉之后） | `AllowGlobalStateModification(true)`（`:691,773,860`）到底禁掉了多少重排/合趟自由度 | 读 core 包 `RenderGraphBuilders.AllowGlobalStateModification` 的实现（本工作区没有 core 源码）＋实测：断掉 F4/F5 后看 RDG 视图的 pass 顺序/是否有合并 |
| V3 | #7/#10 的几何深度 tap 是否真的只在部分像素执行（`shader:100-109` 的 `mask > 0.0001`） | 这是**非一致性分支**，wave 内若有一个像素进入就会取整个 quad 的样本；实际 fetch 数要看编译结果 | 编译后看 ISA（或 RenderDoc 的 texture fetch 计数器）对比"轮廓内/外"两块区域 |
| V4 | `AccessFlags.WriteAll` 在这个 fork 里是否等价于"丢弃旧内容 + 不 load" | 本工作区没有 core 源码，无法确证语义 | 读 core 包 `RenderGraphModule` 里 `AccessFlags` 的定义与后端对 attachment load action 的映射；或在 RenderDoc 里看对应 draw 的 loadOp 是 Clear/DontCare/Load |
| V5 | `Blitter.BlitTexture(RasterCommandBuffer, TextureHandle, Vector4, Material, int)` 是否内部 `SetGlobalTexture(_BlitTexture)` | 决定 §4-K2 能不能直接把源纹理换成 `DrawProcedural`（若 `_BlitScaleBias` 不由这条路径设置，UV 会残留上一趟的值） | 读 core 包 `Runtime/Utilities/Blitter.cs` 的 RasterCommandBuffer 重载；实测：把 #4 换成 `DrawProcedural(Matrix4x4.identity, material, 0, Triangles, 3)` 并显式 `SetGlobalVector(_BlitScaleBias, (1,1,0,0))`，与现状逐像素比 |
| V6 | `_HoCharacterSemanticMaskBlurValid` 是**全局量**，被 #7（`:784`）、#10（`:871`）、#13（`:1053`）各写一次 | 顺序被重排时会不会有消费者读到别人的值（现在靠 `AllowGlobalStateModification(true)` 保守兜住） | 把这三处改成"各自 pass 内先写后读"已经是现状，所以验证点是：断掉 F4/F5 并让 #13 与 #7 换序，看轮廓源 mask 是否变化 |
| V7 | 关闭眼透后 debug 16/17 的行为变化是否可接受 | §4-K1 的附带影响（这两个视图要读 eyeData） | 产品/作者确认，或把 debug 也纳入 K1 的门控条件（本文档建议后者） |

### 3.4 全局量造成的隐式顺序（卫生项，不是真依赖）

一趟里 `SetGlobalFloat/Vector/Texture` 的写法（`:580-582`、`:609-611`、`:698`、`:781-789`、`:864-876`、`:1026-1061`）意味着**每个 pass 自己写自己需要的全局量**，所以 pass 之间不靠全局量传递中间结果（好事）；代价是每趟都要 `AllowGlobalStateModification(true)`，而 RDG 一旦看到这个标志就会放弃一部分重排/合并自由度。想拿回这部分自由度，得把参数从全局量搬回 passData（大改，本文档只记录，不推荐现在做）。

---

## 4. 候选优化刀

### 4.0 先写一处"与任务假设不符"的差异（以代码为准）

任务假设是"把最终 composite **融进每支最后一趟**（少一次读 + 一次写，以及每支的 final 纹理对）"。按代码，composite 是**一条有序的混合链**：`source` → 眼透（`:771`）→ 前发投影（`:772-784`）→ 脸色扩散（`:786-791`）→ 增强轮廓雾（`:793-798`）→ 主体轮廓（`:800-820`），且两处雾色都**读原画面** `source.rgb`（`:796,807`）而不是累加值。因此：

- **只有数学顺序上最后的那一支**能一趟吃掉整个 composite（省 1 读 + 1 写 + 1 个 pass）；
- **逐支各融一次**就必须串行累加：每支都要读累加器 + 读原画面 + 写累加器。按 3 支全开算：现状尾部 = 4 读 + 4 写（3 支 final 各 1 写 + 合成 3 读 + 合成 1 写 + 合成 1 读原画面），逐支融合 = 5 读 + 3 写（第一支 1 读原画面 + 1 写，后两支各 +1 读累加器）——**事件数不降反升**，只省下资源（3 张 final + destination 不再需要）。

### 4.1 刀表

| 刀 | 内容（file:line） | 省什么 | 数学是否等价（理由） | 需目视确认什么 | 取决于哪个测量数 |
| --- | --- | --- | --- | --- | --- |
| **K1** | **捕获支按眼透门控**：`eyeRevealEnabled == false`（且 debug ∉ {1,2,3,16,17}、角度修正不会单独生效）时，不录制 `:557-584`、`:586-613`，同时去掉 #13 的 `:972-973` 两条 `UseTexture` 与 `:1047-1048` 的 `SetGlobalTexture` | **2 个几何 pass + 1 次全屏清屏 + 3 张全屏纹理（2×8 B + 4 B = 41.5 MB 分配）+ 41.5 MB 写 + 合成侧 33.2 MB 读**；顺带把整条眼透关键路径从帧里删掉 | **等价**：`_HoCharacterOptions.x <= 0.5` 时 `ResolveEyeRevealMask` 在**采样前** return 0（`Composite.shader:241-246`），`revealMask=0`（`:620`），`lerp(source.rgb, eyeColor.rgb, 0 × eyeAngleFactor)` 恒等于 `source.rgb`（`:771`；`eyeAngleFactor` 被乘 0，不参与结果）；前发投影的 receiver 只多一个 `revealMask=0` 的加法（`:328`） | 眼透关掉时正常画面逐像素一致；**debug 1/2/3/16/17 会失效**（这是行为变化，建议把 debug 一起纳入门控并把该行为写进文档） | 实测 capture 两趟的 ms 占比：占比 ≥ 15% 时本刀是帧级收益；< 5% 时优先级让给 K4 |
| **K2** | 断 3 条 `source` 假读：`:685`、`:762`、`:849` | 只省 **handle/生命周期与图规模**（这 3 条边本来不采样，**不省带宽**）；收益是让三个 source pass 不再被"相机颜色写者"串住，给 RDG 更多排序/别名空间 | **等价**：`FaceHairDiffuse.shader:38-54`、`SubjectOutline.shader:91-112` 的 Frag 全文没有 `_BlitTexture` 采样（§3.2-F1/F2/F3）。⚠️ 实现上不能只删 `UseTexture`：`Blitter.BlitTexture(cmd, data.source, ...)` 会把源纹理绑成 `_BlitTexture`，要么改成 `DrawProcedural` 并**显式**写 `_BlitScaleBias = (1,1,0,0)`（`_BlitScaleBias` 参与顶点 UV 计算，残留值会让采样错位），要么保留绑定 | 三个 source pass 的输出逐像素不变（建议用 debug 5/9/13 抓 A/B） | 无（不需要测量就能做，但收益要用 RDG 的 alias/峰值显存看） |
| **K3** | 断 4 条语义副本假读：`:766-770`、`:853-857`（轮廓侧）、`:974-978`（合成侧） | 生命周期/别名/图规模；带宽为 0（本来没采样） | **等价**：轮廓侧 shader 只在 `_HoCharacterSemanticMaskBlurValid > 0.5` 时采（`SubjectOutline.shader:43-52`），而该值由 pass 自己写成 `ready && 本效果开关`（`:783-784`、`:870-871`）；合成侧由 `_HoCharacterSemanticMaskOptions.y/z/w` 决定（`Composite.shader:92`），三位只由 前发投影/脸色扩散/眼透 决定（`SemanticMaskBlur.cs:48-52`） | 逐个勾/取消"读取抗锯齿掩码"5 个开关，画面与改前一致 | RDG 视图里语义副本的存活区间是否真的缩短（不改带宽就不必测 ms） |
| **K4** | **脸色扩散的 depth 链只留 1 个通道**：`faceHairDepthDesc` 改用 `R16_SFloat`（`:659-663`、`CreateFaceHairDiffuseTextureDesc` `FaceHairDiffuse.cs:119-129`），shader 只写 `.r`（`FaceHairDiffuse.shader:52,114`），composite 的除零基准改用**颜色纹理**的 `.a`（`Composite.shader:381-382` 的 `blurredDepth.a` → `blurredColor.a`） | 3 张 depth 纹理**分配 49.8 MB → 12.4 MB**；写 49.8 → 12.4 MB；两趟 blur 的 depth 采样**每 tap 字节数 8 B → 2 B**（每趟 41 tap），合成侧 depth 读 16.6 → 4.2 MB | **等价（逐位）**：源 pass 写 `color = (rgb·sm, mask)`、`depth = (nd·mask, 0, 0, mask)`，两者 alpha 是**同一个 `mask` 局部量**（`FaceHairDiffuse.shader:50-52`，`half4` 同一格式存储）；blur 对两张纹理用**同一组权重、同一顺序**累加（`:107-108`）→ `depth.a == color.a` 逐位相同，且 `.g/.b` 恒 0；`R16_SFloat` 与 `RGBA16F` 的单通道精度同为 10 位尾数，收紧单通道**逐位不变** | ① 脸色与前发交界不能出现 1px 环（因为除零基准换了一个纹理，若哪天两者不再同源就会露馅）；② `R16_SFloat` 在目标平台的 `GraphicsFormatUsage.Render` + filterable（代码里已经有 `IsColorFormatUsable` 的模式可复用，`:1214-1217`） | 脸色扩散那一支的 ms + RenderDoc 里该支的 texture fetch 字节：depth 采样是这一支里 fetch 量最大的一项（2×41 tap × 2.07 Mpx） |
| **K5** | 合成趟的绑定收紧：`:981`（FaceHair 源色，只有 debug 5 用）改条件；`:972-973` 与 `eyeRevealEnabled` 对齐 | 生命周期/别名（F7/F8）；`FaceHairDiffuseSourceColor` 不再压住整个支链的存活区间 | **等价**：采样点由 `Composite.shader:681`（debug 5）与 `:241-246` 的 gate 决定 | 每个 debug 视图仍能正常显示（逐个数一遍 1..17） | 无 |
| **K6** | **捕获两趟合一**：一次 `DrawRendererList` + 按物体自己的 object mask 位决定 mode（`CaptureCommon.hlsl:36-43,56-63` 里 objectMask 已经是逐物体数据），去掉第二个 pass 与 `:586-613` | 1 个 pass、1 次几何顶点链（现状两趟都对全部物体跑顶点链）、1 次附件写序列；不动片元总量 | **待验证**（§3.3-V1）：两趟共享 captureDepth 且材质侧 `ZWrite On / ZTest LEqual`，合一后 face/eye 的深度互测顺序会变成一次排序的结果 | 先做 V1 的换序实验；再做 debug 1/2/3/17 的 A/B（尤其眼睛被脸皮遮挡的像素） | capture 两趟的 ms：如果 capture 是帧内大头，这刀的收益最大 |
| **K7** | 清屏方式：`ClearCaptureTargets`（`:1088-1098`）里的全屏三角形（`CaptureClear.shader:44-59`）换成 `RTClearFlags.All`（深度已经在 `:1090` 清过了） | 1 次满屏 2 MRT 写（33.2 MB）+ 1 次 draw | **等价**（两只都写 0；但**为什么当初用 shader 清未在代码里写明**，属于未确认，见 §6-9） | debug 1/2 无残留；旧内容不留 | 清屏在 capture 里的占比（通常极小，属"顺手做"） |
| **K8** | 资源卫生：`CreateTextureDesc` 的 `clearBuffer = true`（`:1132`）与 captureDepth 的 `clear = true`（`:545`）在"第一趟就全屏覆写"的纹理上关掉；`AccessFlags.WriteAll` 与 load/discard 的用法统一；`CapturePassData` 里 3 个死字段（`:325-327`）删除 | 图的规模（少一批 clear 节点/多余 load）、语义清晰；**不省带宽**（fast clear 很便宜） | 需要先确认 `AccessFlags.WriteAll` 语义（§3.3-V4）；确认后是等价改法 | renderScale = Half/Quarter 时纹理比相机小，全屏覆写仍必须全覆盖（抓一次图确认无残留） | 无 |
| **K9** | **scratch 池化**：把 ping-pong 的 temp/source/final 统一走一个 scratch 池 | **只省 handle / 峰值显存 / 图的规模；不省带宽，也不省 GPU 时间** | 等价（不动算法）。注意：RDG 自身就会对 transient 纹理做生命周期别名，池化能拿到的是"handle 数与名字数"和"图里资源条目数"，不是流量 | 无（不是视觉项） | RDG Resource List 里的 transient 峰值：池化前后对比；若峰值本来就被别名压到很小，这刀收益≈0 |
| **K10** | **composite 融进"排序最后的那一支"的最后一趟**（见 §4.0） | 1 读 + 1 写 + 1 个 pass + 该支 final 纹理 + destination 不再需要单独存在（可别名） | **只在 `renderScale = Full` 且只启用单支时严格等价**：合成趟与支链纹理同分辨率时，像素中心正落在纹素中心，双线性重建返回存下来的那个值，与在同一 uv 上重算磁盘卷积逐位一致；`renderScale = Half/Quarter` 时合成趟是满分辨率、支链是半分辨率，融合会把"双线性放大"换成"满分辨率重算"，**不等价**（半径与相位都会变） | 轮廓法线的 `ddx/ddy`（`Composite.shader:525`）融合后改由同一趟邻域求导，边缘是否一致；`resourceData.cameraColor`（`:1066`）指向的必须是下游拿到的那张 | 只有单支启用的场景占比 + 合成趟的 ms |
| **K11** | 合成趟眼透的 81 tap：把"膨胀"落成一张预计算场（PointClamp，`Composite.shader:198-216` 的 `texel = MetadataTexelSize()*radiusPx` 改为先算 `D=max3x3(eyeData.r)` 再对 9 个偏移做加权和 `:228-238`） | tap 81 → 18，但**多 1 趟（1 读 + 1 写）** | **默认参数下逐位等价**：`max` 不引入舍入（8 个 16F 值的 max 仍是 16F 可表示值），权重与求和顺序不变（`:229-238`）；**非整数**羽化/扩张（默认 1.0 / 2.0，是整数）时膨胀支撑会因取整差最多 1 个 texel → 需目视确认 | 极低对比处的眼透边缘（`eyeRevealStrength` 默认 0.05，本来就很淡）：A/B 抓图逐像素差 | **composite 是不是采样瓶颈**：如果不是，多一趟不划算 |

优先级建议：**K1（帧级最大结构性浪费，且可证等价）→ K4（可证等价的带宽刀，随参数常开）→ K3/K5/K2（假依赖与生命周期）→ K6/K10（收益大但要先做 V1/先测单支占比）→ K7/K8/K9（卫生与显存）。**

### 4.2 明确否掉的刀（避免以后有人再提）

| 想法 | 否掉的理由 |
| --- | --- |
| 前发投影的"柔化 + 扩散"也照 K11 预计算 | `SampleSemanticSpread` 走的是 **`sampler_LinearClamp`**（`Composite.shader:99,105,108`），每个 box tap 的 3×3 max 是**双线性重建后**的 max，含亚像素相位信息，不能由一张点采样场复现；而且 box 的 tap 间距 `side/taps` 不总是整数（`:154`，例如 柔化 4 → side=9 / taps=8）。只在"间距为整数 + 只算中心相位"时才可能等价，属于改观感风险，不做 |
| 降分辨率（renderScale 到 Half/Quarter） | 硬约束 C1，且合成趟本来就是满分辨率（`:906`），半分辨率只会把支链变成"放大"路径，观感变化不可控 |
| 换更省的模糊核（例如把 64 tap 的 golden-angle disk 换成可分离/更少 tap） | 硬约束 C2，且 `C4`：轮廓半径大（柔化雾气模式尤其），同一趟内减少 tap 就是降质量 |
| 把 eyeData / eyeColor 收紧到 8 bit | **会改判定**：`SameCharacter` 的容差就是 `0.5/255`（`Composite.shader:79`），而 `eyeData.b` 承载的就是 `byte/255` 的角色 ID（`CaptureCommon.hlsl:76`）；降到 8 bit 会让 ID 落在容差边界上 |
| 把中间 16F 全部收紧到 LDR | 会**截断 HDR 中间值**：`BlendOutlineFog`（`Composite.shader:597-606`）的 `max(baseColor, screen + hdrLift)` 与加色混合都会用到 >1 的值，中途截断等于改观感 |
| 把 captureDepth 直接删掉 | 材质侧是 `ZWrite On / ZTest LEqual`（外部仓库证据），删了就是改遮挡结果；只有 §3.3-V1 证明深度互测不起作用时才谈 |

---

## 5. 后续可选：pass 预算 sim 接口草案（**待实现**）

目标：做一个**纯 C# 检查器**（不依赖 UnityEngine、不启动渲染），输入是这一帧的开关/模式/半径/迭代常量，输出是 pass 列表 + 每趟全屏读写 + 合计 MB；用来在改代码前先算"这把刀值多少"，也用来给 CI/PlayMode 测试加断言。
**待实现**，本工作区当前没有任何检查器（已 grep 全仓库 `Checker|预算|Budget`，只有无关命中）。

```csharp
// 待实现：建议放 Tests 或 Editor 侧的纯 C# 文件（不引用 UnityEngine.Rendering）
public enum HoCharacterSimDebugMode { Off, EyeColor, EyeAlpha, EyeRevealMask, /* ... 与 HoCharacterSpecializationDebugMode 对齐 */ }

public readonly struct HoCharacterSimInput
{
    // 效果开关（来自 HoCharacterSpecializationSettings / Volume）
    public bool EyeReveal, HairDropShadow, FaceHairDiffuse, SubjectOutline, EnhancedOutline;
    public HoCharacterSimDebugMode DebugMode;

    // 语义抗锯齿副本（SemanticMaskBlur.cs:17-25）
    public bool BlurHairShadow, BlurFaceHairDiffuse, BlurEyeReveal, BlurSubjectOutline, BlurEnhancedOutline;
    public float SemanticMaskBlurRadiusPixels;      // 默认 1.0（Effects.cs:18）

    // 影响 tap 数的半径（composite 侧）
    public float HairShadowSoftnessPixels;          // 默认 2.0（Effects.cs:104）
    public float HairShadowSpreadPixels;            // 默认 0.0（Effects.cs:107）
    public float EyeRevealFeatherPixels, EyeRevealDilationPixels; // 1.0 / 2.0（Effects.cs:43,46）

    // 尺寸与格式
    public int CameraWidth, CameraHeight;
    public int RenderScaleDivisor;                  // (int)HoCharacterRenderScale：1 / 2 / 4
    public bool CameraColorIs16F;                   // 决定 source/destination 的 B/px

    // 迭代与核常量（必须与 shader 常量逐一对齐，写在注释里）
    public int FaceHairBlurIterations;      // = 2  (HoCharacterSpecializationRendererFeature.cs:289)
    public int OutlineBlurIterations;       // = 2  (:290)
    public int FaceHairTaps;                // = 40 (HoCharacterFaceHairDiffuse.shader:73)
    public int OutlineTaps;                 // = 64 (HoCharacterSubjectOutline.shader:130)
    public int SemanticBlurMaxTapsPerAxis;  // = 8  (HoCharacterSpecializationShaderConstants.cs:78)
    public int HairShadowMaxTapsPerAxis;    // = 8  (HoCharacterSpecializationComposite.shader:143)
}

public readonly struct HoCharacterSimPass
{
    public string Name;          // 与 §1.1 的 RDG pass 名逐字一致
    public int Reads, Writes;    // 满屏面数
    public double ReadMB, WriteMB;
    public int TapsPerPixel;     // 0 = 不适用（几何 pass）
    public string EnterCondition; // 进图条件的可读文本（便于失败时报出来）
}

public readonly struct HoCharacterSimResult
{
    public IReadOnlyList<HoCharacterSimPass> Passes;
    public int PassCount, FullScreenReads, FullScreenWrites;
    public double ReadMB, WriteMB, TotalMB;
    public int TransientTextureCount, TransientPeakMB; // 只算分配，不算别名（见 §4-K9 的定位）
    public IReadOnlyList<string> NotModeled;           // 必须显式列出没建模的东西
}
```

建议的断言形式（**待实现**，数值按 §1/§2 的推定值写，改代码后应同步）：

```text
// 1) 不变量：与效果开关一致的趟数
Assert(PassCount == 3 + (blurTextures ? 1 : 0)
                 + 3 * (FaceHairDiffuse ? 1 : 0)
                 + 3 * (SubjectOutline ? 1 : 0)
                 + 3 * (EnhancedOutline ? 1 : 0));
// 2) 默认参数（眼透+前发投影+语义抗锯齿前 3 项）
Assert(PassCount == 4 && FullScreenReads == 9 && FullScreenWrites == 9);
Assert(TotalMB <= 300 * W * H / (1920.0 * 1080.0));
// 3) 全开上界
Assert(PassCount <= 13 && FullScreenReads <= 36 && FullScreenWrites <= 21);
Assert(TotalMB <= 1000 * W * H / (1920.0 * 1080.0));
// 4) K1 落地后的新不变量
Assert(!EyeRevealAndNoEyeDebug || Passes.None(p => p.Name.Contains("Capture")));
// 5) 每一趟都必须有进图理由
Assert(Passes.All(p => p.EnterCondition.Length > 0));
// 6) 未建模项必须显式声明，避免把估算当实测
Assert(NotModeled.Contains("cache-reuse") && NotModeled.Contains("material-capture-shading"));
```

---

## 6. 未确认清单

| # | 未确认的点 | 为什么没确认 | 验证方法 |
| --- | --- | --- | --- |
| 1 | 捕获两趟**每像素 tap 数**与逐物体成本 | 真正干活的是材质侧 pass（`LightMode = HoCharacterCapture`），不在本工作区；片元走的是 lilToon 主色链（`lil_pass_hocharacter_capture.hlsl:53-170`，**外部仓库**） | 在 lilToon 仓库读 `Assets/lilToon/Shader/Includes/lil_pass_hocharacter_capture.hlsl` 与各 `ltspass_*.shader` 的该 pass 块，按材质功能开关（主色 1/2/3 层、法线贴图、视差、dissolve、dither）数采样；或直接看 RenderDoc 的该趟 fetch 计数 |
| 2 | `AccessFlags.WriteAll` / `clearBuffer` / attachment load-store 的确切语义 | 本工作区没有 core 包源码（`com.unity.render-pipelines.core` 17.3.0 只在工程 `Library/PackageCache` 里） | 读 core 的 `Runtime/RenderGraph/...`（`AccessFlags`、`RenderGraphPass.SetRenderAttachment`）与 `UniversalRenderer.CreateRenderGraphTexture`；或 RenderDoc 看 loadOp |
| 3 | `Blitter.BlitTexture(RasterCommandBuffer, ...)` 内部是否 `SetGlobalTexture(_BlitTexture)`、是否设置 `_BlitScaleBias` | 同上（core 的 `Blitter.cs` 不在工作区） | 读 `Runtime/Utilities/Blitter.cs` 的 RasterCommandBuffer 重载；再按 §3.3-V5 做替换实测 |
| 4 | composite 的 destination 继承了 `source` 的 desc（`:906-911`），**MSAA 相机下 `_lilHoCharacterCompositeColor` 的 sample count 与下游期望是否一致** | 需要 URP 内部对 `activeColorTexture` 的 MSAA/解析约定，工作区里没有 | RDG 视图 / RenderDoc 看该纹理 sample count，与后续后处理的输入是否同一张、sample count 是否相同 |
| 5 | `_HoCharacterSemanticMaskBlurValid` 这个**全局量被 3 趟轮流写**（`:784`、`:871`、`:1053`）时，RDG 的重排是否被 `AllowGlobalStateModification(true)` 完全挡住 | 依赖 core 的实现（同 #2/#3） | 读 `AllowGlobalStateModification` 的实现＋实测换序（§3.3-V6） |
| 6 | #7/#10 里"只有 `mask>0.0001` 才采几何深度"（`SubjectOutline.shader:100-109`）在 wave 内是否真的省下 fetch | 非一致性分支的实际行为取决于编译 | 编译后看 ISA，或 RenderDoc 对比轮廓内/外的 fetch 计数 |
| 7 | 13 趟同名 marker 在 Unity GPU Profiler 里是否真的无法区分（是否按名字聚合） | 属工具行为，未实测 | 关掉三支后再看 GPU 模块：只剩 4 个同名条目就证明是聚合显示 |
| 8 | 动态分辨率下 CS 纹理（抄了 `useDynamicScale`，`:1137`）是否跟随缩放；destination 从 `source` 抄 desc 是否也跟着 | 工作区里看不到 URP 的 dynamic scale 约定 | 开 dynamic resolution 抓帧看纹理尺寸；同时看 RDG 视图里纹理的实际尺寸 |
| 9 | `ClearCaptureTargets` 为什么用全屏三角形清颜色而不是 `RTClearFlags.All`（`:1088-1098`） | 代码里没写原因（可能是绕某个 RDG 限制，或与兼容路径统一） | 问作者/看提交历史；再按 §4-K7 做替换 A/B |
| 10 | 关掉眼透后 debug 16/17 失效是否可接受（§4-K1 的附带影响） | 属产品取舍 | 由作者确认；本文档建议把 debug 一起纳入门控条件 |
| 11 | `_HoMetadataBufferActive <= 0.5` 的早退分支（`Composite.shader:614-617`）是否可能成立 | 合成趟在 `:1061` 自己把它设成 1，本仓库里没有别的写入者；但外部（其它 feature/材质）是否也写它未确认 | 全仓库/相关仓库 grep `ActiveId` 的写入点；或抓帧看该分支是否进入 |
| 12 | 相机 HDR 关闭时 destination 被强制成 `R16G16B16A16_SFloat`（`:910`、`:1169-1176`）与相机颜色的编码（线性/sRGB）是否一致 | 取决于 URP 对 LDR 相机颜色的处理 | 在 URP asset 里关 HDR，抓帧对比同一画面亮度/后处理输入格式 |

---

## 附：本文档与前文的关系

- 眼透的语义与角度修正见 `Documentation~/CharacterSpecialization_EyeReveal.md`（本文档只补 RDG 结构）。
- 脸色扩散的设计与参数意图见 `Documentation~/CharacterSpecialization_FaceHairDiffuse.md`（本文档 §4-K4 只动存储通道数，不动它的核、半径与两趟结构）。
- 本文档所有"事件数/MB"都是**按代码结构推出的估算**，不是实测 ms；任何性能结论都必须回到 §0 的测量口径。

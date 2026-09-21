# Ho-CharacterSpecialization（CS）RenderGraph 逐趟清单与优化方案

> 状态：**现行（2026 文档审核按 AC 链路重校）**。对象是 `Runtime/CharacterSpecialization/` 的 RDG 主路径（`HoCharacterSpecializationPass.RecordRenderGraph`，`HoCharacterSpecializationRendererFeature.cs`）。
> 本文是**清单 + 方案**，不是施工单；行号以当前工作区为准。
> **本轮重校的最大变化**：`SemanticMask Blur` 那一趟与整套“读取抗锯齿掩码”开关**已被删除**——角色语义位现在由 **AC** 提供（`HoCharacterObjectSemantic.shader` 读 `_HoACSelection0..3` 的 8 条 lane 与覆盖率，按历史 `objectCustom` 布局烤成 low/high 两张位平面），抗锯齿来自 **AC lane 的覆盖率**而不是另做一趟模糊。输入侧也从 `MetadataBuffer` 换成 **AC（语义/身份）+ GB（几何）**。

## 硬约束（前置，不推翻）

| # | 约束 |
| --- | --- |
| C1 | **不降分辨率**：半/四分之一分辨率中间纹理一律不采用；所有候选刀必须在 `renderScale = Full` 下成立（`HoCharacterRenderScale` 只作为“现状可配项”记录） |
| C2 | **不降质量**：不换更省的模糊核、不做改变观感的近似；每把刀都要给“数学是否等价”的判据，给不出就标待验证 |
| C3 | 允许的刀只有**纯架构**：合趟、去冗余工作、断假依赖、资源卫生 |
| C4 | **宽/柔化雾气填充模式保持原样**（`HoCharacterSubjectOutlineFillMode.SoftFog`）；它的开销在主体轮廓那一趟的采样里，不额外增加 pass |

## 1. 这一帧的 pass 清单

计数口径：**一个 RDG pass = 一次 `AddRasterRenderPass`**。捕获两趟是几何 pass（`DrawRendererList`），其余是全屏 raster pass。

> 默认值真源：运行时效果值走 Volume（`HoCharacterSpecializationEffects.cs`）；`HoCharacterSpecializationSettings.cs` 里的同值默认只服务 feature 资产（**改默认值要改两处**，属卫生项）。

### 1.1 主表（全开 = 13 趟；默认 = 4 趟）

| # | RDG pass 名 | 读 | 写 | 进图条件 |
| --- | --- | --- | --- | --- |
| 1 | `CaptureFace` | 0（纯几何） | eyeColor + eyeData + captureDepth（`WriteAll` + 清屏） | `needsFaceCapture` = `RequiresCharacterCapture` ∨ `requiresFaceHairDiffuseTextures` |
| 2 | `CaptureEye` | 0（纯几何） | 同上 3 面（`ReadWrite`，无清屏） | `needsEyeCapture` = `RequiresCharacterCapture`（只有眼透那组条件；脸色扩散不拉起它） |
| 3 | `ObjectSemantic` | **AC Selection0..3（4 面）** | ObjectSemantic0_3 + ObjectSemantic4_7（MRT×2） | **无条件**（前提是 AC Selection 池在 + 打包材质在；不成立时整支 no-op） |
| 4 | `FaceHair Source` | ObjectSemantic0_3 + GB normalDepth + **eyeColor（受光脸）**（3 面） | FaceHairDiffuseSourceColor + SourceDepth | `requiresFaceHairDiffuseTextures` ∧ 材质 |
| 5 | `FaceHair FastGaussian 1` | SourceColor + SourceDepth | TempColor + TempDepth | 同 #4 |
| 6 | `FaceHair FastGaussian 2` | TempColor + TempDepth | FaceHairDiffuseColor + Depth | 同 #4（迭代 2 次） |
| 7 | `SubjectOutline Source` | ObjectSemantic0_3 + 4_7 + GB depthTexture（3 面） | SubjectOutlineSource | `requiresSubjectOutlineTextures` ∧ GB depth ∧ 材质 |
| 8 | `SubjectOutline FastGaussian 1` | Source | Temp | 同 #7 |
| 9 | `SubjectOutline FastGaussian 2` | Temp | SubjectOutlineTexture | 同 #7 |
| 10 | `EnhancedOutline Source` | 同 #7（3 面） | EnhancedOutlineSource | `requiresEnhancedOutlineTextures` ∧ GB depth ∧ 材质 |
| 11 | `EnhancedOutline FastGaussian 1` | Source | Temp | 同 #10 |
| 12 | `EnhancedOutline FastGaussian 2` | Temp | EnhancedOutlineTexture | 同 #10 |
| 13 | `Composite` | source、**OB 身份层 0**（`HoAC_Layer0Group`）、GB normalDepth、eyeColor、ObjectSemantic0_3/4_7，＋ `eyeData`（仅 `needsEyeCapture`）、＋ FaceHair 源色（仅 debug 5/18）、＋ FaceHair Color/Depth（若脸色扩散在）、＋ 两组轮廓 Source/Texture（若在） | `_lilHoCharacterCompositeColor`（= 新相机颜色） | **无条件**（整链前提：非 backbuffer、有相机颜色、AC Selection 池与语义材质可用、GB normalDepth 可用，且需要几何深度时 GB depth 可用） |

**趟数结论**：全开 **13 趟**；默认参数（眼透 + 前发投影开，三支全关）**只有 4 趟** = `CaptureFace` + `CaptureEye` + `ObjectSemantic` + `Composite`。其中 `ObjectSemantic` 与 `Composite` **无条件**进图，`CaptureFace/Eye` 由眼透那组条件（或脸色扩散链）拉起，其余 9 趟由三个效果的开关或它们的 debug 视图拉起。

**tap/px（按现有核，K4/K11 若落地会变）**：脸扩散模糊 = 40 samples × 2 张纹理 + 中心 = **82**；轮廓模糊 = 64 + 中心 = **65**；合成趟默认 ≈ **126**，随参数可变（前发投影柔化 8 × 扩散 ×9 → 单支可到 576，上限 ~700+）。

### 1.2 目标纹理与输入

- CS 自己的非深度纹理统一走 `CreateTextureDesc`：分辨率 = **相机目标 ÷ (int)renderScale**（所以只有 Full 才与相机同尺寸）、`MSAASamples.None`、Bilinear/Clamp；`_lilHoCharacterCompositeColor` 例外——desc 抄 `source`，尺寸=相机目标、16F、`clearBuffer = false`。
- eyeColor / eyeData / 各支中间色 = `R16G16B16A16_SFloat`；ObjectSemantic 两张 = `GetObjectSemanticGraphicsFormat()`（R8G8B8A8 UNorm 级）。
- **输入侧（CS 只读，不创建）**：AC 的 Selection 池（`_HoACSelection0..3`、`_HoACLanes`）；AC 的 OB 身份层 0（合成趟的 `HoAC_Layer0Group`）；GB `normalDepthTexture` 与 `depthTexture`。**CS 不再读任何 MB 纹理**（MB 已整块删除）。

> **单位陷阱（代码事实）**：合成趟里“像素半径”用的是**它自己的**屏幕 texel；三支模糊用的是各自 `_BlitTexture_TexelSize`（= CS 纹理的 texel）。CS / GB / OB 各自的 renderScale 是**独立设置**（默认都是 Full），任一处改成 Half，同一个“像素”数字就指向不同物理尺寸——比对画面时不要误判成 bug。

## 2. 依赖真伪

**真依赖**：`ObjectSemantic` 的两张位平面 → 合成趟与三条 source 趟（`SampleSemanticBit`/`SampleObjectCustomChannel`，点采样）；GB normalDepth → 脸色扩散的深度门；GB depthTexture → 两条轮廓的“高度渐隐”（只有这里读几何深度）；#4→#5→#6→#13、#7→#8→#9→#13、#10→#11→#12→#13（支链）；**#1 的 eyeColor → #4**（受光脸）与 **#1 的 eyeColor/eyeData → #13**；OB 身份层 0 → 合成趟的同角色判定；`#13 → resourceData.cameraColor`（下游）。

**假读（声明了但 shader 从不采样）**：
- 三个 source 趟都不采 `_BlitTexture`（相机颜色），因此**不再声明**这条读（K2 已落地）；
- 合成趟的 FaceHair **源色**只在 debug ∈ {5,18} 时被采（K5 已把声明收窄到这两个 debug）；
- 合成趟的 `eyeData` 只在 `needsEyeCapture` 时有采样点（K1 已跟着门控）；**但 `eyeColor` 不是假读**——`Frag` 开头就无条件采它（debug 1 直出、`:823` 的 `lerp` 目标色），必须保留绑定。

**两条已核实的 RDG 事实**：① `AllowPassCulling(false)` 是**冗余**写法（core 里 `AllowGlobalStateModification(true)` 内部就会关剔除），本 feature 的 pass **永不**被 RDG 自动剔除——所以断假读**只缩短存活区间、不减少 pass**；② RDG **不重排** pass，执行顺序 = 录制顺序。

## 3. 已经落地的刀（等价性已逐条对着 shader/core 复核）

| 刀 | 状态 | 内容 |
| --- | --- | --- |
| **K2** 断 source 假读 | ✅ | 三个 source pass 删掉 `UseTexture(source, Read)`，并把 `Blitter.BlitTexture` 换成 `DrawProcedural` + **显式** `SetGlobalVector(_BlitScaleBias, (1,1,0,0))`（顶点 UV 依赖它，残留值会让采样错位） |
| **K3/F4/F5/F6** 断语义副本假读 | ✅（语义副本本身已被 AC 方案取代） | 读声明改成“本效果真的会采它”才声明 |
| **K5/F7** 收窄 FaceHair 源色 | ✅ | 声明收窄到 `debugMode ∈ {FaceHairDiffuseSourceMask(5), FaceHairDiffuseCapturedFaceLit(18)}` |
| **K1/F8/F9** 捕获支按眼透门控 | ✅ | `needsFaceCapture`/`needsEyeCapture`/`needsCharacterCapture` 三个具名量；两趟捕获、`eyeData`、`captureDepth` 跟着门控；**`eyeColor` 的读声明与绑定保留**（门控掉捕获后由描述符的 `clearBuffer` 让 RDG 显式清 0，不引入 NaN/残留） |
| **K8** 清屏/`AccessFlags` 卫生 | ⏭ 刻意跳过 | `AccessFlags.WriteAll` 在 core 里**就是** `Write \| Discard` → 再加 `Discard` 是空操作；只有 `CaptureEye` 的 `ReadWrite` 不是 `WriteAll`，而它**不能** Discard（要保留 `CaptureFace` 的结果）；`clearBuffer = true` 也必须保留 |

## 4. 还没做的刀（按投入排序）

| 刀 | 内容 | 触发条件 |
| --- | --- | --- |
| **K4** | 脸色扩散的 depth 链只留 1 个通道（`R16_SFloat`），合成侧除零基准改用它自己颜色的 `.a` | **逐位等价**（源趟写 `depth.a == color.a`，blur 同权重同顺序；单通道精度不变）；脸色扩散支占比 ≥10% 时立刻做，省 3 张 depth 纹理的分配与写 |
| **K6** | 捕获两趟合一（一次 `DrawRendererList`，按逐物体 mask 位选 mode） | 先做 §5-V1 的换序实验：两趟共享 `captureDepth` 且材质侧 `ZWrite On / ZTest LEqual`，合一后深度互测顺序会变成一次排序的结果 |
| **K10** | composite 融进“排序最后的那一支”的最后一趟 | 只在 `renderScale = Full` 且**只启用单支**时严格等价（半分辨率下融合会把“双线性放大”换成“满分辨率重算”，半径与相位都会变） |
| **K11** | 合成趟眼透的 81 tap 改成“先算预计算场”（81 → 18 tap，但多 1 趟） | 只在 composite 被证明是采样瓶颈时才划算；非整数羽化/扩张会有最多 1 texel 的支撑差 |
| **K7/K9** | 清屏换 `RTClearFlags`；scratch 池化 | 卫生项——**不省带宽**（fast clear 很便宜），池化只省 handle/图规模，峰值显存本来就靠 RDG 别名压着 |

## 5. 明确否掉的刀

| 想法 | 否掉的理由 |
| --- | --- |
| 前发投影的“柔化 + 扩散”也预计算 | `SampleSemanticSpread` 走 `sampler_LinearClamp`，每个 box tap 的 3×3 max 是**双线性重建后**的 max，含亚像素相位；且 box 的 tap 间距不总是整数 → 属改观感风险 |
| 降分辨率（Half/Quarter） | 硬约束 C1；合成趟本来就是满分辨率，半分辨率只会把支链变成“放大”路径 |
| 换更省的模糊核 | 硬约束 C2 + C4（半径大时减少 tap 就是降质量） |
| eyeData/eyeColor 收紧到 8 bit | **会改判定**：同角色容差就是 `0.5/255`，而 `eyeData.b` 承载 `byte/255` 的角色 ID |
| 中间 16F 全部收紧到 LDR | 会截断 HDR 中间值（雾气的 `max(baseColor, screen + hdrLift)` 与加色混合都用到 >1） |
| 删掉 `captureDepth` | 材质侧是 `ZWrite On / ZTest LEqual`，删了就是改遮挡结果；只有 V1 证明深度互测不起作用时才谈 |

## 6. 测量口径（先做完这一步，再决定动哪把刀）

| 工具 | 取什么 | 注意 |
| --- | --- | --- |
| Unity GPU Profiler | 逐 marker 的 GPU 时间 | **本 feature 的 13 趟共用同一个 sampler 名 `Ho-CharacterSpecialization`** → 你会看到一串同名条目，只能按顺序数（先关三支确认只剩 4 个 marker） |
| Render Graph Viewer | pass 列表与顺序、每趟资源读写、**transient 显存峰值**、资源存活区间 | 唯一能把 pass 名与 §1.1 逐行对上、并直接看到假依赖是否缩短存活区间的地方 |
| RenderDoc | 每趟 region 名、逐 pass fetch/带宽、attachment 的 load/store action | 验证 `AccessFlags` 语义、destination 的 sample count、“某趟到底采了哪些纹理”；本机有 `renderdoc-mcp` 可直接走 |
| 编辑器外 | 同机同分辨率、固定相机/角色/参数，跑 3 次取中位数 | 参数必须一起记录（合成趟 tap 随参数变） |

**要带回的数**：捕获两趟 ms、三支各自的 source/blur 两趟 ms、composite ms（连同当时的半径参数）、总帧时间 + 分辨率 + 三个 renderScale、RDG transient 峰值（CS 资源 vs 整帧）。

> **口径警告**：不要用“每张纹理算一次满屏读”的事件数当 GPU 时间——65 tap 的模糊是同一批纹素被取 65 次（绝大多数命中 L1/L2），捕获两趟的写只发生在角色覆盖面积上，相机颜色格式也由 URP 决定。任何性能结论都必须回到这张表。

## 7. 坑

1. **13 趟同名 marker**：Profiler 里按名字聚合，没法区分；只能按顺序数或改用 RDG 视图/RenderDoc。
2. **捕获两趟不是全屏 pass**：它们是 `DrawRendererList`，片元靠 `clip()` 按物体自己的 Face/Eye 位决定画不画，所以**两趟都会对全部被捕获物体跑完整顶点链**；`CaptureFace` 还额外画一次清屏。
3. **`captureDepth` 在本仓库里没有任何采样者**：它唯一用途是捕获两趟自己的深度测试（材质侧状态）。
4. **`_BlitScaleBias` 不能省**：顶点 UV 由 `bias + uv * scale` 算出，残留值会让整张遮罩错位——这就是 K2 必须显式设 `(1,1,0,0)` 的原因。
5. **语义来源只有一个**：合成与两条轮廓读的都是 `ObjectSemantic` 那两张位平面（AC lane 覆盖率烤出来的），不要再往 MB/`custom0` 那类通道上找。
6. **门控掉捕获后 `eyeColor` 仍被无条件采样**：靠描述符的 `clearBuffer` 保证它是 0，不是垃圾。
7. **全局量写法**：每趟自己写自己需要的全局量（好事：pass 之间不靠全局量传中间结果），代价是每趟都要 `AllowGlobalStateModification(true)`，RDG 因此放弃一部分重排/合并自由度。
8. **改默认值要改两处**（Volume/Effects 与 feature Settings 各一套默认）。

## 8. 未确认清单

1. 捕获两趟**每像素 tap 数**与逐物体成本（真正干活的是 lilToon 材质侧 `LightMode = HoCharacterCapture`，不在本工作区——去 lilToon 仓库数采样，或看 RenderDoc）。
2. 合成 destination 抄 `source` desc 后，**MSAA 相机下 sample count 与下游期望是否一致**。
3. 动态分辨率下 CS 纹理（抄了 `useDynamicScale`）与 destination 是否跟随缩放。
4. `ClearCaptureTargets` 为什么用全屏三角形清颜色而不是 `RTClearFlags.All`（代码里没写原因）。
5. 关掉眼透后 debug 16/17 失效是否可接受（K1 的附带影响；建议把 debug 一起纳入门控）。
6. 相机 HDR 关闭时 destination 被强制成 16F，与相机颜色的编码（线性/sRGB）是否一致。

## 9. 后续可选：pass 预算 sim（**待实现**）

目标是一个**纯 C# 检查器**（不依赖 UnityEngine、不启动渲染）：输入开关/模式/半径/迭代常量，输出 pass 列表 + 每趟全屏读写 + 合计 MB，用来在改代码前先算“这把刀值多少”，并给 CI/PlayMode 测试加断言。当前工作区**没有任何检查器**。断言形态应是：趟数与开关一致、默认帧的上界、全开上界、K1 落地后的“眼透关且无眼 debug 时不得有 Capture 趟”、每趟必须有进图理由、未建模项（cache-reuse / material-capture-shading）必须显式声明。

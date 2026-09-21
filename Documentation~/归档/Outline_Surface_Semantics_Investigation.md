# lilToon 描边与 ScreenProcess 景深：结论与坑

> 状态：**已收敛（2026 文档审核）**。本文原为“现况调查记录”，已压缩为「结论 + 踩过的坑 + 被证伪的假设」。
> 文中的 `HoMetadataBuffer` 已在 R6／R7 删除：对象／材质语义归 **OB + AC**，surface 族归 **SB**，屏幕几何仍由 **GeometryBuffer** 提供。当前架构以 `架构优化/Ho-*.md` 与 `CHANGELOG.md` 为准。

## 1. 结论

1. **描边外扩只发生在 forward outline pass。** `lilCalcOutlinePosition()`（`lil_vert_outline.hlsl`，由 `LIL_OUTLINE` 触发）把描边实现为“由原始网格派生出的屏幕可见外壳”，不是独立 Renderer／mesh。lilToon 所有 outline 变体（`lts*_o`、`ltsl*_o`、`ltsmulti_o`）都是这个结构，不是偶发现象。

   | pass | 是否外扩 |
   | --- | --- |
   | `FORWARD_OUTLINE`（`LIL_OUTLINE`） | ✅ 外扩 |
   | `FORWARD`、`DepthOnly`、`DepthNormals` | ❌ 原始网格 |
   | `HoGeometryBuffer`、`HoObjectBuffer`、`HoSurfaceBuffer` | ❌ 原始网格 |

2. **描边在所有专用 buffer 里一律是“原始网格表面”，且不是几何真值。** 因此 GeometryBuffer 的 `NormalDepth.a`（coverage）对描边像素保持无效；OB 的 identity／AC 的遮罩也按主体几何归属，不把描边壳算作独立对象。

3. **屏幕几何真值 = GeometryBuffer；对象／材质遮罩真值 = AC。** ScreenProcess 所有 effect 的统一口径是“自有 RT 优先，URP camera depth／normals 只作显式 fallback”，回退以 `_HoGeometryBufferValid <= 0.5` 为闸门；遮罩统一走 `ScreenProcessMask.hlsl` → `HoAC_TotalCoverage`，effect shader 不直接读相机 buffer。

4. **“描边是否可见”和“描边是否是真实几何”是两个独立问题**，前者由独立的 `_HoGeometryBufferOutlineNormalDepthTexture` 承担（`HoGeometryBufferOutlineNormalDepth` pass，只被 DOF 用于取描边视觉深度；SSGI／GTAO 不消费），后者继续由 coverage 约束。

5. **DOF 的描边处理**：`SampleEyeDepth` 只读 GB 线性深度；`SampleVisualEyeDepth` 在 outline normal-depth 的 coverage > 0.5 时改用描边自身线性深度，中心像素与每个 tap 都用同一规则——这才能让描边“跟它依附的表面的对焦状态一致”，而不是被强制保持锐利。

6. **`_CameraDepthTexture` 的实际生产分支必须用 Frame Debugger／RenderDoc 确认**（DepthOnly prepass 还是 CopyDepth、copy 相对 forward outline 的时序、`_OutlineZWrite`／`_OutlineZTest` 的材质值）。静态读码不足以断言。

## 2. 描边语义契约（设计结论）

后续再动描边时应先定义三种可独立选择的语义，而不是加一个“描边也算表面”的总开关：

| 语义 | 输出 | 用途 |
| --- | --- | --- |
| Visual-only outline | 只进颜色（+ 专用视觉深度） | 维持 NPR 轮廓，不让 AO／GI 把轮廓当实体 |
| Occluding shell | 进深度，法线按壳朝向重建 | 让 DOF／遮挡／阴影把轮廓当屏幕前方壳层 |
| Semantic extension | 进 mask／id／flags，几何字段按规则写 | 让规则遮罩、角色分组、SSS 知道轮廓归属，但不改变物理深度 |

三者可组合，但**不应默认绑定为“所有 buffer 都写”**。

## 3. ScreenProcess 效果资源矩阵（当前实现）

| Effect | 颜色源 | 几何／语义输入 | 缺口与降级 |
| --- | --- | --- | --- |
| `CustomMaterial` | active color | feature 不主动绑定语义 RT，自定义 shader 自负其责 | 没有显式资源契约，不能假定它已是 GB／AC-first |
| `EdgeLight` | active color | GB `NormalDepth` + AC 遮罩 | GB 无效即不出效果，不回退 URP |
| `Outline` | active color | GB `NormalDepth` + AC 遮罩（按 layer 调制） | GB 无效时显式 no-op；`SampleSceneNormals` 只作 fallback |
| `DropShadow` | active color | **AC 遮罩优先**，`_lilScreenProcessSubjectMaskTexture` 为显式 fallback | SubjectMask 不承载 ID／厚度／曲率 |
| `DepthOfField` | active color | GB `NormalDepth.a` + `OutlineNormalDepth`（描边视觉深度）+ AC 调制 | coverage 无效像素按远裁剪面处理；GB 无效时回退 URP depth |
| `DepthFog` | active color | GB `NormalDepth` | GB 无效时回退 URP depth |
| `PostLighting` | active color | GB `NormalDepth` + AC 遮罩 | — |
| `SkyTyndall` | active color | GB `NormalDepth` + `_HoGeometryBufferSkyTexture`（GB 附属输出） | sky 无效即 no-op |

所有 effect 仍对 active camera color 做 blit；**GB／AC 不是颜色替代品**。

## 4. 踩过的坑

1. **自有 buffer 不会自动改变 `_CameraDepthTexture`。** 写 MetadataBuffer／GeometryBuffer 只是写自己的附件，DOF 当时读的是相机深度——必须在配置层同时改 consumption（`RequiresDepth()`／`ConfigureInput`）、绑定（`UseTexture` + shader global）和采样三处，缺一处就“看起来没生效”。
2. **资源契约必须落到 `UseTexture` + global binding + shader sample 三处**，不能只停在字段名或设计意图上（DropShadow 的 SubjectMask 曾因此成为死路径）。
3. **把描边无条件塞进几何 pass 会污染主体。** 给 `DEPTHNORMALS`／`GBUFFER` 加 `LIL_OUTLINE` 后，这两个 pass 是**整颗 mesh** 的，外扩会把主体也一起外扩 → SSGI 用错误法线／深度照亮主体（脸／身体发红、错乱）。**已回退。**
4. **`#define LIL_OUTLINE` 会重定向 `sampler_MainTex` → `sampler_OutlineTex`**，与同一 pass 里片段采样 `_MainTex` 冲突，报 `sampler_outlinetex / sampler_maintex` 不匹配。即使要做“外扩几何 pass”，也得另立 pass 而不是复用。
5. **关描边 `ZWrite` 不是修法**：描边直接消失（视觉不可接受）。
6. **rendering layer 排除不了描边**：外扩壳不写 rendering layer（层写自基础几何），而且描边与主体共用同一 renderer 的层，无法只按层拆分。
7. **诊断只说“自有 RT 是否存在”不够**：应报“每个 effect 实际选用了哪一个 provider（GB／AC／fallback）”，否则 fallback 静默生效时无法定位（`ScreenProcessRuntimeDiagnostics` 已按 `RequiresCoverage`／`CoverageAvailable` 覆盖这一点）。
8. **coverage 无效像素必须有明确语义**：GB 里 coverage = 0 表示“不参与几何消费”，DOF 一律按远裁剪面（`LilHoGeometryBufferLinearDepthOrFar`）处理，不能读成 0 深度。

## 5. 被证伪／被降级的假设

- ~~“把描边写进 GeometryBuffer／MetadataBuffer 就等于补齐数据”~~ → 与“GB 是唯一几何真值、描边 coverage 无效”冲突，须先定义 §2 的三种语义。
- ~~“DOF 读 `_CameraDepthTexture` 是既定事实”~~ → 现在 GB 优先，URP depth 只是显式 fallback。
- ~~“描边应该被 SSGI 当成受光面处理”~~ → 描边是非物理装饰壳，见 `Ho-已知问题-描边SSGI白边.md`。

## 6. 关键文件

| 责任 | 文件 |
| --- | --- |
| DOF 深度消费 | `Runtime/ScreenProcess/Shaders/ScreenProcess/DepthOfField.shader` |
| ScreenProcess 需求／绑定 | `Runtime/ScreenProcess/ScreenProcessRendererFeature.cs` |
| 遮罩统一入口（→ AC） | `Runtime/ScreenProcess/Shaders/ScreenProcess/ScreenProcessMask.hlsl` |
| 几何真值生产 | `Runtime/GeometryBuffer/HoGeometryBufferPass.cs`（含 `HoGeometryBufferOutlineNormalDepth`） |
| 描边顶点外扩 | `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\Includes\lil_vert_outline.hlsl` |
| lilToon outline pass 模板 | `CustomShaderResources/URP/Default*Outline*.lilblock`、`DefaultMultiOutline.lilblock` |

# CharacterSpecialization 脸色扩散到前发

## 目标

新增一个近景角色效果：把**前发后面那张受光脸**扩散到前发上，让前发获得类似半透的脸色晕染，但不把头发材质改成透明队列。效果保持屏幕空间、Opaque 友好，并复用现有角色语义输入：

- AC 的 `Face` lane 覆盖率（源头是 OB 标签 `脸`）作为扩散源遮罩：既是"这张脸在这个像素上占多少"（脸被前发遮住时它在身份池的层 1 上，照样能取到），也是捕获权重。
- OB 标签 `前发`（FrontHair）作为接收区域。
- **强制脸捕获的 MRT0（`_lilHoCharacterEyeColorTexture`）作为扩散颜色来源**：那上面写的是材质算完光照的 `color`（alpha = 1），所以扩散的是"受光脸"，不是不受光的 albedo。
- `GeometryBuffer NormalDepth` 作为深度限制。
- **不再需要 `MetadataBuffer SurfaceColor`**：那份 coverage 就是 OB 的标签覆盖率，角色特化这一整支已经与 MetadataBuffer 无关。

> 为什么用受光色而不是 albedo：扩散色必须和真实脸的明暗连在一起（脸被阴影挡住时，前发上的晕染也要跟着暗），否则叠上去像贴了一层（owner 的判断）。

## 颜色来源：强制脸捕获（不新增 pass / RT）

- 复用眼透那条捕获支：`CaptureFace`（`HoCharacterSpecializationRendererFeature.cs:574-604`）在 `Face` 模式（`_HoCharacterCaptureMode = 1`）下把材质的受光 `color` 写进 MRT0。
- 门在录制处一行：`needsFaceCapture = RequiresCharacterCapture(settings) || requiresFaceHairDiffuseTextures`（`:542`）。`RequiresCharacterCapture` 仍然是眼透那组条件；`requiresFaceHairDiffuseTextures`（`HoCharacterSpecializationPass.FaceHairDiffuse.cs:11-40`）= `faceHairDiffuseEnabled` 或该链的 8 个 debug 视图任一。
- **无回退**：捕获不可用时这张图就是 0（RDG 按描述符 `clearBuffer` 清），扩散出来的颜色也是 0 —— 效果本来就是坏的，不加"退回平色 tint"的路径。
- **眼像素混入被接受**：眼透开启时 `CaptureEye` 会把眼睛像素写进同一个 MRT0，前发上可能蹭到一点眼睛的颜色。这是已知取舍，不加额外遮罩，也不加 MRT。
- 源趟自己 `SetGlobalTexture(EyeColorTextureId, …)`（`:729`）+ `builder.UseTexture(passData.eyeColorTexture, Read)`（`:719`）：不复用上一帧合成趟残留的全局绑定，同时让 RDG 把捕获排在本趟之前。

## 渲染设计

新增 RT 只走临时 RenderGraph 纹理，不给兼容路径增加新的持久 `RTHandle`。

1. `FaceHair Source`
   - 全屏 RDG raster pass（`HoCharacterSpecializationRendererPass.FaceHairDiffuse` 支）。
   - 读取 **角色特化自己的位平面**（由 AC 的 `Face` lane 转置而来）、`NormalDepth`、**`_lilHoCharacterEyeColorTexture`（捕获）**。
   - 写一张临时 HDR 颜色纹理：
     - `rgb = faceLit.rgb * faceMask`（`faceLit` = 该像素 UV 处采到的受光脸；`HoCharacterFaceHairDiffuse.shader`）
     - `a = faceMask`，其中 `faceMask = 脸的覆盖率 * step(0.0001, NormalDepth.a)`
   - 写一张临时深度数据纹理：
     - `r = linearDepth * faceMask`
     - `a = faceMask`
   - depth / mask / validity 的算法与改造前逐字相同，只有颜色来源与语义来源换了（覆盖率天然带亚像素相位）。

2. `FaceHair Blur`
   - 2 轮各向同性 fast Gaussian disk blur，使用同一组 RDG 临时 ping-pong 纹理。
   - 颜色和深度数据一起 ping-pong，全部使用 RDG 临时纹理。
   - 每轮使用 40 个 golden-angle disk taps，不引入 compute pass。
   - 半径按屏幕像素配置，并按 blur RT 尺寸换算到当前纹理像素。
   - 不走横纵分离，避免极端半径下出现明显单向条带。

3. `Composite`
   - 读取最终模糊后的颜色和深度数据：
     - `blurMask = blurredColor.a`
     - `faceColor = blurredColor.rgb / max(blurMask, eps)`（= **模糊后的受光脸**）
     - `faceDepth = blurredDepth.r / max(blurredDepth.a, eps)`
   - 最终遮罩：
     - `前发覆盖率 * Levels(blurMask) * depthGate`（`前发` 通道现在是覆盖率，不再是 0/1 位）
   - **颜色乘（tint）在模糊之后相乘**，它是层色、不是被扩散的内容：
     - `tintedFaceColor = faceColor * _HoCharacterFaceHairDiffuseTintColor.rgb`
     - 再按混合模式把 `tintedFaceColor` 叠到当前前发颜色上（`HoCharacterSpecializationComposite.shader:838-844`）。

## 参数

RendererFeature 默认值和 Volume override 都暴露：

- `faceHairDiffuseEnabled`
- `faceHairDiffuseStrength`
- `faceHairDiffuseRadiusPixels`
- `faceHairDiffuseDepthTolerance`
- `faceHairDiffuseLevelBlack`
- `faceHairDiffuseLevelWhite`
- `faceHairDiffuseTintColor`
- `faceHairDiffuseBlendMode`

第一版混合模式保持收敛：

- `Lerp`：前发向"受光脸 × 颜色乘"线性靠近。
- `Additive`：给前发加一层脸色，保留黑发可读性。
- `Screen`：更亮的 opaque-friendly 色彩包裹。

> 改了颜色来源之后，`扩散强度` 与 `颜色乘` 的旧调参不再等价（受光色比 albedo 暗/带饱和），实机需要重调一次。

## 改动记录（受光脸输入，2026）

| 位置 | 改前 | 改后 |
|---|---|---|
| `HoCharacterFaceHairDiffuse.shader:51` → `:68`（源趟颜色通道） | `output.color = half4(surfaceColor.rgb * semanticMask, mask)` | `output.color = half4(faceLit * semanticMask, mask)`（`faceLit = SAMPLE(_lilHoCharacterEyeColorTexture, uv).rgb`） |
| `HoCharacterSpecializationComposite.shader`（合成趟） | `faceHairDiffuseColor = (blurredColor.rgb / blurredColor.a) * tint.rgb` | 同一条表达式，未改动 —— tint 从来就在模糊之后乘；这次改的是括号里那个"模糊后的颜色"现在来自捕获（受光脸）而不是 SurfaceColor（albedo） |
| 捕获门 `HoCharacterSpecializationRendererFeature.cs:542` | `needsFaceCapture = RequiresCharacterCapture(settings)` | `needsFaceCapture = RequiresCharacterCapture(settings) \|\| requiresFaceHairDiffuseTextures`（效果开关或它的 8 个 debug 视图都会拉起脸捕获） |

## Debug

`HoCharacterSpecializationDebugMode` 新增四个（**阶段视图**，值 18-21；旧的四项 5-8 保留不动）：

| 值 | 标签 | 内容 | 生产处 |
|---|---|---|---|
| 18 | 脸色扩散捕获受光脸 | ① 源趟**原始采样**到的受光脸（不乘语义遮罩，整屏直出） | `HoCharacterFaceHairDiffuse.shader:59-66`（源趟在 debug 18 下把采样原样写进源色纹理的 rgb）→ 合成趟 `Composite.shader:713-723` 显示 |
| 19 | 脸色扩散模糊受光脸 | ② 模糊后、**乘颜色乘之前**（`blurredColor.rgb / blurredColor.a`） | 合成趟 `Composite.shader:725-735`（与既有的 7 同内容，这里按阶段顺序给一个入口） |
| 20 | 脸色扩散受光脸乘色 | ③ 再乘颜色乘（= 正式合成用的那层颜色） | 合成趟 `Composite.shader:737-746` |
| 21 | 脸色扩散最终合成 | ④ 本支按正式合成的数学与顺序叠到当前画面（**不叠**眼透/前发投影/轮廓，用来单独判断落色） | 合成趟 `Composite.shader:748-761` |

- 四个视图都从同一条 RDG 临时纹理链读，能直接验证实际合成路径；①②在源趟/模糊链里成色，③④在合成趟里成色。
- 四个视图都在 Game 视图里可用（合成趟与源趟都吃 `_HoCharacterOptions.w`，而 `_HoCharacterOptions` 由设置直接给出）。
- 反向检查：把 `颜色乘` 调成纯白，③ 必须与 ② **逐像素一致**；把 `启用脸色扩散` 关掉，④ 必须回到原画面（21 走 `options.x` 的门）。
- 兼容（非 RenderGraph）路径不产生这条链：`options.y = 0` → 18/19/20 显示黑，21 显示原画面，行为与改前一致。

## A/B 目视清单（改前 / 改后；本工作区没有跑过 Unity，像素一律"未验证"）

固定场景 / 相机 / 角色 / 分辨率，参数：`启用脸色扩散` = 开、`扩散强度` 0.35、`模糊半径像素` 48、`深度容差` 0.25、`颜色乘` (1, 0.78, 0.72, 1)、`混合模式` 加色。

| # | 看什么（调试模式） | 期望 |
|---|---|---|
| 1 | **① 18** 脸色扩散捕获受光脸 | 脸上有明暗（高光/阴影/鼻影都在）：这就是"受光"的证据。整屏全黑 ⇒ 捕获没跑（材质侧 `HoCharacterCapture` pass，或该部件的「标签」里没有 `脸`，或 OB feature 不在 renderer 里）。发际线以下（受前发遮挡的脸）应当仍是脸的颜色 —— 这正是被扩散的原料 |
| 2 | **② 19**，半径 48 → 16 → 96 各抓一次 | 与 ① 同色系但摊开；半径越大越糊；发际线处平滑，不能有硬边或 1px 环（有 ⇒ OB 覆盖率没生效：看 feature 面板的「OB 身份池 / OB 语义位平面」自检） |
| 3 | **③ 20**，并临时把 `颜色乘` 改成纯白 | 纯白时必须与 ② 逐像素一致（证明 tint 是"乘"不是"换"）；恢复 (1,0.78,0.72) 后应整体偏暖 |
| 4 | **④ 21** | 只有脸色扩散这一支落在头发上（眼透/前发投影/轮廓都不参与）；把 `启用脸色扩散` 关掉 ⇒ ④ 回到原画面 |
| 5 | **关闭(Off) + 启用脸色扩散**：同一帧，改前 / 改后各抓一次 | 改后：晕染跟着脸的明暗走（脸被阴影挡住时前发一起变暗；脸偏暖时前发偏暖）。改前：不管脸明暗，前发上是同一层 albedo×tint。差异最大的地方＝脸有强阴影或强高光的区域；**前发整体亮度会变（通常变暗）**，需要重调 `扩散强度` / `颜色乘` |
| 6 | 眼透 **开** 时再看 ① 18 | 眼睛像素出现在捕获里（两趟捕获共用 MRT0）⇒ 前发上可能蹭到眼睛颜色。这是**已知接受**的行为，不是 bug |
| 7 | RDG 视图 | `CaptureFace` 现在在"脸色扩散关、眼透关、5/6/7/8 也不选"时不再进图；在"脸色扩散开"时必定进图（这是本次唯一的结构性变化） |

## 执行边界

- 缺少基础 **ObjectBuffer / GeometryBuffer** 输入时，整个 CharacterSpecialization 按现有规则跳过（feature 面板的输入自检会指出缺哪一项）。
- 语义只有 OB 一个来源：`脸` / `前发` 标签没打、或 RSUV 没刷新时，这一支整体为 0，没有"退回 albedo"的路径。
- **捕获（`_lilHoCharacterEyeColorTexture`）是脸色扩散的硬输入**：没有回退路径。
- 非 RenderGraph 兼容路径不分配新增 blur RT，只保留原有眼透和前发投影行为。
- v1 不做 same-character 隔离。该效果定位为近景角色修正，依赖深度和范围控制；脸部扩散颜色串色风险可接受。

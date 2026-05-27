# CharacterSpecialization 脸色扩散到前发

## 目标

新增一个近景角色效果：把脸部基础色扩散到前发上，让前发获得类似半透的脸色晕染，但不把头发材质改成透明队列。效果保持屏幕空间、Opaque 友好，并复用现有角色语义输入：

- `ObjectCustom1` / Face 作为扩散源遮罩。
- `ObjectCustom2` / FrontHair 作为接收区域。
- `MetadataBuffer SurfaceColor` 作为脸部 base color 来源。
- `GeometryBuffer NormalDepth` 作为深度限制。

## 渲染设计

新增 RT 只走临时 RenderGraph 纹理，不给兼容路径增加新的持久 `RTHandle`。

1. `FaceHair Source`
   - 全屏 RDG raster pass。
   - 读取 `ObjectCustom0_3`、`SurfaceColor`、`NormalDepth`。
   - 写一张临时 HDR 颜色纹理：
     - `rgb = surfaceColor.rgb * faceMask`
     - `a = faceMask`
   - 写一张临时深度数据纹理：
     - `r = linearDepth * faceMask`
     - `a = faceMask`

2. `FaceHair Blur`
   - 2 轮各向同性 fast Gaussian disk blur，使用同一组 RDG 临时 ping-pong 纹理。
   - 颜色和深度数据一起 ping-pong，全部使用 RDG 临时纹理。
   - 每轮使用 40 个 golden-angle disk taps，不引入 compute pass。
   - 半径按屏幕像素配置，并按 blur RT 尺寸换算到当前纹理像素。
   - 不走横纵分离，避免极端半径下出现明显单向条带。

3. `Composite`
   - 读取最终模糊后的颜色和深度数据：
     - `blurMask = blurredColor.a`
     - `faceColor = blurredColor.rgb / max(blurMask, eps)`
     - `faceDepth = blurredDepth.r / max(blurredDepth.a, eps)`
   - 最终遮罩：
     - `FrontHair * Levels(blurMask) * depthGate`
   - 最后把 `faceColor * tintColor` 叠到当前前发颜色上。

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

- `Lerp`：前发向脸部 tint 色线性靠近。
- `Additive`：给前发加一层脸色，保留黑发可读性。
- `Screen`：更亮的 opaque-friendly 色彩包裹。

## Debug

`HoCharacterSpecializationDebugMode` 新增：

- `FaceHairDiffuseSourceMask`
- `FaceHairDiffuseBlurMask`
- `FaceHairDiffuseBlurColor`
- `FaceHairDiffuseMask`

这些 debug view 都从同一条 RDG 临时纹理链读取，能直接验证实际合成路径。

## 执行边界

- 缺少基础 MetadataBuffer / GeometryBuffer 输入时，整个 CharacterSpecialization 仍按现有规则跳过。
- `SurfaceColor` 只在脸色扩散启用或相关 debug view 开启时成为必需输入。
- 非 RenderGraph 兼容路径不分配新增 blur RT，只保留原有眼透和前发投影行为。
- v1 不做 same-character 隔离。该效果定位为近景角色修正，依赖深度和范围控制；脸部扩散颜色串色风险可接受。

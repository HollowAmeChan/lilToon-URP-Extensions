# Shoost 边缘光 URP 重写设计

本文记录 `EdgeLight / 边缘光` 的新实现方向。它不再按 Shoost 的透明图片/RawImage 图层算法硬搬，而是作为 URP 主体数据效果实现：先显式产出角色 normal + mask，再在 URP 内置后处理前把 HDR 边缘光合成回 camera color，让 Bloom 能继续吃到它。

## 结论

材质侧开关是合适的，但它不应该直接“做后处理”。更稳的契约是：lilToon 材质提供一个是否写入主体数据 RT 的开关，扩展包的 RendererFeature 负责收集 mask/normal，再由独立的 subject effects pass 读取这些数据并合成发光。

这样做有几个好处：

- 角色、道具、背景可以在材质或 layer mask 上明确选择是否参与边缘光。
- 不依赖最终 camera color 的 alpha，也不会把背景误判成角色边缘。
- 边缘光合成发生在 `BeforeRenderingPostProcessing`，URP Bloom、Tonemapping、Color Adjustments 都能处理它。
- 后续 `Outline / 轮廓`、`DropShadow / 投影` 可以复用同一套主体数据 RT。

## 与 Shoost Final Stack 的关系

如果 Shoost 后续重构为纯最终图层/滤镜系统，`EdgeLight` 不应该继续作为 Shoost final layer 实际执行。它可以保留 Shoost 的名称、图标和参数习惯，但运行时应归入新的 `lilToon Subject Effects` RendererFeature。原因是边缘光需要主体 normal/mask，并且通常希望在 URP Bloom 前写入 HDR 颜色；这和 Shoost final stack 的“URP 后处理之后做最终图层混合”不是同一类职责。

这里的 HDR 边界很重要：边缘光的 `Brightness` 应该能写出超过 1 的 HDR 能量，让 URP Bloom 在后面捕捉。如果把边缘光移动到 Tonemapping / Bloom 之后，它只能在最终 LDR 画面上加白，视觉会更硬，也不会产生项目 Bloom 的扩散。

推荐边界：

- `Shoost Final Stack`：最终画面滤镜、图层混合、颗粒、CRT、VHS、像素化、色阶、最终调色。
- `lilToon Subject Effects`：边缘光、轮廓、投影、基于 normal/depth/mask 的角色特调。
- `lilToon Subject Data`：只生产 normal/mask/depth/color 等中间 RT，不直接改 camera color。

## 渲染顺序

建议拆成两类 pass：

1. `lilToon Subject Data`：在透明物体和 OIT 合成完成后、URP 内置后处理前运行。它不改 camera color，只清空并写入主体数据 RT。
2. `lilToon Subject Effects / EdgeLight`：在 `Before URP Post Processing` 运行，读取 camera color 与主体数据 RT，把 HDR 边缘光合成回 camera color。

推荐事件顺序：

- OIT accumulation：`BeforeRenderingTransparents`
- OIT composite：`AfterRenderingTransparents`
- Subject Data：`AfterRenderingTransparents + 1` 或 `BeforeRenderingPostProcessing - 2`
- EdgeLight：`BeforeRenderingPostProcessing`
- URP Bloom/Tonemapping：URP 内部 post process

如果 EdgeLight 为兼容旧 Shoost 图层列表而保留同名入口，它仍然应该默认解析为 `BeforeURPPostProcessing`，或者提示用户去 `lilToon Subject Effects` 面板调整。用户把它手动改到 After URP Post Processing 时要提示：这会失去 Bloom 参与。

## 主体数据 RT

第一版建议用一张 RT 打包 normal 与 mask：

- 名称：`_lilShoostSubjectDataTexture`
- 格式：优先 `R8G8B8A8_UNorm`；如果 toon 法线量化明显，再升级到 `R16G16B16A16_SFloat`
- RGB：view-space normal，编码为 `normalVS * 0.5 + 0.5`
- A：subject mask，0 表示不参与，1 表示完整参与

view-space normal 比 world-space normal 更适合这里：边缘光核心是相机视角下的 rim，`normalVS.z` 可以直接表示面朝镜头的程度，`normalVS.xy` 可以直接支持 Shoost 的 Angle 参数。

后续如果轮廓/投影需要更强几何关系，可以再加：

- `_lilShoostSubjectDepthTexture`
- `_lilShoostSubjectColorTexture`
- stencil 或 rendering layer 过滤

第一版不要从 `_CameraNormalsTexture` 直接取，因为 URP 的 normals 通常偏向 opaque/depth-normal 路径，透明 lilToon 角色未必稳定写入。我们需要自己的 subject data pass。

## lilToon 材质契约

在 lilToon fork 侧建议新增一个轻量开关：

- `_lilShoostSubjectData`：是否写入 Shoost 主体数据
- 可选 `_lilShoostSubjectMask`：0-1 强度，默认 1
- 可选 `_lilShoostSubjectDataMode`：Off / Character / Prop / Foreground，第一版可以只做 Off/On

材质开关只决定“这个 renderer 能不能被 subject data pass 画进去”。不要让材质自己决定边缘光颜色、亮度或 Bloom；这些仍由 Shoost EdgeLight 图层控制。

lilToon shader 需要暴露一个新 pass：

```shaderlab
Tags { "LightMode" = "lilToonSubjectData" }
```

这个 pass 输出 view-space normal 和 mask。它应复用 lilToon 的 alpha clip / cutout / dissolve 语义，避免透明边缘和主体数据边缘不一致。透明排序上第一版可以沿用 render queue + depth test；如果多层透明角色出现覆盖错误，再考虑单独 subject depth 或 frontmost resolve。

## 边缘光算法

核心 mask 可以由三部分相乘或相加后归一：

- `subjectMask`：主体数据 RT 的 alpha。
- `normalRim`：`1 - saturate(abs(normalVS.z))`，表示视角切线处。
- `directionMask`：由 `Angle` 控制的方向性边缘，使用 `dot(normalVS.xy, dir)`。

建议第一版：

```text
normalRim = 1 - saturate(abs(nVS.z))
direction = dot(normalize(nVS.xy), float2(cos(angle), sin(angle)))
single = saturate(direction)
double = abs(direction)
rim = normalRim * lerp(single, double, modeIsDouble)
rim = smoothstep(size, 1, rim)
rim = pow(rim, sharpen)
rim = ApplyContrast(rim, contrast)
rim *= opacity * intensity * subjectMask
output = source + rimColor * brightness * rim
```

Shoost 的 `Single / Double / Single(Sharpen) / Double(Sharpen)` 可以映射为：

- Single：只取一个方向的边缘。
- Double：左右/上下两侧都取。
- Single(Sharpen)：Single 后提高 sharpen。
- Double(Sharpen)：Double 后提高 sharpen。

如果需要 Shoost 那种“贴着 alpha 外缘”的视觉，可以追加一个很小的 mask dilation：

```text
outerMask = dilate(subjectMask, radius) - subjectMask
rim += outerMask * outerAmount
```

但第一版应以角色表面的 normal rim 为主，外扩只作为 Bloom 友好的补充，避免边缘光变成轮廓描边。

## Shoost 参数映射

用户面板保持 Shoost 习惯，但底层语义写清楚：

- `Color`：边缘光颜色，允许 HDR。
- `Opacity`：最终 alpha/混合强度，0-1。
- `Size`：边缘宽度，0-1；实际 shader 中转成 smoothstep 阈值。
- `Brightness`：HDR 亮度，0-10；为 Bloom 预留超过 1 的能量。
- `Contrast`：边缘硬度/对比度，0-1。
- `Angle`：方向，-180 到 180。
- `Mode`：Single / Double / Single(Sharpen) / Double(Sharpen)。
- `BlendMode`：保留 Shoost UI，但默认建议 Add 或 Screen；若使用 Normal/Overlay 等 Photoshop 模式，需要明确它们是 pre-Bloom 的颜色混合，不等同于 Shoost 透明 RawImage 图层。

建议在现有 `ShoostPostProcessLayer` 中临时映射：

- `color`：Rim Color
- `intensity`：总强度
- `blendMode`：BlendMode
- `parameters0.x`：Size
- `parameters0.y`：Brightness
- `parameters0.z`：Contrast
- `parameters0.w`：Opacity
- `parameters1.x`：Angle
- `parameters1.y`：Mode
- `parameters1.z`：Outer Width
- `parameters1.w`：Outer Amount

后续如果主体数据效果变多，再考虑给 EdgeLight 独立 serializable settings，避免通用 Vector4 继续膨胀。

## 实现步骤

1. 在扩展包 Runtime 新增 `SubjectData` 模块：settings、shader constants、render targets、renderer feature/pass。
2. 在 lilToon fork 新增材质属性和 `LightMode = lilToonSubjectData` pass，只写 normal/mask。
3. 在 Shoost stack runtime 特判 `ShoostPostProcessEffect.EdgeLight`，不要走默认 blit shader；改用 `Hidden/lilToon-Shoost/URP/Shoost/EdgeLight`。
4. 在 editor 面板给边缘光做中文参数 UI，并把默认 insertion 固定为 `Before URP Post Processing`。
5. 用 RenderDoc 验证三件事：subject data RT alpha 只包含目标角色；RGB normal 随相机变化正确；edge light pass 位于 URP Bloom 前。

## 注意事项

- 不要把“所有 lilToon 材质默认写入边缘光 mask”作为长期默认。第一版可以提供全局 layer mask 方便测试，但正式入口应让材质或对象显式 opt-in。
- 不要依赖 camera color alpha。URP 相机目标在大多数项目里已经是合成后的不透明画面。
- 不要把 edge light 做在 `AfterRenderingPostProcessing`，除非用户明确只想要最终覆盖，不想让 Bloom 响应。
- 不要让主体数据 pass 修改主颜色或 OIT 状态；它应该只是给后处理提供数据。

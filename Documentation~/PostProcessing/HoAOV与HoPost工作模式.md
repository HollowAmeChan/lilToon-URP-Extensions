# HoAOV 与 HoPost 工作模式

本文记录当前 HoAOV 的实际工作方式，以及 HoPost 图层如何消费 HoAOV。它不是远期愿景文档，而是给后续实现、排障和重做功能时参考的规则清单。

## 一句话分工

```text
HoAOV  = 生产主体数据的 RendererFeature
HoPost = 消费主体数据的可排序效果栈
Shoost = 消费最终画面的 final stack
```

HoAOV 不改 camera color，只写一组全局 AOV 纹理。HoPost 读取这些纹理做边缘光、轮廓、投影和局部 mask。Shoost 默认只读最终画面，不应该默认依赖 HoAOV。

## 当前渲染链路

推荐顺序：

```text
lilToon / lilPBR 正常渲染
HoAOV Output
URP Post Processing
HoPost Stack
Shoost Final Stack
Final
```

当前 HoAOV pass event 默认为 `AfterRenderingTransparents`。HoPost 固定在 URP 主后处理之后、Shoost Final Stack 之前执行。

HoAOV Output 每帧会：

1. 分配或复用 AOV RT。
2. 清空 AOV color 和 HoAOV 自己的 depth。
3. 设置全局纹理和 `_lilHoAovActive = 1`。
4. 可选绘制 fallback material。
5. 绘制所有带 `LightMode = "HoAOV"` 的材质 pass。
6. 将 AOV RT 绑定为全局纹理供 HoPost 和 debug shader 读取。

## AOV 纹理契约

当前全局纹理名：

```text
_lilHoAovMaskIdTexture
_lilHoAovNormalDepthTexture
_lilHoAovTangentNormalTexture
_lilHoAovSurfaceDataTexture
_lilHoAovCustom0_3Texture
_lilHoAovCustom4_7Texture
_lilHoAovCustom8_11Texture
```

当前实际稳定使用的是 `Custom0..Custom3`，打包在 `_lilHoAovCustom0_3Texture.rgba`。`Custom4..Custom11` 目前只保留纹理和协议占位，不作为默认可用功能扩展。

重要警告：之前尝试把 `_lilHoAovCustom4_7Texture` 和 `_lilHoAovCustom8_11Texture` 也接成真实 custom 输入时，多次遇到 Unity / URP / SRP 在 shader import 或启动阶段直接崩溃。现象不像普通 shader 编译错误，更像 native 侧对大量材质贴图输入、SRP Batcher 常量布局或 texture binding 数量组合不稳定。因此这两张纹理当前只能视为协议预留名，不要默认给 lilToon / lilPBR 继续补 `Custom4..Custom11` 的独立贴图入口。

## AOV 数据分层

HoAOV 不是一个无限扩展的材质 custom 池。当前设计分成三类 AOV：

```text
HoPost 固定消费 AOV
- 系统级输入，由 HoPost 内置效果稳定消费
- 例如 Mask、GroupId、ObjectId、Flags、Normal、Depth、Thickness、Curvature、Material、Utility
- 不作为用户随意扩容的 custom 容量

Material AOV
- 材质级、贴图级、需要 UV 的细节遮罩
- 默认只支持 MaterialCustom0..MaterialCustom3
- 允许灰度颜色 * 灰度贴图 R
- 来源是 lilToon / lilPBR 材质 UI

Object AOV
- 物体级、分组级、可批量配置的语义遮罩
- 目标支持 ObjectCustom0..ObjectCustom7
- 不允许贴图输入，不进入 lilToon / lilPBR 材质 UI
- 来源是场景里的 HoAovGroup 空物体组件
```

容量规则：

```text
贴图型 AOV：最多 4 个，只属于 Material AOV
物体型 AOV：默认预留 8 个，只由 HoAovGroup 批量标记
不要把 ObjectCustom4..7 解释成材质 Custom4..7
不要再给 lilToon/lilPBR 追加更多 custom 贴图入口
```

这样做的原因是：之前崩溃风险来自大量材质 texture binding，而物体 AOV 可以走分组数据、MaterialPropertyBlock 或未来的对象 buffer，不需要新增 sampler。眼透、角色分层合成、前发/脸/眼睛分组这类需求也更适合 Object AOV，而不是材质贴图。

## 物体 AOV 指定方式

Object AOV 第一版应挂在空物体上，而不是塞进 Volume，也不是强制每个 Renderer 都挂组件。建议组件名：

```text
HoAovGroup
- enabled
- includeChildren
- explicitRenderers[]
- layerMask / rendererFilter
- priority
- groupId
- objectIdMode
- objectId
- flags
- materialClass
- utility
- objectCustom0
- objectCustom1
- objectCustom2
- objectCustom3
- objectCustom4
- objectCustom5
- objectCustom6
- objectCustom7
```

用法约定：

```text
角色根节点挂一个 HoAovGroup，控制整套角色的 groupId / flags。
头发、脸、眼睛、配件等子空物体可以再挂 HoAovGroup 覆盖局部分组。
includeChildren 为 true 时收集子层级 Renderer。
explicitRenderers[] 用于补充不在子层级里的 Renderer。
同一个 Renderer 命中多个 HoAovGroup 时，priority 高者覆盖；priority 相同时离 Renderer 最近的层级覆盖。
没有命中 HoAovGroup 的 Renderer 使用材质默认 AOV 和系统默认值。
```

Object AOV 不放进 Volume 的原因：Volume 适合混合后处理参数，不适合混合离散 Renderer 列表。列表在空间权重里很难定义“半影响”或“区域内才属于某组”。Object AOV 属于场景/角色语义，应该跟 prefab 或空物体层级走。

眼透预留建议：

```text
ObjectCustom0 = Character / 主体
ObjectCustom1 = Face
ObjectCustom2 = FrontHair
ObjectCustom3 = Eye
ObjectCustom4 = EyeRevealArea / 允许眼透区域
ObjectCustom5 = Accessory / 配件
ObjectCustom6 = Reserved
ObjectCustom7 = Reserved
```

眼透如果要恢复被前发挡住的眼睛颜色，AOV 只负责 mask，还需要额外 Eye Color Buffer 或角色合成 pass。AOV 侧提前预留 Face / FrontHair / Eye / EyeRevealArea，是为了后续能做简单、统一的角色合成，而不是回到每个材质各自写 stencil 重绘逻辑。

当前通道布局：

```text
MaskId.r      mask weight
MaskId.g      encoded group id
MaskId.b      encoded object id
MaskId.a      encoded flags

NormalDepth.rgb      encoded world normal
NormalDepth.a        linear eye depth

TangentNormal.rgb    encoded tangent normal
TangentNormal.a      tangent normal enabled flag

SurfaceData.r        thickness
SurfaceData.g        curvature
SurfaceData.b        encoded material class
SurfaceData.a        utility

Custom0_3.r          MaterialCustom0
Custom0_3.g          MaterialCustom1
Custom0_3.b          MaterialCustom2
Custom0_3.a          MaterialCustom3
```

ID、flags、material 这类离散值不是直接写原始数字，而是写 `frac(abs(value) * 0.61803398875)` 的稳定编码值。HoPost 做数值匹配时必须对目标值使用同一套编码逻辑。

## HoPost 的 AOV Mask

每个 HoPost 图层都有一组通用 AOV mask 设置：

```text
useAovMask
aovSource
aovMaskMode
aovThreshold
aovSoftness
aovMatchValue
aovMatchColor
invertAovMask
debugAovMask
```

`aovSource` 当前范围：

```text
Mask
GroupId
ObjectId
Flags
Thickness
Curvature
Material
Utility
MaterialCustom0
MaterialCustom1
MaterialCustom2
MaterialCustom3
```

Object AOV 接入后，目标范围应继续增加：

```text
ObjectCustom0
ObjectCustom1
ObjectCustom2
ObjectCustom3
ObjectCustom4
ObjectCustom5
ObjectCustom6
ObjectCustom7
```

`aovMaskMode` 当前含义：

```text
Direct      直接使用通道灰度
Threshold   通道值高于阈值时选中，可用 softness 软化
MatchValue  匹配数值，ID/Flags/Material 会先编码目标值再匹配
MatchColor  从同一张 packed texture 取 RGB，按颜色距离匹配
```

公共 shader 方法位于：

```text
Runtime/HoPostProcessing/Shaders/HoPost/HoPostAovMask.hlsl
```

效果 shader 不要重复实现 AOV 匹配逻辑。HoPost 已经有公共方法可以按不同方式把 AOV 转成蒙版，包括直接灰度、阈值、数值匹配、颜色匹配、softness 和 invert。具体由图层参数 `_LayerAovSource`、`_LayerAovMode`、`_LayerAovParams`、`_LayerAovMatchColor` 驱动。

Editor 侧也应该只维护一套公共 AOV 遮罩 UI。当前入口是 `HoPostProcessStackVolumeEditor.DrawAovMaskProperties`，内部再拆成 source/mode 绘制和 match 参数绘制。后续新增 HoPost 图层时不要在各自效果里复制一套 AOV UI，否则很容易出现“UI 画了参数，但 shader 没用”的假接口。

公共 UI 的显示规则：

```text
始终显示：
- AOV 遮罩 foldout / 启用
- AOV 源
- 使用方式
- 反转
- 输出匹配结果

Direct / 直接灰度：
- 只使用 AOV 源本身的灰度值
- 不使用 aovThreshold
- 不使用 aovSoftness
- 不使用 aovMatchValue
- 不使用 aovMatchColor
- UI 不应显示阈值、柔和度、匹配数值或匹配颜色

Threshold / 阈值：
- 使用 aovThreshold 作为选中起点
- 使用 aovSoftness 作为 threshold 到 threshold + softness 的软过渡宽度
- 不使用 aovMatchValue
- 不使用 aovMatchColor
- UI 只显示阈值和阈值柔和度

MatchValue / 匹配数值：
- 使用 aovMatchValue 作为目标值
- 使用 aovThreshold 作为数值容差
- 使用 aovSoftness 作为容差边缘的软过渡宽度
- 不使用 aovMatchColor
- GroupId、ObjectId、Flags、Material 会把目标值先做稳定编码再比较
- Mask、Thickness、Curvature、Utility、MaterialCustom0..3、ObjectCustom0..7 使用原始标量值比较
- UI 应显示匹配数值 / ID、数值容差、匹配柔和度

MatchColor / 匹配颜色：
- 使用 aovMatchColor.rgb 作为目标颜色
- 使用 aovThreshold 作为 RGB 距离容差
- 使用 aovSoftness 作为颜色容差边缘的软过渡宽度
- 不使用 aovMatchValue
- 颜色来自所选 AOV 源所在的 packed texture 的 RGB
- MaterialCustom 和 ObjectCustom 通常更适合 Direct / Threshold / MatchValue；MatchColor 只在确实把颜色语义打进 packed texture 时使用
- UI 应显示匹配颜色、颜色容差、颜色柔和度
```

参数打包约定：

```text
_LayerAovParams.x = threshold / tolerance
_LayerAovParams.y = softness
_LayerAovParams.z = match value
_LayerAovParams.w = invert
_LayerAovMatchColor = match color
```

`反转` 不是简单的 `1 - mask`。它只在 HoAOV 覆盖范围内做 `coverage - selected`，避免启用反转后把背景整屏选中。`输出匹配结果` 必须走同一套解析路径，用来验证当前 layer 实际喂给效果的 mask，而不是预览原始 AOV 纹理。

普通图层使用：

```hlsl
float mask = LilHoPostResolveAovLayerMask(uv);
```

强依赖 AOV 的效果使用：

```hlsl
float mask = LilHoPostResolveRequiredAovMask(uv);
```

如果只想取当前 AOV 覆盖范围，不走图层匹配规则，使用：

```hlsl
float coverage = LilHoPostAovCoverage(uv);
```

如果效果需要自己读取通道值，可以复用选择函数，不要手写 source enum 分支：

```hlsl
float scalar = LilHoPostSelectAovScalar(maskId, surfaceData, custom0, source);
float4 color = LilHoPostSelectAovColor(maskId, surfaceData, custom0, source);
```

调试输出使用：

```hlsl
if (LilHoPostShouldOutputAovDebug())
{
    return LilHoPostAovDebugColor(uv, true, alpha);
}
```

## HoPost 效果约定

### EdgeLight

边缘光是主体效果。它可以用 AOV mask 限制生效范围，但边缘光强度、颜色、rim 参数仍由 HoPost 图层控制。

### Outline

轮廓使用屏幕空间 depth/normal 边缘，并可乘以 HoPost AOV mask。它不应该自己再生成一套 subject mask。

### DropShadow

投影当前已经改成基于 AOV mask 的屏幕空间偏移：

```text
shadow = offset(mask) - mask
```

它不再依赖旧的 Shoost 投影逻辑。调试时优先打开图层的 `debugAovMask`，确认输入 mask 是否正确，再看 offset、opacity、color。

投影不透明度为 1 时应该可以输出死黑。不要再把最终阴影强度隐式乘以颜色 alpha，除非 UI 明确把 alpha 当作强度参数。

## 材质侧 HoAOV pass 规则

lilToon / lilPBR 负责提供真正的：

```shaderlab
Tags { "LightMode" = "HoAOV" }
```

材质侧 HoAOV pass 必须尽量复用材质自己的语义：

```text
main uv
main texture alpha
color alpha
alpha mask
dissolve
cutout
dither cutout
normal map
normal flip / two side
custom texture * custom color
```

对 alpha 的当前约定：

```text
Opaque       全部写入 AOV
Cutout       按材质 cutoff 丢弃
Dither       按材质 dither 规则丢弃
Transparent  半透材质全部写入 AOV，不按 alpha 丢弃
```

这样做是为了让半透头发、玻璃等对象仍可作为主体参与 HoPost，但镂空贴图的洞必须真的露出背后的 AOV。

## fallback material 规则

HoAOV fallback 只用于没有 `LightMode = "HoAOV"` pass 的普通物体。它只能提供粗略 mask、geometry normal、depth 和默认 custom 值，不能表达 lilToon/lilPBR 的完整材质 alpha、dissolve、normal map 或 custom texture。

关键规则：

```text
fallback 不应该写 HoAOV depth
fallback 不应该遮挡后续真正 HoAOV pass
fallback 不应该被当成最终质量路径
```

之前出现过的问题是 fallback 先于真正 HoAOV pass 绘制，并且 `ZWrite On`。它会把 cutout 洞当成实心写入 HoAOV depth，导致背后的白色 custom 无法通过深度测试，最终洞里仍是黑的。当前规则是 fallback 使用 `ZWrite Off`。

如果怀疑 fallback 污染结果，第一步在 RendererFeature 里关闭 `useFallbackMaterial` 对比。

## Material / Object AOV 限制

当前工程的硬约束：

```text
Material AOV 默认只支持 MaterialCustom0..MaterialCustom3
四个 MaterialCustom 通道打包进一张 RGBA 纹理
每个 MaterialCustom 通道 = 灰度贴图 R * 灰度颜色 R
MaterialCustom 默认值必须是 0
Material AOV UI 不需要每通道启用开关

Object AOV 目标支持 ObjectCustom0..ObjectCustom7
ObjectCustom 不允许贴图输入
ObjectCustom 默认值必须是 0
ObjectCustom 由 HoAovGroup 空物体批量指定
```

不要再默认扩展到 8 或 12 个独立 MaterialCustom 贴图输入。实际测试中，大量独立 custom texture 输入会导致 Unity 在 shader/import/启动阶段 native 崩溃，不是普通 shader 编译错误。

更多用户遮罩应优先走 Object AOV，而不是继续往材质 UI 里加贴图。Object AOV 解决的是“哪个物体/部件属于哪个合成层”，Material AOV 解决的是“材质表面上哪个 UV 区域属于某个遮罩”。这两类不要混成同一套 custom 编号。

如果未来确实需要更多通道，优先考虑：

```text
packed atlas
texture array
外部 mask buffer
明确实验开关
干净工程逐级验证
```

不要直接追加 `_HoAovCustom4Tex` 到 `_HoAovCustom11Tex` 作为默认方案。

## 调试方法

排查 HoAOV 到 HoPost 的问题时按这个顺序：

1. 看 HoAOV debug view，确认原始 AOV 是否写对。
2. 看 HoPost 图层 `debugAovMask`，确认 source/mode/threshold 是否解析对。
3. 如果 debug mask 对，但效果不对，再查具体效果 shader。
4. 如果 cutout 洞里仍是黑的，先关 `useFallbackMaterial` 对比。
5. 如果 Unity 报 shader 行尾错误，先统一相关 shader/hlsl/lilblock 文件行尾。
6. 如果新增 custom 贴图后 Unity 崩溃，立即回撤新增贴图数量，不要继续堆输入。

常见症状：

```text
HoAOV debug 黑，HoPost 也黑
=> 材质 HoAOV pass 没写入，或 custom 默认值/贴图/颜色为 0

HoAOV debug 正确，HoPost debug 黑
=> HoPost source、mode、threshold、match value 或 invert 设置错误

cutout 洞里黑，背后对象没露出
=> alpha clip 没在 HoAOV pass 生效，或 fallback/depth 抢先遮挡

透明对象没写入
=> 不要把 transparent 当 cutout 处理，半透对象按当前约定应全部写入

投影没效果
=> 先打开 DropShadow 的 AOV debug，看输入 mask 是否存在
```

## 使用流程

项目侧使用时：

1. URP Renderer Data 加 `HoAovRendererFeature`。
2. URP Renderer Data 加 `HoPostProcessRendererFeature`，并放在 Shoost 之前。
3. Volume Profile 添加 `lilToon-HoPost / Process Stack`。
4. lilToon/lilPBR 材质在 HoAOV 栏设置 `MaterialCustom0..MaterialCustom3` 的灰度颜色和贴图。
5. 需要物体/部件分组时，在角色根节点、头发、脸、眼睛或配件空物体上挂 `HoAovGroup`。
6. `HoAovGroup` 用 `includeChildren` 或 `explicitRenderers[]` 指定本组影响哪些 Renderer。
7. HoPost 图层打开 `AOV Mask`，选择需要的 source 和 mask mode。
8. 调试时先用 `debugAovMask`，确认 mask 后再调具体效果参数。

材质或 shader 侧新增功能时：

1. 先确认是否真的需要新增通道。
2. 需要贴图/UV 时优先复用现有 `MaterialCustom0..MaterialCustom3`。
3. 需要物体/部件批量分组时优先走 `HoAovGroup` 的 `ObjectCustom0..ObjectCustom7`。
4. 必须保持默认值为 0。
5. 必须复用材质 alpha/cutout/dissolve 规则。
6. 不要让 fallback 覆盖真正 HoAOV pass 的结果。

## 文件入口

核心 Runtime：

```text
Runtime/AOV/HoAovRendererFeature.cs
Runtime/AOV/HoAovGroup.cs
Runtime/AOV/HoAovShaderConstants.cs
Runtime/AOV/Shaders/HoAOV/HoAovFallback.shader
Runtime/AOV/Shaders/HoAOV/HoAovDebugView.shader
Runtime/HoPostProcessing/HoPostProcessLayer.cs
Runtime/HoPostProcessing/HoPostProcessRendererFeature.cs
Runtime/HoPostProcessing/Shaders/HoPost/HoPostAovMask.hlsl
```

lilToon 侧：

```text
Assets/lilToon/Shader/Includes/lil_pass_hoaov.hlsl
Assets/lilToon/CustomShaderResources/URP/Default*.lilblock
```

lilPBR 侧：

```text
Shaders/hoaov.hlsl
Shaders/lilPBR.shader
Shaders/lilPBR_Tessellation.shader
```

## 维护原则

```text
HoAOV 只生产数据，不做视觉效果
HoPost 只消费 AOV 和 camera color，不重新发明 AOV 采集
Shoost 默认只处理最终画面，不默认依赖 AOV
材质 pass 负责材质语义，RendererFeature 负责收集和绑定
物体 AOV 由 HoAovGroup 空物体提供，不放进 Volume 的 renderer 列表
fallback 是过渡路径，不是质量路径
Material AOV 贴图通道固定 4 个，更多语义遮罩走 Object AOV
```

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
HoShadowCast ShadowMap（可选：主天光 / 少量强制投影光源）
lilToon / lilPBR 正常渲染
HoAOV Output
HoCharacterSpecialization（可选：眼透 / 前发投影）
HoShadowCast Composite / SSRTS Bridge（可选：主阴影与次级阴影）
HTrace AO（可选：剩余环境遮蔽）
URP Post Processing
HoPost Stack
Shoost Final Stack
Final
```

当前 HoAOV pass event 默认为 `AfterRenderingTransparents`。`HoCharacterSpecializationRendererFeature` 如果启用，建议紧跟 HoAOV 之后放置，先用 `HoCharacterCapture` 捕获 Face/Eye，再把眼透和前发投影合成回 camera color。HoShadowCast 是项目级阴影光源系统：ShadowMap 阶段在正常渲染前产出少量强制投影光源的 atlas，Composite / SSRTS Bridge 阶段在角色特化之后统一打暗或交给 SSRTS 消费；HTrace AO 只负责剩余环境遮蔽。HoPost 固定在 URP 主后处理之后、Shoost Final Stack 之前执行。详见 `HoShadowCast主灯光与阴影设计.md`。

HoAOV Output 每帧会：

1. 分配或复用 AOV RT。
2. 清空 AOV color 和 HoAOV 自己的 depth。
3. 设置全局纹理和 `_lilHoAovActive = 1`。
4. 可选绘制 fallback material。
5. 绘制所有带 `LightMode = "HoAOV"` 的材质 pass。
6. 将 AOV RT 绑定为全局纹理供 HoPost 和 debug shader 读取。

### 2026-05-17 fallback / cutout 踩坑记录

这次问题表现为：修复 RenderGraph 深度里的脏块后，lilToon cutout 材质在 Custom AOV debug 中又把被 alpha discard 的位置画成黑色。RenderDoc 帧里能看到 `lilToon-HoAOV Output` 写入 `_lilHoAovCustom0_3Texture`，同时场景里大量 cutout 材质来自 `Hidden/ltspass_cutout`。

根因不是 lilToon `HoAOV` pass 的 `clip` 失效，而是 fallback renderer list 先用 override fallback 材质绘制了 `UniversalForward` / `UniversalForwardOnly` 对象。override material 拿不到源材质的 `_MainTex.a`、`_Cutoff`、dither、dissolve 等 alpha 语义，于是会把整片 cutout mesh 写进 AOV。后续真正的 lilToon `HoAOV` pass 只能覆盖未丢弃的像素，已经被 fallback 写过的洞不会自动恢复为 clear color，于是 custom 通道里留下黑色。

当前约束：

- fallback material 只允许兜底 opaque 队列，不能碰 `AlphaTest` / cutout / transparent 队列。
- cutout、dither、dissolve、transparent 只能由材质自己的 `LightMode = "HoAOV"` pass 输出，因为只有它能复用完整 alpha 语义。
- RenderGraph 下可以把 clear 拆成独立 pass，再让 output pass 用 `ReadWrite` 附着；这能避免深度/颜色 load-store 脏块，但不能用 fallback 去补 cutout 覆盖。
- 如果以后发现 custom AOV 的镂空区域又变黑，第一检查项是 fallback renderer list 的 render queue filter 是否重新包含了 `RenderQueue.AlphaTest` 或更高队列。

## AOV 纹理契约

当前全局纹理名：

```text
_lilHoAovMaskIdTexture
_lilHoAovNormalDepthTexture
_lilHoAovTangentNormalTexture
_lilHoAovSurfaceDataTexture
_lilHoAovCustom0_3Texture
_lilHoAovObjectCustom0_3Texture
_lilHoAovObjectCustom4_7Texture
```

当前实际稳定使用的材质 custom 是 `MaterialCustom0..MaterialCustom3`，打包在 `_lilHoAovCustom0_3Texture.rgba`。`Custom4..Custom11` 的旧预留纹理已从运行时输出链路移除，不作为默认可用功能扩展。

Object AOV 使用独立的 ObjectCustom RT，不复用 `_lilHoAovCustom4_7Texture` / `_lilHoAovCustom8_11Texture` 作为物体语义输出名。材质 custom 和物体 custom 在纹理命名、debug mode 和 HoPost source 中都要分开显示。

重要警告：之前尝试把 `_lilHoAovCustom4_7Texture` 和 `_lilHoAovCustom8_11Texture` 也接成真实 custom 输入时，多次遇到 Unity / URP / SRP 在 shader import 或启动阶段直接崩溃。现象不像普通 shader 编译错误，更像 native 侧对大量材质贴图输入、SRP Batcher 常量布局或 texture binding 数量组合不稳定。因此这两张旧预留纹理已删除，不要默认给 lilToon / lilPBR 继续补 `Custom4..Custom11` 的独立贴图入口。

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

2026-05-17 决议：Object AOV 是 Renderer 级协议，Material AOV 是材质/slot/UV 级协议。Object AOV 主路径使用 Unity 6.3+ 的 Renderer Shader User Value；Material AOV 继续使用现有 HoCustomAOV，固定为 `MaterialCustom0..MaterialCustom3` 四个通道。两者编号独立，不共享 `Custom0..N` 命名空间。

RSUV 第一版打包为一个 32-bit `uint`：

```text
bits 0..7    ObjectCustom0..7 二值开关
bits 8..15   CharacterId / GroupId
bits 16..23  PartId / ObjectId
bits 24..31  flags
```

读取语义：

```text
ObjectCustom0..7  按 bit 读取，可以多选，例如 Character + FrontHair 同时为 1
CharacterId       按 8-bit 数字读取，范围 0..255，通常 0 表示未设置
PartId            按 8-bit 数字读取，范围 0..255，通常 0 表示未设置
flags             可按 bit 读取，也可作为 0..255 的小整数标记，第一版优先按 bit 读取
```

默认值约定：

```text
未命中 HoAovGroup 的 Renderer：
- unity_RendererUserValue 应为 0
- ObjectCustom0..7 = 0
- CharacterId / GroupId = 0
- PartId / ObjectId = 0
- Flags = 0

HoAovGroup 组件自身：
- CharacterId / PartId / Flags 默认都是 0
- 只有拖进 ObjectCustom 列表或“仅写 ID”列表的 Renderer 才会写入 RSUV
```

历史踩坑：早期 fallback 和 lilToon/lilPBR pass 会在 `_HoAovObjectId = 0` 时用物体位置生成 `objectSeed`，导致“没有指定任何 AOV 的物体”也显示出随机 ObjectId。这个行为已经废弃；0 就是 0，不再自动生成对象 ID。`_HoAovFlags` 的 shader property、RendererFeature 全局默认和 `HoAovSubject` 默认也必须保持 0。

`CharacterId` 不是 8 个角色开关，`PartId` 也不是 8 个部件开关。脸、前发、眼睛、配件这类可以同时成立的语义放在 `ObjectCustom0..7`；角色编号和部件编号放在 `CharacterId` / `PartId` 里，用于同角色比较、眼透和角色合成。

`ObjectCustom0..7` 是二值开关，不是 8 个可贴图的 float 通道。需要 UV 细节、软遮罩或材质 slot 级差异时，继续走 `MaterialCustom0..3`。如果同一个 Renderer 内部混合了需要不同 Object AOV 的多个 submesh，应优先拆 Renderer；不能拆时才用 Material AOV 承接局部差异。

RSUV 只回答“这个 Renderer 是什么”，不回答“哪些像素存在”。cutout、dither、dissolve 和 transparent 的像素存在性仍由材质自己的 `LightMode = "HoAOV"` pass 决定；fallback 和 RSUV 都不能替代材质 alpha 语义。

容量规则：

```text
贴图型 AOV：最多 4 个，只属于 Material AOV
物体型 AOV：默认预留 8 个，只由 HoAovGroup 批量标记
不要把 ObjectCustom4..7 解释成材质 Custom4..7
不要再给 lilToon/lilPBR 追加更多 custom 贴图入口
```

这样做的原因是：之前崩溃风险来自大量材质 texture binding，而物体 AOV 可以走 RSUV、兼容模式下的 MaterialPropertyBlock 或未来的对象 buffer，不需要新增 sampler。眼透、角色分层合成、前发/脸/眼睛分组这类需求也更适合 Object AOV，而不是材质贴图。

## 物体 AOV 指定方式

Object AOV 第一版应挂在空物体上，而不是塞进 Volume，也不是强制每个 Renderer 都挂组件。建议组件名：

```text
HoAovGroup
- enabled
- characterId / groupId
- partId / objectId
- flags
- explicitRenderers[] / 仅写 ID
- ObjectCustom0 Character      objects[]
- ObjectCustom1 Face           objects[]
- ObjectCustom2 FrontHair      objects[]
- ObjectCustom3 Eye            objects[]
- ObjectCustom4 EyeRevealArea  objects[]
- ObjectCustom5 Accessory      objects[]
- ObjectCustom6 Reserved       objects[]
- ObjectCustom7 Reserved       objects[]
- includeChildrenForListedObjects / 展开预制件
- priority
```

用法约定：

```text
角色根节点挂一个 HoAovGroup，控制整套角色的 groupId / flags。
头发、脸、眼睛、配件等子空物体可以再挂 HoAovGroup 覆盖局部分组。
编辑器显示 8 个 ObjectCustom 列表，列表里拖 GameObject 或 Renderer。
拖进 ObjectCustomN 列表表示命中 Renderer 的对应 bit 写 1。
同一 Renderer 可以同时出现在多个 ObjectCustom 列表里，最终 bit 按 OR 合并。
“仅写 ID”列表用于只写 CharacterId / PartId / Flags，不写任何 ObjectCustom bit。
“展开预制件”为整个组件级开关，ObjectCustom 列表和“仅写 ID”列表都遵守它。
“展开预制件”为 true 时，列表中的 GameObject 会收集子层级 Renderer。
同一个 Renderer 命中多个 HoAovGroup 时，priority 高者覆盖；priority 相同时离 Renderer 最近的层级覆盖。
没有命中 HoAovGroup 的 Renderer 使用材质默认 AOV 和系统默认值。
```

列表项可以混合 `GameObject` 和 `Renderer`，实现上使用 `UnityEngine.Object` 引用并在 Editor 里校验类型。`GameObject` 会按 `includeChildrenForListedObjects` 展开为 Renderer；`Renderer` 只影响自身。不要接受 `Mesh`、`MeshFilter` 或 mesh asset，因为 Object AOV 写入目标是 Renderer，不是网格资源。

Prefab 的正确用法是在 prefab 内部挂 `HoAovGroup`，并引用同一 prefab stage 内的子物体或 Renderer；实例化后组件再把 RSUV 写到实例 Renderer。不要把 prefab asset 拖到另一个场景对象的 `HoAovGroup` 列表里期待它自动标记某个场景实例，这种跨上下文引用应在 Editor 中警告或拒绝。

`HoAovGroup` 的序列化字段是 authoring 源。RSUV 值本身不序列化，进入 Play Mode、场景载入、prefab 实例化或组件校验后都要重新写入目标 Renderer。Unity 6.3+ 使用 RSUV；RSUV API 不可用时才走 MaterialPropertyBlock 兼容路径，并提示 SRP Batcher 风险。

Inspector 的“刷新全场景 RSUV”按钮必须显式清理当前已加载场景中的所有 Renderer，再重建当前启用的 `HoAovSubject` 和 `HoAovGroup`。它不是只刷新当前组件。清理内容包括 `SetShaderUserValue(0)` 和 HoAOV 相关 MPB 字段，目的是处理旧版本、删组件、改列表或切 prefab 后遗留的 RSUV/MPB 脏值。

### HoAovGroup 使用方法

普通对象/角色分组只用 `HoAovGroup`。推荐在角色 prefab 根节点挂一个 `HoAovGroup`，设置 `CharacterId`，然后按语义把对象拖进 8 个列表：

```text
ObjectCustom0 Character      拖角色根节点或身体/衣服等主体对象
ObjectCustom1 Face           拖脸部对象
ObjectCustom2 FrontHair      拖前发对象
ObjectCustom3 Eye            拖眼睛对象
ObjectCustom4 EyeRevealArea  拖允许眼睛透出的区域对象
ObjectCustom5 Accessory      拖配件对象
ObjectCustom6 Reserved       项目自定义
ObjectCustom7 Reserved       项目自定义
```

拖 `GameObject` 时，如果 `列表物体包含子级` 打开，会把底下所有 Renderer 都标上对应 bit；拖 `Renderer` 时只影响这个 Renderer。一个 Renderer 可以出现在多个列表里，最终 bit 会 OR，例如前发可以同时在 `Character` 和 `FrontHair` 中。

`CharacterId` / `PartId` 是 0..255 的数字。`ObjectCustom0..7` 才是 8 个可多选开关。多角色眼透或角色合成时，前发、眼睛和眼透区域应使用同一个 `CharacterId`，合成时再比较 SameCharacterId。

### HoAovSubject 使用方法

`HoAovSubject` 不是 Object AOV 分组入口。它是高级/兼容覆盖组件，用于把系统 AOV、旧式 ID、厚度、曲率或 `MaterialCustom0..3` 值通过 MaterialPropertyBlock 写到子级 Renderer。普通角色部件分组不要用它，优先用 `HoAovGroup`。

典型使用场景：

```text
需要临时覆盖某个对象的 maskWeight / thickness / curvature
需要用 MPB 覆盖 MaterialCustom0..3，而不是改材质资产
需要兼容旧的 HoAovSubject 场景
```

注意：`HoAovSubject` 使用 MaterialPropertyBlock，可能影响 SRP Batcher。Object AOV 的 `CharacterId / PartId / ObjectCustom0..7` 主路径由 `HoAovGroup` 写 RSUV，不要在 `HoAovSubject` 里寻找这些列表。

`HoAovSubject` 是兼容覆盖路径，不应该给默认对象制造身份。默认 `objectId = 0` 就写 0，不能自动 fallback 到组件实例 ID；默认 `flags = 0`，不能默认写 1。禁用或销毁 `HoAovSubject` 时必须清掉它写过的 MPB，否则旧的 GroupId/ObjectId/Flags 会继续留在 Renderer 上，让未分组物体看起来像仍然有 RSUV。

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

ObjectCustom0_3.r    ObjectCustom0
ObjectCustom0_3.g    ObjectCustom1
ObjectCustom0_3.b    ObjectCustom2
ObjectCustom0_3.a    ObjectCustom3

ObjectCustom4_7.r    ObjectCustom4
ObjectCustom4_7.g    ObjectCustom5
ObjectCustom4_7.b    ObjectCustom6
ObjectCustom4_7.a    ObjectCustom7
```

GroupId / CharacterId、ObjectId / PartId、Flags 这类 0..255 ID 值直接写 raw byte normalized，即 `round(value) / 255`。MaterialClass 仍然写 `frac(abs(value) * 0.61803398875)` 的稳定编码值，用来表达材质分类而不是 byte ID。HoPost 做数值匹配时必须按 source 类型选择同一套解析逻辑。

## HoPost 的 AOV Mask

每个 HoPost 图层都有一组通用 AOV mask 设置：

```text
useAovMask
aovSource
aovMaskMode
aovThreshold
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

Object AOV 接入时，HoPost 的 `HoPostAovSource`、公共 shader 选择函数和 Editor source 下拉都必须继续增加：

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

这些 source 读取 `_lilHoAovObjectCustom0_3Texture` 和 `_lilHoAovObjectCustom4_7Texture`，不要读取材质 custom 的预留纹理。HoPost 的 `debugAovMask` 必须能输出 ObjectCustom 匹配结果，用来验证某个图层实际消费到的 object mask，而不只是 HoAOV 原始 debug view。

`aovMaskMode` 当前含义：

```text
Direct      直接使用通道灰度
MatchValue  匹配数值，GroupId/ObjectId/Flags 按 0..255 byte 匹配；Material 会先编码目标值再匹配
MatchColor  从同一张 packed texture 取 RGB，按颜色距离匹配
```

公共 shader 方法位于：

```text
Runtime/HoPostProcessing/Shaders/HoPost/HoPostAovMask.hlsl
```


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
- 不使用 aovMatchValue
- 不使用 aovMatchColor

Threshold / 阈值：
- 使用 aovThreshold 作为选中起点
- 不使用 aovMatchValue
- 不使用 aovMatchColor

MatchValue / 匹配数值：
- 使用 aovMatchValue 作为目标值
- 使用 aovThreshold 作为数值容差
- 不使用 aovMatchColor
- GroupId、ObjectId、Flags 会按 0..255 byte 归一化比较；Material 会把目标值先做稳定编码再比较
- Mask、Thickness、Curvature、Utility、MaterialCustom0..3、ObjectCustom0..7 使用原始标量值比较

MatchColor / 匹配颜色：
- 使用 aovMatchColor.rgb 作为目标颜色
- 使用 aovThreshold 作为 RGB 距离容差
- 不使用 aovMatchValue
- 颜色来自所选 AOV 源所在的 packed texture 的 RGB
- MaterialCustom 和 ObjectCustom 通常更适合 Direct / Threshold / MatchValue；MatchColor 只在确实把颜色语义打进 packed texture 时使用
```

参数打包约定：

```text
_LayerAovParams.x = threshold / tolerance
_LayerAovParams.y = reserved
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

### DepthOfField

景深是 HoPost 的真实相机深度效果，读取 `_CameraDepthTexture` 计算 CoC，并可叠加 HoPost 的通用 AOV mask。当前模式包括 Gaussian、Bokeh 和目标跟焦 Bokeh。目标跟焦模式会优先使用图层里的 `depthOfFieldFocusTarget`，如果 Volume Profile 作为 asset 无法稳定保存场景对象引用，则回退到 `depthOfFieldFocusTargetPath` 层级路径，用当前相机到目标中心的 eye-depth 动态覆盖焦点距离。

当前 Bokeh 参数里 `parameters1.z` 是最大模糊半径，`parameters3.x/y/z/w` 分别是景深强度、前景虚化、远景虚化和虚化曲线。需要“差别很大”的镜头过渡时，优先使用目标跟焦 Bokeh 预设，再拉高最大半径与景深强度；Shoost 的 `BokehZoomBlur` / `ApertureBokeh` 仍属于最终画面光斑/散景层，不替代这个真实深度景深。

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
fallback 默认 GroupId/ObjectId/Flags/ObjectCustom 全部为 0
fallback 不允许在 ObjectId 为 0 时生成随机 objectSeed
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
Material AOV 由现有 HoCustomAOV 承接，不扩展到 MaterialCustom4..11

Object AOV 目标支持 ObjectCustom0..ObjectCustom7
ObjectCustom 是 Renderer 级二值开关，不允许贴图输入
ObjectCustom 默认值必须是 0
ObjectCustom 由 HoAovGroup 空物体批量指定
ObjectCustom 主路径通过 RSUV 写入 Renderer，shader 读取 unity_RendererUserValue
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

Object AOV 排查时，先在 HoAOV debug view 中看 `ObjectCustom0..7` 原始通道，确认 `HoAovGroup` 列表、RSUV 打包和材质 HoAOV pass 都正确写入；再到 HoPost 图层打开 `debugAovMask`，确认该图层选择的 `ObjectCustomN` source 能被消费端解析为预期遮罩。

### Debug View 读到的是什么

HoAOV debug view 是 fullscreen 后处理，它读取的是已经写入 HoAOV MRT 的结果，不是直接读 `HoAovGroup`、`HoAovSubject`、材质面板或 `unity_RendererUserValue` 原始值。

```text
_lilHoAovMaskIdTexture.r    AOV coverage / mask
_lilHoAovMaskIdTexture.g    raw normalized CharacterId / GroupId
_lilHoAovMaskIdTexture.b    raw normalized PartId / ObjectId
_lilHoAovMaskIdTexture.a    raw normalized Flags
ObjectCustom RT             ObjectCustom0..7 的最终二值结果
```

debug 里看到“没有绘制”通常表示该像素 `maskId.r = 0`，也就是没有 HoAOV coverage。未进 `HoAovGroup`、没有 `HoAovSubject` 覆盖、材质默认值也是 0 的对象，即使材质 HoAOV pass 写入 coverage，也不应该在 `RSUV 总览 / 角色组 ID / 部件 ID / 标记 / 仅写 ID` 中显示有效 ID。0 值不应被画成黑色覆盖层，否则会误判成“有数据但颜色很暗”。

ID debug 的颜色是把 raw normalized ID 值 hash 成稳定伪彩色，只用于人眼区分，不是后处理消费的数据源。HoPost 或其他消费者必须读取 AOV RT 中的 raw normalized 值和 mask，不能读取 debug 画面颜色。

`RSUV 仅写 ID` 的 debug 语义是：有 CharacterId 或 PartId 信号，但没有任何 ObjectCustom bit。它用于检查“仅写 ID”列表，而不是检查所有未分组物体。没有组的物体应该不亮。

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

没有放进 HoAovGroup 的 lilPBR/lilToon 物体在 RSUV debug 里亮
=> 先点 HoAovGroup 的“刷新全场景 RSUV”清旧值；再查是否有 HoAovSubject、材质/MPB 隐藏值或旧 shader property 把 GroupId/ObjectId/Flags 写成非 0

角色组 ID 或 Flags debug 里整块变黑，看起来像有数据
=> debug shader 可能把 0 值当成有效覆盖显示；正确规则是只有非 0 ID/Flags 才覆盖显示
```

## 使用流程

项目侧使用时：

1. URP Renderer Data 加 `HoAovRendererFeature`。
2. URP Renderer Data 加 `HoPostProcessRendererFeature`，并放在 Shoost 之前。
3. Volume Profile 添加 `lilToon-HoPost / Process Stack`。
4. lilToon/lilPBR 材质在 HoAOV 栏设置 `MaterialCustom0..MaterialCustom3` 的灰度颜色和贴图。
5. 需要物体/部件分组时，在角色根节点、头发、脸、眼睛或配件空物体上挂 `HoAovGroup`。
6. `HoAovGroup` 的 8 个 ObjectCustom 列表中拖入 GameObject 或 Renderer，表示对应 ObjectCustom bit 写 1。
7. Unity 6.3+ 下 `HoAovGroup` 将最终 object mask、characterId、partId 和 flags 打包进 RSUV；RSUV 不可用时才走 MPB 兼容模式。
8. 只有需要覆盖系统 AOV、厚度、曲率或 MaterialCustom0..3 时，才额外挂 `HoAovSubject`。
9. HoPost 图层打开 `AOV Mask`，选择需要的 source 和 mask mode。
10. 调试时先看 HoAOV debug view 的原始 `ObjectCustom0..7`，再用 HoPost 图层的 `debugAovMask` 确认消费结果。
11. 发现旧 RSUV 或 MPB 清不掉时，点 `HoAovGroup` 的“刷新全场景 RSUV”。这个按钮会清所有已加载场景 Renderer，再重建所有启用的 `HoAovSubject` 和 `HoAovGroup`。

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

## 2026-05-17 AOV 遮罩规则组工作模式

当前 HoPost 和 ShoostStack 的 AOV 遮罩升级为规则组，而不是单条规则。目标是让 ID 成为主要选择手段，同时允许 ID、Object AOV 和 Material AOV 混合：

```text
规则 0: 角色组 ID 范围 1..3
规则 1: AND 主体
规则 2: OR 眼睛
规则 3: Subtract 前发
```

UI 入口是每个图层内部的折叠 box：

```text
AOV 遮罩
- 启用
- 输出匹配结果
- 最终反转
- 规则列表（最多 4 条）
```

规则列表最多 4 条。每项显示启用、名称、AOV 源、匹配方式、匹配参数、混合方式和反转本规则。匹配方式至少包括直接灰度、阈值、大于、大于等于、小于、小于等于、等于、不等于、范围、匹配颜色、包含任意标记 bit 和包含全部标记 bit。混合方式至少包括 Replace、Or、And、Subtract、Add、Multiply。

ID 通道按 raw normalized 值消费：

```text
GroupId / CharacterId = rawId / 255
ObjectId / PartId     = rawId / 255
Flags                 = rawFlags / 255
```

因此 ID 可以做小于、大于、范围和等值比较。debug shader 可以把 raw ID hash 成伪彩显示，但 HoPost/ShoostStack 不能把 debug 颜色当作数据源。Flags 的 bit 匹配以 0..255 的整数语义解析，shader 侧从归一化值还原为 8-bit mask 后执行 Any/All。




2026-05-17 更新：AOV 规则组现在为硬 0/1 判断，规则参数不再包含过渡宽度字段。

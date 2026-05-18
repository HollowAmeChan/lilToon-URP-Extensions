# HoAOV 角色特化眼透与 DropShadow 研究

本文研究如何用现有 HoAOV 系统平替传统 lilToon 的前发眼透与假投影阴影流程，并把它收敛成一个独立的角色特化 RendererFeature。目标不是继续让用户复制三份头发材质、手改 stencil 和队列，而是把“脸、前发、眼睛、眼透区域、同角色关系”交给 HoAOV / HoAovGroup，再由一个统一的角色特化小管线完成眼透、前发投影，以及未来的角色专属阴影、反射和局部光照扩展。

建议新功能命名：

```text
HoCharacterSpecializationRendererFeature
```

中文面板可以叫“角色特化”。它消费 HoAOV，按模块申请额外缓存，输出回 camera color，默认放在 HoAOV 之后、HoPost 之前。

## 传统 lilToon 流程复盘

现有文档 `liltoon原生眼透与dropshadow制作.md` 描述的是一套 stencil + render queue + 多材质槽方案：

```text
眉毛/眼睛     queue 约 1999，Stencil Always / DecrementWrap
脸            Stencil Always / Replace(ref)
前发          Stencil GreaterEqual / Zero(ref)
前发半透补偿  透明模式，Stencil Always，主色 alpha mask Replace
前发假阴影    _lil/[Optional] lilToonFakeShadow，Stencil Equal(ref)，乘法混合
```

它的关键不是材质本身多复杂，而是用绘制顺序把 stencil 当成一个临时的“眼睛/脸/头发交互状态机”。`lilToonFakeShadow` shader 也很轻：URP 模板 `DefaultFakeShadow.lilblock` 只有一个 forward pass，读取普通 stencil / blend / zwrite 参数；`lil_pass_forward_fakeshadow.hlsl` 在顶点阶段按灯光方向和 `_FakeShadowVector` 偏移 clip space 位置，片元阶段只采样 `_MainTex * _Color` 并输出。默认 shader 名是 `_lil/[Optional] lilToonFakeShadow`，队列是 `AlphaTest+55`，属性里 `_SrcBlend=DstColor`、`_DstBlend=Zero`，也就是常见的乘法压暗。

所以传统方案的本质是：

```text
1. 用 stencil 找到“脸/眼睛/前发重叠”的屏幕区域。
2. 用重复绘制的前发材质做局部半透明。
3. 用 FakeShadow 的偏移几何做前发投影，并用乘法混合压到脸上。
```

这个方案能跑，但维护成本很高：材质槽翻倍、队列必须手动排、描边 stencil 也要同步改、侧发和前发要人工拆，材质参数修改还要同步多份副本。

## 传统方案的硬伤

最重要的问题是用户提到的半透明眉毛/眼睛透过问题。

传统流程里，眼透不是“重新取得被头发遮住的眼睛颜色”，而是“把前发再画一遍半透明”。如果眉毛或眼睛本身是半透明材质，它们正确的颜色取决于背后的脸、眼白、虹膜、描边、透明排序和材质 alpha。当前发已经遮住这些像素后，最终 camera color 里并没有可恢复的半透明眉眼结果；单纯让头发透明，只是在已有颜色上混合头发副本，无法可靠重建“半透明眉眼叠在脸上”的结果。

换句话说：

```text
最终画面 + AOV mask 只能告诉我们“哪里应该透”
但不能告诉我们“被头发遮住的半透明眉眼原本是什么颜色”
```

因此新系统如果要真正解决半透明眉眼，需要额外的 Eye/Face Color Buffer。只靠 HoAOV 的 mask、depth、ObjectCustom 不够。

## 现有 HoAOV 能直接复用什么

当前 HoAOV 已经很接近角色特化需求。

Object AOV 语义已经预留：

```text
ObjectCustom0 = Character / 主体
ObjectCustom1 = Face
ObjectCustom2 = FrontHair
ObjectCustom3 = Eye
ObjectCustom4 = EyeRevealArea
ObjectCustom5 = Accessory
ObjectCustom6 = Reserved
ObjectCustom7 = Reserved
```

`HoAovGroup` 已经可以把这些语义通过 RSUV 写进 Renderer：

```text
bits 0..7    ObjectCustom0..7
bits 8..15   CharacterId
bits 16..23  PartId
bits 24..31  Flags
```

lilToon 的 URP 模板也已经有 `LightMode = "HoAOV"` pass，`lil_pass_hoaov.hlsl` 会输出：

```text
_lilHoAovMaskIdTexture
_lilHoAovNormalDepthTexture
_lilHoAovTangentNormalTexture
_lilHoAovSurfaceDataTexture
_lilHoAovCustom0_3Texture
_lilHoAovObjectCustom0_3Texture
_lilHoAovObjectCustom4_7Texture
```

HoPost 的 AOV 规则组也已经支持 `ObjectCustom0..7`。这意味着“选择前发”“选择眼睛”“选择脸”“按角色 ID 限定同一角色”这些用户语义都不需要重新发明。

已修正的一个关键风险：`HoPostAovMask.hlsl` 按 byte-normalized 方式消费 `GroupId / ObjectId / Flags`，fallback shader 也是 `value / 255` 写入；lilToon 的真实 `lil_pass_hoaov.hlsl` 也必须保持同一语义。角色特化依赖 SameCharacterId，禁止再把这些 ID 写成 debug 用 hash。

## 为什么要单开角色特化 RendererFeature

现有 HoPost DropShadow 是通用主体投影：

```text
shadow = offset(mask) - mask
```

它适合“整个人物有一个屏幕空间投影”这种效果，但前发眼透的 DropShadow 有两个不同对象：

```text
投影源：FrontHair
接收面：Face / Eye / EyeRevealArea
```

通用 DropShadow 只有一个 mask，无法自然表达“偏移前发 mask，但只压暗同角色的脸和被眼透恢复出来的眼睛”。眼透还需要 Eye Color Buffer，这已经超出普通 HoPost 图层的职责。

所以推荐独立 RendererFeature：

```text
HoAOV  = 生产对象语义、mask、normal、depth
角色特化 = 用这些语义做眼透、前发投影、同角色限制、额外眼部颜色捕获
HoPost = 继续做通用边缘光、轮廓、普通投影、景深等
```

渲染顺序建议：

```text
OIT / Transparent Resolve
HoAOV
HoCharacterSpecialization
URP Post Processing
HoPost
Shoost Final Stack
Final
```

如果项目希望 URP Post Processing 先发生，也可以把角色特化放在 URP Post 之后、HoPost 之前；但第一版建议紧跟 HoAOV，减少后处理调色对捕获和调试的干扰。

## 角色特化的长期边界

这个 RendererFeature 不应该被设计成单一“眼透插件”。更合适的定位是：

```text
HoCharacterSpecialization = 角色专属屏幕/局部空间合成管线
```

它和 HoPost 的区别是：HoPost 处理通用画面效果，通常只关心“一个 mask 作用到当前画面”；角色特化处理“同一个角色内部的源/接收面/颜色缓存/局部光照关系”，例如前发投到脸、眼睛从前发下透出、角色局部反射、只对角色生效的点光阴影。

推荐模块边界：

```text
HoCharacterSpecializationRendererFeature
- 读取 HoAOV 与角色注册表
- 统一分配角色特化 RT
- 执行启用的模块
- 统一 debug 输出

模块
- EyeReveal
- HairDropShadow
- FarPlaneShadow
- ReflectionSpace
- CharacterLocalLightCast
- Reserved / Custom
```

每个模块只声明自己需要的输入和输出，不直接偷偷分配全局纹理。RendererFeature 负责资源生命周期、RenderGraph/compatibility 双路径、MSAA/尺寸匹配、debug 和执行顺序。

建议内部顺序：

```text
1. Character registry / bounds / screen rect
2. Capture passes，例如 Face/EyeColor、caster depth、reflection source
3. Mask building，例如 EyeRevealMask、HairShadowMask
4. Lighting/reflection/shadow modules
5. Composite back to camera color
6. Debug overlay
```

## 未来扩展口子

### 远平面阴影 / Far Plane Shadow

远平面阴影用于角色在地面、墙面、远处投影平面或风格化 matte 上产生稳定阴影。它不应该依赖场景里真的存在接收阴影的 mesh，也不应该强迫整套场景启用昂贵实时阴影。

推荐做成角色特化模块：

```text
输入:
- Character / caster mask
- 角色 bounds 或 screen rect
- 主光方向或自定义投影方向
- 虚拟接收平面 origin / normal / size / fade
- 可选角色深度或 caster depth

输出:
- _lilHoCharacterFarPlaneShadowTexture
- 合成到 camera color，或输出给后续模块
```

第一版可以是屏幕空间或平面投影 mask：把角色主体 mask 按方向偏移、压扁、模糊后投到虚拟平面。高级版再做角色 caster depth map，让发丝、裙摆、手臂这类形状更准确。

它需要 HoAOV 之外的信息：

```text
角色世界 bounds
投影接收平面定义
投影方向/距离/淡出
可选 caster depth，不只是最终可见 AOV depth
```

### 角色动态反射空间 / Reflection Space

角色动态反射空间用于角色专属的局部反射、眼睛高光、金属/湿润材质反射、舞台风格反射，或未来和 PlanarReflection / probe capture 的桥接。它不应该变成全场景反射系统，而应该先服务角色材质和角色局部合成。

推荐抽象：

```text
HoCharacterReflectionSpace
- anchor transform
- bounds / radius
- capture mode: None / Planar / Cubemap / ScreenFallback
- update rate
- culling mask
- blend weight
- optional per-character reflection texture
```

可能的数据流：

```text
Capture:
  按角色 anchor 或平面捕获反射源

Resolve:
  读取角色 AOV mask / normal / material class
  按材质或 ObjectCustom 限制反射作用范围

Composite:
  只影响同角色像素，或把反射纹理暴露给 shader 采样
```

它需要 HoAOV 之外的信息：

```text
角色级反射 anchor / volume
反射 capture 纹理和更新策略
材质 roughness / reflectance / reflection weight
反射只作用哪些部件的规则
```

当前 HoAOV 的 `SurfaceData.b = Material` 和 `MaterialCustom0..3` 可以承接一部分开关，但没有真正的 roughness、specular、reflection weight。未来如果反射空间要做得好，需要新增角色特化 surface capture，或在 lilToon/lilPBR HoAOV pass 中额外输出可选材质响应数据。

### 角色多光源投射 / Character Local Light Cast

这里的目标不是让所有点光/聚光都给场景投实时阴影，而是让少量被选中的点光、聚光或特效灯只对角色产生可信的局部阴影。例如舞台灯扫过角色、手持灯照到脸、头发或帽檐在脸上留下阴影。

推荐先把它定义成可选高级模块，而不是第一版就实现：

```text
HoCharacterLocalLightCast
- max lights per camera / per character
- light filter: layer, tag, component, distance, importance
- caster set: Character / FrontHair / Accessory / custom flags
- receiver set: Face / Body / Eye / custom flags
- shadow atlas scale
- softness / bias / opacity
```

实现路径可以有两种：

```text
屏幕空间接收:
  为选中光源渲染角色 caster shadow map
  在 fullscreen composite 中用 AOV depth/normal/world position 判断接收
  只修改 camera color

材质侧接收:
  把角色局部阴影图作为全局纹理给 lilToon/lilPBR shader 采样
  更接近真实光照，但侵入材质管线更深
```

第一版如果要做，建议先走屏幕空间接收，最多 1 到 2 个额外光，且默认关闭。它比 URP 全局 additional light shadows 更可控，因为它的 caster/receiver 都是角色语义，不污染场景，也不用让普通环境对象进 shadow map。

它需要 HoAOV 之外的信息：

```text
候选点光/聚光列表
每个光源的矩阵、范围、角度、颜色、重要性
角色 caster renderer list 或 caster depth atlas
接收像素的 world position，或可从 depth 重建
receiver 材质阴影强度/是否接收局部光阴影
```

### 未知未来模块

为了给还没想到的功能留口子，角色特化不要把所有参数写成几个固定 bool。建议用模块化 settings：

```text
HoCharacterSpecializationSettings
- modules[]
- debugMode
- renderScale
- sharedResourceBudget

HoCharacterModuleSettings
- enabled
- moduleType
- executionOrder
- aovRequirements
- resourceRequests
- customMaterial / customShader
```

第一版可以仍然用强类型字段实现，不必一开始做完全动态插件系统；但代码结构上应预留：

```text
IHoCharacterModule
- CollectRequirements()
- AllocateResources()
- RecordRenderGraph()
- ExecuteCompatibility()
- Release()
- DrawDebug()
```

这样未来加“角色残影”“局部风格化描边”“角色专属雾遮挡”“装备发光遮罩”“角色 ID 分层调色”时，不需要继续膨胀 EyeReveal shader。

## HoAOV 当前缺口

本次角色特化会消费 HoAOV，但不能假设 HoAOV 已经提供所有数据。当前需要补或绕开的缺口如下。

1. Raw byte ID 语义必须统一。
   - 角色特化需要 SameCharacterId。
   - `GroupId / PartId / Flags` 应按 `value / 255` 可比较数据写入。
   - debug hash 只能在 debug shader 里做，不能写进 AOV RT。

2. HoAOV 只有最终可见层，不提供被遮挡对象颜色。
   - 眼透需要被前发遮住的 `Face + Eye` 颜色。
   - 这必须由角色特化的 capture pass 重新绘制，不应塞回普通 HoAOV MRT。

3. HoAOV 不提供角色级 bounds / screen rect。
   - 未来远平面阴影、局部反射和 per-character atlas 都需要按角色裁剪。
   - 建议新增 `HoCharacterProfile` 或扩展 `HoAovGroup`，提供角色根、bounds 计算、screen rect 和 per-character index。

4. HoAOV 的 ObjectCustom 是 8 个二值部件开关，不是完整角色工作流。
   - 未来需要 `CastShadow`、`ReceiveShadow`、`ReflectionCaster`、`ReflectionReceiver`、`IgnoreCharacterFX` 等语义。
   - 可优先使用 `Flags` 的 8 bit 定义角色特化语义，不够再考虑第二个 flags 通道或角色 profile 列表。

5. HoAOV 不提供材质响应数据。
   - 反射需要 roughness / reflection weight。
   - 局部光阴影需要 receive intensity / shadow tint。
   - 第一版可用 `MaterialCustom0..3` 或 `SurfaceData.Utility` 近似，长期应考虑角色特化 surface capture。

6. HoAOV 不提供 caster renderer list。
   - RSUV 能让 shader 知道像素属于谁，但 RenderFeature 若要渲染 shadow map / reflection capture，仍需要 renderer 集合。
   - 建议角色特化维护 `HoCharacterRegistry`，从 `HoAovGroup` 或新 `HoCharacterProfile` 收集 Face/Eye/FrontHair/Character renderer lists。

7. HoAOV 不提供多光源数据。
   - 点光/聚光筛选、矩阵、shadow atlas、importance 都应由角色特化自己管理。
   - HoAOV 只提供接收像素的角色语义和可重建位置。

8. HoAOV 不解决透明颜色排序。
   - OIT 可解决部分透明叠加，但 EyeColor capture 仍要明确绘制顺序。
   - Face/Eye capture 必须复用材质 alpha 语义，并小心和 OIT 的执行顺序配合。

## 新系统的数据设计

角色特化至少需要这些输入：

```text
CameraColor
_lilHoAovMaskIdTexture
_lilHoAovNormalDepthTexture
_lilHoAovObjectCustom0_3Texture
_lilHoAovObjectCustom4_7Texture
```

还需要一层角色注册数据。HoAOV 的 RT 是逐像素数据，但角色特化还需要知道“场景里有哪些角色、每个角色有哪些 Renderer、bounds 是多少、哪些模块启用”。建议新增或预留：

```text
HoCharacterProfile
- characterId
- root
- boundsMode: RendererBounds / Manual / Capsule
- modules override
- reflection anchor
- shadow plane override
- renderer groups cache

HoCharacterRegistry
- 每帧收集启用的 HoCharacterProfile
- 建立 characterId -> profile 映射
- 计算 screen rect / world bounds
- 给模块提供 renderer list / bounds / anchors
```

短期可以让 `HoCharacterProfile` 复用 `HoAovGroup.characterId`，甚至先不暴露独立组件；但代码上不要把角色信息只藏在 AOV RT 里。只靠逐像素 AOV 无法高效做反射捕获、shadow atlas、远平面阴影裁剪和 per-character debug。

为解决半透明眉眼，还需要新建临时 RT：

```text
_lilHoCharacterEyeColorTexture
_lilHoCharacterEyeAlphaTexture
_lilHoCharacterEyeDepthTexture
_lilHoCharacterRevealMaskTexture
_lilHoCharacterHairShadowTexture
_lilHoCharacterFarShadowTexture       // 未来模块
_lilHoCharacterReflectionTexture      // 未来模块
_lilHoCharacterLocalLightShadowAtlas  // 未来模块
```

其中 `EyeColor` 不是只画眼睛，它应该先有脸部底色，再把眼睛/眉毛按原材质透明规则画上去。否则半透明眉毛背后没有脸色，还是会错。

推荐捕获语义：

```text
Face Capture:
- 绘制 ObjectCustom1
- 提供半透明眼睛背后的脸部底色
- 默认写 EyeColor，不写 EyeAlpha

Eye Capture:
- 绘制 ObjectCustom3
- 包含眼睛、眉毛、睫毛等希望透出的面部细节
- 按原材质 alpha 混合到 EyeColor
- 同时写 EyeAlpha，作为最终 reveal 权重
```

如果用户需要更大的眼透区域，可以用两种方式扩展：

```text
ObjectCustom4 EyeRevealArea 作为粗粒度允许区域
MaterialCustom0..3 作为前发或眼部的 UV 级软遮罩
```

不要把 `ObjectCustom4` 理解为贴图；它仍然是 Renderer 级开关。

## 眼透合成算法

第一版建议做屏幕空间合成，而不是回到 stencil 重绘。

核心 mask：

```text
frontHair = ObjectCustom2
eyeAlpha = _lilHoCharacterEyeAlphaTexture
eyeRevealArea = ObjectCustom4 或 1
sameCharacter = 当前像素 CharacterId 与捕获角色 CharacterId 相同
hairInFront = frontHair && hairDepth <= eyeDepth + depthBias
revealMask = frontHair * eyeAlpha * eyeRevealArea * sameCharacter * hairInFront
```

合成：

```text
final.rgb = lerp(final.rgb, eyeColor.rgb, revealMask * revealStrength)
```

这个模型比传统方案强的地方在于：`eyeColor.rgb` 已经是“脸 + 半透明眼睛/眉毛/睫毛”的结果。眉毛 alpha 是 0.35 时，它会以 0.35 混在脸上再被拿去透出，而不是让前发透明后碰运气。

可选参数：

```text
Reveal Strength
Reveal Blur / Feather
Reveal Dilation
Depth Bias
Use EyeRevealArea
Use MaterialCustom as reveal refine mask
Same Character Only
```

多角色场景必须启用 Same Character Only，否则 A 角色的前发可能透出 B 角色的眼睛。

## 前发 DropShadow 算法

前发投影不要直接复刻 lilToonFakeShadow 的几何偏移，第一版可以用 HoAOV 做屏幕空间阴影：

```text
hairShadowSource = ObjectCustom2
shiftedHair = offset(hairShadowSource, direction, distance)
receiver = ObjectCustom1 | eyeRevealMask
shadowMask = shiftedHair * receiver * sameCharacter
shadowMask = shadowMask - hairShadowSource * keepOffHair
```

合成推荐用乘法或 lerp 到阴影色：

```text
multiply:
final.rgb *= lerp(1.0, shadowColor.rgb, shadowMask * opacity)

lerp:
final.rgb = lerp(final.rgb, shadowColor.rgb, shadowMask * opacity)
```

为了接近传统 fakeshadow，默认可以使用乘法，阴影色偏粉/灰，支持 softness、spread、distance、angle。投影方向可以先用屏幕空间角度；后续再加“跟随主光方向”模式。

前发阴影的接收面需要包含眼透结果。否则“眼睛被透出后没有被前发阴影压暗”，会看起来像贴在头发上。推荐执行顺序：

```text
1. 计算 eyeRevealMask
2. 先把 EyeColor 合成回 camera color
3. 再把 hairShadow 乘到 camera color
```

如果希望阴影也影响 EyeColor 内部，可在 shadow pass 中把 `receiver` 包含 `eyeRevealMask`，即可覆盖透出的眼睛。

## 材质侧需求

只有 HoAOV 还不够，真正解决半透明眉眼需要一个颜色捕获路径。这里有两个实现选项。

### 方案 A：新增材质 pass

给 lilToon / lilPBR 增加：

```shaderlab
Tags { "LightMode" = "HoCharacterCapture" }
```

这个 pass 复用 forward 材质语义，按 RSUV 判断 Face / Eye：

```text
Face: 写 EyeColor，不写 EyeAlpha
Eye:  写 EyeColor，并把材质 alpha 写入 EyeAlpha
其他: discard
```

优点是最接近真实材质，能支持半透明、贴图 alpha、dissolve、normal/lighting、眉毛叠加等。缺点是需要改 shader 模板，工作量比纯 fullscreen pass 大。

### 方案 B：CPU 收集 Renderer 后重绘

让 `HoAovGroup` 或新组件暴露 Face/Eye Renderer 集合，角色特化用 `CommandBuffer.DrawRenderer` 逐个画入 EyeColor。

优点是不需要让所有材质都多一个 pass。缺点是 pass index / submesh / 多材质 / SRP Batcher / 排序都更脆，维护起来反而容易回到“特殊案例堆叠”。

推荐主线采用方案 A。它和 OIT、HoAOV 的分工一致：材质负责“我如何被正确画出来”，RendererFeature 负责“什么时候画、画到哪里、怎么合成”。

## 当前必须先处理的代码事项

1. 统一 HoAOV ID 编码。
   - `GroupId / ObjectId / Flags` 使用 byte-normalized：`round(value) / 255`。
   - `Material` 仍可使用稳定 hash 编码，因为它是材质分类值，不承担范围比较和 SameCharacterId。
   - debug hash 只能在 debug shader 中做，不能写回 AOV RT。

2. 明确 HoAOV pass 对透明的写入语义。
   - 当前文档约定 Transparent 不按 alpha discard，作为主体完整写入 AOV。
   - 这对 frontHair mask 很好，但 EyeAlpha 不能复用 HoAOV coverage，必须来自 Eye Capture。

3. 新增角色特化 shader 常量。
   - 不要塞进 HoPost 常量。
   - 独立 `HoCharacterSpecializationShaderConstants.cs`。

4. 预留角色注册表。
   - 第一版可以只根据 `HoAovGroup` / `CharacterId` 推导。
   - 代码结构上要允许未来 `HoCharacterProfile` 提供 bounds、anchors、module overrides。

5. 预留模块资源申请。
   - EyeReveal、HairDropShadow 先落地。
   - FarPlaneShadow、ReflectionSpace、CharacterLocalLightCast 先只占 settings / enum / debug 名称，不必实现。

6. 新增调试视图。
   - FrontHair mask
   - EyeColor
   - EyeAlpha
   - RevealMask
   - HairShadowMask
   - SameCharacter / depth reject
   - Character bounds / screen rect
   - FarPlaneShadow / ReflectionSpace / LocalLightCast 预留入口

## 第一版落地建议

推荐分七步做：

1. 保持 HoAOV ID raw byte 输出，保证 HoPost 和 HoAOV 一致。
2. 新增 `HoCharacterSpecializationRendererFeature` 框架和 settings，模块先包含 EyeReveal / HairDropShadow / Future placeholders。
3. 新增轻量 `HoCharacterRegistry`，第一版只做 CharacterId、bounds、screen rect 和 debug。
4. 先做 fullscreen debug，确认能读到 ObjectCustom2/3/4、CharacterId、screen rect。
5. 先做前发 DropShadow：读取 FrontHair mask，偏移后 clip 到 Face，同角色生效。
6. 增加 Eye Capture RT 和 `HoCharacterCapture` pass，先支持 lilToon 标准/透明/双面模板。
7. 做 EyeReveal composite，重点验证半透明眉毛/眼睛、OIT 前发、多角色同屏、侧发不投影这四类场景。

第一版不建议做：

```text
不做 stencil 兼容模式
不要求用户复制前发材质
不把 ObjectCustom4..7 变成材质贴图
不让 HoPost DropShadow 承担“源/接收面分离”的角色投影
不在没有 EyeColorBuffer 的情况下宣称支持半透明眉眼
不实现完整多光源角色阴影，只保留接口和资源预算
不实现完整动态反射空间，只保留 profile/anchor/RT 入口
```

## 使用方式草案

角色 prefab 推荐配置：

```text
角色根节点:
- HoAovGroup
- CharacterId = 1
- ObjectCustom0 Character: 角色主体
- 可选 HoCharacterProfile
- reflection anchor / shadow plane override 可先留空

脸:
- ObjectCustom1 Face

前发:
- ObjectCustom2 FrontHair

眼睛/眉毛/睫毛:
- ObjectCustom3 Eye

可选眼透区域代理:
- ObjectCustom4 EyeRevealArea
```

RendererFeature 配置：

```text
URP Renderer Data:
1. WeightedOITRendererFeature（如果使用 OIT）
2. HoAovRendererFeature
3. HoCharacterSpecializationRendererFeature
4. HoPostProcessRendererFeature
5. ShoostPostProcessRendererFeature
```

角色特化默认参数：

```text
Modules:
- Eye Reveal: Enabled
- Hair DropShadow: Enabled
- Far Plane Shadow: Disabled / Reserved
- Reflection Space: Disabled / Reserved
- Character Local Light Cast: Disabled / Reserved

Eye Reveal:
- Strength 0.75
- Feather 1.0 px
- Dilation 2.0 px
- Same Character Only true
- Depth Bias 0.01

Hair DropShadow:
- Source ObjectCustom2 FrontHair
- Receiver ObjectCustom1 Face + EyeRevealMask
- Blend Multiply
- Opacity 0.35
- Distance 8 px
- Angle 115 degrees
- Softness 2 px

Resource Budget:
- Max Characters 8
- Max Local Cast Lights 0 in first version
- Reflection Update Off in first version
```

## 结论

HoAOV 已经让这件事比传统 lilToon stencil 流程简单很多：对象语义、角色 ID、前发/脸/眼睛分组、ObjectCustom 调试和 HoPost 规则组都已经具备。真正新增的难点不只是眼透合成，而是建立一层角色专属缓存/合成管线：被前发遮住的半透明眉眼颜色不能从最终画面恢复，必须通过角色特化功能额外捕获 `Face + Eye` 的颜色与 alpha；未来远平面阴影、动态反射空间和角色局部多光源阴影也都需要角色 bounds、renderer lists、anchors、局部 RT 和模块化资源管理。

因此推荐的长期架构是：

```text
HoAOV:
  负责谁是脸、谁是前发、谁是眼睛、同不同角色、深度/法线/mask。

HoCharacterSpecialization:
  负责眼部颜色捕获、眼透合成、前发投影、同角色限制、角色级缓存和未来模块。

HoPost:
  继续负责通用主体后处理，不承接角色专属的源/接收面双对象逻辑。
```

这样可以彻底摆脱复制材质和 stencil 队列手工表演，同时把传统方案最处理不了的半透明眉眼一起解决掉，并给角色远平面阴影、动态反射空间、角色专属多光源投射和未知模块留下清晰入口。

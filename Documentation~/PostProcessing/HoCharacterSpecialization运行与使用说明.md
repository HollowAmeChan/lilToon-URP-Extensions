# HoCharacterSpecialization 运行与使用说明

> 历史资料：本文记录旧 `HoAOV` 语境下的角色特化接入说明，不作为当前 `Ho-CharacterSpecialization` 使用说明。当前边界、用户顺序和验收口径以 `../RPComponentRework/RPComponentRework_验收文档.md` 为准。

本文记录 `HoCharacterSpecializationRendererFeature` 的运行方式、接入顺序和当前使用方法。它是 HoAOV 之后的一层角色特化合成管线，第一版目标是解决：

```text
1. 前发遮挡下的眼透，支持半透明眉毛/睫毛/眼部材质
2. 前发投到脸和透出眼部上的屏幕空间 DropShadow
```

它不是 HoPost 的通用图层，也不是 HTrace 的替代品。项目级强制投影光源交给 HoShadowCast；SSRTS/HTrace 只消费这些光源数据和 HoAOV 语义做次级阴影、追踪、降噪和 AO。

## 一句话分工

```text
HoAOV
  负责写角色语义：CharacterId、Face、FrontHair、Eye、EyeRevealArea、depth 等。

HoCharacterSpecialization
  负责重新捕获 Face + Eye 颜色，并把眼透和前发投影合成回 camera color。

HoPost
  继续负责通用后处理：边缘光、轮廓、普通主体投影、景深等。
```

## 渲染链路

推荐使用方式：

```text
Renderer Data:
  添加 HoCharacterSpecializationRendererFeature
  保持“使用 Volume 参数”开启

Scene / Global Volume:
  添加 Post-processing/lilToon-HoCharacter/角色特化
  勾选“启用”
  在 Volume 里调眼睛透过和前发投影
```

RendererFeature 只负责安装 pass。日常参数不要在 Render Asset 里调；Render Asset 里的设置只作为没有 Volume 或关闭 Volume 模式时的兜底默认值。

推荐 RendererFeature 顺序：

```text
1. WeightedOITRendererFeature（如果项目使用 OIT）
2. HoAovRendererFeature
3. HoCharacterSpecializationRendererFeature
4. URP Post Processing
5. HoPostProcessRendererFeature
6. ShoostPostProcessRendererFeature
7. Final
```

当前 `HoCharacterSpecializationRendererFeature` 默认 pass event 是 `AfterRenderingTransparents`。实际效果依赖它排在 `HoAovRendererFeature` 之后，因为它需要读取 HoAOV 产出的这些全局纹理：

```text
_lilHoAovMaskIdTexture
_lilHoAovNormalDepthTexture
_lilHoAovObjectCustom0_3Texture
_lilHoAovObjectCustom4_7Texture
```

它自身会临时生成：

```text
_lilHoCharacterEyeColorTexture
_lilHoCharacterEyeDataTexture
_lilHoCharacterCaptureDepthTexture
```

其中 `EyeData` 当前约定为：

```text
r = EyeAlpha
g = EyeDepth * EyeAlpha
b = CharacterId * EyeAlpha
a = capture alpha
```

合成 shader 会用 `r` 把 `g/b` 还原成 eye depth 和 character id，用于深度判断和同角色限制。

## 每帧怎么运行

每一帧大致分为两个阶段。

第一阶段：HoCharacterCapture

```text
1. 清空 EyeColor / EyeData / CaptureDepth
2. 设置 _HoCharacterCaptureMode = 1
3. 绘制所有带 LightMode = "HoCharacterCapture" 的材质 pass
4. pass 内只让 ObjectCustom1 Face 写入 EyeColor
5. 设置 _HoCharacterCaptureMode = 2
6. 再绘制同一批 HoCharacterCapture pass
7. pass 内只让 ObjectCustom3 Eye 写入 EyeColor + EyeData
```

第二阶段：fullscreen composite

```text
1. 读取当前 camera color
2. 读取 HoAOV 的 FrontHair / Face / EyeRevealArea / CharacterId / depth
3. 读取 EyeColor / EyeData
4. 计算 eyeRevealMask
5. 先把 EyeColor lerp 回 camera color
6. 再计算前发 shadow mask
7. 把 shadow 乘到或 lerp 到 camera color
```

执行顺序很重要：前发阴影在眼透之后合成，这样透出来的眼部也会被前发阴影压暗，不会像贴纸一样浮在头发上。

## 角色 prefab 怎么标

在角色根节点挂 `HoAovGroup`，并保证前发、脸、眼睛使用同一个 `CharacterId`。

推荐 ObjectCustom 语义：

```text
ObjectCustom0 = Character / 主体
ObjectCustom1 = Face
ObjectCustom2 = FrontHair
ObjectCustom3 = Eye
ObjectCustom4 = EyeRevealArea / 允许眼透区域
ObjectCustom5 = Accessory
ObjectCustom6 = Reserved
ObjectCustom7 = Reserved
```

典型配置：

```text
角色根:
- HoAovGroup
- CharacterId = 1

脸:
- 放进 ObjectCustom1 Face

前发:
- 放进 ObjectCustom2 FrontHair
- 也可以同时放进 ObjectCustom0 Character

眼睛 / 眉毛 / 睫毛:
- 放进 ObjectCustom3 Eye

可选眼透区域:
- 放进 ObjectCustom4 EyeRevealArea
```

多角色同屏时必须给不同角色不同 `CharacterId`，否则 A 角色的前发可能会透出 B 角色的眼睛。

## 材质 pass 怎么接

真正支持半透明眉眼的关键不是 fullscreen shader，而是材质侧新增：

```shaderlab
Tags { "LightMode" = "HoCharacterCapture" }
```

这个 pass 应复用材质自己的透明规则：

```text
main texture alpha
color alpha
alpha clip / cutoff
dither
dissolve
double side / cull
必要的贴图、UV、颜色、基础 lighting 语义
```

项目里已经提供公共 include：

```hlsl
Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterSpecialization/Shaders/HoCharacterCaptureCommon.hlsl
```

材质 pass 在算出最终颜色后调用：

```hlsl
return LilHoCharacterBuildCaptureOutput(color, input.positionCS.z);
```

capture pass 应使用预乘混合：

```shaderlab
Blend One OneMinusSrcAlpha
ZWrite On
ZTest LEqual
```

`LilHoCharacterBuildCaptureOutput` 会读取 RSUV / MPB 中的 ObjectCustom mask：

```text
_HoCharacterCaptureMode = 1 时，只绘制 Face
_HoCharacterCaptureMode = 2 时，只绘制 Eye
其他对象 discard
```

注意：如果材质没有 `HoCharacterCapture` pass，它不会进入 EyeColor 捕获。此时眼透仍可能有 mask，但没有正确的半透明眼部颜色可恢复。

## Volume 参数

主要参数：

```text
启用
场景视图
图层遮罩
最小 / 最大渲染队列
渲染时机
渲染缩放
```

眼睛透过：

```text
启用眼睛透过
透过强度
羽化像素
扩张像素
深度偏移
使用眼透区域
仅同角色
```

眼睛透过用到的语义：

```text
Eye / ObjectCustom3:
  提供眼睛颜色、眼睛深度和眼睛 alpha。

FrontHair / ObjectCustom2:
  作为挡在眼睛前面的遮挡物。

EyeRevealArea / ObjectCustom4:
  可选限制区域。开启“使用眼透区域”后才参与。
```

前发投影 DropShadow：

```text
启用前发投影
Volume 投影颜色
投影不透明度
投影距离像素
投影距离透视衰减
投影距离参考深度
投影距离最小倍率
投影角度
柔化像素
扩散像素
避开前发
混合模式: 正片叠底 / 线性混合
```

前发投影用到的语义：

```text
FrontHair / ObjectCustom2:
  投影源。

Face / ObjectCustom1:
  投影接收面。

Same Character Only:
  开启“仅同角色”时只允许同 CharacterId 的前发投到同角色的脸。
```

注意：眼睛透过和 DropShadow 可以使用不同区域。比如眼透可以受 EyeRevealArea 限制，但 DropShadow 仍然只看 FrontHair -> Face。

Debug：

```text
关闭
眼睛颜色
眼睛 Alpha
眼睛透过遮罩
前发投影遮罩
```

## 眼透算法

核心选择条件：

```text
frontHair      = ObjectCustom2
eyeAlpha       = _lilHoCharacterEyeDataTexture.r
eyeRevealArea  = ObjectCustom4 或 1
sameCharacter  = 当前像素 CharacterId == EyeData 中的 CharacterId
hairInFront    = hairDepth <= eyeDepth + depthBias
```

最终：

```text
revealMask = frontHair * eyeAlpha * eyeRevealArea * sameCharacter * hairInFront
final.rgb = lerp(final.rgb, eyeColor.rgb, revealMask * strength)
```

这和传统“把前发再画半透明”不同。这里透出来的是重新捕获的 `Face + Eye` 颜色，所以半透明眉毛、睫毛可以先正确混到脸上，再被拿来透出。

## 前发投影算法

前发投影使用屏幕空间 mask 偏移：

```text
shiftedHair = offset(ObjectCustom2 FrontHair, direction, distance)
receiver    = ObjectCustom1 Face + eyeRevealMask
shadowMask  = shiftedHair * receiver * sameCharacter
shadowMask -= currentHair * keepOffHair
shadowColor = VolumeColor
```

`投影距离像素` 仍然是主距离参数。打开 `投影距离透视衰减` 后，合成 shader 会读取 AOV 线性深度，把远处的偏移按 `参考深度 / 当前深度` 缩短，并用 `投影距离最小倍率` 保底；近处不会被额外放大，正交相机保持原始像素距离。

当前 DropShadow 不读取材质专用参数，只由 Volume 控制颜色、强度、距离、柔化、扩散、避开前发和混合模式。

合成：

```text
Multiply:
final.rgb *= lerp(1, shadowColor, shadowMask * opacity)

Lerp:
final.rgb = lerp(final.rgb, shadowColor, shadowMask * opacity)
```

默认推荐 Multiply，更接近传统 lilToon FakeShadow 的压暗感。

## 调试顺序

如果效果不对，按这个顺序查：

```text
1. HoAOV debug 看 ObjectCustom1 Face 是否正确
2. HoAOV debug 看 ObjectCustom2 FrontHair 是否正确
3. HoAOV debug 看 ObjectCustom3 Eye 是否正确
4. HoAOV debug 看 CharacterId 是否同角色一致
5. HoCharacter debug = Eye Color，看 Face/Eye capture 是否有颜色
6. HoCharacter debug = Eye Alpha，看半透明眉眼 alpha 是否写入
7. HoCharacter debug = Eye Reveal Mask，看深度和区域限制是否选中
8. HoCharacter debug = Hair Shadow Mask，看前发投影 mask 是否存在
```

常见症状：

```text
Eye Color 全黑:
  材质还没有 HoCharacterCapture pass，或 Face/Eye 没放进 HoAovGroup。

Eye Alpha 全黑:
  Eye 对象没写 ObjectCustom3，或材质 pass 没把 alpha 输出给 capture。

RevealMask 全黑:
  检查 FrontHair、EyeAlpha、CharacterId、depth bias、EyeRevealArea。

阴影没有:
  检查 FrontHair mask、Face receiver、shadow opacity、distance 和 angle。

多角色串色:
  检查 CharacterId，开启 Same Character Only。
```

## 当前限制

第一版还没有做：

```text
HoCharacterProfile / per-character bounds
按角色裁剪 capture rect
FarPlaneShadow
ReflectionSpace
HTrace SSRTS Bridge
```

材质侧 `HoCharacterCapture` pass 已接入 lilToon URP 模板和 lilPBR URP shader。lilToon 需要执行
`Assets/lilToon/[Shader] Refresh shaders`，让 `.lilblock` 模板展开到最终 `.shader`。

详见：`HoCharacterCapture材质Pass接入说明.md`。

多光源 cast 不在这里继续扩展。项目级强制投影光源交给 `HoShadowCastController` / `HoShadowCastRendererFeature`，详见 `HoShadowCast主灯光与阴影设计.md`。本模块只保留桥接方向：

```text
HoShadowCast provides main sun / selected light shadow data
HTrace SSRTS reads HoAOV semantics and HoShadowCast light data
HTrace manages tracing / denoise / history
HoCharacterSpecialization optionally composites or shares receiver masks
```

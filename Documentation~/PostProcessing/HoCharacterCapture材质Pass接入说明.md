# HoCharacterCapture 材质 Pass 接入说明

> 历史资料：本文记录旧 `HoAOV` 语境下的材质 pass 接入说明，不作为当前模板或用户配置说明。当前边界、用户顺序和验收口径以 `../RPComponentRework/RPComponentRework_验收文档.md` 为准。

角色特化眼透/前发投影不是只看 HoAOV 标记。运行时的 RendererFeature 会用
`ShaderTagId("HoCharacterCapture")` 单独抓一次材质 pass；如果材质 shader 没有
`Tags { "LightMode" = "HoCharacterCapture" }` 的 pass，就算 HoAOV 标记正确也不会写入角色捕获 RT。

本次接入补的是材质侧 pass：

- lilToon URP 模板：新增 `HO_CHARACTER_CAPTURE` pass。
- lilPBR URP shader：新增 `HoCharacterCapture` pass。
- 这两个 pass 共用扩展包里的 `HoCharacterCaptureCommon.hlsl`，由它决定当前 draw 是 Face 还是 Eye。

## 运行链路

实际调参建议走 Volume，而不是 Render Asset：

```text
Renderer Data:
  添加 HoCharacterSpecializationRendererFeature
  保持“使用 Volume 参数”开启

Volume:
  添加 Post-processing/lilToon-HoCharacter/角色特化
  启用后调眼睛透过和前发投影
```

1. `HoCharacterSpecializationRendererFeature` 建立捕获 RT。
2. RendererFeature 设置 `_HoCharacterCaptureMode = 1`，绘制 Face 标记物体，写入眼睛捕获底色。
3. RendererFeature 设置 `_HoCharacterCaptureMode = 2`，绘制 Eye 标记物体，写入眼睛颜色、深度、角色 ID 和透明度。
4. Composite pass 根据捕获结果、HoAOV 语义和当前场景颜色做眼透/前发投影合成。

材质 pass 内部会读取同一套 HoAOV 角色标记：

- ObjectCustom bit 1：Face。
- ObjectCustom bit 3：Eye。
- Character ID：优先来自 `unity_RendererUserValue` 的第二个 byte，否则来自 `_HoAovGroupId`。
- ObjectCustom mask：优先来自 `unity_RendererUserValue` 的低 byte，否则来自 `_HoAovObjectCustomMask`。

如果走材质属性 fallback：

- Face：`_HoAovObjectCustomMask = 2`
- Eye：`_HoAovObjectCustomMask = 8`
- 同一个角色的 Face/Eye 使用相同 `_HoAovGroupId`

如果走 RendererUserValue/RSUV：

```text
rendererUserValue = objectCustomMask | (groupId << 8) | (objectId << 16) | (flags << 24)
```

## lilToon 使用方式

lilToon 的 `Assets/lilToon/Shader/*.shader` 是生成物，所以这次改在模板层：

- `Assets/lilToon/CustomShaderResources/URP/Default*.lilblock`
- `Assets/lilToon/CustomShaderResources/URP/DefaultUsePass*.lilblock`
- `Assets/lilToon/Shader/Includes/lil_pass_hocharacter_capture.hlsl`

改完后必须在 Unity 里执行：

```text
Assets/lilToon/[Shader] Refresh shaders
```

刷新后检查生成出来的 lilToon shader，应该能搜到：

```text
Name "HO_CHARACTER_CAPTURE"
Tags {"LightMode" = "HoCharacterCapture"}
UsePass "*LIL_PASS_SHADER_NAME*/HO_CHARACTER_CAPTURE"
```

半透明材质会保留 alpha 写入眼睛捕获；Opaque/Cutout/Dither 会按材质自己的裁剪规则处理，Cutout/Dither 通过裁剪后按不透明参与捕获。

每个材质还可以在 `HoAOV` 折叠面板里调 `Character Capture Opacity`。它只乘到眼透捕获 alpha 上，不会改变材质正常渲染出来的透明度。默认值是 `1`。

## lilPBR 使用方式

lilPBR 没有 lilToon 那套 `.lilblock` 生成模板，这次直接改 URP SubShader：

- `Shaders/lilPBR.shader`
- `Shaders/lilPBR_Tessellation.shader`
- `Shaders/hocharacter_capture.hlsl`

不需要 lilToon 的 Refresh shaders。Unity 重新导入 shader 后，普通版和 Tessellation 版都应该包含：

```text
Name "HoCharacterCapture"
Tags { "LightMode" = "HoCharacterCapture" }
```

每个 lilPBR 材质的 HoAOV foldout 里也有 `Character Capture Opacity`，语义和 lilToon 一致。

## 2026-05-18 回滚说明

DropShadow 保持简单稳定路径：只读取 HoAOV 的 `ObjectCustom2 FrontHair -> ObjectCustom1 Face`，强度、颜色、距离、柔化等只由 Volume 控制。材质面板不暴露 DropShadow 专用参数。

`HoCharacterCapture` 只负责眼透的 `EyeColor + EyeData` 捕获。capture pass 使用 `Blend One OneMinusSrcAlpha`，公共 include 输出预乘数据，避免 `Character Capture Opacity` 调低时把半透明眼部混白。

## 排查清单

- 没有效果：先确认最终使用的材质 shader 里真的生成/包含 `LightMode = HoCharacterCapture`。
- lilToon 没有效果：多数是忘了执行 `Assets/lilToon/[Shader] Refresh shaders`，模板改动还没展开到生成 shader。
- 只有 HoAOV 有数据、眼透没数据：说明 HoAOV pass 存在，但 HoCharacterCapture pass 还没被材质提供。
- 半透明眼睛太淡或消失：检查主贴图 alpha、材质透明模式、Cutoff/Dither 设置和 `Character Capture Opacity`；捕获 pass 会裁掉 alpha 小于 `0.001` 的透明像素。
- Face/Eye 串角色：确认 Face 和 Eye 的 groupId 相同，不同角色的 groupId 不同。
- include 报找不到：确认项目里能通过 `Packages/jp.lilxyzw.liltoon.urp.extensions/...` 访问扩展包。

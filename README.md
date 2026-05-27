# lilToon URP Extensions

这个包包含本地 lilToon/lilPBR 渲染系统使用的 URP RendererFeature 和 runtime 桥接代码。它是整套系统里的管线层：`lilToon` 和 `lilPBR` 暴露 shader pass 与材质属性，而这个包负责分配 render target、调度 pass、发布全局贴图和缓冲。

## 在整套系统里的定位

- `lilToon`：消费 OIT、MetadataBuffer、HoCharacterCapture、HoShadowCast 和后处理 mask 的角色/NPR shader。
- `lilPBR`：消费平面反射和 MetadataBuffer 的场景/PBR shader。
- `HoUrp17.3.0`：本包面向的本地 URP 版本。
- `HoUrpConfig17.0.3`：本地 URP shader 配置包。
- `lilToon-UnityGLTF-Extensions`：保存导入阶段的材质契约，后续可映射到 lilToon/lilPBR。

## Runtime 模块

- `Runtime/OIT`：给 lilToon 透明 pass 使用的 Weighted Blended OIT。它会绘制 `LightMode = "lilToonOIT"`，写入 accumulation/revealage，再合成回 camera color。
- `Runtime/MetadataBuffer`：材质、对象、mask、metadata 与当前 SSS source 输入缓冲。
- `Runtime/GeometryBuffer`：normal/depth 几何输入缓冲。
- `Runtime/CharacterSpecialization`：角色捕获和角色定制后处理，包括头发/脸部等风格化处理路径。
- `Runtime/ScreenProcess`：用户可控的语义屏幕处理图层栈，支持 MetadataBuffer rule mask，并有 RenderGraph/非 RenderGraph 路径。
- `Runtime/ImageProcess`：最终图像处理链和具体效果移植。
- `Runtime/PlanarReflection`：`HoPlanarReflectionRendererFeature` 统一调度 `HoPlanarReflectionSurface` 平面反射表面，向材质提供 `_LILPBRPlanarReflectionTexture` 和相关 property block 数据。
- `Runtime/ShadowCast`：独立 HoShadowCast atlas 生成，用于指定的额外方向光、聚光和点光。

## Editor 模块

- `Editor/MetadataBuffer`：MetadataBuffer Inspector 和工具。
- `Editor/CharacterSpecialization`：角色特化编辑器 UI。
- `Editor/LilMatConvert`：材质转换工具。
- `Editor/PostProcessing`：ScreenProcess/ImageProcess 图层栈编辑器。
- `Editor/ShadowCast`：HoShadowCast Controller Inspector。
- `Editor/ImageProcessIcons`：编辑器图标资源。

## 主要 RendererFeature

按需要添加到 URP Renderer Asset：

- `WeightedOITRendererFeature`
- `HoMetadataBufferRendererFeature`
- `HoGeometryBufferRendererFeature`
- `HoCharacterSpecializationRendererFeature`
- `ScreenProcessRendererFeature`
- `ImageProcessRendererFeature`
- `HoShadowCastRendererFeature`
- `HoPlanarReflectionRendererFeature`

平面反射由 `HoPlanarReflectionRendererFeature` 统一调度；把 `HoPlanarReflectionSurface` 加到反射平面 mesh 上作为表面描述组件。

## 安装

```json
{
  "dependencies": {
    "jp.lilxyzw.liltoon.urp.extensions": "file:D:/Unity_Fork/lilToon-URP-Extensions"
  }
}
```

Peer requirement：

- Unity 6000.x
- URP 17.x，推荐使用本地 `HoUrp17.3.0`
- 本地 `lilToon` fork，用于 toon shader pass 集成
- `lilPBR`，用于平面反射和 PBR 侧 MetadataBuffer 工作流

## 注意事项

- 主要功能同时实现了 RenderGraph 和兼容模式路径。
- HoShadowCast 使用自己的 atlas：`_HoShadowCastAtlas` 和 `_HoShadowCastSecondDirectionalAtlas`，不依赖 URP additional light shadow receiver。
- 这个包把 `lilToon` 和 `lilPBR` 当作 peer package，而不是硬依赖，方便项目自己控制包解析。

更多设计和排查记录见 `Documentation~/`。

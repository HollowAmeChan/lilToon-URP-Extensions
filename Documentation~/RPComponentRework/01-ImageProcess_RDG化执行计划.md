# ImageProcess RDG 化边界

> 2026-05-26 收口版。本文记录 ImageProcess 当前职责、RenderGraph 资源边界和后续准入规则。

## 0. 当前结论

`ImageProcess` 是最终图像链，不再承担旧 ShoostStack 或语义后处理职责。

它只处理：

- camera color
- ImageChain 中的临时图像
- image-domain layer 的顺序执行
- 最终 blit / compose

它不读取：

- MetadataBuffer
- GeometryBuffer
- ShadowCast
- SSS
- OIT
- CharacterSpecialization
- ScreenProcess rule mask

需要语义输入的屏幕效果归 `ScreenProcess`。

## 1. 用户侧模型

ImageProcess 的 Volume layer 顺序就是执行顺序。RendererFeature 只负责接入最终图像链，不再提供旧 ShoostStack 兼容入口。

常规使用：

1. 在 RendererFeature 顺序中把 `ImageProcess` 放在 `ScreenProcess` 之后。
2. 在 Volume 中添加 image-domain layer。
3. 只在 ImageProcess layer 内调整图像输入、临时图像、混合和输出。

## 2. RenderGraph 边界

RenderGraph 是主线。

- 进入 RDG record 前释放 compatibility-only RTHandle / camera target 状态。
- 不把 live camera attachment 当作普通长期纹理读取。
- layer 间通过 ImageChain 显式传递中间图像。
- 禁用、无有效 layer 或 camera reset 时释放旧路径临时资源。

Compatibility path 只作为非 RenderGraph fallback，不允许影响 RDG 主线资源所有权。

## 3. Debug 边界

ImageProcess debug 只观察 image-domain 输入和输出，不展示语义 mask、buffer channel 或 ScreenProcess rule mask。

如果需要看 MetadataBuffer / GeometryBuffer / ShadowCast / SSS，使用对应 feature-local debug view 或 DebugTile。

## 4. 后续准入

ImageProcess 后续新增能力必须保持 image-domain：

- 可以新增颜色空间、blur、tone、distortion、composite 等图像处理 layer。
- 不新增依赖 MetadataBuffer / GeometryBuffer 的语义 layer。
- 不恢复旧 `NeedsAovInput` 或 AOV mask 编辑入口。

## 5. 最终摘要

ImageProcess 已完成 RDG 主线收口：旧 ShoostStack 名称、AOV composite 入口、semantic mask 编辑和 compatibility-only RT 生命周期都不再作为当前设计入口。它现在只负责最终画面链。

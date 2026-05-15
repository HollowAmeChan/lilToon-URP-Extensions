# lilToon-Shoost 后处理编辑器设计风格

这份文档只记录编辑器层的交互风格，不讲 shader 细节。目标是把 `lilToon-Shoost Post Process Stack` 做成更接近 Shoost 本体的使用方式。

## 核心原则

- 每种后处理效果只保留一个实例。
- 列表顺序固定，不支持拖拽重排。
- 添加入口只显示当前还没启用的效果。
- 图层标题栏尽量短，只保留最常用的控制。
- 不在标题栏里做名字编辑、复制粘贴、右键菜单这类重操作。
- 顶层只保留全局 `场景视图` 开关；每层不再暴露高级开关。

## 列表交互

- 列表的本质是“效果槽位”，不是通用可排序层栈。
- 同一效果重复出现时，应视为异常数据，自动去重或整理到单个槽位。
- 当前实现更适合按固定顺序组织：
  - 先是基础全屏效果
  - 再是 blur / sharpen / split 这类专用效果
  - 最后是复古合成或特化效果
- 添加按钮只负责补齐缺少的效果，不负责调整顺序。

## 用户侧滤镜入口

Shoost 真正给用户看的滤镜分类，建议以后都按这个口径来做图标和分组：

- 锐化
- 白平衡
- 色阶
- 调色
- 边缘光
- 轮廓
- 投影
- 渐变
- 发光
- 光照
- 中心色彩校正
- LED
- 天气
- 粒子
- 摄像头切换器
- 透明背景
- 胶片（可用近似版）
- 电视（Tube，暂时跳过）
- VHS
- 显示器
- 视频游戏
- 光圈模糊
- 通道模糊
- RGB 分离
- 颗粒
- 暗角
- 像素化
- 帧率限制
- 湍流置换
- 镜头畸变
- 摄像机闪光

这份清单是用户入口，不是源码类名。后面找参考包、导出图标和排面板时，优先跟这份 UI 名称对齐。

当前公开入口只显示适合 `Shoost Final Stack` 的最终画面滤镜或已经有明确专用调度的纯后期效果。`RGBChannelSeparator / RGB 通道分离` 已从旧实现上位到公开入口。旧的 `KawaseBlur / Kawase 模糊` 已整体摘除；如果旧 Volume 数据里还残留该槽位，编辑器会自动清掉。`边缘光`、`轮廓`、`投影` 暂时从公开入口隐藏：它们依赖主体边界、alpha、normal/depth 或独立 subject RT，不应在当前只消费 camera color 的 stack 里假装是普通后处理。`LED`、`透明背景`、`摄像头切换器` 也暂时隐藏：它们更像输入 RT、场景对象、相机或合成控制，只有未来 stack list 能明确控制输入 RT 或相机合成语义时才有重新开放的价值。

## 实现策略标记

UI 入口可以继续沿用 Shoost 名称和图标，但编辑器内部需要给复杂项保留实现策略标记，避免用户入口误导底层实现：

- `普通后处理`：只依赖 camera color，可以直接走 fullscreen pass。典型项是调色、色阶、锐化、暗角、像素化、颗粒、视频游戏、显示器、VHS。
- `专用调度`：仍然是 Shoost 后处理语义，但需要多 RT、历史 buffer、blur pyramid 或 profile 组合。典型项是模糊、发光、运动轨迹、帧率限制、Tube、胶片。`Glow / 发光` 已按 Kino `Bloom_Custom` 的 LDR bloom 三模式完成对齐；`Film / 胶片` 先按 Shoost 组合入口压成单 pass 可用近似版。
- `主体数据效果`：依赖角色边界、alpha、depth 或 normal，不按 Shoost 透明图片源硬搬。典型项是边缘光、轮廓、投影，也包括可能需要 mask 的光照。边缘光、轮廓、投影当前在 Shoost Final Stack UI 中隐藏。
- `场景/相机控制`：更像 Shoost 场景功能，不应伪装成单个颜色后处理。典型项是 LED、透明背景、粒子、天气、摄像头切换器。LED、透明背景、摄像头切换器当前在 Shoost Final Stack UI 中隐藏。

编辑器表现上，暂时跳过或需要重写的入口默认不显示在公开图标栏；只有已经有实际 shader、明确兼容价值，且不会误导用户认为它属于当前 final stack 的旧实现，才可以放进“旧实现”图标栏。当前旧实现栏为空，因此 UI 不显示该栏。不要把隐藏项标成“已对齐”。

`边缘光` 的编辑器入口应优先保持 Shoost 用户习惯：颜色、透明度、大小、亮度、对比度、角度、模式、混合模式。当前 Shoost stack 统一后置；如果这个效果需要被 URP Bloom 捕捉，底层实现应按 [`ShoostEdgeLightDesign.md`](ShoostEdgeLightDesign.md) 走独立 subject effects / lighting feature，而不是依赖 Shoost 图层插入位置。

如果后续将 Shoost 面板重构成最终图层系统，编辑器里应区分两个入口：`Shoost Final Stack` 管理最终滤镜和图层混合，默认在 URP 后处理之后；`lilToon Subject Effects` 管理边缘光、轮廓、投影等主体数据效果，允许放在 URP Bloom 前。两者可以共享 Shoost 名称、图标和部分参数习惯，但不应在同一个执行栈里混排。

UI 上也要标出 HDR 语义：在 Bloom 前运行的效果可以写 HDR 亮度；在 Shoost Final Stack 里运行的效果默认按最终显示空间处理。亮度、闪光这类参数如果被移动到 URP 后处理之后，应提示“不会再触发 URP Bloom”，避免用户把 LDR 叠白误认为 Bloom 失效。`发光` 当前是例外：它自身就是 LDR 纯后期 bloom，不依赖 URP Bloom。

## 标题栏风格

- 标题栏优先显示效果类型。
- 强度放在标题栏里，方便不展开也能快速调。
- 启用开关放在标题栏最前面。
- 不再显示图层名字。
- 不再显示每层齿轮、复制、粘贴、重置等入口。

## 顶层设置

- `场景视图` 是整个 Shoost stack 的全局开关，显示在面板顶部。
- 每层不再显示 `场景预览` 或 `插入位置`；对应数据字段也从新结构里移除。
- `材质覆盖`、`Shader 覆盖` 这类调试字段不作为普通用户入口暴露，后续需要排查时再放进单独的开发工具或调试面板。

## 参数显示原则

- 只显示当前模式真正会用到的参数。
- 有条件分支的参数要跟着模式走，比如：
  - `RGB 分离` 与 `径向色差` 的角度参数
  - `虹膜模糊` 的 RGB 模糊开关和三通道半径
  - `色阶` 的 RGB 模式与单通道模式
- 默认值应尽量保持“无变化”。

## 对齐 Shoost 的思路

- 先按 Shoost 的效果名和图标去找参考包。
- shader、材质、图片和变体优先从解包结果和参考包里找。
- 如果某个效果在源码或预设里出现，但当前抓帧里缺少对应 shader，不要先怀疑实现错了，先把它标成“需要补抓”。
- UI 上的一排图标开关应该对应这份用户侧滤镜清单，而不是我们内部的 enum 名。
- 顶部图标条只绘制已经接进来的用户侧项，兼容层、旧包项和内部实现项不要混进来。
- 另外保留一排“旧实现”图标，只放已经有实际 shader 或明确兼容价值、但还没归并到 Shoost 用户侧入口的实现；纯占位和调试逃生口不显示。当前 `RGB 通道分离` 已归并到公开入口，旧的 Kawase 模糊已摘除，所以旧实现栏为空时直接隐藏。
- 不再单独显示“锐化（后）”。用户侧只保留 Shoost 的“锐化”，并按普通 Shoost final stack 滤镜默认后置执行。
- 不再显示“降分辨率”。它和 Shoost 用户侧的“像素化”重复，只保留旧资产兼容；`像素化` 面板按 Shoost 的使用方式简化成 0-1 的分辨率缩放。
- 目前这个编辑器更像是 Shoost 图层管理器的 URP 版本，而不是通用 Volume 层编辑器。

## 图标素材

- Shoost 解包出的图标已经复制到 `Editor/ShoostIcons/`。
- 来源目录是 `Shoost_v0.16.3/unpack/ExportedProject/Assets/Texture2D/`。
- 当前复制的是所有 `icon_*.png` 及其 `.meta`，后面做一排滤镜开关时优先从这里取图。
- 常用效果图标包括 `icon_AddEffects_v1`、`icon_Effects_v1`、`icon_Blur_v1`、`icon_IrisBlur_v1`、`icon_RGBSplit_v1`、`icon_RGBBlur_v2`、`icon_Sharpen_v1`、`icon_LevelsAdjustment_v1`、`icon_ColorGrading_v1`、`icon_WhiteBalance_v1`、`icon_Vignette_v1`、`icon_Distortion_v1`、`icon_FishEye_v1`、`icon_Pixel_v1` 和 `icon_Grain_v1`。

## 参考位置

- 源映射：`ReferencePackages~/PackageSourceMap.md`
- 参考阅读：`ReferencePackages~/ShoostSourceReadingGuide.md`
- 后处理总览：`Documentation~/PostProcessing/ShoostPostProcessStack.md`

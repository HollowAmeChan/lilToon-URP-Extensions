# lilToon-Shoost 后处理编辑器设计风格

这份文档只记录编辑器层的交互风格，不讲 shader 细节。目标是把 `lilToon-Shoost Post Process Stack` 做成更接近 Shoost 本体的使用方式。

## 核心原则

- 每种后处理效果只保留一个实例。
- 列表顺序固定，不支持拖拽重排。
- 添加入口只显示当前还没启用的效果。
- 图层标题栏尽量短，只保留最常用的控制。
- 不在标题栏里做名字编辑、复制粘贴、右键菜单这类重操作。
- 高级项统一收进一个开关，默认收起。

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
- 胶片
- 电视
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

## 标题栏风格

- 标题栏优先显示效果类型。
- 强度放在标题栏里，方便不展开也能快速调。
- 启用开关放在标题栏最前面。
- 不再显示图层名字。
- 不再显示每层齿轮、复制、粘贴、重置等入口。

## 高级设置

- `场景预览`、`插入位置`、`材质覆盖`、`Shader 覆盖` 这类字段统一视为高级设置。
- 高级设置默认隐藏，只在需要排查或对齐特殊管线时展开。
- 高级设置不应分散到每个图层标题里，而应保持统一入口。

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
- 另外保留一排“旧实现”图标，专门放我们现有的兼容入口和还没最终归并的实现，后面再慢慢整理。
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

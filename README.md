# lilToon URP Extensions

Ho 渲染体系的 URP 功能扩展包，配合 lilToon 与 HoURP，提供光照、阴影、反射、透明合成和后处理。

## 功能

- **基础数据与语义遮罩**：几何数据、法线与深度、逐物体身份、表面材质数据，以及统一的属性与语义遮罩合成。
- **世界空间直接光 RTDI**：解析灯、HDR 天空、射线遮挡、基础 Cutout、可选 NRD 降噪，以及参考 PBR 替换 / 加法叠加；常用参数由 Volume 控制。
- **世界空间间接光 RTGI（开发中）**：独立一跳漫反射 GI，支持离屏几何与自发光；最终画面合成和降噪尚在推进。
- **屏幕空间光照**：GTAO、SSGI 与次表面散射 SSS。
- **阴影与环境**：独立多灯 ShadowCast / PCSS、角色局部阴影、共享大气与太阳体积光。
- **反射与透明**：平面反射、Weighted Blended OIT 透明合成。
- **角色特化**：眼睛透过、前发投影、脸色扩散与角色捕获。
- **后处理与调试**：语义屏幕图层、图像效果栈、渐变映射、网点、调色预设和缓冲调试视图。

详细说明见 [文档索引](Documentation~/文档索引.md)。

## Ho 渲染体系相关仓库

| 仓库 | 用途 |
| --- | --- |
| [HoTools](https://github.com/HollowAmeChan/HoTools) | Blender 角色、模型、贴图、动画与自动化工具 |
| [HoGLTF](https://github.com/HollowAmeChan/HoGLTF) | Blender glTF 导出与 Ho 材质契约桥接 |
| [HoUrp17.3.0](https://github.com/HollowAmeChan/HoUrp17.3.0) | URP 渲染基座 |
| [HoUrpConfig17.0.3](https://github.com/HollowAmeChan/HoUrpConfig17.0.3) | URP Shader 配置 |
| [lilToon](https://github.com/HollowAmeChan/lilToon) | 角色 / NPR 着色器 |
| [lilToon-UnityGLTF-Extensions](https://github.com/HollowAmeChan/lilToon-UnityGLTF-Extensions) | glTF 材质契约导入 |
| [HoUnityTools](https://github.com/HollowAmeChan/HoUnityTools) | 角色、动画与创作工具 |

## 可选材质合集

- [HoShader](https://github.com/HollowAmeChan/HoShader)：宝石、皮肤、绒毛等独立材质与 Shader 实验合集，按需选用。

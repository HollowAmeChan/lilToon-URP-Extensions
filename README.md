# lilToon URP Extensions

Ho 渲染体系的 URP 功能扩展包，配合 lilToon 与 HoURP，提供光照、阴影、反射、透明合成和后处理。

## 功能

- **基础数据与语义遮罩**：几何数据、法线与深度、逐物体身份、表面材质数据，以及统一的属性与语义遮罩合成。
- **世界空间直接光 RTDI**：解析灯、HDR 天空、射线遮挡、基础 Cutout、可选 NRD 降噪，并发布 typed direct-light product；camera color 由 ScreenProcess 的 RTDI layer 消费。
- **世界空间间接光 RTGI**：独立一跳漫反射 GI，支持离屏几何、自发光、独立 NRD，并发布 typed indirect-light product；camera color 由 ScreenProcess 的 RTGI layer 消费，见 [RTGI](Documentation~/Ho-RTGI.md)。
- **屏幕空间光照**：GTAO、SSGI 与次表面散射 SSS。
- **阴影与环境**：独立多灯 ShadowCast / PCSS、角色局部阴影、共享大气与太阳体积光。
- **反射与透明**：平面反射、Weighted Blended OIT 透明合成。
- **角色特化**：眼睛透过、前发投影、脸色扩散与角色捕获。
- **后处理与调试**：语义屏幕图层、图像效果栈、渐变映射、网点、调色预设和缓冲调试视图。

详细说明见 [文档索引](Documentation~/文档索引.md)。当前角色栅格投影见 [Ho-CharacterShadow](Documentation~/Ho-CharacterShadow.md)；Ho-RT 底座与 RTShadow 的现状见 [Ho-RT](Documentation~/Ho-RT.md)，RTAO、反射/折射与整套 RT 优化的未实现路线见 [Ho-RT 功能路线图](Documentation~/Ho-RT-Feature-Roadmap.md)。

## Ho 渲染体系相关仓库

| 仓库 | 用途 |
| --- | --- |
| [HoTools](https://github.com/HollowAmeChan/HoTools) | Blender 角色、模型、贴图、动画与自动化工具 |
| [HoUrp17.3.0](https://github.com/HollowAmeChan/HoUrp17.3.0) | URP 渲染基座 |
| [HoUrpConfig17.0.3](https://github.com/HollowAmeChan/HoUrpConfig17.0.3) | URP Shader 配置 |
| [lilToon](https://github.com/HollowAmeChan/lilToon) | 角色 / NPR 着色器 |
| [lilToon-UnityGLTF-Extensions](https://github.com/HollowAmeChan/lilToon-UnityGLTF-Extensions) | glTF 材质契约导入 |
| [HoUnityTools](https://github.com/HollowAmeChan/HoUnityTools) | 角色、动画与创作工具 |

[HoGLTF](https://github.com/HollowAmeChan/HoGLTF) 已从 Ho 渲染体系退役，不再列为当前工作流的组成部分；原仓库仅作为历史参考。

## 可选材质合集

- [HoShader](https://github.com/HollowAmeChan/HoShader)：宝石、皮肤、绒毛等独立材质与 Shader 实验合集，按需选用。

## 日期版本与安装包

推送默认分支后自动发布日期版本（`YYYYMMDD-HHMMSS`）与 Unity 安装包；在 GitHub Releases 下载 `.tgz`，通过 Package Manager 从 tarball 安装。版本对齐、手动发布与安装说明见 [发布说明](.github/RELEASING.md)。

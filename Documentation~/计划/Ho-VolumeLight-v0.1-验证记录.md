# Ho-VolumeLight v0.1 验证记录

日期：2026-10-10。Unity 6000.3.15f1，HoURP 17.3.0，RenderGraph。隔离工程：`.codex-research/RTInputValidation`。

本文记录最初 v0.1 的结果。2026-10-19–20 已补充太阳 HD 抗锯齿与相机历史，最新证据见 [HD 抗锯齿验证](Ho-VolumeLight-HD-抗锯齿验证.md)；下文“无持久效果 RT”仅描述最初实现，现在时域模式会持有按相机分配的历史。

## 最终结果

| 入口 | 图形 API | 最终日志 | 结果 |
| --- | --- | --- | --- |
| `HoVolumeLightValidation.Run` | D3D12 | `volumelight-v01-gate-d3d12.log` | PASS |
| `HoVolumeLightValidation.Run` | D3D11 | `volumelight-v01-gate-d3d11.log` | PASS |
| `HoAtmosphereValidation.Run` | D3D12 | `volumelight-environment-final-d3d12.log` | PASS |

这些日志均在隔离工程根目录。`VolumeLightValidation/result-Direct3D12.json` 与 `result-Direct3D11.json` 的 `passed=true`、`medium=PASS`、`solar=PASS`，异常文本为空。验证在实际 RenderGraph 中绘制并读取 GPU 结果，没有以 CPU Shader globals 代替纹理/阴影验证。

基础介质已单独提交为 `50b1b4b`；太阳 HD、验证入口和文档作为后续步骤提交。

### lilToon Cutout 补充验证

用户实际树叶使用 lilToon Cutout，故追加 `Hidden/lilToonCutout` 的 GPU 测试，分别用主贴图 alpha 和 Alpha Mask 替换输入，断言孔位的光源空间 probe 透过、实心位置被遮挡。D3D12 的 `volumelight-liltoon-cutout-d3d12.log` 与 D3D11 的 `volumelight-liltoon-cutout-d3d11.log` 均报告 `lilToon Cutout MainTex alpha + AlphaMask PASS`，完整 suite 也 PASS。对应 JSON 增加 `lilToonCutout = MainTex alpha + AlphaMask PASS`。

该结果只证明二值 cutout，未把阈值裁切当成连续灰度透光或彩色玻璃。用户要求连续灰度透光作为后续功能，彩色透光暂缓。

## 目标逐项证据

| 要求 | 权威证据 |
| --- | --- |
| 盒形空间介质与软激活 | GPU probe 检查中心 1、边缘 0.5、区域外 0；旋转/非均匀缩放后世界米淡出仍正确 |
| 颜色与密度 | probe 直接读回给定密度/tint；Shader 与 CPU 分离采样场景 |
| 固定 HoAtmosphere 发布独立 Medium | 读取 `HoAtmosphereMediumResources` 与环境资源；缺天空和环境 Volume 关闭时 Medium 有效 |
| 区域改动不扰动环境 | 修改密度后环境 revision 不变，Medium revision 改变；实际天空材质捕获后再改风/密度，CaptureCount 不增加 |
| 动态世界噪声 | 给定时间 0/1 的纹理 GPU 密度符合预期；移动区域与换观察相机不拖动噪声；实际太阳 RGB 随共同时间变化 |
| 大气激活与完整照明/遮挡分开 | 参与权重 0、区域移出画面均无体积输出；关闭 SDF 激活后光源 shadow probe 不变 |
| 太阳参数跟随 | 实际 RGB 对太阳强度减半作相应变化，蓝太阳产生蓝体积贡献 |
| 窗格与多孔遮挡 | 区域外实体窗框显著降低实际光束；alpha cutout 的孔位光源 probe 明亮、实心位被遮挡 |
| 画面外/区域外遮挡保留 | 明确断言 caster 不在观察相机 frustum，也不在介质区域；它仍降低区内实际 RGB |
| 相机内外与深度截止 | 透视/正交、相机进入区域均有输出；中心和非中心前景遮挡的效果开/关差值为零，去掉遮挡后该处有体积贡献 |
| 屏幕空间阴影不替代空气阴影 | GPU probe 证明 `_MAIN_LIGHT_SHADOWS_SCREEN` variant 实际执行；体积图与原 atlas 路径相对差值小于 2% |
| 阴影距离边界 | 独立标准 surface API probe 验证远端阴影淡出；实际体积 RGB 与无阴影参考相对差值小于 2% |
| 分辨率/MSAA | renderScale=0.5、MSAA4 的输出均值与全分辨率参考在容差内 |
| 基础地编操作 | 实际创建 Region/Feature Editor 类型匹配；区域尺寸 Undo/Redo 正确；默认 Shader resource 可加载 |
| 关闭/重启与资源 | 设置关闭、RendererFeature SetActive(false)、Dispose 无残留效果/材质；重启可重建；基础环境/介质仍有效 |
| 原有环境回归 | six-face HDR/方向、来源、缓存、Volume、跨相机、禁用/销毁、调试与主相机 canary 全通过 |

## 已修正的问题

- 前景深度检查改为比较效果开/关差值，避免将 Lit 黑物体的微弱高光误判成体积泄漏。
- 原始 realtime shadow 查询不带距离淡出；空气着色补充标准 world-position shadow fade，并以独立 surface API 与实际 RGB 对照验收。
- 世界时间按观察者共同采样，编辑器持续推进；时间不混入静态介质/环境 revision。
- RendererFeature 直接禁用也释放材质/调试资源，正常显示路径不分配持久效果 RT。
- 默认 Solar Shader 放入模块 Resources，避免只依赖运行时 Shader.Find；高级覆盖仍保留显式入口。

## 静态与面板检查

新增/修改的大气与 VolumeLight 面板符合 HelpBox/中文说明预算：Region 2、Atmosphere Feature 1、Atmosphere Volume 2；VolumeLight 编辑器文件共 4（实际 Feature/Volume 各 2）。

全仓库 `check-ui-verbosity.ps1` 仍报告既有 GD 与 AC 面板超标，不属于此次修改；完整输出保存在 `.codex-research/volumelight-ui-verbosity-final.log`。本次不改动这些面板。

新资源有独立 `.meta` GUID；代码/文档链接与 whitespace 检查完成。参考 VLB 源码留在本地忽略的 `research~`，没有进入生产资源或提交。

## 当前边界

结果证明 v0.1 的单 Box 区域 + 太阳 HD，在所测 D3D11/D3D12 Editor RenderGraph 路径成立。没有宣称 XR/其他相机域、Player 构建、Solar compatibility、局部灯 SD/HD、任意 SDF、多区域混合、可见灰尘模拟、透明玻璃或 AC/AOV 输出已经完成。

预览 PNG 与线性 EXR 位于隔离工程 `VolumeLightValidation/solar-*.png|exr`；PNG 为压缩亮度预览，RGB/AOV 判断以 GPU float readback 为准。详见 [用法与接口](../Ho-VolumeLight.md)。

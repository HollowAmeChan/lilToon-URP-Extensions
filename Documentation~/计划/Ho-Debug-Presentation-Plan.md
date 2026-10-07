# 调试归属、平铺与最终显示

范围：只修改 Extensions；AC 保持双参数调试；中央结果布局暂缓；保留 DebugTile 类型及 GUID，收缩为 APV 等外部系统调试宿主。

1. 公共设施：模块把调试结果写入独立目标，公共 presenter 在 FinalPost / FinalBlit 后显示；平铺复用模块 shader，公共工具只处理格子和标签。
2. 模块归属：OB / GB / SB / ShadowCast / SSS / PLR 增加 All；适合枚举的其他模块复用同一工具。保留既有枚举数值。
3. DebugTile 剪枝：移除模块资源、显示公式和 AC 需求登记；仅枚举外部系统视图。Registry 保留为 shader 检查目录，不再承担渲染分派。
4. 验证：Unity 编译、shader 编译、RG 与 compatibility 像素读回、后续覆盖 pass、feature 重排、关闭恢复与多相机隔离。

约束：调试输出不能写回正常 cameraColor；公共显示层不认识模块资源。多个调试同时开启时使用稳定的模块键选择输出，不依赖 feature 顺序。临时诊断结果必须显式登记读取依赖。

状态：已完成本轮实现与独立验证。

验证记录（2026-10-08，Unity 6000.3.15f1）：

- D3D12 RenderGraph：145 项通过。
- D3D11 compatibility：144 项通过。
- 覆盖单视图 / All 像素对照、真实 OB / GB producer、ScreenProcess debug layer 隔离、后续图像覆盖、最终 FXAA、feature 重排、缺失输入、部分 viewport、双相机与关闭恢复。
- APV L1 / L2 shader variants 编译通过；AC 未增加 All。
- 真实 SB producer 的 HDR emission、模块内 All、关闭 / 恢复 / 销毁在 D3D12 RG 与 D3D11 compatibility 均通过。
- 当前工作区另有尚未完成的大气模块，缺少 `HoAtmosphereDebugPass`；最终独立验证使用不含该模块的包副本。保留大气模块文件，未代为修改。
- 未修改 HoURP 源码，未自动迁移 RendererData / VolumeProfile，中央结果布局暂缓。跨相机 stack 的最终展示不在本轮逐相机契约内。

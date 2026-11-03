# ScreenProcess 镜头效果与公共镜头参数规划

日期：2026-11-03。

## 本次范围

1. 将光斑变焦放入 ScreenProcess，使用视觉深度；先独立保存焦点距离、镜头焦距、光圈、前后景响应和目标跟焦配置。不读取或要求开启景深层。
2. 光圈散景并入现有 DepthOfField，作为可选的失焦亮部增强，复用该景深层的焦点与光圈形状，不再提供新的独立光圈散景入口。
3. 保留旧 ImageProcess 枚举的序列化编号，提供明确的旧配置迁移入口，避免旧资产被静默解释成别的效果。
4. Game / Scene View 都使用当前渲染相机计算深度和跟焦距离；不依赖 Camera.main。

## 公共镜头参数：只规划，本次不实施

未来可以把镜头焦距、光圈、传感器尺寸、焦点距离与目标跟焦从各效果中提取为独立输入。候选职责：

- Camera 原生属性：实际镜头焦距、光圈、传感器尺寸与可选的焦点距离。
- Camera 附加控制：跟焦目标、偏移、平滑以及目标失效策略。
- 独立公共镜头 Volume：手动参数或相机来源选择，以及明确的 Scene View 预览覆盖。
- 效果层：选择本层或公共镜头输入，并独立保存艺术强度、亮部阈值、采样预算等效果参数。

最终组件位置、参数优先级、混合规则与序列化迁移待后续需求确认。本次不增加公共 Volume、相机组件或相互引用的效果层；光斑变焦先复制独立的镜头/跟焦配置，保证可用。以后只替换参数解析入口，不以景深层作为参数提供者。

### Scene View 边界

Scene View 有内部 Camera，不要求 Hierarchy 中存在 Game Camera。自由预览使用当前 Scene Camera 的姿态与深度；读取指定 Game Camera 镜头或跟焦目标时，要明确区分借用镜头数据和使用该相机视角，不能承诺两个不同视角的景深完全相同。

参考 HDRP 17.3：

- [Depth Of Field](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.3/manual/Post-Processing-Depth-of-Field.html)：物理相机镜头属性与 Volume 效果配置分工，对焦距离可选 Camera / Volume。
- [HDRP 6000.3 PostProcess 源码](https://github.com/Unity-Technologies/Graphics/blob/6000.3/staging/Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/HDRenderPipeline.PostProcess.cs)：常规景深路径主动排除 Scene View；Ho 的 Scene View 预览是自己的支持能力。
- [SceneView.camera](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SceneView-camera.html)。

## 实施顺序与验收

- [x] P0：记录本次边界与延期的公共镜头方案。
- [ ] P1：独立 ScreenProcess 光斑变焦、Inspector / 预设 / 跟焦与 RG / Compatibility 资源绑定。
- [ ] P2：景深可选光圈散景增强；保留现有合焦描边、视觉深度和逐邻点采样契约。
- [ ] P3：旧入口收口与配置迁移、文档、GPU 回归和最终检查。

光斑变焦至少验证：景深关闭时独立工作、合焦区域保护、前后景强度可设零、目标跟焦和备用距离、缺失深度原图回退、描边视觉深度、层遮罩与 HDR 颜色。

景深至少验证：增强关闭时旧结果不变、只增强失焦亮部、合焦亮点不扩散、前后景与描边边界不渗色、亮部阈值/增益实际生效。资源与采样修改须覆盖 D3D11 / D3D12，以及 RG / Compatibility。使用隔离测试项目，不替换用户打开场景的生产 shader 做负对照。

## 进展

后续阶段在本节记录提交、实际验证和仍未验证的边界，不把编译通过等同于 GPU 画面通过。

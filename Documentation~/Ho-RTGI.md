# Ho-RTGI：二次命中接入

当前已交付 GI 所需的实际 DXR 命中材质路径，尚未添加可用的 Ho-RTGI Renderer Feature，也未输出间接光。下一步接用户参考工程的 BrdfRayTracing → ShadeSecondarySurfaces → GI temporal/spatial/final 链路。

## 已接通的部分

`HoRTScene` 可选择绑定 GI 专用 hit material；原来的 DI inline query 默认路径不变。命中属性读取沿参考工程 `SurfaceRayTracingCommon.hlsl` 的 Unity vertex-fetch 路径，使用实际 primitive、重心坐标与对象变换，不依赖相机可见性或屏幕颜色。读取 UV0～3，并返回世界位置所需的 hit distance、三角形几何法线、插值/normal-map 着色法线、主色、perceptual roughness、metallic、reflectance 和独立 HDR emission。

`HoRTGISecondaryMaterials` 拥有私有材质适配副本，复制原材质属性；不修改源材质、Forward、CS 或 ShadowCast。调用顺序为 BeginFrame → scene.Synchronize（内部 Resolve）→ EndFrame → Build/Dispatch。销毁时先销毁 scene，再销毁适配材质。

Emission 直接调用 lilToon 现有 `lilEmissionComponent` / `lilEmission2ndComponent`，求和 RGB × 各组权重。保留两组的启用、颜色、贴图/alpha、UV 选择、ST/动画、mask、渐变、blink 与 main-strength；与 SB 相同，fluorescence 使用中性 lighting gate。不会把主色、matcap、rim、旧 Forward 明亮部分当作发光来源。

## 首版支持域

- 使用现有 HoRTScene 的刚性 opaque MeshRenderer，支持屏幕外、非均匀/镜像变换；不支持 skinned、cutout/透明、顶点变形、static batching 或 additional vertex streams。
- 当前命中材质接收标准 `lilToon` opaque，第一主色链、两组 normal map、两组专用 emission，以及 smoothness/metallic 贴图。Main2nd/Main3rd、parallax 位移、Lite/Multi/Hair/自定义 shader 尚未接入，明确返回 unsupported。
- Unsupported opaque 材质仍有真实命中，status=2，保持遮挡；不能把它转成环境 miss。status=0 才表示 miss，status=1 表示已求值的材质。
- 纹理采样显式 LOD 0，尚未实现 ray-cone 过滤与 GSAA。发光视角组分使用二次射线的观察方向。没有 SSS、最终 NPR 接收、合成、降噪或 Player 验收。

## 验证

`HoRTGISecondaryValidation.Run` 在隔离工程的 D3D12 上分别以 Gamma / Linear 色彩空间执行真实 TraceRay，核对离屏命中/距离、材质参数、两组 HDR emission、main-strength、UV1/贴图 alpha/ST、mask RGB/alpha、禁用与材质切换、unsupported 遮挡、镜像非均匀变换及源材质保持原样。保留原材质 Color/HDR 与 Gamma-tagged metallic/reflectance 的颜色空间语义；三角形法线朝向射线所在侧，插值法线对齐同侧。Shader 警告/错误视作失败。

现有 Ho-RTDI 的 D3D12 RenderGraph / compatibility 与 D3D11 不支持分支已回归通过。这一步完成的是 GI 链路必需的命中着色适配，不能据此宣称 ReSTIR GI 已交付。

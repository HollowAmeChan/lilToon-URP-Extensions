# RT 主表面读取

`HoRTPrimarySurfaceInputs` 是 Runtime 内部的逐 pass 输入快照，`HoRTPrimarySurface.hlsl` 是共享读取入口。它直接借用 GB/SB 资源，不是新的 Renderer Feature，不分配 GBuffer，不写相机颜色，也不经过 AC。当前提供 raster、compute 与 compatibility 的绑定方式；世界空间求交另由 [HoRTScene](Ho-RTScene.md) 提供。本模块不管理 AS、灯光、reservoir、降噪或最终合成。

## 数据与来源

| 字段 | 来源 / 语义 |
| --- | --- |
| `positionWS` | SB raw depth + 本相机实际投影/视图的逆 GPU VP；位置对应源像素中心 |
| `eyeDepth` | 同一深度的线性眼空间值；正交/透视和 reversed-Z 分别处理 |
| `geometricNormalWS` | SB Normal.BA 的 octa 解码；顶点路径之后插值的网格法线，无 normal map |
| `shadingNormalWS` | SB Normal.RG 的共享 octa 解码，含 normal map |
| `viewDirectionWS` | 指向观察者；透视为相机到表面反方向，正交为平行方向 |
| `baseColor` | 原始线性 HDR SB 主色，保留大于 1 的值，不自动变成物理反射率 |
| `perceptualRoughness` | SB Material.R 原值，不在读取层平方 |
| `metallic` | SB Material.G 原值 |
| `reflectance` | SB Reflection.R 原值，不在读取层换算 F0 |
| `emission` | SB Emission.RGB 的专用组分，不含主色或最终画面颜色 |
| `ownerAvailable / owner` | 可选 SB Owner 的可用性与 16-bit 身份；未登记的 0 不影响表面有效性 |

无扰动网格法线不是三角形 flat face normal，也不是世界空间 AS 的命中法线。它保留材质的既有 `_FlipNormal` / facing 约定；真实求交时的 primitive、双面、ray origin 与 face normal 仍由 HoRTScene / ray-hit 契约处理。

本轮 GPU 验证发现此前“GB 提供无扰动几何法线”的判断不成立：lilToon 的 GB shader 也应用 normal map/Tesion。因此保留 GB 现有消费者行为，使用 SB Normal 的预留 BA 通道提供明确组分。Material.A 为 `meshNormalAvailable`；lilToon SB writer 写 1，旧版/自定义 writer 未实现时必须保留 0。读取端在标记缺失时拒绝，而不是解码旧预留值。原 Normal.RG、Material.RGB 和 MRT 格式/数量不变。

## 调用

RenderGraph pass 在 GB/SB 生产之后调用：

```csharp
data.inputs = HoRTPrimarySurfaceInputs.Read(graph, frameData, builder, includeOwner: false);
// Raster callback：需要 builder.AllowGlobalStateModification(true)
data.inputs.Bind(context.cmd);
// Compute callback：显式绑定到具体 shader/kernel，不依赖 raster 的全局变量
data.inputs.Bind(context.cmd, computeShader, kernel);
```

`Read` 接受通用 graph builder，并逐项登记读依赖。默认读取 SB color/normal/material/reflection、用于 writer 与 emission 的已有 Emission、关联需要的 SB/GB depth 和 GB normalDepth；不读 Classification、SSS、PLR strength、OB 或 motion。只有请求 `includeOwner` 时才读 SB owner，不要求额外 OB 身份图。

兼容路径在 producer 执行后调用 `BindCompatibility(cmd, cameraData, includeOwner)` 或 `BindCompatibilityCompute(cmd, shader, kernel, cameraData, includeOwner)`。句柄来自本相机实际生产记录，不使用 CPU `Shader.GetGlobalTexture` 推测 GPU 绑定。

Compute 必须给每个声明的纹理槽绑定资源；没有输入或未请求 owner 时绑定已有黑色默认资源，并用显式状态拒绝读取。没有因此复制属性或读取未请求的 SB owner。

HLSL：

```hlsl
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RayTracing/Shaders/HoRTPrimarySurface.hlsl"
HoRTPrimarySurface surface;
if (HoRTTryLoadPrimarySurface(pixel, surface))
{
    // 在消费者里定义实际 BRDF / lighting response。
}
```

`HoRTTryLoadPrimarySurfaceAtUV(uv, surface)` 检查 UV 后定位源像素，并吸附到像素中心。它不会在两个表面之间双线性插值，也不会用任意子像素 UV 重建一个与采样属性不同的位置。

## 有效性与边界

先通过已有 GB/SB 深度关联，再读取属性；只有状态 5 返回 true。失败时仅保留 status，其他 payload 全部为 0：

| 状态 | 原因 |
| --- | --- |
| 0～6 | 沿用关联契约：缺失输入、无 writer、无 GB 几何、取样域不匹配、深度错配、成功、越界 |
| 7 | 不支持的相机/投影域 |
| 8 | 必要属性图缺失，或 writer 未提供 Normal.BA 的可用标记 |
| 9 | 非有限数据或无效法线 |

当前支持 Mono、Texture2D、同分辨率的单采样域，标准透视/正交投影，可包含 lens shift 与本帧 jitter。相机矩阵、位置、观察方向与深度参数随快照绑定，不依赖之后的 Unity raster camera globals。Oblique、非标准深度投影、XR / array 主表面暂不支持；MSAA resolve 与异分辨率仍沿用关联层的“无法确认”。共同 camera render scale 与硬件动态分辨率是不同概念，后者尚未验收。

快照仅用于当前相机当前 graph，不缓存 TextureHandle 到下一帧。读取成功只证明本次支持域内的数据关联与完整性，不证明 primitive 身份或最终相机画面的全部几何可见性。尚未实现 motion / history / temporal，也未定义 HDR 主色到 PBR/NPR 接收响应的映射。

## 验证

隔离入口：`HoRTPrimarySurfaceValidation.Run` / `RunCompatibility`。同一场景同时输出 raster 与显式 compute 读回，验证正交/透视、偏移投影、移动/旋转相机、双轴离心像素、像素中心吸附、两种法线、HDR 主色与专用 emission、未平方的粗糙度、可选身份、旧 writer 标记、失效与清零。SB 的 `Geometry Normal` 单视图/All 也有实际像素验证。

平台完成记录见 RTDI/RTGI 调查报告的实现进展；测试输出 RT 仅存在于 Editor 隔离验证，不是 Runtime 的属性副本。

2026-10-08：FXC 对 UV 包装函数转发 `out surface` 的 definite-assignment 警告已通过显式初始化和局部结果修正；D3D11 RG / compatibility 重新通过。验证现在同时拦截 Compute 编译警告与错误，不能仅以 GPU 输出通过作为编译干净的证据。

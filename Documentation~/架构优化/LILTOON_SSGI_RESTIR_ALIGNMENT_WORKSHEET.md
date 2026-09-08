# Ho-SSGI / HTrace ReSTIR 对齐工作表

> 用途：Ho-SSGI ReSTIR 的唯一阶段进度表。每次改算法前先更新对应行的“状态、证据、下一步”，避免只凭最终画面判断问题。
>
> 当前场景：`D:\Unity_Project\BREAK_URP\Assets\mmd场景测试\朱木古堂`
>
> HTrace 源码根目录：`D:\Unity_Project\BREAK_URP\Assets\HTraceSSGI`

## 状态定义

| 状态 | 含义 |
|---|---|
| `已对齐` | 结构和语义已与 HTrace 对齐，并有可重复画面/资源证据 |
| `部分对齐` | 主路径已存在，但格式、验证、采样分布或 denoise 仍有明确差异 |
| `仅有 Ho 实现` | Ho 有自己的实现，但还不能称为 HTrace 对齐 |
| `未开始` | 尚未实现 |
| `不适用` | HTrace 有该功能，但当前 Ho 管线有明确边界，不迁移 |

## 总链路

HTrace 的目标链：

```text
PrePass / motion / geometry
  -> temporal reprojection
  -> optional checkerboard
  -> GI trace candidates
  -> ReSTIR temporal
  -> firefly suppression
  -> ReSTIR spatial resampling
  -> ReSTIR spatial validation
  -> denoiser temporal accumulation
  -> denoiser spatial filter 1/2
  -> optional interpolation/stabilization
  -> composite
```

当前 Ho 链：

```text
HoGeometryBuffer + opaque camera source
  -> source color history/reprojection
  -> Ho-SSGI raw trace candidate reservoir
  -> four-tap temporal reservoir merge
  -> firefly W clamp
  -> world-plane Poisson spatial reservoir reuse
  -> selected-ray re-march validation
  -> HDR bilateral denoise
  -> _HoGITexture composite
```

## 阶段矩阵

| ID | 阶段 | HTrace 证据 | Ho 当前实现 | 状态 | 画面/资源验证 | 下一步 |
|---|---|---|---|---|---|---|
| G0 | 几何输入 | `GBufferPassURP.cs`、`HMain.hlsl` | `HoGeometryBuffer.normalDepthTexture`，RGB 法线、A 线性深度/coverage | `部分对齐` | Geometry、Source Validity；描边 coverage 必须为无效 | 保持 GeometryBuffer 为唯一几何真值 |
| G1 | Motion | `PrePassURP.cs`、`HBUFFER_MOTION_VECTOR` | URP `motionVectorColor` | `部分对齐` | 运动物体重投影是否方向正确 | 记录 motion UV/Y 翻转与 render scale 契约 |
| G2 | Camera history | `CameraHistorySystem`、`SSGIPassURP.SetupShared` | `HoSSGIHistory`，cameraId/尺寸变化失效，GI/depth/reservoir ping-pong | `部分对齐` | 切换相机、改分辨率、重载场景后不能读旧历史 | 增加 camera cut/render-scale 显式 reset |
| G3 | Depth/Hi-Z | `HDepthPyramid`、`GBufferPassURP` | 当前 Ho-SSGI 仍直接采 GeometryBuffer crossing；GTAO 有独立 pyramid | `未开始` | Frame Debugger 中暂时没有 Ho-SSGI Hi-Z 阶段 | 先完成 ReSTIR，再单独评估 Hi-Z，不和 reservoir 混改 |
| T-Source | 光照源时域重投影 | `HTemporalReprojectionSSGI.compute:79-206`，先生成 `ColorReprojected` | Ho 新增 source history ping-pong，motion/depth/normal 四 tap 重投影，并把结果喂给 raw trace | `部分对齐` | Frame Debugger 对比 Source Reprojection/Raw Trace；灯光变化后看旧亮度残留 | 补 render-scale previous VP/world-plane disocclusion 和亮度 moments |
| T0 | Temporal color reprojection | `HTemporalReprojectionSSGI.compute:230-365` | Ho Temporal 使用 motion、四 tap history、depth/normal、source luminance | `部分对齐` | Temporal reuse 开/关；看历史拖影和上下边缘错位 | 补 render-scale/history UV 契约和 local source clamp |
| R0 | Ray candidate | `HRenderSSGI.compute:71-145` | world-space cosine ray；命中 source；candidate target=luminance(candidate)；M 包含 miss | `部分对齐` | Raw Trace 看原始噪声；Raw GI 看 candidate resolve | 改用稳定低差异/blue-noise 序列，确认 candidate 能量归一化 |
| R1 | Reservoir payload | `HReservoirSSGI.hlsl:28-122` | Color/Wsum/M/target/hit/distance + direction/originNormal，RGBAHalf MRT | `部分对齐` | Reservoir Weight/M/Hit；检查 target、M、W 是否合理 | 评估整数 packed layout；目前不急于复制 HTrace bit packing |
| R2 | Temporal reservoir | `HRestirSSGI.compute:81-123` | 四 tap history reservoir merge，history M cap=100，reuse 可单独关闭 | `部分对齐` | Temporal reuse 开/关；静止画面噪声下降且灯光变化能响应 | 增加 HTrace 风格 reprojected hit validity 和 selected target 重评估 |
| R3 | Temporal validation | `HRestirSSGI.compute:125-245` | selected-ray 几何 re-march + source lighting validation；只限制 history | `部分对齐` | Validation 开/关；不能把当前 candidate 压黑 | 对 moving hit、off-screen、曝光变化分别做测试 |
| R4 | Firefly | `HRestirSSGI.compute:272-334` | 7x7 luminance moments，限制 reservoir W，可单独关闭 | `部分对齐` | Firefly 开/关；亮点应减少但不应整体变暗 | 复核 first frames 权重和 moment 边界采样 |
| R5 | Spatial candidate reuse | `HRestirSSGI.compute:338-440` | world-plane Poisson 8 邻居；plane/normal/depth/Gaussian 权重；reuse 可单独关闭 | `部分对齐` | Spatial reuse 开/关；边缘不能跨平面串光 | 使用稳定 Poisson buffer，替换当前 shader 常量表 |
| R6 | Spatial validation | `HRestirSSGI.compute:445-498` | selected-ray 8-step re-march；失败回退中心 reservoir | `部分对齐` | 空间 validation 开/关；失败时不能黑屏 | 保存 spatial guidance/occlusion，支持第二轮独立 validation |
| D0 | Temporal denoiser | `HDenoiserSSGI.compute:98-177` | 当前没有独立的 post-ReSTIR temporal radiance accumulation | `未开始` | 比较 Spatial resolve 与最终输出的样本稳定性 | 以 GI local RGB AABB/history sample count 为目标实现 |
| D1 | Spatial denoiser | `HDenoiserSSGI.compute:244-343` | 一次 5x5 HDR bilateral，depth/normal/Gaussian，tone map | `部分对齐` | Raw GI 与最终 Off 对比；边缘与亮点不能扩散 | 对齐 HTrace 两轮 filter、plane/AO guidance |
| D2 | Interpolation | `HInterpolationSSGI.compute`、`SSGIPassURP.cs:616-637` | 不适用：当前 Ho 固定 full-resolution 质量路径 | `不适用` | 不打开 checkerboard/半分辨率 | 只有引入 render scale 后再排期 |
| O0 | Output | `ColorComposeURP.shader` | `_HoGITexture`，Before Post Processing composite | `部分对齐` | 关闭 Ho-SSGI 后无残留；Geometry coverage 控 receiver | 后续再接 lilToon 材质，不改 producer 契约 |

## 当前资源契约

### Ho raw/temporal reservoir

```text
ReservoirColor.rgb = selected radiance
ReservoirColor.a   = Wsum
ReservoirAux.x     = M
ReservoirAux.y     = selected target
ReservoirAux.z     = HitFound
ReservoirAux.w     = distance
ReservoirRay.xy    = oct(direction)
ReservoirRay.zw    = oct(origin normal)
```

当前格式是 RGBAHalf，优先保证可调试性；没有直接复制 HTrace 的 `uint4` packed layout。改变布局前必须先完成 R1/R2/R3 的画面对齐，否则只是换存储格式，不能解决噪声。

### Volume A/B 开关

| 开关 | 关闭后应观察什么 |
|---|---|
| `时域 Reservoir 重用` | 只剩当前帧 candidate + spatial；静止画面噪声明显上升 |
| `空间 Reservoir 重用` | 保留 temporal；空间连续性下降，邻接平面串光应减少 |
| `时域射线验证` | 历史更稳定但灯光/遮挡变化反应更慢 |
| `空间射线验证` | 空间 reuse 更强但漏光风险增加 |
| `Firefly 抑制` | 亮点和极端 W 是否减少 |

调试模式与算法阶段的关系：

```text
Raw Trace       = 当前帧逐射线候选，噪声正常
Raw GI          = temporal/spatial reservoir + denoise 后的 GI
Reservoir Weight= selected W 诊断
Reservoir M     = 历史/候选数量诊断
Reservoir Hit   = selected candidate 命中诊断
Confidence      = producer confidence，不等于 reservoir M
```

## 朱木古堂验收表

| 用例 | 固定条件 | 观察项 | 通过标准 |
|---|---|---|---|
| 静止相机 | ray/step 固定，所有 ReSTIR 开 | Raw GI、Off、Reservoir M | 10-20 帧后噪声明显下降，M 不持续失控 |
| 摄像机上下移动 | 开 motion、四 tap history | Temporal reuse、Confidence | 不出现上下区域反向穿越，不出现整屏历史拖影 |
| 灯光强度变化 | 只改灯，不动相机 | Temporal lighting validation | 亮度能快速响应，不长期保留旧灯光 |
| 红墙/白地面 | source saturation=1 | Raw Trace -> Raw GI | 颜色反弹连续，描边不成为独立亮源 |
| 外扩描边 | 主 shader 保持描边 | Source Validity、Geometry、Raw GI | 描边 coverage 无效，不进入 candidate/history |
| 平面交界 | 地面/墙面/角色边缘 | Spatial reuse/validation | 不跨平面抹亮，不出现漏光长带 |
| 低命中区域 | 保持当前 ray 数 | Confidence、Reservoir Hit | 低命中只降低贡献，不产生异常亮点 |
| 重载/切相机 | 改 camera 或分辨率 | History reset | 无上一相机残留，无旧 reservoir 拖影 |

## 当前执行顺序

1. 先用 `Raw Trace` 确认 candidate、深度 crossing 和 source 正确；
2. 开 temporal reuse，验证四 tap history、M cap 和 lighting validation；
3. 开 spatial reuse，验证 world-plane Poisson 与 selected-ray re-march；
4. 开 Firefly 与 bilateral denoise，比较 Raw GI/最终 Off；
5. 只有上述四步稳定后，才考虑 Hi-Z、AO guidance、recurrent blur 或 lilToon 内部接收。

## 暂不做

- 不把 GTAO reservoir 合并进 Ho-SSGI；
- 不新增 Ho-RTBuffer/HoReSTIR RendererFeature；
- 不把 MetadataBuffer 接入 GI；
- 不为了低质量档维护另一套算法；
- 不在没有 A/B 证据前调整光源耦合或材质接收语义。

最后更新：2026-09-09

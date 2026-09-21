# Ho-SSGI / HTrace ReSTIR 对齐：结论、契约与坑

> 状态：**已实现并收敛（2026 文档审核）**。本文原为逐阶段进度表，现压缩为「链路 + 资源契约 + 已知差异 + 坑 + 验收」。
> Ho-SSGI 已落地（`Runtime/SSGI/`，自研，读 `gisexclude`，opaque 之后合成）；HTrace SSGI 只作对照参考（`Assets/HTraceSSGI`）。
> 场景：`D:\Unity_Project\BREAK_URP\Assets\mmd场景测试\朱木古堂`。描边白边是已知问题（见 `LILTOON_KNOWN_ISSUE_OUTLINE_SSGI_GLOW.md`）。

## 1. 链路

```text
HoGeometryBuffer + opaque camera source
  -> source color history/reprojection
  -> raw trace candidate reservoir
  -> four-tap temporal reservoir merge
  -> firefly W clamp
  -> world-plane Poisson spatial reservoir reuse
  -> selected-ray re-march validation
  -> temporal radiance accumulation + RGB AABB clamp
  -> spatial filter 1 / 2（tone mapped bilateral）
  -> _HoGITexture composite（Before Post Processing）
```

对照 HTrace 的差异只在实现细节（见 §3），链路阶段一一对应：PrePass/motion/geometry → temporal reprojection → checkerboard（**Ho 不适用**，固定 full-resolution）→ GI trace → ReSTIR temporal → firefly → spatial resampling → spatial validation → denoiser temporal → spatial filter 1/2 → composite。

## 2. 资源契约（当前真值）

### raw / temporal reservoir

```text
ReservoirColor.rgb = selected radiance
ReservoirColor.a   = W = Wsum / (M * target)
ReservoirAux.x     = M
ReservoirAux.y     = selected target
ReservoirAux.z     = HitFound
ReservoirAux.w     = distance
ReservoirRay.xy    = oct(direction)
ReservoirRay.zw    = oct(origin normal)
```

### near-occlusion reservoir（独立信号）

```text
OcclusionAux.x = selected near-occlusion [0,1]
OcclusionAux.y = M
OcclusionAux.z = W = Wsum / (M * selected near-occlusion)
OcclusionAux.w = selected distance
OcclusionRay.xy = oct(selected direction)
```

- **near-occlusion 是独立的空间指导信号，不是 GI 能量**，也不替代已发布的 `_HoAOTexture`。
- `_HoAOTexture` 只参与“AO 状态差异权重”；**GI 的 radiance reservoir 不乘 AO**。
- 格式是 **RGBAHalf**（优先可调试性），没有复制 HTrace 的 `uint4` packed layout。**改变布局前必须先完成画面对齐**，否则只是换存储格式、不解决噪声。

### 调试模式 → 算法阶段

| 模式 | 含义 |
| --- | --- |
| Raw Trace | 当前帧逐射线候选（噪声正常） |
| Raw GI | temporal + spatial reservoir + denoise 之后的 GI |
| Reservoir Weight / M / Hit | selected W / 历史与候选数量 / selected candidate 命中 |
| Confidence | producer confidence，**不等于** reservoir M |
| Temporal Resolve | Temporal ReSTIR 输出（未经空间重用） |
| Spatial Resolve | Spatial Validation 输出（未经 temporal denoise） |
| Temporal Denoised | temporal radiance accumulation 输出 |
| Spatial Filter 1 | 第一轮 bilateral 输出 |
| Near Occlusion | near-occlusion reservoir 均值诊断 |

### Volume A/B 开关

| 关掉 | 应该看到什么 |
| --- | --- |
| 时域 Reservoir 重用 | 只剩当前帧 candidate + spatial；静止画面噪声明显上升 |
| 空间 Reservoir 重用 | 保留 temporal；空间连续性下降、邻接平面串光减少 |
| 时域射线验证 | 历史更稳定，但灯光/遮挡变化反应更慢 |
| 空间射线验证 | 空间 reuse 更强，但漏光风险增加 |
| Firefly 抑制 | 亮点与极端 W 是否减少 |

## 3. 已实现 vs 仍缺（对照 HTrace）

- **几何输入**：GB `NormalDepth`（RGB 法线、A 线性深度/coverage）是唯一几何真值；**描边 coverage 必须为无效**，不进入 candidate/history。
- **Motion**：复用 URP `motionVectorColor`；render scale / motion UV 的 Y 翻转契约仍需登记。
- **Camera history**：每个 Game/SceneView camera 独立 history（最多 4 槽、LRU 淘汰），cameraId/尺寸变化即失效；GI/depth/reservoir/source/denoised/metadata 全部 ping-pong，Temporal GI / source / denoised 都有 producer，保存 previous inverse VP。**仍缺 camera cut 与 render-scale 的显式 reset。**
- **Depth / Hi-Z**：自有 5 级 R32 线性深度 pyramid；trace 远段采粗层，候选 crossing 回读 mip0 GB 精确确认，selected-ray validation 始终用精确深度。**仍缺 raw-device-depth 归约、LOD 随 march footprint 变化、refine intersection / half-step validation。**
- **Source 时域重投影**：source history ping-pong + motion/depth/normal/previous-plane 四 tap + 3×3 source luminance moments clamp。**仍缺 render-scale 坐标与 best-tap/曝光语义。**
- **Candidate**：world-space cosine ray、稳定 16 帧低差异序列、35% 距离阈值后指数衰减、`M` 含 miss、可选 sky fallback、几何命中与 radiance brightness 分离、**同时产生独立 near-occlusion candidate**。
- **Temporal reservoir**：四 tap history merge，near-occlusion 同步重投影/验证，history M cap = 100，reuse 可单独关闭，replacement 随帧重新播种。**仍缺 reprojected hit validity 与 selected target 重评估。**
- **Temporal validation**：selected-ray 几何 re-march + source lighting validation，**只限制 history、不压黑当前 candidate**；用 previous inverse VP 做 plane agreement。**moving hit / off-screen / 曝光变化三类需分别测。**
- **Firefly**：7×7 luminance moments，按 metadata sample count 调阈值，限制 reservoir W，可单独关闭。
- **Spatial reuse**：world-plane Poisson 8 邻居，用 tapGeometry 重建世界位置算 plane/normal/depth/Gaussian 权重；`_HoAOTexture` 只作 AO 状态差异权重；near-occlusion 参与邻居一致性。**仍缺共享稳定 Poisson buffer、AO adaptive scale、spatial occlusion history。**
- **Spatial validation**：读 near-occlusion guidance，按配置步数重走 selected GI ray，失败回退 temporal GI；`guidance.w` = selected-ray visibility，`guidance.z` = 独立 near occlusion。**这两个信号不能互换。**
- **Denoiser**：独立 denoised history；3×3 moments、DirectClipToAABB、`1−1/N` 历史权重、motion/depth/normal/previous-plane 拒绝；独立 sample-count/invalidity metadata history；首帧用本帧开始时的 history validity。两轮 spatial filter 用 HTrace 风格 maximum-channel tone map/inverse。**仍缺 invalidity 传播与曝光变化重置的完全对齐。**
- **Interpolation / checkerboard**：**不适用**（Ho 固定 full-resolution 质量路径，不打开半分辨率）。只有引入 render scale 后再排期。

## 4. 踩过的坑

1. **诊断模式必须与算法阶段一一对应**（见 §2 表）：把 `Confidence` 当成 reservoir M、把 `Spatial Resolve` 当成最终 GI，都会得出错误结论。
2. **验证必须“只限制 history”**：射线验证失败时压黑当前 candidate 会直接吃掉当帧 GI。
3. **`guidance.z` / `guidance.w` 语义不能互换**：一个是 near-occlusion，一个是 selected-ray visibility。
4. **AO 不是 GI 的一部分**：GI radiance reservoir 不乘 AO；`_HoAOTexture` 只做状态差异权重；near-occlusion 是独立指导信号。
5. **Camera history 必须按 camera 隔离并保存 previous inverse VP**：否则 SceneView/GameView 交替会互相清空，切换相机/分辨率/重载场景会读到旧历史。
6. **首帧不能读 denoised history**；且 denoised / metadata 两组 history 必须同步失效。
7. **粗层 Hi-Z 不能直接当命中结论**：候选 crossing 必须回读 mip0 精确深度确认，否则远处墙面/薄物体出现假命中。
8. **`M` 必须把 miss 计入**：否则低命中区域会被误当成高置信度，产生异常亮点；低命中应该只降低贡献。
9. **改存储布局不解决噪声**：先把 R1/R2/R3 的画面对齐做完，再考虑 packed layout。
10. **不要在没有 A/B 证据前动光源耦合或材质接收语义。**

## 5. 验收（朱木古堂）

| 用例 | 观察项 | 通过标准 |
| --- | --- | --- |
| 静止相机（ray/step 固定、ReSTIR 全开） | Raw GI、Off、Reservoir M | 10–20 帧后噪声明显下降，M 不持续失控 |
| 相机上下移动（开 motion、四 tap history） | Temporal reuse、Confidence | 无上下区域反向穿越、无整屏历史拖影 |
| 灯光强度变化 | Temporal lighting validation | 亮度快速响应，不长期保留旧灯光 |
| 红墙 / 白地面 | Raw Trace → Raw GI | 颜色反弹连续，**描边不成为独立亮源** |
| 外扩描边 | Source Validity、Geometry、Raw GI | 描边 coverage 无效，不进入 candidate/history |
| 平面交界 | Spatial reuse/validation | 不跨平面抹亮、无漏光长带 |
| 低命中区域 | Confidence、Reservoir Hit | 只降低贡献，不产生异常亮点 |
| 重载 / 切相机 | History reset | 无上一相机残留、无旧 reservoir 拖影 |

**执行顺序**：先 `Raw Trace` 确认 candidate/深度 crossing/source → 开 temporal reuse 验证四 tap history、M cap、lighting validation → 开 spatial reuse 验证 world-plane Poisson 与 selected-ray re-march → 开 Firefly 与两轮 spatial filter 对比 Raw GI / 最终 Off。

## 6. 暂不做

- 不把 GTAO reservoir 合并进 Ho-SSGI；
- 不新增 Ho-RTBuffer / HoReSTIR RendererFeature；
- 不接旧 MetadataBuffer（已删除；需要对象语义走 AC / `gisexclude`）；
- 不为低质量档维护另一套算法；
- 不在没有 A/B 证据前调整光源耦合或材质接收语义。

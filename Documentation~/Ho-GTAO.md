# Ho-GTAO：设计意图与现状

> 状态：**已实现并接入**（2026 文档审核核对）。原“v1 实施规划”的任务清单已执行完，本文保留**设计意图、契约、现状参数、取舍与坑**。
> 配套：`归档/LILTOON_HTRACE_GTAO_QUALITY_REFERENCE.md`（HTrace 参数档案）、`归档/LILTOON_GTAO_H_TRACE_ALIGNMENT_WORKSHEET.md`（对齐口径与坑）、`归档/LILTOON_GTAO_MSAA_SILHOUETTE_INVESTIGATION_LOG.md`（MSAA 轮廓白线的机理与修法）。
> 契约：`ao` 通道（R8f，0..1 visibility，1 = 无遮挡）；生产端 = 自研 `Ho-GTAO`；消费端 = 材质采样 + AOV。

## 1. 目标与立场

1. 独立的 `Ho-GTAO` RendererFeature 生产实时 AO。
2. 发布全局纹理 `_HoAOTexture`。lilToon 在着色时采样，AOV 和 DebugTile 也可读取 AO。
3. `_HoAOAvailable` 在每台相机开始渲染时归零，仅在该相机实际发布纹理时置一。实时来源不可用时，材质的实时项为中性值，混合模式继续使用烘焙 AO。
4. **架构立场（强硬版）**：GeometryBuffer 是**屏幕几何的唯一生产者**（全分辨率、不动 renderScale），GTAO 是它的**消费者**，降分辨率只是消费者自己的计算分辨率。这与 HTrace“自画 PrePass + 用深度重建法线”的隔离路线相反——**只用一个屏幕几何，谁用谁降采样**：
   - 消除 HTrace 的三笔代价：多画一遍深度、重建法线 ≠ 真实渲染法线（toon 法线贴图下 AO 错位/网格感的来源）、两套深度/法线语义并存。
5. **唯一结构性时序改动**：GeometryBuffer 与 Ho-GTAO 同在 `BeforeRenderingOpaques`，**先后由 Renderer Feature 列表顺序保证**（GB 在前、GTAO 紧随）。HoTrace 时代 GB 在 opaque 之后（300），而材质采样模式要求 AO 在 opaque 绘制前就绪。
6. **lilToon 侧解耦已完成**：`_ScreenSpaceAOSource` / `_HTraceBufferAO` / `GetScreenSpaceAmbientOcclusion` / `_SCREEN_SPACE_OCCLUSION` / `_AmbientOcclusionParam` / `_ScreenSpaceOcclusionTexture` 在 lilToon Includes 里**全部 0 命中**——材质只采样 `_HoAOTexture`，与 URP 内置 SSAO 零共享状态。

## 2. 契约与语义

### 仅发布 AO 纹理

Settings 与 Volume 的“仅发布 AO 纹理”默认关闭。启用后，在不透明绘制前只清理并发布全白、可写的 `_HoAOTexture`，不请求 GB／Color／Motion，不创建新的 GTAO 历史，也不执行追踪和滤波。此模式为 Capsule AO 等附加项提供中性载体；总开关关闭时仍不发布。

正常／仅发布切换会使该相机的旧 GTAO 历史失效。仅发布模式只接受 AO 调试视图，其它调试选择不拉起追踪计算。公共 AO 仍是全相机尺寸、单采样、1 为无遮挡；附加项通过帧内句柄和可写标记借用，不能把几何／Temporal 调试图当作公共可见度写入。

Volume 覆盖解析到逐相机运行时副本，不回写 Feature 序列化的回退设置。Capsule AO 的组件安装、混合和支持域见 [Capsule AO / Cross](角色特化/CharacterSpecialization_CapsuleAO.md)。Compatibility 仅实现白色发布路径，原正常 GTAO 计算仍要求 RenderGraph。

- `_HoAOTexture` 存储 0～1 的可见度，1 表示无遮挡。材质在「表面定义 → AO」选择关闭、烘焙、实时或混合，并设置实时权重、调整和遮罩。阴影和 AO 压暗各自控制对这份 AO 的响应。
- **必须显式单采样**：`msaaSamples = None`、`bindTextureMS = false`、`filterMode = Bilinear`——发布给材质的全局纹理**不能继承相机颜色的 MSAA 描述**（4x + bindTextureMS 会让正经 `Texture2D` 采样的材质读未定义行为）。
- **公共输出极性**：中间缓冲存“AO 量”（1−visibility），最终输出存“visibility 乘数”（1 = 无 AO）。fragment 版必须沿用这个极性约定。
- `aointent`（意图模式）：**未做**（契约占位保留）。
- 与 `gisexclude` 无关（GI 用）；**描边不写 GB，因此 AO 天然不受描边壳影响**。
- 发布的调试纹理（`_HoGTAODebugColor`）与真 AO 分开命名；调试 pow 只影响调试画面。

## 3. 现状

### 3.1 算法与流水线

`深度金字塔（4 级 R32，2×2 最近深度归约）→ Bitmask ray march（32-bin，按 log2(length(sampleOffset))-3 选 LOD）→ temporal（可选，重投影 + 累积 + 校验）→ spatial（Disk / Box）→ 全分辨率发布`。

- 只实现 **VisibilityBitmasks**（复用 HTrace 的 32-bin `UpdateBitmask`、线性厚度、距离衰减、平方步进、蓝噪声抖动、逐帧 slice rotation），**没有 HorizonSearch 连续 arc 分支**。
- 逐片输出 `1 − countbits(mask)/26`。
- temporal：4 tap 历史 + 深度/法线/previous-plane 校验 + sampleCount 累积 + rejection；对象 Motion Mask/Delta（GB 深度上生产）+ 相机 MV（复用 URP motion vector pass）。
- spatial：**Disk**（半径随对比度自适应，边缘保护更好）或 **Box**（Poisson 偏移、可叠 pass，便宜稳定）——两者都是双边式。
- `temporalFrameCount = 0` 时跳过历史、历史深度和运动矢量路径，空间滤波直接消费当前帧追踪结果。
- GTAO 的追踪、历史和空间滤波按 `Full / Half / Quarter` 分辨率执行，最终 `_HoAOTexture` 在发布前放大到相机分辨率。Half/Quarter 复用全分辨率 GeometryBuffer 和深度金字塔，避免重复绘制几何。

### 3.2 一次性填写预设（`HoGTAOQuality`）

Volume Inspector 的 Low/Medium/High 按钮只填写以下六项具体参数，不修改任何 Override 勾选，也不修改半径、厚度或调试参数。用户手动启用所需 Override，填完后可自由调整。运行时没有质量档字段或自动套用预设的分支，只读取具体参数；不提供旧质量字段兼容或迁移。

| 预设 | 算法 | 去噪 | 计算分辨率 | Slice×Step |
| --- | --- | --- | --- | --- |
| Low | Bitmask | 无时间累积 + Box×1 | Quarter | 2×8 |
| Medium | Bitmask | 4 帧累积 + Box×2 | Half | 2×12 |
| High | Bitmask | 12 帧累积 + Box×3 | Full | 4×32 |

### 3.3 公开参数（默认值）

Renderer Feature 默认：`worldSpaceRadius = 3.0`、`screenSpaceRadius = 30`、`thickness = 0.6`（Linear 档：`max(T/10·linDepth, T)`）、`sliceCount = 4`、`stepCount = 32`、`temporalFrameCount = 12`、`temporalRejection = 0.7`、`spatialFilter = Box`、`filterRadius = 0.4`、`filterAdaptivity = 0.1`、`boxPassCount = 3`、`useAttenuation = 开`、`useLinearThickness = 开`、`passEvent = BeforeRenderingOpaques`。

调试模式：`Off / AO / Depth / Normal / Motion / Temporal Disocclusion`（**序列化值 5 保留给历史资产**，不要重排）。`debugIntensity` 默认 3.672（HTrace 对照值），只影响调试显示。

### 3.4 不带进 Ho 的 HTrace 参数（及原因）

- `TracingMode=HorizonSearch`（只保留最高档 Bitmask）；
- `Denoising=None/SpatialOnly/TemporalOnly`（由具体累积帧数和空间滤波参数控制）；
- `DepthFormat`（HTrace 只 R16 一档；Ho 侧不受该枚举约束）；
- `ExcludedIntensity`（用材质 AO 掩码替代，不引入新通道）；
- `Intensity`（契约 0..1，强度归材质）；
- `OutputDithering`（HTrace 侧就是死参数）；
- `DebugModeGTAO` 四象限拼图（用 feature-local debug + DebugTile 替代）。

## 4. 坑

1. **同事件顺序是契约，不是巧合**：GB 与 Ho-GTAO 都在 250，先后靠 feature 列表；顺序颠倒会读到 black/空纹理。**改 pass event 前先看 `Ho-RenderFeatureOrdering.md`。**
2. **历史必须按 camera 隔离**（SceneView/GameView 交替否则互相清空），并在相机尺寸/切换/重载时重置。
3. **`_HoAOTexture` 要显式关 MSAA 并指定 Bilinear**（见 §2）；`filterMode` 不指定会继承相机颜色。
4. **半分辨率换算不要用 `_ScreenParams.zw`**：那是全屏尺寸，要乘 divisor 才对（更稳的做法是用计算 RT 的 texel size 全局参数），否则会出现半径变小/条纹这类“分辨率感错误”。
5. **矩阵与 Z 参数**：march 的视空间重建依赖 `UNITY_MATRIX_P/V`，temporal 的 deviceZ 依赖 `_ZBufferParams` 逆式；反向 Z 平台与 `GL.GetGPUProjectionMatrix` 的传参（`renderIntoTexture`）都要显式对齐，否则表现为相机运动时历史漂移、AO 闪烁位错。
6. **RG 中把持久 `RTHandle` `ImportTexture` 当 raster attachment 写**需要有预案（失败特征是 Console 断言 / 历史不更新 → 改为写 transient 再 `CopyTexture`）。
7. **无 temporal 档（Low）会有静止噪声/闪烁**：靠蓝噪声 + Disk 兜底，与 HTrace SpatialOnly 同级。
8. **Bitmask + 降分辨率在细几何（头发缝隙）上仍可能渗漏**：由 Radius/Thickness 调，并靠深度引导上采样抑制 checker 残留。
9. **MSAA 开启时的轮廓白线**不是 AO 数值问题，而是“单值 AO × MSAA 平均色”+ 解析几何取最近 sample 的结构性问题；修法见 `归档/LILTOON_GTAO_MSAA_SILHOUETTE_INVESTIGATION_LOG.md`。

## 5. URP 内置 SSAO：解耦（已做）与完整删除（未做）

**本次只解耦不删除**：Ho-GTAO 走独立 `_HoAOTexture`，不写 `_ScreenSpaceOcclusionTexture`、不启 `_SCREEN_SPACE_OCCLUSION`、不碰 `_AmbientOcclusionParam`、不占 `resourceData.ssaoTexture`。内置 SSAO feature 装不装都无副作用。

将来若要从 URP fork 里**整块删掉**内置 SSAO，先看这份清单：

1. **运行时安全禁用**：仅移除 feature 即可——URP 自带 shader 走 `_SCREEN_SPACE_OCCLUSION` OFF 分支 → AO 恒 1。**但必须保留 `ScriptableRenderer.cs` 里每相机渲染前的 `_AmbientOcclusionParam` 清零**（x=0 是“无 SSAO → AO=1”的安全开关），别连它一起删。
2. **硬依赖三群**（按风险排序）：① Deferred 的 `SSAOOnly` pass（`DeferredLights` 的 RenderSSAOBeforeShading / SSAOOnlyPassNames + `StencilDeferred.shader/hlsl`）；② 构建与剥离逻辑（`ShaderBuildPreprocessor` / `ShaderScriptableStripper` / `ScreenSpaceAmbientOcclusionStripper` / `UniversalRenderPipelineAssetPrefiltering` 直接引用 `ScreenSpaceAmbientOcclusion.k_*` 常量 + 对应测试）；③ 资源注册（`ScreenSpaceAmbientOcclusion` 的 Persistent/DynamicResources、BlueNoise 资源、SSAO shader/hlsl）。
3. **同批扫尾**：keyword 全局态（删 SSAO 后在 `ClearRenderingState` 追加关闭 keyword）；`_ssaoTexture` 链路删 SSAO 后 `IsValid` 自动短路（**最低改动=保留**）；DepthNormals prepass 会自动解除（无 pass 请求 Normal input 后 `requiresNormalsTexture=false`）；20+ 处 `multi_compile _SCREEN_SPACE_OCCLUSION` 不删也无运行时问题（只影响变体数）。
4. **何时做**：等 Ho-SSGI 完工、HTrace 全移除之后再启动——一次动 URP fork + 构建 + 测试，避免半吊子状态。

## 6. 不做清单（登记不实现）

bent normals、RTAO、意图模式（`aointent`）、`gisexclude` 接入 AO、描边排除位、URP 内置 SSAO 的移除。

## 7. 实机验证要点（失败特征 → 预案）

| 假设 | 失败特征 | 预案 |
| --- | --- | --- |
| RG 中 `ImportTexture(RTHandle)` 可作 raster attachment 写 | Console 断言 / 历史不更新 | temporal 改为输出 transient，帧内 `CopyTexture` 到持久 RTHandle |
| `UNITY_MATRIX_P/V` 在 RG raster pass 内可用 | AO 图案扭曲、位置错位 | march 内显式 `SetGlobalMatrix` 相机矩阵（HTrace 同款） |
| `_ZBufferParams` 逆式在反向 Z 平台正确 | temporal 重投影错乱 / AO 闪烁位错 | 用 `GL.GetGPUProjectionMatrix` 显式逆投影 |
| `GL.GetGPUProjectionMatrix(proj, false)` 与渲染目标一致 | 相机运动时历史重投影漂移 | 按 `cameraData.renderIntoTexture` 传参 |
| 半分辨率 RT 的采样换算正确 | 半径变小 / 条纹 | 用计算 RT 的 texel size，而不是 `_ScreenParams.zw` |
| `_HoAOTexture` 默认 white 被全局覆盖 | 挂载后仍全白 | 确认 pass 执行 + feature enabled + GB 在 250 |
| 250 同事件顺序 = 列表顺序 | AO 读空（黑） | 确认列表顺序（GB 在前），必要时自定义事件值方案 |

> 常见现象对照：**全白/无效果** → 后两项；**图案扭曲** → 矩阵/换算；**相机运动闪烁拖影** → Z/投影参数；**历史不更新** → RG 附件写入。

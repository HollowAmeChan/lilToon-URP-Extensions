# HoShadowCast 额外投影光源与 ShadowMap 设计

本文记录 HoShadowCast 第一版设计。它用于补齐项目里“必须产生 shadowcast 的少量光源”，并产出一套独立 shadow atlas。

## 一句话分工

```text
HoShadowCast = 少量确定性 shadow map 光源
```

HoShadowCast 不替代 URP 光照，也不接管所有灯。它只回答“哪些光必须产生可控 shadowmap”，并输出一组项目自己的 shadow atlas 与必要的 atlas 描述数据。

## 管理方式

组件不挂在 Light 上，而是挂在一个空物体上集中管理。推荐组件名：

```text
HoShadowCastController
```

第一版字段建议：

```text
方向光阴影列表: Light[4]
聚光阴影列表: Light[4]
点光阴影列表: Light[4]

Caster Layer Mask
Shadow Strength
Atlas Size
Directional Resolution
Spot Resolution
Point Face Resolution
Debug Mode
```

灯本身仍然是普通 Unity `Light`。Controller 只是引用这些 Light，并决定它们是否进入 HoShadowCast 阴影链路。

被引用的 Light 可以把 Unity 自带 `Shadows` 保持为 `None`。HoShadowCast 生成侧不再依赖 URP additional shadow caster culling，因此可以只借用 Light 的位置、方向、范围、颜色和阴影参数来生成自己的普通 `_HoShadowCastAtlas` 与第二天光 `_HoShadowCastSecondDirectionalAtlas`。

这不代表画面要消费 URP 自己的 additional light shadow。lilToon / lilPBR 接收侧会忽略 URP additional lights 的 `shadowAttenuation`；最终额外投影由 `_HoShadowCastAtlas` 与 `_HoShadowCastSecondDirectionalAtlas` 决定。URP 主光 shadow receiver 仍然保持原样。

## URP 主光边界

URP 内部有 Main Light / Sun Source 语义：

```text
RenderSettings.sun 指定 Directional Light 时，URP 优先把它当主光。
没有 Sun Source 时，URP 会在可见 Directional Light 里选最亮的一个。
Point Light 和 Spot Light 不会成为 URP Main Light。
```

HoShadowCast 不提供自己的主光字段，也不改写 URP 内置 Main Light / Sun Source：

```text
- 不设置 RenderSettings.sun。
- 不改变 URP Main Light 的选择。
- 不干预 URP 内置 main light shadow caster。
- 运行时如果列表里的灯等于当前帧 URP mainLightIndex，则跳过，不写入任何 HoShadowCast atlas。
```

`RenderSettings.sun` 是场景全局状态，会影响 URP Main Light、天空盒太阳方向以及部分环境光和反射语义，因此 HoShadowCast 第一版不主动写入它。需要指定 URP 主光时，应由场景原本的灯光/渲染设置负责，而不是由 HoShadowCast RendererFeature 修改。

方向光列表只表示“主光之外仍需额外 shadowcast 的方向光”。如果用户误把 URP 主光放入列表，运行时会根据 `UniversalLightData.mainLightIndex` 自动跳过，避免主光阴影被 HoShadowCast 重复处理。

## 光源容量

第一版目标容量：

```text
4 Directional Lights
4 Spot Lights
4 Point Lights
```

当前 shadow slice 数：

```text
Second directional atlas:
  Directional: up to 4 lights * up to 4 cascades = 16 slices

Regular atlas:
  Spot:        4 slices
  Point:       24 slices, each point light uses 6 cube faces
  Total:       28 active punctual slices, fixed arrays still keep 32 slots for compatibility
```

项目当前不优先考虑性能，容量可以按这套上限设计。实际场景通常不会填满；如果将来需要控制开销，可再增加 caster scope 或区域约束。

## 第一版阴影形态

第一版先做简单阴影，不深入做完整软阴影。receiver 侧使用 3x3 compare 采样做基础边缘过渡，并保留一个暗部下限，避免强度为 1 时直接把材质光照压成死黑。

```text
Directional:
  Controller 的 4 个额外方向光槽进入独立第二天光 atlas。
  每盏有效方向光写入 secondDirectionalCascadeCount 个 cascade tile。
  receiver 按相机距离为每盏方向光选择一个 cascade，只采样命中的 tile。

Spot:
  每个聚光一个 perspective shadow slice。

Point:
  每个点光 6 个 cube face slice。
  receiver 根据像素相对点光的方向选择对应 face，只采样命中的 cube face。
```

因为 URP additional shadow atlas 不支持 additional directional shadow，HoShadowCast 不应依赖 URP 内置 additional shadow 机制来表达这套设计。它应渲染项目自己的 shadow atlas。

## 渲染架构

推荐模块：

```text
HoShadowCastController
HoShadowCastLightRegistry
HoShadowCastRendererFeature
HoShadowCastShadowPass
```

每帧流程：

```text
1. Registry 找到当前场景启用的 HoShadowCastController。
2. Controller 收集方向光、聚光、点光引用。
3. RendererFeature 只用 UniversalLightData.visibleLights 判断是否误放了 URP 主光；普通额外灯即使不在 URP additional shadow 列表里，也会按 Controller 引用强制生成 slice。
4. ShadowPass 按固定容量渲染普通 _HoShadowCastAtlas，并为额外方向光渲染独立 _HoShadowCastSecondDirectionalAtlas。
5. 写入全局 shadow/light 数据。
6. lilToon / lilPBR 在自己的接收侧读取 `_HoShadowCastAtlas` 与 `_HoShadowCastSecondDirectionalAtlas`，并把结果作为独立阴影衰减乘进材质光照。
```

建议全局资源：

```text
_HoShadowCastAtlas
_HoShadowCastLightCount
_HoShadowCastSliceCount
_HoShadowCastAtlasSize
_HoShadowCastWorldToShadow[32]
_HoShadowCastLightData0[12]
_HoShadowCastLightData1[12]
_HoShadowCastLightData2[12]
_HoShadowCastLightColor[12]
_HoShadowCastSliceData[32]
_HoShadowCastSecondDirectionalAtlas
_HoShadowCastSecondDirectionalParams
_HoShadowCastSecondDirectionalWorldToShadow[16]
_HoShadowCastSecondDirectionalLightData[4]
_HoShadowCastSecondDirectionalSliceData[16]
```

数组大小可以按 `4 + 4 + 4 lights` 和 `32 slices` 固定，先避免动态 buffer 带来的调试复杂度。

RenderGraph 注意事项：

```text
每个 shadow slice 需要自己的 RendererList。
不要在同一帧重复执行同一个 RendererListHandle。
```

这是为了避开 Unity RenderGraph 的限制：同一个 RendererList 一帧内执行多次会报错。

## 材质接入

材质侧第一版不增加接收排除、不增加每材质阴影接收强度，也不增加 lilToon / lilPBR 面板参数。接收语义统一由 `HoShadowCastController.shadowStrength` 和每盏 Light 的 `shadowStrength` 控制。

原因：

```text
HoShadowCast 与 URP 内置主光/附加光 shadow receiver 平行存在。
投射侧用材质自己的 ShadowCaster pass 写 shadow depth。
接收侧由 lilToon / lilPBR 调用 HoShadowCast 独立采样函数，不塞回 URP 内置 shadow atlas。
```

lilToon 和 lilPBR 接入方式：

```text
lilToon: 使用现有 URP ShadowCaster pass。
lilPBR: 使用现有 URP ShadowCaster pass。
```

不要用 override material 去替代材质自己的 ShadowCaster pass。cutout、dither、dissolve、透明裁剪、双面和 wind 等语义应由材质自己的 ShadowCaster pass 处理。没有 ShadowCaster pass 的材质第一版就不参与 HoShadowCast。

## 接收边界

HoShadowCast 不把结果塞回 URP 内置阴影接收链路，也不修改 `_MainLightShadowmapTexture` / `_AdditionalLightsShadowmapTexture`。URP 内置 shadow atlas 的 layout、light index、shadow params 都由 URP 管理，外部强行写入风险太高。

lilToon / lilPBR 的接入分为两侧：

```text
投射侧：RendererFeature 使用材质自己的 URP ShadowCaster pass 写入 `_HoShadowCastAtlas`。
接收侧：材质 forward pass 读取普通 `_HoShadowCastAtlas` 与方向光级联 `_HoShadowCastSecondDirectionalAtlas`；directional / point / spot 统一合成 receiver shadow field，再混进主光 shadow attenuation。
```

这套接收不修改 URP 内置 shadow atlas，也不直接压最终颜色。材质消费端使用 `HoShadowCastAttenuation(positionWS)` 接入主光阴影模型，让 lilToon / lilPBR 原本的阴影颜色、toon band、mask 和强度继续生效。多盏 HoShadowCast 灯之间允许相乘变暗，但统一 receiver 输出有最终暗部下限，避免叠到死黑。第一版不做每材质接收排除、每材质强度或彩色阴影。

多光源 HDR / additional light 颜色需要额外乘同一个 `HoShadowCastAttenuation(positionWS)` 作为 brightening gate。它不是新的阴影模型，只是避免材质自己的额外补光把已经被 cast 压暗的区域重新提亮。没有 HoShadowCast atlas、没有 light 或没有 slice 时，采样函数返回 1，因此普通多光源 HDR 颜色不受影响。

额外方向光使用独立的 `_HoShadowCastSecondDirectionalAtlas`。Controller 中的 4 个方向光槽都属于这套第二天光级联系统；每盏有效方向光写入 `secondDirectionalCascadeCount` 个 cascade tile。普通 `_HoShadowCastAtlas` 不再写入方向光，只负责 spot / point，避免方向光级联挤占普通 atlas slice，也避免同一盏方向光被两套 receiver 重复消费。

不要把多光源阴影继续塞进 HoCharacterSpecialization 的前发 DropShadow。DropShadow 仍然是角色特化的局部屏幕空间效果；HoShadowCast 是项目级阴影光源系统。

## 预留但第一版不做

以下能力结构上可以预留，但第一版不实现：

```text
Caster scope / region
某些 caster 只被附近几个标记光影响
基于 AOV 或 group id 的 caster 分组
PCSS 软阴影：见 `HoShadowCast软阴影PCSS规划.md`
材质级 receiver 排除
Per-light 多光源染色：见 `HoShadowCast可选升级-PerLight多光源.md`
```

第一版优先目标是跑通 Controller、shadow atlas、debug view 和 lilToon/lilPBR 独立接收。

## 2026-05-18 实现记录

本次先落地 `HoShadowCastController` 与 `HoShadowCastRendererFeature`：

```text
Runtime/ShadowCast/HoShadowCastController.cs
Runtime/ShadowCast/HoShadowCastRendererFeature.cs
Runtime/ShadowCast/HoShadowCastShaderConstants.cs
Runtime/ShadowCast/HoShadowCastRenderGraphResources.cs
Runtime/ShadowCast/Shaders/HoShadowCastDebug.shader
Runtime/ShadowCast/Shaders/HoShadowCastSampling.hlsl
Editor/ShadowCast/HoShadowCastControllerEditor.cs
Editor/ShadowCast/HoShadowCastRendererFeatureEditor.cs
```

当前实现范围：

```text
1. Controller 挂空物体，集中引用 4 个方向光、4 个聚光、4 个点光。
2. 不提供 HoShadowCast 主光字段；运行时跳过 URP 当前 mainLightIndex，避免重复处理 URP 主光阴影。
3. RendererFeature 默认在 BeforeRenderingPrePasses 生成独立 _HoShadowCastAtlas 与 _HoShadowCastSecondDirectionalAtlas，避开 URP 自己的 main/additional shadow caster。
4. 使用材质自己的 ShadowCaster pass，不使用 override material。
5. 每个 shadow slice 单独创建 RendererList，避免同帧重复执行同一个 RendererList。
6. 输出固定数组全局数据：light count、slice count、atlas size、world-to-shadow、light data、slice data。
7. 调试模式 Atlas / 第二天光 Atlas 会在后处理阶段把对应 atlas 直接显示到相机颜色上，用来确认 shadowmap 是否已经生成。
8. lilToon / lilPBR forward shader 通过 `HoShadowCastSampling.hlsl` 读取 atlas，作为独立 receiver 衰减材质光照。
9. receiver 侧使用 3x3 compare 采样、暗部下限、点光 cube face 选择和可调点/聚光范围衰减速度，避免第一版阴影过黑、过硬或点光采样不到正确 face。
10. Runtime 不要求 `Light.shadows` 必须开启；点光/聚光可以保持 Shadows None，只由 HoShadowCast 生成和接收自己的阴影。
11. lilToon / lilPBR 会忽略 URP additional light 的 `light.shadowAttenuation`，避免 Light.shadows 开启后又吃到 URP 自带低分辨率 additional shadow receiver。
```

暂不包含：

```text
1. 全屏最终投影合成。
2. 把 HoShadowCast atlas 接入 URP 内置 shadow receiver。
3. debug atlas view 之外的可视化工具。
4. caster scope / region 过滤。Controller 里保留 caster layer mask 语义位，第一版还没有用它重新 cull 或过滤 shadow caster。
5. 材质侧 receiver 排除或 receiver 强度。
```

因此第一版判断标准是：选定的普通 Unity Light 能通过 lilToon / lilPBR 现有 ShadowCaster pass 写入对应 HoShadowCast atlas，并且 lilToon / lilPBR forward 材质能读取该 atlas 产生额外接收阴影。

## 2026-05-18 接收隔离与生成侧修正

这次确认的边界如下：

```text
URP 主光阴影：保持 URP 原生 main light receiver，不由 HoShadowCast 替换。
URP 附加光阴影：lilToon / lilPBR 接收侧忽略 additional light 的 light.shadowAttenuation，避免吃到 URP 低分辨率 additional shadow。
HoShadowCast spot / point：由 _HoShadowCastAtlas 采样并进入 HoShadowCastAttenuation(positionWS)。
HoShadowCast directional：由 _HoShadowCastSecondDirectionalAtlas 采样；4 个方向光槽可同时存在，每盏方向光有自己的 cascade tiles。
HoShadowCast brightening gate：lilPBR additional lights / subsurface additional lights，以及 lilToon addLightColor / HDR additional light 再乘同一个 HoShadowCastAttenuation；无 HoShadowCast 时恒为 1。
```

生成侧也不能继续依赖 URP `ShadowDrawingSettings` / `CreateShadowRendererList`。那条路径会绑定到 URP 自己为 main/additional shadow 准备的 shadow caster culling，点光或聚光即使被 HoShadowCastController 引用，也可能因为没有进入 URP additional shadow 数据而得到空 atlas。

现在的规则是：

```text
1. 借用 Unity Light 的位置、方向、范围和 bias 参数，HoShadowCast 自己计算每盏灯、每个 slice 的 view/projection/world-to-shadow。
2. 每个 shadow slice 单独创建普通 RendererList。
3. RendererList 使用材质自己的 ShadowCaster pass。
4. RendererList 使用 HoShadowCastController.casterLayerMask 过滤投射物图层。
5. 不使用 override material，不写回 _MainLightShadowmapTexture 或 _AdditionalLightsShadowmapTexture。
```

这个修正让“生成 HoShadowCast 自己的 shadow atlas”与“最终接收不吃 URP additional shadow”两件事分离。点光/聚光可以不打开 Unity 自带 Shadows，从而避免触发 URP additional shadow receiver；主光仍由 URP 负责；HoShadowCast 只处理被 Controller 显式列出的非主光额外 shadowcast。列表里的灯只要非空、类型匹配、启用中、不是当前 URP 主光，就会尝试分配 slice。当前第一版仍基于相机已有 culling results 创建 RendererList，离相机可见集很远的离屏投射物如果之后暴露问题，再补独立 light-space culling。

实现注意：

```text
ShadowCaster pass 渲染时，unity_WorldToCamera 必须设置为当前 shadow slice 的 light-view 矩阵，而不是相机 view 矩阵。
手写 spot/point/directional slice 时，view matrix 使用 TRS(position, rotation, one).inverse。
本帧会先统计需要的 slice 总数，再按 atlas 网格容量自动压低单 slice 分辨率；点光 6 个 face 不能因为 UI 分辨率过大而整盏灯回滚到 0 输出。
Controller Inspector 会显示同一套容量估算；当某类分辨率超过当前 atlas / slice 数能容纳的上限时，UI 会直接警告运行时会自动降级。
receiver 侧点光按接收点相对点光的主轴方向选择一个 cube face 采样，避免多个 face 叠加造成扇形叠影。slice uv 与 z 范围都必须落在有效区间内，否则视为 lit，不允许把 frustum 外的点强行 clamp 进 shadow compare。
receiver 侧不使用硬件 `TEXTURE2D_SHADOW` compare，而是读取 depth 后手动比较。空 depth 视为 lit，并按 `UNITY_REVERSED_Z` 选择比较方向，附带一个小 receiver bias，避免地面这类 caster/receiver 自投影成整圈黑。
普通 atlas 不再写入额外方向光。Controller 上遗留的 `directionalResolution` / `directionalShadowSize` / `directionalShadowDepth` 是旧版单 slice 方向光路径的兼容字段，当前 Inspector 不再暴露；方向光实际使用第二天光级联参数。
lilToon Console 调试日志会输出 `lightSlices=[name:type@firstSlice+sliceCount]`，用于确认多个点光是否真的写到了不同 atlas slice。
点光/聚光的 shadow view/projection 优先使用 Unity `CullingResults.ComputePointShadowMatricesAndCullingPrimitives` / `ComputeSpotShadowMatricesAndCullingPrimitives`，只保留自建 RendererList、atlas 和 receiver。这样避免手写 cube face 矩阵与 URP ShadowCaster pass 约定不一致导致点光 slice 分配了但 atlas 不写深度。
lilPBR 接收侧在 GetMainLight 后把 HoShadowCastAttenuation 乘进 mainLight.shadowAttenuation；lilToon 接收侧在 LIL_LIGHT_ATTENUATION 宏里把 HoShadowCastAttenuation 乘进 fd.attenuation。additional/HDR 多光源颜色只把 HoShadowCastAttenuation 当 brightening gate 使用，避免补光冲掉 cast 暗区；无 HoShadowCast 时不改变原本多光源表现。
第二天光级联 atlas 是独立资源。Receiver 会对每一盏额外方向光按相机距离选择对应 cascade，只采样该 cascade，再把多盏方向光结果相乘。Debug Mode 可以在普通 atlas 和第二天光 atlas 之间切换。
Atlas Debug 开启时，Runtime 每隔约 60 帧输出一次 HoShadowCast 状态：路径、lightCount、sliceCount、atlas、casterMask、已分配灯数和首个 slice 信息。
```

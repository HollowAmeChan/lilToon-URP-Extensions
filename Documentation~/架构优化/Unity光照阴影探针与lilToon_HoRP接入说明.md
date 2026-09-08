# Unity 光照、阴影与探针：lilToon / HoRP 混合实现与接入说明

> 状态：Draft v0.1
>
> 更新时间：2026-09-08
>
> 适用范围：Unity 6000.x、URP 17.x、本地 lilToon fork 2.3.x、lilToon-URP-Extensions、Ho-GTAO / Ho-GI 计划。
>
> 本文目的：建立一份工程认知文档，说明 Unity 中光照、阴影、Light Probe、Adaptive Probe Volume、Reflection Probe、Lightmap、GTAO、SSGI 等系统如何组合，以及它们当前如何被 lilToon 和 HoRP 消费。

---

## 0. 结论先行

HO/NPR 管线不应在 Lightmap、Light Probe、APV、屏幕空间 GI 之间做单选，而应采用分层组合：

```text
静态几何       -> Lightmap（表面逐纹素烘焙）
动态角色/道具  -> APV 或 Light Probe（空间中的间接光）
金属/宝石/湿面 -> Reflection Probe（环境镜面反射）
水面/镜子      -> Planar Reflection / SSR
动态投影       -> ShadowCaster / ShadowMap
接触暗部       -> GTAO
动态间接增强   -> Ho-GI / SSGI
```

几个必须牢记的边界：

1. Lightmap 烘焙到静态 Renderer 的第二套 UV，不是烘焙到 Light Probe。
2. Light Probe/APV 保存低频环境间接光，不保存真正的镜面反射，也不能替代硬阴影。
3. Reflection Probe 保存 Cubemap，主要给高光、金属、宝石和环境反射使用。
4. ShadowCaster 不读取探针；动态物体仍需要 ShadowMap 才能投影和接收实时阴影。
5. 对 HO 管线，探针应作为稳定的低频基础光照，GTAO/SSGI/SSR/Planar 作为局部或动态增强。

Unity 官方对 Light Probe 的定位是：静态光照通过 Lightmap 处理，动态物体通过附近探针插值获得烘焙间接光。[Light Probes](https://docs.unity.cn/2021.1/Documentation/Manual/LightProbes.html)

---

## 1. 需要考虑的光照与阴影方案

### 1.1 实时直接光与 ShadowMap

URP 的实时光照通常由主光和 Additional Lights 提供。光照方向、颜色、距离衰减和实时阴影在 Forward/Forward+ 阶段计算。

ShadowMap 的基本流程是：

```text
光源视角绘制 ShadowCaster
        -> 生成深度图
相机视角着色时投影到 ShadowMap
        -> PCF/软阴影/衰减
        -> 材质的 shadowmix 或直接光衰减
```

它适合：动态角色投影、动态灯光、接触阴影和风格化主光阴影。它不负责环境反弹光，也不提供镜面环境。

Ho-ShadowCast 目前是额外投影光源的 atlas 生产者；它和 Unity 主光 ShadowMap、材质 toon 阴影门控属于不同层，不应和 Light Probe 混为一谈。

### 1.2 Lightmap：烘焙到静态表面

Lightmapper 对标记为 Contribute Global Illumination 的静态几何计算直接光和间接光，并将结果写入 Lightmap 图集。Renderer 运行时通过 lightmap index、`unity_LightmapST` 和 lightmap UV 采样。

Lightmap 的特点：

- 空间分辨率沿表面展开，静态表面细节高。
- 适合建筑、地面、墙体和固定道具。
- 需要正确的 Lightmap UV、静态标记和烘焙设置。
- 不能自然跟随移动物体。
- Baked Light 本身不能给动态物体提供真正的高频镜面光，镜面环境通常仍由 Reflection Probe 负责。

当前 lilToon 的 URP forward 变体保留 `LIGHTMAP_ON`、`DYNAMICLIGHTMAP_ON`、`SHADOWS_SHADOWMASK` 等关键词，并在 [lil_common_macro.hlsl:2051](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_macro.hlsl:2051) 采样 Lightmap。

### 1.3 Light Probe / SH：烘焙到空间采样点

传统 Light Probe 不写入网格 UV，而是在空间中记录球谐系数。运行时，动态 Renderer 根据 Anchor/位置找到附近探针并插值，再用法线重建环境光。

```text
烘焙阶段：空间采样点 -> SH 系数
运行阶段：Renderer Anchor -> 探针插值 -> SH(normal) -> 环境光
```

Light Probe 是低频 irradiance，不适合表达非常尖锐的阴影、镜面反射或高频光斑。Unity 文档说明传统探针通常使用 L2 SH，总计 27 个浮点系数；光照变化大处需要更密集的探针。[Light Probe Group](https://docs.unity.cn/6000.1/Documentation/Manual/class-LightProbeGroup.html)

Light Probe 会影响：

- 动态角色和道具的环境亮度。
- 彩色间接反弹。
- lilToon toon 阴影区的环境色。
- lilToon 根据 SH 推导的风格化光照方向。

Light Probe 不会直接影响：

- ShadowCaster 是否投影。
- Reflection Probe 的 Cubemap。
- 屏幕空间折射背景。

### 1.4 Adaptive Probe Volume（APV）

APV 是 Unity 6 对探针系统的扩展。它根据几何密度自动布置探针，使用砖块和空间数据结构存储烘焙间接光，并按像素采样。相比传统 Light Probe，它更适合大体积角色、室内外过渡和需要身体不同部位获得不同环境光的场景。[APV 概览](https://docs.unity3d.com/cn/6000.0/Manual/urp/probevolumes-concept.html)

APV 的实际工程流程：

1. URP Asset 的 Light Probe System 选择 Adaptive Probe Volumes。
2. 场景加入 Global 或 Local APV。
3. 设置 Baking Set，将需要共同烘焙的场景加入同一个集合。
4. 设置最小/最大 probe spacing。
5. 使用 Probe Adjustment Volume 处理漏光、门窗和特殊区域。
6. 烘焙并用 Rendering Debugger 检查 probes、bricks 和采样结果。

APV 仍然是低频间接光，不是实时光追，也不是反射系统。它可能出现漏光、接缝和过度平滑，因此需要空间布局和调整体积配合。

### 1.5 Reflection Probe：烘焙环境 Cubemap

Reflection Probe 在某个位置拍摄六个方向的环境，生成 Cubemap，并按粗糙度使用不同 mip。Baked Probe 在编辑器中生成静态 Cubemap；Realtime Probe 运行时更新；Custom Probe 可以使用手工 Cubemap。[Reflection Probe](https://docs.unity.cn/Manual/class-ReflectionProbe.html)

Reflection Probe 的关键设置：

- `Box Size`：探针影响区域。
- `Blend Distance`：进入探针边界后的渐变距离。
- `Box Projection`：有限空间中的局部正确投影。
- `Importance`：重叠探针选择优先级。
- `Resolution/HDR`：Cubemap 质量与内存。
- Renderer 的 Reflection Probes 模式：Simple、Blend Probes、Blend Probes and Skybox。

URP 会选择最多两个影响同一对象的探针，并根据像素到探针盒边界的距离计算贡献。[URP Reflection Probes](https://docs.unity.cn/Packages/com.unity.render-pipelines.universal@17.0/manual/lighting/reflection-probes.html)

Reflection Probe 适合：

- 金属、眼睛、宝石、湿润表面。
- 室内墙面、地面和局部环境反射。
- SSR/Planar Reflection 不可见区域的 fallback。

Reflection Probe 不适合替代：

- 镜子或水面的真正平面反射。
- 需要实时反映动态对象的反射，除非使用 Realtime Probe。
- 漫反射 GI 和接触阴影。

### 1.6 GTAO：屏幕空间接触遮蔽

GTAO 不做全场景光照烘焙，而是读取屏幕空间的深度和法线，沿屏幕方向估算附近几何体对当前像素的遮蔽。

```text
GeometryBuffer normal/depth
        -> horizon/ray march
        -> visibility 0..1
        -> temporal/spatial denoise
        -> 材质或 ScreenProcess 施加
```

它的强项是接触暗部、缝隙、脚底和局部结构；弱点是屏幕外物体不可见、远距离不稳定、容易产生 halo 或 temporal 拖影。

当前 Ho-GTAO 计划采用 GeometryBuffer 作为唯一几何输入，输出公共 `_HoAOTexture`，材质强度和 mask 留在消费端。[LILTOON_GTAO_PLAN.md](D:/Unity_Fork/lilToon-URP-Extensions/Documentation~/架构优化/LILTOON_GTAO_PLAN.md)

### 1.7 GI / SSGI：动态间接光增强

Ho-GI/SSGI 读取屏幕空间深度、法线和颜色，估算当前画面可见范围内的间接光。它不能看到屏幕外物体，也不能稳定处理多次反弹，因此不应成为唯一 GI 来源。

推荐组合：

```text
Lightmap/APV/SH = 稳定、低频、全场景基础 GI
Ho-GI/SSGI      = 动态、局部、高频风格化增强
```

SSGI 必须排除描边壳、非物理表面和不应参与反弹的材质，否则会出现描边发亮等问题。已有管线评审也将 `lightmap/APV/SH` 定为静态主源、屏幕空间 GI 定为增强。[LILTOON_RENDER_PIPELINE_REVIEW_AND_PLAN.md:147](D:/Unity_Fork/lilToon-URP-Extensions/Documentation~/架构优化/LILTOON_RENDER_PIPELINE_REVIEW_AND_PLAN.md:147)

### 1.8 Planar Reflection、SSR 与折射

- **Planar Reflection**：适合水面、镜子和明确平面的高质量反射。
- **SSR**：适合屏幕内的动态局部反射；必须有 probe fallback。
- **Refraction**：通常读取 Camera Opaque Texture，再用法线偏移 UV；它不是 Reflection Probe 的替代品。

当前 lilToon 折射在 URP 中读取 `_CameraOpaqueTexture`。[lil_common_frag.hlsl:1268](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_frag.hlsl:1268)

### 1.9 资产探针怎么摆

#### 传统 Light Probe

- 探针必须形成三维体积，至少两层高度；不要只沿地面铺一层。
- 角色移动路径、房间入口、窗户、强烈阴影边界、彩色反弹处加密。
- 不要穿过墙体、地板或空间隔断放置。
- 传统探针默认按 Renderer 的 Anchor 给整体物体一个插值结果；多 Renderer 角色可以共用 Anchor Override。
- 平坦且光照变化小的室外区域可以稀疏放置。

#### APV

- 优先覆盖整套场景的可玩/可渲染空间。
- 室内、走廊、门窗、局部强光区域使用更小 spacing。
- 用 Adjustment Volume 修漏光和特殊几何区域。
- 用 Rendering Debugger 的 Debug Probe Sampling 检查实际被采样的探针。

#### Reflection Probe

- 室内通常按房间或主要环境单元布置。
- 让影响盒覆盖反射物真正活动的区域，而不是无限扩大到整张地图。
- 盒子适当重叠，使用 Blend Distance 消除切换。
- 室内优先 Box Projection；开放区域可以使用 skybox blending。
- 大型反射墙、湿地面、英雄道具附近单独评估。

### 1.10 推荐烘焙工作流

```text
1. 标记静态几何和 Contribute GI
2. 展开/检查 Lightmap UV
3. 配置 Lightmap 与 Mixed Lighting
4. 配置 Light Probe 或 APV
5. 配置 Reflection Probe 体积、分辨率和 Box Projection
6. 生成 Lighting / APV 数据
7. 单独验证 Reflection Probe Cubemap
8. 运行时验证动态角色、ShadowMap、GTAO、Ho-GI
```

Lightmap/APV 和 Reflection Probe 可以在同一编辑器工作流中触发，但它们是独立的数据产物；环境变化后需要确认两类数据都重新生成。

### 1.11 资源类型与访问 API 速查

下面的表按“资产/运行时资源是什么”来记，不把所有资源都笼统称为纹理。

| 方案 | 主要资源类型 | 编辑器/运行时 C# API | Shader/HLSL 访问 | 生命周期 |
|---|---|---|---|---|
| Lightmap | `LightmapData[]`，其中有 `lightmapColor`、可选 `lightmapDir`、`shadowMask` | `LightmapSettings.lightmaps`、`Renderer.lightmapIndex`、`Renderer.lightmapScaleOffset`、编辑器 `Lightmapping.Bake/BakeAsync` | `unity_Lightmap`、`unity_DynamicLightmap`、`unity_LightmapST`、`DecodeLightmap` | 场景烘焙资产；运行时只读 |
| Light Probe | `LightProbes`，包含位置、SH 系数和四面体化数据 | `LightmapSettings.lightProbes`、`LightProbes.GetInterpolatedProbe`、`Renderer.lightProbeUsage`、`Renderer.probeAnchor` | `unity_SHAr/Ag/Ab`、`unity_SHBr/Bg/Bb`、`unity_SHC`、`SampleSH` | 场景烘焙资产；可运行时替换/更新 |
| APV | APV Baking Set、Probe Volume bricks/cells、Probe Volume lighting data | 使用 APV 组件、Lighting/Baking Set 和 `Lightmapping` 工作流；不建议游戏代码直接操作内部 `ProbeReferenceVolume` | Unity APV `SampleAPV(positionWS, normalWS, renderingLayer, viewDirection)` | 烘焙场景数据，可按场景/集合加载 |
| Reflection Probe | Baked/Custom/Realtime Cubemap，URP 可能进入 probe atlas | `ReflectionProbe`、`ReflectionProbeUsage`、`Lightmapping.BakeReflectionProbe`、Renderer 的 `reflectionProbeUsage` | `unity_SpecCube0/1`、`unity_SpecCube0_HDR`、BoxMin/BoxMax/ProbePosition、`GlossyEnvironmentReflection`、`UnityGI_IndirectSpecular` | Baked/Custom 为资产；Realtime 为运行时更新 |
| Main Shadow | 主光 ShadowMap / atlas | `Light`、`UniversalAdditionalLightData`、URP renderer/shadow settings | `GetMainLight()`、`GetMainLight(shadowCoord)`、`TransformWorldToShadowCoord`、`SampleShadowmap` | 每帧或按光源/相机更新 |
| Additional Shadow | Additional Light shadow atlas | URP `UniversalResourceData.additionalShadowsTexture`、Additional Light 配置 | `GetAdditionalLight(index, positionWS)`，其 `shadowAttenuation` | 每帧或按光源/相机更新 |
| Camera Opaque | 相机不透明颜色拷贝 | URP `UniversalResourceData.cameraOpaqueTexture`；需要 Copy Color/输入声明 | `_CameraOpaqueTexture`、`SampleCameraColor` 或 lilToon 的 `LIL_GET_BG_TEX` | 当前帧资源 |
| Geometry Buffer | HoRP 自定义 normal/depth TextureHandle | `HoGeometryBufferPass`、`HoGeometryBufferShaderConstants` | `_HoGeometryBufferNormalDepthTexture`、`_HoGeometryBufferDepthTexture` | 当前帧资源 |
| GTAO | Ho-GTAO 输出 visibility RT | `HoGTAORendererFeature`、`HoGTAOShaderConstants.AOTextureId` | `_HoAOTexture`，语义为 0..1 visibility | 当前帧资源 |
| Ho-GI/SSGI | Ho-GI 输出的颜色/因子 RT | 未来 RendererFeature 的 `TextureHandle` 和 channel constants | 推荐注册为 `_HoGITexture` 或语义等价资源，不让材质绑定算法名 | 当前帧资源，可选 temporal history |

几个关键 Unity API 的意义：

- `ReceiveGI.Lightmaps`：Renderer 占用 Lightmap 图集空间并从 Lightmap 接收 GI。
- `ReceiveGI.LightProbes`：Renderer 不占用 Lightmap 图集空间，从 Light Probe 系统接收 GI。[ReceiveGI API](https://docs.unity3d.com/cn/6000.0/ScriptReference/ReceiveGI.html)
- `LightProbeUsage.BlendProbes`：普通四面体插值；`UseProxyVolume`：使用 3D 探针体积；`CustomProvided`：由 MaterialPropertyBlock 提供 SH。[LightProbeUsage API](https://docs.unity3d.com/cn/6000.0/ScriptReference/Rendering.LightProbeUsage.html)
- `ReflectionProbeUsage.BlendProbes`：室内探针之间混合；`BlendProbesAndSkybox`：允许与默认环境反射混合；`Simple`：不混合重叠探针。[ReflectionProbeUsage API](https://docs.unity3d.com/cn/6000.0/ScriptReference/Rendering.ReflectionProbeUsage.html)
- `LightmapSettings.lightmaps` 是场景的 `LightmapData[]`；`LightmapData` 具体包含颜色图、方向图和 ShadowMask 引用。[LightmapSettings API](https://docs.unity3d.com/cn/6000.0/ScriptReference/LightmapSettings.html) [LightmapData API](https://docs.unity3d.com/cn/6000.0/ScriptReference/LightmapData.html)

### 1.12 传统方案和 HoRP 方案的资源所有权

```text
Unity Lighting / APV / Probe assets
    -> Unity/URP 内置绑定（unity_SH、unity_Lightmap、unity_SpecCube）
    -> lilToon Forward Shader

HoRP Geometry/AO/GI/SSS/Planar resources
    -> RenderGraph TextureHandle
    -> SetGlobalTextureAfterPass / semantic channel
    -> lilToon 或 ScreenProcess 消费
```

这两条链不要混淆：Unity 的 Lightmap、SH、Reflection Probe 是场景/Renderer 级内置光照输入；HoRP 的 AO、GI、SSS、Metadata、Geometry 是当前帧的自定义屏幕资源。前者通常由 URP 在绘制材质前自动绑定，后者必须由 HoRP pass 明确声明、生产和发布。

---

## 2. lilToon 如何消费这些东西

### 2.1 总体数据流

```text
Lightmap / SH / APV / Reflection Probe / ShadowMap
                         ↓
                  lilToon Forward
                         ↓
     toon direct / toon indirect / reflection / refraction
                         ↓
        Ho-GTAO / Ho-GI / SSS / Planar / ScreenProcess
                         ↓
                       Beauty
```

HoRP 的原则应是：材质消费 `ambient`、`gi`、`reflection`、`shadow` 等语义，而不是知道当前数据是由哪一个 RendererFeature 或哪一种算法生成的。

### 2.2 组件消费矩阵

| lilToon/HoRP 组分 | Lightmap | SH/Light Probe | APV | Reflection Probe | ShadowMap/ShadowCast | GTAO | Ho-GI/SSGI |
|---|---:|---:|---:|---:|---:|---:|---:|
| 主光 toon 明暗 | 通过 lightmap color 合入 | 可参与 SH 光照方向/颜色 | 可参与 APV 光照方向/颜色 | 否 | 是 | 通常不直接决定主光 | 可作为增强 |
| toon 阴影区环境色 | 间接影响 | 直接影响 `indLightColor` | 直接影响 APV indirect | 否 | 阴影门控/衰减 | 可压暗接触区 | 可增强颜色反弹 |
| Backlight / Rim | 可作为环境底色 | 可提供环境色 | 可提供环境色 | 通常否 | 可受阴影影响 | 可作为遮蔽 | 可作为局部增强 |
| SSS diffuse source | 可提供静态基础 | 可提供低频基础 | 推荐作为动态基础 | 否 | 可调制 shadow | 可调制接触 | 可增强动态反弹 |
| Metallic / smooth reflection | 否 | 否 | 否 | 主要来源 | 否 | 通常否 | SSR/SSGI 可增强 |
| Gem / environment refraction | 否 | 否 | 否 | Fresnel 环境部分 | 否 | 否 | Camera Opaque / SSR |
| ShadowCaster | 否 | 否 | 否 | 否 | 独立 Pass | 否 | 否 |
| 屏幕空间 AO | 否 | 否 | 否 | 否 | 否 | 主要生产者 | 否 |
| 屏幕空间 GI | 否 | 作为基础源 | 作为基础源 | 反射 fallback 可用 | 需要排除非物理表面 | 可独立叠加 | 主要生产者 |

### 2.3 lilToon 中的 SH/Light Probe 路径

`lilShadeSH9()` 读取 Unity 的 SH 系数；`lilShadeSH9LPPV()` 在启用 LPPV 时按位置读取体积 SH。[lil_common_functions.hlsl:866](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_functions.hlsl:866)

`lilGetLightColorDouble()` 将光照拆成 `lightColor` 和 `indLightColor`。在 toon shading 中，后者参与 Environment Light，并受 `_ShadowEnvStrength` 控制。[lil_common_frag.hlsl:1040](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_frag.hlsl:1040)

因此 Light Probe/APV 对 lilToon 最重要的不是“把角色变亮”，而是：

- 给阴影面提供环境色。
- 给 toon 阈值提供稳定的低频光照方向。
- 让动态角色和场景的间接光关系一致。
- 为 SSS、背光、边缘光提供基础环境项。

### 2.4 lilToon 中的 Lightmap 路径

URP 主光宏根据不同混合模式将 Lightmap color 合入主光颜色、shadowmask attenuation 或 baked indirect。相关路径位于 [lil_common_macro.hlsl:2215](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_macro.hlsl:2215) 至 [lil_common_macro.hlsl:2284](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_macro.hlsl:2284)。

当前应注意：`DIRLIGHTMAP_COMBINED` 变体虽然可能被生成，但文件中随后明确 `#undef LIL_USE_DIRLIGHTMAP`。在把方向性 Lightmap 当作 HO 高质量光照依据前，需要做实机验证或明确修正策略。

### 2.5 lilToon 中的 Reflection Probe 路径

标准 lilToon 的 `_ApplyReflection` 路径会计算反射向量、粗糙度和 Fresnel，然后调用 `LIL_GET_ENVIRONMENT_REFLECTION()`。URP 下该宏读取 Unity 的 Reflection Probe 数据，若没有可用 Probe，则退回材质的 `_ReflectionCubeTex`。[lil_common_macro.hlsl:961](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_macro.hlsl:961)

这使 Reflection Probe 成为：

- 金属和光滑材质的主要环境反射源。
- Planar/SSR 失败时的稳定 fallback。
- 宝石、眼睛和折射材质环境边缘的基础反射源。

材质的自定义 Cubemap override 仍然有价值：它适合风格化、角色专属反射或探针不可覆盖的特殊资产。

### 2.6 lilToon 中的折射路径

折射先对 `_CameraOpaqueTexture` 做屏幕 UV 偏移，再在通用 Reflection 阶段使用环境反射参与 Fresnel 混合。因此折射需要两个独立前置条件：

1. URP/HoRP 必须提供可采样的 camera opaque/transparent 输入。
2. 材质需要可用的 Reflection Probe 或自定义 Cubemap 作为环境 fallback。

不能把 Reflection Probe 当成“折射背景”；它只表示环境 Cubemap，不包含当前屏幕后方的实时对象。

### 2.7 lilToon 与 ShadowCaster

ShadowCaster Pass 走独立的顶点和 Alpha Clip 路径，最终输出 ShadowMap 深度。它不调用 `lilShadeSH9`、Reflection Probe 或 APV。

因此：

- 光照探针不会自动产生投影阴影。
- Reflection Probe 不会改变投影阴影。
- GTAO 不会改变 ShadowCaster 几何。
- 描边壳是否参与阴影，需要通过 ShadowCaster 变体、材质属性或 Ho-ShadowCast 过滤控制。

### 2.8 与 Ho-GTAO / Ho-GI 的接入原则

推荐把探针和烘焙光照当作静态基础，把屏幕空间效果当作增强：

```text
ambient_base = Lightmap / SH / APV
gi_dynamic    = Ho-GI / SSGI
ao_visibility = Ho-GTAO
reflection    = Probe + Planar + SSR fallback
```

不要让 lilToon Shader 根据 `_HTraceBufferAO`、某个 SSGI 实现名或某个具体 RT 名称决定行为。材质应该消费语义纹理或已经解析好的光照结果。现有 HoRP 通道契约已经把 `ao`、`gi`、`reflection`、`normal`、`depth` 分开登记，这个方向应继续保持。[LILTOON_CHANNEL_CONTRACT_V1.md](D:/Unity_Fork/lilToon-URP-Extensions/Documentation~/架构优化/LILTOON_CHANNEL_CONTRACT_V1.md)

### 2.9 每个 lilToon 组分的资源与 API 对照

#### A. 主光、Additional Light 与阴影

**资源：**

- 光源数据：主光方向/颜色、Additional Light 列表、距离衰减、渲染层。
- 阴影数据：主光 ShadowMap、Additional Light shadow atlas。
- 材质数据：toon ramp、阴影颜色、border/blur、receive mask。

**URP Shader API：**

```hlsl
Light mainLight = GetMainLight();
Light mainLightWithShadow = GetMainLight(shadowCoord);
Light additional = GetAdditionalLight(lightIndex, positionWS);
float3 shadowCoord = TransformWorldToShadowCoord(positionWS);
```

实际项目中优先通过 lilToon 已有的 `LIL_MAINLIGHT_*`、`lilGetLightDirectionAndColor()`、`lilGetAdditionalLights()` 宏层访问，不要在每个 Ho 组件里重新拼 URP 光照结构。

**HoRP 接入：**

- 主光 ShadowMap 由 URP 生产，不需要 Ho-ShadowCast 重复生产。
- Ho-ShadowCast 生产额外灯 atlas，通过 `_HoShadowCastAtlas` 和 `_HoShadowCastSecondDirectionalAtlas` 发布。
- 未来如果要让材质读取额外灯阴影，应提供“阴影因子语义”，而不是让材质知道 atlas 内部布局。

#### B. Lightmap

**资源：**

- `LightmapData.lightmapColor`：入射光颜色/强度。
- `LightmapData.lightmapDir`：方向性 Lightmap，可选。
- `LightmapData.shadowMask`：Mixed Shadowmask，可选。
- Renderer 的 `lightmapIndex` 和 `lightmapScaleOffset`：选择图集和 UV 变换。

**Shader API：**

```hlsl
float2 lmUV = uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
float4 encoded = SAMPLE_TEXTURE2D(unity_Lightmap, samplerunity_Lightmap, lmUV);
float3 irradiance = DecodeLightmap(encoded);
```

lilToon 已将这套逻辑封装进 `lilGetLightMapColor(uv1, uv2)`。实际主光合并由 `LIL_GET_MAINLIGHT` 根据 `LIGHTMAP_SHADOW_MIXING`、`SHADOWS_SHADOWMASK` 等关键词选择。

**Renderer API：**

```csharp
renderer.receiveGI = ReceiveGI.Lightmaps;
renderer.lightmapIndex;
renderer.lightmapScaleOffset;
LightmapSettings.lightmaps;
```

HO 建议：静态场景优先用 Lightmap，动态角色不要强行分配 Lightmap UV；静态 NPR 材质仍应在 lilToon 的 `_LightMinLimit/_LightMaxLimit/_MonochromeLighting` 和 toon shadow 参数中重新压缩烘焙光照范围。

#### C. Light Probe / SH

**资源：**

- `LightProbes.positions`：烘焙采样点位置。
- `LightProbes.bakedProbes`：每个探针的 `SphericalHarmonicsL2`。
- tetrahedral tessellation：运行时查找/插值所在空间单元。

**C# 查询 API：**

```csharp
LightProbes.GetInterpolatedProbe(
    worldPosition,
    renderer,
    out SphericalHarmonicsL2 probe);

LightmapSettings.lightProbes;
renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
renderer.probeAnchor = anchorTransform;
```

`GetInterpolatedProbe` 是调试/工具 API；普通 Renderer 渲染时 Unity 会自动把 SH 系数绑定给 Shader。lilToon 直接读取 `unity_SH*`，再由 `lilShadeSH9()` 重建。

**Shader API：**

```hlsl
float3 irradiance = SampleSH(normalWS);
// lilToon 等价路径：lilShadeSH9(normalWS)
```

**HO 适用范围：**

- 角色的基础环境色、阴影面颜色、背光基础色。
- 不应承担金属反射或硬阴影。
- 传统 Light Probe 是每 Renderer/Anchor 级近似；需要身体不同部位有连续变化时，优先 APV 或 LPPV，而不是无限增加单点探针。

#### D. APV

**资源：**

- APV Baking Set。
- Probe Volume 的 bricks/cells。
- 每个 probe 的 L0/L1/L2 光照数据和压缩资源。

**Shader API：**

```hlsl
#include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolume.hlsl"
APVSample sample = SampleAPV(positionWS, normalWS, renderingLayer, viewDirection);
sample.Decode();
```

lilToon 已实现 `lilGetToonSHDoubleAPV()`、`lilGetFixedLightDirectionAPV()` 和 `lilGetLightColorDoubleAPV()`，入口位于 [lil_common_functions.hlsl:1042](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_functions.hlsl:1042)。

**编译前提：**

```text
PROBE_VOLUMES_L1/L2 变体存在
ProbeVolumeVariants.hlsl 被 include
URP Asset/Renderer 开启 APV
场景属于有效 Baking Set 且已烘焙
```

当前 lilToon 模板通过 `lil_skip_variants_probevolumes` 阻止了第二项，因此源码函数存在不等于材质实际能采样 APV。

#### E. Reflection Probe

**资源：**

- Cubemap / HDR decode 参数。
- 0/1 两个候选 probe。
- BoxMin/BoxMax/ProbePosition。
- Blend、Box Projection、Atlas、Rotation 关键词。

**Shader API：**

```hlsl
float3 reflection = GlossyEnvironmentReflection(
    reflect(-viewDirection, normalWS),
    perceptualRoughness,
    1.0);
```

lilToon 标准路径使用 `UnityGI_IndirectSpecular()` 或 URP 的 `GlossyEnvironmentReflection()`，并以 `_ReflectionCubeTex` 作为自定义 Cubemap fallback。

**Renderer/C# API：**

```csharp
renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
ReflectionProbe probe = ...;
probe.boxProjection = true;
probe.size = boxSize;
probe.blendDistance = blendDistance;
Lightmapping.BakeReflectionProbe(probe, path);
```

**HO 适用范围：**

- 反射是材质的 view-dependent 组件，不应并入 Light Probe 的 diffuse/ambient 语义。
- SSR/Planar 失败时使用 Reflection Probe fallback。
- Lite 模板的 probe blending/box projection variants 被裁掉，反射关键资产使用标准 Default 系列。

#### F. Camera Opaque 与折射

**资源：**

- 当前相机的 opaque color copy。
- 对透明材质来说，它通常不包含当前透明物体后绘制的内容。

**URP RenderGraph API：**

```csharp
UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
TextureHandle opaque = resourceData.cameraOpaqueTexture;
```

需要在 URP 设置中启用 Copy Color，或者由自定义 pass 明确生产一张 camera color copy。URP17 的 `UniversalResourceData` 还提供 `cameraColor`、`activeColorTexture`、`cameraDepthTexture`、`motionVectorColor`、`ssaoTexture` 等资源句柄。[UniversalResourceData API](https://docs.unity.cn/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalResourceData.html)

**Shader API：**

```hlsl
float3 background = SampleCameraColor(screenUV, lod);
// lilToon URP 抽象：LIL_GET_BG_TEX(refractUV, lod)
```

折射接入必须写入 HO 资源契约：生产时机、采样 UV、透明排序、是否允许读取透明结果、是否与 OIT 交互。

#### G. GeometryBuffer、GTAO 与 Ho-GI

**HoRP 资源：**

```text
_HoGeometryBufferNormalDepthTexture
_HoGeometryBufferDepthTexture
_HoAOTexture
未来的 _HoGITexture / gi channel
```

**RenderGraph API：**

```csharp
TextureHandle normalDepth = renderGraph.CreateTexture(desc);
builder.UseTexture(normalDepth, AccessFlags.Read);
builder.SetRenderAttachment(destination, 0);
builder.SetGlobalTextureAfterPass(destination, shaderPropertyId);
```

HoRP 的全局发布标准是：

```csharp
public const string AOTextureName = "_HoAOTexture";
public static readonly int AOTextureId = Shader.PropertyToID(AOTextureName);
```

对应实现见 [HoGTAOShaderConstants.cs](D:/Unity_Fork/lilToon-URP-Extensions/Runtime/GTAO/HoGTAOShaderConstants.cs) 和 [HoGTAORendererFeature.cs:614](D:/Unity_Fork/lilToon-URP-Extensions/Runtime/GTAO/HoGTAORendererFeature.cs:614)。

**输入声明 API：**

对于 URP 内置资源，RendererFeature 应在合适的位置使用 `ConfigureInput(ScriptableRenderPassInput.Depth | Normal | Color | Motion)`；RenderGraph 路径则从 `UniversalResourceData` 获取 `TextureHandle` 并显式声明读写依赖。[ScriptableRenderPass.ConfigureInput](https://docs.unity.cn/Packages/com.unity.render-pipelines.universal@13.1/api/UnityEngine.Rendering.Universal.ScriptableRenderPass.html)

**语义边界：**

- GeometryBuffer 是几何生产者，不是 GI 算法。
- GTAO 生产 `visibility`，不要把材质强度烘进 AO RT。
- Ho-GI 生产动态间接光，不能伪装成 Lightmap/APV。
- ScreenProcess 可以统一施加语义，但必须知道它消费的是因子、颜色还是意图。

#### H. ShadowCast atlas

**资源：**

```text
_HoShadowCastAtlas
_HoShadowCastSecondDirectionalAtlas
```

Ho-ShadowCast 通过 RenderGraph 创建 atlas，再调用 `SetGlobalTextureAfterPass` 发布。它的 Shader 采样函数位于 `Runtime/ShadowCast/Shaders/HoShadowCastSampling.hlsl`。

**重要限制：**

- Atlas 是 HoRP 自己的额外灯阴影资源，不是 URP `mainShadowsTexture`。
- 材质消费时应通过统一 shadow semantic/采样函数，不要复制 atlas tile layout。
- 阴影因子、toon 阴影颜色和最终 beauty 应保留独立 debug/AOV 语义。

---

## 3. lilToon 实际编译情况工作表

### 3.1 证据规则

本文区分三种状态：

- **源码支持**：HLSL 中存在相关函数或宏。
- **模板声明**：`.lilblock` 中要求生成相关变体。
- **实际生成**：`lilShaderContainerImporter.cs` 最终是否加入对应 URP include/pragma。

不能只看到 HLSL 函数就认为功能已经对当前材质生效。

### 3.2 当前工作表

| 功能 | 源码 | URP 模板/Importer | 当前结论 | 后续动作 |
|---|---|---|---|---|
| 静态 Lightmap | `lilGetLightMapColor`、`LIGHTMAP_ON` | 标准 Default 未跳过 lightmap；Importer 生成 lightmap variants | **实际支持** | 用静态场景验证色调、曝光和混合模式 |
| Dynamic Lightmap | 有 `DYNAMICLIGHTMAP_ON` 路径 | 标准 Default 生成 variant | **部分支持** | 按当前 URP 版本验证是否适合 HO 使用 |
| Directional Lightmap | 有 `lilGetLightMapDirection` 函数 | 后续 `#undef LIL_USE_DIRLIGHTMAP` | **源码存在，最终路径可疑/默认关闭** | 明确是否要恢复方向性烘焙 |
| 传统 Light Probe / SH | `lilShadeSH9`、`unity_SH*` | 标准 forward 使用 SH | **实际支持** | 绑定动态角色并检查 `indLightColor` |
| LPPV | `lilShadeSH9LPPV`、`LIL_USE_LPPV` | `LIL_LPPV_MODE` 默认 0 | **源码支持，默认不启用** | 只作为旧方案保留，不作为 Unity 6 主方案 |
| APV L1/L2 | `SampleAPV`、`PROBE_VOLUMES_L1/L2` | `LIL_OPTIMIZE_USE_PROBEVOLUMES=true` 时，Importer 绕过模板 skip 并加入 `ProbeVolumeVariants.hlsl` | **已支持，可由全局 Shader Setting 控制；朱木古堂当前已打开** | 仍需完成 APV 组件、Baking Set、烘焙和画面验收 |
| 单 Reflection Probe | `UnityGI_IndirectSpecular` / `GlossyEnvironmentReflection` | 标准 Default 生成 Reflection Probe 相关路径 | **实际支持** | 检查 Renderer Reflection Probes 设置 |
| Reflection Probe blending | `UNITY_SPECCUBE_BLENDING` 路径 | Default 保留；Lite 显式 skip reflections | **Default 支持，Lite 不支持完整混合** | 反射关键材质不要使用 Lite，或恢复变体 |
| Reflection Probe box projection | `UNITY_SPECCUBE_BOX_PROJECTION` 路径 | Default 保留；Lite 显式 skip reflections | **Default 支持，Lite 关闭** | 室内反射优先使用 Default + Box Projection |
| Reflection Probe atlas | URP GI/Reflection 路径 | URP17 Importer 加入 `_REFLECTION_PROBE_ATLAS` | **生成层支持** | 用 Unity 版本实机确认 atlas 资源绑定 |
| ShadowCaster | `lil_pass_shadowcaster.hlsl` | 所有主要 URP 模板包含 ShadowCaster Pass | **实际支持** | 逐材质检查 Alpha Clip、Cull、Bias |
| Main Light Shadow | toon shadow / shadowmix | DefaultDirect 等模板按需保留主光 shadow variants | **实际支持，但由模板选择决定** | Standard 与 Direct 变体分别验证 |
| Additional Light Shadow | Additional Light keywords | 部分模板显式 skip `_ADDITIONAL_LIGHT_SHADOWS` | **按模板/材质变体变化** | 不要假设所有 lilToon 材质都支持附加灯阴影 |
| URP 内置 SSAO | 原始 shader 仍可见兼容逻辑 | 模板有 `lil_skip_variants_ao`；当前 Ho-GTAO 计划移除旧消费路径 | **不应作为 HO 主路径** | 统一使用 `_HoAOTexture` 语义 |
| Ho-GTAO | 材质消费 `_HoAOTexture` 的计划与 Runtime Feature | Ho-GTAO 生产端正在替换 HTrace | **规划/实现中** | 完成时序、材质采样和 debug 验收 |
| Ho-GI / SSGI | 管线规划与通道契约 | 不属于 lilToon 原生 Shader 变体 | **HoRP 管线增强** | 以 `gi` 语义接入，排除描边/非物理表面 |
| Planar Reflection | HoPlanarReflection + lilToon reflection intent | 独立 RendererFeature / ScreenProcess | **HoRP 扩展支持** | 与 Reflection Probe/SSR 做 fallback 链 |
| Refraction | `_CameraOpaqueTexture` + Reflection Probe env | DefaultRefraction 保留环境反射路径 | **实际支持，但依赖 camera opaque** | 明确透明输入契约和排序 |

### 3.3 APV 的关键编译事实

当前源码链路如下：

```text
Default*.lilblock
    -> #pragma lil_skip_variants_probevolumes
    -> MultiCompileOptions.skipProbeVolumes = true
    -> 不加入 ProbeVolumeVariants.hlsl
    -> 不生成 PROBE_VOLUMES_L1/L2 变体
```

相关实现见：

- [Default.lilblock:11](D:/Unity_Fork/lilToon/Assets/lilToon/CustomShaderResources/URP/Default.lilblock:11)
- [lilShaderContainerImporter.cs:929](D:/Unity_Fork/lilToon/Assets/lilToon/Editor/lilShaderContainerImporter.cs:929)
- [lilShaderContainerImporter.cs:1085](D:/Unity_Fork/lilToon/Assets/lilToon/Editor/lilShaderContainerImporter.cs:1085)
- [lil_common_functions.hlsl:1042](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_functions.hlsl:1042)

因此 APV 接入不能只修改 HLSL。必须：

1. 修改模板或 importer 的 skip 策略。
2. 重新生成所有 lilToon Shader。
3. 在生成结果中确认 `ProbeVolumeVariants.hlsl` 和 `PROBE_VOLUMES_L1/L2` 存在。
4. 在 APV 场景中确认动态角色的 SH/indirect 真的发生空间变化。

### 3.4 Reflection Probe 的模板差异

`Default`、`DefaultDirect`、`DefaultRefraction` 等标准模板没有 `lil_skip_variants_reflections`，会生成 blending 和 box projection 变体。

`DefaultLite`、`DefaultLiteDirect` 及相关 Lite 模板包含该标记，因此它们会省略：

```text
_REFLECTION_PROBE_BLENDING
_REFLECTION_PROBE_BOX_PROJECTION
```

这不是“Lite 完全没有反射”，而是“Lite 不保留完整的探针混合/盒投影质量”。反射关键资产应优先使用标准 Default 系列。

### 3.5 朱木古堂测试环境的实证基线

测试工程：

```text
D:\Unity_Project\BREAK_URP
场景：Assets\mmd场景测试\朱木古堂\New Scene.unity
URP：D:\Unity_Fork\HoUrp17.3.0
lilToon：D:\Unity_Fork\lilToon\Assets\lilToon
```

#### 当前 Shader 输出

场景材质的 Shader GUID 已和 lilToon 源文件对应：

| 测试工程 Shader GUID | 源文件 | 用途 |
|---|---|---|
| `df12117ecd77c31469c224178886498e` | `Shader/lts.shader` | 标准 Opaque，Shader 名 `lilToon`，`LIL_RENDER=0` |
| `85d6126cae43b6847aff4b13f4adb8ec` | `Shader/lts_cutout.shader` | 标准 Cutout，Shader 名 `Hidden/lilToonCutout`，`LIL_RENDER=1` |
| `e66172d3b40f88d449a821efed862d2d` | `lilPBR/Shaders/lilPBR.shader` | lilPBR，不属于 lilToon 输出矩阵 |

朱木古堂当前实际使用的是标准 `lts.shader` 和 `lts_cutout.shader`，没有使用 `ltsl` Lite、`ltsmulti` Multi、`lts_fur`、`lts_gem` 或 `lts_ref` Refraction Shader。因此，标准输出的编译能力是当前场景最直接的证据；其他 Shader 家族仍然需要独立验证。

#### 当前场景/URP 资源状态

从 `New Scene.unity`、`PC_RPAsset.asset` 和 `PC_Renderer.asset` 盘点到的状态：

| 项目 | 当前状态 | 对 Shader 支持判断的影响 |
|---|---|---|
| 光照数据资产 | `m_LightingDataAsset` 为默认空引用 | 当前场景没有已绑定的烘焙 LightingDataAsset |
| Lightmap Shader 变体 | lilToon 输出明确 skip | 即使 Renderer/材质存在 Lightmapping 标志，也不会进入 Lightmap 采样路径 |
| Light Probe System | `m_LightProbeSystem: 1` | URP17 枚举中 `1 = Adaptive Probe Volumes`；场景仍需有效 APV 组件/烘焙数据 |
| APV 组件/数据 | 场景未发现 Adaptive Probe Volume/Probe Volume 组件 | 当前画面不构成 APV 实证 |
| Reflection Probe | `New Scene.unity` 未发现 ReflectionProbe 组件 | 当前画面不构成局部 Reflection Probe 实证，只能验证 Shader 变体存在 |
| URP depth | `PC_RPAsset.m_RequireDepthTexture: 1` | `_CameraDepthTexture`/Ho Geometry 相关消费有基础条件 |
| URP opaque | `PC_RPAsset.m_RequireOpaqueTexture: 1` | lilToon Refraction/Camera Opaque 路径有基础条件 |
| Ho Geometry/Metadata | Renderer 中均启用 | 标准 lilToon 输出带 `HoGeometryBuffer`、`HoMetadataBuffer` Pass |
| Ho-GTAO | Renderer 中配置，当前 `m_Active: 1` | 当前基线已启用 Ho-GTAO；材质 AO 路径与 GeometryBuffer/GTAO 时序需要验收 |
| HTrace AO | Renderer 中配置，但 `m_Active: 0` | 当前基线未启用 HTrace AO |
| HTrace SSGI | Renderer 中配置，当前 `m_Active: 1` | 当前基线已启用 HTrace SSGI；它属于屏幕空间增强，不是 lilToon 原生 Shader 变体 |
| Ho-ShadowCast | 一档 `m_Active: 1`，另一档 `m_Active: 0` | 启用档生产额外阴影 atlas，不等于主光 ShadowMap |

`PC_RPAsset.asset` 的相关设置见 [PC_RPAsset.asset](D:/Unity_Project/BREAK_URP/Assets/Settings/PC_RPAsset.asset)，测试 Shader 设置见 [lilToonSetting.json](D:/Unity_Project/BREAK_URP/ProjectSettings/lilToonSetting.json)。

#### 当前生成 Shader 的关键事实

标准 `lts.shader` / `lts_cutout.shader` 的 HLSLINCLUDE 中定义了完整功能集合，包括：

```text
LIL_FEATURE_SHADOW / RECEIVE_SHADOW / SHADOW_3RD
LIL_FEATURE_REFLECTION / MATCAP / RIMLIGHT / GLITTER / BACKLIGHT
LIL_FEATURE_SSAO / SSS / PARALLAX / POM / DISTANCE_FADE / DISSOLVE
LIL_FEATURE_NORMAL_1ST / NORMAL_2ND / ANISOTROPY
LIL_FEATURE_EMISSION_1ST / EMISSION_2ND
LIL_FEATURE_*Map / *Mask / *ColorTex
```

这说明当前生成器不是按每个材质只生成一份窄 Shader，而是按 Shader 家族将大量功能宏固定编入输出；材质属性和统一 Shader 代码再决定具体分支的实际行为。

但这些宏存在不等于所有 URP 变体都存在。朱木古堂当前生成结果明确包含：

```text
#pragma skip_variants LIGHTMAP_ON DYNAMICLIGHTMAP_ON
#pragma skip_variants LIGHTMAP_SHADOW_MIXING SHADOWS_SHADOWMASK
#pragma skip_variants DIRLIGHTMAP_COMBINED _MIXED_LIGHTING_SUBTRACTIVE
```

所以当前工程的真实结论是：

> 标准 lilToon 的 toon/反射/ShadowCaster/HoRP Pass 功能宏很完整；当前项目仍主动裁掉 Lightmap 变体，但已通过 `LIL_OPTIMIZE_USE_PROBEVOLUMES=true` 生成 APV 变体。

### 3.10 为什么 lilToon 默认关闭 Lightmap

这不是一个单纯的“运行时性能开关”，而是 lilToon 的产品目标和编译规模选择。

#### 产品目标：lilToon 首先面向 Avatar

lilToon 官方 Shader Settings 文档明确把 Lightmap 描述为“用于所有材质的全局 Shader 设置”，并说明 Avatar 用途通常不需要 Lightmap，Lightmap 更偏向 World/场景用途。[lilToon Shader Settings](https://lilxyzw.github.io/lilToon/ja_JP/other/settings.html)

Avatar 通常是：

- 动态移动、没有稳定的 Lightmap UV。
- 由 World 的 Light Probe/APV 接收环境间接光。
- 需要实时主光和实时 ShadowMap。
- 由上传容量、Shader 编译时间和变体数量约束。

所以默认关闭 Lightmap 对 Avatar 是合理默认值；这不代表 Lightmap 在场景材质中没有价值。

#### 编译成本：确实会增加变体与导入/构建时间

`LIL_OPTIMIZE_USE_LIGHTMAP` 为 `false` 时，Importer 生成：

```text
#pragma skip_variants LIGHTMAP_ON
#pragma skip_variants DYNAMICLIGHTMAP_ON
#pragma skip_variants LIGHTMAP_SHADOW_MIXING
#pragma skip_variants SHADOWS_SHADOWMASK
#pragma skip_variants DIRLIGHTMAP_COMBINED
#pragma skip_variants _MIXED_LIGHTING_SUBTRACTIVE
```

为 `true` 时，URP17 Forward 生成器会加入 Lightmap 相关的多组 `multi_compile`，包括 `LIGHTMAP_ON`、`DYNAMICLIGHTMAP_ON`、`SHADOWS_SHADOWMASK`、`DIRLIGHTMAP_COMBINED`、`LIGHTMAP_BICUBIC_SAMPLING`、`REFLECTION_PROBE_ROTATION` 和 `USE_LEGACY_LIGHTMAPS`。仅按二元组合计算，理论上最多会给相关 Pass 增加约 `2^8 = 256` 倍的组合空间；Unity 的材质/场景 Shader stripping 会删掉大量不需要的组合，实际数量会小很多，但导入和构建仍会变慢。

因此要区分三种成本：

| 成本 | Lightmap 开关的影响 |
|---|---|
| Shader 导入/编译时间 | 增加，尤其是全功能 Standard/Multi 输出 |
| Player Shader 数据/变体数量 | 可能增加，取决于 URP stripping 和实际材质 |
| 每像素运行时 | 使用 Lightmap 的变体会多一次 Lightmap 采样和解码；不使用的材质不会承担这条路径 |
| Lightmap 纹理内存/烘焙时间 | 由场景静态 Renderer、Lightmap 分辨率和图集决定，不是 lilToon Shader Setting 单独决定 |

对我们的 HO 渲染环境，编译/运行性能不是首要约束，因此可以把 Lightmap 变体打开；但应接受 Shader 导入时间和变体缓存变大的代价，并用实际 ShaderVariant 日志确认成本，而不是凭感觉判断。

#### APV 的成本与 Lightmap 不同

APV 不会通过 Lightmap UV 变体实现。它需要 `PROBE_VOLUMES_L1/L2` 变体，运行时按像素采样 APV 数据。打开 APV 变体主要增加：

- Forward Shader 的 APV 关键词组合。
- APV 数据资源和运行时采样成本。
- 烘焙/加载 APV 的时间和内存。

APV 与 Lightmap 可以并存：静态几何用 Lightmap，动态角色用 APV。它们不是互相排斥的全局模式。

### 3.11 lilToon 的设置入口、持久化和交互方式

#### 用户入口

当前 lilToon 的入口是任意 lilToon 材质的 Inspector：

```text
选中任意 lilToon Material
    -> Inspector 顶部模式栏
    -> Advanced | Preset | Shader Setting
    -> 选择 Shader Setting
```

对应代码：

- `lilInspector.cs` 选择 `EditorMode.Settings`。
- `lilGUIUtility.cs` 绘制三段模式 Toolbar。
- `lilSettingAndPresetGUI.cs` 的 `DrawSettingsGUI()` 绘制全局设置。

这不是“该材质自己的编译开关”。即使从某一个材质 Inspector 打开，它编辑的也是整个工程的 lilToon Shader Setting。

#### 持久化位置

```text
ProjectSettings/lilToonSetting.json
```

路径由 `lilDirectoryManager.GetShaderSettingPath()` 返回。朱木古堂当前文件是：[lilToonSetting.json](D:/Unity_Project/BREAK_URP/ProjectSettings/lilToonSetting.json)

#### 设置分层

| 分层 | 代表字段 | 作用 | 是否写入单个材质 |
|---|---|---|---:|
| 全局功能编译开关 | `LIL_FEATURE_*` | 把某类 HLSL 功能宏编入/移出生成 Shader | 否 |
| 全局构建/变体开关 | `LIL_OPTIMIZE_*` | ForwardAdd、VertexLight、Lightmap 等变体与路径选择 | 否 |
| 全局优化行为 | `isDebugOptimize`、`isOptimizeInNDMF`、`isOptimizeInTestBuild` | 编辑器/NDMF/构建阶段是否进行优化；其中部分字段在当前 fork 中没有完整本地调用链 | 否 |
| 全局默认值 | `defaultLightMinLimit`、`defaultLightMaxLimit` 等 | `[GameObject] Fix lighting` 和 Lighting Preset 的初始值 | 否，之后复制到材质属性 |
| 材质运行时属性 | `_UseShadow`、`_ShadowBorder`、`_UseScreenSpaceAO`、`_ReflectionColor` 等 | 每个材质的艺术控制和运行时行为 | 是 |
| Renderer/场景设置 | `ReceiveGI`、`LightProbeUsage`、`ReflectionProbeUsage`、APV/Lightmap | 对象如何接收 Unity 光照资源 | 否，属于 Renderer/场景 |

#### 修改后发生什么

1. GUI 修改 `lilToonSetting` 对象。
2. `ApplyShaderSetting()` 先把设置写入 `ProjectSettings/lilToonSetting.json`。
3. 它从 `BaseShaderResources` 找到 `.lilinternal`，调用 `lilShaderContainer.UnpackContainer()`。
4. 重新写出 `Shader/*.shader`，重新导入 `.lilcontainer` 目录并刷新 AssetDatabase。
5. 生成文本中的 `#define LIL_FEATURE_*`、`#pragma skip_variants` 和 Pass 由此改变。

“Assets/lilToon/[Shader] Refresh shaders” 菜单会重新生成全部 Shader。这个菜单不是只刷新 Inspector，而是真正重写生成文件。[lilToonEditorUtils.cs](D:/Unity_Fork/lilToon/Assets/lilToon/Editor/lilToonEditorUtils.cs)

#### 自动优化模式

当 `isDebugOptimize` 或构建预处理触发优化时，lilToon 会：

```text
收集场景/项目中的 Material 和 AnimationClip
    -> TurnOffAllShaderSetting()
    -> SetupShaderSettingFromMaterial()
    -> 检查实际材质属性、贴图、动画绑定
    -> 只重新打开真正使用的 LIL_FEATURE_*
    -> ApplyShaderSetting()
```

`SetupShaderSettingFromMaterial()` 会跳过 Lite 和 Multi Shader，说明这两类本来就不是按普通材质功能扫描的 Standard 优化路径。构建前还会记录需要临时优化的 Shader，构建后恢复设置。

### 3.12 HO 开关应该放在哪里

建议沿用 lilToon 的“全局编译设置 + 材质运行时属性”分层，但不要把 HoRP 的 RendererFeature 生命周期塞进材质编译开关。

#### 适合放在 lilToon 全局 Shader Setting 的内容

- 是否生成 APV `PROBE_VOLUMES_L1/L2` 变体。
- 是否保留 Lightmap variants。
- 是否保留 Reflection Probe blending/box projection variants。
- 是否保留某个 HoRP 需要的轻量 Shader 输入宏。

这些开关的共同特点是：**会改变生成 Shader 的结构或变体数量，应该工程统一，而不是每个材质各自决定。**

建议未来增加：

```text
LIL_OPTIMIZE_USE_PROBEVOLUMES
LIL_OPTIMIZE_USE_LIGHTMAP
LIL_OPTIMIZE_USE_REFLECTION_PROBE_QUALITY
```

其中 `LIL_OPTIMIZE_USE_PROBEVOLUMES` 应该是“可条件启用”，默认值可保持关闭；当 URP Asset 使用 APV 时由项目设置打开。

#### 适合放在材质界面的内容

- `_UseScreenSpaceAO`、`_SSAOStrength`、`_SSAODirectStrength`、`_SSAOIndirectStrength`。
- GI/Reflection/SSS 的强度、mask、颜色和 apply mode。
- 是否让该材质写入/排除某个 HoRP 语义通道。
- 反射 roughness、Fresnel、Cubemap override。

这些是艺术意图或消费强度，不应改变整个 Shader 的编译结构。

#### 不建议放在材质界面的内容

- “本材质是否编译 APV”——会造成同一 Shader 家族的编译策略不一致，且当前 lilToon 生成器并非按材质生成独立 APV Shader。
- “本材质是否打开 Lightmap variants”——Lightmap 是 Renderer/场景资源，材质只应决定是否消费，不应拥有全局变体开关。
- “本材质是否生成 Ho-GTAO/Ho-GI”——生产端是 RendererFeature，不是材质属性。

### 3.13 Lite 材质在当前工程中的实际使用

扫描整个 `D:\Unity_Project\BREAK_URP\Assets` 中的 Material，并按 lilToon Shader GUID 与 `Shader/*.shader.meta` 对照：

```text
已识别 lilToon Material：238
Lite（ltsl*.shader）：0
Multi（ltsmulti*.shader）：0
UsePass（ltspass*.shader 直接作为材质 Shader）：0
Standard（lts*.shader）：238
```

朱木古堂目录中共有 55 个 `.mat`，其中 53 个是 lilToon Standard/Cutout，2 个是 lilPBR；Lite/Multi 均为 0。

这意味着 Lite 当前不是 HO 场景的真实依赖。结合我们“性能不敏感、质量优先”的目标，建议：

1. **HO 主线默认使用 Standard 家族。**
2. 不为了理论上的远景性能保留 Lite 作为主工作流要求。
3. Lite 继续作为上游兼容资产和低成本 fallback，但不参与 HO 质量验收基线。
4. 只有出现明确的大规模远景、透明层或 Shader 编译瓶颈时，才重新评估 Lite。
5. 如果未来需要 Lite，也应把它定义为明确的“质量降级 preset”，而不是和 Standard 混用后再猜测功能差异。

### 3.6 52 个输出 Shader 家族的用途与宏环境

当前 `D:\Unity_Fork\lilToon\Assets\lilToon\Shader` 有 52 个生成 Shader，按用途分为四个家族：

| 家族 | 数量 | 文件前缀 | 主要用途 | 关键宏/限制 |
|---|---:|---|---|---|
| Standard | 25 | `lts*.shader` | 普通 Opaque/Cutout/Transparent、Outline、Fur、Gem、Refraction、FakeShadow、Overlay | 完整 `LIL_FEATURE_*`；标准场景使用 `lts.shader` / `lts_cutout.shader` |
| Lite | 12 | `ltsl*.shader` | 低成本 Opaque/Transparent/Outline/Overlay | `LIL_LITE`；跳过主光/附加光阴影和 Reflection blending/box projection；HLSL 许多功能有 `!LIL_LITE` 门控 |
| Multi | 5 | `ltsmulti*.shader` | 多材质输入/多段材质组合、Multi Fur/Gem/Refraction | `LIL_MULTI` + `LIL_MULTI_INPUTS_*`；当前输出跳过附加灯阴影 |
| UsePass | 10 | `ltspass*.shader` | 给外部/旧材质通过 UsePass 复用 lilToon Pass | 可能重复包含 SubShader；Lite/普通分别有独立文件；应按 Pass 级别验证 |

完整输出文件清单如下，后续新增或删除 Shader 文件时应同步更新这里：

```text
Standard (25)
lts.shader
lts_cutout.shader
lts_cutout_o.shader
lts_cutout_oo.shader
lts_fakeshadow.shader
lts_fur.shader
lts_fur_cutout.shader
lts_fur_two.shader
lts_furonly.shader
lts_furonly_cutout.shader
lts_furonly_two.shader
lts_gem.shader
lts_o.shader
lts_oo.shader
lts_onetrans.shader
lts_onetrans_o.shader
lts_overlay.shader
lts_overlay_one.shader
lts_ref.shader
lts_ref_blur.shader
lts_trans.shader
lts_trans_o.shader
lts_trans_oo.shader
lts_twotrans.shader
lts_twotrans_o.shader

Lite (12)
ltsl.shader
ltsl_cutout.shader
ltsl_cutout_o.shader
ltsl_o.shader
ltsl_onetrans.shader
ltsl_onetrans_o.shader
ltsl_overlay.shader
ltsl_overlay_one.shader
ltsl_trans.shader
ltsl_trans_o.shader
ltsl_twotrans.shader
ltsl_twotrans_o.shader

Multi (5)
ltsmulti.shader
ltsmulti_fur.shader
ltsmulti_gem.shader
ltsmulti_o.shader
ltsmulti_ref.shader

UsePass (10)
ltspass_baker.shader
ltspass_bakeramp.shader
ltspass_cutout.shader
ltspass_dummy.shader
ltspass_lite_cutout.shader
ltspass_lite_opaque.shader
ltspass_lite_transparent.shader
ltspass_opaque.shader
ltspass_proponly.shader
ltspass_transparent.shader
```

#### Standard 家族

| 输出文件 | 运行环境 | 额外宏/Pass |
|---|---|---|
| `lts.shader` | Opaque 标准材质 | `LIL_RENDER=0`；完整 Forward、ShadowCaster、DepthOnly、DepthNormals、HoMetadata、HoGeometry、HoCharacterCapture、GBuffer、MotionVectors、Meta |
| `lts_cutout.shader` | Cutout 标准材质 | `LIL_RENDER=1`；与标准 Opaque 基本相同，Alpha Clip 在 alpha pass 中生效 |
| `lts_trans.shader` / `lts_onetrans.shader` / `lts_twotrans.shader` | Transparent | `LIL_RENDER=2`；包含 `lilToonOIT` 的变体/Pass，按透明结构区分 |
| `lts_o.shader` / `lts_cutout_o.shader` / `lts_trans_o.shader` | 带 Outline 的完整材质 | `LIL_OUTLINE`；额外 `FORWARD_OUTLINE`，通常 LightMode=`UniversalForward` |
| `lts_oo.shader` / `lts_cutout_oo.shader` | 仅 Outline | 只保留 `FORWARD_OUTLINE`，不应当当作主体材质使用 |
| `lts_fur*.shader` | Fur | `LIL_FUR`；可能有 `FORWARD_FUR_PRE`、`FORWARD_FUR`，并有重复 SubShader/Pass 结构 |
| `lts_gem.shader` | Gem | `LIL_GEM`、`LIL_GEM_PRE`；先预处理/折射，再主体 Forward |
| `lts_ref.shader` | Refraction | `LIL_REFRACTION`；Camera Opaque + 环境反射，跳过 Lightmap 和附加灯阴影变体 |
| `lts_ref_blur.shader` | 模糊 Refraction | `LIL_REFRACTION` + `LIL_REFRACTION_BLUR2`；额外按粗糙度采样/模糊背景 |
| `lts_fakeshadow.shader` | Fake Shadow | 主体用途是伪阴影投射；跳过 Lightmap、顶点灯、Light Probe、Reflection blending/box 等大量变体 |
| `lts_overlay*.shader` | Overlay/OIT | 主要保留 Forward 与 `lilToonOIT`，不适合依赖完整静态 GI |

#### Lite 家族

Lite 输出在文件中定义 `LIL_LITE`。`lil_common_frag.hlsl` 中大量功能以 `!defined(LIL_LITE)` 保护，例如：

```hlsl
#if defined(LIL_FEATURE_SSS) && !defined(LIL_LITE) && !defined(LIL_GEM)
#if defined(LIL_FEATURE_REFLECTION) && ... && !defined(LIL_LITE)
#if defined(LIL_FEATURE_BACKLIGHT) && !defined(LIL_LITE) && !defined(LIL_GEM)
```

因此 Lite 不是“同一个 Shader 少几个关键词”，而是 HLSL 逻辑本身进入了低成本分支。当前生成结果还会跳过：

```text
主光阴影相关 variants
Additional Light Shadow
Reflection Probe blending
Reflection Probe box projection
```

Lite 适合远景、简单道具、低成本透明层，不适合作为 HO 质量基线。

#### Multi 家族

Multi 输出定义 `LIL_MULTI` 和一组 `LIL_MULTI_INPUTS_*`，例如：

```text
LIL_MULTI_INPUTS_MAIN2ND
LIL_MULTI_INPUTS_MAIN3RD
LIL_MULTI_INPUTS_SHADOW
LIL_MULTI_INPUTS_NORMAL
LIL_MULTI_INPUTS_REFLECTION
LIL_MULTI_INPUTS_MATCAP
LIL_MULTI_INPUTS_EMISSION
```

它的意义是把多个材质/输入组装进同一 Shader 结构，而不是简单的性能 Lite 变体。`ltsmulti_gem` 和 `ltsmulti_ref` 还叠加 `LIL_GEM` 或 `LIL_REFRACTION`。

当前 Multi 输出普遍包含：

```text
#pragma skip_variants _ADDITIONAL_LIGHT_SHADOWS
```

所以 Multi 材质的附加灯阴影不能按 Standard 结果推断。

#### UsePass 家族

UsePass 输出是兼容层，主要用于将 lilToon Pass 提供给其他材质/旧工作流。它们不是新算法，也不应被视为和 Standard 文件完全等价。对 HoRP 来说，必须逐 Pass 检查它是否包含：

```text
ShadowCaster
DepthOnly
DepthNormals
HoGeometryBuffer
HoMetadataBuffer
MotionVectors
```

如果一个外部材质只 UsePass 了 Forward，而没有复用 Geometry/Metadata/Shadow Pass，它就不能自动参与 HoRP 的全部语义通道。

### 3.7 当前项目的宏开关与真实功能状态

`D:\Unity_Project\BREAK_URP\ProjectSettings\lilToonSetting.json` 是当前工程 Shader 输出的关键控制面。当前值及其实际后果如下：

| 设置 | 当前值 | 输出结果 |
|---|---:|---|
| `LIL_FEATURE_*` 全局功能集合 | 大部分 `true` | Standard/Cutout 输出包含完整 lilToon 功能宏 |
| `LIL_OPTIMIZE_APPLY_SHADOW_FA` | `true` | Shadow/FA 相关逻辑使用优化路径 |
| `LIL_OPTIMIZE_USE_FORWARDADD` | `true` | 生成 Additional Light/ForwardAdd 相关逻辑 |
| `LIL_OPTIMIZE_USE_FORWARDADD_SHADOW` | `false` | 不强制启用 ForwardAdd 全阴影替代路径 |
| `LIL_OPTIMIZE_USE_VERTEXLIGHT` | `true` | 保留 Vertex Light 相关输入/分支 |
| `LIL_OPTIMIZE_USE_LIGHTMAP` | `false` | 生成结果跳过 Lightmap、Dynamic Lightmap、Shadowmask、Directional Lightmap variants |
| `LIL_OPTIMIZE_DEFFERED` | `false` | 不把当前主线当作 Deferred-only 输出 |
| `m_LightProbeSystem` | `1` | 项目已切到 APV 系统；仍需 APV 组件和烘焙数据 |
| `m_RequireDepthTexture` | `1` | Camera Depth / Ho Geometry 使用有基础条件 |
| `m_RequireOpaqueTexture` | `1` | Refraction 使用 Camera Opaque 有基础条件 |

一个非常关键的工程结论是：

> “未来不会新增编译文件数量”并不等于“未来不增加变体”。当前架构已经把大量功能宏固定进 52 个 Shader 输出文件，真正控制编译规模的是 `lilToonSetting` 的 `skip_variants` 和每个家族的 Pass/宏组合。新增 HoRP 功能时，优先增加现有 Pass 的语义输入或全局资源，不要新增一套独立 Shader 家族。

### 3.8 当前朱木古堂的真实支持结论

| 功能 | 源码存在 | 当前 `lts/lts_cutout` 输出 | 朱木古堂实际使用情况 |
|---|---:|---:|---|
| 主光 toon | 是 | 是 | 正在使用 |
| Main Light Shadow | 是 | 是 | 场景有 Directional Light，Renderer 也启用 ShadowCast |
| Additional Lights | 是 | 是 | 场景有大量 Point Light，Standard 保留 Additional Light 变体 |
| Additional Light Shadows | 是 | Standard 保留 | 是否有实际 atlas/灯光阴影取决于灯与 Renderer 设置 |
| Lightmap | 是 | **否，variants 被 skip** | 当前不能作为该场景的 lilToon 光照来源 |
| 传统 SH/Light Probe | 是 | Standard Shader 可用 SH 路径 | 场景没有明确的 Light Probe Group，不能称为已验证 |
| APV | HLSL 存在 | **是，适用输出含 `ProbeVolumeVariants.hlsl`** | URP Asset 已启用 APV，场景数据待烘焙 |
| Reflection Probe | HLSL/URP variants 存在 | **是，含 blending/box/atlas** | 场景未放 ReflectionProbe，未实际验证 |
| Refraction | 独立 `lts_ref` 输出 | 不属于当前标准场景 Shader | 工程有 opaque texture，但朱木古堂材质主要是 `lts/lts_cutout` |
| Ho Geometry/Metadata | HoRP Pass 存在 | **是** | 当前场景 Renderer 已启用对应 Feature |
| Ho-GTAO | 材质属性和 `_HoAOTexture` 存在 | **是** | 当前 Renderer 同时存在 HTrace AO 与 Ho-GTAO，需按顺序验收 |
| Ho-GI/SSGI | 管线 Feature | 不是 lilToon 固有宏 | HTrace SSGI 当前 `m_Active: 1`，后续换 Ho-GI |
| ShadowCaster | Pass 存在 | **是** | Standard/Cutout 都可被 ShadowMap/HoRP 重绘 |

这张表比“lilToon 支持 Lightmap/Probe/Reflection”更接近当前工程真实情况：**支持能力必须同时通过源码、生成文本、ProjectSettings 和测试场景资产四层证据确认。** 当前朱木古堂的 Ho-GTAO/SSGI 已激活，HTrace AO/Planar Reflection 仍未激活；Renderer 资产状态变化后必须重新核对这张表。

### 3.9 P0 调查结果与决策

#### P0-1：当前生成 Shader 是否包含 APV 变体（修复前后）

**修复前结论：不包含；修复后结论：包含。**

实证方法：扫描当前生成的 52 个 `*.shader` 文件，搜索：

```text
ProbeVolumeVariants.hlsl
PROBE_VOLUMES_L1
PROBE_VOLUMES_L2
```

修复前扫描结果为 0 个文件命中。加入全局开关并重新导入后，当前 52 个输出中有 47 个文件命中 `ProbeVolumeVariants.hlsl`；剩余 5 个是 FakeShadow/Baker/Dummy 等不应采样场景光照的专用输出。

源码链路现在是：

```text
lil_common_functions.hlsl
    -> #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
    -> SampleAPV(...)

Default*.lilblock
    -> #pragma lil_skip_variants_probevolumes
    -> importer.skipProbeVolumes = true
    -> LIL_OPTIMIZE_USE_PROBEVOLUMES=false 时不加入 ProbeVolumeVariants.hlsl
    -> LIL_OPTIMIZE_USE_PROBEVOLUMES=true 时保留 include，并生成 PROBE_VOLUMES_L1/L2 变体
```

因此 APV 当前是“源码已实现、输出已可选编译”。如果画面仍无 APV 效果，下一步应检查 APV 组件、Baking Set、Lighting 数据和材质/Renderer 是否真正使用 APV，而不是再修改 lilToon HLSL。

#### P0-2：是否应该恢复 APV 变体

**决策已实现：恢复为可条件启用的变体，不新增 Shader 文件。**

推荐方案：

1. 增加一个编译设置，例如 `LIL_OPTIMIZE_USE_PROBEVOLUMES`，默认值跟随当前项目保持关闭。
2. 当 URP Asset 使用 `LightProbeSystem.ProbeVolumes` 时，Standard/Lite/Multi/UsePass 对应 Forward Pass 不再插入 `lil_skip_variants_probevolumes`。
3. 让 Unity 的 URP ShaderBuildPreprocessor 根据当前 URP Asset 再做最终 stripping。
4. APV 项目构建后必须检查最终 ShaderVariantCollection/Player build 中存在 `PROBE_VOLUMES_L1/L2`。
5. 先恢复 Standard 家族，确认角色、SSS 和 Ho-GI 基础光照正确后，再决定 Lite/Multi 是否需要 APV。

这样不会增加 52 个 Shader 文件，只会增加“启用 APV 的 Shader 家族”的变体数量。当前朱木古堂 `m_LightProbeSystem: 1` 已切到 `Adaptive Probe Volumes`，并且测试工程的 `lilToonSetting.json` 已将 `LIL_OPTIMIZE_USE_PROBEVOLUMES` 设为 `true`；仍需在场景中完成 APV 组件和烘焙。

#### P0-3：`DIRLIGHTMAP_COMBINED` 是否需要恢复

**当前场景：不需要；未来静态高质量 GI：需要专门验证后决定。**

当前工程有两个独立限制：

1. `lilToonSetting.json` 的 `LIL_OPTIMIZE_USE_LIGHTMAP=false` 让 Standard/Cutout 输出直接 skip 所有 Lightmap variants。
2. `lil_common_macro.hlsl:173` 无条件 `#undef LIL_USE_DIRLIGHTMAP`，即使某个 Lite/Multi 输出保留了 `DIRLIGHTMAP_COMBINED` pragma，最终 toon 主光宏也不会进入方向性 Lightmap 分支。

因此 P0 不建议立刻打开方向性 Lightmap，而是分两步：

```text
先打开 LIL_OPTIMIZE_USE_LIGHTMAP
    -> 验证普通 Non-Directional Lightmap 对 toon 颜色/阴影的影响
再单独移除 LIL_USE_DIRLIGHTMAP 的无条件 undef
    -> 验证 Directional Lightmap 是否符合 HO/NPR 风格
```

验收必须比较：Lightmap color、direction、shadowmask、toon shadow threshold、`_ShadowEnvStrength`，不能只看场景整体亮度。

#### P0-4：Lite 是否适合高质量 Reflection Probe

**结论：不适合做高质量反射基线。**

扫描结果：12 个 `ltsl*.shader` 全部包含：

```text
#pragma skip_variants _REFLECTION_PROBE_BLENDING _REFLECTION_PROBE_BOX_PROJECTION
#pragma skip_variants ... _MAIN_LIGHT_SHADOWS ...
#pragma skip_variants _ADDITIONAL_LIGHT_SHADOWS
```

Lite 仍保留 `_REFLECTION_PROBE_ATLAS`，因此不是完全没有 Reflection Probe；但它不能依赖两个探针的完整混合，也不能依赖 Box Projection 的局部正确性。对 HO 质量基线的规则应固定为：

```text
室内/湿面/金属/英雄道具 -> Standard lts 系列
远景/低成本/简单透明    -> Lite ltsl 系列
```

如果未来希望 Lite 也支持局部正确反射，应恢复对应两个 variants，并重新评估 Lite 的性能定位；不建议偷偷把 Lite 当作 Standard 使用。

#### P0 状态

- [x] 扫描当前 52 个生成 Shader，确认 APV 变体缺失。
- [x] 核对 `lts.shader`/`lts_cutout.shader` 的实际 Pass、关键词和 Lightmap skip。
- [x] 核对 `lilToonSetting.json` 的编译优化开关。
- [x] 核对 Lite 家族 12 个输出的 Reflection/Shadow variants 裁剪。
- [x] 确认朱木古堂使用 Standard Opaque/Cutout，而不是 Lite/Multi/Refraction。
- [x] 作出“不新增 Shader 文件、增加条件变体开关”的 APV 决策。
- [ ] 在 Unity Editor 中生成 APV 开关打开后的真实 Shader，并运行 APV 对照场景。
- [ ] 在打开 Lightmap variants 后重新烘焙朱木古堂，验证 Non-Directional/Directional 两条路径。

---

## 4. HO/NPR 的推荐工程基线

### 4.1 按资产类型选择光照方案

| 资产 | 主光照 | 间接光 | 反射 | 阴影 |
|---|---|---|---|---|
| 静态建筑/地面 | Lightmap + 主光 | Lightmap | Baked Reflection Probe | baked/shadowmask |
| 动态角色 | 实时主光 | APV；APV 不可用时 Light Probe | Reflection Probe | 实时 ShadowCaster |
| 动态小道具 | 实时主光 | APV/Light Probe | Reflection Probe | 实时 ShadowCaster |
| 金属/湿面 | 实时主光 | APV/Lightmap | Probe + SSR/Planar | 实时或 shadowmask |
| 水面/镜子 | 实时主光 | APV/Lightmap | Planar Reflection | 独立投影策略 |
| 玻璃/宝石 | 实时主光 | APV/Lightmap | Probe + Camera Opaque Refraction | 视材质需求决定 |

### 4.2 推荐的执行顺序

1. 先稳定 Lightmap/APV/Reflection Probe 基础光照。
2. 再调 lilToon 的 toon threshold、shadow environment、backlight 和 SSS。
3. 再加入 Ho-GTAO，默认只压制接触和间接光。
4. 再加入 Ho-GI/SSGI，作为动态颜色反弹增强，并排除描边壳。
5. 最后接入 SSR/Planar，并把 Reflection Probe 作为 fallback。

不要在基础探针和 Lightmap 尚未稳定时调 SSGI，否则很难分辨是烘焙错误、探针错误还是屏幕空间算法错误。

### 4.3 最小验收场景

建议建立一个长期保留的 `LightingProbeValidation` 场景，至少包含：

- 静态墙体和地面。
- 室内/室外过渡。
- 一个动态角色，包含脸、头发、衣服和金属配件。
- 一个彩色反弹区域。
- 一个强烈窗光和一个阴影角落。
- 一个金属球、一个湿地面、一个玻璃/宝石对象。
- 至少两个 Reflection Probe，并开启/关闭 blending 与 box projection 对照。
- Lightmap only、Light Probe only、APV only、APV + GTAO、APV + Ho-GI 对照视图。

验收时分别观察：

1. 角色从室外走入室内时，环境色是否连续。
2. 角色身体不同部位是否需要 APV 的逐像素变化。
3. toon 阴影面是否保留合理的环境色，而不是被探针冲白。
4. 动态角色是否正常投影和接收 ShadowMap。
5. 金属/玻璃是否在 SSR 不可见时退回 Reflection Probe。
6. GTAO 是否只增加接触感，没有把整个角色压黑。
7. SSGI 是否排除描边和非物理表面。

### 4.4 在朱木古堂中启用 Lightmap：最小操作路径

这是“让静态物体接收烘焙光照”的路径，不需要 APV。

#### A. 先让 lilToon 编译 Lightmap 变体

1. 选中任意 lilToon 材质，进入 Inspector 的 `Shader Setting` 模式。
2. 展开 `Build Size Optimization (for all materials)` / “优化构建大小（用于所有材质）”。
3. 打开 `Use Lightmap` / “使用光照贴图”。
4. 点击 `Assets/lilToon/[Shader] Refresh shaders`，或让 `ApplyShaderSetting()` 自动刷新。
5. 检查 [lts.shader](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/lts.shader) 不再出现 `skip_variants LIGHTMAP_ON ...`，并出现 `LIGHTMAP_ON`、`DYNAMICLIGHTMAP_ON` 等 `multi_compile`。

这一步只改变 Shader 编译能力，不会自动把场景变成已烘焙状态。

#### B. 设置静态场景几何

对朱木古堂的墙、地面、建筑和固定道具：

1. 在 Hierarchy 选中对象，Inspector 顶部 `Static` 菜单勾选 `Contribute GI`。
2. 在 Mesh Renderer 的 Lighting 区域确认 `Receive Global Illumination = Lightmaps`。
3. 对模型资产检查 Model Importer 的 `Generate Lightmap UVs`，或者确保已有合格 UV2。
4. 移动角色、布料、动态道具不要设为 Lightmap receiver；它们以后应使用 Light Probe/APV。

Unity API 对应：`Renderer.receiveGI = ReceiveGI.Lightmaps`、`Renderer.lightmapIndex`、`Renderer.lightmapScaleOffset`。[ReceiveGI](https://docs.unity3d.com/cn/6000.0/ScriptReference/ReceiveGI.html)

#### C. Lighting 窗口

```text
Window -> Rendering -> Lighting
    -> Scene / Lighting Settings
    -> 确认 Baked Global Illumination
    -> 选择 Progressive CPU/GPU Lightmapper
    -> 设置 Lightmap Resolution、Padding、Filtering、Bounces
    -> Generate Lighting
```

朱木古堂当前场景已经保存了一组 Lightmapping 参数，但 `m_LightingDataAsset` 仍为空，因此不能视为已经有可消费的烘焙数据。生成后应确认场景出现有效 LightingDataAsset 和 Lightmap 纹理。

#### D. 验收

用一个静态地面材质和一个静态墙面材质观察 Lightmap 变化；再移动动态角色。正确结果应是：

- 静态物体出现逐纹素烘焙变化。
- 动态角色不会因为 Lightmap UV 缺失而变黑，而是继续使用实时主光/探针。
- 如果角色需要与烘焙场景融合，应配置 Light Probe 或 APV，而不是把角色强行加入静态 Lightmap。

### 4.5 在朱木古堂中启用传统 Light Probe：兼容基线

如果暂时不修改 URP Asset 的 Light Probe System：

1. `GameObject -> Light -> Light Probe Group` 创建探针组。
2. 在 Scene 视图点击 `Edit Light Probes`。
3. 沿角色活动区域布置至少两层高度的 3D 探针体积；门窗、灯光边界和室内角落加密。
4. 选中动态角色的 MeshRenderer/SkinnedMeshRenderer：
   - `Receive Global Illumination = Light Probes`。
   - `Light Probes = Blend Probes`。
   - 必要时设置统一的 `Probe Anchor`。
5. 回到 `Window -> Rendering -> Lighting -> Generate Lighting`。

运行时 Unity 会把插值后的 `unity_SH*` 自动绑定给 lilToon。可用 C# 调试：

```csharp
LightProbes.GetInterpolatedProbe(
    renderer.bounds.center,
    renderer,
    out SphericalHarmonicsL2 probe);
```

如果角色身体各部分出现明显断层，先统一 `probeAnchor`，再考虑增加探针或改用 APV。

### 4.6 在朱木古堂中启用 APV：推荐质量基线

APV 是 Unity 6/URP17 的新探针系统。它和 Lightmap 不是互斥开关：建议静态建筑继续用 Lightmap，动态角色使用 APV。

#### A. 从 PC_RPAsset 切换探针系统

主入口是当前质量等级正在使用的 URP Asset，而不是先在场景里随便创建探针：

```text
选中 Assets/Settings/PC_RPAsset.asset
    -> Inspector
    -> Lighting
    -> Light Probe System
    -> 将 Light Probe Groups 切换为 Adaptive Probe Volumes
```

也可以从：

```text
Window -> Rendering -> Lighting
    -> Adaptive Probe Volumes 标签
    -> 进入/提示切换当前 URP Asset 的 Light Probe System
```

两条路径修改的是同一个 URP Asset 字段；切换完成后，APV 的详细设置应继续在 Lighting 窗口的 `Adaptive Probe Volumes` 面板中完成，而不是回到每个材质上设置。

URP17 中：

```text
LightProbeSystem.LegacyLightProbes = 0
LightProbeSystem.ProbeVolumes      = 1
```

朱木古堂当前 `PC_RPAsset.asset` 为 `m_LightProbeSystem: 1`，即 Adaptive Probe Volumes。项目文件中的数字不是建议直接手改的入口，优先使用 Inspector，避免漏掉 URP Asset 关联资源和编辑器缓存。

#### B. 场景中添加 APV

```text
GameObject -> Light -> Adaptive Probe Volumes -> Adaptive Probe Volume
```

第一次建议：

- `Mode = Global`，覆盖整套朱木古堂场景。
- 场景光照结构复杂处降低 Probe Spacing。
- `Fill Empty Spaces` 保持开启，先获得连续覆盖。
- 对描边、纯特效、不会参与 GI 的对象使用 Renderer Filter 排除。

#### C. 设置光源与几何

- 参与烘焙的灯使用 `Baked` 或 `Mixed` Light Mode；完全 `Realtime` 的灯不会被当作静态 APV 光照烘入。
- 墙、地面、屋顶等几何勾选 `Contribute Global Illumination`。
- 动态角色一般不需要 Contribute GI，但必须使用 APV 可用的 Shader 变体，并作为 APV 的接收者渲染。
- 玻璃、描边壳、纯后处理代理几何应按需求从 APV 几何过滤中排除。

#### D. Baking Set 与烘焙

```text
Window -> Rendering -> Lighting -> Adaptive Probe Volumes
    -> Baking Mode = Single Scene
    -> Probe Positions = Recalculate
    -> 设置 Min Probe Spacing / Max Probe Spacing
    -> Generate Lighting
```

如果未来需要多个场景一起烘焙，创建 Baking Set 并把相关场景加入；如果要在多个 Lighting Scenario 之间混合，保持 probe positions 不变。[APV panel reference](https://docs.unity.cn/6000.0/Documentation/Manual/urp/probevolumes-lighting-panel-reference)

#### E. 修漏光和调试

- 在问题区域添加 `Probe Adjustment Volume`。
- 使用 `Window -> Analysis -> Rendering Debugger -> Probe Volume`。
- 打开 `Display Probes`、`Display Bricks`、`Debug Probe Sampling`。
- 先确认 APV 数据存在，再确认 lilToon Shader 的 `PROBE_VOLUMES_L1/L2` 变体存在。

当前 lilToon 的 APV HLSL 已经存在，但生成器仍裁掉这些变体。因此 APV 场景在当前基线下即使烘焙成功，Standard lilToon 也不会真正采样 APV；必须先完成 P0 的条件编译改造。

### 4.7 朱木古堂建议的第一次实验顺序

为了避免一次打开太多变量，建议复制场景做四个版本：

| 场景 | lilToon Lightmap variants | URP Light Probe System | 用途 |
|---|---:|---|---|
| `朱木古堂_LiveBaseline` | 关闭 | Legacy Light Probes | 当前画面基线 |
| `朱木古堂_Lightmap` | 打开 | Legacy Light Probes | 验证静态表面 Lightmap |
| `朱木古堂_Probe` | 可关闭 | Legacy Light Probes | 验证动态角色 SH/Light Probe |
| `朱木古堂_APV` | 可关闭 | Adaptive Probe Volumes | 验证 APV；Shader variants 已生成，仍需 APV 组件和烘焙数据 |

每个场景再分别切换 Ho-GTAO/Ho-GI，记录同一镜头的 Beauty、ambient/gi、normal、depth、shadow 和 reflection 对照。

---

## 5. 后续任务清单

### P0：确认当前真实支持

- [x] 扫描修复前的 `lts.shader`、`lts_cutout.shader` 和全部 52 个输出，确认 APV 变体缺失。
- [x] 实现并启用“条件启用 APV 变体、不新增 Shader 文件”的路线。
- [x] 重新生成并确认 47/52 个适用输出包含 `ProbeVolumeVariants.hlsl`。
- [x] 确认当前朱木古堂不需要 `DIRLIGHTMAP_COMBINED`；未来启用 Lightmap 后再单独验收方向性路径。
- [x] 标记 Lite 家族不适合高质量 Reflection Probe blending/box projection 场景。
- [x] 在 Unity Editor 中生成 APV 开关打开后的真实 Shader。
- [ ] 在 APV 组件和数据烘焙完成后运行 APV 对照场景。
- [ ] 在打开 Lightmap variants 后重新烘焙朱木古堂，验证 Non-Directional/Directional 两条路径。

### P1：建立资产和场景规范

- [ ] 写一份 Lightmap Static / Contribute GI / Receive GI 的项目约定。
- [ ] 写一份 APV Baking Set、spacing、Adjustment Volume 约定。
- [ ] 写一份 Reflection Probe 命名、体积、分辨率、Box Projection 约定。
- [ ] 在 `LightingProbeValidation` 场景中固定对照截图和 DebugTile。

### P2：HoRP 语义接入

- [ ] 将 Lightmap/SH/APV 统一视为 `ambient_base` 的候选生产端。
- [ ] 将 Ho-GI 统一输出 `gi`，不要让材质绑定具体 SSGI 实现名。
- [ ] 将 Reflection Probe、SSR、Planar Reflection 统一为可插拔 reflection source。
- [ ] 将 ShadowMap、Ho-ShadowCast atlas 和材质 toon 门控拆成明确的 shadow 语义。
- [ ] 保持 GTAO 的 `ao`、SSGI 的 `gi`、反射的 `reflection` 通道独立可调试。

---

## 6. 参考资料

### Unity 官方

- [Light Probes](https://docs.unity.cn/2021.1/Documentation/Manual/LightProbes.html)
- [Light Probe Group placement](https://docs.unity.cn/6000.1/Documentation/Manual/class-LightProbeGroup.html)
- [Adaptive Probe Volumes concept](https://docs.unity3d.com/cn/6000.0/Manual/urp/probevolumes-concept.html)
- [Adaptive Probe Volumes in URP](https://docs.unity3d.com/cn/6000.0/Manual/urp/probevolumes.html)
- [Reflection Probe](https://docs.unity.cn/Manual/class-ReflectionProbe.html)
- [Reflection Probes in URP](https://docs.unity.cn/Packages/com.unity.render-pipelines.universal@17.0/manual/lighting/reflection-probes.html)
- [Using Reflection Probes](https://docs.unity.cn/Manual/UsingReflectionProbes.html)

### 本地实现

- [lilToon rendering functions](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_functions.hlsl)
- [lilToon rendering macros](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_macro.hlsl)
- [lilToon fragment lighting](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_frag.hlsl)
- [lilToon ShadowCaster pass](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_pass_shadowcaster.hlsl)
- [lilToon shader importer](D:/Unity_Fork/lilToon/Assets/lilToon/Editor/lilShaderContainerImporter.cs)
- [lilToon shader settings](D:/Unity_Fork/lilToon/Assets/lilToon/Editor/lilToonSetting.cs)
- [当前 Standard 生成输出 lts.shader](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/lts.shader)
- [当前 Cutout 生成输出 lts_cutout.shader](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/lts_cutout.shader)
- [朱木古堂测试场景](D:/Unity_Project/BREAK_URP/Assets/mmd场景测试/朱木古堂/New%20Scene.unity)
- [朱木古堂 lilToon 编译设置](D:/Unity_Project/BREAK_URP/ProjectSettings/lilToonSetting.json)
- [朱木古堂 URP Asset](D:/Unity_Project/BREAK_URP/Assets/Settings/PC_RPAsset.asset)
- [朱木古堂 Renderer](D:/Unity_Project/BREAK_URP/Assets/Settings/PC_Renderer.asset)
- [URP17 LightProbeSystem enum](D:/Unity_Fork/HoUrp17.3.0/Runtime/Data/UniversalRenderPipelineAsset.cs:398)
- [HO pipeline review](D:/Unity_Fork/lilToon-URP-Extensions/Documentation~/架构优化/LILTOON_RENDER_PIPELINE_REVIEW_AND_PLAN.md)
- [HO channel contract](D:/Unity_Fork/lilToon-URP-Extensions/Documentation~/架构优化/LILTOON_CHANNEL_CONTRACT_V1.md)
- [HO GTAO plan](D:/Unity_Fork/lilToon-URP-Extensions/Documentation~/架构优化/LILTOON_GTAO_PLAN.md)

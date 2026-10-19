# Ho-RTGI：一跳 diffuse GI

已沿用户参考工程接通 BrdfRayTracing → ShadeSecondarySurfaces → GI temporal/spatial/final，提供独立的一跳diffuse GI输出及可选的场景加法合成。SDK GI reservoir与时间/空间重采样复用固定原文件；应用bridge适配Ho生产端。最终NPR影色/ramp映射或材质内接收未纳入本阶段。

2026-10-19起的持续推进、实际参考对照、失败与交接见[RTGI入口](RTGI/README.md)。次级天空、显式直接天空归属和正常相机加法合成已有实测；独立GI降噪已接入原版RELAX_DIFFUSE，Linear D3D12 Mono Player两路径已验证真实应用帧下的请求式生产/响应/合成。正常enabled-camera自动呈现、目标场景、GPU预算和完整Player矩阵继续推进，不能由局部对照通过替代。

## 使用与输出

Renderer 添加 **Ho-RTGI**，前面启用 GB / SB；需要天空时再启用 Ho-Atmosphere。需要 D3D12 DXR、inline RT、Compute、Mono、同分辨率单采样 GB/SB，以及已更新的 SB 网格法线布局。GI 可以独立启用，不要求先打开 Ho-RTDI；两者同时启用也各自生产独立输出。

合成默认关闭。全局或局部Volume的Ho-RTGI提供「合成到场景」及「间接光强度」，可逐相机覆盖Feature高级设置中的未覆盖默认值（强度默认.25）。启用后在透明物之前，把GI加到有效opaque接收面；强度0保留原画面，强度变化不重置采样历史。只支持全视口、Mono、单采样合成，其他情况保留独立生产与原画面。

输出已含接收BRDF/albedo，合成不再乘主色、不再添加primary emission；Gamma工程先将原画面转线性相加，再转回显示空间。雾只衰减新增GI，保留原画面雾色；透明物在其后绘制。与DI同时合成时按Feature顺序消费当前camera color，通常将DI放在GI前。已有烘焙/SSGI等间接光仍由原路径负责，需要按场景决定叠加份额，GI不会自动改写它们。

Diffuse / Receiver / RawDiffuse / All调试显示独立输出；Receiver绿为有效接收面、红为输入无效。Diffuse与正常合成消费当前发布结果；RawDiffuse始终显示未降噪贡献。调试与正常画面合成是两项独立控制。

RenderGraph 消费 `HoRTGIRenderGraphResources.diffuse` 并声明读依赖；兼容全局为 `_HoRTGIDiffuseTexture` 与 `_HoRTGIActive`。RGB 为线性 HDR，**已包含参考 Lambert 接收 BRDF 与 diffuse albedo**；A 为 receiver validity。有效暗处可以 RGB=0、A=1，主表面缺失或 GB/SB 域不匹配为 A=0。

### 独立GI降噪

Volume的「独立 GI 降噪」可逐相机覆盖Feature的`denoise`默认值，默认关闭。启用后，每相机分配独立的NRD4.17.3 RELAX_DIFFUSE实例、池和guide/history；不共享DI的历史，不运行DI私有RELAX shader body。借用的只有固定native ABI与dispatch资源调度实现。若库、shader或UAV格式不可用，状态栏给出原因并发布raw。

Final在乘主表面albedo之前直接产生demodulated diffuse；不从已乘色信号反除。NRD输入使用固定signed-oct normal编码、diffuse roughness=1、正viewZ与实际GI样本/射线命中距离，天空距离限于FP16可表示范围；invalid使用远viewZ并在resolve强制RGB/A清零。输入除64、输出乘64是固定HDR单位转换，保留原信号和raw；不是外观增益，也不承诺任意HDR范围。

RELAX参数采用固定头文件的原版默认值，累计预算按观测到的非缩放渲染时间换算为约.5秒主历史/.1秒快速历史；Scene View使用30/6样本预算。静态世界使用前后相机矩阵及零world motion；几何/灯/材质/天空/采样签名变化保守清历史，暂不处理动态表面速度。关闭降噪释放资源，再启用从新历史开始。

`HoRTGIRenderGraphResources.rawDiffuse` / `_HoRTGIRawDiffuseTexture`始终提供raw；`diffuse` / `_HoRTGIDiffuseTexture`在降噪开启且可用时提供filtered。二者的线性HDR、已乘接收albedo与receiver validity契约相同。启用降噪增加约79B/像素加16×16 tile标记、192KiB常量缓冲，1080p约156.4MiB，尚无目标场景GPU毫秒结论。

### 天空贡献归属

| 设置 | 默认 | 行为 |
| --- | --- | --- |
| `useEnvironment` | true | 借用前置Ho-Atmosphere；关闭时直接天空与次级天空均关闭 |
| `secondaryEnvironmentSamples` | 4 | 次级命中点的天空PDF/RIS候选预算，0关闭该项；即使没有解析灯也能生成反弹 |
| `includeDirectEnvironment` | false | 是否将主表面射线直接miss的天空加入GI；与RTDI配合时保持关闭，GI独立承担直接天空时显式打开 |

与早期原型相比，**主表面直接天空现在默认关闭**，避免和RTDI重复计光。天空经墙面再到接收面的贡献保留。调整GI设置或天空revision会按对应签名失效历史；关闭GI不会改写Ho-Atmosphere或DI的输入。

`maxDistance` 控制GI反弹命中范围，不代表该距离外没有遮挡。启用直接天空时，有限范围内miss还会按需查询到100000世界单位的天空可见性；远处挡板仍能挡住天空。这个独立模式可能增加一条主射线，不能据输出分辨率不变称没有成本。

## 已接通的部分

`HoRTScene` 可选择绑定 GI 专用 hit material；原来的 DI inline query 默认路径不变。命中属性读取沿参考工程 `SurfaceRayTracingCommon.hlsl` 的 Unity vertex-fetch 路径，使用实际 primitive、重心坐标与对象变换，不依赖相机可见性或屏幕颜色。读取 UV0～3，并返回世界位置所需的 hit distance、三角形几何法线、插值/normal-map 着色法线、主色、perceptual roughness、metallic、reflectance 和独立 HDR emission。

`HoRTGISecondaryMaterials` 拥有私有材质适配副本，复制原材质属性；不修改源材质、Forward、CS 或 ShadowCast。调用顺序为 BeginFrame → scene.Synchronize（内部 Resolve）→ EndFrame → Build/Dispatch。销毁时先销毁 scene，再销毁适配材质。

Emission 直接调用 lilToon 现有 `lilEmissionComponent` / `lilEmission2ndComponent`，求和 RGB × 各组权重。保留两组的启用、颜色、贴图/alpha、UV 选择、ST/动画、mask、渐变、blink 与 main-strength；与 SB 相同，fluorescence 使用中性 lighting gate。不会把主色、matcap、rim、旧 Forward 明亮部分当作发光来源。

## 首版支持域

- 刚性opaque MeshRenderer已有GI命中验证，支持屏幕外、变换更新和非均匀/镜像变换。共享HoRTScene后来已有skinned snapshot，但GI的骨骼材质命中/动态接收仍待专门验收；GI尚未接入Cutout适配，透明、任意shader顶点变形、static batching或additional vertex streams不在本阶段支持域。
- 当前命中材质接收标准 `lilToon` opaque，第一主色链、两组 normal map、两组专用 emission，以及 smoothness/metallic 贴图。Main2nd/Main3rd、parallax 位移、Lite/Multi/Hair/自定义 shader 尚未接入，明确返回 unsupported。
- 命中材质副本读取 sharedMaterial；Renderer MaterialPropertyBlock 属性覆盖及自定义 lilToon 扩展尚未接入。
- Unsupported opaque 材质仍有真实命中，status=2，保持遮挡；不能把它转成环境 miss。status=0 才表示 miss，status=1 表示已求值的材质。
- 纹理采样显式LOD0，尚未实现ray-cone过滤与GSAA。发光视角组分使用二次射线的观察方向。已有加法场景合成及独立GI降噪；SSS、最终NPR ramp/材质内接收及完整Player/场景验收仍未交付。
- 二次命中点支持 point / spot / directional 解析灯，限制与 Ho-RTDI 相同：cookie、非全局 cullingMask、baked-only、其他灯型排除；Rendering Layer / 艺术灯光链接未接入。RT 可见性独立于 raster Light.shadows。
- 二次材质命中点计算解析灯、专用emission与天空照明。次级天空沿固定SDK的环境重要性采样接入；主表面直接天空仅在显式开启时经miss路径贡献。没有多跳反馈、发光网格重要性采样、主表面specular GI或完美镜面路径。
- 不支持的 opaque 命中按黑表面处理；无贡献或被几何法线拒绝的方向仍计作黑样本，避免丢弃零贡献样本导致能量偏高。为保留 HDR 能量，未启用参考中的 firefly radiance clamp；当前 raw 输出有噪声。

## 参考链路、资源与历史

GenerateSecondary 使用参考 BrdfRayTracing 的 diffuse 分支、SDK RNG 与 Donut cosine hemisphere / ONB，每像素一条射线。ShadeSecondary调用`RTXDI_SampleLightsForSurface`统一解析灯/天空候选，使用stored reservoir重建真正选中的样本再测可见性、求辐射，加专用emission后调用`RTXDI_MakeGIReservoir`。Temporal / Spatial分别调用SDK的GI重采样，均使用ray-traced bias correction；Final按visibility / reservoir weight / diffuse BRDF着色，不启用final MIS。

显式启用的天空miss沿参考distant-sample表示进入GI reservoir，读取Ho-Atmosphere全方向原始HDR辐射。次级天空使用与DI相同的128²等立体角PDF mip和固定SDK RIS预采样，但拥有私有资源与shader-local绑定。SDK原算法/许可/provenance保留；不为GI引入通用PT、DLSS或ReGIR。

求交、环境准备与GI重采样/Final在一个graphics-queue unsafe pass内顺序执行。未合成时保留BeforeRenderingPostProcessing；启用合成时，生产与合成均放在BeforeRenderingTransparents，按入队顺序先生产后消费。直接借用GB/SB，不复制当前主表面GBuffer；显式声明AS metadata、buffer、输出与天空依赖。Ho-Atmosphere先生产环境，GI使用shader-local绑定，关闭GI天空不清掉其他消费者输入。

兼容路径由 Ho-Atmosphere 环境 pass 发布借用快照，GI 在执行时读取；每相机开始和生产端销毁时清空快照。RenderGraph pass data 会由池复用，因此每次记录都显式清空缺失天空的 handle/参数/revision，避免沿用旧输入。

每相机保存三份32B SDK GI reservoir、一份48B二次数据、一份20B共享布局的上帧guide，以及RGBA16F输出（8B/像素）。1080p含reservoir block padding约342MiB，另有灯/AS/输入；开启次级天空增加约.208MiB的128² R32F全mip PDF与16384条uint2 RIS，不随输出分辨率增长。禁用时释放PDF并仅保留单元素dummy RIS绑定。DI/GI各自持有场景和AS，目标场景GPU预算尚待验收。

静态世界按前后相机矩阵生成 SDK 所需的像素 motion / depth delta。几何 Revision、灯、材质属性、纹理 updateCount / sampler / ST、天空 Revision、分辨率或关键采样设置变化清历史；禁用后重启也清历史。已声明的时间动画参数保守禁用时间复用。纹理更新识别依赖 Texture.updateCount，不能据此宣称识别任意外部 GPU 写入或自定义 shader global 变化。

dispatch设置在Setup前捕获为不可变数值快照，同一帧的资源准备、signature、RG记录与兼容执行消费一致值；下一次Setup才消费Inspector/脚本新值。它不授权在已记录图执行前任意销毁借用的场景/纹理资源。

不维护 previous TLAS，也没有动态角色 motion；只有未变化的几何可以复用历史，此时当前 AS 同时描述上一帧世界，可从上一帧表面做 visibility。一次只做一跳，不把历史 reservoir 当作多跳反馈缓存。

## 验证

2026-10-20步骤006：两个Linear D3D12 Win64 Mono Player（RG/compatibility）实际exit0，各304次相机请求、299条有效应用帧记录、16项runtime检查。非均匀红蓝emission反弹的filtered/raw红蓝均值约.970/.970，空间均值图L1约10.35%，时间方差约降99.89%；camera运动59帧保留重投影、几何移动24帧保守reset、移除emission首帧归零、Volume/denoise与实际filtered加法合成均通过。每个真实Update只一次请求，不标称正常自动camera；复现、失败和警告边界见[应用帧/Player证据](../Tests~/RTGI/Dynamic/README.md)。

2026-10-19场景合成：Linear/Gamma × RG/compat四组，每组62次手动相机请求、33项检查，实际exit0；覆盖同帧贡献、主色/emission、Volume与两台相机独立强度、雾/透明/前景depth、域不匹配、局部视口绕过、DI+GI连续合成及真实离屏红墙反弹进入camera color。另有legacy GI与未合成环境两项回归。说明与边界见[合成验证](../Tests~/RTGI/Composite/README.md)，不据此宣称Player、自动帧运动或NRD完成。

2026-10-19次级环境生产接入：Linear/Gamma下，新增环境RG/compat四项、既有完整GI链路RG/compat四项、secondary材质命中两项，以及DX11 inactive一项，11个实际进程均退出0。新增每组138次请求（含3次有界editor warmup）、23项检查，含解析灯+环境MIS叠加5%门槛、零解析灯、近/远挡板、天空归属、revision/历史失效与实际pass snapshot。原始失败保留；复现与范围见[次级环境验证](../Tests~/RTGI/Environment/README.md)。这不替代正常应用帧、NRD、目标场景GPU预算或Player验收。

`HoRTGISecondaryValidation.Run` 在隔离工程的 D3D12 上分别以 Gamma / Linear 色彩空间执行真实 TraceRay，核对离屏命中/距离、材质参数、两组 HDR emission、main-strength、UV1/贴图 alpha/ST、mask RGB/alpha、禁用与材质切换、unsupported 遮挡、镜像非均匀变换及源材质保持原样。保留原材质 Color/HDR 与 Gamma-tagged metallic/reflectance 的颜色空间语义；三角形法线朝向射线所在侧，插值法线对齐同侧。Shader 警告/错误视作失败。

`HoRTGIValidation.Run` / `RunCompatibility` / `RunUnsupported` 验证完整五阶段。Unity 6000.3.15f1 的 D3D12 RenderGraph / compatibility 在 Gamma / Linear 下通过：天空积分、实际 HDR cube、Unity 天空盒方向性 HDR 捕获、彩色/金属接收响应、离屏 HDR emission、次级 RTXDI 解析灯能量、unsupported 遮挡、空间单独复用、时间单独复用与 sample age、历史失效、静态世界相机重投影、receiver 域拒绝、关闭/销毁、无自动合成与 Volume 调试。Compute 警告/错误视作失败，D3D11 不支持分支通过。DI/GI 同时启用的独立发布与既有 DI 两条路径回归也通过。

定向测试天空 RGB = 8 × (direction × 0.5 + 0.5)，面向 -Z 的白色 Lambert 接收面的理论积分为 (4, 4, 1.3333)，实测统计均值约 (4.008, 3.982, 1.330)。这是链路与能量验证，不是性能或最终画质验收。

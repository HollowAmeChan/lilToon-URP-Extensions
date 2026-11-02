# Unity 灯光、Ho Cast 与 lilToon：现状审计和统一契约建议

日期：2026-11-02。范围：Unity 6000.3.15f1、HoURP 17.3.0、本机 Ho lilToon 与当前动画场景。

**状态：源码调查及当前场景诊断已完成；后半的统一契约、改造阶段是建议，尚未实施。** 本次新增只读诊断工具，未修改 Runtime / Shader 阴影算法、灯光或材质设置。

## 1. 先说清楚三个不同的问题

1. **这盏灯照不照这个物体**：位置、范围、锥角、颜色/色温、强度、Cookie、Culling Mask、Lighting Rendering Layers。
2. **这盏灯到这个位置有没有被挡**：ShadowCaster 几何、投射层、shadow map / RT 查询、阴影开关、强度、距离淡出、烘焙遮挡。
3. **材质如何表现这些结果**：lilToon 的光色/方向聚合、toon ramp、三层接收强度/Mask、艺术化主 Cast、额外 HDR 加亮。

同一个词“Cast”目前同时指深度生成、可见度和最终压暗，容易造成误会。这里用 `V=1` 表示无遮挡、`V=0` 表示全遮挡；这是几何可见度，不是灯的亮度或最终颜色。

URP 的 Main Light 是选中的一盏方向光：优先可见的 `RenderSettings.sun`，否则选可见最亮方向光。它不是整个 Skybox、SH、APV 或全部环境光的合称。

## 2. 当前开着的场景：已经复现什么

场景：`BREAK_URP/Assets/00动画/MMD动画场景/花吐电気通用动画/Scenes/頑張りたい人へ.unity`。

先在活 Editor 只读导出，随后 Editor 关闭；隐藏批处理对保存的同一场景，从现有 `演出相机 · VMD` 发出一次 **320×180 诊断渲染**，读取真正的 `HoShadowCastRuntimeDiagnostics`。没有为了测试移动机位、切灯或保存场景。最后报告 `dirty=false`。

| 实际进入 Ho atlas 的灯 | Unity Light 阴影 | 模式 | Ho 实际分配 |
| --- | --- | --- | --- |
| 室内天光反弹 | Soft | Mixed | Spot 1024，1 slice |
| 桌椅柔光 | Soft | Realtime | Spot 1024，1 slice |
| 窗外晴日 · 后庭 | Soft | Realtime | Spot 1024，1 slice |
| 桌边花灯 001 · 柔光 | **None** | Realtime | **Point 512，6 slices** |
| 环境补光 | **None** | Realtime | **第二方向光 2048，4 cascades** |

这不是只根据 Inspector 推测：后两盏关闭阴影的灯，实际仍被收集和分配了 Ho 深度图。

还有一处 Light Linking 不一致：后庭灯的 **Lighting Layers=2**，WTD 身体/头发 Renderer 的 **Rendering Layers=1**，两者不匹配；它的 Custom Shadow Layers 却允许所有 caster。原生语义允许它使用这些物体投影、同时不照亮角色，但 Ho 全局接收函数没有逐接收面的灯光层过滤，仍会把该灯的 Cast 纳入全局遮挡。是否在某个具体像素压出阴影，还取决于范围、锥角、深度图与材质接收。

WTD 的 `_MultiLightIntensity=0` **只关闭自定义的末尾 HDR 加亮**。当前基础附加光宏的强度固定为 1，颜色/方向仍会聚合；`_VertexLightStrength=0` 也不能据此认定当前 URP 基础附光关闭。部分材质第一层 `_ShadowReceive=0`，第二/三层仍为 1，不能把第一层开关当整份材质的投影总开关。

当前主光 RT 产品处于生产状态、CS 没有接收声明；这个状态不证明每个像素都成功消费 RT。实际材质还要经过 OB 身份、请求、深度与有效性检查。

## 3. 原生 URP 阴影到底怎么运作

### 3.1 是逐灯的，不是所有灯合成一张压暗图

方向主光使用自己的级联 atlas。Point / Spot 使用附加灯 atlas，Spot 一个面，Point 六个面。atlas 是共用存储，但里面的灯/面有索引，接收时按当前灯读取；“共用池”不等于把所有灯阴影相乘。

概念上，每盏灯贡献自己的 `C_i × V_i`，再累加。红灯被挡住应减少红灯的贡献，蓝灯仍可照亮物体；不能因为红灯被挡住，就把蓝灯和环境一起压黑。对于 lilToon，`C_i` 需按其 NPR 聚合定义，不强制变成 Lambert/PBR。

### 3.2 参数与所有权

| 控制 | 原生意义 |
| --- | --- |
| Light.enabled / 对象启用 | 是否参与 |
| Shadows=None | 该灯不产生实时几何投影；仍可照明 |
| Hard / Soft | 是否使用软影过滤；不等于改变物体法线产生的明暗 |
| Shadow Strength | 该灯投影的混合强度 |
| Bias=Use Pipeline / Custom | 选择管线偏差或逐灯偏差；不是两个值同时用 |
| Additional Shadow Resolution | 每灯 Low/Medium/High 或 Custom 的请求分辨率 |
| Soft Shadow Quality | 逐灯或继承管线的过滤预算 |
| Lighting Layers | 哪些接收物体得到该灯照明 |
| Custom Shadow Layers | 哪些物体可为该灯投影，可以与受光层不同 |
| Renderer.shadowCastingMode | 物体是否投影、是否仅投影、双面投影 |
| 材质接收 | shader 是否读取阴影，及如何解释 |

原生 `ShadowUtils.IsValidShadowCastingLight` 明确要求附加灯 `shadows != None`、`shadowStrength > 0`。原生 `GetAdditionalLight` 的阴影重载返回 `shadowAttenuation`，材质必须实际使用它；生成了深度图不等于材质消费成功。

### 3.3 Realtime / Baked / Mixed 不是质量档

| Mode | 应如何理解 |
| --- | --- |
| Realtime | 运行时计算直接光和实时几何阴影 |
| Baked | 相应贡献存入烘焙数据，不应由自定义实时系统悄悄再补一遍直接光/投影 |
| Mixed | 要结合场景 Lighting Mode、烘焙数据与静态/动态接收物体判断 |

Mixed 的 Baked Indirect、Shadowmask、Distance Shadowmask、Subtractive 行为不同。Shadowmask 用 RGBA 保存有限的重叠烘焙遮挡通道；这与实时附加灯可参与数量不是同一限制。仅把 Inspector 的 Mode 改为 Mixed，并没有自动生成 Lightmap / Probe / Shadowmask。

原生 `AdditionalLightShadow` 会处理实时/烘焙遮挡及距离混合；自定义系统绕过它，就需要明确复现、限制支持域或回退。当前 lilToon 还有自身较简化的 Lightmap / Shadowmask 分支，不能视作完整 URP/Lit 兼容。

## 4. Ho 各模块实际各走哪条路

| 模块 | Light.shadows=None | 逐灯 Strength | Hard/Soft 与软影 | 生成/消费边界 |
| --- | --- | --- | --- | --- |
| URP 原生 | 不生成该灯实时投影 | 使用 | 原生 PCF / 质量档 | 主/附加 atlas，逐灯取样 |
| Ho ShadowCast | **当前不排除** | **当前只用 Feature 组强度** | **不区分，按 Ho 组 PCSS** | 独立 punctual atlas + 第二方向光 atlas，最后聚合相乘 |
| Ho CharacterShadow | 主光 None 时不生产 | 使用主灯强度 | 使用独立 CS 过滤/PCSS 设置，不是完整继承 Light 的软影语义 | 角色局部高密度主光 map；有效域替换主光来源 |
| Ho RTShadow | **当前请求过滤未检查 None** | 主光消费阶段使用 | 方向光按角直径；Point/Spot 当前 delta 硬影，不继承原生软影档 | 存量逐灯生产/诊断；现行 lilToon 消费只接主光 |
| 太阳体积光 | 无主光阴影时可显示无遮挡散射 | 原生主光采样处理 | 原生主光级联采样 | 空气点读取原生主光 atlas，不读取角色 CS 或屏幕 RT 表面 V |
| 局部体积光 | 可显示散射，但 HD 不捕获该灯遮挡 | Light × Source 艺术强度 | Hard 单次比较，Soft 小核过滤；HD 分辨率由 Source 设置 | 独立光源深度，不是复用 URP 附加 atlas |

局部体积读取 Light 参数这件事已比 ShadowCast 完整，但它并没有因此自动继承全部 Baked/Mixed、Cookie、材质表面消费语义。SD 明确是无光源深度的散射模式。

### 4.1 ShadowCast 不是“接管 URP 后就不再画原生投影”

它使用材质同一份 `ShadowCaster` pass，**另画自己的深度 atlas**、发布自己的全局纹理。它没有覆盖 URP 的主光/附加灯纹理，也没有统一关掉原生阴影 pass；原生和自研可以同时产生开销。

当前接收主干可概括为：

```text
主光来源 S = 有效 RTShadow，否则有效角色 CS，否则 URP 主光
Ho额外遮挡 H = 所有纳入 punctual 的遮挡之积 × 其他方向光遮挡之积
传入 toon 接收的 attenuation ≈ S × H
```

`HoShadowCastLightInfluence` 根据范围、锥角衰减投影影响，没有按灯的实际照明能量加权；弱补光仍可能产生明显压暗。采集器存了光色，但上述聚合采样没有用它恢复逐灯能量。

额外 HDR 加亮也会被同一个 `H` 调制。一个低质量附光 Cast 因而可以叠到干净的主光 RT 结果上；关闭主光 RT 并不能单独定位这个来源。

### 4.2 已确认的互操作缺口

**源码及当前场景已确认：**

- `HoShadowCastFrameCollector.IsLightCollectable` 没有检查 `Light.shadows`、逐灯 `shadowStrength` 或 Baked Mode。
- 写入强度来自 Feature 全局/组参数，不是 `Light.shadowStrength`。
- Ho ShadowCast 不按每灯的 Hard/Soft、Resolution Tier、Soft Quality、Use Pipeline Bias 完整解析；偏差直接读 Light 字段。
- caster 的 Rendering Layers 有过滤，接收函数没有照明层匹配参数；两种层的所有权不能混为一谈。
- lilToon 的附光颜色/方向累加未消费原生逐灯 `shadowAttenuation`，额外 HDR 路径也如此。
- `lilGetAdditionalLightHDR` 的 `minShadow` 参数没有写入；调用处初始化为 1，所以当前 `_MultiLightCastShadowStrength` 的这条最终混合是恒等式。
- RTShadow 有逐灯产品不代表“全灯 RT 接收”已接通；`HoRTShadowSampling.hlsl` 当前公开给材质的是主光入口。

**源码提示、仍需独立像素夹具验证：**

- Ho ShadowCast 使用相机 cullResults 和普通 RendererList 绘制 ShadowCaster，而非完整的原生逐灯 ShadowRendererList 路径。离屏 caster、ShadowsOnly / TwoSided / Off、LOD、Terrain 的一致性需单独验证，不能因画出了阴影就宣称全部继承。
- 自定义附光重载的选择与 `_ADDITIONAL_LIGHT_SHADOWS` 绑定；Cookie 和 Mixed/APV 的实际输入要补矩阵测试。只给当前累加补一个 `* light.shadowAttenuation` 还不够：现有 InputData 多数置零，未完整填入 Shadowmask / APV 遮挡。

## 5. lilToon 那些灯光参数该怎么理解

当前上游 lilToon 的 URP 分支也以聚合附光颜色/方向为主要方式，不能直接当 URP/Lit 的逐灯材质。项目魔改又叠加了 Ho 主 Cast 和独立 HDR 加亮。

| 参数/入口 | 本机当前含义与注意点 |
| --- | --- |
| 基础附光模式宏 | 当前 Forward+ 走片元聚合，颜色与方向混入主光；强度宏固定 1 |
| `_VertexLightStrength` | 原有跨管线 UI 属性；不能作为本机 URP 基础附光总强度开关 |
| `_MultiLightIntensity` | 魔改额外 HDR 加亮的强度，0 不关闭基础附光 |
| `_MultiLightCastShadowStrength` | UI 暴露，但当前 HDR 路径的 `minShadow` 未接，不能依赖 |
| `_LightMinLimit / _LightMaxLimit` | 基础光照上下限；额外 HDR 加亮发生在原有 clamp 之后 |
| `_MonochromeLighting` | 基础照明去色，不保证所有魔改新增路径都遵循 |
| `_LightDirectionOverride` | 艺术化修改 toon 光向，不能据它推断每灯几何投影 |
| `_ShadowReceive / 2nd / 3rd` + Mask | 各层 toon ramp 的接收权重，不是选灯列表 |
| `_UseShadow` | 材质 toon 阴影流程开关，不是 Renderer 的投射开关 |

现有主接收还会把 `fd.L` 与原方向之差用于艺术化调整后再进 ramp。即便可见度相同，改了附光方向、光色上限、AO、接收层或 ramp，最终影形也未必相同。

## 6. 原生能否做很多独立灯、很高质量

**可以独立管理，但有共同预算，不是无限独立高清贴图。** 当前 PC_Renderer 是 Forward+，不能把 PC_RPAsset 的 Per Object Limit=4 读成只能吃四盏灯；每相机和阴影 atlas 仍有限额。

本工程配置：

| 设置 | 当前值 |
| --- | --- |
| 原生主光 atlas | 4096，4 cascades，Shadow Distance=90m |
| 原生附加 atlas | 4096 |
| 原生附灯 Low / Medium / High | 256 / 512 / 1024 |
| 原生 Soft Quality | High |
| Ho punctual atlas | 8192，Spot=1024，Point 每面=512 |
| Ho 过滤 | PCSS High，最大半影半径 32 |
| Ho 第二方向光 | 独立 4096 atlas；当前补光分到每 cascade 2048 |

原生 atlas 满时会缩放请求/淘汰过小 slice；当前 Ho 保持配置 tile 分辨率，满后不再接纳。两者“更多灯 vs 每灯质量”的取舍不同。当前几盏 Spot 请求都可为 1024，因此不能仅凭 atlas 是 8192 就断言每盏 Ho Spot 都比原生精细两倍。

原生 Low 是 4 次硬件比较过滤，Medium/High 是 5×5 / 7×7 tent PCF。Ho PCSS 另做 blocker search 和接触相关的半影估计，观感更柔和不等于几何细节恢复更多；较强滤波也可能掩盖锯齿。角色 CS 的优势主要是把大量 texel 集中到小接收域。

质量调节要分开看：

1. **阴影几何分辨率**：方向光覆盖宽度 / tile 分辨率决定世界 texel 密度；Spot 在接收距离的锥体宽度同理。更窄合理锥角、正确距离与局部覆盖，比盲目扩大 atlas 更有效。
2. **软影模型**：Hard / PCF / PCSS / 有限光源 RT 不是一回事。提高 Native Soft Quality 是增加过滤质量，不自动得到物理面积光半影。
3. **最终像素采样**：toon 窄 ramp、细头发、Cutout 和屏幕采样还会放大边缘误差；更大的 map 或 RT 并不自动消除最终像素锯齿。
4. **偏差**：过大的 Normal Bias 会吃掉细小接触阴影，过小会自阴影；Use Pipeline Bias 不应被自研静默忽略。
5. **Point 成本**：每灯六面。同样每面 2048 比 512 要 16 倍 texel 存储/绘制面积；不能给一堆装饰灯都无差别拉满。

先统一生成和接收语义，再做同灯、同 caster、同 mask、同覆盖范围、同输出采样的 A/B。当前差异不能直接归因于“URP 阴影本来就差”。

## 7. 建议的长期分工

用户提出的“艺术主 Cast + 公共全灯 Cast”方向合理，但公共池应保留逐灯意义或正确加权的聚合信号，不能沿用所有 V 相乘的黑白遮罩。

```text
Unity Light + UniversalAdditionalLightData：灯、受光/投射层、原生开关的唯一事实
                       |
             Ho 灯光解析与阴影策略
          /              |                 \
     主方向光         其他灯公共照明        体积空气照明
   URP / CS / RT      URP 或未来 RT 池      原生/独立深度/未来空间查询
          |              |                 |
       SunCast        遮挡后 RGB/方向        空气采样点可见度
          |
   + 明确选入的 ArtCast 灯
          |
      艺术主 Cast → 材质各层 ramp
```

### 7.1 统一的灯光事实与默认行为

- Light 的类型、启停、Mode、颜色/色温、强度、范围、内外锥角、Cookie、Culling Mask、Lighting Layers、Shadow Layers 先统一解析，所有模块读同一结果。
- **Shadows=None 是表面几何投影的总否决**；不生成它的实时 Cast，不让它进入主 Cast 或公共 Cast。仍允许正常照明，以及显式启用的无遮挡体积散射。
- Hard / Soft 的语义跟 Light 走，Ho 再选择后端和质量。采样数、贴图分辨率、角直径/发光半径、体积近源柔化是不同维度。
- Baked 不偷偷补实时直接光/投影。Mixed 支持未完成的后端给出明确回退/不支持原因，不伪装成 V=1，也不把烘焙照明重复相加。
- 逐灯 Strength 只应用一次。原生 `shadowAttenuation` 已包含强度/部分混合；RT 原始 V 则不应事先烘入强度。公共接口要声明它返回 raw 还是 effective。

### 7.2 主 Cast：NPR 艺术服务

默认主方向光。额外灯只有显式勾选“参与艺术主 Cast”才可把其投影印到主 ramp；提供权重和作用部件/层。普通补光默认不加入。

同一灯的几何可见度尽量复用公共后端，但“把这道投影用来压主 ramp”是独立艺术路由。它不自动意味着把最终全色再乘一次，更不能让不照亮角色的灯凭投射层穿过角色的受光过滤。

Ho ShadowCast 可以保留为高质量 raster / 艺术投影生产者；无需把整个 URP 阴影系统接管才能服务这个定位。现有自动全收集、全乘做法应作为明确标记的 Legacy 行为处理，而非长期默认。

### 7.3 公共 Cast：按灯管理照明可见度

原生 atlas 或未来 RT 是后端。对附光 NPR 聚合，可先明确：

```text
B = Σ(该接收面允许的未遮挡 RGB 贡献)
S = Σ(同样贡献 × 对应灯有效 V)
```

还要保留匹配的方向统计。`S` 才是被遮挡后的附光颜色；不能用 `ΠV_i` 把全图压黑。简单例子：两个等强灯，一个被挡，剩下一个仍可照亮；乘积却变成 0。

现有无限灯研究的 `SunCast + OtherLightsCast` 和 RGB/方向聚合建议可继续使用。所谓“无限”是灯集合不受少量固定槽约束，固定射线预算仍是有噪声的估计，不是所有灯免费逐帧精确追踪。当前逐灯全屏 RT Output 不应直接扩成大量灯的持久屏幕纹理。

### 7.4 体积光参与同一灯光策略，但保持空间遮挡接口

空气需要 `V(light, worldPosition)`，不能用只针对相机第一层表面的屏幕 RT V 替代。主光 CS 的角色局部盒也不覆盖所有空气。

共享灯身份、Light 开关、受光/投射语义和质量策略；允许空气使用完整范围的独立深度图。以后如共享原生 shadow atlas，也要保证其距离、离屏 caster 和采样域满足空气需求。

当前 `HoVolumeLightSource.sourceRadius` 只是近源反平方亮点限制，**不是** RT 面积光半径；CS 的角直径、ShadowCast 的组 softness、原生 PCF Quality 也不能因为都叫“软”就合成一个数。

## 8. 建议实施次序与调试要求

1. **先止住契约漏洞**：None、Strength、Baked eligibility、Lighting/Shadow Layers 分离、原生 Bias/Soft/Resolution 继承。先明确旧场景的 Legacy 迁移方式，避免一升级就把用户手调的风格全部改掉。
2. **修接收接口**：分开基础附光、额外 HDR、逐灯可见度与 Art Main 路由；处理当前无效的 `_MultiLightCastShadowStrength`，完善真实 Shadowmask/APV 输入。
3. **用原生 raster 建立参考**：标准 URP/Lit 对照、自定义 lilToon 对照、选灯 ArtCast 对照。之后再谈替换公共池。
4. **接后端而非重写所有灯**：通过独立 Ho resolver / adapter 保留插件兼容性。只有原生 API 无法暴露实际分配/资源复用等必要信息时，再在 HoURP 做窄 hook。
5. **最后接无限灯 RT 聚合**：沿用既有研究阶段，不恢复少量固定灯槽的生产材质方案，不顺手改变 SIGMA / 主天光采样。

每个被调试的接收面/像素至少应能解释：灯稳定 ID、是否受该灯照明、是否允许投影、caster 层、实际后端、raw/effective V、Strength 是否已应用、tile 请求/实际分辨率、回退原因、是否进入 ArtMain、是否进入附光 RGB、体积使用哪份空间遮挡。

最小验证矩阵：None/Hard/Soft × Strength=0/.5/1；灯开关；Realtime/Baked/各 Mixed 模式；灯与接收层不匹配；自定义投射层；离屏 caster；Renderer Off/ShadowsOnly/TwoSided；Point 六面；Cookie；切场景/相机；角色近景与多角度。先证明确实在用正确的光/影，再做美术质量评价。

## 9. 证据与入口

只读现场快照（最近相机可能是旧帧）：

```powershell
python Tools~/MmdStages/stage-cli.py lighting-audit
```

上述 CLI 沿用制作队列的非 Play 模式入口，先退出 Play 再运行；它不为了读取状态自动停止用户的播放。

显式从现有 MainCamera 渲染一个小诊断帧，再读真实收集数据；不切场景，不改灯光，不保存：

```powershell
python Tools~/MmdStages/stage-cli.py lighting-audit-render
```

工程关闭时，隐藏 D3D12 批处理入口为 `HoLightingAudit.RunBatch -hoScene Assets/...unity`。工具位于 `Tools~/MmdStages/Unity/Editor/HoLightingAudit.cs`；输出 `BREAK_URP/Library/HoLightingAudit/current-scene.json`。本轮归档在 `Temp~/LightingAudit-20261102/`。320×180 单帧只验证调度/接线，不是阴影画质或视频收敛验收。

可版本管理的精简证据：`Tests~/LightingAudit/20261102-current-scene-summary.json`，含实际收集灯、关键 Light 参数、角色接收层、Feature 配置与核心源码 SHA-256；原始大快照仍留在本机临时目录。

关键本地源码：

- `Runtime/ShadowCast/HoShadowCastFrameCollector.cs`：`IsLightCollectable`、`AddLight`、`BuildSecondDirectionalFrameData`。
- `Runtime/ShadowCast/HoShadowCastPass.cs`：独立 atlas、RendererList 与发布。
- `Runtime/ShadowCast/Shaders/HoShadowCastSampling.hlsl`：`HoShadowCastLightInfluence`、`HoShadowCastAttenuation`。
- `Runtime/CharacterShadow/Shaders/HoCharacterShadowSampling.hlsl`：`HoCSResolveMainCast`。
- `Runtime/RTShadow/HoRTShadowRendererFeature.cs` 与 `Shaders/HoRTShadowSampling.hlsl`：请求过滤、产品和主光消费。
- `Runtime/VolumeLight/HoVolumeLightLocalPass.cs`、`Resources/HoVolumeLightSolarCommon.hlsl`：空气遮挡。
- `D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_macro.hlsl`：基础附光/HDR/主 Cast 宏；`lil_common_frag.hlsl`：各层 ramp 接收。
- `D:/Unity_Fork/HoUrp17.3.0/Runtime/ShadowUtils.cs`、`Passes/AdditionalLightsShadowAtlasLayout.cs`、`UniversalRenderPipeline.cs`。
- `D:/Unity_Fork/HoUrp17.3.0/ShaderLibrary/RealtimeLights.hlsl`、`Shadows.hlsl`：原生有效阴影与烘焙混合。

官方资料（2026-11-02 查询；本机 fork 的实际行为仍以本机版本为准）：

```text
https://docs.unity3d.com/6000.3/Documentation/Manual/urp/light-component.html
https://docs.unity3d.com/6000.3/Documentation/Manual/urp/shadow-resolution-urp.html
https://docs.unity3d.com/6000.3/Documentation/Manual/LightMode-Mixed-Shadowmask.html
https://docs.unity.com/en-us/engine/6000.3/manual/lighting-overview/lighting/rendering-layers
https://raw.githubusercontent.com/lilxyzw/lilToon/master/Assets/lilToon/Shader/Includes/lil_common_macro.hlsl
```

## 10. 后续讨论：聚合归属、烘焙与 Ho ShadowCast 退役候选

本节回应用户进一步提出的“静态尽量烘焙、少量艺术灯动态投影、考虑删除 Ho ShadowCast”。**这是定位建议，没有执行删除、禁用或资产迁移。** 对 §7.2 的保留候选进一步收敛：更倾向停止扩展现有独立全灯 atlas Feature，将其列为 Legacy 退役候选，不再建设“每盏灯挂一个组件就获得额外投影预算”的第二套通用系统。

### 10.1 谁聚合：灯光求值阶段，不是 atlas 打包阶段

不同灯的深度图处于不同光空间，不能直接混合深度纹理得到通用遮挡。接收位置确定后，才能查对应灯的可见度并按其贡献聚合。

| 路径 | 聚合工作的归属 |
| --- | --- |
| 原生 Forward+ | 先建立空间 cluster 的灯列表，材质片元只遍历影响当前 cluster 的候选灯 |
| Ho NPR 聚合产品 | 独立 GPU lighting pass 根据表面/身份/灯层计算颜色与方向统计；材质取统计后处理自己的 ramp |
| 未来大量灯 RT | GPU compute / ray tracing pass 做选灯、重要性权重、遮挡查询、重建和聚合，输出 SunCast / OtherLights 产品 |
| 静态 Lightmap | 烘焙阶段积分静态接收面的照明，运行时采样结果 |

GPU 中的少量局部灯循环很常见，Forward+ 的作用正是缩小逐像素候选集合。把同一个全灯循环搬到 Compute 本身不会消除复杂度；大量灯方案还需要空间剔除、重要性采样和可能的时空重用。

RT 计算仍由 GPU shader 程序执行。需要避免的是让每份 lilToon 材质都精确枚举世界所有灯、逐灯发射大量射线。固定采样预算估计的是加权聚合量；灯表维护、候选构建、重用校正、噪声与收敛仍有成本。

对于现有 NPR 模型，聚合可保留 `C0 / ΔC` 与对应方向矩，材质用自身接收权重重组后执行 ramp。它不能从少量统计量恢复任意逐灯 BRDF、任意每灯每层独立权重。既有无限灯研究 §6–8 已说明该边界，不把普通 PBR RTDI 的最终颜色直接拿来替代这些 NPR 输入。

### 10.2 “烘焙进池子”需要区分的数据

**普通 Lightmap / Probe / APV：** 主要缓存照明信息。静态房间烘好了，移动角色可以从探针等取得环境照明；普通 Lightmap 本身不提供任意空间位置的高清逐灯遮挡。Mixed Shadowmask 可为动态物体提供部分静态遮挡，但探针采样和有限重叠通道不能等同于完整高清逐灯深度图。

**静态 caster 的逐灯深度缓存：** 灯与静态遮挡物不变时，可以不重画这些深度；移动接收面仍可投影到缓存图查询。但灯/几何变化需要失效更新，移动角色的自阴影、互相投影和向地面投影仍需动态几何层或 RT。接收端的逐灯取样/聚合成本也保留。

因此“灯 Mode=Baked”不会自动把每灯高精度深度放入当前公共池。现有 Ho ShadowCast 按帧创建 atlas 并绘制，没有这样的持久缓存合同。若以后确有需求，应作为统一公共阴影后端的能力，而不是另开不受总预算约束的灯挂件。

Shadow map 可以做到高质量，缓存也有实际价值；HDRP 的 cached shadow 是可参考的机制。对本管线的取舍依据是收益、数据维护和预算，不是认定光栅阴影天然达不到高质量。RT 和烘焙同样需要分辨率/采样与正确接收，不自动保证成片最好。

### 10.3 更建议的生产分工

- **静态环境基础照明：** Lightmap / Probe / APV 等烘焙方案，按场景组织数据；先核对 PMX 的 lightmap UV、材质烘焙与 lilToon 接收，不能仅改变灯 Mode 就视为完成。
- **角色主光与重要动态照明：** 现有主光 RT / 角色 CS / 少量原生实时阴影按明确支持域选择，保留动态自阴影与相互遮挡。
- **艺术动态投影：** 少量显式声明、统一预算与优先级；只有选入者影响艺术主 Cast，普通补光默认不加入。
- **大量其他动态灯：** 未来需要时通过统一 RT 聚合产品扩展，不要求当前先为每灯维护一张额外高分辨率实时图。
- **体积光：** 保留空气采样所需的空间遮挡路径。退役 Ho ShadowCast 不会自动替代太阳体积所读的原生图或局部体积独立深度。

预算在渲染器/场景层集中控制；逐灯设置表达优先级和相对质量需求，不能无限追加独立预算。烘焙数据按受影响的灯/几何变化更新，避免给敏捷制作流程增加不必要的全场重烘焙。

建议的退役范围是现有 Ho ShadowCast 的自动全灯收集、独立全灯 atlas 和全局相乘接收。PCSS 工具函数、角色 CS、局部体积深度、少量原生动态阴影各自评估，不因共用了“shadow map”概念就整套删除。

真正删除前，先解除 lilToon 对该 Feature 的采样依赖、迁移旧场景/Renderer 资产及必要艺术灯路由，关闭时保证明确的中性 fallback。之后再删除实现与旧序列化入口；本节没有提前执行这些动作。

补充主要资料：

```text
https://docs.unity3d.com/6000.3/Documentation/Manual/urp/rendering/forward-plus-rendering-path.html
https://docs.unity3d.com/6000.3/Documentation/Manual/LightMode-Baked.html
https://docs.unity3d.com/6000.3/Documentation/Manual/LightMode-Mixed-Shadowmask.html
https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition%4017.3/manual/Shadows-in-HDRP.html
https://developer.nvidia.com/rtxdi
```

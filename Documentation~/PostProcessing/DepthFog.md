# 深度雾 / 高度雾（ScreenProcess · `DepthFog`）

> **状态：已实现。** 一个效果里两个槽——**深度雾**与**高度雾**，各有自己的开关，可以只开一个或两个都开；
> 两层在同一趟 pass 里按顺序合成（不依赖 ScreenProcess 的层叠加）。见 §3 与 §4。

相关背景：家族 C（深度/大气/天空遮罩驱动的染色）的划分见 `GradientInvestigation.md` 第 6 节。

## 0. 定位：这是**合成雾**，不是物理雾

- ScreenProcess 的每个效果都是图像合成，它和 ImageProcess 的差别**不是"更物理"，而是能读 buffer**（MetadataBuffer / GeometryBuffer / Sky）。本效果就是：**用深度（必要时加世界高度、天空遮罩）生成雾层，再合成到画面上**——和「渐变映射」是同一族（一个亮度驱动的颜色合成，一个深度/高度驱动的颜色合成）。
- 因此**不追求**：能量守恒、散射积分、与光照/泛光的物理耦合。也**不为"更物理"去换注入点**（tonemap 之前那套是物理雾路线，不做）。
- **因此得到**：颜色所见即所得（后置、显示空间合成）；透明物体、粒子、天空能统一处理；能被层遮罩按物体排除；参数直白，预设就是"一层雾的配方"。
- **因此放弃**：不参与 bloom、不被 tonemap 压肩、没有光锥/体积自阴影（要光柱是 `SkyTyndall` 的事）。
- **一层必须自足**：ScreenProcess 目前不做重叠合成（产品上也暂不打算做），所以深度项、高度项、近/远双色、天空处理全都落在**同一个效果**里，用乘子和两个槽表达组合——这就是"一个效果两个槽"而不是两个效果的原因。

## 1. 现状调查（一手证据）

| 结论 | 依据 |
| --- | --- |
| 注入点是 tonemap 之后（整个 ScreenProcess 栈） | `ScreenProcessRenderPassEvents.cs:7` `AfterRenderingPostProcessing`；ImageProcess 在 `+1` |
| 图层预算：6 个 Vector4 + `color` + `texture` + 强度/混合模式 + 层遮罩 | `ScreenProcessLayer.cs`（`parameters0-5`、`useMask`/`invertMask`/`debugMask`） |
| 深度有两条来源：GeometryBuffer 的**线性眼深**（世界单位）、或 URP 相机深度 | `DepthOfField.shader:95-104`（`LilHoGeometryBufferLinearDepthOrFar` vs `LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams)`） |
| GB 的 normal-depth 是 `R16G16B16A16_SFloat`，**眼深是半精度**，极远处会台阶化 | `HoGeometryBufferFormatUtility.cs:9-13` |
| 天空可以精确判定：coverage = `step(0.0001, depth)`，天空 `a≈0` | `HoGeometryBufferSampling.hlsl:12-15`、`SkyTyndall.shader:392-393` |
| 反投影用 Unity 自带函数即可（正交也正确） | `ComputeWorldSpacePosition`（core `Common.hlsl:1390-1401`，fork 内模板 `Utils/ScreenSpaceShadows.shader:23-29`）；矩阵在 `UnityInput.hlsl:99-102, 234`，别名见 `Input.hlsl:107, 216` |
| 矩阵声明在 URP 的 `UnityInput.hlsl`，core 不声明 → 新 shader 必须 include URP `Core.hlsl` | 同上；本 shader 已按此写 |
| `LinearEyeDepth` **不支持正交**；URP 自己的延迟雾也跳过正交 | core `Common.hlsl:1205`、`DeferredLights.cs:1136-1139` |
| URP 自带雾是 per-object、view-Z、tonemap 之前；与后置雾**会叠加** | `Lighting.hlsl:206-240`、`ShaderVariablesFunctions.hlsl:315-347, 411-414`、延迟走 `Utils/FogDeferred.hlsl` |
| fork 里没有任何 fog RendererFeature / 体积雾，也没有可复用的 inscattering | 搜 `Ho*` 文件为空；只有一个 GUID 悬空的 `OasisFogVolumeComponent`（`DefaultVolumeProfile.asset:255-282`） |
| 参考实现里没有能直接搬的雾：PPv2 的 Deferred Fog 只有两个 bool 且没有高度项（Unity Companion License）；Standard Assets `GlobalFog` 是唯一的高度雾但**目录里没有 licence 文件** | `.codex-research/depth_fog_research/existing-fog-implementations.md` |
| 包内已有"高度窗"先例可对齐 | `HoCharacterSpecializationComposite.shader:487-521`（`ComputeWorldSpacePosition` + world Y + smoothstep + hardness）、参数形状 `Settings.cs:37-45` |

## 2. 两个槽各自做什么

```
距离量 dist = viewZ（默认，对齐引擎雾的 max(viewZ - near, 0)）
            或 length(viewPos)（视线欧氏距离，"球形雾"）

深度雾槽（开关）：
  直线   dDepth = saturate((dist - start) / max(far - start, eps))
  指数   dDepth = 1 - exp(-density * max(dist - start, 0))
  指数平方 dDepth = 1 - exp(-(density * max(dist - start, 0))^2)
  alphaA = dDepth * 深度雾浓度

高度雾槽（开关，四种曲线形式）：
  高度窗（下方浓） h = 1 - smoothstep_硬度(startY, endY, height)
  高度窗（上方浓） h = 1 - 上面那个
  指数衰减（下方浓） h = exp(-falloff * max(height - baseY, 0))
  指数衰减（上方浓） h = exp(-falloff * max(baseY - height, 0))     // 云海
  height = 世界 Y（默认）或相机相对 Y
  alphaB = h * 高度雾浓度

合成（一趟 pass，顺序固定）：
  1. 深度雾：先按 alphaA 的强度对像素做"空气感"去饱和，再把颜色从近色插值到远色，最后按图层混合模式合成
  2. 高度雾：按 alphaB 用高度雾自己的颜色合成
  两次都用图层的混合模式（与 ImageProcess 共用的 24 模式表，见 `README.md` 的「共享图层混合表」）与 `_Intensity`，并乘以该层的层遮罩（今天 = MetadataBuffer 覆盖率，以后由 AC 提供）
```

- **天空**：`跳过天空`（默认，用 GB coverage 精确判定；没有 GB 时用远裁剪面近似）、`一起上雾`（天空也按远平面结果上雾，高度项在天空上自动失效）、`单独天空色`（天空用远色 × 天空强度，地面不受影响）。
- **air/去饱和**只由深度雾槽驱动（`alphaA × 去饱和`），关掉深度雾就只有纯色雾层。

## 3. 参数槽位（6 个 Vector4 刚好用完）

| 槽位 | 内容 |
| --- | --- |
| `color` | **深度雾近色**（面板顶部的"颜色"一栏） |
| `parameters0` | (深度雾开关, 距离曲线, 起始距离, 终点距离) |
| `parameters1` | (密度, 深度雾浓度, 远色混合, 空气感/去饱和) |
| `parameters2` | (远色 r, 远色 g, 远色 b, 高度雾开关) |
| `parameters3` | (高度曲线 0..3, 高度参考 0=世界 1=相机, 高度 A, 高度 B) |
| `parameters4` | (过渡硬度, 高度雾浓度, 高度雾颜色 r, 高度雾颜色 g) |
| `parameters5` | (高度雾颜色 b, 天空模式, 天空强度, 抖动) |

模式数值写在公开枚举里（`ScreenProcessFogMode.cs`）：`ScreenProcessFogDepthMode` 0/1/2、`ScreenProcessFogHeightMode` 0..3、`ScreenProcessFogHeightReference` 0/1、`ScreenProcessFogSkyMode` 0/1/2——Inspector、预设、shader 共用同一份契约，有一个检查专门钉住这些数值（§6）。

`高度 A/B` 的含义随曲线形式变化：窗模式是"起点/终点高度"，衰减模式是"基准高度/衰减率"。UI 标签跟着切换。

## 4. Inspector

```
[✓] 深度雾                 ← 槽 1 的开关（关掉只留这一行）
    距离曲线 / 起始距离 / 终点距离 / 密度 / 浓度上限 / 远色 / 远色混合 / 空气感（去饱和）
[ ] 高度雾                 ← 槽 2 的开关
    高度曲线 / 高度参考 / 起点或基准高度 / 终点或衰减率 / 过渡硬度 / 高度雾浓度 / 高度雾颜色
天空 / 天空强度（天空≠跳过时）/ 抖动
```

- 两个槽的开关就是"折叠"：关掉的槽只占一行，行数由 `GetDepthFogLineCount` 与绘制函数严格对应（有检查钉住，§6）。
- 顶部原有的"颜色 / 混合模式"两行属于图层核心字段，其中"颜色"就是深度雾的近色。
- 层遮罩照旧可用（把角色排除、只对背景上雾等），它是**层内**能力，不是叠层；遮罩来源以后由 AC 提供，今天吃 MetadataBuffer 覆盖率。

## 5. 预设（5 组 13 个）

| 组 | 预设 | 用的是哪个槽 |
| --- | --- | --- |
| 空气感 | 远景空气感 / 清晨薄雾 / 黄昏尘雾 | 深度（薄雾/尘雾另加高度窗） |
| 天气 | 雨雾 / 浓雾 / 雪雾 | 深度（雨雾/雪雾另加高度衰减） |
| 高度 | 深谷高度雾 / 云海 / 贴地薄雾 | 只开高度 |
| 组合 | 贴地雾 + 远景霾 | 两个槽同时开（演示"一层里两种雾"） |
| 风格 | 夜景霓虹雾 / 沙漠热霾 / 天空染色 | 深度（热霾用指数平方 + 大抖动；天空染色只作用于天空） |

## 6. 验证

| 检查 | 内容 | 结果 |
| --- | --- | --- |
| `.codex-research/depth_fog_sim/fog_math_check`（dotnet，链接出货的 `ScreenProcessFogMath.cs`） | 11 项：三种距离曲线的解析值/单调性/边界、高度窗与衰减（含方向与硬度）、浓度合成、**眼深↔设备深度往返**（reversed-Z 与非 reversed 两组，最坏相对误差 6.6e-5；换算到世界空间的影响 < 0.0002）、**模式枚举数值契约**、两槽合成顺序与混合模式（可分离的 20 个模式逐一镜像共享混合表）、两槽互不影响 | 全过；`-- --negative-control` 会让 3 项 FAIL（证明检查能失败） |
| `.codex-research/depth_fog_sim/check_screenprocess_fog_ui.js` | Inspector 行数：`GetDepthFogLineCount` 声明 20 行 == 绘制函数实际 19 行增量 + 1 行收尾；两边的分支条件集合一致；元素行数加的是 4 行基础 | 全过（曾在这里抓到"属性缺失时多留 4 行"的 bug）；负对照会 FAIL |
| `.codex-research/shader-check`（D3DCompiler，`ps_5_0`） | `check_all.ps1` 把全部 11 个 ImageProcess / ScreenProcess shader 抽成 standalone 再编译（包内 include 全部内联，只给"被丢掉的 include 本该提供的东西"打桩）；`check_blend_include.js` 要求 IP/SP 两边都 include 同一张混合表、且不得再出现本地副本（6 条负对照每次运行都跑） | 11/11 通过；`-NegativeControl` 会拿掉 `_HoGeometryBufferValid` 的声明并要求检查失败 |
| Roslyn 独立编译（Editor + Runtime） | 0 error（仅剩仓库原有 11 条 CS0649 警告） | 通过 |
| `.codex-research/effect_enum_check/check_effect_enum_coverage.js` | 效果枚举完整性：图标面板覆盖每个成员且不重复、六个 `switch`（绘制/行数/排序/默认值/显示名/预设）都认识每个成员、`ScreenProcessEffectRegistry` 的每个成员都指向真实存在的 `.shader`、`Editor/PostProcessing` 里没有"用字面量夹住枚举下标"的写法 | 全过；4 个负对照全部生效（详见 §6.1） |

### 6.1 两次"编译通过但实机坏掉"的坑（都已被检查钉住）

1. **`undeclared identifier '_HoGeometryBufferValid'`**（Unity d3d11 报在 `DepthFog.shader`）。shader 漏声明这个 uniform，而当时 checker 的桩里恰好也有一份同名声明，于是本地编译"通过"、Unity 才报错。规则改成：**只给被丢掉的 include 本该提供的东西打桩**，shader 自己要声明的 uniform 一律不代劳；顺手把桩补齐（`_BlitTexture_TexelSize`、`SampleSceneNormals`），现在 10 个 shader 全过，负对照也从"通过"变成"复现 Unity 的原话"。
2. **`GetEffect()` 用字面量夹枚举下标**：原来是 `(ScreenProcessEffect)Mathf.Clamp(value, 0, 6)`，`6` 是当时的最后一个效果（`SkyTyndall`）。`DepthFog = 7` 加进来后，任何深度雾图层读回来都是 `SkyTyndall` —— 图标按钮认不出自己那层，**每点一次就多加一层**，图层名还显示成"天光丁达尔"。这类错误编译器不会报，所以单独加了枚举完整性检查（负对照里重新塞回 `Mathf.Clamp(..., 0, 6)` 会立刻 FAIL）。新增效果时**不要**在下标上写死上界，用 `Enum.GetValues(typeof(...)).Length`（`ImageProcessStackVolumeEditor` 早就是这么写的）。

## 7. 尚未验证（需要实机看）

1. 深度来源两条路径的画面差异：`GeometryBuffer` 开/关、正交相机（正交会强制走相机深度）；
2. 天空三种模式的观感，尤其是没有 GB 时"远裁剪面近似"把远景山脊误判成天空的情况；
3. 与 URP 自带雾**同时开启**时的叠加（用本效果时应把 Render Settings 的 Fog 关掉）；
4. 半精度 GB 深度在远裁剪面很大时是否出现台阶（补救：关掉 GB 用相机深度，或加大抖动）；
5. 两个槽同时开时的观感与混合模式搭配（`组合/贴地雾 + 远景霾` 就是为此准备的预设）；
6. Inspector 行高是否与层列表一致（两个开关切换时最容易错位）；
7. 层遮罩把角色排除时，雾在角色边缘是否有半透明过渡的瑕疵。

## 8. 后续可做

- **遮罩柔化**（对深度多次采样再算 alpha，出画笔感雾边、压掉半精度台阶）：参数槽位已用满，需要先砍一个参数或给 `ScreenProcessLayer` 加一个 Vector4；
- **按物体/材质的不同雾**：层遮罩已经能表达一部分（今天只按 MetadataBuffer 覆盖率），更细的要等 AC 的具名遮罩；
- **体积感**：需要 raymarch + 光源可见性，属于另一个效果（另立方案）。

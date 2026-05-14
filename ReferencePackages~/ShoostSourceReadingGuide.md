# Shoost 源码参考阅读指南

这份文档记录如何把 Shoost v0.16.3 的 AssetRipper 解包工程、Cpp2IL 输出、RenderDoc shader dump 和外部来源包串起来看，用来重写进 `lilToon-URP-Extensions`。

## 总体判断

现在的资料已经足够还原 Shoost 后处理项目的大致原貌：

- AssetRipper 给出了 Unity 工程结构、PPS v2 效果设置类、预设资产、纹理和 shader 名称。
- Cpp2IL 的 ISIL 输出能补上很多 renderer 运行流程，例如 `Shader.Find`、uniform 设置、临时 RT 和 blit 顺序。
- RenderDoc 抓帧导出了实际运行的 D3D11 shader bytecode 和 DXBC 反汇编。
- 外部包源码可以补齐标准算法和命名语义，例如 PPS v2、Retro Look Pro、X-PostProcessing、Kino。

限制也要记住：AssetRipper 导出的 `.shader` 大多是 `DummyShaderTextExporter`，不能当真实 HLSL 源码。RenderDoc 反汇编是编译后的结果，能还原算法，但不会保留原始变量名和源码结构。

还有一个很重要的边界：RenderDoc dump 只覆盖 `shoostALL.rdc` 这一帧里、当时滤镜开关和参数状态实际执行/绑定过的 shader/variant。Shoost 里很多滤镜还有变体开关、mode、pass 分支和预设组合；如果后续看 C#、预设或外部包源码时发现某个 shader/pass 在当前 dump 里缺失，优先判断为“这次没抓到对应开关/变体”，不要直接判断 Shoost 没有这个 shader。遇到这种情况应记录缺口，并提醒重新开对应滤镜/模式再抓一帧。

## 关键路径

Shoost 分析目录：

```text
D:\Unity_Fork\Shoost_v0.16.3
```

主要资料：

- `unpack\ExportedProject`
  - AssetRipper 解包工程。
- `DecompileWorkFiles\Cpp2ILOutputs\ISIL\cpp2il_out_pr20_isil\IsilDump\Assembly-CSharp`
  - Cpp2IL ISIL 输出，优先看 renderer 流程。
- `RenderDocShaderDump`
  - 只导出 `Hidden/Custom/*` 的 shader dump。
- `RenderDocAllShaderDump`
  - 同一帧实际绑定/执行过的全部 VS/PS/CS shader dump。
  - 这里的“全部”不是 Shoost 所有可能变体，只是当前抓帧覆盖到的变体。
- `Shoost后处理包来源索引.md`
  - 包来源和效果列表。

本包参考目录：

```text
D:\Unity_Fork\lilToon-URP-Extensions\ReferencePackages~
```

## 推荐阅读顺序

### 1. 先从效果设置类入手

在 AssetRipper 工程里找 `PostProcessEffectSettings`：

```powershell
rg -n "PostProcessEffectSettings|\[PostProcess\(" "D:\Unity_Fork\Shoost_v0.16.3\unpack\ExportedProject\Assets\Scripts\Assembly-CSharp"
```

重点看：

- `[PostProcess(...)]` 菜单路径。
- class 名和 renderer 名。
- 参数字段、范围、默认值。
- 是否明显来自第三方包，例如 `Retro Look Pro/...`、`X-PostProcessing/...`、`Kino/...`、`Custom/...`。

这些信息决定 URP 侧的 VolumeComponent 参数设计。

### 2. 再看 Cpp2IL ISIL 里的 renderer 流程

AssetRipper 的 C# 常常函数体不完整，renderer 的 `Render()` 要去 ISIL 补。

例子：

```powershell
rg -n "RGBSplitRenderer|LUTColorGradingRenderer|GrainCustomRenderer|VignetteRenderer_Custom|MotionTrailRenderer|TubeRenderer" "D:\Unity_Fork\Shoost_v0.16.3\DecompileWorkFiles\Cpp2ILOutputs\ISIL\cpp2il_out_pr20_isil\IsilDump\Assembly-CSharp"
```

重点提取：

- `Shader.Find("...")`
- `Shader.PropertyToID("...")`
- `PropertySheet.properties.SetFloat/SetVector/SetTexture`
- `RuntimeUtilities.BlitFullscreenTriangle`
- 临时 RT 创建、尺寸、格式、downsample。
- 多 pass 顺序。

这些信息决定 URP RenderPass 的执行顺序、RT 分配和 material uniform 名。

### 3. 再看 RenderDoc shader dump

Custom 效果优先看：

```text
D:\Unity_Fork\Shoost_v0.16.3\RenderDocShaderDump\ShaderDumpIndex.md
```

第三方/全局对比看：

```text
D:\Unity_Fork\Shoost_v0.16.3\RenderDocAllShaderDump\ShaderDumpIndex.md
```

注意：这里的“全量”是指 `shoostALL.rdc` 这一帧实际绑定/执行过的全部 shader，不等于 Shoost 所有可能变体。遇到当前 dump 里没有的 shader/variant，优先补抓 RenderDoc。

文件含义：

- `.dxbc`：原始 shader bytecode。
- `.dxbc.asm`：RenderDoc 反汇编。

阅读反汇编时先看顶部声明：

- `ps_4_0` / `vs_4_0` / `cs_5_0`
- `dcl_constantbuffer cb0[...]`
- `dcl_resource_texture2d t0/t1/...`
- `dcl_sampler s0/s1/...`
- `dcl_input_ps linear v1.xy`

再读指令主体：

- `sample_indexable`：纹理采样。
- `mad`：乘加，经常是 UV 偏移或 lerp。
- `dp3`：颜色矩阵、亮度、色彩空间转换。
- `log/exp`：gamma / linear 转换。
- `sincos`：旋转、抖动、波形。
- `round_ni/floor/frc`：像素化、LUT 切片、量化。

### 4. 最后对比外部来源包

按 `PackageSourceMap.md` 找对应来源包：

- `UnityPostProcessingV2`
  - 标准 Bloom、Vignette、Grain、Color Grading、FinalPass、Uber。
- `RetroLookPro`
  - VHS、旧胶片、Bleed、Noise、Jitter、CRT、NTSC。
- `XPostProcessing`
  - Kawase/DualKawase、IrisBlur、RGBSplit、Pixelize 等。
- `KinoPostprocessing`
  - Bloom、Bokeh、Tube、Glitch、Overlay、Recolor 等。
- `ShoostUnpack`
  - Shoost 自定义参数、预设和改版痕迹。

如果来源包源码和 Shoost RenderDoc dump 差异很大，优先相信 Shoost 的 Cpp2IL uniform 流程和 RenderDoc 反汇编。

如果来源包或 Cpp2IL 指向的 shader 在 dump 里不存在，优先判断为当前抓帧没覆盖到对应开关/变体，而不是直接认为 Shoost 删除了它。补抓时最好只开目标滤镜或目标 mode，这样 Event Browser 和 shader dump 会干净很多。

## 按单个效果追踪的方法

以 `RGBSplit` 为例：

1. AssetRipper 找设置类：
   - `RGBSplit`
   - `RGBSplitRenderer`
   - 参数如 intensity、angle、mode。
2. ISIL 找 renderer：
   - `Shader.Find("Hidden/Custom/RGBSplit")`
   - `_Intensity`
   - `_Angle`
   - `_Mode`
   - `_ScreenRatio`
3. RenderDoc 看 shader：
   - `RenderDocShaderDump\01670_Hidden_Custom_RGBSplit.dxbc.asm`
   - 读 UV 偏移、通道重组、mode 分支。
4. 外部包对比：
   - X-PostProcessing 里可能有类似 RGB split/glitch。
   - 如果算法不同，就按 Shoost dump 重写。

## 哪些资料分别回答什么问题

| 问题 | 优先资料 |
|---|---|
| 这个效果叫什么，放在哪个菜单？ | AssetRipper C# 设置类 |
| 参数有哪些，范围是多少？ | AssetRipper C# 设置类、AMS 预设 |
| Runtime 如何调 shader？ | Cpp2IL ISIL |
| 有几个 pass？RT 怎么分配？ | Cpp2IL ISIL、RenderDoc event 顺序 |
| shader 算法是什么？ | RenderDoc `.dxbc.asm` |
| 这个效果是否来自外部包？ | `PackageSourceMap.md`、菜单路径、命名空间 |
| Shoost 是否改过第三方 shader？ | 外部包源码 vs `RenderDocAllShaderDump` |
| dump 里找不到某个 shader/variant 怎么办？ | 先认为当前抓帧没覆盖到，记录缺口并补抓 |

## URP 重写建议

重写时不要照搬 PPS v2 架构。建议拆成三层：

1. Volume 参数层
   - 对应 Shoost 的 `PostProcessEffectSettings`。
2. RenderFeature / RenderPass 调度层
   - 对应 Shoost renderer 的 `Render()` 流程。
3. HLSL shader 层
   - 对应 RenderDoc 反汇编和来源包算法。

先移植低耦合单 pass：

- `RGBSplit`
- `Pixelize`
- `Vignette_Custom`
- `Fisheye`
- `LUTColorGrading`
- `LevelAdjustment`
- `DownScaleResolution`

再移植多 pass / 依赖历史帧 / 依赖临时贴图：

- `RGBBlur`
- `RGBBlurV2`
- `IrisBlur`
- `KawaseBlur`
- `MotionTrail`
- `GrainCustomBaker` / `GrainCustomUber`
- `FilmBreath_GateWeave`
- `CRTEffects`

## 命名建议

为了后续对照方便，URP 侧初期可以保留 Shoost shader/uniform 名：

- shader 名用 `Hidden/lilToonURP/Shoost/...`
- uniform 先保留 `_Intensity`、`_Angle`、`_Mode` 等原名。
- 文件注释里写来源：
  - Shoost setting class
  - Cpp2IL ISIL 文件
  - RenderDoc asm 文件
  - 外部包源码文件

等效果对齐后，再考虑统一命名和整理公共 include。

# Material Gradient Texture

## 定位

`MaterialGradient` 是 lilToon URP Extensions 里的材质编辑增强模块。它不改变 shader 的运行时契约，也不要求材质仓库保存 `Gradient` 类型数据。材质、RenderFeature shader 和后处理 shader 仍然只接收普通 `Texture2D` ramp 图。

URP Extensions 安装后，指定的 ramp/gradient 贴图属性可以在材质 Inspector 中显示成 Unity `GradientField`。编辑器会把 Gradient 烘焙成一张 `256x1` 的 `Texture2D`，并把这张贴图作为 `.mat` 子资源折叠保存在材质下面。

## 依赖方向

- 材质仓库只声明普通贴图属性和采样逻辑。
- URP Extensions 提供 Editor-only 的 Gradient UI、贴图烘焙、材质子资源清理。
- Runtime shader 只复用 `HoMaterialGradientSampling.hlsl` 里的采样函数。
- 构建、VRC 上传和运行时看到的都是普通贴图，不依赖 Editor 脚本。

这个方向允许多个材质仓库共享同一个编辑器增强，也允许 URP Extensions 自己的 RenderFeature shader 复用 ramp 采样工具。

## 文件规划

```text
Editor/MaterialGradient/
  HoGradientTextureDrawer.cs
  HoMaterialGradientShaderGUI.cs
  HoMaterialGradientEditorApi.cs
  HoMaterialGradientPropertyGui.cs
  HoMaterialGradientPresetLibrary.cs
  HoMaterialGradientPresetStore.cs
  HoMaterialGradientTextureBaker.cs
  HoMaterialGradientPropertyRules.cs
  HoMaterialGradientDebounce.cs

Runtime/MaterialGradient/Shaders/
  HoMaterialGradientSampling.hlsl
```

## Shader 属性约定

推荐让 shader 属性保持普通 `2D` 贴图：

```shaderlab
_ShadowRampTex ("Shadow Ramp", 2D) = "white" {}
_RimGradientTex ("Rim Gradient", 2D) = "white" {}
```

`HoMaterialGradientShaderGUI` 会把以下 Texture 属性当成可编辑 ramp：

- 属性名或显示名包含 `ramp`
- 属性名或显示名包含 `gradient`

如果只想标记单个属性，也可以使用 drawer attribute：

```shaderlab
[HoGradientTexture] _ShadowRampTex ("Shadow Ramp", 2D) = "white" {}
```

## Inspector 接入

手写 shader 可以加：

```shaderlab
CustomEditor "lilToon.URP.Extensions.Editor.MaterialGradient.HoMaterialGradientShaderGUI"
```

ShaderGraph 可以在 Graph Inspector 的 Custom Editor GUI 中填入：

```text
lilToon.URP.Extensions.Editor.MaterialGradient.HoMaterialGradientShaderGUI
```

没有 URP Extensions 时，shader 属性仍然是普通 `Texture2D`。有 URP Extensions 时，匹配属性会自动显示成 Gradient 编辑器，并回写到贴图槽。

如果材质仓库已经有自己的 `ShaderGUI`，不要强行替换整个 Inspector。可以在自己的 UI 里调用：

```csharp
using lilToon.URP.Extensions.Editor.MaterialGradient;

if (!HoMaterialGradientEditorApi.TryDrawGradientTextureLayout(property, new GUIContent(property.displayName), materialEditor))
{
    materialEditor.ShaderProperty(property, property.displayName);
}
```

## Project Presets

项目级 ramp 预设保存在：

```text
Assets/HoMaterialGradient/Editor/Presets/HoMaterialGradientPresetLibrary.asset
```

Inspector 里的 `Project Preset`、`Preset Name`、`Save`、`Delete` 操作都会读写这个 asset。这个路径在项目 `Assets` 下，可以进版本管理；不要依赖 Unity Gradient picker 自带的用户级 preset 菜单。

`Interpolation` 下拉使用英文显示：

```text
Blend
Fixed
Perceptual Blend
```

## HLSL 使用

URP shader 中 include：

```hlsl
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/MaterialGradient/Shaders/HoMaterialGradientSampling.hlsl"
```

声明和采样：

```hlsl
TEXTURE2D(_ShadowRampTex);
SAMPLER(sampler_ShadowRampTex);

half3 rampColor = HoSampleGradientRgb(TEXTURE2D_ARGS(_ShadowRampTex, sampler_ShadowRampTex), ndotl);
```

`ndotl` 或其他输入值会被 `saturate` 到 `0..1`，采样坐标为 `(t, 0.5)`。

## Demo Material

包内提供一个最小测试材质：

```text
Runtime/MaterialGradient/HoMaterialGradientDemo.mat
```

测试步骤：

1. 把 `HoMaterialGradientDemo.mat` 挂到 Quad、Plane 或有 UV 的 Mesh 上。
2. 在 Inspector 里编辑 `Demo Ramp`。
3. 展开材质资产，确认材质下面有 `HoGradientTexture` 子贴图。

Demo shader 使用隐藏路径 `Hidden/lilToon/URP/MaterialGradient/Demo`，材质直接引用它。`Ramp Axis` 用于在 U/V 方向之间切换，`Ramp Power` 用于测试 ramp 输入值变化。这个 demo 使用 `HoMaterialGradientShaderGUI` 自动识别 `Demo Ramp`。

## 生成和清理

编辑 Gradient 后，模块会：

1. 在当前材质资产下查找对应的生成贴图。
2. 不存在时创建 `256x1 Texture2D` 子资源。
3. 根据 Gradient keys 写入像素。
4. 按 Gradient mode 设置滤波：`Blend` 使用 `Bilinear`，`Fixed` 使用 `Point`。
5. 把贴图赋回材质的原始 Texture 属性。

Inspector 右侧的 `Clean` 会删除该材质下没有被当前 ramp 属性引用的生成贴图，避免旧 ramp 子资源残留。

## VRC 和构建

这个模块不是运行时脚本。上传或构建时，材质引用的是已经烘焙好的 `Texture2D` 子资源。正常情况下 AssetBundle 会把它作为材质依赖一起打包。

如果第三方转换器、导出器或 Quest 专用流程不能正确处理 `.mat` 子资源，再额外做“导出为 PNG/TGA”的工具。默认流程不需要把每个 ramp 都保存成独立图片文件。

## 后续扩展

- 增加导出外部 PNG 的菜单，用于兼容特殊工具链。
- 增加项目级规则资产，允许每个材质仓库配置自己的属性匹配规则。
- 增加批量扫描材质并清理/重建 ramp 子贴图的工具。

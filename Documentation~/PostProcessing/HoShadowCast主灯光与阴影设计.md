# HoShadowCast 主灯光与阴影设计

本文记录 HoShadowCast 第一版设计。它用于补齐项目里“必须产生 shadowcast 的少量光源”，并作为后续 SSRTS 与 HTrace AO 的上游语义层。

## 一句话分工

```text
HoShadowCast = 少量确定性 shadow map 光源
SSRTS        = 次级屏幕空间阴影和接触关系
HTrace AO    = 剩余环境暗部和兜底遮蔽
```

HoShadowCast 不替代 URP 光照，也不接管所有灯。它只回答“哪些光必须产生可控阴影”，并输出一组项目自己的 shadow atlas 和 light data，供后续阴影合成或 SSRTS 使用。

## 管理方式

组件不挂在 Light 上，而是挂在一个空物体上集中管理。推荐组件名：

```text
HoShadowCastController
```

第一版字段建议：

```text
主天光 / Main Sun Light: Light
同步为 Unity Sun Source: bool

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

## 主天光语义

URP 内部有 Main Light / Sun Source 语义：

```text
RenderSettings.sun 指定 Directional Light 时，URP 优先把它当主光。
没有 Sun Source 时，URP 会在可见 Directional Light 里选最亮的一个。
Point Light 和 Spot Light 不会成为 URP Main Light。
```

HoShadowCastController 应提供“主天光”字段，并允许显式同步到 Unity Sun Source：

```text
主天光不为空时：
- 必须是 Directional Light。
- 自动占用 Directional slot 0。
- HoShadowCast、SSRTS 和 HTrace 相关方向语义都优先使用它。
- 如果“同步为 Unity Sun Source”开启，则设置 RenderSettings.sun = 主天光。
```

同步开关必须显式暴露，因为 `RenderSettings.sun` 是场景全局状态，会影响 URP Main Light、天空盒太阳方向以及部分环境光和反射语义。

## 光源容量

第一版目标容量：

```text
4 Directional Lights
4 Spot Lights
4 Point Lights
```

对应 shadow slice 数：

```text
Directional: 4 slices
Spot:        4 slices
Point:       24 slices, each point light uses 6 cube faces
Total:       32 slices
```

项目当前不优先考虑性能，容量可以按这套上限设计。实际场景通常不会填满；如果将来需要控制开销，可再增加 caster scope 或区域约束。

## 第一版阴影形态

第一版先做硬阴影，不深入做软阴影。

```text
Directional:
  每个方向光一个正交 shadow slice。
  先不做 cascade。
  范围由 Controller 的 directional shadow size / near / far 控制。

Spot:
  每个聚光一个 perspective shadow slice。

Point:
  每个点光 6 个 cube face slice。
```

因为 URP additional shadow atlas 不支持 additional directional shadow，HoShadowCast 不应依赖 URP 内置 additional shadow 机制来表达这套设计。它应渲染项目自己的 shadow atlas。

## 渲染架构

推荐模块：

```text
HoShadowCastController
HoShadowCastLightRegistry
HoShadowCastRendererFeature
HoShadowCastShadowPass
HoShadowCastComposite / SSRTS Bridge
```

每帧流程：

```text
1. Registry 找到当前场景启用的 HoShadowCastController。
2. Controller 收集主天光、方向光、聚光、点光引用。
3. RendererFeature 从 UniversalLightData.visibleLights 中匹配这些 Light。
4. ShadowPass 按固定容量渲染 _HoShadowCastAtlas。
5. 写入全局 shadow/light 数据。
6. 后续 fullscreen shadow composite 或 SSRTS 读取这些数据。
```

建议全局资源：

```text
_HoShadowCastAtlas
_HoShadowCastLightCount
_HoShadowCastWorldToShadow[32]
_HoShadowCastLightData[12]
_HoShadowCastShadowParams[32]
```

数组大小可以按 `4 + 4 + 4 lights` 和 `32 slices` 固定，先避免动态 buffer 带来的调试复杂度。

RenderGraph 注意事项：

```text
每个 shadow slice 需要自己的 RendererList。
不要在同一帧重复执行同一个 RendererListHandle。
```

这是为了避开 Unity RenderGraph 的限制：同一个 RendererList 一帧内执行多次会报错。

## 材质接入

材质侧第一版不增加接收排除、不增加阴影接收强度，也不增加 lilToon / lilPBR 面板参数。

原因：

```text
HoShadowCast 是打暗设计，不是带颜色的发光或局部材质特效。
接收侧统一由后处理 / SSRTS 合成处理。
材质只需要作为 caster 正确写 shadow depth。
```

lilToon 和 lilPBR 接入方式：

```text
lilToon: 使用现有 URP ShadowCaster pass。
lilPBR: 使用现有 URP ShadowCaster pass。
```

不要用 override material 去替代材质自己的 ShadowCaster pass。cutout、dither、dissolve、透明裁剪、双面和 wind 等语义应由材质自己的 ShadowCaster pass 处理。没有 ShadowCaster pass 的材质第一版就不参与 HoShadowCast。

## 接收与合成

接收侧不走材质 UI。第一版合成可以按屏幕空间统一打暗：

```text
receiver pixel = 当前可见像素
world position = camera depth 或 HoAOV depth 重建
shadow = sample HoShadowCast atlas
color *= lerp(1, darken, shadow * strength)
```

是否使用 HoAOV 参与 receiver 语义，留给 SSRTS / composite 阶段决定。第一版不做材质级“接收排除”。

## 与 SSRTS / HTrace 的关系

HoShadowCast 只负责确定性强光阴影。

```text
HoShadowCast:
  必须存在、必须稳定、需要由少量关键光源投出的 shadowcast。

SSRTS:
  使用 HoShadowCast 的主天光和少量光源数据，补次级阴影、接触阴影和屏幕空间关系。

HTrace AO:
  处理剩余环境遮蔽，不追求逐光源精确投影。
```

不要把多光源阴影继续塞进 HoCharacterSpecialization 的前发 DropShadow。DropShadow 仍然是角色特化的局部屏幕空间效果；HoShadowCast 是项目级阴影光源系统。

## 预留但第一版不做

以下能力结构上可以预留，但第一版不实现：

```text
Caster scope / region
某些 caster 只被附近几个标记光影响
基于 AOV 或 group id 的 caster 分组
软阴影质量分级
Directional cascade
材质级 receiver 排除
```

第一版优先目标是跑通 Controller、shadow atlas、debug view 和后续 SSRTS 可消费的数据格式。

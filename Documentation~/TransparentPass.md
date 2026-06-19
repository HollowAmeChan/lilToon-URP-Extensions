# Ho-Transparent

`HoTransparentRendererFeature` 是一个通用的 URP 透明物体绘制调度器。
它不会绑定到宝石、玻璃、毛发或某一个具体 Shader，而是在透明阶段按配置顺序绘制指定 `LightMode` 的 Shader Pass。

默认配置会依次绘制：

1. `HoTransparentBackface`
2. `HoTransparentFrontface`

这个默认顺序用于解决双面透明物体最常见的问题：先画背面，再画正面。
默认渲染队列范围从 `AlphaTest + 1` 开始，因此队列早于 Unity 标准 `Transparent` 的 lilToon 透明材质也可以直接参与，不需要额外改队列。

## 使用方式

在 URP Renderer Data 上添加 `Ho-Transparent` Renderer Feature。
启用后，Feature 会在透明阶段查找材质 Shader 中匹配的 `LightMode`，并按列表顺序绘制。

默认列表适合双面透明：

- `HoTransparentBackface`：建议 Shader Pass 使用 `Cull Front`，只绘制背面。
- `HoTransparentFrontface`：建议 Shader Pass 使用 `Cull Back`，只绘制正面。

默认 `Activate Pass Event` 早于 URP 普通透明物体绘制，`Draw Pass Event` 位于透明阶段。
如果 Shader 用 `_HoTransparentActive` 跳过自己的普通 `UniversalForward` Pass，不要把激活事件改到普通透明物体之后，否则会出现普通透明和 `Ho-Transparent` 重复绘制。

如果项目后续有其他透明需求，也可以在同一个 Feature 里追加新的 `LightMode`，例如特殊深度预写、透明描边或自定义排序层。

## Shader 约定

Shader 需要主动添加匹配的 `LightMode` Pass：

```shaderlab
Pass
{
    Name "HoTransparentBackface"
    Tags { "LightMode" = "HoTransparentBackface" }
    Cull Front
    ZWrite Off
    Blend One OneMinusSrcAlpha
}

Pass
{
    Name "HoTransparentFrontface"
    Tags { "LightMode" = "HoTransparentFrontface" }
    Cull Back
    ZWrite Off
    Blend One OneMinusSrcAlpha
}
```

宝石、火彩、高光这类包含亮部增量的材质，建议使用预乘 alpha 输出。
也就是 Shader 里把透射/基底颜色乘以 alpha，反射、高光、火彩等亮部作为增量保留，再配合 `Blend One OneMinusSrcAlpha`。
如果直接用 `Blend SrcAlpha OneMinusSrcAlpha`，背面和正面都会被同一个 alpha 压低，容易出现正面贡献很少、整体发灰或像被背面覆盖的问题。

启用 `Publish Active Flag` 时，Feature 会在透明 Pass 列表开始前把全局 `_HoTransparentActive` 设为 `1`，在透明阶段结束后重置为 `0`。
接入的 Shader 可以用这个标记跳过自己的普通 `UniversalForward` 透明 Pass，避免同一个材质被常规透明流程和 `Ho-Transparent` 重复绘制。

## 和 OIT 的关系

`Ho-Transparent` 不是 OIT resolve。
它只负责“哪些透明 Shader Pass 被画出来”以及“按什么顺序画”。

Weighted OIT 仍然应该保持为独立 Feature，因为它改变的是透明片元的累积和合成方式。
一个 Shader 可以同时提供 `HoTransparentBackface` / `HoTransparentFrontface` Pass 和 OIT Pass，但同一个材质在同一个相机里通常应该只选择一条 resolve 路径，避免重复混合。

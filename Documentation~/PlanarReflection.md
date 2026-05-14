# 平面反射

`LILPlanarReflectionSurface` 会为平面反射表面渲染一个镜像相机，并通过 `MaterialPropertyBlock` 把结果传给对应 renderer。

## 设置

1. 把 `LILPlanarReflectionSurface` 加到平面反射 mesh 上。
2. 使用会采样 `_LILPBRPlanarReflectionTexture` 的材质，例如修改后的 lilPBR shader。
3. 调整材质里的 `Planar Reflection` 强度、tint、smoothness threshold、edge fade 和 distance fade。
4. 把组件上的 `Reflection Mask` 设置为需要出现在反射里的层。

反射平面使用当前 GameObject 的 transform：

- 位置：`transform.position`
- 法线：`transform.up`

## 说明

- 渲染反射时，表面自身的 renderer 会临时隐藏，避免递归自反射。
- 反射纹理和投影矩阵通过 `MaterialPropertyBlock` 写入，所以每个表面都可以拥有自己的反射。
- 默认情况下，组件也会通过同一个 property block 为该 renderer 启用 lilPBR 的 `_UsePlanarReflection` 属性。
- 反射相机会使用源相机的 projection，包括 FOV / focal length，只额外把 `Clip Plane Offset` 应用到 oblique clipping plane。
- lilPBR 用当前相机的 screen UV 采样平面反射。对投影匹配的镜像相机来说，这是稳定的映射方式。
- 在 lilPBR 中，`Fade Start` 和 `Fade End` 提供基于相机距离的淡出。两者都保持 `0` 时禁用距离淡出。
- 这条路径适合平面镜、抛光地面、水面片以及类似的平面反射表面。
- 它比 screen-space reflection 更贵，因为需要把场景再渲染一遍。

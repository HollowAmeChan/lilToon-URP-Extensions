# HoShadowCast 踩坑记录

## 2026-05-18 atlas 分块与 receiver 不一致

这次问题非常隐蔽：atlas debug 看起来已经正确写入，但材质侧完全没有投影。最终确认不是 caster、RendererList、lightData 或强度问题，而是生成侧 atlas 分块修复后，receiver 侧的矩阵与 tile 映射没有稳定跟上。

必须同时满足以下规则：

1. atlas allocator 必须以 `ref` 共享给所有灯光和所有 slice。`HoShadowCastAtlasPacker` 是 struct，如果按值传入 `AddLightArray` / `AddLight`，每盏灯都会从 `(0,0)` 重新分配，导致天光、点光、第二个点光互相覆盖。
2. receiver 不要依赖“已经烘进 atlas offset 的矩阵黑盒”。CPU 输出 slice-local `worldToShadow`，shader 再用 `_HoShadowCastSliceData.xy/z` 显式计算 `atlasUV = sliceOffset + localUV * sliceScale`。
3. `_HoShadowCastWorldToShadow` 不再直接用 matrix array 上传给 shader。已改为 4 组 `Vector4[]` row 上传，并在 HLSL 里用 dot 手动乘。这个路径和 light/slice data 一样稳定，避免 RenderGraph/平台矩阵数组布局问题。
4. Unity light/camera view 约定是看向本地 `-Z`。手写 directional/spot/point fallback view matrix 时必须用 `Quaternion.LookRotation(-forward, up)`，不能用 `LookRotation(forward, up)`。后者会让 xy 似乎进了 slice，但 z/depth compare 永远不成立。
5. RenderGraph shadow pass 内不能对当前正在作为 depth attachment 写入的 atlas 调 `SetGlobalTexture`。应使用 `builder.SetGlobalTextureAfterPass(atlasTexture, _HoShadowCastAtlas)` 在 pass 结束后发布给 receiver。
6. `Light.shadows == None` 仍允许 HoShadowCast 投影。receiver 强度不能直接使用可能为 0 的 `light.shadowStrength`，当前规则是 Shadows None 时按 1 处理，再乘 controller 的 `shadowStrength`。

## 推荐排查顺序

如果以后再次出现“atlas 有深度但 receiver 没效果”：

1. 先看 console 日志：`lights / slices / lightSlices=[name:type@first+count*strength]`。如果 strength 是 0，先查 controller 或 Light 阴影强度。
2. 用临时 receiver 固定暗化确认材质接入是否执行。若固定暗化无效，问题在 lilToon/lilPBR include 或 shader variant。
3. 用灯光 influence 调试确认 point/spot range 和 lightData 是否可见。若 influence 有效，说明 receiver loop 通。
4. 用 bounds 调试只检查 `worldToShadow.xy` 是否进 `0..1`。若 bounds 有效但 Off 无效，问题在 z/depth compare。
5. 若 bounds 无效，优先查矩阵上传和 Unity `-Z` view 约定，不要先怀疑 atlas 写入。

## 2026-05-18 point/spot receiver 衰减

点光和聚光不能只用“范围内 1、范围外 0”的硬 influence。HoShadowCast 最终是乘到材质 receiver 上的额外投影，如果 point/spot 不按 `Light.range` 淡出，阴影会在光照边界突然截断，看起来像 receiver 或 atlas 抖掉了一块。

当前规则：

1. CPU 为每盏 punctual light 上传 `_HoShadowCastLightAttenuation`。`x` 是 `1 / range^2`，`zw` 是 spot 内外锥角的 `scale/offset` 参数。
2. HLSL 侧先用 `rangeFactor = distanceSqr / range^2` 算平滑淡出，只把这个 0..1 fade 乘进 shadow strength，不使用光照的 `1 / distance^2` 亮度衰减。这里控制的是“投影影响范围”，不是重算灯光亮度。
3. spot 额外乘 cone fade，使用 `Light.innerSpotAngle` 到 `Light.spotAngle` 的软边。这样聚光边缘和 range 边缘都会自然退掉。
4. directional light 不走这套衰减，始终是全局 influence 1。

## 2026-05-18 receiver 消费模型

不要把 HoShadowCast 当作最终颜色上的全局乘法。主光阴影只影响主光直射项，环境光、GI、暗部色和很多材质内的补光逻辑不会一起被压暗；如果 HoShadowCast 直接乘 `fd.col.rgb` 或整包 `diff/spec`，颜色会绕开材质自身阴影模型。

当前消费规则：

1. 材质消费端统一使用 `HoShadowCastAttenuation(positionWS)`，把 directional / point / spot 的投射结果作为一个 receiver shadow field 混进主光 shadow attenuation。
2. lilPBR 在 `GetMainLight` 后乘 `mainLight.shadowAttenuation`，lilToon 在 `LIL_LIGHT_ATTENUATION` 宏里乘主光实时阴影。这样 HoShadowCast 会继续走 lilPBR / lilToon 原本的阴影模型。
3. lilPBR additional lights / subsurface additional lights，以及 lilToon `addLightColor` / HDR additional light，会再乘一次同一个 HoShadowCast attenuation 作为 brightening gate，避免额外补光把 cast 暗区重新提亮。
4. 没有 HoShadowCast atlas、没有 light 或没有 slice 时，采样函数必须返回 1，使 brightening gate 完全不影响普通多光源 HDR 颜色。
5. 多盏 HoShadowCast 灯之间允许相乘变暗，因为开启 cast 的目的就是额外压暗接收面；但统一 receiver 输出有最终暗部下限，避免多盏灯叠到死黑。
6. `HoShadowCastDirectionalAttenuation` / `HoShadowCastPunctualAttenuation` 保留为分组函数和调试入口，不再由材质消费端分别压主光与附加光。

## 保留的最终修复

最终代码只保留生产路径：

- 共享 packer，避免 slice 覆盖。
- slice-local matrix + shader 显式 atlas tile 映射。
- world-to-shadow row vector 上传。
- 手写 view matrix 使用 `-forward`。
- point/spot receiver 按 `Light.range` 与 spot 内外锥角做淡出。
- directional / point / spot receiver 统一混进主光阴影入口，由材质自身阴影模型消费。
- Atlas debug 只用于看 `_HoShadowCastAtlas` 本体，receiver 不保留临时调试模式。

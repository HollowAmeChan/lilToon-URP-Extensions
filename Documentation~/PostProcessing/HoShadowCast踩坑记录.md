# HoShadowCast 踩坑记录

> 历史资料：本文保留旧 `HoShadowCast` 开发排障记录，不作为当前 `Ho-ShadowCast` 使用说明。当前 ShadowCast 行为以 `../RPComponentRework/06-ShadowCast改造计划.md`、用户顺序以 `../RPComponentRework/07-用户向RendererFeature使用与顺序.md`、完成计划以 `../RPComponentRework/09-重构完成计划.md` 为准。

## 2026-05-18 atlas 分块与 receiver 不一致

这次问题非常隐蔽：atlas debug 看起来已经正确写入，但材质侧完全没有投影。最终确认不是 caster、RendererList、lightData 或强度问题，而是生成侧 atlas 分块修复后，receiver 侧的矩阵与 tile 映射没有稳定跟上。

必须同时满足以下规则：

1. atlas allocator 必须以 `ref` 共享给所有灯光和所有 slice。`HoShadowCastAtlasPacker` 是 struct，如果按值传入 `AddLightArray` / `AddLight`，每盏灯都会从 `(0,0)` 重新分配，导致天光、点光、第二个点光互相覆盖。
2. receiver 不要依赖“已经烘进 atlas offset 的矩阵黑盒”。CPU 输出 slice-local `worldToShadow`，shader 再用 `_HoShadowCastSliceData.xy/z` 显式计算 `atlasUV = sliceOffset + localUV * sliceScale`。
3. `_HoShadowCastWorldToShadow` 不再直接用 matrix array 上传给 shader。已改为 4 组 `Vector4[]` row 上传，并在 HLSL 里用 dot 手动乘。这个路径和 light/slice data 一样稳定，避免 RenderGraph/平台矩阵数组布局问题。
4. Unity light/camera view 约定是看向本地 `-Z`。手写 directional/spot/point fallback view matrix 时必须用 `Quaternion.LookRotation(-forward, up)`，不能用 `LookRotation(forward, up)`。后者会让 xy 似乎进了 slice，但 z/depth compare 永远不成立。
5. RenderGraph shadow pass 内不能对当前正在作为 depth attachment 写入的 atlas 调 `SetGlobalTexture`。普通 atlas 和第二天光 atlas 都应使用 `builder.SetGlobalTextureAfterPass(...)` 在 pass 结束后发布给 receiver。
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

1. CPU 为每盏 punctual light 上传 `_HoShadowCastLightAttenuation`。`x` 是 `1 / range^2`，`y` 是 Controller 的点光/聚光范围衰减速度，`zw` 是 spot 内外锥角的 `scale/offset` 参数。
2. HLSL 侧先用 `rangeFactor = distanceSqr / range^2` 算平滑淡出，只把这个 0..1 fade 乘进 shadow strength，不使用光照的 `1 / distance^2` 亮度衰减。这里控制的是“投影影响范围”，不是重算灯光亮度。
3. 范围衰减速度默认 1，保持旧曲线；大于 1 会让阴影更快淡出，小于 1 会让影响范围内的阴影保持更久。
4. spot 额外乘 cone fade，使用 `Light.innerSpotAngle` 到 `Light.spotAngle` 的软边。这样聚光边缘和 range 边缘都会自然退掉。
5. directional light 不走这套衰减，始终是全局 influence 1。

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
- Atlas debug 只用于看 `_HoShadowCastAtlas` 或 `_HoShadowCastSecondDirectionalAtlas` 本体，receiver 不保留临时调试模式。

## 2026-05-18 第二天光多槽级联

“第二天光”不是单独再拖一个 Light，而是 Controller 上已有的 4 个额外方向光槽。每个有效方向光都会写入独立第二天光 atlas，并分配 `secondDirectionalCascadeCount` 个 cascade tile。

关键约束：

1. 普通 `_HoShadowCastAtlas` 不再写入方向光，只负责 spot / point，避免方向光级联挤占普通 atlas slice。
2. 第二天光 atlas 的固定容量是 `4 lights * 4 cascades = 16 slices`。C# 的 row/slice 数组、HLSL 的数组长度和 debug shader 的循环上限必须一起改，不能只改一边。
3. receiver 按相机距离为每盏方向光选择一个 cascade，只采样该 cascade，再把多盏额外方向光相乘。
4. Debug Mode 需要能在普通 atlas 和第二天光 atlas 之间切换。排查时先确认选的是正确 atlas，否则会误判为 AtlasRaw 空。
5. Controller 上遗留的单 slice 方向光字段只作为序列化兼容保留，不再暴露在 Inspector。当前方向光实际由“第二天光级联”栏控制。

## 2026-05-18 点光贴近墙面出现正方形亮块

现象：点光源参与 HoShadowCast 时，如果灯非常贴近墙壁或接收面，画面上可能出现一个近似正方形的亮块。它看起来不像正常的圆形点光衰减，也不像普通 shadow acne，而更像点光 cube face / shadow slice 的投影边界被直接显露出来。

已观察到的特征：

```text
1. 光源离墙越近越容易出现。
2. 亮块形状接近点光单个 cube face 的方形投影。
3. 这类问题可能在 PCSS 或较大 filter 半径下更明显，因为 blocker/filter 采样会把 tile 或 face 边缘问题放大。
4. 开启 PCSS 后，方形亮块的半径会明显变大。这不是另一个独立问题，而是 PCSS 在 near plane / cube face footprint 边界周围做 blocker search 和 variable filter，把原本较小的方形有效区扩散出来。
```

优先排查方向：

```text
1. 点光 near plane 过大或光源进入接收面，导致 cube face shadow projection 的近裁剪区域直接落在墙上。
2. 点光 receiver 只采当前 face，不跨 face；贴墙时 face 选择、face 边缘和 filter clamp 可能让方形 face footprint 变成可见亮块。
3. PCSS blocker search / filter radius clamp 在当前 tile 内，贴近表面时可能把大量采样压在 tile 边缘或空 depth 上，空 depth 又被视为 lit，形成亮块。
4. 点光 range 衰减和 shadow influence 是圆形的，但 shadow map slice 是方形透视投影；当灯贴墙时，两者不再自然掩盖 slice footprint。
```

临时规避：

```text
1. 不要把参与 cast 的点光贴到墙面或接收面内部，给 light position 留一点离墙距离。
2. 降低点光 PCSS 半径 / 最大半影半径，确认是否由软阴影采样放大。
3. 必要时增大点光 near plane / 调整 light range，观察方块是否随 cube face 投影变化。
```

后续修复候选：

```text
1. 已加近距离保护：PCSS 半径会在 shadowCoord.z 接近 near plane 时自动缩小，点光 receiver 距离 light 太近时也会缩小，最后退回 3x3 manual PCF。第一次阈值太保守，贴墙点光仍会放大方块；当前规则是点光 receiver 距离小于 light range 的约 10% 时基本不走 PCSS，10%-35% 之间逐步恢复。
2. 为点光 receiver 增加 face-edge fade，接近 cube face 边缘时减弱 shadow contribution。
3. PCSS 对点光增加更严格的有效深度判断，避免贴墙时把空 depth 当作大面积 lit filter。
4. 长期方案是点光 PCSS 支持跨 cube face 采样，但实现复杂，第一版先不做。
```

## 2026-05-18 PCSS raw depth 比较方向

从 hardware shadow compare 改成 raw depth + manual compare 后，必须显式处理 `UNITY_REVERSED_Z`。否则会出现第二天光黑白反相，或者 blocker search 按错误方向寻找遮挡物。

规则：

```text
1. 空 depth 仍然优先视为 lit，避免 atlas 未写入区域整片变黑。
2. 非 reversed-Z：rawDepth >= receiverDepth - bias 表示 lit；rawDepth < receiverDepth - bias 是 blocker。
3. reversed-Z：rawDepth <= receiverDepth + bias 表示 lit；rawDepth > receiverDepth + bias 是 blocker。
4. PCSS 的 penumbra 公式也要跟着反向：reversed-Z 用 averageBlockerDepth - receiverDepth。
5. 修改 compare 方向时必须同时改 blocker search，否则硬阴影看似对了，软阴影仍会乱。
```

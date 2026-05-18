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

## 保留的最终修复

最终代码只保留生产路径：

- 共享 packer，避免 slice 覆盖。
- slice-local matrix + shader 显式 atlas tile 映射。
- world-to-shadow row vector 上传。
- 手写 view matrix 使用 `-forward`。
- Atlas debug 只用于看 `_HoShadowCastAtlas` 本体，receiver 不保留临时调试模式。

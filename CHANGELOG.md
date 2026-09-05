# Changelog

## 0.2.0

- 角色特化：眼透新增**相机角度修正**。
  - `HoMetadataBufferGroup` 新增"面部朝向"（Transform + 脸前/右/上三轴枚举）与 `TryGetWorldFacing()` 世界朝向入口（可供 SDF 等后续消费者复用）。
  - 新增 `HoCharacterEyeAngleTable`：每个渲染相机一张 256×1 角度表，CPU 在 `AddRenderPasses` 时机按相机计算平转角/俯仰角（atan2 全角域）并绑定为全局纹理；多相机/双窗口各自正确，无判定、无模式分支。
  - `Composite` 新增 `ResolveEyeAngleFactor`：视锥内 1 / 视锥外 0（柔化过渡），仅作用于眼透不透明度，前发投影 receiver 与原始 revealMask 不受影响。
  - 参数（Settings + Volume）：启用相机角度修正、角度修正强度、平转半角范围（默认 90°）、俯仰半角范围（默认 60°）、角度柔化（默认 40°）；默认关闭，行为与旧版一致。
  - 调试模式：`EyeAngleFactor (16)`、`EyeAngleTable (17)`。
  - 文档：新增 `Documentation~/CharacterSpecialization_EyeReveal.md`。

## 0.1.0

- Added the Unity Package Manager manifest.
- Added runtime/editor assembly definitions.
- Added the initial Weighted OIT renderer feature entry point.

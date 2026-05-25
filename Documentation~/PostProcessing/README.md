# PostProcessing 历史资料

> 本目录保留旧 `HoAOV` / `HoPost` / `ShoostStack` / 早期 `HoShadowCast` 设计和排障记录，不作为当前 RendererFeature 使用说明。

当前架构和用户向说明以这些文档为准：

- `../RPComponentRework/07-用户向RendererFeature使用与顺序.md`
- `../RPComponentRework/08-PostProcess顺序与输入边界.md`
- `../RPComponentRework/09-重构完成计划.md`

当前用户侧主入口是 `ShadowCast`、`MetadataBuffer`、`GeometryBuffer`、`SSS`、`OIT`、`CharacterSpecialization`、`ScreenProcess`、`ImageProcess`。旧名只用于理解迁移历史和排查旧提交中的行为。

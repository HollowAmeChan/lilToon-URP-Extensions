# Ho SSS Volume

2026-11-23。Volume 的 Add Override 列表新增扁平入口 **Ho SSS**，标题与菜单一致，不带 Volume 后缀。

- Renderer 中仍需添加 `HoSubsurfaceScatteringRendererFeature`，并提供原有 AC/OB/SB/GeometryBuffer 输入。Volume 不会自动创建渲染 Feature。
- Volume 提供总开关、质量、渲染缩放、强度、扩散门控、材质配置表、未命中回退、透射及调试。场景视图开关控制整个效果。
- 左侧覆盖勾选使用标准 Volume 语义。未覆盖的字段保留 RendererFeature 原值；总开关可覆盖为开或关。禁用组件、权重为零或相机离开作用范围后按正常 Volume 栈回退。
- 数值与颜色沿用 Unity Volume 混合；枚举和材质配置表为离散选择。配置表最多使用前 8 项，按 Profile ID 匹配，整张表统一覆盖，避免插值产生中间 ID。
- 渲染时机与 Shader 仍在 RendererFeature，现有 Renderer 资产和材质/Profile ID 无需迁移。
- Inspector 使用统一折叠与悬停提示，不显示常驻信息框；缺失运行输入保留警告。

每相机先复制 Feature 兜底值，再应用当前 Volume 栈覆盖；工作区和 8 个材质配置对象复用。不会把 Volume 值写回 RendererFeature，也不把 Profile 表直接作为可变运行时数组。RenderGraph 与兼容路径的全部 SSS pass 都使用解析后的参数。

验证入口：`lilToon.URP.Extensions.Editor.SubsurfaceScattering.HoSubsurfaceScatteringVolumeValidation.Validate`（隔离 batch editor），以及公共 `HoVolumeUIValidation.Validate`。覆盖全部运行参数默认值/覆盖、启停、未覆盖/禁用回退、配置表深拷贝与序列化、相机栈切换、正常/调试 pass 入队与场景视图关闭；不代表已验收具体角色的最终 SSS 画面或性能。

# Ho Volume 面板规范

2026-11-23。适用于包内全部 18 个 Volume 组件，以及它们调用的图层编辑器。

## 名称与添加菜单

入口统一使用 `Ho ` 前缀、PascalCase 功能名，技术缩写保持大写；不添加 `Volume` 后缀。菜单在 Add Override 根列表平铺，不用 `/` 创建子菜单。

```text
Ho Atmosphere                 Ho AttributeComposite
Ho CharacterShadow            Ho CharacterSpecialization
Ho DebugHUD                   Ho GeometryData
Ho GTAO                       Ho ImageProcess
Ho ObjectBuffer               Ho PlanarReflection
Ho RT                         Ho RTDI
Ho RTGI                       Ho RTShadow
Ho ScreenProcess              Ho SSGI
Ho SurfaceBuffer              Ho VolumeLight
```

`VolumeLight` 是功能本名。DebugHUD 的序列化类型仍为 `HoDebugTileVolume`；ImageProcess、ScreenProcess 的序列化类型仍保留原有 `StackVolume` 名称。显示名调整不迁移脚本、GUID、字段或 Profile 数据。

名称集中定义在 `Runtime/HoVolumeNames.cs`。组件的 `VolumeComponentMenu` 和 Unity 6.3+ 的 `DisplayInfo.name` 引用同一个常量；旧 Unity 版本通过原有 `displayName` 机制提供相同标题。保留各组件已有的渲染管线与 Renderer Feature 适用性限制。

## 面板与说明

- 自定义 Volume Editor 继承 `HoVolumeComponentEditor`。自定义标签保留字段的 `Tooltip`，需要时再合并当前模式说明。
- 常用参数直接显示，必要分组使用 `HoVolumeEditorGUI.Section` 的紧凑原生折叠样式，不使用大块彩色标题或重复外框。高级和调试分组默认收起。
- “场景视图”“游戏视图”等通用标签保持一致。参数左侧仍是原生 Volume Override 勾选，不另做一套开关规则。
- 参数含义、覆盖规则、预设行为和调试通道图例放在相应控件的 tooltip 中，不绘制常驻 Info/None 说明框，也不换成占空间的长段 Label。
- 只有当前配置确实需要处理时绘制 Warning/Error。例如已选目标失效、映射后输入与 HDR 发光阈值冲突、启用后处理预览但 Scene View 后处理关闭。
- ImageProcess、ScreenProcess、CharacterSpecialization 保留原有效果浏览器、图层顺序、预设与操作方式。修改嵌套提示时同步调整行数/元素高度，避免留下空白或遮挡后续图层。

本轮只整理 Volume 显示层；Renderer Feature Inspector 和场景组件面板不受公共样式修改影响。渲染、质量预算、默认数值和 Volume 混合语义不在此处改变。

## 验证

执行 `HoLil/Validation/Validate Volume UI` 可检查包内所有 Volume 的菜单和标题一致、入口不重复且平铺、编辑器使用公共 tooltip 逻辑，以及创建编辑器不会改变组件序列化默认值。添加新组件时应同时补充名称常量和相应 Editor。

本轮额外在隔离工程中保存修改前的 18 个组件默认值及一份包含混合 Override 状态的 Profile，并在更新脚本后重新加载逐项比较。测试不读写用户场景和正式 Volume Profile。隔离验证记录位于 `Tests~/VolumeUI/20261123`。

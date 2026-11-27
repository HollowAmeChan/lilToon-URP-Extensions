# Ho Shadows 接收编译检查

在两个独立的 Unity 6000.3 工程中运行。一个安装发布候选 HoURP，另一个安装 Unity 官方 URP 17.3。两侧使用同一批发布候选的材质包，把 `HoShadowAbiCompileProbe.cs` 复制为 `Assets/Editor/HoShadowAbiCompileProbe.cs`。

设置 `HO_SHADOW_ABI_OUTPUT` 为新建的输出目录，设置 `HO_SHADOW_ABI_PROVIDER` 为 `managed` 或 `stock`，在 D3D11 Editor 执行 `HoShadowAbiCompileProbe.Run`。可用 `HO_SHADOW_ABI_PACKAGES` 限定 `Hair,Skin,Eye,Cloth,Environment,Effect` 中的参与者。

报告保存 DXBC、实际声明的资源、编译诊断与每次请求时间。两个工程首次导入和缓存状态可能不同。比较编译时间时同时记录缓存状态。受管不透明、角色 Capture、已覆盖的 early／late 和 Cloth OIT 验证屏幕资源。标准 URP 分支与明确的原生特例验证原生图集。发布 Shader 无旧材质预览关键词和独立 Ho 图集绑定。

# Ho Shadows 生产迁移 dry-run

`plan_migration.py` 读取磁盘上的 Renderer、URP 资产、Volume、灯与角色声明、材质和 Variant，并扫描舞台工具及序列化属性覆盖。它只向标准输出写 JSON，可以在关闭或运行 Unity 时使用。运行需要 Python 3.9+、`rg` 和 Git。

```powershell
python -B 'Tools~/HoShadows/plan_migration.py' `
  --project 'D:/Unity_Project/BREAK_URP' `
  --hoshader 'D:/Unity_Fork/HoShader' > 'D:/Temp/ho-shadows-migration-plan.json'
```

- `renderer.features` 按实际顺序记录 Feature、fileID、GUID、启用状态和建议。生产 Renderer 继续用旧 Character Feature 提供图集。隔离试切开启托管角色图集后，才在副本中停用旧 Character Feature。
- `materialRecords` 记录旧 Main／Full 的有效接收、属性来源、Scene／Prefab 引用和特殊 Pass。只有当前可能接收附加投影、可链接到记录有投影 Point／Spot 的场景、且通过普通材质门控的旧 Main 被列为 `review_full_candidate`。确认实际受光后，可在后续迁移中把该项设为 Full。无确定受光证据、Variant／锁、透明、Capture 和 OIT 分别核验。
- `volumeProfiles`、`settingsAndComponents` 与 `scriptReferences` 带出处。默认托管 RT 关闭，具名 RT 镜头另行验证 opt-in。保留投射、接收、角色边界和 Capture owner 的作者配置。

报告包含后续写入前可重查的 GUID 和 SHA-256。Scene 链接只反映已保存引用，嵌入灯计数只用于筛选复查场景。相机可见性、导入模型的所有绑定、动画或脚本写入和未保存编辑需要单独验证。

## 隔离试切

将 `HoShadowsPilotCutover.cs` 复制到物理试验工程的 `Assets/Editor`，并接入实现统一 Feature 的当前包。项目与单独的空输出目录都须在 `D:/Temp` 下。入口拒绝生产路径、junction、symbolic link 和有多个硬链接的目标文件。它将原 Renderer 与 Volume 文件及其 .meta 复制到输出目录，再配置隔离 Renderer。

```powershell
$env:HO_SHADOWS_P5_PILOT_PROJECT = 'D:/Temp/HoShadows-P5/SceneSnapshot'
$env:HO_SHADOWS_P5_PILOT_OUTPUT = 'D:/Temp/HoShadows-P5/CutoverBackup'
New-Item -ItemType Directory -Path $env:HO_SHADOWS_P5_PILOT_OUTPUT
unity run $env:HO_SHADOWS_P5_PILOT_PROJECT --editor-version 6000.3.15f1 `
  --log-file 'D:/Temp/HoShadows-P5/pilot.log' -- `
  -nographics -executeMethod HoShadowsPilotCutover.Run
```

试切开启托管角色图集、透明 Eye、加权 OIT 和 Capture 输入，并关闭 RT。旧 Feature 作为原 Renderer subasset 留存并停用。角色图集沿用旧分辨率、容量和深度／法线偏移，滤波使用托管路径的 PCF。统一 Feature 插在原有输入生产端之后、Capture 和 OIT 之前。材质和默认 Volume 保持原样，镜头测试可按 dry-run 清单选取。

`pilot-before.json` 和 `pilot-applied.json` 记录文件哈希和逐项前后索引及本地 fileID。回放旧基线时，关闭隔离 Editor，再把备份的 Renderer 与 Volume 文件及其 `.meta` 复制回副本的 `Assets/Settings`。

## 真实镜头的隔离对照

把 `HoShadowsSceneCutoverComparison.cs` 与试切工具一起复制到物理快照的 `Assets/Editor`。快照包含保存的羽啾与圣诞万花镜场景和各自依赖。重新使用尚未试切的 Renderer，设置两个空输出目录，以 D3D11 启动 Unity。

```powershell
$env:HO_SHADOWS_P5_PILOT_PROJECT = 'D:/Temp/HoShadows-P5-Local/SceneSnapshot'
$env:HO_SHADOWS_P5_PILOT_OUTPUT = 'D:/Temp/HoShadows-P5-Local/CutoverBackup'
$env:HO_SHADOWS_P5_COMPARE_OUTPUT = 'D:/Temp/HoShadows-P5-Local/SceneComparison'
New-Item -ItemType Directory -Path $env:HO_SHADOWS_P5_PILOT_OUTPUT,$env:HO_SHADOWS_P5_COMPARE_OUTPUT
unity run $env:HO_SHADOWS_P5_PILOT_PROJECT --editor-version 6000.3.15f1 --timeout 900 --no-tail `
  --log-file "$env:HO_SHADOWS_P5_COMPARE_OUTPUT/unity.log" -- `
  -force-d3d11 --burst-disable-compilation -executeMethod HoShadowsSceneCutoverComparison.Run
```

以 640×360 渲染两个已保存的 Base Camera。先读两次旧路径，再用同一快照跑新 Feature 的 4 和 16 个逐灯预算。受管预览只在本次绘制选中的 Skin、Eye、Cloth 和 Environment 材质上临时启用，把其中旧 Main 且开启阴影响应的材质临时设为 Full，退出当前镜头时恢复。可用 `HO_SHADOWS_P5_COMPARE_FILTER` 按场景路径选择子集，或用 `HO_SHADOWS_P5_COMPARE_SCENE` 指定快照内另一场景的 `Assets/...unity` 路径。默认控制平面反射、相机后处理和 AA，设 `HO_SHADOWS_P5_COMPARE_AUTHORED=1` 可以沿用保存的相机效果配置。

| 默认控制条件，D3D11 | 预览材质／临时转 Full | 四灯预算的 RGB 最大分量差 | 旧路径重复波动 | 阈值 `>0.002` 且旧重复稳定的差异像素 |
| --- | ---: | ---: | ---: | ---: |
| 羽啾 | 13／11 | 0.00001324 | 0.00001324 | 0 |
| 圣诞万花镜 | 19／0 | 0.00280380 | 0.00280380 | 0 |

圣诞万花镜的两路预算各有 9 个像素超过阈值，旧路径重复也有 9 个。此快照中该镜头没有启用阴影响应的 Main 材质临时转为 Full，逐灯预算在实际受光材质上的影响还应使用 Hiro 测试场景验证。`scene-cutover.json` 里的时间来自同步提交和读回，包含 Editor 开销。正式帧预算用 GPU 帧计时检查。

保留保存的相机效果配置重跑后，默认四灯预算下两场景仍各有 0 个超过阈值且旧重复稳定的差异像素。羽啾最大差为 0.00055987，圣诞万花镜为 0.00390625，均和各自的旧路径重复波动一致。圣诞万花镜的 16 灯对照最大差为 0.01583862。样本覆盖这两个保存镜头，生产仍需核对 Hair、Effect 等实际使用的其他阴影接收路径，以及 RT 和更高逐灯预算的镜头。

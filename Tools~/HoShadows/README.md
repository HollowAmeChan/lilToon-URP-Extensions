# Ho Shadows 生产迁移 dry-run

`plan_migration.py` 读取磁盘上的 Renderer、URP 资产、Volume、灯与角色声明、材质和 Variant，并扫描舞台工具及序列化属性覆盖。它只向标准输出写 JSON，可以在关闭或运行 Unity 时使用。运行需要 Python 3.9+、`rg` 和 Git。

```powershell
python -B 'Tools~/HoShadows/plan_migration.py' `
  --project 'D:/Unity_Project/BREAK_URP' `
  --hoshader 'D:/Unity_Fork/HoShader' > 'D:/Temp/ho-shadows-migration-plan.json'
```

- `renderer.features` 按实际顺序记录 Feature、fileID、GUID、启用状态和建议。生产 Renderer 继续用旧 Character Feature 提供图集。隔离试切开启托管角色图集后，才在副本中停用旧 Character Feature。
- `materialRecords` 记录旧 Main／Full 的有效接收、属性来源、Scene／Prefab 引用和特殊 Pass。只有当前可能接收附加投影、可链接到记录有投影 Point／Spot 的场景、且通过普通材质门控的旧 Main 被列为 `review_full_candidate`。确认实际受光后，可在后续迁移中把该项设为 Full。无确定受光证据、Variant／锁、透明、Capture 和 OIT 分别核验。
- Hair 同时记录材质配方和自定义队列。BREAK 的 9 份透明 Hair 有 8 份在 `2000` 或 `2400` 绘制，按不透明阶段调度，迁移需保留当前排序。Effect 记录仅描边、关闭深度写入以及自定义混合等本体接收资格。
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

试切开启托管角色图集、透明 Eye、加权 OIT 和 Capture 输入，并关闭 RT。旧 Feature 作为原 Renderer subasset 留存并停用。角色图集沿用旧分辨率、容量和深度／法线偏移，滤波使用托管路径的 PCF。统一 Feature 插在原有输入生产端之后、Capture 和 OIT 之前。旧 RT 已绑定的 ray tracing 程序、命中材质、重建程序及有序 SIGMA pass 引用直接复制到统一 Feature，为具名镜头后续验证 opt-in 留存 Player 资源引用。材质和默认 Volume 保持原样，镜头测试可按 dry-run 清单选取。

设 `HO_SHADOWS_P5_PILOT_EARLY_TRANSPARENT=1` 可在试用 Renderer 启用不透明队列里透明接收面的独立两层解析。它使用相机和 Renderer 的不透明层交集，只读已准备的深度。该路径要求 Forward 或 Forward+、关闭 Depth Priming，以及相机内一致的预写和不透明层遮罩。

`pilot-before.json` 和 `pilot-applied.json` 记录文件哈希和逐项前后索引及本地 fileID。回放旧基线时，关闭隔离 Editor，再把备份的 Renderer 与 Volume 文件及其 `.meta` 复制回副本的 `Assets/Settings`。

把执行方法换为 `HoShadowsPilotCutover.RunWithSeparateRenderer`，可在同一物理试验工程建立 `PC_HoShadowsPilotRenderer.asset`，并把它追加到 `PC_RPAsset` 的 Renderer 列表。报告给出 `candidateRendererIndex`。实测旧资产 SHA-256 未变化、默认索引仍为 `0`、新 Renderer 索引为 `1`、RT 总开关保持关闭，13 个程序与材质引用已经保留。

分镜头试用时，副本里的 Base Camera 可显式选择新索引。共享材质的屏幕预览是材质局部关键字。先确认材质只供受管镜头使用，或者给试用镜头分配材质副本，再开启关键字。此工具保留原有相机与材质引用。在弃用副本时，关闭 Editor，从输出目录恢复副本的 `PC_RPAsset` 及其 `.meta`，清理单独的试用 Renderer 资产。

具名 RT 镜头的独立副本可在追加 Renderer 时设置 `HO_SHADOWS_P5_PILOT_RT_OPT_IN=1`。工具保留旧 RT 设置和程序引用，报告候选的开关与射线偏移。WTD 保存了启用 RT 的 Volume，旧 Skin 即使是 Main 也能接收主灯 RT。测试统一路径时，临时给该相机的 Body 和 Head 材质副本启用预览并设为 Full，保持两次受管对照的材质和灯光一致，仅切换 RT 总开关。

## 真实镜头的隔离对照

把 `HoShadowsSceneCutoverComparison.cs` 与试切工具一起复制到物理快照的 `Assets/Editor`。快照包含保存的羽啾与圣诞万花镜场景、两者的依赖，以及带有屏幕预览的 HoShader 包版本。重新使用尚未试切的 Renderer，设置两个空输出目录，以 D3D11 启动 Unity。

```powershell
$env:HO_SHADOWS_P5_PILOT_PROJECT = 'D:/Temp/HoShadows-P5-Local/SceneSnapshot'
$env:HO_SHADOWS_P5_PILOT_OUTPUT = 'D:/Temp/HoShadows-P5-Local/CutoverBackup'
$env:HO_SHADOWS_P5_COMPARE_OUTPUT = 'D:/Temp/HoShadows-P5-Local/SceneComparison'
New-Item -ItemType Directory -Path $env:HO_SHADOWS_P5_PILOT_OUTPUT,$env:HO_SHADOWS_P5_COMPARE_OUTPUT
unity run $env:HO_SHADOWS_P5_PILOT_PROJECT --editor-version 6000.3.15f1 --timeout 900 --no-tail `
  --log-file "$env:HO_SHADOWS_P5_COMPARE_OUTPUT/unity.log" -- `
  -force-d3d11 --burst-disable-compilation -executeMethod HoShadowsSceneCutoverComparison.Run
```

以 640×360 渲染两个已保存的 Base Camera。先读两次旧路径，再用同一快照跑新 Feature 的 4 和 16 个逐灯预算。当前版本在已支持的 Skin、Eye、Cloth、Environment、Hair 和 Effect 材质路径临时启用受管预览。场景退出时恢复材质。默认保留材质的 Main／Full。要测试拟议的 Full 迁移，用 `HO_SHADOWS_P5_COMPARE_PLAN` 传入 dry-run JSON。对照工具只处理报告内的 `review_full_candidate`，逐项核对快照材质文件的 SHA-256。

设 `HO_SHADOWS_P5_COMPARE_HAIR_TRANSPARENT=1` 可在试切臂临时预览透明 Hair，并单列早队列和常规透明队列的合资格材质数。早队列还需要同时启用试切的早队列开关。对照保留材质的保存队列、深度写入、接收强度和阴影范围，只临时设置预览关键字和对应资格关键字。

可用 `HO_SHADOWS_P5_COMPARE_FILTER` 按场景路径选择子集，或用 `HO_SHADOWS_P5_COMPARE_SCENE` 指定快照内另一场景的 `Assets/...unity` 路径。默认关闭用作对照变量的平面反射、相机后处理和 AA。设 `HO_SHADOWS_P5_COMPARE_AUTHORED=1` 改用原生相机栈请求，保留保存的相机效果并执行 URP 的 Volume 更新。

| 首轮 D3D11 对照（Hair 和 Effect 接入前） | 预览材质／临时转 Full | 四灯预算的 RGB 最大分量差 | 旧路径重复波动 | 阈值 `>0.002` 且旧重复稳定的差异像素 |
| --- | ---: | ---: | ---: | ---: |
| 羽啾（保存机位只有背景和地面） | 13／11 | 0.00001324 | 0.00001324 | 0 |
| 圣诞万花镜 | 19／0 | 0.00280380 | 0.00280380 | 0 |

圣诞万花镜的两路预算各有 9 个像素超过阈值，旧路径重复也有 9 个。此快照中该镜头没有启用阴影响应的 Main 材质临时转为 Full，逐灯预算在实际受光材质上的影响还应使用 Hiro 测试场景验证。`scene-cutover.json` 里的时间来自同步提交和读回，包含 Editor 开销。正式帧预算用 GPU 帧计时检查。

早期单相机请求保留了平面反射、后处理和 AA 开关。圣诞万花镜四灯预算下超过阈值且旧重复稳定的差异像素为 `0`，最大差 `0.00390625` 与旧路径重复波动一致。16 灯对照的最大差为 `0.01583862`。这组既有结果只覆盖 Base 相机单独提交，没有运行完整相机栈的 Volume 更新。

羽啾的保存视角在角色上方，早期的像素对照不计为角色材质验收。保留保存机位的原生相机栈一度报出 `12488` 个差异像素，经 EXR 检查全部来自性能 HUD 中启用的 Feature 数量和行顺序。对照工具现在关闭两侧的性能 HUD，并检查中心区域有有效场景信号。`HO_SHADOWS_P5_COMPARE_FRAME_SUBJECT=1` 可在副本加载时根据 Skin 网格的世界包围盒重新取景，报告保留实际机位。`HO_SHADOWS_P5_COMPARE_ISOLATE=1` 增加仅生产端、仅 Core、仅 Hair、仅 Effect、关闭角色图集、关闭 Capture 输入六个定位对照。

两个物理项目的冷缓存仍未完成旧路径首帧。羽啾重新取景后在旧路径首帧触发 `360 s` 超时，Hiro 冻结包快照在同一步触发 `900 s` 超时。`HO_SHADOWS_P5_COMPARE_WITNESSES_ONLY=1` 可以按盘点清单保留候选材质所在的网格，将其它投射物改为仅投射模式。这组 Hiro 对照也在首帧触发 `240 s` 超时。各次均未写出可用于画面对照的 EXR。Hiro 的七份候选材质已和盘点逐项核对 SHA-256，真实镜头的转 Full 验收仍未完成。

标准透明队列的 Hair 屏幕预览在 D3D11 通过两层 owner、错误 owner 和同镜头 Eye。合成夹具首帧只读到亮值，提交之间保持材质值，第二帧和第三帧均正确变暗。

独立的早队列预览在 D3D11 Forward 和 Forward+ 各通过 12 个相机样本。`2000`、`2400` 与 `3000` 可在同像素混合，跨队列共享 owner 时仍按各自深度和阶段匹配。仅改变早队列 owner、仅改变晚队列 owner 及停用早队列解析都只影响对应像素通道。关闭灯影后启用预览，整帧 RGB 与原绘制一致。D3D12 的 Eye 与 Weighted OIT 六个组合回归和早队列改动前逐像素一致。Rendering Layers MRT 分支尚缺独立 GPU 验收。生产盘点仍为 7 份 `2000`、1 份 `2400` 与 1 份 `3000`，实际镜头中的材质、owner、首帧和相机设置仍需复查。

### 冷缓存异步预热

复制 `HoShadowsAsyncSceneWarmup.cs` 到物理副本的 `Assets/Editor`。此入口按 Editor update 提交原生相机栈，等待着色器编译队列空闲后复查两张有场景内容的渲染结果。它临时关闭实际相机 Renderer 上的性能 HUD，记录进度、提交耗时和稳定性。达到截止时间时会保留报告，待编译完成的占位画面不会被保存为基线。

```powershell
$env:HO_SHADOWS_WARMUP_PROJECT = 'D:/Temp/HoShadows-P5-Local/SceneSnapshot'
$env:HO_SHADOWS_WARMUP_OUTPUT = 'D:/Temp/HoShadows-P5-Local/AsyncWarmup'
$env:HO_SHADOWS_WARMUP_SCENE = 'Assets/00动画/角色/羽啾/羽啾.unity'
$env:HO_SHADOWS_WARMUP_ARM = 'legacy'
$env:HO_SHADOWS_WARMUP_FRAME_SKIN = '1'
$env:HO_SHADOWS_WARMUP_SECONDS = '900'
New-Item -ItemType Directory -Path $env:HO_SHADOWS_WARMUP_OUTPUT
& 'C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe' `
  -batchmode -projectPath $env:HO_SHADOWS_WARMUP_PROJECT -force-d3d11 `
  -logFile "$env:HO_SHADOWS_WARMUP_OUTPUT/unity.log" -executeMethod HoShadowsAsyncSceneWarmup.Run
```

入口完成后自行退出。`HO_SHADOWS_WARMUP_RENDERER_INDEX` 可选择副本中已配置的 Renderer，默认沿用保存的相机。可用 `HO_SHADOWS_WARMUP_CAMERA` 指定相机名。需要材质预览的对照臂先准备相机和材质，再调用 `Capture`。两个 EXR 和 JSON 使用各自的臂名。保留 TAA 的诊断可设 `HO_SHADOWS_WARMUP_ALLOW_TEMPORAL=1`，记录两张编译已完成的画面和重复帧变化，用各自的重复帧划分可比较像素。

羽啾的 D3D11 原设置异步首次提交在 `2.68 s` 返回，编译队列在 `290 s` 内仍未清空。另一次仅用于定位的诊断关闭着色器优化，使队列清空并拍到角色。热缓存下三次提交取得重复 EXR，中心非均匀信号 `21006` 像素，两帧差异 `>0.01` 的像素有 `6176` 个。该数据单独记录 TAA 波动与编译诊断条件。

## 逐 Pass GPU 时间

在已完成隔离试切、附加灯预算为 `4`、托管 RT 关闭的工程内，复制 `HoShadowsSceneGpuProfile.cs` 到 `Assets/Editor`。使用 Unity 6000.3.15f1 图形模式运行，保持同一 GPU 和分辨率。入口用 Editor update 每次提交一个请求，采集延迟返回的 GPU 记录。省略 `-quit`，入口会在采样结束后退出。

```powershell
$env:HO_SHADOWS_P5_GPU_PROJECT = 'D:/Temp/HoShadows-P5-Local/SceneSnapshot'
$env:HO_SHADOWS_P5_GPU_OUTPUT = 'D:/Temp/HoShadows-P5-Local/GpuProfiles'
New-Item -ItemType Directory -Path $env:HO_SHADOWS_P5_GPU_OUTPUT
& 'C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe' `
  -batchmode -projectPath $env:HO_SHADOWS_P5_GPU_PROJECT -force-d3d12 -burst-disable-compilation `
  -logFile "$env:HO_SHADOWS_P5_GPU_OUTPUT/profile-d3d12.log" `
  -executeMethod HoShadowsSceneGpuProfile.Run
```

默认测 Base 相机单独提交，`HO_SHADOWS_P5_GPU_AUTHORED_STACK=1` 可测包含 Volume 更新的原生相机栈。`scene-gpu-profile.json` 保留逐帧 block 数、有效帧数和 P50／均值／P95。没有有效 GPU 样本的 pass 会明确标记。直接提交的嵌套平面反射可能计入同名的逐帧 Pass，保存相机单独计时使用相机专属 marker，不能把各行直接加总。

RTX 4060／D3D12，1280×720，Base 单独提交，受管样本使用 Skin、Eye、Cloth 与 Environment 的屏幕接收预览。各场景两侧都有 49 个有效相机 GPU 样本。下表为中位数，单位 ms。

| 场景 | 相机旧路径 | 相机受管 | 原生逐灯图集旧／受管 | 新主光解析 | 新不透明逐灯解析 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 羽啾（保存机位未拍到角色） | 7.517 | 6.849 | 有效样本不足 | 0.065 | 无有效记录 |
| 圣诞万花镜 | 55.223 | 52.730 | 27.799／28.951 | 0.120 | 0.306 |

圣诞万花镜的受管透明逐灯解析为 `0.148 ms`，深度预写为 `0.189 ms`。在最终工具的热缓存复测中，每侧有 17 个有效样本。旧 HoShadowCast 图集 P50 为 `2.104 ms`。受管主光、不透明逐灯和透明逐灯解析分别为 `0.114`、`0.301`、`0.145 ms`。两轮均未获取完整帧 GPU 时间或角色图集的有效记录。

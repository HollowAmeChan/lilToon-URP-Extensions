# Ho-UI 风格规范（OB / SB / AC 及后续 feature）

> **适用范围**：OB / SB / AC 及后续**通道 / 声明型** feature。
> **后处理三块**（`ImageProcess` / `ScreenProcess` / 角色特化）的效果行与"效果浏览器"是**自成一套**的特殊设计
> （搜索栏 + 图标侧栏 + 窄行 + 无底控件），**不受本规范约束**，也不要照本规范去"统一"它们。
>
> 参照物：`HoGTAOVolumeEditor` / `HoGTAORendererFeatureEditor`（**调试在 Volume、feature 只留高级 + 兜底**）、`HoShadowCastRendererFeatureEditor`（分节最全）、`HoDebugTileRendererFeatureEditor`（Registry / 状态行）。**新 feature 一律照这份写，不要自造样式。**

## 0. 三条硬规矩

1. **调试入口在 Volume**（`HoXxxVolume` 的「调试」分组）；**feature 里只放高级设置 + 兜底默认值**，并留一行 HelpBox 指明调试去哪了。
2. **分节一律走 `LilUrpEditorSectionGui.DrawSectionHeader(ref expanded, "标题", summary, color)`** + `new EditorGUILayout.VerticalScope(EditorStyles.helpBox)`；**不许自造 foldout / 颜色 / 标题控件**。
3. **标签用中文**，英文原名 / 缩写放括号里（`Debug In Scene View`、`passEvent` 这类保留英文原样）。

## 1. 色板（语义固定，跨 feature 复用）

| 用途 | 颜色 | 用在 |
| --- | --- | --- |
| **运行** | `0.46, 0.64, 0.92` | 启用 / 分辨率 / 质量档 |
| **高级** | `0.62, 0.58, 0.78` | "时机 / Shader"（`passEvent` / shader / 兼容分支） |
| **调试** | `0.86, 0.62, 0.38` | 调试模式 / 视图开关 / 强度 |
| **内容 / 通道** | `0.42, 0.72, 0.58` | 本 feature 的通道、profile、合成 |
| **RendererFeature 设置** | `0.45, 0.64, 0.96` | feature 级设置、声明 |
| **名称 / 声明** | `0.80, 0.55, 0.85` | 具名条目、槽位声明 |

同一 feature 内颜色**固定在类顶部的 `private static readonly Color XxxColor`**，不散在方法里。

## 2. 分节顺序（照这个排）

```text
运行 → [本 feature 的内容 / 声明] → 调试 → 高级（"时机 / Shader"） → 运行状态
```

- **摘要**（标题右侧）用 `BoolSummary` / `IntSummary` / `FloatSummary` / `EnumSummary` / `FormatAvailable` 拼，分隔符统一 `" / "`。例：`开 / 4 槽 / 2 张`、`8.0 / 12`、`可用`。
- 每个 `showXxx` 是 `private static bool`，默认折叠状态按重要度定（运行展开、调试折叠）。
- **依赖前置条件**用 `EditorGUILayout.HelpBox(..., MessageType.Info)`；会改运行时行为的用 `Warning`；解释语义用 `None`。
- 可用性一律给"可用 / 缺失"这类**只读状态行**，不要只靠颜色。

## 3. Volume 的写法（调试的落点）

```csharp
[VolumeComponentMenu("Post-processing/Ho-ObjectBuffer/逐物体通道")]
[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public sealed class HoObjectBufferVolume : VolumeComponent, IPostProcessComponent
{
    [InspectorName("启用"), Tooltip("……")]
    public BoolParameter enable = new BoolParameter(true);
}
```

- 枚举参数要有自己的 `VolumeParameter<T>` 子类并实现 `Interp`（离散量取 `t > 0 ? to : from`），照 `HoGTAOVolume.cs` 顶部那批写。
- **「调试」分组固定内容**：调试模式 → `Debug In Scene View` → `Debug In Game View` → 强度（`Debug Pow` 之类）。
- 调试模式的下拉**只写视图名**（`Color` / `Owner` / `Lane Coverage`…），**当前模式的说明跟着字段单独画一行**（`EditorGUILayout.HelpBox(desc, MessageType.None)`）。说明写通道含义与取景范围，照对应 debug shader 写 —— 改 shader 就改这一行；`Off` 和名字已经自解释的视图不画那一行。
- 调试模式**直出替换画面**；同一批视图**同时注册进 `HoDebugViewRegistry`**，这样 DebugTile 里还能按条目小窗看（两者不冲突）。
- Volume 未覆盖 / volume 关掉时**用 feature 的兜底默认值**，UI 上要写明这件事。

**什么进 Volume，什么留 feature**：

| 数据性质 | 放哪 |
| --- | --- |
| per-camera / 质量档可覆盖（启用、分辨率、调试） | **Volume** |
| 项目级**声明数据**（名字表、槽数声明、组表） | **feature 设置 / 组件**（Volume 是 per-camera 覆盖，声明不是） |
| 结构 / 编译期（`passEvent`、shader、自建 MSAA 样本数） | **feature 的「高级」** |
| 兜底默认值 | **feature**，并在 UI 上标注"Volume 未覆盖时用这里的值" |

## 4. feature 的写法

- 顶部 HelpBox 写**依赖与前置条件**（例：GTAO 写"必须排在 Ho-GeometryBuffer 之后"）；OB/SB/AC 写自己的偏序与"下游只吃 AC"。
- 分节固定为：**运行（兜底默认值）** / **声明（只读汇总）** / **高级（"时机 / Shader"）** / 调试，最后一节只留一行：

  > `调试模式与视图开关已移至 Ho-Xxx Volume 的「调试」分组。`

- **不在 feature 里留第二份调试开关**（会变成两份真值）。
- 「运行」里的默认值必须写明它是兜底：`Volume 未覆盖时生效`。

## 5. 用语与命名

- **UI 上不出现 "Cryptomatte"**（合规导出归独立 AOV/export feature）。
- ID 相关名只用 OB §1.1 那一套：**组 ID / 部件 ID / 标记 / 物体位（全角色·脸·前发·眼睛·眼透区域·配件·人体·预留 7）/ 材质位 0~3**。
- 槽位相关一律说“**语义 lane**”（AC Selection transport lane）；UI 同时显示 SemanticId/名字，不把 LaneIndex 冒充 ID。身份相关说“**身份池**”。
- 纹理名与契约登记名**不在 UI 上出现**（`_HoObjectBuffer*` 这类只在文档与代码里）。
- 枚举的 `InspectorName` **只写名字**：不写说明（下拉里一屏长句，选值反而看不见），更不许出现 `/`（Unity 的下拉把斜杠当分组分隔符，那一项会变成一串子菜单而不是一个可选值；`[InspectorName("")]` 变分隔线是同一套规则）。每个值的说明**在字段下面按当前值单独画一行**（见 §3）。摘要行里的 `" / "` 是普通字符串，不受影响。

## 6. 三个 feature 的具体分节

### OB — `HoObjectBufferVolume` / `HoObjectBufferRendererFeature`

| 侧 | 分节 | 内容 |
| --- | --- | --- |
| **Volume** | 运行 | 启用 |
| | 调试 | 身份池 `Id0` / `Id1` / coverage / Facing / object semantic lane mask / **溢出** / **未声明 ID** / owner 对齐参考 |
| **Feature** | 运行（兜底） | 启用、朝向图开关、采集 `layerMask` |
| | 声明（只读汇总） | 组表 / 条目表：名字 → 组 / 部件 / 标记 / 物体位 / object semantic membership |
| | 高级 | `passEvent`、shader、`自建 MSAA 样本数 N = 4`（只读，标注"与相机 AA 解耦"） |
| | 调试 | 一行 HelpBox → Volume |

### SB — `HoSurfaceBufferVolume` / `HoSurfaceBufferRendererFeature`

| 侧 | 分节 | 内容 |
| --- | --- | --- |
| **Volume** | 运行 | 启用 |
| | 调试 | `Color` / `Normal` / `Material` / `Reflection` / `Classification` / SurfaceOwner / 每个 SurfaceSemantic lane 的 ID·value·written / owner mismatch |
| **Feature** | 运行（兜底） | 启用、五张数值图按需开关、semantic batch 质量/成本状态 |
| | 声明（只读汇总） | 材质侧参数名；来自 `HoSemanticSchema` 的 surface-writable SemanticId / LaneIndex / sourceMode |
| | 高级 | `passEvent`、shader、两段式深度说明 |
| | 调试 | 一行 HelpBox → Volume |

### AC — `HoAttributeCompositeVolume` / `HoAttributeCompositeRendererFeature`

| 侧 | 分节 | 内容 |
| --- | --- | --- |
| **Volume** | 运行 | 启用、**按属性的 resolve 开关** |
| | 调试 | 合成属性图 / AC Selection lane SemanticId·coverage·sourceMode / object·surface sample / owner mismatch / **消费者登记表** / **解析失败** |
| **Feature** | 运行（兜底） | 启用、属性清单默认开关、`HoSemanticSchema`、4/8/16 lane 成本档 |
| | 声明（只读汇总） | SemanticId / LaneIndex / sourceMode / 消费者登记表（解析不到就报出来） |
| | 高级 | `passEvent`、shader |
| | 调试 | 一行 HelpBox → Volume |

## 7. 禁止清单

- ❌ 自造折叠控件 / 颜色 / 标题样式（一律用 `LilUrpEditorSectionGui`）。
- ❌ 在 feature 里再放一份调试开关（两份真值）。
- ❌ 把声明数据塞进 Volume（它是 per-camera 覆盖）。
- ❌ UI 上出现 `Cryptomatte`、`_HoMetadataBuffer*`、`Target5`、`custom0` 这类旧名。
- ❌ 只在颜色上表达状态（缺可用性文字）。
- ❌ 静默失败：解析不到的名字、层数不一致、溢出 —— 必须在「调试」或「运行状态」里看得见。

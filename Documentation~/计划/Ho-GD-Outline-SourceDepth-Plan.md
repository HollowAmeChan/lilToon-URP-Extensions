# GD 描边统一修正：来源深度接入规划

状态：设计规划，尚未实施或通过 Unity GPU 验证。日期：2026-10-20。

## 1. 已确定的范围

- 继续使用 lilToon 的正常描边 draw、外扩覆盖、材质着色与硬件深度测试；修复发生在参与深度测试的深度来源。
- 归属现有 GD OutlineCorrection，与方向／厚度共用组件、准备流程、Renderer 槽和资源生命周期，不增加独立 Outline RendererFeature 或独立的用户启用链。
- 外扩位置负责光栅覆盖；来源映射负责求值深度；原有 ZTest、ZWrite、Stencil、Blend 仍承担各自的管线职责。
- 不将外扩壳变成 GB 的物理几何；AO/GI 的物理 GB 输入保持真实主体。
- 本文只规划接入。尚未证明“外扩前位置插值”足以修复用户场景，不把基础插值原型称为完整像素追溯。

## 2. 先验证来源定义，再冻结数据格式

第一步用方块穿墙验证来源深度的定义。保留外扩前、已经过当前形变的源位置，建立片元对应关系；同时观察外扩前位置插值的不足，尤其是来源落在原网格背面或被遮挡时。

必须同时满足：后置墙不错误削掉可见描边；真正的前景仍遮挡描边；主体内部不因来源深度相等而被背面壳填黑。不得用 ZTest Always 或大偏移伪造通过。

若插值不足，只增强 GD 内的来源映射。先证明需要哪些顶点／图元／邻接数据，再决定是否扩展静态 buffer；不预先上传完整拓扑，也不默认增加全屏 Visibility Buffer。若确实需要每相机可见性资源，应单独说明成本与接入范围，不能视为已有实现。

片元深度只能影响已有光栅片段，不能恢复已被 Cull、裁剪或退化几何丢弃的覆盖；这一边界需纳入验收。

## 3. 接入位置

下表 lilToon 路径相对于 `D:/Unity_Fork/lilToon/Assets/lilToon/`，其余相对于本仓库。

| 位置 | 计划改动 |
| --- | --- |
| `Runtime/GeometryData/Outline/HoGeometryDataOutlineCorrectionBuilder.cs` | 复用已有顶点合并、面／边邻接构建，仅输出经过原型证明必要的来源数据；保留原方向／厚度 RGBA 语义。 |
| `Runtime/GeometryData/Outline/HoGeometryDataOutlineCorrection.cs` | 同一 Prepare 准备完整修正包；为缓存增加必要的格式／能力版本检查，旧数据不得误报已具备来源深度能力。 |
| `Runtime/GeometryData/Outline/HoOutlineDataRegistry.cs` | 扩展来源数据的地址与有效性；复用高 16 bit GD 槽，不改变低 16 bit OB 身份，不建立第二套 Renderer 注册。 |
| `Runtime/GeometryData/HoGeometryDataRendererFeature.cs` | 现有发布 pass 绑定新增资源并声明 RG 依赖；保持每相机重置、Compatibility 发布与释放一致。无新增资源时不强行扩展。 |
| `Runtime/GeometryData/Shaders/HoGeometryOutline.hlsl` | 提供共享来源解码与深度求值接口，区分来源位置、最终设备深度、匹配的线性眼深和有效性；函数名在实现时确定。 |
| lilToon `Shader/Includes/lil_gd_outline_pragmas.hlsl` | 当前 `_HO_GD_OUTLINE` 是 `shader_feature_local_vertex`，需扩展为顶点和片元均能识别的变体；限定 URP 描边消费者。 |
| lilToon `Shader/Includes/lil_common_vert.hlsl`、`lil_vert_outline.hlsl` | 分离主体来源与外扩位置，传递来源数据及有效性；审计 OS/WS 自定义变形的执行顺序。不能仅在外扩前缓存 OS 坐标就声称覆盖了所有 WS 变形。 |
| lilToon `Shader/Includes/lil_pass_forward_normal.hlsl` | GD 描边变体输出共享函数计算的 `SV_Depth`；普通主体与非 GD 变体保留原路径。所有非 discard 路径，包括数据缺失回退，都必须输出有效深度。 |
| lilToon `Shader/Includes/lil_pass_outline_normal_depth.hlsl` | 使用相同来源和设备深度参与测试；normal-depth 的眼深从同一最终深度定义产生，owner/semantic writer 保持原归属。深度修正不自动把壳法线改成源法线。 |
| lilToon `CustomShaderResources/URP/*Outline*.lilblock` | 审计 Direct、Only、TwoSide、Multi、OIT 等现有模板及 UsePass 覆盖；按需补齐插值、变体和状态。通过生成器重建产物，不逐个手改生成 shader。 |
| AC／ScreenProcess | 保持现有消费接口，验证视觉 identity、coverage、景深都与新可见描边一致；不为每个 effect 再新增专用修正开关。 |

附加审计：Lite、自定义输出宏、深度预通道、深度拷贝时机、MotionVectors 与 OIT。仅接通 ordinary Forward 不能标记所有变体已支持。URP 摄像机方案先实现；共享 include 不得改变 Built-in／HDRP 行为。

OIT 目前通过 `LIL_OIT_RETURN` 使用位置深度计算权重，且有独立 MRT 输出结构，需要专项接入，不能只给普通颜色返回值添加深度成员。第一批验证先覆盖普通 Opaque/Cutout，再逐类扩展；不支持的组合明确显示状态，不偷偷改走近似混合。

## 4. 统一深度契约

```text
当前主体来源数据 → 来源映射 → 来源位置／深度
                               ↓ 唯一的艺术深度偏移
                        最终设备深度
                         ├─ Forward SV_Depth
                         ├─ GB outline SV_Depth
                         └─ 同一定义的线性眼深 → 视觉后处理

原有外扩位置 ───────────────→ SV_POSITION / 光栅覆盖
```

- `SV_Depth` 用于测试；ZWrite 开启时也写同一深度，不能宣称测试源深度但写壳深度。
- 当前绑定的相机 depth 可能含描边视觉深度，取决于绘制／拷贝时序；专用物理 GB 仍只写主体。不能声称“所有深度均不受影响”。
- 原始来源深度和艺术偏移后的最终深度在内部区分，避免景深又读回旧壳深度。
- 处理透视／正交、reversed-Z、平台设备深度范围与 XR 每眼矩阵，不直接插值非线性设备深度充当精确来源重建。
- 动态可用性不能只由材质 keyword 决定。相同材质可用于不同 Renderer；顶点与片元须一致知道当前 draw 是否具备有效数据。
- 无来源、GD 关闭、旧缓存、相机切换时使用明确一致的回退，并显示原因；不能把只运行了方向修正的结果报告为完整修正。
- 先用普通 `SV_Depth` 保证正确性；只有证明了相对于光栅深度的单调关系，才能考虑 conservative depth。测量 Early-Z 与过绘成本。
- MSAA 下显式片元深度不天然等于原有逐样本插值深度。需要验证边缘、coverage 与深度测试，必要时评估逐样本求值；不沿用未经验证的逐样本精确声明。

## 5. 用户入口与高级设置

### 5.1 单一能力入口

现有来源枚举值 3 保留序列化值，界面从 `GD (Direction + Width)` 调整为“GD 几何修正”，说明包含方向、厚度和来源深度。组件仍使用现有类名与 Prepare 操作，不增加第二个“修复穿插”开关。

材质显示“请求使用 GD”；实际是否生效按 Renderer、数据版本和当前相机判断。单独选材质时不假装知道所有实例状态；选中对象或在 GD 诊断里显示有效／缺数据／当前变体未支持等结果。

Inspector 在 `lilPropertyGroupDrawerAdvancedSetting.cs`、`lilNextInspectorGUI.cs`、`lilMainInspectorGUI.cs` 共享同一说明和诊断逻辑，避免新版与旧版界面给出不同规则。`lilMaterialUtils.cs` 中来源 keyword 同步、渲染模式重置与属性复制同样需要核对。

### 5.2 各设置的处理

| 设置 | GD 模式下的约定／呈现 |
| --- | --- |
| 宽度、宽度贴图、距离宽度修正、颜色、纹理、光照 | 正常保留，不把美术参数藏进深度诊断。 |
| 顶点色来源枚举 | 保留 0/1/2 的既有语义；值 3 表示完整 GD 修正。 |
| 描边方向贴图 | 现有 GD 通过 RGBA 路径写入方向，可能已经覆盖贴图方向；UI 按实际优先级禁用被覆盖控件并解释，不新增“看起来能调但实际无效”的选项。 |
| `_OutlineZBias` 及 mask | 明确为唯一一处艺术深度偏移，作用于追溯后的来源深度定义；原型要验证旧对象空间偏移语义如何转换。保留序列化值；不能静默换单位或在壳与来源上双重累加。 |
| `ZTest` | 继续作为真实比较操作。“来源深度”不是新的 CompareFunction。Always 仍会绕过遮挡，Equal 需要精确匹配，Less/LEqual 需要验证自身遮挡；UI 按设置解释效果。 |
| `ZWrite` | 继续有效，标明写入追溯后的最终深度；Off 表示不写，不能描述成禁用深度测试。 |
| `Offset Factor/Units` | 属于光栅深度偏置，与 `_OutlineZBias` 分开。首版推荐为 0，放高级区并在非零时显示兼容状态；不能承诺它会自动作用于 shader 输出的深度，也不强行模拟不同 API 的硬件偏置。 |
| `Cull` | 改变候选覆盖，Front 为常见背面壳配置；修改后仍按用户设置执行，诊断说明覆盖已改变，不暗中强制 Front。 |
| `ZClip`／近裁面取消 | 裁剪先于片元输出，来源深度不能恢复被裁掉的几何；特殊组合列入验证。 |
| `Stencil`（含 ZFail） | 原规则继续有效，ZFail 对应修正后的测试结果；GD 不额外保留 stencil 位，也不偷偷改 Compare/Ref。 |
| `Blend`、`ColorMask`、`AlphaToMask` | 保留功能；GB 元数据捕获需要核对可见份额一致性，不能给 owner MRT 原样复制颜色混合状态。任意透明／混合组合通过验证前不宣称视觉身份精确匹配。 |
| Queue、Transparent/OIT、自定义 shader | 高级区显示适用范围和实测状态；来源深度不会改变透明排序算法。 |

注意：当前 Opaque 模板和 `lilMaterialUtils` 的 `_OutlineZTest = 2` 对应 Less，不能未经验证就将“推荐设置”写成现有默认 LEqual。是否需要改为 LEqual 是原型的专项结论；贸然改成 LEqual 可能让源深度相等的背面壳覆盖主体内部。

### 5.3 降低认知干扰的界面约定

- 描边主面板只显示“GD 几何修正”、简短说明和当前可获取的有效状态。
- 高级区显示原始设置；偏离已验证组合时列出具体原因，例如“ZTest Always：当前不进行遮挡判断”，不笼统写“GD 失效”。
- “恢复已验证的渲染设置”是显式按钮，操作前列出会改哪些字段，执行支持 Undo、多材质编辑；不在 OnValidate、打开 Inspector 或切换来源时静默重置旧材质。
- 此按钮的范围只包含对应模式的渲染状态，不清空颜色、贴图、宽度或用户 stencil 分配。
- 不增加标准／高级两套运行算法。折叠区只是组织控件，实际设置和 shader 行为保持可解释。

## 6. 实施顺序与验收

1. **来源深度原型**：方块穿墙、前置遮挡、自身内部防填黑、斜面连续性。先验证核心假设；失败就调整来源映射，不用偏移掩盖。
2. **GD 完整接入**：准备、缓存能力版本、绑定、共享求值、Forward＋GB；校验缺数据和关闭后的行为。
3. **设置契约与 Inspector**：ZTest Less/LEqual 对照、ZBias 单次作用、Offset 状态、旧材质／多材质／共享材质跨 Renderer。界面只宣称真实已实现能力。
4. **完整视觉链**：真实项圈／交叉带、前后景、两个角色交叠；颜色与 owner/coverage 对照；AC 语义、景深、雾，且 AO/GI 仍读主体。
5. **采样与执行路径**：MSAA 1/2/4、RG/Compatibility、正交／透视、D3D11/D3D12、双相机与镜头运动、蒙皮／BlendShape、native RG 清理及 stencil。
6. **扩展与性能**：按变体补齐 Multi/TwoSide/Lite/OIT、必要的 MotionVectors；测量 shader 变体、片元执行与显存成本。通过后更新正式 GD 与描边继承文档。

现有 `Tests/Editor/ScreenProcess/ScreenProcessRegressionTests.cs` 是 GB→AC→SP、MSAA 与运动残留回归的复用入口。新增测试重点覆盖“深度来源改变后以前错误的画面恢复，真正前景仍遮挡”，同时保留旧实现负对照；不能只验证新函数输出与自身实现一致。

## 7. 参考

- [HLSL semantics：SV_Depth 的测试、写入与 MSAA 限制](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-semantics)
- [Unity stage-specific shader keywords](https://docs.unity.cn/2022.1/Documentation/Manual/SL-MultipleProgramVariants.html)
- [现有 GD 文档](../Ho-GeometryData.md)
- [AC 描边继承契约](Ho-AC-OutlineInheritance-Contract.md)

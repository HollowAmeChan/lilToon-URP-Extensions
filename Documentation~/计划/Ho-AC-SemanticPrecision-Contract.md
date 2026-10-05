# AC 语义精确合成：关联统计契约

日期：2026-10-06。状态：Scalar V1 生产关联、归约和 AC 消费已实现；支持范围与近似降级如下。一般化的逐 lane 权重与其他采样域仍为后续工作。

## 1. 本轮证据

旧的像素级路径只保留一个 SB writer 的语义值，AC 将它作用于 OB 多身份的聚合覆盖率。两个身份共享 Face、A 的权重为 0、B 的权重为 1 时：

| 采样布局 | 当前选择 A 为 surface writer | 逐样本参考 |
| --- | --- | --- |
| 1 个 A | 0 | 0 |
| 1 个 A + 1 个 B | 0 | 0.5 |
| 3 个 A + 1 个 B | 0 | 0.25 |

四样本移动交界中，中心 writer 在 A/B 间切换，当前值可从 1 跳到 0；参考值依次为 1、0.75、0.5、0.25、0。本轮 GPU 固定案例观察到最大覆盖率偏差 0.5。该数值描述案例，不是任意场景的全局误差上界。

owner 在 OB 身份池内，不代表它是主导身份。只接受 layer 0 会丢失非主导身份的合法贡献；旧公式不检查 owner；本轮关联路径逐样本比较两个 owner，合法非主导身份也计入 W/V。

## 2. 需要保留的充分统计量

对一个像素的每个 OB ranked 身份 i 和语义 lane l：

- `C_i`：属于该身份的样本数 / 实际采样数 N；沿用 OB coverage。
- `W_i,l`：SB owner 与 OB 同一样本身份匹配，且 SemanticId 等于声明值的样本数 / N。显式写零仍计入 W。
- `V_i,l`：上述有效写入样本的语义 value 之和 / N。
- `o_i,l`：该身份条目的语义 membership，取 0 或 1。

W 和 V 必须先在对应样本上校验 owner，然后按身份归约。不能从一张已 resolve 的 SB value、一个 pixel owner 与 OB coverage 猜出它们。采用不归一化覆盖率，满足 `0 <= V <= W <= C`；不将 V 除以 W，否则未写比例再次丢失。

| sourceMode | 精确最终 coverage |
| --- | --- |
| ObjectOnly | `sum(o_i,l * C_i)` |
| SurfaceOnly | `sum(V_i,l)` |
| Union | `sum(o_i,l * C_i + (1-o_i,l) * V_i,l)` |
| SurfaceOverride | `sum(V_i,l + o_i,l * (C_i-W_i,l))` |
| Intersection | `sum(o_i,l * V_i,l)` |

这些公式利用 object membership 的二值性，与逐样本先合成再平均等价。隔离验证对 N=1/2/4、背景、不同 membership、未写、写零、错误 owner 和多个权重组合进行了独立样本 oracle 对照。生产实现另由真实 MSAA 纹理、归约 shader、AC shader 和原生 lilToon 材质验证。

## 3. 生产与归约职责

由 SB 语义生产侧的专用关联归约发布 W/V，AC 继续读取单采样的 owner 关联统计，不把原始 MSAA 采样接口扩散到屏幕消费者。

- OB 需要给该归约提供原始 IdentityMS 的引用、实际 N、像素尺寸、有效 viewport、眼索引与帧/相机键；身份定义和 ranked 身份池仍由 OB 持有。
- SB 的语义捕获使用同一实际 N、样本位置、viewport、jitter 和可见性规则；关联归约比较对应样本的两个 owner。若任一条件不同，不能称为精确路径。
- 关联归约是显式声明的跨轴依赖，只用于语义配对；SB 数值 pass、GB 几何 pass不因它增加相互读取。该例外需在接入时同步总览、资源契约和 RG 依赖。
- 统计按 OB 同一帧的 ranked 身份顺序输出。身份缺失、owner mismatch 与未写分别诊断；owner mismatch 视为无有效 writer，不冒充“显式写零”。
- 查询消费者仍读取固定 lane 的 `(SemanticId,coverage)`，SemanticId、IdentityId 与 LaneIndex 不重新编号。

当前 lilToon writer 对 renderer 已有的所有 lane 写同一 `_HoSemanticWeight`。首版使用 `HoSurfaceCorrelatedV1` / `HO_SURFACE_CORRELATED_V1` 声明这一限制，将每身份 W/V 因子化为两张 RGBA16F：RGBA 对应最多四个 OB owner。AC 按 owner membership 还原每 lane 的 W/V，避免直接增加 16 张泛化 lane/owner RT。未来支持每 lane 独立权重时必须扩展格式或按需求物化，不能继续套用因子化假设。

## 4. 缺失与降级

- 整个 surface 来源未产出：保持当前契约，所有 sourceMode 回退 object 结果，同时状态可见。
- 来源可用但某样本未写：W/V 为零，各 sourceMode 按上表处理；与来源不可用区分。
- 写入声明 ID 且 value=0：W 增加、V 不增加。
- 实际 N=1 是合法降级，精度随之降低；N、格式或样本位置无法匹配时拒绝“精确”标记，采用已声明的近似路径并给诊断。
- 半透明/OIT 颜色贡献不在身份覆盖率保证内，不将此统计称为完整透明贡献。

## 5. 已实现的 Scalar V1 生产链

1. OB 发布当前相机/帧的原始 IdentityMS（N>1），或 packed Id0（N=1），以及实际 N、尺寸、slice、纹理维度、动态缩放标志、viewport、view/projection 矩阵。
2. SB 在独立 RGBA8 MSAA 附件与私有深度上执行 `HoSurfaceCorrelatedV1`。packet 为 `RG=owner bytes, B=weight, A=written`；weight=0 保留 A=1。该 pass 共用原生 SB 的几何与 alpha clip 路径。
3. fragment 用 `SV_SampleIndex` 和 `EvaluateAttributeAtSample` 计算当前样本的 UV、位置与裁切；纹理遮罩须显式启用 `_HO_SEMANTIC_MASK`。相关 HLSL 行为见 [Microsoft 文档](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/evaluateattributeatsample)。
4. 归约读取两个 MSAA 附件的同一像素/sample，比较实际 `GetSamplePosition`。匹配 owner 后按 OB ranked 身份累计 W/V，不只接受主导身份。
5. 发布两张 RGBA16F 单采样统计及一张 RGBA8 状态：R=owner mismatch/身份不在池，G=有 OB 但无 writer，B=样本位置不一致，A=背景上的 SB writer。它们都是 /N 的覆盖率。
6. AC 对每个 owner 的 membership 还原 lane 统计。Scalar V1 下 ObjectOnly/Union=C，SurfaceOnly/Intersection=V，SurfaceOverride=V+C-W。身份表每像素查询四次，八条 lane 复用查询结果及 W/V。

格式约束保证 `0<=V<=W<=C`，但 packet 权重仍是 UNORM8，Selection 输出也保留 UNORM8 量化。这里的“精确”指样本与 owner 关联，不指无限数值精度。

## 6. 支持范围与可观察性

- RenderGraph、D3D11/D3D12、Shader Model 5、2D 单 slice、完整 viewport、无 dynamic scale；OB 实际 N=1/2/4。
- 普通 render scale 改变的是实际 descriptor 尺寸；匹配尺寸可关联。动态缩放、XR、局部 viewport、其他 API、本轮格式不足或 shader 缺失都明确回退像素路径。
- 当前捕获层与队列范围内的已登记材质具有旧 `HoSurfaceSemantic` 而不具有可用 V1 pass，则整个相机保留旧路径。V1 pass 的启停按 LightMode 判断。声明 V1 的自定义 shader 必须遵守共享标量与已有 membership 契约；不同 lane 权重需要新版本协议。
- 编辑器在相机裁剪前请求准备 writer 变体，异步编译未就绪时明确近似降级；同步编译模式等待准备。不能等到 renderer list 创建后才编译，否则冷材质可能在第一帧使用临时变体。这里使用 [Unity ShaderUtil.CompilePass](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/shaderutil/compilepass)；Player 不调用编辑器 API。
- 样本位置 mismatch 不使用该像素的 W/V；owner mismatch 和未写视为无有效 writer，按对应 sourceMode 处理。
- 兼容路径继续使用像素合成。关闭 SB、关闭语义或关闭关联开关不会复用其他相机/上一帧统计。
- AC Volume 增加 Written Coverage、Weighted Coverage、Semantic Precision。Precision 绿=关联可用，橙=近似/样本域无效。Feature 只读汇总显示最近相机的生产状态与实际 N；逐样本异常比例在 status 纹理。

旧的数值面和单采样 lane 捕获目前仍运行，以服务既有调试与近似降级。本轮多一趟样本捕获、私有深度、一趟三 MRT 归约；尚未做按消费者需求裁剪和性能基准。

## 7. 验证与下一步

隔离 Unity 6000.3.15f1 工程与报告位于忽略目录 `research~/AttributeComposite/`。验证包含 N=1/2/4 的真实 MSAA 归约、五种 sourceMode、未写/写零/错 owner、原生 lilToon writer、两身份不同权重交界、移动交界、纹理遮罩、双相机以及明确近似降级。

最终 D3D11/D3D12 各 149 项关联检查、各 440 项正确性回归通过；D3D11 兼容输入/ScreenProcess 回归 82 项通过。详情见本地 `research~/AttributeComposite/Precision-Report.md`。本轮未覆盖全部 dissolve/dither/LOD 动态交界组合及大规模性能。

下一步优先将消费者登记扩展为资源需求，裁剪重复 lane 捕获和无人消费的统计/Selection。每 lane 独立权重、XR/动态缩放、透明贡献与 16-lane 分批分别立项，不能用 Scalar V1 的结果冒充完成。

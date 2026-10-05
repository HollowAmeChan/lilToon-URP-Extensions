# AC 语义精确合成：关联统计契约

日期：2026-10-05。状态：正确性案例与统计等价证明已建立；以下生产数据契约拟用于下一轮实现，尚未替换当前像素级合成。

## 1. 本轮证据

当前 SB 只保留一个像素 writer 的语义值，AC 将它作用于 OB 多身份的聚合覆盖率。两个身份共享 Face、A 的权重为 0、B 的权重为 1 时：

| 采样布局 | 当前选择 A 为 surface writer | 逐样本参考 |
| --- | --- | --- |
| 1 个 A | 0 | 0 |
| 1 个 A + 1 个 B | 0 | 0.5 |
| 3 个 A + 1 个 B | 0 | 0.25 |

四样本移动交界中，中心 writer 在 A/B 间切换，当前值可从 1 跳到 0；参考值依次为 1、0.75、0.5、0.25、0。本轮 GPU 固定案例观察到最大覆盖率偏差 0.5。该数值描述案例，不是任意场景的全局误差上界。

owner 在 OB 身份池内，不代表它是主导身份。只接受 layer 0 会丢失非主导身份的合法贡献；当前公式不检查 owner，新增 owner 视图用于显示此事实，不暗中改变画面。

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

这些公式利用 object membership 的二值性，与逐样本先合成再平均等价。隔离验证对 N=1/2/4、背景、不同 membership、未写、写零、错误 owner 和多个权重组合进行了独立样本 oracle 对照。证明不代表生产 producer 已实现。

## 3. 生产与归约职责

推荐由 SB 语义生产侧的专用关联归约发布 W/V，AC 继续读取单采样的 owner 关联统计，不把原始 MSAA 采样接口扩散到屏幕消费者。

- OB 需要给该归约提供原始 IdentityMS 的引用、实际 N、像素尺寸、有效 viewport、眼索引与帧/相机键；身份定义和 ranked 身份池仍由 OB 持有。
- SB 的语义捕获使用同一实际 N、样本位置、viewport、jitter 和可见性规则；关联归约比较对应样本的两个 owner。若任一条件不同，不能称为精确路径。
- 关联归约是显式声明的跨轴依赖，只用于语义配对；SB 数值 pass、GB 几何 pass不因它增加相互读取。该例外需在接入时同步总览、资源契约和 RG 依赖。
- 统计按 OB 同一帧的 ranked 身份顺序输出。身份缺失、owner mismatch 与未写分别诊断；owner mismatch 视为无有效 writer，不冒充“显式写零”。
- 查询消费者仍读取固定 lane 的 `(SemanticId,coverage)`，SemanticId、IdentityId 与 LaneIndex 不重新编号。

当前 lilToon writer 对 renderer 已有的所有 lane 写同一 `_HoSemanticWeight`。首版可在严格验证该限制后，将每身份 W/V 因子化为两张 RGBA16F：RGBA 对应最多四个 OB owner。AC 按 owner membership 还原每 lane 的 W/V，避免直接增加 16 张泛化 lane/owner RT。未来支持每 lane 独立权重时必须扩展格式或按需求物化，不能继续套用因子化假设。

## 4. 缺失与降级

- 整个 surface 来源未产出：保持当前契约，所有 sourceMode 回退 object 结果，同时状态可见。
- 来源可用但某样本未写：W/V 为零，各 sourceMode 按上表处理；与来源不可用区分。
- 写入声明 ID 且 value=0：W 增加、V 不增加。
- 实际 N=1 是合法降级，精度随之降低；N、格式或样本位置无法匹配时拒绝“精确”标记，采用已声明的近似路径并给诊断。
- 半透明/OIT 颜色贡献不在身份覆盖率保证内，不将此统计称为完整透明贡献。

## 5. 下一轮实现验收

先发布 common sample domain 与原始 owner 引用，再接 SB 关联归约与 W/V 格式，最后让 AC consume 统计。重跑五种 sourceMode 的固定案例：两身份不同权重必须得到 0.5 / 0.25 等参考结果；再测试 viewport、render scale、双相机、移动交界与 N 降级。仅换公式或仅加 owner gate 不算完成。

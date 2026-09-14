# Ho-Cryptomatte 规划：纯值 → object → surface 的递进覆盖与合成层

> 状态：**概念草案**（待细化）。与 `Ho-ObjectBuffer_规划.md` §1.6 配套。
> 分工：**各 buffer 只提供可写通道，本 feature 决定"多来源怎么合成、谁覆盖谁"**；`Cryptomatte` 这个名字从 ObjectBuffer 手里收回来，专指这一层。

## 0. 一句话

**它不是一个 buffer，而是一层"属性解析器"**：读三轴的原始数据（含各 buffer 预留的可写通道），按 **纯值 → object → surface** 的递进顺序合成出每像素最终可用的属性，再据此产出遮罩、选区与导出。

    GB（几何轴）             ─┐   ┌─ 纯值：艺术家/材质直接写的字面量（各 buffer 的可写通道）
    ObjectBuffer（逐物体轴） ─┼─→ │  object：挂在物体 ID 上的属性（palette 表）
    SurfaceBuffer（表面轴）  ─┘   └─ surface：逐像素的表面数值（SB）
                                      ↓ 递进覆盖（优先级见 §2）
                                  合成属性 → 遮罩/选区 → 消费者 & AOV 导出

## 1. 为什么要单独一层

1. **解耦**：buffer 不需要知道属性语义，也不需要知道"冲突时谁赢"；加一个新属性只占一个可写槽位，**不动 buffer 的契约**。
2. **收敛**：今天每个消费端各自啃五张语义图、二十个 source（`ScreenProcessRuleSource` 就是活证据）；这一层之后，消费者只问它一次。
3. **名字各归其位**：`ObjectBuffer` 说轴（逐物体），`Cryptomatte` 说"选区 + 合成 + 导出"，`SurfaceBuffer` 说表面数值——互不冒充。

## 2. 递进覆盖的语义（待定，建议）

| 层 | 来源 | 例子 | 性质 |
| --- | --- | --- | --- |
| surface | SB 的逐像素数值 | thickness / roughness / 表面色 | 物理、逐像素、最泛 |
| object | ObjectBuffer 的 palette 表 | 类别、标签、逐物体属性（朝向） | 语义、逐物体 |
| 纯值 | 各 buffer 可写通道里被直接写入的字面量 | 材质画的遮罩、工具算的数 | 人为指定、最具体 |

**建议优先级**：`surface`（底） < `object` < `纯值`（顶）。理由是合成习惯：**越是人为显式指定的越该赢**，而逐物体语义应能盖住逐像素的物理默认值。

> 另一种读法是"纯值 → object → surface 依次覆盖"（surface 最高）。两种都自洽但含义完全不同，**这是本 feature 唯一必须拍板的语义**。

## 3. 待定清单

1. **优先级方向**（§2）。
2. **"纯值"由谁写**：材质 pass / 工具脚本 / 还是也给一个手写遮罩组件？写入口必须单一。
3. **按属性合成还是按遮罩合成**：每个属性各一条覆盖链，还是先合成"谁的属性生效"再统一取值？（后者更省，但要求三层同一套 ID 域。）
4. **可写通道预算**：ObjectBuffer 已定"逐物体辅助量 ≤ 2 张"；SB 的备用分量（`Material.a`、`Reflection.b/a`）是否也开放给这一层、开多少？
5. **它是否也做导出**：还是导出留给 AOV 层，它只负责合成？
6. **消费者怎么读**：读合成结果（推荐）；**是否允许仍直读某个 buffer 的原始通道**——允许的话容易长回"各读各的"，建议禁止。

## 4. 与既定内容的关系

- **遮罩只有一个来源**（ObjectBuffer 规划决策 2）：ScreenProcess 只吃本层的具名遮罩；
- **屏幕效果统一走本层**：角色特化、SSS、PLR 的遮罩需求都经这里；
- **导出档位**：Deep IDs 风格 UINT / 合规 `crypto_*`（float 位重解释 + manifest + 32 bit）——只有合规那档才用 Cryptomatte 的名字（延续 ObjectBuffer 规划决策 5）；
- **可复用的既有结论**：`Ho-CharacterBuffer_规划.md` §4.1/§4.2（Cryptomatte 布局、层数默认值、manifest 应嵌 EXR metadata）、§5.5（K = N 无损）。
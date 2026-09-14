# Ho-AttributeComposite（AC）规划：纯值 → object → surface 的递进覆盖与运行时合成层

> 状态：**概念草案**（待细化）。与 `Ho-ObjectBuffer_规划.md` §1.6 配套。
> **命名**：运行时合成器叫 **`Ho-AttributeComposite`（AC）**。**"Cryptomatte" 这个名字在这三个 feature 里一律不用**——它不是真正的 Cryptomatte（整数 ID、无 float 位重解释）。真正的 Cryptomatte 要么**以后单独规划一个 feature**，要么**由 AC 自己直出**（后者更好：**导出后外部处理与 Unity 内处理吃同一份数据**）。V2 §0 第 3 条把运行时名冻结为 AC，本文此前用的 "Ho-Cryptomatte" 作废。
> **输入只有两轴：OB + SB**（不是三轴——**GB 不喂 AC**，几何门控由消费端自己读 GB）。**OB 与 SB 各自带自己的 selection**，都进 AC 叠。
> **输出**：**下游真正消费的那一份 ID / 覆盖率 / 属性图**（叠完的结果，而不是各 buffer 的原始图）。
> 分工：**各 buffer 只提供可写通道 + 自己的 selection，本 feature 决定"多来源怎么合成、谁覆盖谁"**。

## 0. 一句话

**它不是一个 buffer，而是一层"属性解析器"**：读三轴的原始数据（含各 buffer 预留的可写通道），按 **纯值 → object → surface** 的递进顺序合成出每像素最终可用的属性，再据此产出遮罩、选区与导出。

    GB（几何轴）             ─┐   ┌─ 纯值：全屏 fallback（只在代码里表示）
    ObjectBuffer（逐物体轴） ─┼─→ │  object：挂在物体 ID 上的属性（palette 表）
    SurfaceBuffer（表面轴）  ─┘   └─ surface：逐像素的表面数值（SB）
                                      ↓ 递进覆盖：纯值 < object < surface
                                  AC 合成 → **下游真正吃的 ID / 覆盖率 / 属性图** → 消费者（ScreenProcess / 角色特化 / SSS / PLR …）
                                  （可选）AC 自己直出 → 与 Unity 内处理同一份数据

> ⚠ 上图里 GB 只画出来表示"它也在 L1"，**它不是 AC 的输入**：AC 只吃 OB + SB。

## 1. 为什么要单独一层

1. **解耦**：buffer 不需要知道属性语义，也不需要知道"冲突时谁赢"；加一个新属性只占一个可写槽位，**不动 buffer 的契约**。
2. **收敛**：今天每个消费端各自啃五张语义图、二十个 source（`ScreenProcessRuleSource` 就是活证据）；这一层之后，消费者只问它一次。
3. **名字各归其位**：`ObjectBuffer` 说轴（逐物体），`Cryptomatte` 说"选区 + 合成 + 导出"，`SurfaceBuffer` 说表面数值——互不冒充。

**帧序位置（已定）**：本层在 **opaque 之后**（`LILTOON_FORMAL_PIPELINE_DRAFT_V2.md` §2）——它合成的是"**最终画面上**每像素是谁、表面是什么样"，早于 opaque 就没有最终归属可言。它读 [OB / SB / GB] 三轴的产物，产出供 SSS / OIT / PLR / 角色特化 / ScreenProcess 使用。

> **推论（很重要）**：**GTAO 在 opaque 之前，因此吃不到本层的遮罩**——AO 的"谁参与"只能靠 layer mask / 材质意图。这条已写进 v2 §2 的偏序表，免得以后有人给 GTAO 接上 CM。

## 2. 递进覆盖的语义（待定，建议）

| 层 | 来源 | 例子 | 性质 |
| --- | --- | --- | --- |
| surface | SB 的逐像素数值 | thickness / roughness / 表面色 | 物理、逐像素、最泛 |
| object | ObjectBuffer 的 palette 表 | 类别、标签、逐物体属性（朝向） | 语义、逐物体 |
| 纯值 | 各 buffer 可写通道里被直接写入的字面量 | 材质画的遮罩、工具算的数 | 人为指定、最具体 |

**已定优先级**：`纯值`（底） **<** `object` **<** `surface`（顶）。

- **surface 最高**：物理真值最终说了算，逐像素的表面数值盖住上面两层；
- **object 居中**：挂在物体 ID 上的属性（类别 / 标签 / 朝向）盖住纯值；
- **纯值 = 全屏 fallback 值**，**可以只在代码里表示**、不必占通道：它是"没人写的时候默认是什么"（例如某个属性的缺省常量）。真正逐像素写进来的东西由 surface / object 两层表达。

> 三层不是"三种纹理"，而是**三种来源 + 一条覆盖链**；纯值这层连显存都不需要。

## 3. 待定清单

1. **"纯值"的载体**：既然只在代码里表示，那它是"本 feature 的常量表"还是"消费端各自的默认值"？（建议前者：默认值集中一处才不会被各消费端解释成不同意思。）
2. **按属性合成还是按遮罩合成**：每个属性各一条覆盖链，还是先合成"谁的属性生效"再统一取值？（后者更省，但要求三层同一套 ID 域。）
3. **可写通道预算**：ObjectBuffer 已定"逐物体辅助量 ≤ 2 张"；SB 的备用分量（`Material.a`、`Reflection.b/a`）是否也开放给这一层、开多少？
4. **它是否也做导出**：还是导出留给 AOV 层，它只负责合成？
5. **消费者怎么读**：读合成结果（推荐）；**是否允许仍直读某个 buffer 的原始通道**——允许的话容易长回"各读各的"，建议禁止。

## 4. 与既定内容的关系

- **遮罩只有一个来源**（ObjectBuffer 规划决策 2）：ScreenProcess 只吃本层的具名遮罩；
- **屏幕效果统一走本层**：角色特化、SSS、PLR 的遮罩需求都经这里；
- **导出档位**：Deep IDs 风格 UINT / 合规 `crypto_*`（float 位重解释 + manifest + 32 bit）——只有合规那档才用 Cryptomatte 的名字（延续 ObjectBuffer 规划决策 5）；
- **可复用的既有结论**：`Ho-CharacterBuffer_规划.md` §4.1/§4.2（Cryptomatte 布局、层数默认值、manifest 应嵌 EXR metadata）、§5.5（K = N 无损）。
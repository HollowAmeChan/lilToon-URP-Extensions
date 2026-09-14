# Ho-Cryptomatte 占位（专门做遮罩的 feature）

> 状态：**占位·未开工**。决策来源：CB 规划**决策 21**——Cryptomatte 选择层从 `Ho-CharacterBuffer` 与 `Ho-SurfaceBuffer` **整体移出**，由本 feature 专门承担。
> 本文只记"它要解决什么、边界在哪、输入从哪来、有哪些待定"，避免这个需求再次被塞进别的 buffer。

---

## 1. 为什么单独做一个 feature

| 问题 | 谁回答 | 备注 |
| --- | --- | --- |
| 几何在哪、朝向如何、几何覆盖多少 | GB | 几何法线 + depth |
| **这是谁、占多少** | **CB** | 4 层 `(ID, 覆盖率)` + palette（**K = N = 4，无损**） |
| 表面是什么样 | SB | 表面色 / 着色法线 / roughness / metallic / thickness … |
| **我要把哪一块单独抠出来** | **`Ho-Cryptomatte`（本 feature）** | 具名遮罩 / 可点选的导出 |

**三个理由**（决策 21 的展开）：

1. **CB / SB 各自只剩一句话**。选择层是第三种问题，横跨多个物体、有自己的名字表与层数预算，塞进谁都会把那个 feature 变成杂物间。
2. **`custom0~3` 那种匿名通道没有复活的口子**——它的替代品有了明确归属，不必在 CB / SB 里先占位。
3. **遮罩可以独立演进**：层数（2 / 4 / 8）、导出档位、Nuke 兼容、溢出可见性——改它不牵动 CB / SB 的契约。

---

## 2. ⭐ 它同时要简化 ScreenProcess（用户明确要求）

**约束：ScreenProcess 只允许吃 `Ho-Cryptomatte`**，不再直接读那一堆通道。

今天的 `ScreenProcessRuleSource` 有 **20 个 source**（`Runtime/ScreenProcess/ScreenProcessLayer.cs`）：

```text
遮罩 / 角色组 ID / 部件 ID / 标记 / 厚度 / 曲率 / 材质分类 / 透射提示      ← maskId 四通道 + surfaceData 四通道
材质自定义通道 0~3                                                      ← custom0 四通道
CharacterFull / 脸 / 前发 / 眼睛 / 眼透区域 / 配件 / CharacterBody / 预留7  ← objectCustom 八位
```

**迁移后只剩一族**：

```text
Cryptomatte 选择（按名字） + 覆盖率
```

**这意味着**：

- ScreenProcess **不再做"通道 + 阈值"匹配**（`Custom2 > 0.5` 这种没信息量的条件消失），**只做"具名遮罩"匹配**；
- ScreenProcess 不再需要 `RequiresSurfaceData` / `RequiresCustom0` / `RequiresObjectCustom0` 那套可用性诊断（`ScreenProcessRuntimeDiagnostics`）——依赖面从"5 张图 20 个 source"降到"1 套遮罩"；
- 材质侧**不再为 ScreenProcess 暴露那些通道**（这是管线草案 §6.2/§6.3 收敛暴露面的一部分）；
- "厚度 > 0.5 的发丝"这类规则改写成**一个具名选择**（由材质画遮罩、在这里命名）——表达力没降，条件可读了。

> 这也是这次拆分最直接的收益：**一个 feature 的依赖面从 20 个 source 降到 1 套具名遮罩。**

---

## 3. 输入从哪来（待定，三条路）

它要出遮罩，就需要"每像素多个物体身份 + 各自覆盖率"。现成的东西：

| 来源 | 有什么 | 缺口 |
| --- | --- | --- |
| **CB**（已有，P1 落地中） | 角色的 4 层 `(ID, 覆盖率)`，K = N = 4 无损 + palette（角色→部件名） | **只有角色**，没有场景物体 |
| ~~SB~~ | ~~场景侧身份~~ **已删除**（决策 21） | — |
| 自建 | 自己想画谁就画谁 | 角色会被再画一遍（多一趟 draw） |

**三条路**：

1. **完全自建 ID pass**：一次画完场景 + 角色，自己一套 ID 空间。**最独立**，代价是角色被画第二遍（CB 已经画过一次）。
2. **角色复用 CB + 自建场景那半**（我倾向）：角色身份直接读 `_Id0/_Id1/_Coverage`（不重画），本 feature 只画**非角色**物体；合成遮罩时把两边的结果并起来。麻烦点是"两套 ID 空间怎么合并"（名字表各自持有，遮罩求值时分别查）。
3. **恢复 SB 的场景身份**（即回到决策 10-13 的旧方案）：场景 ID 由 SB 出，本 feature 只负责"具名 + 层数 + 导出"。**最少重复劳动**，但把"身份"又放回 SB 里了——与"SB 只回答表面是什么样"冲突。

**建议路径 2**，并把"合并两套 ID 空间"当成设计要点写进正式规划。

---

## 4. 已有的结论可以直接搬（来自 CB 规划 §5.11 的作废原文）

这些结论经过核对，**对本 feature 仍然有效**：

- **布局**：一张 RGBA8 装两个 `(ID, 覆盖率)` 对（`R=id0, G=cov0, B=id1, A=cov1`）；**层数必须成偶数**（ID 与覆盖率必须成对）——这是 Cryptomatte 的成对规矩，业界默认 6~8 层（Octane 6 / Redshift 8），而我们的逐样本归属一像素最多 N 个，所以 **K = N** 就够。
- **命名纪律**：**UI 可以叫 Cryptomatte，但内部编码不合规时不许冒用这个名字**；只有导出**真正合规**的层（float 位重解释 ID + manifest + 32 bit）时才用 `crypto_*` 命名。
- **导出两档**：① 符合 Deep IDs 规范的 UINT 通道（`objectid` 等）；② 合规的 `crypto_*` 层。
- **不动数据**：ID 点采样、`round(v*255)` 还原、不滤波、不平均；覆盖率是线性量、只能按 ID 匹配加权（CB 规划 §5.5 / §6）。
- **溢出要可见**：层数不够时丢 ID 会表现为噪声（ASWF Deep IDs 规范原话），必须有 debug 提示。
- **manifest 默认嵌 EXR metadata**（OpenEXR 库不支持 sidecar；Blender 的节点干脆不读 sidecar）。
- **参考**：CB 规划 §4.1/§4.2 那批链接（Psyop 参考实现、MoonRay 排序规则、Deep IDs 规范、Encryptomatte、Nuke 节点、Octane/Redshift 的层数默认值）。

---

## 5. 待定

1. **输入路径**：§3 的三选一（建议 2）。
2. **层数与容量**：每像素 2 / 4 / 8 层？名字表容量多少？
3. **组件形态**：自己一个组件（`HoCryptomatteGroup`？）还是挂在 CB / SB 的组件上做"具名层"？（倾向自己一个，与"独立演进"一致。）
4. **ScreenProcess 的迁移时机**：它是本 feature 的第一个消费者，**在它落地前 ScreenProcess 仍吃 MetadataBuffer 的老 source**（不能断供）。
5. **Nuke 兼容的验收**：是否要求"导出后 Nuke 原生 Cryptomatte 节点能直接点选"（那就是合规档位 ②，工作量更大）。

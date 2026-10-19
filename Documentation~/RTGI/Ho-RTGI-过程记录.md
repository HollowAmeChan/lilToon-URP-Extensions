# Ho-RTGI 持续推进过程记录

开始日期：2026-10-19。当前目标与用户约束见[规划](../计划/Ho-RTGI-v0.1-实施规划.md)，最新操作状态见[交接](Ho-RTGI-交接.md)。每一步都要有事前评审、事后 review 与精确路径提交；本文“下一步”只表示写作时判断。

## 000：目标约束、参考调查与交接入口

状态：接纳文档与工作流程；无 Runtime 改动，不计画质收益。

**事前评审：** 用户先要求规划 RTGI，随后要求多看本机参考工程及 DI 过程文档，开启目标持续推进，并明确每步 review/过滤提交、无收益路线搁置/禁试记录和多次压缩交接。需要先固定这些约束，避免后续只继承功能清单。

**调查：** 阅读 DI 可行性调查 §15～17、第108步参考收敛审计、NRD、motion、距离、参数快照、环境和相关失败文档；对照本机参考 GI 五阶段、Unity RR、Native RELAX 的实际 host / shader 路径。参考副本 `.codex-research/UnityPathTracing-6000.3.15`，native 源码 `.codex-research/UnityPathTracing/RenderingPlugin`。

**判断：** 先实跑参考 GI，再按同场景差距移植。DI 的 raw/重建分离方法可复用；DI 已通过的参数、距离和派生滤波不能直接推广至 GI。详见规划文末的具体映射表。

**产出：** 规划、RTGI 导航、过程记录、踩坑/禁试和交接文档。活动 goal 的 objective 已引用规划；工具不支持修改活动 objective，因此将新要求写入该引用文档，不虚假完成再重建目标。

**验证：** 检查规划的本地 Markdown 引用全部存在；`git diff --check` 无 whitespace 错误（并行任务文件可能输出 CRLF 提示，非本步改动）。提交前再次只对本步文档核对路径。文档修改不执行 Unity 回归。

**事后 review：** 内容覆盖用户的逐步提交、正确性/收益复审、无收益搁置、禁试证据与压缩交接要求；没有扩大 PT/SSS/DLSS 产品范围，没有操作用户场景。收益是后续任务可追溯与可接手；GI 质量/功能仍未交付。接纳。

**过滤提交：** 仅 `Documentation~/计划/Ho-RTGI-v0.1-实施规划.md` 和 `Documentation~/RTGI/` 下本步四个文档。新参考探针尚在 M0 验证，不混入本步；RTDI 并行修改不纳入。

## 001：参考 GI 真实运行与基线隔离（进行中）

**事前评审：** 既有 DI 探针显式关闭 GI；需要新入口实际启用 GI 并读实际输出。先建立单解析灯、白色接收面与离屏红墙，禁用环境/jitter/额外次级 DI 复用，减小混杂。不得以合并 RR 画面直接代表 GI raw。

**当前实现：** 新参考探针 `Tests~/RTGI/Reference/HoReferenceGIProbe.cs`，复制至隔离参考的 `Assets/Editor/`。不替换采样 shader。先渲染 8 次无 GI 的单灯基线，再启用 GI 256 次，读取真实 raw 和可用重建输出。此协议含同步 readback，仅用于诊断，非 GPU timing 或正常应用帧运动验收。

**初步证据：** `HoReferenceGIProbe.RunUnityRaw` 实际进程 PID 18940 已退出 0；报告 `pass=true`，D3D12 / RTX4060 / Linear / 264 请求。ROI raw 增量 RGB 约 `(0.072124, 0.002533, 0.001618)`，direct-only 两次基线差为 0。证据仍在隔离工程 `ho-reference-gi-v01-unity-raw/` 与 `.codex-research/rtgi-v01-reference-unity-raw.log`，未作完整接纳或复制正式快照。

**未完成 review 项：** 核查探针统计、GI 分量隔离和接收材质是否与 Ho 对齐；运行无复用与重建路径；检查参考 shader 诊断及退出时资源提示。不能把第一次诊断通过称为 M0 已完成、无偏或高质量收敛。正式提交需补 review 结论和精确证据。

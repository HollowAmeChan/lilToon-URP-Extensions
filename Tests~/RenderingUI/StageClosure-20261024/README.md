# U01：GI/DI 阶段冻结、文档入口与 GI 面板整理

范围和前后 review 见[阶段收尾](../../../Documentation~/架构/Ho-RT-GIDI-阶段收尾与UI整理.md)。这是文档与两个 GI Inspector 的展示修改。

`review.json` 记录相对 A02 Release 的712源逐文件 SHA 核对：仅两个已审查 Inspector 不同，修改前字节匹配历史记录；Runtime 无 diff。逐个核对 Feature 设置/Shader 引用与 Volume fetch 的前后字段集合及次数，无遗漏或重复。`compile.log.gz` 无损保留最终 Runtime/Editor 分程序集 Roslyn 编译的原始输出（SHA256 按解压原字节核对），0 errors；既有 warnings 保留。

实际 Unity Inspector 观察覆盖 Feature 基础/采样内容及四组标题、Volume 运行/合成内容及三组标题，仅操作折叠，未改用户参数或保存资产。记录不冒称全平台 UI 或角色画质验收。未启动新 GPU/Player 进程。

旧 A02 verifier 修改前通过；修改后其整包 SHA guard 会因两份新 UI 源拒绝。A01 verifier 在本轮开始前就因 A02 已接纳的 `HoRTGINrdContext.cs` 修改拒绝。旧 run/source-index/verifier 不改写；本记录只证明展示修改的范围、编译和现场面板复核，沿用的 GPU 证据按原范围解释。

原047过程预审与PlanarReference保持未提交。本步不恢复confidence、局部公式或半GI路线，不更改生产历史/默认/平台与支持域。

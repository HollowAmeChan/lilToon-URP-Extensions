# A02：GI NRD输入／发布纹理顺序复用

> 2026-10-24 后续文档/UI整理只改变两个 GI Inspector，旧 Player 源码 SHA guard 会据此拒绝当前整包；不要改旧 SHA 来消除拒绝。渲染核心逐文件一致与新 Editor 编译/界面复核见 [阶段收尾](../../Documentation~/架构/Ho-RT-GIDI-阶段收尾与UI整理.md)。本页以下保留 A02 原协议与证据。

2026-10-24。已验证并接纳：FinalNRD完整写RGBA16F输入→所有NRD dispatch消费→Resolve从独立RGBA32F输出恢复物理单位并覆盖原输入→发布；下帧重新全域写入。删除单独filtered纹理，raw、32F output、normal/viewZ、NRD池/独立历史/reset/精度和Shader均不改。

## 实际证据

| 运行 | PID | 实际结果 |
| --- | --- | --- |
| 初RG配对 | 44828 | actual0，无Shader/AS/fence诊断，旧新逐位同 |
| 正式RG配对 | 46604 | 74请求，41配对帧，1318912分量逐位同 |
| 正式compat配对 | 12752 | 同上；完整reuse-audit.json与RG一致 |
| GI原有NRD验证RG/compat | 49744 / 47024 | 各243请求、41check通过 |
| Release构建 | 46324 | actual0/PASS；3条既有Shader warning逐行历史审查 |
| 隐藏Release Mono Player | 36292 | D3D12/Linear，304 distinct-Update渲染请求、16check通过，运行诊断0 |

旧wrapper原字节冻结，使用相同实际NRD输入、common和settings运行独立旧NRD实例；在生产Resolve后，以同一Resolve kernel/当前primary绑定将旧独立32F output写入旧filtered16。比较两套NRD32F输出和最终16F发布的全部RGBA。旧、新输入/normal/viewZ复制在NRD前完成，无新随机采样。旧分支有自己的pool/history，不改生产输出/历史。

正式每后端41配对帧，40帧64²加1帧32²，共1318912个output+publication分量逐位同。8帧开启显式输入快照，全RGBA与旧未覆写的输入逐位同；关闭请求后快照确实释放。曲面normal见证、零albedo仍保留入射红色、最大物理输入1015、最小正signal=5.960464477539063e−8均有记录。两Feature独立开关、固定初始4实例的alpha变化/world reset、resize和释放通过，不做AS实例增长实验。

## 资源账本

| GI外部NRD目标 | 旧分配 | 新默认分配 | 节省 |
| --- | --- | --- | --- |
| 64²实测纹理payload | 180224 B | 147456 B | 32768 B |
| 32²实测纹理payload | 45056 B | 36864 B | 8192 B |
| 1080p布局算式 | 44 B/像素 | 36 B/像素 | 16588800 B≈15.82MiB/相机 |

按实际RT实例ID/格式/尺寸去重核算；snapshot=false时四个实际目标，旧为五个。诊断snapshot=true会额外分配8B/像素，节省暂为0，不冒充默认产品预算。以上不含NRD池/常量/raw/AS/驱动，不称1080实跑/整帧显存或耗时优化。

## 输入读取契约

signal只在FinalNRD→NRD消费期间代表physical/64 radiance与hit distance；Resolve之后它是最终physical RGB/receiver-validity。Editor要后读旧输入，须在render前设ValidationCaptureSignal=true，读ValidationSignal。快照按需分配并在NRD前复制，false时下一Prepare释放，Player不编译这些诊断字段。6处现有输入审计已迁移；直接上传synthetic输入和只检查signal尺寸仍可使用signal。

NRD原生validation输出目前未启用（common.validation=0）。未来若启用，需重新审查OUT_VALIDATION与IN_DIFF别名，不应直接沿用当前映射。公开GI debug模式使用自己的debug shader，不等于该原生validation路径。

## 复现与范围

```powershell
python Tests~/RenderingReuse/verify_validation.py
```

重跑先generate_baseline.py，再run_validation.ps1 -Mode reuse/reuseCompat/gi/giCompat；仅私有Unity工程，hidden窗口和D3D12 debug，独占工程。Release用playerBuild，再run_player.ps1；旧build warning只接受既有Backbuffer规则的精确行，不改原run diagnostics3。source-index复用A01原对象，其它新源以无损gzip保存；runs索引保存完整日志/报告/图与SHA，历史原源不改。

首次Python复核把GI报告的failure字段误写成error而KeyError，已修辅助schema读取；没有改GPU数据/候选/门槛或为此重跑GPU。产品与GPU实验本轮无失败。

本阶段到此收尾：已拿到无数值退化且可量化的低成本收益。跨Feature primary历史需帧末提交、共享pose需版本/引用计数/GPU生命周期，重改太大；精确匹配scratch收益仅约2MiB/1080布局，优先级低；不合并NRD历史、不降GI32F、不恢复原047积分或局部公式研究。

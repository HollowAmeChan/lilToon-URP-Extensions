# 035 当前stock/no-op重现性通过，历史异常保留

暂停算法参数/算式实验，只记录输入与重现默认5。新快照可选启用，首尾包含真实shader/app帧、72灯Vector4（18灯）、sky数值、16384 RIS记录、完整secondary/primary哈希及27个逐字段记录/probe；不把端点当每帧全部输入证明。

初28112资格和31692 stock初探actual0、diagnostics0，但Unity JSON没有输出未标Serializable的灯/primary复合结构，原哈希/其它字段仍在。此版本数值记录资格未过，移入initial-numeric-omission，不能补写丢失值。初stock的raw也与旧基线不同，说明历史差异不只出现在候选路径，但没有证明原因。

改为Vector4[]/uint[]后，53896资格实际1073741845、无fresh终态报告，日志有DEVICE_HUNG/removal与Unmap错误；发生在首个快照前，不能归因JSON。外部Python校验exit1未令PowerShell自动停止，误启动7312 stock；验证本任务PID/Unity路径/启动时间后仅停止该子进程（actual−1），partial文件保留。abort-review记明编排错误。后续加入终止错误、LASTEXITCODE显式检查与数值字段资格门槛，不在失败后自动排队。

没有改TDR、重启driver或结束其它应用。显存快照7302/8188 MiB、util50%只作观测，不证明TDR原因。6968空场景/no-AS小已知compute/fence恢复资格actual0、24样本通过、debug0；empty/128/2048 workload中位0/.006144/.039936ms。20912空场景真实JsonUtility往返资格actual0，72Vector4/135uint/16384RIS无丢失。恢复后显存快照降低，再以2分钟上限重试短场景，44040资格actual0/98请求、numeric fields/hash通过。小负载恢复不等于大场景保证。

正式当前原版A53112、原版B50368、no-op clone50088全部actual0，各1314请求，API/Shader/Property/UAV/fence/drift0、数值字段资格过。4个seed（0/104729/5347609/6396185）、64warm/256观察，原27点/场景支持域/5轮不变。

三组全raw+filtered时间均值图对旧026同seed逐bit一致；A/B/no-op之间完整secondary/primary、灯、RIS端点哈希一致，选中逐字段/sky也一致。no-op clone是原body只改include定位、无debug-UAV/guard，shader/kernel恢复通过。它支持当前条件下严格重现，不反向给034的原配对失败发资格。

初stock与当前A的端点灯/sky/RIS/primary同，full secondary hash不同；27选点差仅position/normal，hit最大9.26e−6、miss单位方向7.82e−8、100000合成radius最大.01324、normal最多1个oct16步，均在019既有输入精度界内。相关性不证明这些差异导致raw漂移或TDR，更不证明具体engine/driver根因；仍保留historical cause unproven。

255精确source对象、8个场景进程完整logs、2个恢复/序列化log、77原JSON/bin lossless gzip、abort/source/support输入/analysis/verify。成功与缺字段、crash、显式abort均分开验证。没有画质修复或新产品性能预算；Runtime/DI/vendor无改动。

下一036使用新鲜stock与完整输入检查作为硬前置；可先建立同一GI输入的两个独立NRD baseline/no-op分支，验证输出/历史/重置等价，后续只改一个分支，减少跨进程raytrace末位差混入滤波比较。保持实际GPU资格/host约束，历史F51/F53/F55候选不自动重开，不重复MC/旧AS参数矩阵。所有原质量/static/F39/复杂动态/1080预算和目标active继续。
